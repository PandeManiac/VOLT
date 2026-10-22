using Volt.Ecs;

namespace Volt.Gameplay.Attributes;

/// <summary>
/// Stores gameplay attribute values and modifiers for the entities of
/// one world.
/// </summary>
/// <remarks>
/// <para>
/// Each entity owns a sorted array of attribute entries. An entry
/// holds the permanent base value, the cached current value, and its
/// modifiers. Reading the current value returns the cache; the
/// aggregator runs only when a base value or a modifier changes, so
/// repeated reads during a frame cost one binary search and no
/// arithmetic over the modifier list.
/// </para>
/// <para>
/// Storage is sparse and segmented, like
/// <see cref="Tags.GameplayTagStore"/>. An entity that never touched
/// an attribute has no entry array. Reading a missing attribute
/// returns its registered default without allocating.
/// </para>
/// <para>
/// Modifiers are stable slots within an attribute. Removing one marks
/// the slot inactive and advances its generation, so a stale
/// <see cref="GameplayModifierHandle"/> cannot remove a modifier that
/// later reused the slot. Entity generations provide the same
/// protection when an entity index is recycled.
/// </para>
/// <para>
/// The store is single-threaded. Entity destruction clears the
/// entity's attributes and modifiers through
/// <see cref="World.EntityDestroyed"/>.
/// </para>
/// </remarks>
public sealed class GameplayAttributeStore : IDisposable
{
	private const int SegmentShift = 10;
	private const int SegmentSize = 1 << SegmentShift;
	private const int SegmentMask = SegmentSize - 1;
	private const int InitialEntryCapacity = 4;

	private readonly World _world;
	private readonly uint _allocatorId;

	private Slot[]?[] _segments = [];
	private int[] _denseIndices = new int[16];
	private int _denseCount;

	private long _nextModifierSequence = 1;

	private bool _disposed;

	/// <summary>
	/// Creates attribute storage for a world and subscribes to its
	/// entity destruction notifications.
	/// </summary>
	/// <param name="world">The world whose entities own attributes.</param>
	/// <exception cref="ArgumentNullException">
	/// The world is <see langword="null"/>.
	/// </exception>
	public GameplayAttributeStore(World world)
	{
		ArgumentNullException.ThrowIfNull(world);

		_world = world;
		_allocatorId = world.AllocatorId;

		_world.EntityDestroyed += HandleEntityDestroyed;
	}

	/// <summary>
	/// Occurs after a base value or an evaluated current value
	/// changes.
	/// </summary>
	/// <remarks>
	/// One notification is raised per mutation, after clamping. The
	/// event is not raised when a write leaves both values unchanged,
	/// when an entity is destroyed, or when a store is cleared.
	/// With no subscribers the cost is one null check per mutation.
	/// </remarks>
	public event Action<AttributeChange>? AttributeChanged;

	/// <summary>
	/// Gets the number of entities that own at least one attribute.
	/// </summary>
	public int AttributedEntityCount => _denseCount;

	/// <summary>
	/// Gets an entity's permanent base value.
	/// </summary>
	/// <param name="entity">The entity.</param>
	/// <param name="attribute">The attribute.</param>
	/// <returns>
	/// The base value, or the attribute's registered default when the
	/// entity has no state for it.
	/// </returns>
	/// <exception cref="ArgumentException">
	/// The attribute is not registered.
	/// </exception>
	/// <remarks>
	/// Reading a dead or recycled handle returns the default rather
	/// than throwing, so a query over a mixed population does not need
	/// an aliveness check merely to inspect a value.
	/// </remarks>
	public float GetBaseValue(Entity entity, GameplayAttribute attribute)
	{
		ThrowIfDisposed();

		GameplayAttributeDefinition definition = GetDefinitionOrThrow(attribute);

		if (!TryGetSlotLocation(entity, out int segmentIndex, out int offset))
		{
			return definition.DefaultBaseValue;
		}

		ref Slot slot = ref _segments[segmentIndex]![offset];

		int entryIndex = FindEntry(slot.Entries, slot.Count, attribute);

		return entryIndex >= 0
			? slot.Entries[entryIndex].BaseValue
			: definition.DefaultBaseValue;
	}

	/// <summary>
	/// Gets an entity's current value, including temporary modifiers.
	/// </summary>
	/// <param name="entity">The entity.</param>
	/// <param name="attribute">The attribute.</param>
	/// <returns>
	/// The cached current value, or the registered default when the
	/// entity has no state for the attribute.
	/// </returns>
	/// <exception cref="ArgumentException">
	/// The attribute is not registered.
	/// </exception>
	public float GetCurrentValue(Entity entity, GameplayAttribute attribute)
	{
		ThrowIfDisposed();

		GameplayAttributeDefinition definition = GetDefinitionOrThrow(attribute);

		if (!TryGetSlotLocation(entity, out int segmentIndex, out int offset))
		{
			return definition.DefaultBaseValue;
		}

		ref Slot slot = ref _segments[segmentIndex]![offset];

		int entryIndex = FindEntry(slot.Entries, slot.Count, attribute);

		return entryIndex >= 0
			? slot.Entries[entryIndex].CurrentValue
			: definition.DefaultBaseValue;
	}

	/// <summary>
	/// Determines whether an entity owns explicit state for an
	/// attribute.
	/// </summary>
	/// <param name="entity">The entity.</param>
	/// <param name="attribute">The attribute.</param>
	/// <remarks>
	/// A mere read does not create state; only a write or a modifier
	/// does. This method is intended for diagnostics and tests, not for
	/// deciding whether a value is usable.
	/// </remarks>
	public bool HasAttribute(Entity entity, GameplayAttribute attribute)
	{
		ThrowIfDisposed();
		GetDefinitionOrThrow(attribute);

		if (!TryGetSlotLocation(entity, out int segmentIndex, out int offset))
		{
			return false;
		}

		ref Slot slot = ref _segments[segmentIndex]![offset];

		return FindEntry(slot.Entries, slot.Count, attribute) >= 0;
	}

	/// <summary>
	/// Sets an entity's permanent base value.
	/// </summary>
	/// <param name="entity">The living entity.</param>
	/// <param name="attribute">The attribute.</param>
	/// <param name="value">The new base value.</param>
	/// <returns>
	/// <see langword="true"/> when a value changed.
	/// </returns>
	/// <exception cref="ArgumentException">
	/// The attribute is not registered or the entity is not alive.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The value is not finite.
	/// </exception>
	/// <remarks>
	/// The value is clamped to the attribute's range. Writing the
	/// default value to an entity that has no state does not allocate
	/// an entry and reports no change.
	/// </remarks>
	public bool SetBaseValue(
		Entity entity,
		GameplayAttribute attribute,
		float value)
	{
		ThrowIfDisposed();

		GameplayAttributeDefinition definition = GetDefinitionOrThrow(attribute);

		if (!float.IsFinite(value))
		{
			throw new ArgumentOutOfRangeException(
				nameof(value),
				value,
				"An attribute value must be finite. NaN and infinity " +
				"would make every comparison against the attribute " +
				"false or produce a value that cannot be clamped.");
		}

		if (!_world.IsAlive(entity))
		{
			throw new ArgumentException(
				"The entity is not alive in this store's world. Only a " +
				"living entity can own attribute state.",
				nameof(entity));
		}

		float clamped = definition.Clamp(value);

		if (!TryGetSlotLocation(entity, out int segmentIndex, out int offset))
		{
			if (clamped == definition.DefaultBaseValue)
			{
				return false;
			}

			ref Slot created = ref GetOrCreateSlot(entity);

			int createdIndex = EnsureEntry(ref created, attribute, entity);

			return ApplyBaseValue(
				ref created.Entries[createdIndex],
				entity,
				definition,
				clamped);
		}

		ref Slot slot = ref _segments[segmentIndex]![offset];

		int entryIndex = FindEntry(slot.Entries, slot.Count, attribute);

		if (entryIndex < 0)
		{
			if (clamped == definition.DefaultBaseValue)
			{
				return false;
			}

			entryIndex = EnsureEntry(ref slot, attribute, entity);
		}

		return ApplyBaseValue(
			ref slot.Entries[entryIndex],
			entity,
			definition,
			clamped);
	}

	/// <summary>
	/// Adds a delta to an entity's permanent base value.
	/// </summary>
	/// <param name="entity">The living entity.</param>
	/// <param name="attribute">The attribute.</param>
	/// <param name="delta">The signed change.</param>
	/// <returns>
	/// <see langword="true"/> when a value changed.
	/// </returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The delta or resulting value is not finite.
	/// </exception>
	public bool ModifyBaseValue(
		Entity entity,
		GameplayAttribute attribute,
		float delta)
	{
		ThrowIfDisposed();

		if (!float.IsFinite(delta))
		{
			throw new ArgumentOutOfRangeException(
				nameof(delta),
				delta,
				"An attribute delta must be finite.");
		}

		float value = GetBaseValue(entity, attribute) + delta;

		if (!float.IsFinite(value))
		{
			throw new ArgumentOutOfRangeException(
				nameof(delta),
				delta,
				"The resulting attribute value is not finite.");
		}

		return SetBaseValue(entity, attribute, value);
	}

	/// <summary>
	/// Applies a modifier operation to an entity's permanent base value
	/// once, without keeping any state that could undo it.
	/// </summary>
	/// <param name="entity">The living entity.</param>
	/// <param name="attribute">The attribute.</param>
	/// <param name="operation">How the magnitude changes the base value.</param>
	/// <param name="magnitude">The operation's magnitude.</param>
	/// <returns>
	/// <see langword="true"/> when the base value changed.
	/// </returns>
	/// <exception cref="ArgumentException">
	/// The attribute is not registered or the entity is not alive.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The magnitude is not finite or the operation is undefined.
	/// </exception>
	/// <remarks>
	/// <para>
	/// This is how an instant gameplay effect executes: each modifier of
	/// the effect is folded into the base value in declaration order, so
	/// an override followed by an addition leaves the addition, and a
	/// multiplication sees the result of an earlier addition. This
	/// reproduces Unreal's <c>FAggregator::StaticExecModOnBaseValue</c>,
	/// where every operation except <c>Override</c> and
	/// <c>DivideAdditive</c> is a plain arithmetic step.
	/// </para>
	/// <para>
	/// A divide by exactly zero leaves the value unchanged, because the
	/// alternative is infinity. Every write is clamped to the
	/// attribute's range, so an intermediate result is clamped before
	/// the next modifier of the same effect runs.
	/// </para>
	/// </remarks>
	public bool ExecuteInstantModifier(
		Entity entity,
		GameplayAttribute attribute,
		GameplayModifierOperation operation,
		float magnitude)
	{
		ThrowIfDisposed();

		GetDefinitionOrThrow(attribute);

		if ((uint)operation > (uint)GameplayModifierOperation.AddFinal)
		{
			throw new ArgumentOutOfRangeException(
				nameof(operation),
				operation,
				"The modifier operation is not defined.");
		}

		if (!float.IsFinite(magnitude))
		{
			throw new ArgumentOutOfRangeException(
				nameof(magnitude),
				magnitude,
				"A modifier magnitude must be finite.");
		}

		if (!_world.IsAlive(entity))
		{
			throw new ArgumentException(
				"The entity is not alive in this store's world. An " +
				"instant modifier cannot be applied to a destroyed or " +
				"recycled handle.",
				nameof(entity));
		}

		float current = GetBaseValue(entity, attribute);

		float value = operation switch
		{
			GameplayModifierOperation.Override => magnitude,
			GameplayModifierOperation.AddBase => current + magnitude,
			GameplayModifierOperation.AddFinal => current + magnitude,
			GameplayModifierOperation.MultiplyAdditive => current * magnitude,
			GameplayModifierOperation.MultiplyCompound => current * magnitude,
			GameplayModifierOperation.DivideAdditive => magnitude == 0f
				? current
				: current / magnitude,
			_ => current
		};

		if (!float.IsFinite(value))
		{
			throw new ArgumentOutOfRangeException(
				nameof(magnitude),
				magnitude,
				"The resulting attribute value is not finite. The " +
				"magnitude overflows the base value.");
		}

		return SetBaseValue(entity, attribute, value);
	}

	/// <summary>
	/// Applies a modifier to an entity's attribute.
	/// </summary>
	/// <param name="entity">The living entity.</param>
	/// <param name="attribute">The attribute.</param>
	/// <param name="operation">How the magnitude modifies the value.</param>
	/// <param name="magnitude">The operation's magnitude.</param>
	/// <returns>
	/// A handle that removes the modifier, or
	/// <see cref="GameplayModifierHandle.None"/> only if the store is
	/// shutting down.
	/// </returns>
	/// <exception cref="ArgumentException">
	/// The attribute is not registered or the entity is not alive.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The magnitude is not finite or the operation is undefined.
	/// </exception>
	public GameplayModifierHandle AddModifier(
		Entity entity,
		GameplayAttribute attribute,
		GameplayModifierOperation operation,
		float magnitude)
	{
		ThrowIfDisposed();

		GameplayAttributeDefinition definition = GetDefinitionOrThrow(attribute);

		if ((uint)operation > (uint)GameplayModifierOperation.AddFinal)
		{
			throw new ArgumentOutOfRangeException(
				nameof(operation),
				operation,
				"The modifier operation is not defined.");
		}

		if (!float.IsFinite(magnitude))
		{
			throw new ArgumentOutOfRangeException(
				nameof(magnitude),
				magnitude,
				"A modifier magnitude must be finite.");
		}

		if (!_world.IsAlive(entity))
		{
			throw new ArgumentException(
				"The entity is not alive in this store's world. A " +
				"modifier cannot be applied to a destroyed or recycled " +
				"handle.",
				nameof(entity));
		}

		ref Slot slot = ref GetOrCreateSlot(entity);

		int entryIndex = EnsureEntry(ref slot, attribute, entity);

		ref AttributeEntry entry = ref slot.Entries[entryIndex];

		long sequence = _nextModifierSequence++;
		float oldCurrentValue = entry.CurrentValue;

		int modifierSlot = AcquireModifierSlot(
			ref entry,
			operation,
			magnitude,
			sequence);

		entry.CurrentValue = definition.Clamp(Evaluate(ref entry));

		// The generation is read before the notification, because a
		// listener may destroy the entity, which clears the entry the
		// reference still points at.
		uint generation = entry.Modifiers[modifierSlot].Generation;

		if (oldCurrentValue != entry.CurrentValue)
		{
			RaiseChange(
				entity,
				attribute,
				entry.BaseValue,
				entry.BaseValue,
				oldCurrentValue,
				entry.CurrentValue);
		}

		return new GameplayModifierHandle(
			entity,
			attribute,
			modifierSlot,
			generation);
	}

	/// <summary>
	/// Removes a modifier previously added by
	/// <see cref="AddModifier"/>.
	/// </summary>
	/// <param name="handle">The handle returned when it was added.</param>
	/// <returns>
	/// <see langword="true"/> when this call removed the modifier;
	/// <see langword="false"/> when it was already removed, belongs to
	/// another store, or the entity is gone.
	/// </returns>
	public bool RemoveModifier(GameplayModifierHandle handle)
	{
		ThrowIfDisposed();

		if (handle.IsNone)
		{
			return false;
		}

		if (!handle.Attribute.IsValid
			|| !GameplayAttributeRegistry.Shared.TryGetDefinition(
				handle.Attribute,
				out GameplayAttributeDefinition definition))
		{
			return false;
		}

		if (!TryGetSlotLocation(handle.Entity, out int segmentIndex, out int offset))
		{
			return false;
		}

		ref Slot slot = ref _segments[segmentIndex]![offset];

		int entryIndex = FindEntry(slot.Entries, slot.Count, handle.Attribute);

		if (entryIndex < 0)
		{
			return false;
		}

		ref AttributeEntry entry = ref slot.Entries[entryIndex];

		if ((uint)handle.Slot >= (uint)entry.ModifierSlotCount)
		{
			return false;
		}

		ref Modifier modifier = ref entry.Modifiers[handle.Slot];

		if (!modifier.Active || modifier.Generation != handle.Generation)
		{
			return false;
		}

		modifier.Active = false;
		modifier.Generation = NextGeneration(modifier.Generation);
		modifier.Magnitude = 0f;
		modifier.Sequence = 0;

		entry.ActiveModifierCount--;

		float oldCurrentValue = entry.CurrentValue;

		entry.CurrentValue = definition.Clamp(Evaluate(ref entry));

		if (oldCurrentValue != entry.CurrentValue)
		{
			RaiseChange(
				handle.Entity,
				handle.Attribute,
				entry.BaseValue,
				entry.BaseValue,
				oldCurrentValue,
				entry.CurrentValue);
		}

		return true;
	}

	/// <summary>
	/// Replaces the magnitude of an active modifier.
	/// </summary>
	/// <param name="handle">The handle returned when it was added.</param>
	/// <param name="magnitude">The new magnitude.</param>
	/// <returns>
	/// <see langword="true"/> when a magnitude changed;
	/// <see langword="false"/> when the handle is stale, belongs to
	/// another store, is already removed, or already had this
	/// magnitude.
	/// </returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The magnitude is not finite.
	/// </exception>
	/// <remarks>
	/// A stacked effect rescales its modifiers in place instead of
	/// removing and re-adding them. The attribute is re-evaluated once,
	/// so a stack change produces one change notification rather than a
	/// dip and a recovery that a UI would have to debounce. The
	/// modifier keeps its original insertion sequence, so override
	/// precedence does not change when a magnitude does.
	/// </remarks>
	public bool SetModifierMagnitude(
		GameplayModifierHandle handle,
		float magnitude)
	{
		ThrowIfDisposed();

		if (!float.IsFinite(magnitude))
		{
			throw new ArgumentOutOfRangeException(
				nameof(magnitude),
				magnitude,
				"A modifier magnitude must be finite.");
		}

		if (handle.IsNone)
		{
			return false;
		}

		if (!GameplayAttributeRegistry.Shared.TryGetDefinition(
			handle.Attribute,
			out GameplayAttributeDefinition definition))
		{
			return false;
		}

		if (!TryGetSlotLocation(
			handle.Entity,
			out int segmentIndex,
			out int offset))
		{
			return false;
		}

		ref Slot slot = ref _segments[segmentIndex]![offset];

		int entryIndex = FindEntry(slot.Entries, slot.Count, handle.Attribute);

		if (entryIndex < 0)
		{
			return false;
		}

		ref AttributeEntry entry = ref slot.Entries[entryIndex];

		if ((uint)handle.Slot >= (uint)entry.ModifierSlotCount)
		{
			return false;
		}

		ref Modifier modifier = ref entry.Modifiers[handle.Slot];

		if (!modifier.Active
			|| modifier.Generation != handle.Generation
			|| modifier.Magnitude == magnitude)
		{
			return false;
		}

		modifier.Magnitude = magnitude;

		float oldCurrentValue = entry.CurrentValue;

		entry.CurrentValue = definition.Clamp(Evaluate(ref entry));

		if (oldCurrentValue != entry.CurrentValue)
		{
			RaiseChange(
				handle.Entity,
				handle.Attribute,
				entry.BaseValue,
				entry.BaseValue,
				oldCurrentValue,
				entry.CurrentValue);
		}

		return true;
	}

	/// <summary>
	/// Gets the number of active modifiers on an attribute.
	/// </summary>
	/// <param name="entity">The entity.</param>
	/// <param name="attribute">The attribute.</param>
	public int GetModifierCount(Entity entity, GameplayAttribute attribute)
	{
		ThrowIfDisposed();
		GetDefinitionOrThrow(attribute);

		if (!TryGetSlotLocation(entity, out int segmentIndex, out int offset))
		{
			return 0;
		}

		ref Slot slot = ref _segments[segmentIndex]![offset];

		int entryIndex = FindEntry(slot.Entries, slot.Count, attribute);

		return entryIndex >= 0
			? slot.Entries[entryIndex].ActiveModifierCount
			: 0;
	}

	/// <summary>
	/// Removes every attribute and modifier from an entity.
	/// </summary>
	/// <param name="entity">The entity.</param>
	/// <returns>
	/// <see langword="true"/> when the entity owned attribute state.
	/// </returns>
	/// <remarks>
	/// No change notifications are raised: the values no longer exist
	/// for anyone to observe.
	/// </remarks>
	public bool Clear(Entity entity)
	{
		ThrowIfDisposed();

		if (!TryGetSlotLocation(entity, out int segmentIndex, out int offset))
		{
			return false;
		}

		ref Slot slot = ref _segments[segmentIndex]![offset];

		bool hadState = slot.Count > 0;

		ClearSlot(ref slot);

		return hadState;
	}

	/// <summary>
	/// Copies the handles of all entities that own attribute state.
	/// </summary>
	/// <param name="destination">The destination span.</param>
	/// <returns>
	/// The number of handles written, which is less than
	/// <see cref="AttributedEntityCount"/> when the span is too small.
	/// </returns>
	public int CopyAttributedEntities(Span<Entity> destination)
	{
		ThrowIfDisposed();

		int count = Math.Min(destination.Length, _denseCount);

		for (int index = 0; index < count; index++)
		{
			int entityIndex = _denseIndices[index];
			int segmentIndex = entityIndex >> SegmentShift;

			ref Slot slot = ref _segments[segmentIndex]![entityIndex & SegmentMask];

			destination[index] = new Entity(
				_allocatorId,
				(uint)entityIndex,
				slot.Generation);
		}

		return count;
	}

	/// <inheritdoc />
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_world.EntityDestroyed -= HandleEntityDestroyed;

		AttributeChanged = null;
		_segments = [];
		_denseCount = 0;
		_disposed = true;

		GC.SuppressFinalize(this);
	}

	private bool ApplyBaseValue(
		ref AttributeEntry entry,
		Entity entity,
		GameplayAttributeDefinition definition,
		float clamped)
	{
		if (entry.BaseValue == clamped)
		{
			return false;
		}

		float oldBaseValue = entry.BaseValue;
		float oldCurrentValue = entry.CurrentValue;

		entry.BaseValue = clamped;
		entry.CurrentValue = definition.Clamp(Evaluate(ref entry));

		RaiseChange(
			entity,
			entry.Attribute,
			oldBaseValue,
			entry.BaseValue,
			oldCurrentValue,
			entry.CurrentValue);

		return true;
	}

	private void RaiseChange(
		Entity entity,
		GameplayAttribute attribute,
		float oldBaseValue,
		float newBaseValue,
		float oldCurrentValue,
		float newCurrentValue)
	{
		AttributeChanged?.Invoke(new AttributeChange(
			entity,
			attribute,
			oldBaseValue,
			newBaseValue,
			oldCurrentValue,
			newCurrentValue));
	}

	private void HandleEntityDestroyed(Entity entity)
	{
		if (entity.AllocatorId != _allocatorId)
		{
			return;
		}

		if (!TryGetSlotLocation(entity, out int segmentIndex, out int offset))
		{
			return;
		}

		ref Slot slot = ref _segments[segmentIndex]![offset];

		ClearSlot(ref slot);
	}

	private static float Evaluate(ref AttributeEntry entry)
	{
		float addBase = 0f;
		float multiplyAdditive = 1f;
		float divideAdditive = 1f;
		float multiplyCompound = 1f;
		float addFinal = 0f;

		float overrideValue = 0f;
		long overrideSequence = long.MaxValue;
		bool hasOverride = false;

		for (int index = 0; index < entry.ModifierSlotCount; index++)
		{
			ref Modifier modifier = ref entry.Modifiers[index];

			if (!modifier.Active)
			{
				continue;
			}

			switch (modifier.Operation)
			{
				case GameplayModifierOperation.AddBase:
					addBase += modifier.Magnitude;
					break;

				case GameplayModifierOperation.MultiplyAdditive:
					multiplyAdditive += modifier.Magnitude - 1f;
					break;

				case GameplayModifierOperation.DivideAdditive:
					divideAdditive += modifier.Magnitude - 1f;
					break;

				case GameplayModifierOperation.MultiplyCompound:
					multiplyCompound *= modifier.Magnitude;
					break;

				case GameplayModifierOperation.AddFinal:
					addFinal += modifier.Magnitude;
					break;

				case GameplayModifierOperation.Override:
					if (!hasOverride || modifier.Sequence < overrideSequence)
					{
						hasOverride = true;
						overrideSequence = modifier.Sequence;
						overrideValue = modifier.Magnitude;
					}

					break;
			}
		}

		if (hasOverride)
		{
			return overrideValue;
		}

		if (divideAdditive == 0f)
		{
			// Division by an exactly zero aggregated divisor would
			// produce infinity. Unreal substitutes one; it also does
			// so for near-zero divisors, which VOLT deliberately does
			// not, because a configured 0.0001 divisor is meaningful.
			divideAdditive = 1f;
		}

		return ((entry.BaseValue + addBase)
			* multiplyAdditive
			/ divideAdditive
			* multiplyCompound)
			+ addFinal;
	}

	private static int AcquireModifierSlot(
		ref AttributeEntry entry,
		GameplayModifierOperation operation,
		float magnitude,
		long sequence)
	{
		int slot = -1;

		for (int index = 0; index < entry.ModifierSlotCount; index++)
		{
			if (!entry.Modifiers[index].Active)
			{
				slot = index;
				break;
			}
		}

		if (slot < 0)
		{
			slot = entry.ModifierSlotCount;

			if (slot == entry.Modifiers.Length)
			{
				Array.Resize(
					ref entry.Modifiers,
					Math.Max(
						InitialEntryCapacity,
						entry.Modifiers.Length * 2));
			}

			entry.ModifierSlotCount++;
		}

		ref Modifier modifier = ref entry.Modifiers[slot];

		if (modifier.Generation == 0)
		{
			modifier.Generation = 1;
		}

		modifier.Operation = operation;
		modifier.Magnitude = magnitude;
		modifier.Sequence = sequence;
		modifier.Active = true;

		entry.ActiveModifierCount++;

		return slot;
	}

	private static uint NextGeneration(uint generation)
	{
		generation++;

		return generation == 0 ? 1 : generation;
	}

	private int EnsureEntry(
		ref Slot slot,
		GameplayAttribute attribute,
		Entity entity)
	{
		int index = FindEntry(slot.Entries, slot.Count, attribute);

		if (index >= 0)
		{
			return index;
		}

		int insertion = ~index;

		if (slot.Count == slot.Entries.Length)
		{
			Array.Resize(
				ref slot.Entries,
				Math.Max(
					InitialEntryCapacity,
					slot.Entries.Length * 2));
		}

		Array.Copy(
			slot.Entries,
			insertion,
			slot.Entries,
			insertion + 1,
			slot.Count - insertion);

		GameplayAttributeDefinition definition =
			GameplayAttributeRegistry.Shared.GetDefinition(attribute);

		slot.Entries[insertion] = new AttributeEntry
		{
			Attribute = attribute,
			BaseValue = definition.DefaultBaseValue,
			CurrentValue = definition.DefaultBaseValue,
			Modifiers = [],
			ModifierSlotCount = 0,
			ActiveModifierCount = 0
		};

		slot.Count++;

		if (slot.DenseIndex < 0)
		{
			slot.DenseIndex = _denseCount;

			AppendDenseIndex((int)entity.Index);
		}

		return insertion;
	}

	private static int FindEntry(
		AttributeEntry[] entries,
		int count,
		GameplayAttribute attribute)
	{
		int low = 0;
		int high = count - 1;

		while (low <= high)
		{
			int middle = low + ((high - low) >> 1);
			uint middleId = entries[middle].Attribute.Id;
			uint id = attribute.Id;

			if (middleId == id)
			{
				return middle;
			}

			if (middleId < id)
			{
				low = middle + 1;
			}
			else
			{
				high = middle - 1;
			}
		}

		return ~low;
	}

	private ref Slot GetOrCreateSlot(Entity entity)
	{
		int entityIndex = (int)entity.Index;
		int segmentIndex = entityIndex >> SegmentShift;

		if (segmentIndex >= _segments.Length)
		{
			int capacity = Math.Max(segmentIndex + 1, _segments.Length * 2);

			Array.Resize(ref _segments, capacity);
		}

		Slot[]? segment = _segments[segmentIndex];

		if (segment is null)
		{
			segment = new Slot[SegmentSize];
			_segments[segmentIndex] = segment;
		}

		ref Slot slot = ref segment[entityIndex & SegmentMask];

		if (slot.Generation != entity.Generation)
		{
			slot.Generation = entity.Generation;
			slot.Count = 0;
			slot.DenseIndex = -1;
			slot.Entries ??= new AttributeEntry[InitialEntryCapacity];
		}

		return ref slot;
	}

	private bool TryGetSlotLocation(
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

		if (segmentIndex >= _segments.Length)
		{
			return false;
		}

		Slot[]? segment = _segments[segmentIndex];

		if (segment is null)
		{
			return false;
		}

		offset = entityIndex & SegmentMask;

		return segment[offset].Generation == entity.Generation;
	}

	private void ClearSlot(ref Slot slot)
	{
		if (slot.Count == 0)
		{
			return;
		}

		Array.Clear(slot.Entries, 0, slot.Count);

		slot.Count = 0;

		ReleaseDenseIndex(ref slot);
	}

	private void ReleaseDenseIndex(ref Slot slot)
	{
		int denseIndex = slot.DenseIndex;

		slot.DenseIndex = -1;

		if (denseIndex < 0 || denseIndex >= _denseCount)
		{
			return;
		}

		int last = _denseCount - 1;
		int movedEntityIndex = _denseIndices[last];

		if (denseIndex != last)
		{
			_denseIndices[denseIndex] = movedEntityIndex;

			int movedSegmentIndex = movedEntityIndex >> SegmentShift;

			ref Slot moved = ref _segments[movedSegmentIndex]![
				movedEntityIndex & SegmentMask];

			moved.DenseIndex = denseIndex;
		}

		_denseCount = last;
	}

	private void AppendDenseIndex(int entityIndex)
	{
		if (_denseCount == _denseIndices.Length)
		{
			Array.Resize(ref _denseIndices, _denseIndices.Length * 2);
		}

		_denseIndices[_denseCount++] = entityIndex;
	}

	private static GameplayAttributeDefinition GetDefinitionOrThrow(
		GameplayAttribute attribute)
	{
		if (!GameplayAttributeRegistry.Shared.TryGetDefinition(
			attribute,
			out GameplayAttributeDefinition definition))
		{
			throw new ArgumentException(
				"The gameplay attribute is not registered. Resolve " +
				"attributes through the registry before using them.",
				nameof(attribute));
		}

		return definition;
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}

	private struct Slot
	{
		public uint Generation;
		public int DenseIndex;
		public AttributeEntry[] Entries;
		public int Count;
	}

	private struct AttributeEntry
	{
		public GameplayAttribute Attribute;
		public float BaseValue;
		public float CurrentValue;
		public Modifier[] Modifiers;
		public int ModifierSlotCount;
		public int ActiveModifierCount;
	}

	private struct Modifier
	{
		public GameplayModifierOperation Operation;
		public float Magnitude;
		public long Sequence;
		public uint Generation;
		public bool Active;
	}
}
