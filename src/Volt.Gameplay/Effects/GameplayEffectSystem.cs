using Volt.Ecs;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Simulation;
using Volt.Gameplay.Tags;

namespace Volt.Gameplay.Effects;

/// <summary>
/// Applies, tracks, schedules, and removes gameplay effects for the
/// entities of one world.
/// </summary>
/// <remarks>
/// <para>
/// Instances live in a generational slot pool, not in managed objects.
/// Each instance stores only what differs between applications: the
/// definition, the target and source, the stack count, the clock, and
/// the modifier handles needed to undo it. Applying the same
/// definition to a million entities does not create a million
/// <see cref="GameplayEffectDefinition"/> copies.
/// </para>
/// <para>
/// Expiry and periodic ticks share one binary min-heap keyed by due
/// time and insertion order. An effect population that is idle costs a
/// single comparison per <see cref="Advance"/> call, because nothing is
/// scanned: the heap root is the next event, and if it is not due yet
/// the call returns. There is no per-frame sweep over active effects.
/// </para>
/// <para>
/// Events are delivered in due-time order, then in the order they were
/// scheduled, so two expirations on the same step always resolve the
/// same way. A period tick that lands exactly on an expiry executes
/// before the effect expires, matching Unreal's final periodic
/// execution.
/// </para>
/// <para>
/// The system is single-threaded and owns no locks. It composes the
/// attribute and tag stores rather than duplicating them:
/// <see cref="GameplayAttributeStore"/> performs the numeric
/// aggregation and <see cref="GameplayTagStore"/> performs the
/// reference-counted tag bookkeeping.
/// </para>
/// </remarks>
public sealed class GameplayEffectSystem : IDisposable
{
	private static int s_nextSystemId;

	private const int SegmentShift = 10;
	private const int SegmentSize = 1 << SegmentShift;
	private const int SegmentMask = SegmentSize - 1;
	private const int InitialEffectCapacity = 16;
	private const int InitialDueCapacity = 32;
	private const int MaxRefreshPasses = 8;

	private readonly World _world;
	private readonly GameplayAttributeStore _attributes;
	private readonly GameplayTagStore _tags;
	private readonly uint _allocatorId;
	private readonly uint _systemId;

	private ActiveEffect[] _effects = new ActiveEffect[InitialEffectCapacity];
	private int _effectSlotCount;
	private int _freeEffectSlot = -1;
	private int _activeEffectCount;

	private EntityEffects[]?[] _entitySegments = [];

	private DueEntry[] _dueEntries = new DueEntry[InitialDueCapacity];
	private int _dueCount;

	private double _time;
	private long _nextOrder = 1;
	private bool _advancing;
	private SimulationClockClaim? _clockClaim;
	private bool _disposed;

	/// <summary>
	/// Creates an effect runtime over a world's attribute and tag
	/// stores.
	/// </summary>
	/// <param name="world">The world whose entities can be affected.</param>
	/// <param name="attributes">The attribute store to modify.</param>
	/// <param name="tags">The tag store to read and grant.</param>
	/// <exception cref="ArgumentNullException">
	/// An argument is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// The stores must have been created for the same world, and must
	/// outlive this system. Dispose this system before disposing them.
	/// </remarks>
	public GameplayEffectSystem(
		World world,
		GameplayAttributeStore attributes,
		GameplayTagStore tags)
	{
		ArgumentNullException.ThrowIfNull(world);
		ArgumentNullException.ThrowIfNull(attributes);
		ArgumentNullException.ThrowIfNull(tags);

		_world = world;
		_attributes = attributes;
		_tags = tags;
		_allocatorId = world.AllocatorId;
		_systemId = AllocateSystemId();

		_world.EntityDestroyed += HandleEntityDestroyed;
		_tags.TagsChanged += HandleTagsChanged;
	}

	/// <summary>
	/// Occurs after an effect is applied, stacked, inhibited, restored,
	/// removed, expired, or executed periodically.
	/// </summary>
	/// <remarks>
	/// The notification is raised after the change is committed, so a
	/// handler may query the system and may apply or remove effects.
	/// With no subscribers the cost is one null check per change.
	/// </remarks>
	public event Action<GameplayEffectChange>? Changed;

	internal event Action<GameplayEffectChange>? CueChanged;

	/// <summary>
	/// Gets the simulation time the system has advanced through, in
	/// seconds.
	/// </summary>
	/// <remarks>
	/// The clock is driven by <see cref="Advance"/> with the game
	/// loop's simulation delta, so time scaling and pausing are
	/// already applied by the caller. A paused game advances by zero
	/// and no effect makes progress.
	/// </remarks>
	public double Time => _time;

	/// <summary>
	/// Gets the number of active, tracked effect instances.
	/// </summary>
	public int ActiveEffectCount => _activeEffectCount;

	/// <summary>
	/// Gets whether the clock is owned by a
	/// <see cref="Simulation.GameplaySimulation"/>.
	/// </summary>
	internal bool IsSimulationDriven => _clockClaim is not null;

	internal World World => _world;

	/// <summary>
	/// Applies an effect definition to an entity.
	/// </summary>
	/// <param name="target">The living entity to affect.</param>
	/// <param name="definition">The shared effect definition.</param>
	/// <param name="source">
	/// The entity credited with the application, or
	/// <c>default(Entity)</c> for none. It is an identity, not a live
	/// reference, so it may already be destroyed.
	/// </param>
	/// <returns>The outcome, including the handle of the new or
	/// updated instance.</returns>
	/// <exception cref="ArgumentNullException">
	/// The definition is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// The target is not alive in this system's world.
	/// </exception>
	/// <remarks>
	/// <para>
	/// A rejected application is reported, not thrown: failing a tag
	/// requirement is ordinary gameplay. Applying an effect that
	/// modifies an unregistered attribute fails at definition build
	/// time instead.
	/// </para>
	/// <para>
	/// An <see cref="GameplayEffectDurationType.Instant"/> effect
	/// executes its modifiers against the base value and returns no
	/// handle, exactly like Unreal, which never stores an instant spec
	/// in the active container.
	/// </para>
	/// </remarks>
	public GameplayEffectApplyResult Apply(
		Entity target,
		GameplayEffectDefinition definition,
		Entity source = default)
	{
		return Apply(target, definition, source, out _);
	}

	internal GameplayEffectApplyResult Apply(
		Entity target,
		GameplayEffectDefinition definition,
		Entity source,
		out GameplayEffectApplicationRollback rollback)
	{
		rollback = default;

		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(definition);

		if (!_world.IsAlive(target))
		{
			throw new ArgumentException(
				"The entity is not alive in this system's world. An " +
				"effect cannot be applied to a destroyed or recycled " +
				"handle.",
				nameof(target));
		}

		if (!definition.ApplicationRequirements.IsSatisfiedBy(_tags, target))
		{
			return new GameplayEffectApplyResult(
				GameplayEffectApplyStatus.RejectedByRequirements,
				GameplayEffectHandle.None,
				target,
				0);
		}

		if (definition.IsInstant)
		{
			ExecuteModifiers(target, definition, 1);
			if (_world.IsAlive(target))
			{
				NotifyCueChange(new GameplayEffectChange(
					GameplayEffectChangeKind.Applied,
					GameplayEffectHandle.None,
					definition,
					target,
					source,
					1));
			}

			return new GameplayEffectApplyResult(
				GameplayEffectApplyStatus.InstantExecuted,
				GameplayEffectHandle.None,
				target,
				1);
		}

		int existing = FindStackableInstance(target, definition, source);

		if (existing >= 0)
		{
			rollback = CaptureRollback(existing);

			return AddStack(existing, definition, source);
		}

		return CreateInstance(target, definition, source);
	}

	/// <summary>
	/// Removes one active instance.
	/// </summary>
	/// <param name="handle">The handle of the instance.</param>
	/// <returns>
	/// <see langword="true"/> when this call removed the instance;
	/// <see langword="false"/> when it had already ended, belongs to
	/// another system, or its entity is gone.
	/// </returns>
	/// <remarks>
	/// Removing an instance withdraws its modifiers and granted tags
	/// immediately and raises one
	/// <see cref="GameplayEffectChangeKind.Removed"/> notification.
	/// </remarks>
	public bool Remove(GameplayEffectHandle handle)
	{
		ThrowIfDisposed();

		if (!TryResolve(handle, out int slot))
		{
			return false;
		}

		RemoveEffect(slot, GameplayEffectChangeKind.Removed);

		return true;
	}

	/// <summary>
	/// Removes every active instance on an entity.
	/// </summary>
	/// <param name="target">The entity.</param>
	/// <returns>The number of instances removed.</returns>
	public int RemoveAll(Entity target)
	{
		ThrowIfDisposed();

		if (!TryGetEntitySlot(target, out int segmentIndex, out int offset))
		{
			return 0;
		}

		int removed = 0;
		int slot = _entitySegments[segmentIndex]![offset].Head;

		while (slot >= 0)
		{
			int next = _effects[slot].NextOnEntity;
			uint nextGeneration = next >= 0
				? _effects[next].Generation
				: 0;

			if (_effects[slot].Active)
			{
				RemoveEffect(slot, GameplayEffectChangeKind.Removed);
				removed++;
			}

			slot = next;

			// A removal handler may remove and reuse the next slot.
			// Stop at that point instead of removing the new occupant.
			if (slot >= 0 && _effects[slot].Generation != nextGeneration)
			{
				break;
			}
		}

		return removed;
	}

	/// <summary>
	/// Copies a snapshot of one active instance.
	/// </summary>
	/// <param name="handle">The handle of the instance.</param>
	/// <param name="effect">The snapshot.</param>
	/// <returns>
	/// <see langword="true"/> when the handle refers to a live
	/// instance.
	/// </returns>
	public bool TryGetEffect(
		GameplayEffectHandle handle,
		out ActiveGameplayEffect effect)
	{
		ThrowIfDisposed();

		if (!TryResolve(handle, out int slot))
		{
			effect = default;
			return false;
		}

		effect = Describe(slot);

		return true;
	}

	/// <summary>
	/// Gets the number of active instances on an entity.
	/// </summary>
	/// <param name="target">The entity.</param>
	public int GetEffectCount(Entity target)
	{
		ThrowIfDisposed();

		return TryGetEntitySlot(target, out int segmentIndex, out int offset)
			? _entitySegments[segmentIndex]![offset].Count
			: 0;
	}

	/// <summary>
	/// Finds the active instance of a definition on an entity.
	/// </summary>
	/// <param name="target">The entity.</param>
	/// <param name="definition">The definition.</param>
	/// <param name="effect">The snapshot of the first match.</param>
	/// <returns>
	/// <see langword="true"/> when an instance is active.
	/// </returns>
	public bool TryFindEffect(
		Entity target,
		GameplayEffectDefinition definition,
		out ActiveGameplayEffect effect)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(definition);

		if (TryGetEntitySlot(target, out int segmentIndex, out int offset))
		{
			int slot = _entitySegments[segmentIndex]![offset].Head;

			while (slot >= 0)
			{
				ref ActiveEffect candidate = ref _effects[slot];

				if (candidate.Active
					&& ReferenceEquals(candidate.Definition, definition))
				{
					effect = Describe(slot);
					return true;
				}

				slot = candidate.NextOnEntity;
			}
		}

		effect = default;
		return false;
	}

	/// <summary>
	/// Copies the handles of an entity's active instances.
	/// </summary>
	/// <param name="target">The entity.</param>
	/// <param name="destination">The destination span.</param>
	/// <returns>
	/// The number of handles written, which is less than
	/// <see cref="GetEffectCount"/> when the span is too small.
	/// </returns>
	public int CopyEffects(Entity target, Span<GameplayEffectHandle> destination)
	{
		ThrowIfDisposed();

		if (!TryGetEntitySlot(target, out int segmentIndex, out int offset))
		{
			return 0;
		}

		int written = 0;
		int slot = _entitySegments[segmentIndex]![offset].Head;

		while (slot >= 0 && written < destination.Length)
		{
			ref ActiveEffect effect = ref _effects[slot];

			if (effect.Active)
			{
				destination[written++] = CreateHandle(slot);
			}

			slot = effect.NextOnEntity;
		}

		return written;
	}

	/// <summary>
	/// Copies the handles of every active instance in the system.
	/// </summary>
	/// <param name="destination">The destination span.</param>
	/// <returns>
	/// The number of handles written, which is less than
	/// <see cref="ActiveEffectCount"/> when the span is too small.
	/// </returns>
	/// <remarks>
	/// This is the enumeration an integration uses to adopt state that
	/// was created before it was connected, such as the cue system
	/// synchronizing the instances that already exist when it binds.
	/// </remarks>
	internal int CopyActiveEffects(Span<GameplayEffectHandle> destination)
	{
		ThrowIfDisposed();

		int written = 0;

		for (int slot = 0; slot < _effectSlotCount && written < destination.Length; slot++)
		{
			if (_effects[slot].Active)
			{
				destination[written++] = CreateHandle(slot);
			}
		}

		return written;
	}

	/// <summary>
	/// Gets the longest remaining duration among an entity's active
	/// instances that grant any of the given tags.
	/// </summary>
	/// <param name="target">The entity.</param>
	/// <param name="tags">The tags to match.</param>
	/// <returns>
	/// The longest remaining duration, zero when no active instance
	/// grants a matching tag, or infinity for an infinite instance.
	/// </returns>
	/// <remarks>
	/// An inhibited instance is included, because its clock keeps
	/// running and its tags may return. This is the query Unreal's
	/// <c>GetActiveEffectsTimeRemaining</c> answers for a tag query.
	/// </remarks>
	public double GetMaxRemainingDuration(Entity target, GameplayTagSet tags)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(tags);

		if (tags.IsEmpty
			|| !TryGetEntitySlot(target, out int segmentIndex, out int offset))
		{
			return 0d;
		}

		double remaining = 0d;
		int slot = _entitySegments[segmentIndex]![offset].Head;

		while (slot >= 0)
		{
			ref ActiveEffect effect = ref _effects[slot];

			if (effect.Active && effect.Definition.GrantedTags.HasAny(tags))
			{
				double candidate = Math.Max(0d, effect.ExpireTime - _time);

				if (candidate > remaining)
				{
					remaining = candidate;
				}
			}

			slot = effect.NextOnEntity;
		}

		return remaining;
	}

	/// <summary>
	/// Advances simulation time and processes every effect event that
	/// came due.
	/// </summary>
	/// <param name="deltaTime">
	/// The elapsed simulation time in seconds. It must be finite and
	/// not negative.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The delta is not finite or is negative.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The call re-enters itself from an effect or attribute
	/// notification.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Every due boundary is processed, in order, so a large step
	/// produces the same sequence of periodic ticks as several small
	/// ones. The engine's fixed-update loop advances by a bounded step,
	/// which keeps that sequence short; a caller that advances by a
	/// huge delta is asking for a correspondingly large amount of
	/// deterministic work.
	/// </para>
	/// <para>
	/// A delta of zero is a no-op, which is what a paused game
	/// produces.
	/// </para>
	/// </remarks>
	public void Advance(float deltaTime)
	{
		ThrowIfDisposed();

		if (_clockClaim is not null)
		{
			throw new InvalidOperationException(
				"This effect system is advanced by a " +
				"GameplaySimulation attached to the fixed-step loop. " +
				"Calling Advance manually would advance the same clock " +
				"twice in one step. Detach the integration or use its " +
				"loop to advance simulation time.");
		}

		AdvanceCore(deltaTime);
	}

	/// <summary>
	/// Advances the clock from the attached simulation integration.
	/// </summary>
	internal void AdvanceFromSimulation(float deltaTime)
	{
		ThrowIfDisposed();

		AdvanceCore(deltaTime);
	}

	/// <summary>
	/// Claims the clock for one fixed-step integration.
	/// </summary>
	/// <param name="claim">The claiming integration's identity.</param>
	/// <exception cref="InvalidOperationException">
	/// Another integration already owns the clock.
	/// </exception>
	internal void BeginSimulation(SimulationClockClaim claim)
	{
		ArgumentNullException.ThrowIfNull(claim);

		if (_clockClaim is not null)
		{
			throw new InvalidOperationException(
				"This effect system is already advanced by another " +
				"GameplaySimulation. A clock has one owner.");
		}

		_clockClaim = claim;
	}

	/// <summary>
	/// Releases the clock when its integration is disposed.
	/// </summary>
	/// <param name="claim">The releasing integration's identity.</param>
	/// <remarks>
	/// A claim this system does not hold is left alone, so an
	/// integration that failed to acquire it cannot detach the owner.
	/// </remarks>
	internal void EndSimulation(SimulationClockClaim claim)
	{
		if (!ReferenceEquals(_clockClaim, claim))
		{
			return;
		}

		_clockClaim = null;
	}

	private void AdvanceCore(float deltaTime)
	{
		if (!float.IsFinite(deltaTime) || deltaTime < 0f)
		{
			throw new ArgumentOutOfRangeException(
				nameof(deltaTime),
				deltaTime,
				"A simulated time step must be finite and not negative. " +
				"A negative step would rewind effect clocks, which is " +
				"not supported.");
		}

		if (deltaTime == 0f)
		{
			return;
		}

		if (_advancing)
		{
			throw new InvalidOperationException(
				"Advance cannot be called from an effect or attribute " +
				"notification. Apply or remove effects from a handler " +
				"instead, and advance time from the simulation loop.");
		}

		_advancing = true;

		try
		{
			_time += deltaTime;

			while (_dueCount > 0 && _dueEntries[0].Time <= _time)
			{
				ProcessDue(PopDue());
			}
		}
		finally
		{
			_advancing = false;
		}

		CompactDueQueueIfWasteful();
	}

	/// <inheritdoc />
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_world.EntityDestroyed -= HandleEntityDestroyed;
		_tags.TagsChanged -= HandleTagsChanged;

		Changed = null;
		CueChanged = null;
		_effects = [];
		_entitySegments = [];
		_dueEntries = [];
		_dueCount = 0;
		_effectSlotCount = 0;
		_freeEffectSlot = -1;
		_activeEffectCount = 0;
		_clockClaim = null;
		_disposed = true;

		GC.SuppressFinalize(this);
	}

	private GameplayEffectApplyResult CreateInstance(
		Entity target,
		GameplayEffectDefinition definition,
		Entity source)
	{
		int slot = AcquireEffectSlot();

		_effects[slot].Definition = definition;
		_effects[slot].Target = target;
		_effects[slot].Source = source;
		_effects[slot].StackCount = 1;
		_effects[slot].ModifierCount = 0;
		_effects[slot].TagsGranted = false;
		_effects[slot].Active = true;
		_effects[slot].Inhibited = false;
		_effects[slot].PendingOrder = 0;
		_effects[slot].NextPeriodTime = double.PositiveInfinity;
		_effects[slot].ExpireTime = definition.IsInfinite
			? double.PositiveInfinity
			: _time + definition.Duration;

		Link(target, slot);

		_activeEffectCount++;

		uint generation = _effects[slot].Generation;
		GameplayEffectHandle handle = CreateHandle(slot);

		// The initial inhibition decision is made before the effect
		// grants its own tags, matching Unreal, which evaluates the
		// ongoing requirements before the effect's contribution exists.
		// The initial state is reported on the Applied notification; it
		// is not a transition and raises no Inhibited notification.
		bool inhibited = !definition.OngoingRequirements.IsSatisfiedBy(
			_tags,
			target);

		_effects[slot].Inhibited = inhibited;

		if (!inhibited)
		{
			// Granting a tag can notify listeners, which can apply or
			// remove effects, destroy the entity, or inhibit this
			// effect through its own or another effect's tags.
			GrantTags(slot);

			if (!IsLive(slot, generation))
			{
				return Discarded(target);
			}

			if (!_effects[slot].Inhibited)
			{
				AddModifiers(slot);
			}
		}

		if (!IsLive(slot, generation))
		{
			return Discarded(target);
		}

		if (definition.IsPeriodic)
		{
			if (!_effects[slot].Inhibited
				&& definition.ExecutePeriodicOnApplication)
			{
				ExecutePeriodicTick(slot);

				if (!IsLive(slot, generation))
				{
					return Discarded(target);
				}
			}

			_effects[slot].NextPeriodTime = _time + definition.Period;
		}

		if (!IsLive(slot, generation))
		{
			return Discarded(target);
		}

		Schedule(slot);

		int stackCount = _effects[slot].StackCount;

		RaiseChange(GameplayEffectChangeKind.Applied, slot);

		return new GameplayEffectApplyResult(
			GameplayEffectApplyStatus.Applied,
			handle,
			target,
			stackCount);
	}

	internal bool RevertApplication(
		GameplayEffectHandle handle,
		in GameplayEffectApplicationRollback rollback)
	{
		if (handle.IsNone)
		{
			return false;
		}

		if (!rollback.WasStacked || rollback.Handle != handle)
		{
			return Remove(handle);
		}

		if (!TryResolve(handle, out int slot))
		{
			return false;
		}

		ref ActiveEffect effect = ref _effects[slot];

		uint generation = effect.Generation;
		int currentStackCount = effect.StackCount;

		effect.StackCount = rollback.StackCount;
		effect.Source = rollback.Source;
		effect.ExpireTime = rollback.ExpireTime;
		effect.NextPeriodTime = rollback.NextPeriodTime;
		effect.PendingOrder = rollback.PendingOrder;

		// The previous heap entry was left in place by AddStack; once
		// the order is restored it is valid again and the entry the
		// failed application pushed becomes stale.
		RefreshModifierMagnitudes(slot, generation);

		if (currentStackCount != rollback.StackCount
			&& IsLive(slot, generation))
		{
			RaiseChange(GameplayEffectChangeKind.StackChanged, slot);
		}

		return true;
	}

	private GameplayEffectApplicationRollback CaptureRollback(int slot)
	{
		ref ActiveEffect effect = ref _effects[slot];

		return new GameplayEffectApplicationRollback(
			CreateHandle(slot),
			effect.StackCount,
			effect.Source,
			effect.ExpireTime,
			effect.NextPeriodTime,
			effect.PendingOrder);
	}

	private static GameplayEffectApplyResult Discarded(Entity target)
	{
		return new GameplayEffectApplyResult(
			GameplayEffectApplyStatus.Discarded,
			GameplayEffectHandle.None,
			target,
			0);
	}

	private GameplayEffectApplyResult AddStack(
		int slot,
		GameplayEffectDefinition definition,
		Entity source)
	{
		ref ActiveEffect effect = ref _effects[slot];

		Entity target = effect.Target;
		uint generation = effect.Generation;
		int startingStackCount = effect.StackCount;
		int nextStackCount = startingStackCount + definition.StacksPerApplication;

		if (definition.StackLimit > 0
			&& nextStackCount > definition.StackLimit)
		{
			nextStackCount = definition.StackLimit;
		}

		// AggregateByTarget merges applications from different
		// sources, so the newest source owns the instance.
		effect.Source = source;
		effect.StackCount = nextStackCount;

		switch (definition.StackDurationRefreshPolicy)
		{
			case GameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication:
				if (!definition.IsInfinite)
				{
					effect.ExpireTime = _time + definition.Duration;
				}

				break;

			case GameplayEffectStackingDurationPolicy.ExtendDuration:
				if (!definition.IsInfinite
					&& !double.IsPositiveInfinity(effect.ExpireTime))
				{
					effect.ExpireTime += definition.Duration;
				}

				break;
		}

		if (definition.IsPeriodic
			&& definition.StackPeriodResetPolicy
				== GameplayEffectStackingPeriodPolicy.ResetOnSuccessfulApplication)
		{
			effect.NextPeriodTime = _time + definition.Period;
		}

		GameplayEffectHandle handle = CreateHandle(slot);

		Schedule(slot);

		if (nextStackCount != startingStackCount)
		{
			RefreshModifierMagnitudes(slot, generation);

			if (!IsLive(slot, generation))
			{
				return Discarded(target);
			}

			RaiseChange(GameplayEffectChangeKind.StackChanged, slot);
		}

		return new GameplayEffectApplyResult(
			GameplayEffectApplyStatus.Stacked,
			handle,
			target,
			nextStackCount);
	}

	private void ProcessDue(DueEntry entry)
	{
		if ((uint)entry.Slot >= (uint)_effectSlotCount)
		{
			return;
		}

		ref ActiveEffect effect = ref _effects[entry.Slot];

		if (!effect.Active
			|| effect.Generation != entry.Generation
			|| effect.PendingOrder != entry.Order)
		{
			// A stale entry: its effect ended, or a later change
			// rescheduled it. Stale entries are discarded lazily.
			return;
		}

		if (effect.NextPeriodTime <= entry.Time)
		{
			ExecutePeriodicTick(entry.Slot);
			return;
		}

		if (effect.ExpireTime <= _time)
		{
			HandleExpiry(entry.Slot);
		}
	}

	private void HandleExpiry(int slot)
	{
		GameplayEffectDefinition definition = _effects[slot].Definition;
		uint generation = _effects[slot].Generation;

		switch (definition.StackExpirationPolicy)
		{
			case GameplayEffectStackingExpirationPolicy.RemoveSingleStackAndRefreshDuration:
				if (_effects[slot].StackCount <= 1)
				{
					RemoveEffect(slot, GameplayEffectChangeKind.Expired);
					return;
				}

				_effects[slot].StackCount--;
				_effects[slot].ExpireTime = _time + definition.Duration;

				Schedule(slot);

				if (IsLive(slot, generation))
				{
					RefreshModifierMagnitudes(slot, generation);
				}

				if (IsLive(slot, generation))
				{
					RaiseChange(GameplayEffectChangeKind.StackChanged, slot);
				}

				return;

			case GameplayEffectStackingExpirationPolicy.RefreshDuration:
				_effects[slot].ExpireTime = _time + definition.Duration;

				Schedule(slot);
				return;
		}

		RemoveEffect(slot, GameplayEffectChangeKind.Expired);
	}

	private void ExecutePeriodicTick(int slot)
	{
		ref ActiveEffect effect = ref _effects[slot];

		GameplayEffectDefinition definition = effect.Definition;
		Entity target = effect.Target;
		uint generation = effect.Generation;
		bool inhibited = effect.Inhibited;

		if (inhibited)
		{
			FastForwardPeriod(slot);
		}
		else
		{
			ExecuteModifiers(target, definition, effect.StackCount);

			if (!IsLive(slot, generation))
			{
				return;
			}

			// The next boundary is one period after the scheduled one,
			// not one period after the current time. A step that covers
			// several boundaries therefore replays each of them in
			// order instead of skipping to the last one.
			_effects[slot].NextPeriodTime += definition.Period;

			Schedule(slot);

			RaiseChange(GameplayEffectChangeKind.PeriodExecuted, slot);
		}
	}

	private void FastForwardPeriod(int slot)
	{
		ref ActiveEffect effect = ref _effects[slot];

		// A tick that came due while the effect was inhibited is
		// skipped, not replayed: jump to the first boundary after the
		// current time while staying on the original schedule grid.
		effect.NextPeriodTime = NextPeriodBoundary(
			effect.NextPeriodTime,
			effect.Definition.Period);

		Schedule(slot);
	}

	private double NextPeriodBoundary(double scheduledTime, double period)
	{
		double next = scheduledTime + period;

		if (next > _time)
		{
			return next;
		}

		// The effect was inhibited, or the step was large enough that
		// several ticks are already behind us. Skip to the first
		// boundary after the current time while staying on the
		// original schedule grid, so a NeverReset policy resumes
		// exactly where it would have been.
		double missed = Math.Ceiling((_time - next) / period);

		return next + (missed * period);
	}

	private void ExecuteModifiers(
		Entity target,
		GameplayEffectDefinition definition,
		int stackCount)
	{
		int count = definition.ModifierCount;

		for (int index = 0; index < count; index++)
		{
			if (!_world.IsAlive(target))
			{
				return;
			}

			GameplayEffectModifier modifier = definition.Modifiers[index];

			_attributes.ExecuteInstantModifier(
				target,
				modifier.Attribute,
				modifier.Operation,
				definition.GetStackedMagnitude(index, stackCount));
		}
	}

	private void AddModifiers(int slot)
	{
		ref ActiveEffect effect = ref _effects[slot];

		if (effect.Definition.IsPeriodic)
		{
			return;
		}

		GameplayEffectDefinition definition = effect.Definition;
		int count = definition.ModifierCount;

		if (count == 0)
		{
			return;
		}

		GameplayModifierHandle[] handles = EnsureModifierCapacity(slot, count);
		uint generation = effect.Generation;
		Entity target = effect.Target;

		_effects[slot].ModifierCount = 0;
		bool stackChanged = false;

		for (int index = 0; index < count; index++)
		{
			GameplayEffectModifier modifier = definition.Modifiers[index];
			int stackCount = _effects[slot].StackCount;

			// The attribute notification is raised from inside
			// AddModifier, before the returned handle can be published
			// in the effect. A listener may stack the instance, inhibit
			// it, remove it, or clear the attribute. Reconcile the
			// handle instead of assuming the instance stayed in the
			// same state.
			GameplayModifierHandle handle = _attributes.AddModifier(
				target,
				modifier.Attribute,
				modifier.Operation,
				definition.GetStackedMagnitude(index, stackCount));

			if (!IsLive(slot, generation))
			{
				// The instance ended while the notification was running.
				// Its removal could not see this handle because the count
				// had not been published yet, so withdraw it here. If the
				// effect slot was reused, the handle is either stale or
				// belongs to an unrelated instance and does nothing.
				_attributes.RemoveModifier(handle);
				return;
			}

			if (_effects[slot].Inhibited)
			{
				// Inhibition removed the previously published handles.
				// This one was still pending and must follow them.
				_attributes.RemoveModifier(handle);
				return;
			}

			handles[index] = handle;
			_effects[slot].ModifierCount = index + 1;

			if (_effects[slot].StackCount != stackCount)
			{
				stackChanged = true;
			}
		}

		if (stackChanged)
		{
			// A notification during AddModifier stacked the instance
			// before its handle was published. The magnitude was
			// computed for the old count; refresh it once all handles
			// are visible. The ordinary path does no extra pass.
			RefreshModifierMagnitudes(slot, generation);
		}
	}

	private void RemoveModifiers(int slot)
	{
		int count = _effects[slot].ModifierCount;

		if (count == 0)
		{
			return;
		}

		GameplayModifierHandle[] handles = _effects[slot].Modifiers!;

		_effects[slot].ModifierCount = 0;

		for (int index = 0; index < count; index++)
		{
			_attributes.RemoveModifier(handles[index]);
		}
	}

	private void RefreshModifierMagnitudes(int slot, uint generation)
	{
		if (!IsLive(slot, generation))
		{
			return;
		}

		ref ActiveEffect effect = ref _effects[slot];

		if (effect.Inhibited
			|| effect.Definition.IsPeriodic
			|| !effect.Definition.FactorInStackCount
			|| effect.ModifierCount == 0)
		{
			return;
		}

		GameplayEffectDefinition definition = effect.Definition;
		GameplayModifierHandle[] handles = effect.Modifiers!;
		int count = effect.ModifierCount;

		for (int index = 0; index < count; index++)
		{
			// Read the stack count per handle. SetModifierMagnitude
			// raises a notification, so a listener may stack the
			// instance again while this loop is running; the nested
			// refresh then writes the newer value. Re-reading here
			// prevents this outer pass from restoring an older one.
			_attributes.SetModifierMagnitude(
				handles[index],
				definition.GetStackedMagnitude(
					index,
					_effects[slot].StackCount));

			if (!IsLive(slot, generation))
			{
				return;
			}
		}
	}

	private void GrantTags(int slot)
	{
		ref ActiveEffect effect = ref _effects[slot];

		GameplayTagSet tags = effect.Definition.GrantedTags;

		if (tags.IsEmpty)
		{
			return;
		}

		Entity target = effect.Target;
		uint generation = effect.Generation;

		effect.TagsGranted = true;

		foreach (GameplayTag tag in tags.Tags)
		{
			if (!IsLive(slot, generation))
			{
				return;
			}

			_tags.AddOwnedTag(target, tag);
		}
	}

	private void RemoveGrantedTags(int slot)
	{
		if (!_effects[slot].TagsGranted)
		{
			return;
		}

		GameplayTagSet tags = _effects[slot].Definition.GrantedTags;
		Entity target = _effects[slot].Target;
		uint generation = _effects[slot].Generation;

		_effects[slot].TagsGranted = false;

		foreach (GameplayTag tag in tags.Tags)
		{
			// Cleanup runs after the instance has been marked dead, so
			// the guard checks slot identity rather than liveness: the
			// slot must not have been recycled underneath us. A destroyed
			// target's tag store may already have retired its generation.
			if (!IsSameSlot(slot, generation))
			{
				return;
			}

			if (!_world.IsAlive(target))
			{
				return;
			}

			_tags.RemoveOwnedTag(target, tag);
		}
	}

	private void SetInhibition(int slot, bool inhibit)
	{
		ref ActiveEffect effect = ref _effects[slot];

		uint generation = effect.Generation;

		effect.Inhibited = inhibit;

		if (inhibit)
		{
			RemoveModifiers(slot);

			// Removing a modifier raises an attribute notification. A
			// listener may remove this instance, and the slot may then
			// be handed to a replacement effect. Do not withdraw the
			// replacement's tags by looking the old index up again.
			if (!IsLive(slot, generation))
			{
				return;
			}

			RemoveGrantedTags(slot);

			if (IsLive(slot, generation))
			{
				RaiseChange(GameplayEffectChangeKind.Inhibited, slot);
			}

			return;
		}

		GrantTags(slot);

		if (!IsLive(slot, generation))
		{
			return;
		}

		AddModifiers(slot);

		if (!IsLive(slot, generation))
		{
			return;
		}

		GameplayEffectDefinition definition = _effects[slot].Definition;

		if (definition.IsPeriodic)
		{
			switch (definition.PeriodicInhibitionPolicy)
			{
				case GameplayEffectPeriodInhibitionPolicy.ResetPeriod:
					_effects[slot].NextPeriodTime = _time + definition.Period;
					Schedule(slot);
					break;

				case GameplayEffectPeriodInhibitionPolicy.ExecuteAndResetPeriod:
					ExecutePeriodicTick(slot);

					if (!IsLive(slot, generation))
					{
						return;
					}

					_effects[slot].NextPeriodTime = _time + definition.Period;
					Schedule(slot);
					break;
			}
		}

		if (IsLive(slot, generation))
		{
			RaiseChange(GameplayEffectChangeKind.Uninhibited, slot);
		}
	}

	private void EvaluateEffectState(int slot)
	{
		if (!_effects[slot].Active)
		{
			return;
		}

		GameplayEffectDefinition definition = _effects[slot].Definition;
		Entity target = _effects[slot].Target;

		if (!definition.RemovalRequirements.IsEmpty
			&& definition.RemovalRequirements.IsSatisfiedBy(_tags, target))
		{
			RemoveEffect(slot, GameplayEffectChangeKind.Removed);
			return;
		}

		bool shouldInhibit = !definition.OngoingRequirements.IsSatisfiedBy(
			_tags,
			target);

		if (shouldInhibit == _effects[slot].Inhibited)
		{
			return;
		}

		SetInhibition(slot, shouldInhibit);
	}

	private bool RemoveEffect(int slot, GameplayEffectChangeKind kind)
	{
		if (!_effects[slot].Active)
		{
			return false;
		}

		Entity target = _effects[slot].Target;
		GameplayEffectDefinition definition = _effects[slot].Definition;
		Entity source = _effects[slot].Source;
		int stackCount = _effects[slot].StackCount;
		GameplayEffectHandle handle = CreateHandle(slot);
		bool isInhibited = _effects[slot].Inhibited;

		// Mark the instance dead before any cleanup notification can
		// reach user code, so a handler that queries it sees it gone.
		_effects[slot].Active = false;

		Unlink(slot);

		_activeEffectCount--;

		Exception? failure = null;
		try
		{
			RemoveModifiers(slot);
		}
		catch (Exception exception)
		{
			failure = exception;
		}

		try
		{
			RemoveGrantedTags(slot);
		}
		catch (Exception exception)
		{
			failure = CombineFailures(failure, exception);
		}
		finally
		{
			ReleaseSlot(slot);
		}

		try
		{
			NotifyChange(new GameplayEffectChange(
				kind,
				handle,
				definition,
				target,
				source,
				stackCount,
				isInhibited));
		}
		catch (Exception exception)
		{
			failure = CombineFailures(failure, exception);
		}

		ThrowFailure(failure);
		return true;
	}

	private void HandleEntityDestroyed(Entity entity)
	{
		if (entity.AllocatorId != _allocatorId)
		{
			return;
		}

		int entityIndex = (int)entity.Index;
		int segmentIndex = entityIndex >> SegmentShift;
		if ((uint)segmentIndex >= (uint)_entitySegments.Length
			|| _entitySegments[segmentIndex] is not { } segment)
		{
			return;
		}

		int offset = entityIndex & SegmentMask;
		if (segment[offset].Generation != entity.Generation)
		{
			return;
		}

		int head = segment[offset].Head;
		int capacity = Math.Max(1, _entitySegments[segmentIndex]![offset].Count);
		GameplayEffectChange[] changes = new GameplayEffectChange[capacity];
		int changeCount = 0;
		Exception? failure = null;
		int slot = head;

		while (slot >= 0)
		{
			int next = _effects[slot].NextOnEntity;
			uint nextGeneration = next >= 0
				? _effects[next].Generation
				: 0;

			if (_effects[slot].Active)
			{
				GameplayEffectChange change = new(
					GameplayEffectChangeKind.TargetDestroyed,
					CreateHandle(slot),
					_effects[slot].Definition,
					_effects[slot].Target,
					_effects[slot].Source,
					_effects[slot].StackCount,
					_effects[slot].Inhibited);
				if (changeCount == changes.Length)
				{
					Array.Resize(ref changes, changes.Length * 2);
				}
				changes[changeCount++] = change;

				try
				{
					RemoveEffectSilently(slot);
				}
				catch (Exception exception)
				{
					failure = CombineFailures(failure, exception);
				}
			}

			slot = next;

			if (slot >= 0 && _effects[slot].Generation != nextGeneration)
			{
				// A cleanup handler recycled the next slot.
				break;
			}
		}

		// Cleanup handlers may have recycled the index and linked
		// new effects to the replacement. Never clear that new chain.
		if (TryGetEntitySlot(entity, out segmentIndex, out offset))
		{
			_entitySegments[segmentIndex]![offset].Head = -1;
			_entitySegments[segmentIndex]![offset].Count = 0;
		}
		else
		{
			// If a replacement already took the index, retire the old
			// generation's container without touching its new state.
			int retiredEntityIndex = (int)entity.Index;
			segmentIndex = retiredEntityIndex >> SegmentShift;
			if ((uint)segmentIndex < (uint)_entitySegments.Length
				&& _entitySegments[segmentIndex] is { } retiredSegment)
			{
				ref EntityEffects oldState = ref retiredSegment[retiredEntityIndex & SegmentMask];
				if (oldState.Generation == entity.Generation)
				{
					oldState.Head = -1;
					oldState.Count = 0;
				}
			}
		}

		for (int index = 0; index < changeCount; index++)
		{
			try
			{
				NotifyChange(changes[index]);
			}
			catch (Exception exception)
			{
				failure = CombineFailures(failure, exception);
			}
		}

		ThrowFailure(failure);
	}

	private void RemoveEffectSilently(int slot)
	{
		if (!_effects[slot].Active)
		{
			return;
		}

		_effects[slot].Active = false;

		Unlink(slot);

		_activeEffectCount--;

		Exception? failure = null;
		try
		{
			RemoveModifiers(slot);
		}
		catch (Exception exception)
		{
			failure = exception;
		}

		try
		{
			RemoveGrantedTags(slot);
		}
		catch (Exception exception)
		{
			failure = CombineFailures(failure, exception);
		}
		finally
		{
			ReleaseSlot(slot);
		}

		ThrowFailure(failure);
	}

	private void RetireStaleEntityEffects(
		ref EntityEffects slotState,
		uint replacementGeneration)
	{
		int slot = slotState.Head;
		slotState = new EntityEffects
		{
			Generation = replacementGeneration,
			Head = -1,
			Refreshing = false,
			RefreshAgain = false
		};

		Exception? failure = null;
		while (slot >= 0 && (uint)slot < (uint)_effectSlotCount)
		{
			uint generation = _effects[slot].Generation;
			int next = _effects[slot].NextOnEntity;
			uint nextGeneration = next >= 0
				? _effects[next].Generation
				: 0;

			if (_effects[slot].Active)
			{
				GameplayEffectChange change = new(
					GameplayEffectChangeKind.TargetDestroyed,
					CreateHandle(slot),
					_effects[slot].Definition,
					_effects[slot].Target,
					_effects[slot].Source,
					_effects[slot].StackCount,
					_effects[slot].Inhibited);

				try
				{
					RemoveEffectSilently(slot);
				}
				catch (Exception exception)
				{
					failure = CombineFailures(failure, exception);
				}

				try
				{
					NotifyChange(change);
				}
				catch (Exception exception)
				{
					failure = CombineFailures(failure, exception);
				}
			}

			slot = next;
			if (slot >= 0 && _effects[slot].Generation != nextGeneration)
			{
				// A cleanup or change callback recycled the next slot.
				break;
			}
		}

		ThrowFailure(failure);
	}

	private void ReleaseSlot(int slot)
	{
		// The modifier handle array outlives the instance so that a
		// steady-state apply/expire cycle reuses it instead of
		// allocating a new one for every application.
		GameplayModifierHandle[]? modifiers = _effects[slot].Modifiers;

		_effects[slot] = new ActiveEffect
		{
			Generation = NextGeneration(_effects[slot].Generation),
			NextOnEntity = _freeEffectSlot,
			Modifiers = modifiers
		};

		_freeEffectSlot = slot;
	}

	private void HandleTagsChanged(Entity entity)
	{
		RefreshEffectStates(entity);
	}

	private void RefreshEffectStates(Entity entity)
	{
		if (!TryGetEntitySlot(entity, out int segmentIndex, out int offset))
		{
			return;
		}

		if (_entitySegments[segmentIndex]![offset].Refreshing)
		{
			_entitySegments[segmentIndex]![offset].RefreshAgain = true;
			return;
		}

		_entitySegments[segmentIndex]![offset].Refreshing = true;

		try
		{
			for (int pass = 0; pass < MaxRefreshPasses; pass++)
			{
				if (!TryGetEntitySlot(entity, out segmentIndex, out offset))
				{
					return;
				}

				_entitySegments[segmentIndex]![offset].RefreshAgain = false;

				int slot = _entitySegments[segmentIndex]![offset].Head;

				while (slot >= 0)
				{
					if ((uint)slot >= (uint)_effectSlotCount
						|| !_effects[slot].Active
						|| _effects[slot].Target != entity)
					{
						break;
					}

					int next = _effects[slot].NextOnEntity;

					EvaluateEffectState(slot);

					slot = next;
				}

				if (!_entitySegments[segmentIndex]![offset].RefreshAgain)
				{
					return;
				}
			}
		}
		finally
		{
			if (TryGetEntitySlot(entity, out segmentIndex, out offset))
			{
				_entitySegments[segmentIndex]![offset].Refreshing = false;
				_entitySegments[segmentIndex]![offset].RefreshAgain = false;
			}
		}
	}

	private int FindStackableInstance(
		Entity target,
		GameplayEffectDefinition definition,
		Entity source)
	{
		if (definition.StackingType == GameplayEffectStackingType.None)
		{
			return -1;
		}

		if (!TryGetEntitySlot(target, out int segmentIndex, out int offset))
		{
			return -1;
		}

		int slot = _entitySegments[segmentIndex]![offset].Head;

		while (slot >= 0)
		{
			ref ActiveEffect effect = ref _effects[slot];

			if (effect.Active
				&& ReferenceEquals(effect.Definition, definition)
				&& (definition.StackingType
						== GameplayEffectStackingType.AggregateByTarget
					|| effect.Source == source))
			{
				return slot;
			}

			slot = effect.NextOnEntity;
		}

		return -1;
	}

	private void Schedule(int slot)
	{
		// Every reschedule leaves the previous entry behind to be
		// discarded when it is popped. A caller that stacks or
		// refreshes effects many times without advancing time would
		// otherwise grow the queue without bound, so compact it once
		// the waste passes the live population.
		if (_dueCount >= InitialDueCapacity
			&& _dueCount > _activeEffectCount * 2)
		{
			CompactDueQueue();
		}

		double due = Math.Min(
			_effects[slot].ExpireTime,
			_effects[slot].NextPeriodTime);

		if (!double.IsFinite(due))
		{
			_effects[slot].PendingOrder = 0;
			return;
		}

		long order = _nextOrder++;

		_effects[slot].PendingOrder = order;

		PushDue(due, order, slot);
	}

	private void PushDue(double time, long order, int slot)
	{
		if (_dueCount == _dueEntries.Length)
		{
			Array.Resize(ref _dueEntries, _dueEntries.Length * 2);
		}

		DueEntry entry = new()
		{
			Time = time,
			Order = order,
			Slot = slot,
			Generation = _effects[slot].Generation
		};

		int index = _dueCount++;

		_dueEntries[index] = entry;

		while (index > 0)
		{
			int parent = (index - 1) >> 1;

			if (!IsEarlier(entry, _dueEntries[parent]))
			{
				break;
			}

			_dueEntries[index] = _dueEntries[parent];
			_dueEntries[parent] = entry;
			index = parent;
		}
	}

	private DueEntry PopDue()
	{
		DueEntry result = _dueEntries[0];

		_dueCount--;

		if (_dueCount == 0)
		{
			return result;
		}

		DueEntry last = _dueEntries[_dueCount];
		int index = 0;

		while (true)
		{
			int left = (index * 2) + 1;

			if (left >= _dueCount)
			{
				break;
			}

			int right = left + 1;
			int child = right < _dueCount
				&& IsEarlier(_dueEntries[right], _dueEntries[left])
				? right
				: left;

			if (!IsEarlier(_dueEntries[child], last))
			{
				break;
			}

			_dueEntries[index] = _dueEntries[child];
			index = child;
		}

		_dueEntries[index] = last;

		return result;
	}

	private static bool IsEarlier(DueEntry left, DueEntry right)
	{
		if (left.Time != right.Time)
		{
			return left.Time < right.Time;
		}

		return left.Order < right.Order;
	}

	private void CompactDueQueueIfWasteful()
	{
		if (_dueCount <= InitialDueCapacity
			|| _dueCount <= _activeEffectCount * 2)
		{
			return;
		}

		CompactDueQueue();
	}

	private void CompactDueQueue()
	{
		_dueCount = 0;

		for (int slot = 0; slot < _effectSlotCount; slot++)
		{
			ref ActiveEffect effect = ref _effects[slot];

			if (!effect.Active)
			{
				continue;
			}

			double due = Math.Min(effect.ExpireTime, effect.NextPeriodTime);

			if (!double.IsFinite(due))
			{
				continue;
			}

			if (effect.PendingOrder == 0)
			{
				effect.PendingOrder = _nextOrder++;
			}

			PushDue(due, effect.PendingOrder, slot);
		}
	}

	private ActiveGameplayEffect Describe(int slot)
	{
		ref ActiveEffect effect = ref _effects[slot];

		return new ActiveGameplayEffect(
			CreateHandle(slot),
			effect.Definition,
			effect.Target,
			effect.Source,
			effect.StackCount,
			effect.Definition.IsInfinite
				? double.PositiveInfinity
				: Math.Max(0d, effect.ExpireTime - _time),
			double.IsFinite(effect.NextPeriodTime)
				? Math.Max(0d, effect.NextPeriodTime - _time)
				: double.PositiveInfinity,
			effect.Inhibited);
	}

	private GameplayEffectHandle CreateHandle(int slot)
	{
		ref ActiveEffect effect = ref _effects[slot];

		return new GameplayEffectHandle(
			_systemId,
			effect.Target,
			slot,
			effect.Generation);
	}

	private bool TryResolve(GameplayEffectHandle handle, out int slot)
	{
		slot = handle.Slot;

		if (handle.IsNone
			|| handle.SystemId != _systemId
			|| (uint)handle.Slot >= (uint)_effectSlotCount)
		{
			return false;
		}

		ref ActiveEffect effect = ref _effects[handle.Slot];

		return effect.Active
			&& effect.Generation == handle.Generation
			&& effect.Target == handle.Target;
	}

	private bool IsLive(int slot, uint generation)
	{
		return (uint)slot < (uint)_effectSlotCount
			&& _effects[slot].Active
			&& _effects[slot].Generation == generation;
	}

	private bool IsSameSlot(int slot, uint generation)
	{
		return (uint)slot < (uint)_effectSlotCount
			&& _effects[slot].Generation == generation;
	}

	private int AcquireEffectSlot()
	{
		int slot;

		if (_freeEffectSlot >= 0)
		{
			slot = _freeEffectSlot;
			_freeEffectSlot = _effects[slot].NextOnEntity;
		}
		else
		{
			if (_effectSlotCount == _effects.Length)
			{
				Array.Resize(
					ref _effects,
					Math.Max(InitialEffectCapacity, _effects.Length * 2));
			}

			slot = _effectSlotCount++;
		}

		ref ActiveEffect effect = ref _effects[slot];

		if (effect.Generation == 0)
		{
			effect.Generation = 1;
		}

		return slot;
	}

	private GameplayModifierHandle[] EnsureModifierCapacity(
		int slot,
		int required)
	{
		ref ActiveEffect effect = ref _effects[slot];

		if (effect.Modifiers is null)
		{
			effect.Modifiers = new GameplayModifierHandle[
				Math.Max(required, 2)];

			return effect.Modifiers;
		}

		if (effect.Modifiers.Length >= required)
		{
			return effect.Modifiers;
		}

		Array.Resize(
			ref effect.Modifiers,
			Math.Max(required, effect.Modifiers.Length * 2));

		return effect.Modifiers;
	}

	private void Link(Entity entity, int slot)
	{
		ref EntityEffects slotState = ref GetOrCreateEntitySlot(entity);

		int head = slotState.Head;

		_effects[slot].NextOnEntity = head;
		_effects[slot].PreviousOnEntity = -1;

		if (head >= 0)
		{
			_effects[head].PreviousOnEntity = slot;
		}

		slotState.Head = slot;
		slotState.Count++;
	}

	private void Unlink(int slot)
	{
		ref ActiveEffect effect = ref _effects[slot];

		Entity target = effect.Target;
		int previous = effect.PreviousOnEntity;
		int next = effect.NextOnEntity;
		bool hasSlot = TryGetEntitySlot(
			target,
			out int segmentIndex,
			out int offset);

		if (previous >= 0)
		{
			_effects[previous].NextOnEntity = next;
		}
		else if (hasSlot)
		{
			_entitySegments[segmentIndex]![offset].Head = next;
		}

		if (next >= 0)
		{
			_effects[next].PreviousOnEntity = previous;
		}

		if (hasSlot)
		{
			ref EntityEffects slotState = ref _entitySegments[segmentIndex]![offset];

			if (slotState.Count > 0)
			{
				slotState.Count--;
			}
		}

		_effects[slot].NextOnEntity = -1;
		_effects[slot].PreviousOnEntity = -1;
	}

	private ref EntityEffects GetOrCreateEntitySlot(Entity entity)
	{
		int entityIndex = (int)entity.Index;
		int segmentIndex = entityIndex >> SegmentShift;

		if (segmentIndex >= _entitySegments.Length)
		{
			Array.Resize(
				ref _entitySegments,
				Math.Max(segmentIndex + 1, _entitySegments.Length * 2));
		}

		EntityEffects[]? segment = _entitySegments[segmentIndex];

		if (segment is null)
		{
			segment = new EntityEffects[SegmentSize];
			_entitySegments[segmentIndex] = segment;
		}

		ref EntityEffects slot = ref segment[entityIndex & SegmentMask];

		if (slot.Generation != entity.Generation)
		{
			if (slot.Generation != 0)
			{
				RetireStaleEntityEffects(ref slot, entity.Generation);
			}
			else
			{
				slot.Generation = entity.Generation;
				slot.Head = -1;
				slot.Count = 0;
				slot.Refreshing = false;
				slot.RefreshAgain = false;
			}
		}

		return ref slot;
	}

	private bool TryGetEntitySlot(
		Entity entity,
		out int segmentIndex,
		out int offset)
	{
		segmentIndex = 0;
		offset = 0;

		if (entity.AllocatorId != _allocatorId)
		{
			return false;
		}

		int entityIndex = (int)entity.Index;
		segmentIndex = entityIndex >> SegmentShift;

		if (segmentIndex >= _entitySegments.Length)
		{
			return false;
		}

		EntityEffects[]? segment = _entitySegments[segmentIndex];

		if (segment is null)
		{
			return false;
		}

		offset = entityIndex & SegmentMask;

		return segment[offset].Generation == entity.Generation;
	}

	private void NotifyCueChange(in GameplayEffectChange change)
	{
		CueChanged?.Invoke(change);
	}

	private void RaiseChange(GameplayEffectChangeKind kind, int slot)
	{
		if (Changed is null && CueChanged is null)
		{
			return;
		}

		ref ActiveEffect effect = ref _effects[slot];

		if (!effect.Active)
		{
			return;
		}

		NotifyChange(new GameplayEffectChange(
			kind,
			CreateHandle(slot),
			effect.Definition,
			effect.Target,
			effect.Source,
			effect.StackCount,
			effect.Inhibited));
	}

	private void NotifyChange(in GameplayEffectChange change)
	{
		Exception? failure = null;
		try
		{
			Changed?.Invoke(change);
		}
		catch (Exception exception)
		{
			failure = exception;
		}

		try
		{
			CueChanged?.Invoke(change);
		}
		catch (Exception exception)
		{
			failure = CombineFailures(failure, exception);
		}

		ThrowFailure(failure);
	}

	private static Exception? CombineFailures(Exception? existing, Exception next)
	{
		return existing is null
			? next
			: new AggregateException(existing, next);
	}

	private static void ThrowFailure(Exception? failure)
	{
		if (failure is not null)
		{
			System.Runtime.ExceptionServices.ExceptionDispatchInfo
				.Capture(failure)
				.Throw();
		}
	}

	private static uint AllocateSystemId()
	{
		uint id = unchecked((uint)Interlocked.Increment(ref s_nextSystemId));
		return id == 0
			? unchecked((uint)Interlocked.Increment(ref s_nextSystemId))
			: id;
	}

	private static uint NextGeneration(uint generation)
	{
		generation++;

		return generation == 0 ? 1 : generation;
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}

	private struct ActiveEffect
	{
		public GameplayEffectDefinition Definition;
		public Entity Target;
		public Entity Source;
		public int StackCount;
		public double ExpireTime;
		public double NextPeriodTime;
		public GameplayModifierHandle[]? Modifiers;
		public int ModifierCount;
		public int NextOnEntity;
		public int PreviousOnEntity;
		public long PendingOrder;
		public uint Generation;
		public bool Active;
		public bool Inhibited;
		public bool TagsGranted;
	}

	private struct EntityEffects
	{
		public uint Generation;
		public int Head;
		public int Count;
		public bool Refreshing;
		public bool RefreshAgain;
	}

	private struct DueEntry
	{
		public double Time;
		public long Order;
		public int Slot;
		public uint Generation;
	}
}
