using Volt.Ecs;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tags;

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

	private readonly World _world;
	private readonly GameplayTagStore _tags;
	private readonly GameplayEffectSystem _effects;
	private readonly GameplayAttributeStore _attributes;
	private readonly uint _allocatorId;

	private AbilitySlot[] _abilities = new AbilitySlot[InitialAbilityCapacity];
	private int _abilitySlotCount;
	private int _freeAbilitySlot = -1;
	private int _grantedCount;
	private int _activeCount;
	private int _nextAttemptStamp;

	private EntityAbilities[]?[] _entitySegments = [];

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
		GameplayAttributeStore attributes)
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
		_abilities[slot].AttemptStamp = 0;

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
	/// Unreal's <c>ClearAbility</c> also cancels first.
	/// </remarks>
	public bool RevokeAbility(GameplayAbilityHandle handle)
	{
		ThrowIfDisposed();

		if (!TryResolve(handle, out int slot))
		{
			return false;
		}

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

	/// <summary>
	/// Revokes every grant an entity holds.
	/// </summary>
	/// <param name="owner">The entity.</param>
	/// <returns>The number of grants this call removed.</returns>
	/// <remarks>
	/// Abilities granted by a notification raised during the call are
	/// not revoked by the same call, which bounds the work even if a
	/// handler grants a replacement every time one is removed.
	/// </remarks>
	public int RevokeAll(Entity owner)
	{
		ThrowIfDisposed();

		int remaining = GetAbilityCount(owner);
		int removed = 0;

		while (remaining-- > 0)
		{
			if (!TryGetEntitySlot(owner, out int segmentIndex, out int offset))
			{
				break;
			}

			int slot = _entitySegments[segmentIndex]![offset].Head;

			if (slot < 0)
			{
				break;
			}

			if (RevokeAbility(CreateHandle(slot)))
			{
				removed++;
			}
		}

		return removed;
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

		return CountActiveSlots(owner, null, -1);
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
	/// then applies the cost and the cooldown before raising
	/// <see cref="GameplayAbilityChangeKind.Activated"/>. A handler
	/// therefore observes the paid cost and the granted cooldown tags.
	/// </para>
	/// <para>
	/// An activation without a handle that still resolves is reported
	/// as <see cref="GameplayAbilityActivationStatus.NotGranted"/>,
	/// not thrown: a stale handle is a gameplay race, not a
	/// programming error.
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

		GameplayAbilityDefinition definition = _abilities[slot].Definition;
		Entity owner = _abilities[slot].Owner;
		int level = _abilities[slot].Level;

		if (definition.CooldownEffect is not null
			&& _tags.HasAny(owner, definition.CooldownTags))
		{
			return Rejected(
				GameplayAbilityActivationStatus.RejectedByCooldown,
				handle,
				owner,
				target,
				level);
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

		_abilities[slot].Active = true;
		_abilities[slot].Target = target;

		_activeCount++;

		if (!definition.CancelAbilitiesWithTags.IsEmpty)
		{
			CancelMatching(owner, definition.CancelAbilitiesWithTags, slot);
		}

		if (definition.CostEffect is not null)
		{
			if (!IsStillActive(handle, owner, out slot))
			{
				return Rejected(
					GameplayAbilityActivationStatus.EndedDuringActivation,
					handle,
					owner,
					target,
					level);
			}

			_effects.Apply(owner, definition.CostEffect, owner);
		}

		if (definition.CooldownEffect is not null)
		{
			if (!IsStillActive(handle, owner, out slot))
			{
				return Rejected(
					GameplayAbilityActivationStatus.EndedDuringActivation,
					handle,
					owner,
					target,
					level);
			}

			_effects.Apply(owner, definition.CooldownEffect, owner);
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

		RaiseChange(GameplayAbilityChangeKind.Activated, slot);

		if (!IsStillActive(handle, owner, out slot))
		{
			return Rejected(
				GameplayAbilityActivationStatus.EndedDuringActivation,
				handle,
				owner,
				target,
				level);
		}

		if (definition.EndsOnActivate)
		{
			EndActivation(slot, GameplayAbilityChangeKind.Ended);

			return new GameplayAbilityActivationResult(
				GameplayAbilityActivationStatus.ActivatedAndEnded,
				handle,
				owner,
				target,
				level);
		}

		return new GameplayAbilityActivationResult(
			GameplayAbilityActivationStatus.Activated,
			handle,
			owner,
			target,
			level);
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
	/// Every grant bound to the input is attempted, most recently
	/// granted first — the order the per-owner list is stored in. The
	/// separate attempt stamps make the loop independent of
	/// notifications that revoke or add grants while it runs.
	/// </remarks>
	public int TryActivateByInput(Entity owner, int inputId)
	{
		ThrowIfDisposed();

		int stamp = unchecked(++_nextAttemptStamp);
		int remaining = CountGrantedWithInput(owner, inputId);
		int activated = 0;

		while (remaining-- > 0)
		{
			int slot = FindGrantedWithInput(owner, inputId, stamp);

			if (slot < 0)
			{
				break;
			}

			_abilities[slot].AttemptStamp = stamp;

			if (TryActivate(CreateHandle(slot)).Succeeded)
			{
				activated++;
			}
		}

		return activated;
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
	/// cancels an ability whose asset tag is <c>Ability.Dash</c>.
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
	/// The remaining duration of the cooldown effect, zero when no
	/// instance is active, or infinity for an infinite cooldown.
	/// </returns>
	/// <remarks>
	/// Unreal queries the active effects that grant the cooldown tags.
	/// VOLT looks up the ability's own cooldown effect, which is the
	/// only instance that can start one; the ability cannot activate
	/// again while its tags are present, so at most one instance
	/// exists. An inhibited cooldown still reports its remaining time,
	/// because Unreal's <c>GetActiveEffectsTimeRemaining</c> ignores
	/// inhibition.
	/// </remarks>
	public double GetCooldownTimeRemaining(
		Entity owner,
		GameplayAbilityDefinition definition)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(definition);

		if (definition.CooldownEffect is null
			|| !_effects.TryFindEffect(
				owner,
				definition.CooldownEffect,
				out ActiveGameplayEffect effect))
		{
			return 0d;
		}

		return effect.RemainingDuration;
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
		_abilities = [];
		_entitySegments = [];
		_abilitySlotCount = 0;
		_freeAbilitySlot = -1;
		_grantedCount = 0;
		_activeCount = 0;
		_disposed = true;

		GC.SuppressFinalize(this);
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
			level);
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
			|| !TryGetEntitySlot(owner, out int segmentIndex, out int offset))
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
		int remaining = CountActiveSlots(owner, tags, ignoreSlot);
		int cancelled = 0;

		while (remaining-- > 0)
		{
			int slot = FindActiveSlot(owner, tags, ignoreSlot);

			if (slot < 0)
			{
				break;
			}

			EndActivation(slot, GameplayAbilityChangeKind.Cancelled);
			cancelled++;
		}

		return cancelled;
	}

	private int CountActiveSlots(
		Entity owner,
		GameplayTagSet? tags,
		int ignoreSlot)
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
				count++;
			}

			slot = candidate.NextOnEntity;
		}

		return count;
	}

	private int FindActiveSlot(
		Entity owner,
		GameplayTagSet? tags,
		int ignoreSlot)
	{
		if (!TryGetEntitySlot(owner, out int segmentIndex, out int offset))
		{
			return -1;
		}

		int slot = _entitySegments[segmentIndex]![offset].Head;

		while (slot >= 0)
		{
			ref AbilitySlot candidate = ref _abilities[slot];

			if (candidate.Active
				&& slot != ignoreSlot
				&& (tags is null || candidate.Definition.AssetTags.HasAny(tags)))
			{
				return slot;
			}

			slot = candidate.NextOnEntity;
		}

		return -1;
	}

	private int CountGrantedWithInput(Entity owner, int inputId)
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
				count++;
			}

			slot = candidate.NextOnEntity;
		}

		return count;
	}

	private int FindGrantedWithInput(
		Entity owner,
		int inputId,
		int stamp)
	{
		if (!TryGetEntitySlot(owner, out int segmentIndex, out int offset))
		{
			return -1;
		}

		int slot = _entitySegments[segmentIndex]![offset].Head;

		while (slot >= 0)
		{
			ref AbilitySlot candidate = ref _abilities[slot];

			if (candidate.InputId == inputId
				&& candidate.AttemptStamp != stamp)
			{
				return slot;
			}

			slot = candidate.NextOnEntity;
		}

		return -1;
	}

	private bool EndActivation(int slot, GameplayAbilityChangeKind kind)
	{
		if (!_abilities[slot].Active)
		{
			return false;
		}

		// Mark the ability inactive before the notification reaches
		// user code, so a handler that queries it sees it ended.
		_abilities[slot].Active = false;

		_activeCount--;

		RaiseChange(kind, slot);

		return true;
	}

	private void HandleTagsChanged(Entity entity)
	{
		if (!TryGetEntitySlot(entity, out int segmentIndex, out int offset))
		{
			return;
		}

		int remaining = CountCancelledByTags(entity);

		while (remaining-- > 0)
		{
			int slot = FindCancelledByTags(entity);

			if (slot < 0)
			{
				break;
			}

			EndActivation(slot, GameplayAbilityChangeKind.Cancelled);
		}
	}

	private int CountCancelledByTags(Entity entity)
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
				count++;
			}

			slot = candidate.NextOnEntity;
		}

		return count;
	}

	private int FindCancelledByTags(Entity entity)
	{
		if (!TryGetEntitySlot(entity, out int segmentIndex, out int offset))
		{
			return -1;
		}

		int slot = _entitySegments[segmentIndex]![offset].Head;

		while (slot >= 0)
		{
			ref AbilitySlot candidate = ref _abilities[slot];

			if (candidate.Active
				&& !candidate.Definition.CancelOnTags.IsEmpty
				&& _tags.HasAny(entity, candidate.Definition.CancelOnTags))
			{
				return slot;
			}

			slot = candidate.NextOnEntity;
		}

		return -1;
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

		return ability.Generation == handle.Generation
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
			PreviousOnEntity = -1
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
			slot.Generation = entity.Generation;
			slot.Head = -1;
			slot.Count = 0;
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
			ability.Level);
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

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}

	private struct AbilitySlot
	{
		public GameplayAbilityDefinition Definition;
		public Entity Owner;
		public Entity Target;
		public int Level;
		public int InputId;
		public int AttemptStamp;
		public int NextOnEntity;
		public int PreviousOnEntity;
		public uint Generation;
		public bool Active;
	}

	private struct EntityAbilities
	{
		public uint Generation;
		public int Head;
		public int Count;
	}
}
