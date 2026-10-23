using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Events;
using Volt.Gameplay.Tags;

namespace Volt.Gameplay.Tasks;

/// <summary>
/// Owns the pending tasks of every ability activation in one world.
/// </summary>
/// <remarks>
/// <para>
/// Task state lives in one pooled record per pending task, linked into
/// its activation's task list and its owner's task list. There is no
/// managed object per task, no virtual task hierarchy, and no delegate
/// per task. Idle tasks are not visited at all: duration and
/// tag-condition tasks sit in one binary min-heap ordered by deadline,
/// and event tasks sit in the event system's tag index. A simulation
/// step with nothing due costs one heap comparison.
/// </para>
/// <para>
/// A task is owned by an activation token, not by a grant. The token
/// carries the grant handle and the activation serial number, so a task
/// started for an earlier activation can never complete, cancel, or
/// continue a later activation of the same grant.
/// </para>
/// <para>
/// Ending, cancelling, or revoking an ability detaches all of its tasks
/// before any task notification is raised. A handler therefore never
/// observes a half-cancelled activation, and a stale event or deadline
/// cannot wake a task of an activation that already ended. Destroying
/// an entity releases its tasks silently, exactly as the other gameplay
/// stores release their state.
/// </para>
/// <para>
/// Time comes from <see cref="Advance"/>; the caller supplies the
/// scaled fixed step. Pausing with a zero step does no work, and time
/// scaling is whatever step the game loop supplies.
/// </para>
/// <para>
/// The system is single-threaded, like the rest of the gameplay
/// runtime. Notifications are post-commit and an exception from a
/// handler propagates after the task state has been released.
/// </para>
/// </remarks>
public sealed class GameplayAbilityTaskSystem : IDisposable
{
	private const int SegmentShift = 10;
	private const int SegmentSize = 1 << SegmentShift;
	private const int SegmentMask = SegmentSize - 1;
	private const int InitialTaskCapacity = 32;
	private const int InitialDeadlineCapacity = 32;

	private static int s_nextSystemId;

	private readonly GameplayAbilitySystem _abilities;
	private readonly GameplayEventSystem _events;
	private readonly GameplayTagStore _tags;
	private readonly World _world;
	private readonly uint _allocatorId;
	private readonly uint _systemId;
	private readonly EventSink _eventSink;

	private TaskSlot[] _tasks = new TaskSlot[InitialTaskCapacity];
	private int _taskSlotCount;
	private int _freeTaskSlot = -1;
	private int _pendingCount;
	private uint _nextSequence;
	private double _time;
	private bool _advancing;
	private bool _simulationDriven;

	private DeadlineEntry[] _deadlines =
		new DeadlineEntry[InitialDeadlineCapacity];
	private int _deadlineCount;

	private EntityTasks[]?[] _owners = [];

	private readonly BufferPool<GameplayAbilityTaskChange> _cancelChanges =
		new();
	private readonly BufferPool<GameplayAbilityTaskHandle> _cancelHandles =
		new();

	private bool _disposed;

	/// <summary>
	/// Creates the task runtime for an ability system. This is created
	/// lazily by <see cref="GameplayAbilitySystem.Tasks"/>; application
	/// code does not construct it directly.
	/// </summary>
	internal GameplayAbilityTaskSystem(
		GameplayAbilitySystem abilities,
		GameplayEventSystem events,
		GameplayTagStore tags)
	{
		_abilities = abilities;
		_events = events;
		_tags = tags;
		_world = abilities.World;
		_allocatorId = _world.AllocatorId;
		_systemId = AllocateSystemId();
		_eventSink = new EventSink(this);

		_world.EntityDestroyed += HandleEntityDestroyed;
		_tags.TagChanged += HandleTagChanged;
	}

	/// <summary>
	/// Occurs after a task starts, completes, or is cancelled.
	/// </summary>
	/// <remarks>
	/// A terminal notification is raised after the task has been
	/// unlinked, unsubscribed, and removed from the deadline heap, so
	/// its handle no longer resolves. With no subscribers the cost is
	/// one null check per transition.
	/// </remarks>
	public event Action<GameplayAbilityTaskChange>? Changed;

	/// <summary>
	/// Gets the accumulated simulation time of the task runtime.
	/// </summary>
	public double Time => _time;

	/// <summary>
	/// Gets the number of pending tasks.
	/// </summary>
	public int PendingTaskCount => _pendingCount;

	/// <summary>
	/// Starts a task that completes after a simulation duration.
	/// </summary>
	/// <param name="activation">The owning activation.</param>
	/// <param name="durationSeconds">
	/// The duration in simulation seconds. Zero is allowed and completes
	/// on the next positive <see cref="Advance"/>.
	/// </param>
	/// <returns>
	/// The task handle, or <see cref="GameplayAbilityTaskHandle.None"/>
	/// when the activation is no longer the active activation of its
	/// grant.
	/// </returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The duration is negative or not finite.
	/// </exception>
	/// <remarks>
	/// Task creation never completes a task synchronously. A zero
	/// duration task is scheduled at the current simulation time and
	/// completes during the next advance, which keeps task creation free
	/// of user callbacks.
	/// </remarks>
	public GameplayAbilityTaskHandle WaitDuration(
		GameplayAbilityActivation activation,
		float durationSeconds)
	{
		ValidateDuration(durationSeconds);

		return StartDurationTask(activation, durationSeconds, -1);
	}

	/// <summary>
	/// Starts a task that completes when a matching gameplay event is
	/// delivered.
	/// </summary>
	/// <param name="activation">The owning activation.</param>
	/// <param name="eventTag">
	/// The event tag, or <see cref="GameplayTag.None"/> to wait for any
	/// event.
	/// </param>
	/// <param name="match">
	/// Whether the tag matches exactly or hierarchically.
	/// </param>
	/// <param name="source">
	/// An optional source entity filter, or <c>default(Entity)</c> for
	/// any source.
	/// </param>
	/// <param name="target">
	/// An optional target entity filter, or <c>default(Entity)</c> for
	/// any target.
	/// </param>
	/// <returns>
	/// The task handle, or <see cref="GameplayAbilityTaskHandle.None"/>
	/// when the activation is no longer active.
	/// </returns>
	/// <remarks>
	/// The subscription is one-shot: the task unsubscribes when the
	/// first matching event arrives. Several tasks may wait for the same
	/// event; each receives it once, in subscription order.
	/// </remarks>
	public GameplayAbilityTaskHandle WaitEvent(
		GameplayAbilityActivation activation,
		GameplayTag eventTag,
		GameplayEventMatch match = GameplayEventMatch.Exact,
		Entity source = default,
		Entity target = default)
	{
		ValidateEventTag(eventTag);

		return StartGameplayEventTask(
			activation,
			eventTag,
			match,
			source,
			target,
			-1);
	}

	/// <summary>
	/// Starts a task that completes when a tag becomes present or
	/// absent.
	/// </summary>
	/// <param name="activation">The owning activation.</param>
	/// <param name="tag">The tag to watch.</param>
	/// <param name="mode">
	/// Whether to wait for presence or absence.
	/// </param>
	/// <param name="exact">
	/// <see langword="true"/> to require an explicit grant of the tag;
	/// <see langword="false"/> to accept a granted descendant.
	/// </param>
	/// <returns>
	/// The task handle, or <see cref="GameplayAbilityTaskHandle.None"/>
	/// when the activation is no longer active.
	/// </returns>
	/// <remarks>
	/// The condition is evaluated when the task starts and again on
	/// every tag change for the owner. It is not polled. Completion
	/// happens on the next advance after the condition was observed
	/// true, so a condition that flips back before that advance keeps
	/// the task waiting.
	/// </remarks>
	public GameplayAbilityTaskHandle WaitTag(
		GameplayAbilityActivation activation,
		GameplayTag tag,
		GameplayTagWaitMode mode,
		bool exact = false)
	{
		ValidateTag(tag);
		ValidateTagWaitMode(mode);

		return StartTagConditionTask(activation, tag, mode, exact, -1);
	}

	/// <summary>
	/// Cancels one pending task.
	/// </summary>
	/// <param name="handle">The task handle.</param>
	/// <returns>
	/// <see langword="true"/> when this call cancelled the task;
	/// <see langword="false"/> when it had already completed, was
	/// cancelled, or belongs to another system.
	/// </returns>
	public bool Cancel(GameplayAbilityTaskHandle handle)
	{
		ThrowIfDisposed();

		if (!TryResolve(handle, out int slot))
		{
			return false;
		}

		GameplayAbilityTaskChange change = CreateChange(
			GameplayAbilityTaskChangeKind.Cancelled,
			slot,
			GameplayAbilityTaskCompletionReason.Cancelled,
			default);

		ReleaseTaskStorage(slot, unlinkOwner: true);

		RaiseChange(change);

		return true;
	}

	/// <summary>
	/// Completes one pending task immediately.
	/// </summary>
	/// <param name="handle">The task handle.</param>
	/// <returns>
	/// <see langword="true"/> when this call completed the task;
	/// <see langword="false"/> when it had already ended or belongs to
	/// another system.
	/// </returns>
	/// <remarks>
	/// An event-driven completion carries the event; a manual
	/// completion has no event. The notification reports
	/// <see cref="GameplayAbilityTaskCompletionReason.ConditionMet"/>.
	/// </remarks>
	public bool Complete(GameplayAbilityTaskHandle handle)
	{
		ThrowIfDisposed();

		if (!TryResolve(handle, out int slot))
		{
			return false;
		}

		return CompleteTask(
			slot,
			GameplayAbilityTaskCompletionReason.ConditionMet,
			default);
	}

	/// <summary>
	/// Cancels every pending task of an entity.
	/// </summary>
	/// <param name="owner">The entity.</param>
	/// <returns>The number of tasks this call cancelled.</returns>
	/// <remarks>
	/// The call captures the tasks that exist when it starts. A task
	/// started by a cancellation handler is not part of the call.
	/// </remarks>
	public int CancelAll(Entity owner)
	{
		ThrowIfDisposed();

		if (!TryGetOwnerSlot(owner, out int segmentIndex, out int offset))
		{
			return 0;
		}

		ref EntityTasks state = ref _owners[segmentIndex]![offset];

		if (state.Count == 0)
		{
			return 0;
		}

		GameplayAbilityTaskHandle[] captured =
			_cancelHandles.Rent(state.Count);

		try
		{
			int count = CaptureOwnerTasks(owner, captured);
			int cancelled = 0;

			for (int index = 0; index < count; index++)
			{
				if (Cancel(captured[index]))
				{
					cancelled++;
				}
			}

			return cancelled;
		}
		finally
		{
			_cancelHandles.Return();
		}
	}

	/// <summary>
	/// Copies a snapshot of one pending task.
	/// </summary>
	/// <param name="handle">The task handle.</param>
	/// <param name="task">The snapshot.</param>
	/// <returns>
	/// <see langword="true"/> when the handle refers to a pending task
	/// whose activation is still active.
	/// </returns>
	public bool TryGetTask(
		GameplayAbilityTaskHandle handle,
		out GameplayAbilityTaskInfo task)
	{
		ThrowIfDisposed();

		if (!TryResolve(handle, out int slot))
		{
			task = default;
			return false;
		}

		ref TaskSlot record = ref _tasks[slot];
		GameplayAbilityActivation activation = new(
			record.Ability,
			record.ActivationId);

		if (!_abilities.TryResolveActivation(
			activation,
			out _,
			out _,
			out _))
		{
			task = default;
			return false;
		}

		double remaining = double.IsPositiveInfinity(record.Deadline)
			? double.PositiveInfinity
			: record.Deadline - _time;

		task = new GameplayAbilityTaskInfo(
			CreateHandle(slot),
			activation,
			record.Kind,
			record.PlanStep,
			record.Deadline,
			remaining);

		return true;
	}

	/// <summary>
	/// Gets the number of pending tasks an entity owns.
	/// </summary>
	/// <param name="owner">The entity.</param>
	public int GetPendingTaskCount(Entity owner)
	{
		ThrowIfDisposed();

		return TryGetOwnerSlot(owner, out int segmentIndex, out int offset)
			? _owners[segmentIndex]![offset].Count
			: 0;
	}

	/// <summary>
	/// Copies the handles of an entity's pending tasks.
	/// </summary>
	/// <param name="owner">The entity.</param>
	/// <param name="destination">The destination span.</param>
	/// <returns>
	/// The number of handles written, which is less than
	/// <see cref="GetPendingTaskCount"/> when the span is too small.
	/// </returns>
	public int CopyTasks(
		Entity owner,
		Span<GameplayAbilityTaskHandle> destination)
	{
		ThrowIfDisposed();

		if (!TryGetOwnerSlot(owner, out int segmentIndex, out int offset))
		{
			return 0;
		}

		int written = 0;
		int slot = _owners[segmentIndex]![offset].Head;

		while (slot >= 0 && written < destination.Length)
		{
			destination[written++] = CreateHandle(slot);

			slot = _tasks[slot].NextOnOwner;
		}

		return written;
	}

	/// <summary>
	/// Advances simulation time and completes the tasks that are due.
	/// </summary>
	/// <param name="deltaSeconds">
	/// The scaled fixed step. Zero is a no-op, which is what a paused
	/// game supplies.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The step is negative or not finite.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The call re-enters itself from a task or event notification.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Tasks created during the advance are deferred to the next
	/// advance, even when their deadline has already passed. That keeps
	/// a completion handler from extending one step without bound, and
	/// it matches the one-frame latency of a zero-duration timer.
	/// </para>
	/// <para>
	/// A large step processes every deadline that was pending when the
	/// call started, in order. Completions run at the end-of-step
	/// clock; a task created by one of those completions is deferred
	/// to the next advance even when its duration is zero. Chained
	/// waits are therefore intentionally not independent of how a
	/// large step is subdivided: one <c>Advance(10)</c> completes one
	/// link of a callback chain, while ten <c>Advance(1)</c> calls
	/// give each link an advance to run in. This matches the fixed
	/// step loop and
	/// <see cref="Volt.Core.Timing.SimulationScheduler"/>, where work
	/// registered during a step is eligible on the following step.
	/// </para>
	/// </remarks>
	public void Advance(float deltaSeconds)
	{
		ThrowIfDisposed();

		if (_simulationDriven)
		{
			throw new InvalidOperationException(
				"This task system is advanced by a " +
				"GameplaySimulation attached to the fixed-step loop. " +
				"Calling Advance manually would advance the same clock " +
				"twice in one step. Detach the integration or use its " +
				"loop to advance simulation time.");
		}

		AdvanceCore(deltaSeconds);
	}

	/// <summary>
	/// Advances the clock from the attached simulation integration.
	/// </summary>
	internal void AdvanceFromSimulation(float deltaSeconds)
	{
		ThrowIfDisposed();

		AdvanceCore(deltaSeconds);
	}

	/// <summary>
	/// Claims the clock for one fixed-step integration.
	/// </summary>
	internal void BeginSimulation()
	{
		if (_simulationDriven)
		{
			throw new InvalidOperationException(
				"This task system is already advanced by another " +
				"GameplaySimulation. A clock has one owner.");
		}

		_simulationDriven = true;
	}

	/// <summary>
	/// Releases the clock when its integration is disposed.
	/// </summary>
	internal void EndSimulation()
	{
		_simulationDriven = false;
	}

	private void AdvanceCore(float deltaSeconds)
	{
		if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0f)
		{
			throw new ArgumentOutOfRangeException(
				nameof(deltaSeconds),
				deltaSeconds,
				"A simulated time step must be finite and not negative. " +
				"A negative step would rewind task deadlines, which is " +
				"not supported.");
		}

		if (deltaSeconds == 0f)
		{
			return;
		}

		if (_advancing)
		{
			throw new InvalidOperationException(
				"Advance cannot be called from a task or event " +
				"notification. Complete or cancel tasks from a handler " +
				"instead, and advance time from the simulation loop.");
		}

		_advancing = true;

		try
		{
			_time += deltaSeconds;

			double stepTime = _time;

			while (_deadlineCount > 0)
			{
				DeadlineEntry head = _deadlines[0];

				if (head.Deadline > stepTime)
				{
					break;
				}

				PopDeadline();

				if (!TryResolveTask(
					head.Slot,
					head.Generation,
					out int slot))
				{
					continue;
				}

				ref TaskSlot record = ref _tasks[slot];

				if (record.Kind is not (
					GameplayAbilityTaskKind.Duration
					or GameplayAbilityTaskKind.TagCondition))
				{
					continue;
				}

				if (record.Kind == GameplayAbilityTaskKind.TagCondition
					&& !ConditionHolds(ref record))
				{
					// The condition flipped back between the tag change
					// that armed the task and this step. It stays
					// pending; the next matching tag change re-arms it.
					continue;
				}

				CompleteTask(
					slot,
					record.Kind == GameplayAbilityTaskKind.Duration
						? GameplayAbilityTaskCompletionReason.Elapsed
						: GameplayAbilityTaskCompletionReason.ConditionMet,
					default);
			}
		}
		finally
		{
			_advancing = false;
		}
	}

	/// <summary>
	/// Starts a duration task for an ability plan step.
	/// </summary>
	internal GameplayAbilityTaskHandle StartDurationTask(
		GameplayAbilityActivation activation,
		float durationSeconds,
		int planStep)
	{
		ThrowIfDisposed();

		if (!TryBeginTask(
			activation,
			GameplayAbilityTaskKind.Duration,
			planStep,
			out int slot))
		{
			return GameplayAbilityTaskHandle.None;
		}

		ScheduleDeadline(slot, _time + durationSeconds);

		return FinishStart(slot);
	}

	/// <summary>
	/// Starts an event task for an ability plan step.
	/// </summary>
	internal GameplayAbilityTaskHandle StartGameplayEventTask(
		GameplayAbilityActivation activation,
		GameplayTag eventTag,
		GameplayEventMatch match,
		Entity source,
		Entity target,
		int planStep)
	{
		ThrowIfDisposed();

		if (!TryBeginTask(
			activation,
			GameplayAbilityTaskKind.GameplayEvent,
			planStep,
			out int slot))
		{
			return GameplayAbilityTaskHandle.None;
		}

		GameplayEventSubscription subscription;

		try
		{
			subscription = _events.SubscribeSink(
				eventTag,
				match,
				_tasks[slot].Owner,
				source,
				target,
				_eventSink,
				slot);
		}
		catch
		{
			ReleaseTaskStorage(slot, unlinkOwner: true);
			throw;
		}

		_tasks[slot].EventSubscription = subscription;
		_tasks[slot].Tag = eventTag;
		_tasks[slot].Match = match;

		return FinishStart(slot);
	}

	/// <summary>
	/// Starts a tag-condition task for an ability plan step.
	/// </summary>
	internal GameplayAbilityTaskHandle StartTagConditionTask(
		GameplayAbilityActivation activation,
		GameplayTag tag,
		GameplayTagWaitMode mode,
		bool exact,
		int planStep)
	{
		ThrowIfDisposed();

		if (!TryBeginTask(
			activation,
			GameplayAbilityTaskKind.TagCondition,
			planStep,
			out int slot))
		{
			return GameplayAbilityTaskHandle.None;
		}

		_tasks[slot].Tag = tag;
		_tasks[slot].TagMode = mode;
		_tasks[slot].Exact = exact;

		LinkTagOwner(_tasks[slot].Owner, slot);

		if (ConditionHolds(ref _tasks[slot]))
		{
			ScheduleDeadline(slot, _time);
		}

		return FinishStart(slot);
	}

	/// <summary>
	/// Gets whether the runtime has been disposed.
	/// </summary>
	internal bool IsDisposed => _disposed;

	/// <summary>
	/// Determines whether a handle still refers to a pending task.
	/// </summary>
	internal bool IsPending(GameplayAbilityTaskHandle handle)
	{
		return !_disposed && TryResolve(handle, out _);
	}

	/// <summary>
	/// Cancels every task of an activation and raises their
	/// notifications.
	/// </summary>
	internal void CancelActivation(GameplayAbilityActivation activation)
	{
		CancelActivationCore(activation, notify: true);
	}

	/// <summary>
	/// Cancels every task of an activation without notifications, for a
	/// failed activation commit that never became observable.
	/// </summary>
	internal void CancelActivationSilently(
		GameplayAbilityActivation activation)
	{
		CancelActivationCore(activation, notify: false);
	}

	/// <inheritdoc />
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_world.EntityDestroyed -= HandleEntityDestroyed;
		_tags.TagChanged -= HandleTagChanged;

		Changed = null;

		for (int slot = 0; slot < _taskSlotCount; slot++)
		{
			GameplayEventSubscription subscription =
				_tasks[slot].EventSubscription; if (!subscription.IsNone && !_events.IsDisposed)
			{
				_events.Unsubscribe(subscription);

			}
		}

		_tasks = [];
		_owners = [];
		_deadlines = [];
		_cancelChanges.Clear();
		_cancelHandles.Clear();
		_taskSlotCount = 0;
		_freeTaskSlot = -1;
		_pendingCount = 0;
		_deadlineCount = 0;
		_nextSequence = 0;
		_time = 0d;
		_advancing = false;
		_simulationDriven = false;
		_disposed = true;

		GC.SuppressFinalize(this);
	}

	private void CancelActivationCore(
		GameplayAbilityActivation activation,
		bool notify)
	{
		ThrowIfDisposed();

		if (activation.IsNone
			|| !_abilities.TryResolveGrant(
				activation.Ability,
				out int abilitySlot,
				out _)
			|| _abilities.GetActivationId(abilitySlot) != activation.Id)
		{
			return;
		}

		int head = _abilities.GetTaskHead(abilitySlot);

		if (head < 0)
		{
			return;
		}

		int count = 0;

		for (int slot = head; slot >= 0; slot = _tasks[slot].NextOnActivation)
		{
			count++;
		}

		GameplayAbilityTaskChange[] changes = notify
			? _cancelChanges.Rent(count)
			: [];

		try
		{
			int written = 0;
			int current = head;

			while (current >= 0)
			{
				int next = _tasks[current].NextOnActivation;

				if (notify)
				{
					changes[written++] = CreateChange(
						GameplayAbilityTaskChangeKind.Cancelled,
						current,
						GameplayAbilityTaskCompletionReason.AbilityEnded,
						default);
				}

				ReleaseTaskStorage(current, unlinkOwner: true);

				current = next;
			}

			_abilities.SetTaskHead(abilitySlot, -1);

			if (!notify)
			{
				return;
			}

			for (int index = 0; index < written; index++)
			{
				RaiseChange(changes[index]);
			}
		}
		finally
		{
			if (notify)
			{
				_cancelChanges.Return();
			}
		}
	}

	private bool TryBeginTask(
		GameplayAbilityActivation activation,
		GameplayAbilityTaskKind kind,
		int planStep,
		out int slot)
	{
		slot = -1;

		if (!_abilities.TryResolveActivation(
			activation,
			out int abilitySlot,
			out Entity owner,
			out _))
		{
			return false;
		}

		slot = AcquireTaskSlot();

		ref TaskSlot task = ref _tasks[slot];

		task.ActivationId = activation.Id;
		task.Ability = activation.Ability;
		task.Owner = owner;
		task.Kind = kind;
		task.PlanStep = planStep;
		task.Deadline = double.PositiveInfinity;
		task.HeapIndex = -1;
		task.EventSubscription = GameplayEventSubscription.None;
		task.Tag = GameplayTag.None;
		task.Exact = false;

		LinkOwner(owner, slot);
		LinkActivation(abilitySlot, slot);

		_pendingCount++;

		return true;
	}

	private GameplayAbilityTaskHandle FinishStart(int slot)
	{
		GameplayAbilityTaskHandle handle = CreateHandle(slot);

		RaiseChange(CreateChange(
			GameplayAbilityTaskChangeKind.Started,
			slot,
			GameplayAbilityTaskCompletionReason.ConditionMet,
			default));

		return handle;
	}

	private bool CompleteTask(
		int slot,
		GameplayAbilityTaskCompletionReason reason,
		in GameplayEvent gameplayEvent)
	{
		if ((uint)slot >= (uint)_taskSlotCount
			|| _tasks[slot].Generation == 0)
		{
			return false;
		}

		GameplayAbilityTaskChange change = CreateChange(
			GameplayAbilityTaskChangeKind.Completed,
			slot,
			reason,
			in gameplayEvent);

		ReleaseTaskStorage(slot, unlinkOwner: true);

		RaiseChange(change);

		return true;
	}

	private void HandleGameplayEvent(
		in GameplayEvent gameplayEvent,
		GameplayEventSubscription subscription,
		int token)
	{
		if ((uint)token >= (uint)_taskSlotCount)
		{
			return;
		}

		ref TaskSlot task = ref _tasks[token];

		if (task.Generation == 0
			|| task.Kind != GameplayAbilityTaskKind.GameplayEvent
			|| task.EventSubscription != subscription)
		{
			return;
		}

		GameplayAbilityActivation activation = new(
			task.Ability,
			task.ActivationId);

		if (!_abilities.TryResolveActivation(
			activation,
			out _,
			out _,
			out _))
		{
			// The activation ended without this task being released.
			// The event subscription is still live, so release the
			// task silently rather than completing work for an
			// activation that no longer exists.
			ReleaseTaskStorage(token, unlinkOwner: true);
			return;
		}

		CompleteTask(
			token,
			GameplayAbilityTaskCompletionReason.ConditionMet,
			in gameplayEvent);
	}

	private void HandleTagChanged(Entity entity, GameplayTag changedTag)
	{
		if (!TryGetOwnerSlot(entity, out int segmentIndex, out int offset))
		{
			return;
		}

		ref EntityTasks state = ref _owners[segmentIndex]![offset];

		// Only a task watching the changed tag itself, or an ancestor of
		// it, can have its condition change. Exact tasks live in their
		// own bucket and an ancestor is not an exact match; hierarchical
		// tasks are visited for the changed tag and every ancestor. A
		// tag change therefore costs O(tasks watching that tag and its
		// ancestors), not O(tasks on the entity).
		VisitTagBucket(state.ExactTagHeads, changedTag);

		if (state.HierarchicalTagHeads is null)
		{
			return;
		}

		VisitTagBucket(state.HierarchicalTagHeads, changedTag);

		foreach (GameplayTag ancestor in
			GameplayTagRegistry.Shared.GetAncestors(changedTag))
		{
			VisitTagBucket(state.HierarchicalTagHeads, ancestor);
		}
	}

	private void VisitTagBucket(
		Dictionary<GameplayTag, int>? heads,
		GameplayTag tag)
	{
		if (heads is null || !heads.TryGetValue(tag, out int slot))
		{
			return;
		}

		while (slot >= 0)
		{
			int next = _tasks[slot].NextOnTag;
			ref TaskSlot task = ref _tasks[slot];

			if (task.HeapIndex < 0 && ConditionHolds(ref task))
			{
				ScheduleDeadline(slot, _time);
			}

			slot = next;
		}
	}

	private bool ConditionHolds(ref TaskSlot task)
	{
		bool present = task.Exact
			? _tags.HasTagExact(task.Owner, task.Tag)
			: _tags.HasTag(task.Owner, task.Tag);

		return task.TagMode == GameplayTagWaitMode.Present
			? present
			: !present;
	}

	private void LinkActivation(int abilitySlot, int slot)
	{
		int head = _abilities.GetTaskHead(abilitySlot);

		_tasks[slot].NextOnActivation = head;
		_tasks[slot].PreviousOnActivation = -1;

		if (head >= 0)
		{
			_tasks[head].PreviousOnActivation = slot;
		}

		_abilities.SetTaskHead(abilitySlot, slot);
	}

	private void UnlinkActivation(int slot)
	{
		ref TaskSlot task = ref _tasks[slot];

		if (!_abilities.TryResolveGrant(
				task.Ability,
				out int abilitySlot,
				out _)
			|| _abilities.GetActivationId(abilitySlot) != task.ActivationId)
		{
			// The grant was released or recycled, which resets its task
			// head. There is no list to update.
			return;
		}

		int previous = task.PreviousOnActivation;
		int next = task.NextOnActivation;

		if (previous >= 0)
		{
			_tasks[previous].NextOnActivation = next;
		}
		else
		{
			_abilities.SetTaskHead(abilitySlot, next);
		}

		if (next >= 0)
		{
			_tasks[next].PreviousOnActivation = previous;
		}

		task.NextOnActivation = -1;
		task.PreviousOnActivation = -1;
	}

	private void LinkOwner(Entity owner, int slot)
	{
		ref EntityTasks state = ref GetOrCreateOwnerSlot(owner);
		int head = state.Head;

		_tasks[slot].NextOnOwner = head;
		_tasks[slot].PreviousOnOwner = -1;

		if (head >= 0)
		{
			_tasks[head].PreviousOnOwner = slot;
		}

		state.Head = slot;
		state.Count++;
	}

	private void UnlinkOwner(int slot)
	{
		ref TaskSlot task = ref _tasks[slot];

		if (!TryGetOwnerSlot(
			task.Owner,
			out int segmentIndex,
			out int offset))
		{
			return;
		}

		ref EntityTasks state = ref _owners[segmentIndex]![offset];
		int previous = task.PreviousOnOwner;
		int next = task.NextOnOwner;

		if (previous >= 0)
		{
			_tasks[previous].NextOnOwner = next;
		}
		else
		{
			state.Head = next;
		}

		if (next >= 0)
		{
			_tasks[next].PreviousOnOwner = previous;
		}

		if (state.Count > 0)
		{
			state.Count--;
		}

		task.NextOnOwner = -1;
		task.PreviousOnOwner = -1;
	}

	private void LinkTagOwner(Entity owner, int slot)
	{
		ref EntityTasks state = ref GetOrCreateOwnerSlot(owner);
		ref TaskSlot task = ref _tasks[slot];
		GameplayTag tag = task.Tag;
		Dictionary<GameplayTag, int>? heads = task.Exact
			? state.ExactTagHeads ??= []
			: state.HierarchicalTagHeads ??= [];

		int head = heads.TryGetValue(tag, out int existing)
			? existing
			: -1;

		task.NextOnTag = head;
		task.PreviousOnTag = -1;

		if (head >= 0)
		{
			_tasks[head].PreviousOnTag = slot;
		}

		heads[tag] = slot;
	}

	private void UnlinkTagOwner(int slot)
	{
		ref TaskSlot task = ref _tasks[slot];

		if (!TryGetOwnerSlot(
			task.Owner,
			out int segmentIndex,
			out int offset))
		{
			return;
		}

		ref EntityTasks state = ref _owners[segmentIndex]![offset];
		Dictionary<GameplayTag, int>? heads = task.Exact
			? state.ExactTagHeads
			: state.HierarchicalTagHeads;

		if (heads is null)
		{
			return;
		}

		int previous = task.PreviousOnTag;
		int next = task.NextOnTag;

		if (previous >= 0)
		{
			_tasks[previous].NextOnTag = next;
		}
		else
		{
			heads[task.Tag] = next;
		}

		if (next >= 0)
		{
			_tasks[next].PreviousOnTag = previous;
		}

		task.NextOnTag = -1;
		task.PreviousOnTag = -1;

		if (heads.TryGetValue(task.Tag, out int head) && head < 0)
		{
			// Keep the empty dictionary. Removing it would make every
			// later wait on this entity allocate a new one, which is a
			// steady-state allocation in an otherwise allocation-free
			// task lifecycle. The dictionary is released when the entity
			// is destroyed or its slot is recycled.
			heads.Remove(task.Tag);
		}
	}

	private void ReleaseTaskStorage(int slot, bool unlinkOwner)
	{
		ref TaskSlot task = ref _tasks[slot];
		GameplayEventSubscription subscription = task.EventSubscription;

		if (!subscription.IsNone && !_events.IsDisposed)
		{
			_events.Unsubscribe(subscription);
		}

		if (task.HeapIndex >= 0)
		{
			RemoveDeadline(slot);
		}

		if (unlinkOwner)
		{
			UnlinkOwner(slot);
		}

		if (task.Kind == GameplayAbilityTaskKind.TagCondition)
		{
			UnlinkTagOwner(slot);
		}

		UnlinkActivation(slot);

		if (_pendingCount > 0)
		{
			_pendingCount--;
		}

		ReleaseSlot(slot);
	}

	private void ReleaseOwnerTasksSilently(ref EntityTasks state)
	{
		int slot = state.Head;

		while (slot >= 0)
		{
			int next = _tasks[slot].NextOnOwner;

			ReleaseTaskStorage(slot, unlinkOwner: false);

			slot = next;
		}

		state.Head = -1;
		state.Count = 0;
		state.ExactTagHeads = null;
		state.HierarchicalTagHeads = null;
	}

	private void HandleEntityDestroyed(Entity entity)
	{
		if (entity.AllocatorId != _allocatorId)
		{
			return;
		}

		if (!TryGetOwnerSlot(entity, out int segmentIndex, out int offset))
		{
			return;
		}

		ReleaseOwnerTasksSilently(ref _owners[segmentIndex]![offset]);
	}

	private EntityTasks[] GetOwnerSegment(int segmentIndex)
	{
		if (segmentIndex >= _owners.Length)
		{
			Array.Resize(
				ref _owners,
				Math.Max(segmentIndex + 1, _owners.Length * 2));
		}

		EntityTasks[]? segment = _owners[segmentIndex];

		if (segment is null)
		{
			segment = new EntityTasks[SegmentSize];
			_owners[segmentIndex] = segment;
		}

		return segment;
	}

	private ref EntityTasks GetOrCreateOwnerSlot(Entity owner)
	{
		int entityIndex = (int)owner.Index;
		int segmentIndex = entityIndex >> SegmentShift;
		EntityTasks[] segment = GetOwnerSegment(segmentIndex);

		ref EntityTasks state = ref segment[entityIndex & SegmentMask];

		if (state.Generation != owner.Generation)
		{
			// An earlier destruction handler may have recycled the index
			// and granted abilities to the replacement before this
			// system observed the old destruction. Retire the old
			// tasks before adopting the new generation.
			if (state.Count > 0)
			{
				ReleaseOwnerTasksSilently(ref state);
			}

			state.Generation = owner.Generation;
			state.Head = -1;
			state.Count = 0;
			state.ExactTagHeads = null;
			state.HierarchicalTagHeads = null;
		}

		return ref state;
	}

	private bool TryGetOwnerSlot(
		Entity owner,
		out int segmentIndex,
		out int offset)
	{
		segmentIndex = 0;
		offset = 0;

		if (owner.AllocatorId != _allocatorId)
		{
			return false;
		}

		int entityIndex = (int)owner.Index;
		segmentIndex = entityIndex >> SegmentShift;

		if (segmentIndex >= _owners.Length)
		{
			return false;
		}

		EntityTasks[]? segment = _owners[segmentIndex];

		if (segment is null)
		{
			return false;
		}

		offset = entityIndex & SegmentMask;

		return segment[offset].Generation == owner.Generation;
	}

	private int CaptureOwnerTasks(
		Entity owner,
		GameplayAbilityTaskHandle[] destination)
	{
		if (!TryGetOwnerSlot(owner, out int segmentIndex, out int offset))
		{
			return 0;
		}

		int count = 0;
		int slot = _owners[segmentIndex]![offset].Head;

		while (slot >= 0)
		{
			destination[count++] = CreateHandle(slot);

			slot = _tasks[slot].NextOnOwner;
		}

		return count;
	}

	private GameplayAbilityTaskHandle CreateHandle(int slot)
	{
		ref TaskSlot task = ref _tasks[slot];

		return new GameplayAbilityTaskHandle(
			_systemId,
			task.Owner,
			slot,
			task.Generation,
			task.ActivationId);
	}

	private GameplayAbilityTaskChange CreateChange(
		GameplayAbilityTaskChangeKind kind,
		int slot,
		GameplayAbilityTaskCompletionReason reason,
		in GameplayEvent gameplayEvent)
	{
		ref TaskSlot task = ref _tasks[slot];

		return new GameplayAbilityTaskChange(
			kind,
			CreateHandle(slot),
			new GameplayAbilityActivation(task.Ability, task.ActivationId),
			task.Kind,
			reason,
			task.PlanStep,
			gameplayEvent);
	}

	private void RaiseChange(in GameplayAbilityTaskChange change)
	{
		Changed?.Invoke(change);
	}

	private bool TryResolve(
		GameplayAbilityTaskHandle handle,
		out int slot)
	{
		slot = handle.Slot;

		if (handle.IsNone
			|| (uint)handle.Slot >= (uint)_taskSlotCount)
		{
			return false;
		}

		ref TaskSlot task = ref _tasks[handle.Slot];

		return handle.SystemId == _systemId
			&& task.Generation == handle.Generation
			&& task.Owner == handle.Owner
			&& task.ActivationId == handle.ActivationId;
	}

	private bool TryResolveTask(
		int slot,
		uint generation,
		out int resolved)
	{
		resolved = slot;

		if ((uint)slot >= (uint)_taskSlotCount)
		{
			resolved = -1;
			return false;
		}

		return _tasks[slot].Generation == generation
			&& generation != 0;
	}

	private int AcquireTaskSlot()
	{
		int slot;

		if (_freeTaskSlot >= 0)
		{
			slot = _freeTaskSlot;
			_freeTaskSlot = _tasks[slot].NextOnOwner;
		}
		else
		{
			if (_taskSlotCount == _tasks.Length)
			{
				Array.Resize(
					ref _tasks,
					Math.Max(InitialTaskCapacity, _tasks.Length * 2));
			}

			slot = _taskSlotCount++;
		}

		ref TaskSlot task = ref _tasks[slot];

		if (task.Generation == 0)
		{
			task.Generation = 1;
		}

		return slot;
	}

	private void ReleaseSlot(int slot)
	{
		ref TaskSlot task = ref _tasks[slot];
		uint generation = NextGeneration(task.Generation);

		_tasks[slot] = new TaskSlot
		{
			Generation = generation,
			NextOnOwner = _freeTaskSlot,
			PreviousOnOwner = -1,
			NextOnActivation = -1,
			PreviousOnActivation = -1,
			HeapIndex = -1,
			NextOnTag = -1,
			PreviousOnTag = -1,
			EventSubscription = GameplayEventSubscription.None,
			PlanStep = -1
		};

		_freeTaskSlot = slot;
	}

	private void ScheduleDeadline(int slot, double deadline)
	{
		ref TaskSlot task = ref _tasks[slot];

		// A task created while the advance is delivering completions
		// must not run in that same advance. A zero-duration task
		// would otherwise sit at the heap root with a deadline equal
		// to the current step and starve eligible tasks behind it.
		// Move it just past the step boundary instead; the next
		// positive advance completes it. Tasks created before the
		// advance keep the exact deadline they requested.
		if (_advancing && deadline <= _time)
		{
			deadline = Math.BitIncrement(_time);
		}

		task.Deadline = deadline;
		task.Sequence = NextSequence();

		if (_deadlineCount == _deadlines.Length)
		{
			Array.Resize(
				ref _deadlines,
				Math.Max(InitialDeadlineCapacity, _deadlines.Length * 2));
		}

		int index = _deadlineCount++;

		_deadlines[index] = new DeadlineEntry(
			deadline,
			task.Sequence,
			slot,
			task.Generation);

		task.HeapIndex = index;

		SiftUp(index);
	}

	private void RemoveDeadline(int slot)
	{
		int index = _tasks[slot].HeapIndex;

		if (index < 0)
		{
			return;
		}

		_tasks[slot].HeapIndex = -1;

		int last = --_deadlineCount;

		if (index != last)
		{
			_deadlines[index] = _deadlines[last];

			DeadlineEntry moved = _deadlines[index];

			if (TryResolveTask(moved.Slot, moved.Generation, out int movedSlot))
			{
				_tasks[movedSlot].HeapIndex = index;
			}

			int parent = (index - 1) >> 1;

			if (index > 0 && IsBefore(_deadlines[index], _deadlines[parent]))
			{
				SiftUp(index);
			}
			else
			{
				SiftDown(index);
			}
		}

		_deadlines[last] = default;
	}

	private void PopDeadline()
	{
		DeadlineEntry root = _deadlines[0];

		if (TryResolveTask(root.Slot, root.Generation, out int rootSlot))
		{
			_tasks[rootSlot].HeapIndex = -1;
		}

		int last = --_deadlineCount;

		if (last > 0)
		{
			_deadlines[0] = _deadlines[last];

			DeadlineEntry moved = _deadlines[0];

			if (TryResolveTask(moved.Slot, moved.Generation, out int movedSlot))
			{
				_tasks[movedSlot].HeapIndex = 0;
			}

			SiftDown(0);
		}

		_deadlines[last] = default;
	}

	private void SiftUp(int index)
	{
		while (index > 0)
		{
			int parent = (index - 1) >> 1;

			if (!IsBefore(_deadlines[index], _deadlines[parent]))
			{
				return;
			}

			SwapDeadlines(index, parent);
			index = parent;
		}
	}

	private void SiftDown(int index)
	{
		while (true)
		{
			int left = (index << 1) + 1;

			if (left >= _deadlineCount)
			{
				return;
			}

			int right = left + 1;
			int smallest = right < _deadlineCount
				&& IsBefore(_deadlines[right], _deadlines[left])
					? right
					: left;

			if (!IsBefore(_deadlines[smallest], _deadlines[index]))
			{
				return;
			}

			SwapDeadlines(index, smallest);
			index = smallest;
		}
	}

	private void SwapDeadlines(int left, int right)
	{
		DeadlineEntry entry = _deadlines[left];

		_deadlines[left] = _deadlines[right];
		_deadlines[right] = entry;

		if (TryResolveTask(
			_deadlines[left].Slot,
			_deadlines[left].Generation,
			out int leftSlot))
		{
			_tasks[leftSlot].HeapIndex = left;
		}

		if (TryResolveTask(
			_deadlines[right].Slot,
			_deadlines[right].Generation,
			out int rightSlot))
		{
			_tasks[rightSlot].HeapIndex = right;
		}
	}

	private static bool IsBefore(DeadlineEntry left, DeadlineEntry right)
	{
		return left.Deadline < right.Deadline
			|| (left.Deadline == right.Deadline
				&& left.Sequence < right.Sequence);
	}

	private uint NextSequence()
	{
		uint sequence = unchecked(++_nextSequence);

		return sequence == 0 ? unchecked(++_nextSequence) : sequence;
	}

	private static void ValidateDuration(float durationSeconds)
	{
		if (!float.IsFinite(durationSeconds) || durationSeconds < 0f)
		{
			throw new ArgumentOutOfRangeException(
				nameof(durationSeconds),
				durationSeconds,
				"A task duration must be finite and not negative. A zero " +
				"duration completes on the next advance.");
		}
	}

	private static void ValidateEventTag(GameplayTag eventTag)
	{
		// GameplayTag.None is the wildcard: wait for any event.
		if (!eventTag.IsValid && eventTag != GameplayTag.None)
		{
			throw new ArgumentException(
				"An event tag must be registered or the invalid tag for " +
				"a wildcard wait.",
				nameof(eventTag));
		}
	}

	private static void ValidateTag(GameplayTag tag)
	{
		if (!tag.IsValid)
		{
			throw new ArgumentException(
				"A tag condition needs a registered tag.",
				nameof(tag));
		}
	}

	private static void ValidateTagWaitMode(GameplayTagWaitMode mode)
	{
		if (mode is not (
			GameplayTagWaitMode.Present or GameplayTagWaitMode.Absent))
		{
			throw new ArgumentOutOfRangeException(
				nameof(mode),
				mode,
				"Unknown tag wait mode.");
		}
	}

	private static uint NextGeneration(uint generation)
	{
		generation++;

		return generation == 0 ? 1 : generation;
	}

	private static uint AllocateSystemId()
	{
		uint id = unchecked((uint)Interlocked.Increment(ref s_nextSystemId));

		return id == 0
			? unchecked((uint)Interlocked.Increment(ref s_nextSystemId))
			: id;
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}

	private sealed class EventSink : IGameplayEventSink
	{
		private readonly GameplayAbilityTaskSystem _system;

		public EventSink(GameplayAbilityTaskSystem system)
		{
			_system = system;
		}

		public void OnGameplayEvent(
			in GameplayEvent gameplayEvent,
			GameplayEventSubscription subscription,
			int token)
		{
			_system.HandleGameplayEvent(
				in gameplayEvent,
				subscription,
				token);
		}
	}

	private sealed class BufferPool<T>
	{
		private readonly List<T[]> _buffers = [];
		private int _depth;

		public T[] Rent(int capacity)
		{
			if (_depth == _buffers.Count)
			{
				_buffers.Add(new T[Math.Max(16, capacity)]);
			}

			T[] buffer = _buffers[_depth++];

			if (buffer.Length < capacity)
			{
				Array.Resize(
					ref buffer,
					Math.Max(capacity, buffer.Length * 2));

				_buffers[_depth - 1] = buffer;
			}

			return buffer;
		}

		public void Return()
		{
			_depth--;
		}

		public void Clear()
		{
			_buffers.Clear();
			_depth = 0;
		}
	}

	private struct TaskSlot
	{
		public uint Generation;
		public uint ActivationId;
		public GameplayAbilityHandle Ability;
		public Entity Owner;
		public GameplayAbilityTaskKind Kind;
		public int NextOnActivation;
		public int PreviousOnActivation;
		public int NextOnOwner;
		public int PreviousOnOwner;
		public int NextOnTag;
		public int PreviousOnTag;
		public int HeapIndex;
		public double Deadline;
		public uint Sequence;
		public int PlanStep;
		public GameplayTag Tag;
		public GameplayEventMatch Match;
		public GameplayTagWaitMode TagMode;
		public bool Exact;
		public GameplayEventSubscription EventSubscription;
	}

	private struct DeadlineEntry
	{
		public DeadlineEntry(
			double deadline,
			uint sequence,
			int slot,
			uint generation)
		{
			Deadline = deadline;
			Sequence = sequence;
			Slot = slot;
			Generation = generation;
		}

		public double Deadline;
		public uint Sequence;
		public int Slot;
		public uint Generation;
	}

	private struct EntityTasks
	{
		public uint Generation;
		public int Head;
		public int Count;
		public Dictionary<GameplayTag, int>? ExactTagHeads;
		public Dictionary<GameplayTag, int>? HierarchicalTagHeads;
	}
}
