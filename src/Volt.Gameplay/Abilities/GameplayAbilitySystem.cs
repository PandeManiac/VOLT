using Volt.Ecs;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Events;
using Volt.Gameplay.Simulation;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tasks;

namespace Volt.Gameplay.Abilities;

/// <summary>
/// Grants abilities to the entities of one world and drives their
/// activation lifecycle.
/// </summary>
/// <remarks>
/// <para>
/// Grants live in a generational slot pool linked per owner, not in
/// managed objects. A grant stores only the definition, the owner, the
/// level, and the input binding; activation adds the target and an
/// active flag. Granting the same definition to a million entities
/// does not copy the definition.
/// </para>
/// <para>
/// Costs and cooldowns are gameplay effects, so the runtime delegates
/// all of their scheduling, stacking, and tag bookkeeping to
/// <see cref="GameplayEffectSystem"/>. Attribute affordability is read
/// from <see cref="GameplayAttributeStore"/>, and tag requirements from
/// <see cref="GameplayTagStore"/>.
/// </para>
/// <para>
/// There is no clock of its own: cooldowns end when the cooldown
/// effect expires, so advancing the simulation through the effect
/// system is enough. Time scaling and pausing therefore apply to
/// abilities exactly as they do to effects.
/// </para>
/// <para>
/// The system is single-threaded and owns no locks. Notifications are
/// delivered after each change is committed, and a handler may call
/// back into the system; every path re-resolves handles it holds
/// across a notification instead of trusting a slot index.
/// </para>
/// </remarks>
public sealed class GameplayAbilitySystem : IDisposable
{
	private const int SegmentShift = 10;
	private const int SegmentSize = 1 << SegmentShift;
	private const int SegmentMask = SegmentSize - 1;
	private const int InitialAbilityCapacity = 16;
	private const int MaxTagCancelPasses = 8;

	private static int s_nextSystemId;

	private readonly World _world;
	private readonly GameplayTagStore _tags;
	private readonly GameplayEffectSystem _effects;
	private readonly GameplayAttributeStore _attributes;
	private readonly uint _allocatorId;
	private readonly uint _systemId;

	private GameplayEventSystem? _events;
	private GameplayAbilityTaskSystem? _tasks;
	private bool _ownsEvents;

	private AbilitySlot[] _abilities = new AbilitySlot[InitialAbilityCapacity];
	private int _abilitySlotCount;
	private int _freeAbilitySlot = -1;
	private int _grantedCount;
	private int _activeCount;
	private uint _nextActivationId;

	private EntityAbilities[]?[] _entitySegments = [];

	private readonly CapturePool _cancelPool = new();
	private readonly CapturePool _tagCancelPool = new();
	private readonly CapturePool _inputPool = new();
	private readonly CapturePool _revokePool = new();
	private readonly Dictionary<string, GameplayAbilityTaskDefinition>
		_taskDefinitions = new(StringComparer.Ordinal);

	private SimulationClockClaim? _clockClaim;
	private bool _disposed;

	/// <summary>
	/// Creates an ability runtime over a world's gameplay stores.
	/// </summary>
	/// <param name="world">The world whose entities can hold abilities.</param>
	/// <param name="tags">The tag store used for requirements and granted tags.</param>
	/// <param name="effects">
	/// The effect system that applies costs and cooldowns and that owns
	/// their clocks.
	/// </param>
	/// <param name="attributes">
	/// The attribute store read to check whether a cost is affordable.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// An argument is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// The stores must have been created for the same world, and must
	/// outlive this system. Dispose this system before disposing them.
	/// </remarks>
	public GameplayAbilitySystem(
		World world,
		GameplayTagStore tags,
		GameplayEffectSystem effects,
		GameplayAttributeStore attributes,
		GameplayEventSystem? events = null)
	{
		ArgumentNullException.ThrowIfNull(world);
		ArgumentNullException.ThrowIfNull(tags);
		ArgumentNullException.ThrowIfNull(effects);
		ArgumentNullException.ThrowIfNull(attributes);

		_world = world;
		_tags = tags;
		_effects = effects;
		_attributes = attributes;
		_allocatorId = world.AllocatorId;
		_systemId = AllocateSystemId();
		_events = events;

		_world.EntityDestroyed += HandleEntityDestroyed;
		_tags.TagsChanged += HandleTagsChanged;
	}

	/// <summary>
	/// Occurs after an ability is granted, revoked, activated, ended,
	/// or cancelled.
	/// </summary>
	/// <remarks>
	/// The notification is raised after the change is committed, so a
	/// handler may query the system and may grant, revoke, activate, or
	/// cancel abilities. With no subscribers the cost is one null check
	/// per change.
	/// </remarks>
	public event Action<GameplayAbilityChange>? Changed;

	/// <summary>
	/// Gets the number of grants held by all entities.
	/// </summary>
	public int GrantedAbilityCount => _grantedCount;

	/// <summary>
	/// Gets the number of active abilities.
	/// </summary>
	public int ActiveAbilityCount => _activeCount;

	/// <summary>
	/// Gets the event runtime abilities use for plans and task waits.
	/// </summary>
	/// <remarks>
	/// When the constructor was given an event system, this is that
	/// system and the caller keeps ownership. Otherwise the first access
	/// creates one, and this system disposes it. Sharing one event
	/// system between the ability runtime and application listeners is
	/// the normal setup.
	/// </remarks>
	public GameplayEventSystem Events => _events ??= CreateEventSystem();

	/// <summary>
	/// Gets the task runtime for this ability system.
	/// </summary>
	/// <remarks>
	/// The task runtime is created on first use, so abilities that never
	/// start a task and have no plan never allocate one, never subscribe
	/// to tag changes, and never appear in the deadline heap.
	/// </remarks>
	public GameplayAbilityTaskSystem Tasks => _tasks ??= CreateTaskSystem();

	/// <summary>
	/// Registers a custom task that abilities of this system can wait
	/// on.
	/// </summary>
	/// <param name="name">
	/// A unique name, used for diagnostics and for identifying a
	/// pending task through
	/// <see cref="GameplayAbilityTaskInfo.CustomDefinition"/>.
	/// </param>
	/// <returns>The shared definition.</returns>
	/// <exception cref="ArgumentException">
	/// The name is empty or only whitespace.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The name is already registered.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Registering is initialization work. The definition is an
	/// identity: it carries no callback and no per-activation state, so
	/// any number of activations can share it.
	/// </para>
	/// <para>
	/// Application code completes a pending custom task through
	/// <see cref="GameplayAbilityTaskSystem.Complete"/> when the signal
	/// it owns fires, and observes cancellation through
	/// <see cref="GameplayAbilityTaskSystem.Changed"/>. Nothing polls
	/// the task.
	/// </para>
	/// </remarks>
	public GameplayAbilityTaskDefinition RegisterTaskDefinition(
		string name)
	{
		ThrowIfDisposed();

		if (string.IsNullOrWhiteSpace(name))
		{
			throw new ArgumentException(
				"A custom task definition needs a non-empty name.",
				nameof(name));
		}

		if (_taskDefinitions.ContainsKey(name))
		{
			throw new InvalidOperationException(
				$"A custom task named '{name}' is already registered " +
				"on this ability system. Reuse the existing definition.");
		}

		GameplayAbilityTaskDefinition definition = new(
			this,
			_taskDefinitions.Count + 1,
			name);

		_taskDefinitions.Add(name, definition);

		return definition;
	}

	/// <summary>
	/// Grants an ability to an entity.
	/// </summary>
	/// <param name="owner">The living entity that receives the ability.</param>
	/// <param name="definition">The shared ability definition.</param>
	/// <param name="level">
	/// The grant level, which starts at one. It is reported on the
	/// grant and its notifications; the definitions of this milestone
	/// have constant magnitudes, so the level does not scale them.
	/// </param>
	/// <param name="inputId">
	/// The input binding, or zero for an ability that is not bound to
	/// one.
	/// </param>
	/// <returns>The handle of the new grant.</returns>
	/// <exception cref="ArgumentNullException">
	/// The definition is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// The owner is not alive in this system's world.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The level is less than one.
	/// </exception>
	/// <remarks>
	/// Granting the same definition twice creates two independent
	/// grants, exactly like Unreal's <c>GiveAbility</c>, which adds a
	/// new spec each time it is called.
	/// </remarks>
	public GameplayAbilityHandle GiveAbility(
		Entity owner,
		GameplayAbilityDefinition definition,
		int level = 1,
		int inputId = 0)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(definition);

		if (!_world.IsAlive(owner))
		{
			throw new ArgumentException(
				"The entity is not alive in this system's world. An " +
				"ability cannot be granted to a destroyed or recycled " +
				"handle.",
				nameof(owner));
		}

		if (level < 1)
		{
			throw new ArgumentOutOfRangeException(
				nameof(level),
				level,
				"An ability level starts at one.");
		}

		int slot = AcquireAbilitySlot();

		_abilities[slot].Definition = definition;
		_abilities[slot].Owner = owner;
		_abilities[slot].Level = level;
		_abilities[slot].InputId = inputId;
		_abilities[slot].Target = default;
		_abilities[slot].Active = false;
		_abilities[slot].Activating = false;
		_abilities[slot].Revoking = false;
		_abilities[slot].Ending = false;
		_abilities[slot].ActivationId = 0;
		_abilities[slot].TaskHead = -1;
		_abilities[slot].PlanStep = -1;

		Link(owner, slot);

		_grantedCount++;

		GameplayAbilityHandle handle = CreateHandle(slot);

		RaiseChange(GameplayAbilityChangeKind.Granted, slot);

		return handle;
	}

	/// <summary>
	/// Revokes one grant.
	/// </summary>
	/// <param name="handle">The handle of the grant.</param>
	/// <returns>
	/// <see langword="true"/> when this call removed the grant;
	/// <see langword="false"/> when it had already been revoked, its
	/// owner is gone, or it belongs to another system.
	/// </returns>
	/// <remarks>
	/// An active ability is cancelled before the grant is removed, so
	/// listeners see <see cref="GameplayAbilityChangeKind.Cancelled"/>
	/// followed by <see cref="GameplayAbilityChangeKind.Revoked"/>.
	/// Unreal's <c>ClearAbility</c> also cancels first. While the call
	/// is in progress the grant is marked as revoking: a nested
	/// revocation returns <see langword="false"/>, and an activation
	/// reports <see cref="GameplayAbilityActivationStatus.NotGranted"/>,
	/// so a cancellation handler cannot resurrect it.
	/// </remarks>
	public bool RevokeAbility(GameplayAbilityHandle handle)
	{
		ThrowIfDisposed();

		if (!TryResolve(handle, out int slot)
			|| _abilities[slot].Revoking
			|| _abilities[slot].Ending)
		{
			return false;
		}

		// Mark the grant as being revoked before its cancellation
		// notification can reach user code. A handler that tries to
		// reactivate it, or to revoke it again, is refused instead of
		// leaving an active grant to be released without cleanup.
		_abilities[slot].Revoking = true;

		try
		{
			if (_abilities[slot].Active)
			{
				EndActivation(slot, GameplayAbilityChangeKind.Cancelled);

				if (!TryResolve(handle, out slot))
				{
					return false;
				}
			}

			RevokeSlot(slot);

			return true;
		}
		finally
		{
			if (TryResolve(handle, out slot))
			{
				_abilities[slot].Revoking = false;
			}
		}
	}

	/// <summary>
	/// Revokes every grant an entity holds.
	/// </summary>
	/// <param name="owner">The entity.</param>
	/// <returns>The number of grants this call removed.</returns>
	/// <remarks>
	/// The call captures the grants that exist when it starts. Abilities
	/// granted by a notification raised during the call are not revoked
	/// by the same call, which bounds the work even if a handler grants
	/// a replacement every time one is removed.
	/// </remarks>
	public int RevokeAll(Entity owner)
	{
		ThrowIfDisposed();

		int capacity = GetAbilityCount(owner);

		if (capacity == 0)
		{
			return 0;
		}

		// Capture the grants that exist when the call starts. A
		// notification raised by a revocation may grant a replacement;
		// that replacement is not part of this call's scope, even
		// though it is pushed onto the same owner list.
		CapturedAbility[] captured = _revokePool.Rent(capacity);

		try
		{
			int count = CaptureGrants(owner, captured);
			int removed = 0;

			for (int index = 0; index < count; index++)
			{
				if (RevokeAbility(captured[index].Handle))
				{
					removed++;
				}
			}

			return removed;
		}
		finally
		{
			_revokePool.Return();
		}
	}

	/// <summary>
	/// Copies a snapshot of one grant.
	/// </summary>
	/// <param name="handle">The handle of the grant.</param>
	/// <param name="ability">The snapshot.</param>
	/// <returns>
	/// <see langword="true"/> when the handle refers to a live grant.
	/// </returns>
	public bool TryGetAbility(
		GameplayAbilityHandle handle,
		out GrantedGameplayAbility ability)
	{
		ThrowIfDisposed();

		if (!TryResolve(handle, out int slot))
		{
			ability = default;
			return false;
		}

		ability = Describe(slot);

		return true;
	}

	/// <summary>
	/// Gets the number of grants an entity holds, active or not.
	/// </summary>
	/// <param name="owner">The entity.</param>
	public int GetAbilityCount(Entity owner)
	{
		ThrowIfDisposed();

		return TryGetEntitySlot(owner, out int segmentIndex, out int offset)
			? _entitySegments[segmentIndex]![offset].Count
			: 0;
	}

	/// <summary>
	/// Gets the number of active abilities an entity has.
	/// </summary>
	/// <param name="owner">The entity.</param>
	public int GetActiveAbilityCount(Entity owner)
	{
		ThrowIfDisposed();

		return TryGetEntitySlot(owner, out int segmentIndex, out int offset)
			? _entitySegments[segmentIndex]![offset].ActiveCount
			: 0;
	}

	/// <summary>
	/// Copies the handles of an entity's grants.
	/// </summary>
	/// <param name="owner">The entity.</param>
	/// <param name="destination">The destination span.</param>
	/// <returns>
	/// The number of handles written, which is less than
	/// <see cref="GetAbilityCount"/> when the span is too small.
	/// </returns>
	public int CopyAbilities(
		Entity owner,
		Span<GameplayAbilityHandle> destination)
	{
		ThrowIfDisposed();

		if (!TryGetEntitySlot(owner, out int segmentIndex, out int offset))
		{
			return 0;
		}

		int written = 0;
		int slot = _entitySegments[segmentIndex]![offset].Head;

		while (slot >= 0 && written < destination.Length)
		{
			destination[written++] = CreateHandle(slot);

			slot = _abilities[slot].NextOnEntity;
		}

		return written;
	}

	/// <summary>
	/// Finds the first grant of a definition on an entity.
	/// </summary>
	/// <param name="owner">The entity.</param>
	/// <param name="definition">The definition.</param>
	/// <param name="ability">The snapshot of the first match.</param>
	/// <returns>
	/// <see langword="true"/> when the entity holds the ability.
	/// </returns>
	public bool TryFindAbility(
		Entity owner,
		GameplayAbilityDefinition definition,
		out GrantedGameplayAbility ability)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(definition);

		if (TryGetEntitySlot(owner, out int segmentIndex, out int offset))
		{
			int slot = _entitySegments[segmentIndex]![offset].Head;

			while (slot >= 0)
			{
				ref AbilitySlot candidate = ref _abilities[slot];

				if (ReferenceEquals(candidate.Definition, definition))
				{
					ability = Describe(slot);
					return true;
				}

				slot = candidate.NextOnEntity;
			}
		}

		ability = default;
		return false;
	}

	/// <summary>
	/// Finds the first grant bound to an input.
	/// </summary>
	/// <param name="owner">The entity.</param>
	/// <param name="inputId">The input binding.</param>
	/// <param name="handle">The handle of the first matching grant.</param>
	/// <returns>
	/// <see langword="true"/> when a grant is bound to the input.
	/// </returns>
	public bool TryFindAbilityByInput(
		Entity owner,
		int inputId,
		out GameplayAbilityHandle handle)
	{
		ThrowIfDisposed();

		if (TryGetEntitySlot(owner, out int segmentIndex, out int offset))
		{
			int slot = _entitySegments[segmentIndex]![offset].Head;

			while (slot >= 0)
			{
				ref AbilitySlot candidate = ref _abilities[slot];

				if (candidate.InputId == inputId)
				{
					handle = CreateHandle(slot);
					return true;
				}

				slot = candidate.NextOnEntity;
			}
		}

		handle = GameplayAbilityHandle.None;
		return false;
	}

	/// <summary>
	/// Attempts to activate a granted ability.
	/// </summary>
	/// <param name="handle">The handle of the grant.</param>
	/// <param name="target">
	/// The entity the ability is used against, or
	/// <c>default(Entity)</c> for an untargeted activation.
	/// </param>
	/// <returns>The outcome of the attempt.</returns>
	/// <remarks>
	/// <para>
	/// The checks run in Unreal's order: cooldown, cost, tag
	/// requirements, then the already-active rule. Cooldowns are read
	/// from the owner's tags, costs from the attribute store, and tag
	/// requirements from the owner and the target, so all three are
	/// gameplay queries rather than stored activation state.
	/// </para>
	/// <para>
	/// A committed activation marks the ability active, cancels active
	/// abilities whose asset tags match
	/// <see cref="GameplayAbilityDefinition.CancelAbilitiesWithTags"/>,
	/// then applies the cooldown and the cost before raising
	/// <see cref="GameplayAbilityChangeKind.Activated"/>. A handler
	/// therefore observes the granted cooldown tags and the paid cost.
	/// </para>
	/// <para>
	/// The cooldown is reversible; the cost is the commit point. A
	/// cooldown that refuses to apply aborts before payment, and a cost
	/// that refuses to apply or is no longer affordable rolls the
	/// cooldown back. A notification that ends the activation after the
	/// cooldown was applied but before the cost is paid rolls the
	/// cooldown back too, because the ability never bought it. A failed
	/// commit raises no ability notification and reports
	/// <see cref="GameplayAbilityActivationStatus.RejectedByCost"/> or
	/// <see cref="GameplayAbilityActivationStatus.RejectedByCooldown"/>.
	/// Once the cost is paid or <see cref="GameplayAbilityChangeKind.Activated"/>
	/// is raised, the cooldown is retained and an activation ended by a
	/// later notification reports
	/// <see cref="GameplayAbilityActivationStatus.EndedDuringActivation"/>.
	/// </para>
	/// <para>
	/// An activation without a handle that still resolves is reported
	/// as <see cref="GameplayAbilityActivationStatus.NotGranted"/>,
	/// not thrown: a stale handle is a gameplay race, not a
	/// programming error. A nested activation of the grant that is
	/// already being activated reports
	/// <see cref="GameplayAbilityActivationStatus.AlreadyActive"/>.
	/// </para>
	/// </remarks>
	public GameplayAbilityActivationResult TryActivate(
		GameplayAbilityHandle handle,
		Entity target = default)
	{
		ThrowIfDisposed();

		if (!TryResolve(handle, out int slot))
		{
			return Rejected(
				GameplayAbilityActivationStatus.NotGranted,
				handle,
				handle.Owner,
				default,
				0);
		}

		if (_abilities[slot].Revoking)
		{
			return Rejected(
				GameplayAbilityActivationStatus.NotGranted,
				handle,
				_abilities[slot].Owner,
				target,
				_abilities[slot].Level);
		}

		if (_abilities[slot].Activating || _abilities[slot].Ending)
		{
			return Rejected(
				GameplayAbilityActivationStatus.AlreadyActive,
				handle,
				_abilities[slot].Owner,
				target,
				_abilities[slot].Level);
		}

		GameplayAbilityDefinition definition = _abilities[slot].Definition;
		Entity owner = _abilities[slot].Owner;
		int level = _abilities[slot].Level;

		if (definition.CooldownEffect is not null)
		{
			if (_tags.HasAny(owner, definition.CooldownTags))
			{
				return Rejected(
					GameplayAbilityActivationStatus.RejectedByCooldown,
					handle,
					owner,
					target,
					level);
			}

			// The cooldown is part of what activation buys. If its
			// own requirements are unmet, applying it would silently
			// leave the ability without a cooldown, so reject up front.
			if (!definition.CooldownEffect.ApplicationRequirements
				.IsSatisfiedBy(_tags, owner))
			{
				return Rejected(
					GameplayAbilityActivationStatus.RejectedByCooldown,
					handle,
					owner,
					target,
					level);
			}
		}

		if (!CostIsMet(owner, definition.CostEffect))
		{
			return Rejected(
				GameplayAbilityActivationStatus.RejectedByCost,
				handle,
				owner,
				target,
				level);
		}

		if (!definition.ActivationRequirements.IsSatisfiedBy(_tags, owner))
		{
			return Rejected(
				GameplayAbilityActivationStatus.RejectedByRequirements,
				handle,
				owner,
				target,
				level);
		}

		if (!TargetRequirementsAreMet(definition.TargetRequirements, target))
		{
			return Rejected(
				GameplayAbilityActivationStatus.RejectedByRequirements,
				handle,
				owner,
				target,
				level);
		}

		if (IsBlockedByActiveAbility(owner, definition.AssetTags, slot))
		{
			return Rejected(
				GameplayAbilityActivationStatus.BlockedByActiveAbility,
				handle,
				owner,
				target,
				level);
		}

		// The guard is set before a retrigger ends the current
		// instance, so a handler cannot start a nested activation
		// between the end and the restart.
		_abilities[slot].Activating = true;

		try
		{
			if (_abilities[slot].Active)
			{
				if (!definition.CanRetrigger)
				{
					return Rejected(
						GameplayAbilityActivationStatus.AlreadyActive,
						handle,
						owner,
						target,
						level);
				}

				// Unreal ends the running instance normally and then
				// starts a fresh activation.
				EndActivation(slot, GameplayAbilityChangeKind.Ended);

				if (!TryResolve(handle, out slot))
				{
					return Rejected(
						GameplayAbilityActivationStatus.NotGranted,
						handle,
						owner,
						target,
						level);
				}
			}

			try
			{
				return CommitActivation(
					handle,
					slot,
					definition,
					owner,
					target,
					level);
			}
			catch (Exception activationException)
			{
				// A notification raised during the commit may throw after
				// a cost, cooldown, or activation tag was committed. The
				// activation must not stay active without a plan cursor
				// or a task that could ever continue it. Cancelling is the
				// same terminal state a failed plan uses; committed costs
				// and cooldowns are deliberately not rolled back.
				try
				{
					CancelFailedActivation(handle);
				}
				catch (Exception cleanupException)
				{
					throw new AggregateException(
						"An ability activation notification failed and " +
						"cleanup also failed. Both exceptions are reported; " +
						"the activation was marked inactive before cleanup " +
						"notifications ran.",
						activationException,
						cleanupException);
				}

				throw;
			}
		}
		finally
		{
			if (TryResolve(handle, out slot))
			{
				_abilities[slot].Activating = false;
			}
		}
	}

	private GameplayAbilityActivationResult CommitActivation(
		GameplayAbilityHandle handle,
		int slot,
		GameplayAbilityDefinition definition,
		Entity owner,
		Entity target,
		int level)
	{
		uint activationId = NextActivationId();

		_abilities[slot].Active = true;
		_abilities[slot].Target = target;
		_abilities[slot].ActivationId = activationId;
		_abilities[slot].PlanStep = -1;

		_activeCount++;

		GetOrCreateEntitySlot(owner).ActiveCount++;

		if (!definition.CancelAbilitiesWithTags.IsEmpty)
		{
			CancelMatching(owner, definition.CancelAbilitiesWithTags, slot);
		}

		if (!IsStillActive(handle, owner, out slot))
		{
			return Rejected(
				GameplayAbilityActivationStatus.EndedDuringActivation,
				handle,
				owner,
				target,
				level);
		}

		GameplayEffectHandle cooldown = GameplayEffectHandle.None;
		GameplayEffectApplicationRollback cooldownRollback = default;

		if (definition.CooldownEffect is not null)
		{
			GameplayEffectApplyResult applied = _effects.Apply(
				owner,
				definition.CooldownEffect,
				owner,
				out cooldownRollback);

			if (!CooldownBecameActive(owner, definition, applied))
			{
				if (IsStillActive(handle, owner, out slot))
				{
					AbortActivation(slot);
				}

				return Rejected(
					GameplayAbilityActivationStatus.RejectedByCooldown,
					handle,
					owner,
					target,
					level);
			}

			cooldown = applied.Handle;

			if (!IsStillActive(handle, owner, out slot))
			{
				// The cooldown was reversible until the cost was paid.
				// A notification that ended the activation before that
				// point must not leave the owner cooling down for an
				// ability that never committed.
				RollbackCooldown(cooldown, cooldownRollback);

				return Rejected(
					GameplayAbilityActivationStatus.EndedDuringActivation,
					handle,
					owner,
					target,
					level);
			}
		}

		if (definition.CostEffect is not null)
		{
			// Cancellation callbacks and the cooldown's modifiers may
			// have spent or changed the resource after the initial
			// check. Re-check immediately before the irreversible
			// payment, while the cooldown can still be rolled back.
			if (!CostIsMet(owner, definition.CostEffect))
			{
				if (IsStillActive(handle, owner, out slot))
				{
					AbortActivation(slot);
				}

				RollbackCooldown(cooldown, cooldownRollback);

				return Rejected(
					GameplayAbilityActivationStatus.RejectedByCost,
					handle,
					owner,
					target,
					level);
			}

			GameplayEffectApplyResult applied = _effects.Apply(
				owner,
				definition.CostEffect,
				owner);

			if (applied.Status != GameplayEffectApplyStatus.InstantExecuted)
			{
				// The cost was the commit point. Paying it is the last
				// irreversible step, so a rejected cost rolls the
				// reversible cooldown back and ends the attempt.
				if (IsStillActive(handle, owner, out slot))
				{
					AbortActivation(slot);
				}

				RollbackCooldown(cooldown, cooldownRollback);

				return Rejected(
					GameplayAbilityActivationStatus.RejectedByCost,
					handle,
					owner,
					target,
					level);
			}

			if (!IsStillActive(handle, owner, out slot))
			{
				return Rejected(
					GameplayAbilityActivationStatus.EndedDuringActivation,
					handle,
					owner,
					target,
					level);
			}
		}

		RaiseChange(GameplayAbilityChangeKind.Activated, slot);

		if (!IsStillActive(handle, owner, out slot))
		{
			return new GameplayAbilityActivationResult(
				GameplayAbilityActivationStatus.EndedDuringActivation,
				handle,
				owner,
				target,
				level,
				activationId);
		}

		if (definition.EndsOnActivate)
		{
			EndActivation(slot, GameplayAbilityChangeKind.Ended);

			return new GameplayAbilityActivationResult(
				GameplayAbilityActivationStatus.ActivatedAndEnded,
				handle,
				owner,
				target,
				level,
				activationId);
		}

		bool endedByPlan = false;

		if (definition.Plan is not null)
		{
			endedByPlan = RunPlanGuarded(
				new GameplayAbilityActivation(handle, activationId),
				0);

			if (!IsStillActive(handle, owner, out slot))
			{
				return new GameplayAbilityActivationResult(
					endedByPlan
						? GameplayAbilityActivationStatus.ActivatedAndEnded
						: GameplayAbilityActivationStatus.EndedDuringActivation,
					handle,
					owner,
					target,
					level,
					activationId);
			}
		}

		return new GameplayAbilityActivationResult(
			GameplayAbilityActivationStatus.Activated,
			handle,
			owner,
			target,
			level,
			activationId);
	}

	private bool CooldownBecameActive(
		Entity owner,
		GameplayAbilityDefinition definition,
		GameplayEffectApplyResult applied)
	{
		if (applied.Status is not (
			GameplayEffectApplyStatus.Applied
			or GameplayEffectApplyStatus.Stacked))
		{
			return false;
		}

		// The cooldown check reads tags, so another effect may have
		// supplied the same tag while this instance was being applied.
		// An instance that is active but inhibited has no tag yet and
		// still satisfies the requirement; a handler that removed the
		// instance leaves neither a tag nor a live handle.
		return _tags.HasAny(owner, definition.CooldownTags)
			|| (!applied.Handle.IsNone
				&& _effects.TryGetEffect(applied.Handle, out _));
	}

	/// <summary>
	/// Attempts the activation bound to an input.
	/// </summary>
	/// <param name="owner">The entity.</param>
	/// <param name="inputId">The input binding.</param>
	/// <returns>
	/// The number of grants bound to the input that activated.
	/// </returns>
	/// <remarks>
	/// Every grant bound to the input when the call starts is attempted,
	/// most recently granted first — the order the per-owner list is
	/// stored in. A grant added by a notification is not part of that
	/// press, and a nested input call does not share its scope.
	/// </remarks>
	public int TryActivateByInput(Entity owner, int inputId)
	{
		ThrowIfDisposed();

		int capacity = GetAbilityCount(owner);

		if (capacity == 0)
		{
			return 0;
		}

		// Capture the grants that exist when the call starts. A grant
		// added by a notification is not part of this input press, so
		// it cannot consume an attempt that belongs to an older grant.
		CapturedAbility[] captured = _inputPool.Rent(capacity);

		try
		{
			int count = CaptureInputGrants(owner, inputId, captured);
			int activated = 0;

			for (int index = 0; index < count; index++)
			{
				if (TryActivate(captured[index].Handle).Succeeded)
				{
					activated++;
				}
			}

			return activated;
		}
		finally
		{
			_inputPool.Return();
		}
	}

	/// <summary>
	/// Ends an active ability normally.
	/// </summary>
	/// <param name="handle">The handle of the grant.</param>
	/// <returns>
	/// <see langword="true"/> when this call ended the ability.
	/// </returns>
	public bool End(GameplayAbilityHandle handle)
	{
		ThrowIfDisposed();

		if (!TryResolve(handle, out int slot) || !_abilities[slot].Active)
		{
			return false;
		}

		EndActivation(slot, GameplayAbilityChangeKind.Ended);

		return true;
	}

	/// <summary>
	/// Cancels an active ability.
	/// </summary>
	/// <param name="handle">The handle of the grant.</param>
	/// <returns>
	/// <see langword="true"/> when this call cancelled the ability.
	/// </returns>
	public bool Cancel(GameplayAbilityHandle handle)
	{
		ThrowIfDisposed();

		if (!TryResolve(handle, out int slot) || !_abilities[slot].Active)
		{
			return false;
		}

		EndActivation(slot, GameplayAbilityChangeKind.Cancelled);

		return true;
	}

	/// <summary>
	/// Cancels every active ability an entity has.
	/// </summary>
	/// <param name="owner">The entity.</param>
	/// <returns>The number of abilities this call cancelled.</returns>
	/// <remarks>
	/// The call captures the abilities that are active when it starts.
	/// An ability activated by a <see cref="GameplayAbilityChangeKind.Cancelled"/>
	/// notification is a new activation and survives the call.
	/// </remarks>
	public int CancelAll(Entity owner)
	{
		ThrowIfDisposed();

		return CancelMatching(owner, null, -1);
	}

	/// <summary>
	/// Cancels the active abilities whose asset tags match a set.
	/// </summary>
	/// <param name="owner">The entity.</param>
	/// <param name="tags">The asset tags to match.</param>
	/// <returns>The number of abilities this call cancelled.</returns>
	/// <remarks>
	/// Matching is hierarchical, so cancelling <c>Ability</c> also
	/// cancels an ability whose asset tag is <c>Ability.Dash</c>. Like
	/// <see cref="CancelAll"/>, the call captures the active abilities
	/// when it starts; one activated by a notification survives.
	/// </remarks>
	public int CancelByTags(Entity owner, GameplayTagSet tags)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(tags);

		return CancelMatching(owner, tags, -1);
	}

	/// <summary>
	/// Determines whether a grant is currently active.
	/// </summary>
	/// <param name="handle">The handle of the grant.</param>
	public bool IsActive(GameplayAbilityHandle handle)
	{
		ThrowIfDisposed();

		return TryResolve(handle, out int slot) && _abilities[slot].Active;
	}

	/// <summary>
	/// Gets the token of the grant's current activation.
	/// </summary>
	/// <param name="handle">The handle of the grant.</param>
	/// <param name="activation">The activation token.</param>
	/// <returns>
	/// <see langword="true"/> when the grant is currently active.
	/// </returns>
	/// <remarks>
	/// Tasks and plans bind to this token rather than to the grant, so
	/// a later activation of the same grant is a different owner. A
	/// handler that started before a retrigger keeps the old token and
	/// cannot affect the new activation through it.
	/// </remarks>
	public bool TryGetActivation(
		GameplayAbilityHandle handle,
		out GameplayAbilityActivation activation)
	{
		ThrowIfDisposed();

		if (!TryResolve(handle, out int slot) || !_abilities[slot].Active)
		{
			activation = GameplayAbilityActivation.None;
			return false;
		}

		activation = new GameplayAbilityActivation(
			CreateHandle(slot),
			_abilities[slot].ActivationId);

		return true;
	}

	/// <summary>
	/// Determines whether an ability is on cooldown for an entity.
	/// </summary>
	/// <param name="owner">The entity.</param>
	/// <param name="definition">The ability definition.</param>
	/// <remarks>
	/// The check reads the owner's tags, so any effect that grants a
	/// cooldown tag puts the ability on cooldown — not only this
	/// ability's own cooldown effect. That is Unreal's
	/// <c>CheckCooldown</c>, which tests the owner for any matching
	/// tag.
	/// </remarks>
	public bool IsOnCooldown(
		Entity owner,
		GameplayAbilityDefinition definition)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(definition);

		return definition.CooldownEffect is not null
			&& _tags.HasAny(owner, definition.CooldownTags);
	}

	/// <summary>
	/// Gets how long the owner's cooldown for an ability has left.
	/// </summary>
	/// <param name="owner">The entity.</param>
	/// <param name="definition">The ability definition.</param>
	/// <returns>
	/// The longest remaining duration among active effects that grant
	/// the cooldown tags, zero when none is active, or infinity for an
	/// infinite cooldown.
	/// </returns>
	/// <remarks>
	/// Unreal queries every active effect that grants the cooldown
	/// tags with <c>MakeQuery_MatchAnyOwningTags</c> and takes the
	/// longest remaining duration. VOLT asks the effect system for the
	/// same maximum, so an unrelated effect that grants a cooldown tag
	/// contributes its duration instead of being invisible to the
	/// query. A tag present without an effect behind it still blocks
	/// activation but has no duration to report, exactly as in Unreal.
	/// An inhibited instance still reports its remaining time, because
	/// <c>GetActiveEffectsTimeRemaining</c> ignores inhibition.
	/// </remarks>
	public double GetCooldownTimeRemaining(
		Entity owner,
		GameplayAbilityDefinition definition)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(definition);

		return definition.CooldownEffect is null
			? 0d
			: _effects.GetMaxRemainingDuration(owner, definition.CooldownTags);
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

		if (_tasks is not null)
		{
			_tasks.Changed -= HandleTaskChanged;
			_tasks.Dispose();
			_tasks = null;
		}

		if (_ownsEvents)
		{
			_events?.Dispose();
		}

		_events = null;
		_ownsEvents = false;
		_clockClaim = null;
		Changed = null;
		_abilities = [];
		_entitySegments = [];
		_cancelPool.Clear();
		_tagCancelPool.Clear();
		_inputPool.Clear();
		_revokePool.Clear();
		_taskDefinitions.Clear();
		_abilitySlotCount = 0;
		_freeAbilitySlot = -1;
		_grantedCount = 0;
		_activeCount = 0;
		_nextActivationId = 0;
		_disposed = true;

		GC.SuppressFinalize(this);
	}

	private GameplayEventSystem CreateEventSystem()
	{
		_events = new GameplayEventSystem(_world);
		_ownsEvents = true;

		return _events;
	}

	private GameplayAbilityTaskSystem CreateTaskSystem()
	{
		GameplayAbilityTaskSystem tasks = new(this, Events, _tags);

		tasks.Changed += HandleTaskChanged;

		if (_clockClaim is not null)
		{
			tasks.BeginSimulation(_clockClaim);
		}

		return tasks;
	}

	/// <summary>
	/// Claims task advancement for an attached fixed-step integration.
	/// </summary>
	/// <param name="claim">The claiming integration's identity.</param>
	/// <exception cref="InvalidOperationException">
	/// Another integration already owns the clock.
	/// </exception>
	/// <remarks>
	/// A task runtime created later inherits the same claim, so an
	/// ability that first uses a task after registration is still
	/// advanced by the integration and not by hand.
	/// </remarks>
	internal void BeginSimulation(SimulationClockClaim claim)
	{
		ArgumentNullException.ThrowIfNull(claim);

		if (_clockClaim is not null)
		{
			throw new InvalidOperationException(
				"These abilities are already advanced by another " +
				"GameplaySimulation. A clock has one owner.");
		}

		_clockClaim = claim;
		_tasks?.BeginSimulation(claim);
	}

	/// <summary>
	/// Releases task advancement when its integration is disposed.
	/// </summary>
	/// <param name="claim">The releasing integration's identity.</param>
	/// <remarks>
	/// A claim these abilities do not hold is left alone, so an
	/// integration that failed to acquire them cannot detach the owner.
	/// </remarks>
	internal void EndSimulation(SimulationClockClaim claim)
	{
		if (!ReferenceEquals(_clockClaim, claim))
		{
			return;
		}

		_clockClaim = null;
		_tasks?.EndSimulation(claim);
	}

	/// <summary>
	/// Advances pending tasks from the attached integration without
	/// creating a task runtime for an ability system that never used
	/// one.
	/// </summary>
	internal void AdvanceTasksFromSimulation(float deltaSeconds)
	{
		if (_tasks is not null && !_tasks.IsDisposed)
		{
			_tasks.AdvanceFromSimulation(deltaSeconds);
		}
	}

	internal World World => _world;

	/// <summary>
	/// Gets the effect runtime this system applies costs, cooldowns, and
	/// plan effects through.
	/// </summary>
	/// <remarks>
	/// A fixed-step integration must advance this exact instance.
	/// Advancing a different effect system in the same world would let
	/// cooldowns expire on a clock nothing else uses.
	/// </remarks>
	internal GameplayEffectSystem EffectRuntime => _effects;

	internal bool TryResolveActivation(
		GameplayAbilityActivation activation,
		out int slot,
		out Entity owner,
		out Entity target)
	{
		slot = -1;
		owner = default;
		target = default;

		if (activation.IsNone
			|| !TryResolveGrant(activation.Ability, out slot, out owner))
		{
			return false;
		}

		ref AbilitySlot ability = ref _abilities[slot];

		if (!ability.Active || ability.ActivationId != activation.Id)
		{
			return false;
		}

		target = ability.Target;

		return true;
	}

	internal bool TryResolveGrant(
		GameplayAbilityHandle handle,
		out int slot,
		out Entity owner)
	{
		owner = default;

		if (!TryResolve(handle, out slot))
		{
			return false;
		}

		owner = _abilities[slot].Owner;

		return true;
	}

	internal uint GetActivationId(int slot)
	{
		return _abilities[slot].ActivationId;
	}

	internal int GetTaskHead(int slot)
	{
		return (uint)slot < (uint)_abilitySlotCount
			? _abilities[slot].TaskHead
			: -1;
	}

	internal void SetTaskHead(int slot, int head)
	{
		if ((uint)slot < (uint)_abilitySlotCount)
		{
			_abilities[slot].TaskHead = head;
		}
	}

	private void HandleTaskChanged(GameplayAbilityTaskChange change)
	{
		// Application tasks use a negative step and never drive a plan.
		if (change.PlanStep < 0)
		{
			return;
		}

		GameplayAbilityActivation activation = change.Activation;

		if (!TryResolveActivation(activation, out int slot, out _, out _)
			|| _abilities[slot].PlanStep != change.PlanStep)
		{
			return;
		}

		if (change.Kind == GameplayAbilityTaskChangeKind.Cancelled)
		{
			// A plan step that is cancelled by game code cannot resume,
			// so the ability ends normally instead of staying active
			// forever without a pending task. Cancellation caused by the
			// ability itself already cleared the cursor before the task
			// notification was raised.
			_abilities[slot].PlanStep = -1;

			EndActivation(slot, GameplayAbilityChangeKind.Ended);

			return;
		}

		if (change.Kind != GameplayAbilityTaskChangeKind.Completed)
		{
			return;
		}

		_abilities[slot].PlanStep = -1;

		RunPlanGuarded(activation, change.PlanStep + 1);
	}

	/// <summary>
	/// Runs a plan and applies the documented failure policy: an
	/// exception from a step cancels the activation, releasing every
	/// task and subscription, before the exception is rethrown.
	/// </summary>
	/// <remarks>
	/// A plan exception must not leave an active ability with no
	/// pending task and no plan cursor. Cancelling is a terminal state:
	/// the ability is no longer active, a <c>Cancelled</c> notification
	/// was raised, and the committed cost and cooldown are not rolled
	/// back. The original exception is rethrown; if cleanup itself throws
	/// (a cancellation notification failed), both exceptions are
	/// reported in an <see cref="AggregateException"/> instead of the
	/// cleanup failure silently replacing the plan failure.
	/// </remarks>
	private bool RunPlanGuarded(
		GameplayAbilityActivation activation,
		int stepIndex)
	{
		try
		{
			return RunPlan(activation, stepIndex);
		}
		catch (Exception planException)
		{
			try
			{
				FailPlan(activation);
			}
			catch (Exception cleanupException)
			{
				throw new AggregateException(
					"A gameplay ability plan failed and its cleanup also " +
					"failed. Both exceptions are reported; the activation " +
					"was marked inactive before cleanup notifications ran.",
					planException,
					cleanupException);
			}

			throw;
		}
	}

	private void FailPlan(GameplayAbilityActivation activation)
	{
		if (!TryResolveActivation(activation, out int slot, out _, out _))
		{
			return;
		}

		// Clear the cursor before cancellation notifications run, so a
		// task cancellation cannot try to resume the broken plan. The
		// activation is cancelled rather than silently aborted: game
		// code observes the same terminal notification as any other
		// interruption, and the already committed cost and cooldown
		// follow the normal transaction policy.
		_abilities[slot].PlanStep = -1;

		EndActivation(slot, GameplayAbilityChangeKind.Cancelled);
	}

	private void CancelFailedActivation(GameplayAbilityHandle handle)
	{
		if (!TryResolve(handle, out int slot) || !_abilities[slot].Active)
		{
			return;
		}

		_abilities[slot].PlanStep = -1;

		EndActivation(slot, GameplayAbilityChangeKind.Cancelled);
	}

	private bool RunPlan(
		GameplayAbilityActivation activation,
		int stepIndex)
	{
		while (true)
		{
			if (!TryResolveActivation(
				activation,
				out int slot,
				out Entity owner,
				out Entity target))
			{
				return false;
			}

			GameplayAbilityPlan? plan = _abilities[slot].Definition.Plan;

			if (plan is null)
			{
				return false;
			}

			if (stepIndex >= plan.Count)
			{
				EndActivation(slot, GameplayAbilityChangeKind.Ended);

				return true;
			}

			_abilities[slot].PlanStep = -1;

			GameplayAbilityPlanStep step = plan[stepIndex];

			switch (step.Kind)
			{
				case GameplayAbilityPlanStepKind.WaitDuration:
					{
						// Claim the step before creating the task. A Started
						// handler may publish the event that completes an event
						// task, which advances the plan from inside the start.
						_abilities[slot].PlanStep = stepIndex;

						GameplayAbilityTaskHandle task = Tasks.StartDurationTask(
							activation,
							step.DurationSeconds,
							stepIndex);

						return HandleWaitStepStarted(
							activation,
							stepIndex,
							task);
					}
				case GameplayAbilityPlanStepKind.WaitEvent:
					{
						_abilities[slot].PlanStep = stepIndex;

						GameplayAbilityTaskHandle task =
							Tasks.StartGameplayEventTask(
								activation,
								step.Tag,
								step.Match,
								default,
								default,
								stepIndex);

						return HandleWaitStepStarted(
							activation,
							stepIndex,
							task);
					}
				case GameplayAbilityPlanStepKind.WaitTag:
					{
						_abilities[slot].PlanStep = stepIndex;

						GameplayAbilityTaskHandle task =
							Tasks.StartTagConditionTask(
								activation,
								step.Tag,
								step.WaitMode,
								step.Exact,
								stepIndex);

						return HandleWaitStepStarted(
							activation,
							stepIndex,
							task);
					}
				case GameplayAbilityPlanStepKind.ApplyEffect:
					{
						Entity effectTarget =
							step.EffectTarget == GameplayAbilityEffectTarget.Owner
								? owner
								: target;

						if (_world.IsAlive(effectTarget))
						{
							_effects.Apply(effectTarget, step.Effect!, owner);
						}

						stepIndex++;
						break;
					}
				case GameplayAbilityPlanStepKind.PublishEvent:
					{
						Events.Publish(new GameplayEvent(
							step.Tag,
							owner,
							target));

						stepIndex++;
						break;
					}
				case GameplayAbilityPlanStepKind.WaitAny:
					{
						// Claim the step before creating the task, so a
						// Started handler that completes the race advances
						// from inside the start.
						_abilities[slot].PlanStep = stepIndex;

						GameplayAbilityTaskHandle task =
							Tasks.StartWaitAnyTask(
								activation,
								step.Wait!,
								stepIndex);

						return HandleWaitStepStarted(
							activation,
							stepIndex,
							task);
					}
				case GameplayAbilityPlanStepKind.WaitCustom:
					{
						_abilities[slot].PlanStep = stepIndex;

						GameplayAbilityTaskHandle task =
							Tasks.StartCustomTask(
								activation,
								step.CustomTask!,
								stepIndex);

						return HandleWaitStepStarted(
							activation,
							stepIndex,
							task);
					}
				case GameplayAbilityPlanStepKind.Branch:
					{
						// Conditions read live state at the moment the
						// step runs, so a callback that changed tags or
						// attributes after the previous step is observed
						// here.
						GameplayConditionContext conditionContext = new(
							_tags,
							_attributes,
							owner,
							target,
							_world.IsAlive(target));

						if (!step.Condition!.Evaluate(in conditionContext))
						{
							stepIndex = step.TargetStep;
							break;
						}

						stepIndex++;
						break;
					}
				case GameplayAbilityPlanStepKind.Goto:
					{
						stepIndex = step.TargetStep;
						break;
					}
				default:
					{
						EndActivation(slot, GameplayAbilityChangeKind.Ended);

						return true;
					}
			}
		}
	}

	private bool HandleWaitStepStarted(
		GameplayAbilityActivation activation,
		int stepIndex,
		GameplayAbilityTaskHandle task)
	{
		if (!task.IsNone && _tasks is not null && _tasks.IsPending(task))
		{
			return false;
		}

		// The task was never created, or its Started notification ended
		// it. A completed step may already have advanced the plan, so
		// only clear the cursor when it still names this step.
		if (TryResolveActivation(activation, out int slot, out _, out _)
			&& _abilities[slot].PlanStep == stepIndex)
		{
			_abilities[slot].PlanStep = -1;
		}

		return false;
	}

	private static GameplayAbilityActivationResult Rejected(
		GameplayAbilityActivationStatus status,
		GameplayAbilityHandle handle,
		Entity owner,
		Entity target,
		int level)
	{
		return new GameplayAbilityActivationResult(
			status,
			handle,
			owner,
			target,
			level,
			0);
	}

	private bool CostIsMet(
		Entity owner,
		GameplayEffectDefinition? costEffect)
	{
		if (costEffect is null)
		{
			return true;
		}

		if (!costEffect.ApplicationRequirements.IsSatisfiedBy(_tags, owner))
		{
			return false;
		}

		// Unreal's CanApplyAttributeModifiers checks only additive
		// modifiers: would the owner's current value plus the cost go
		// below zero? Multipliers and overrides are skipped.
		ReadOnlySpan<GameplayEffectModifier> modifiers = costEffect.Modifiers;

		for (int index = 0; index < modifiers.Length; index++)
		{
			GameplayEffectModifier modifier = modifiers[index];

			if (modifier.Operation != GameplayModifierOperation.AddBase)
			{
				continue;
			}

			if (_attributes.GetCurrentValue(owner, modifier.Attribute)
				+ modifier.Magnitude < 0f)
			{
				return false;
			}
		}

		return true;
	}

	private bool TargetRequirementsAreMet(
		GameplayTagRequirements requirements,
		Entity target)
	{
		if (requirements.IsEmpty)
		{
			return true;
		}

		return _world.IsAlive(target)
			&& requirements.IsSatisfiedBy(_tags, target);
	}

	private bool IsBlockedByActiveAbility(
		Entity owner,
		GameplayTagSet assetTags,
		int ignoreSlot)
	{
		if (assetTags.IsEmpty
			|| !TryGetEntitySlot(owner, out int segmentIndex, out int offset)
			|| _entitySegments[segmentIndex]![offset].ActiveCount == 0)
		{
			return false;
		}

		int slot = _entitySegments[segmentIndex]![offset].Head;

		while (slot >= 0)
		{
			ref AbilitySlot candidate = ref _abilities[slot];

			if (candidate.Active && slot != ignoreSlot)
			{
				GameplayTagSet blockTags =
					candidate.Definition.BlockAbilitiesWithTags;

				if (!blockTags.IsEmpty && assetTags.HasAny(blockTags))
				{
					return true;
				}
			}

			slot = candidate.NextOnEntity;
		}

		return false;
	}

	private bool IsStillActive(
		GameplayAbilityHandle handle,
		Entity owner,
		out int slot)
	{
		slot = -1;

		return _world.IsAlive(owner)
			&& TryResolve(handle, out slot)
			&& _abilities[slot].Active;
	}

	private int CancelMatching(
		Entity owner,
		GameplayTagSet? tags,
		int ignoreSlot)
	{
		int capacity = GetAbilityCount(owner);

		if (capacity == 0)
		{
			return 0;
		}

		// Capture the abilities that are active when the call starts.
		// An ability activated by a Cancelled notification is a new
		// activation and is left alone; an ability that was already in
		// scope but has since been reactivated is also left alone,
		// because its activation identity changed.
		CapturedAbility[] captured = _cancelPool.Rent(capacity);

		try
		{
			int count = CaptureActiveAbilities(
				owner,
				tags,
				ignoreSlot,
				captured);
			int cancelled = 0;

			for (int index = 0; index < count; index++)
			{
				CapturedAbility candidate = captured[index];

				if (!TryResolve(candidate.Handle, out int slot)
					|| !_abilities[slot].Active
					|| _abilities[slot].ActivationId != candidate.ActivationId)
				{
					continue;
				}

				EndActivation(slot, GameplayAbilityChangeKind.Cancelled);
				cancelled++;
			}

			return cancelled;
		}
		finally
		{
			_cancelPool.Return();
		}
	}

	private int CaptureActiveAbilities(
		Entity owner,
		GameplayTagSet? tags,
		int ignoreSlot,
		CapturedAbility[] destination)
	{
		if (!TryGetEntitySlot(owner, out int segmentIndex, out int offset))
		{
			return 0;
		}

		int count = 0;
		int slot = _entitySegments[segmentIndex]![offset].Head;

		while (slot >= 0)
		{
			ref AbilitySlot candidate = ref _abilities[slot];

			if (candidate.Active
				&& slot != ignoreSlot
				&& (tags is null || candidate.Definition.AssetTags.HasAny(tags)))
			{
				destination[count++] = new CapturedAbility(
					CreateHandle(slot),
					candidate.ActivationId);
			}

			slot = candidate.NextOnEntity;
		}

		return count;
	}

	private int CaptureInputGrants(
		Entity owner,
		int inputId,
		CapturedAbility[] destination)
	{
		if (!TryGetEntitySlot(owner, out int segmentIndex, out int offset))
		{
			return 0;
		}

		int count = 0;
		int slot = _entitySegments[segmentIndex]![offset].Head;

		while (slot >= 0)
		{
			ref AbilitySlot candidate = ref _abilities[slot];

			if (candidate.InputId == inputId)
			{
				destination[count++] = new CapturedAbility(
					CreateHandle(slot),
					candidate.ActivationId);
			}

			slot = candidate.NextOnEntity;
		}

		return count;
	}

	private bool EndActivation(int slot, GameplayAbilityChangeKind kind)
	{
		if (!_abilities[slot].Active)
		{
			return false;
		}

		// Build the ability notification before any user code can see
		// the ending. A task cancellation handler may revoke the grant
		// and recycle its slot; the payload must still describe the
		// activation that ended.
		GameplayAbilityChange change = CreateChange(kind, slot);
		GameplayAbilityActivation activation = new(
			CreateHandle(slot),
			_abilities[slot].ActivationId);

		// Ending blocks a nested activation from a task cancellation
		// handler. Without it, a handler could start a new activation
		// before the old one's Ended notification was raised.
		_abilities[slot].Ending = true;

		MarkInactive(slot);

		try
		{
			if (_tasks is not null && !_tasks.IsDisposed)
			{
				_tasks.CancelActivation(activation);
			}
		}
		finally
		{
			if (TryResolveGrant(activation.Ability, out int current, out _)
				&& _abilities[current].ActivationId == activation.Id)
			{
				_abilities[current].Ending = false;
			}

			Changed?.Invoke(change);
		}

		return true;
	}

	private void AbortActivation(int slot)
	{
		// A commit that never raised Activated is rejected rather than
		// cancelled: it is rolled back silently, like a failed check.
		if (_abilities[slot].Active)
		{
			GameplayAbilityActivation activation = new(
				CreateHandle(slot),
				_abilities[slot].ActivationId);

			MarkInactive(slot);

			// A callback during the commit may have started tasks even
			// though Activated was never observed. Release them without
			// notifications: the activation never became observable.
			if (_tasks is not null && !_tasks.IsDisposed)
			{
				_tasks.CancelActivationSilently(activation);
			}
		}
	}

	private void RollbackCooldown(
		GameplayEffectHandle cooldown,
		GameplayEffectApplicationRollback rollback)
	{
		if (!cooldown.IsNone)
		{
			_effects.RevertApplication(cooldown, rollback);
		}
	}

	private void MarkInactive(int slot)
	{
		Entity owner = _abilities[slot].Owner;

		_abilities[slot].Active = false;
		_abilities[slot].PlanStep = -1;

		_activeCount--;

		if (TryGetEntitySlot(owner, out int segmentIndex, out int offset))
		{
			ref EntityAbilities state = ref _entitySegments[segmentIndex]![offset];

			if (state.ActiveCount > 0)
			{
				state.ActiveCount--;
			}
		}
	}

	private void HandleTagsChanged(Entity entity)
	{
		if (!TryGetEntitySlot(entity, out int segmentIndex, out int offset))
		{
			return;
		}

		ref EntityAbilities state = ref _entitySegments[segmentIndex]![offset];

		if (state.ActiveCount == 0)
		{
			return;
		}

		if (state.CancellingByTags)
		{
			// A cancellation handler changed tags again. Run another
			// pass after the current one instead of recursing, which
			// also means an ability activated by a handler is evaluated
			// by the tag change it observed.
			state.TagsChangedAgain = true;
			return;
		}

		state.CancellingByTags = true;

		try
		{
			for (int pass = 0; pass < MaxTagCancelPasses; pass++)
			{
				if (!TryGetEntitySlot(entity, out segmentIndex, out offset))
				{
					return;
				}

				_entitySegments[segmentIndex]![offset].TagsChangedAgain = false;

				CancelByTagPass(entity);

				if (!TryGetEntitySlot(entity, out segmentIndex, out offset)
					|| !_entitySegments[segmentIndex]![offset].TagsChangedAgain)
				{
					return;
				}
			}
		}
		finally
		{
			if (TryGetEntitySlot(entity, out segmentIndex, out offset))
			{
				ref EntityAbilities final = ref _entitySegments[segmentIndex]![offset];

				final.CancellingByTags = false;
				final.TagsChangedAgain = false;
			}
		}
	}

	private void CancelByTagPass(Entity entity)
	{
		int capacity = GetAbilityCount(entity);

		if (capacity == 0)
		{
			return;
		}

		CapturedAbility[] captured = _tagCancelPool.Rent(capacity);

		try
		{
			int count = CaptureTagCancelled(entity, captured);

			for (int index = 0; index < count; index++)
			{
				CapturedAbility candidate = captured[index];

				if (!TryResolve(candidate.Handle, out int slot)
					|| !_abilities[slot].Active
					|| _abilities[slot].ActivationId != candidate.ActivationId)
				{
					continue;
				}

				EndActivation(slot, GameplayAbilityChangeKind.Cancelled);
			}
		}
		finally
		{
			_tagCancelPool.Return();
		}
	}

	private int CaptureTagCancelled(
		Entity entity,
		CapturedAbility[] destination)
	{
		if (!TryGetEntitySlot(entity, out int segmentIndex, out int offset))
		{
			return 0;
		}

		int count = 0;
		int slot = _entitySegments[segmentIndex]![offset].Head;

		while (slot >= 0)
		{
			ref AbilitySlot candidate = ref _abilities[slot];

			if (candidate.Active
				&& !candidate.Definition.CancelOnTags.IsEmpty
				&& _tags.HasAny(entity, candidate.Definition.CancelOnTags))
			{
				destination[count++] = new CapturedAbility(
					CreateHandle(slot),
					candidate.ActivationId);
			}

			slot = candidate.NextOnEntity;
		}

		return count;
	}

	private int CaptureGrants(Entity owner, CapturedAbility[] destination)
	{
		if (!TryGetEntitySlot(owner, out int segmentIndex, out int offset))
		{
			return 0;
		}

		int count = 0;
		int slot = _entitySegments[segmentIndex]![offset].Head;

		while (slot >= 0)
		{
			ref AbilitySlot candidate = ref _abilities[slot];

			destination[count++] = new CapturedAbility(
				CreateHandle(slot),
				candidate.ActivationId);

			slot = candidate.NextOnEntity;
		}

		return count;
	}

	private void HandleEntityDestroyed(Entity entity)
	{
		if (entity.AllocatorId != _allocatorId)
		{
			return;
		}

		if (!TryGetEntitySlot(entity, out int segmentIndex, out int offset))
		{
			return;
		}

		int slot = _entitySegments[segmentIndex]![offset].Head;

		while (slot >= 0)
		{
			int next = _abilities[slot].NextOnEntity;

			if (_abilities[slot].Active)
			{
				// The stores clear their own per-entity state, so this
				// only releases the grants. No notification is raised:
				// the abilities no longer exist for anyone to observe.
				_abilities[slot].Active = false;
				_activeCount--;
			}

			Unlink(slot);
			ReleaseSlot(slot);

			_grantedCount--;

			slot = next;
		}

		_entitySegments[segmentIndex]![offset].Head = -1;
		_entitySegments[segmentIndex]![offset].Count = 0;
		_entitySegments[segmentIndex]![offset].ActiveCount = 0;
	}

	private void RevokeSlot(int slot)
	{
		GameplayAbilityChange change = CreateChange(
			GameplayAbilityChangeKind.Revoked,
			slot);

		Unlink(slot);

		_grantedCount--;

		ReleaseSlot(slot);

		Changed?.Invoke(change);
	}

	private GrantedGameplayAbility Describe(int slot)
	{
		ref AbilitySlot ability = ref _abilities[slot];

		return new GrantedGameplayAbility(
			CreateHandle(slot),
			ability.Definition,
			ability.Owner,
			ability.Level,
			ability.InputId,
			ability.Active);
	}

	private GameplayAbilityHandle CreateHandle(int slot)
	{
		ref AbilitySlot ability = ref _abilities[slot];

		return new GameplayAbilityHandle(
			_systemId,
			ability.Owner,
			slot,
			ability.Generation);
	}

	private bool TryResolve(GameplayAbilityHandle handle, out int slot)
	{
		slot = handle.Slot;

		if (handle.IsNone
			|| (uint)handle.Slot >= (uint)_abilitySlotCount)
		{
			return false;
		}

		ref AbilitySlot ability = ref _abilities[handle.Slot];

		return handle.SystemId == _systemId
			&& ability.Generation == handle.Generation
			&& ability.Owner == handle.Owner;
	}

	private int AcquireAbilitySlot()
	{
		int slot;

		if (_freeAbilitySlot >= 0)
		{
			slot = _freeAbilitySlot;
			_freeAbilitySlot = _abilities[slot].NextOnEntity;
		}
		else
		{
			if (_abilitySlotCount == _abilities.Length)
			{
				Array.Resize(
					ref _abilities,
					Math.Max(InitialAbilityCapacity, _abilities.Length * 2));
			}

			slot = _abilitySlotCount++;
		}

		ref AbilitySlot ability = ref _abilities[slot];

		if (ability.Generation == 0)
		{
			ability.Generation = 1;
		}

		return slot;
	}

	private void ReleaseSlot(int slot)
	{
		ref AbilitySlot ability = ref _abilities[slot];

		uint generation = NextGeneration(ability.Generation);

		_abilities[slot] = new AbilitySlot
		{
			Generation = generation,
			NextOnEntity = _freeAbilitySlot,
			PreviousOnEntity = -1,
			TaskHead = -1,
			PlanStep = -1
		};

		_freeAbilitySlot = slot;
	}

	private void Link(Entity owner, int slot)
	{
		ref EntityAbilities slotState = ref GetOrCreateEntitySlot(owner);

		int head = slotState.Head;

		_abilities[slot].NextOnEntity = head;
		_abilities[slot].PreviousOnEntity = -1;

		if (head >= 0)
		{
			_abilities[head].PreviousOnEntity = slot;
		}

		slotState.Head = slot;
		slotState.Count++;
	}

	private void Unlink(int slot)
	{
		ref AbilitySlot ability = ref _abilities[slot];

		Entity owner = ability.Owner;
		int previous = ability.PreviousOnEntity;
		int next = ability.NextOnEntity;
		bool hasSlot = TryGetEntitySlot(
			owner,
			out int segmentIndex,
			out int offset);

		if (previous >= 0)
		{
			_abilities[previous].NextOnEntity = next;
		}
		else if (hasSlot)
		{
			_entitySegments[segmentIndex]![offset].Head = next;
		}

		if (next >= 0)
		{
			_abilities[next].PreviousOnEntity = previous;
		}

		if (hasSlot)
		{
			ref EntityAbilities slotState = ref _entitySegments[segmentIndex]![offset];

			if (slotState.Count > 0)
			{
				slotState.Count--;
			}
		}

		_abilities[slot].NextOnEntity = -1;
		_abilities[slot].PreviousOnEntity = -1;
	}

	private ref EntityAbilities GetOrCreateEntitySlot(Entity entity)
	{
		int entityIndex = (int)entity.Index;
		int segmentIndex = entityIndex >> SegmentShift;

		if (segmentIndex >= _entitySegments.Length)
		{
			Array.Resize(
				ref _entitySegments,
				Math.Max(segmentIndex + 1, _entitySegments.Length * 2));
		}

		EntityAbilities[]? segment = _entitySegments[segmentIndex];

		if (segment is null)
		{
			segment = new EntityAbilities[SegmentSize];
			_entitySegments[segmentIndex] = segment;
		}

		ref EntityAbilities slot = ref segment[entityIndex & SegmentMask];

		if (slot.Generation != entity.Generation)
		{
			// An earlier destruction handler may have recycled the
			// index before this system observed the old entity's
			// destruction. The old grants must be released here, or
			// they would keep their slots and counters forever. A
			// never-used segment has a zero generation and no grants;
			// its default head is not a list to walk.
			if (slot.Count > 0)
			{
				ReleaseStaleGrants(ref slot);
			}

			slot.Generation = entity.Generation;
			slot.Head = -1;
			slot.Count = 0;
			slot.ActiveCount = 0;
			slot.CancellingByTags = false;
			slot.TagsChangedAgain = false;
		}

		return ref slot;
	}

	private void ReleaseStaleGrants(ref EntityAbilities state)
	{
		int slot = state.Head;

		while (slot >= 0)
		{
			int next = _abilities[slot].NextOnEntity;

			if (_abilities[slot].Active)
			{
				_activeCount--;
			}

			// The old owner is already dead, so its grants are
			// released silently, exactly as in HandleEntityDestroyed.
			Unlink(slot);
			ReleaseSlot(slot);

			_grantedCount--;

			slot = next;
		}

		state.Head = -1;
		state.Count = 0;
		state.ActiveCount = 0;
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

		EntityAbilities[]? segment = _entitySegments[segmentIndex];

		if (segment is null)
		{
			return false;
		}

		offset = entityIndex & SegmentMask;

		return segment[offset].Generation == entity.Generation;
	}

	private GameplayAbilityChange CreateChange(
		GameplayAbilityChangeKind kind,
		int slot)
	{
		ref AbilitySlot ability = ref _abilities[slot];

		return new GameplayAbilityChange(
			kind,
			CreateHandle(slot),
			ability.Definition,
			ability.Owner,
			ability.Target,
			ability.Level,
			ability.ActivationId);
	}

	private void RaiseChange(GameplayAbilityChangeKind kind, int slot)
	{
		Action<GameplayAbilityChange>? handler = Changed;

		if (handler is null)
		{
			return;
		}

		handler(CreateChange(kind, slot));
	}

	private static uint NextGeneration(uint generation)
	{
		generation++;

		return generation == 0 ? 1 : generation;
	}

	private static uint AllocateSystemId()
	{
		// System identity is process-global and never reused, so a handle
		// can never be mistaken for one from a later system. Wrapping to
		// zero is skipped because zero is the default handle's id.
		uint id = unchecked((uint)Interlocked.Increment(ref s_nextSystemId));

		return id == 0
			? unchecked((uint)Interlocked.Increment(ref s_nextSystemId))
			: id;
	}

	private uint NextActivationId()
	{
		uint id = unchecked(++_nextActivationId);

		return id == 0 ? unchecked(++_nextActivationId) : id;
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}

	private readonly struct CapturedAbility
	{
		public CapturedAbility(GameplayAbilityHandle handle, uint activationId)
		{
			Handle = handle;
			ActivationId = activationId;
		}

		public GameplayAbilityHandle Handle { get; }
		public uint ActivationId { get; }
	}

	private sealed class CapturePool
	{
		private readonly List<CapturedAbility[]> _buffers = [];
		private int _depth;

		public CapturedAbility[] Rent(int capacity)
		{
			if (_depth == _buffers.Count)
			{
				_buffers.Add(new CapturedAbility[Math.Max(16, capacity)]);
			}

			CapturedAbility[] buffer = _buffers[_depth++];

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

	private struct AbilitySlot
	{
		public GameplayAbilityDefinition Definition;
		public Entity Owner;
		public Entity Target;
		public int Level;
		public int InputId;
		public int NextOnEntity;
		public int PreviousOnEntity;
		public uint Generation;
		public uint ActivationId;
		public int TaskHead;
		public int PlanStep;
		public bool Active;
		public bool Activating;
		public bool Revoking;
		public bool Ending;
	}

	private struct EntityAbilities
	{
		public uint Generation;
		public int Head;
		public int Count;
		public int ActiveCount;
		public bool CancellingByTags;
		public bool TagsChangedAgain;
	}
}
