using Volt.Ecs;

namespace Volt.Gameplay.Tags;

/// <summary>
/// Stores reference-counted gameplay tags for the entities of one
/// world.
/// </summary>
/// <remarks>
/// <para>
/// Tags are sparse state. The store keeps a slot per entity index in
/// lazily allocated segments of 1,024 slots, so tagging one entity in
/// a million-entity world cost one segment rather than a
/// million-entry table, while lookup stays a direct index instead of
/// a hash.
/// </para>
/// <para>
/// Each entity holds a sorted array of tag entries. An entry records
/// how many grants name the tag explicitly ("this entity has
/// <c>Status.Stunned</c>") and how many name it or a descendant ("this
/// entity satisfies <c>Status</c>"). Adding a tag increments both its
/// own entry and every ancestor's total; removing decrements the same
/// entries and drops the ones that reach zero. Two effects that grant
/// the same tag therefore produce a count of two, and removing one
/// leaves the tag present.
/// </para>
/// <para>
/// Handles are validated by generation, so a destroyed entity's index
/// cannot be read through a stale handle even after the index is
/// reused. Destruction is also delivered through
/// <see cref="World.EntityDestroyed"/>, which clears the slot and
/// releases its dense-index entry immediately instead of waiting for
/// reuse.
/// </para>
/// <para>
/// The store is single-threaded, like the effects and abilities that
/// will use it. Reads after all mutations complete are safe from other
/// threads only when the caller provides the usual publication
/// synchronization; the store does not hide a lock in the hot path.
/// </para>
/// </remarks>
public sealed class GameplayTagStore : IDisposable
{
	private const int SegmentShift = 10;
	private const int SegmentSize = 1 << SegmentShift;
	private const int SegmentMask = SegmentSize - 1;
	private const int InitialEntryCapacity = 4;

	private readonly World _world;
	private readonly uint _allocatorId;

	private Slot[]?[] _segments = [];
	private int[] _taggedIndices = new int[16];
	private int _taggedCount;

	private GameplayTag[] _explicitScratch = new GameplayTag[InitialEntryCapacity];
	private GameplayTag[] _presentScratch = new GameplayTag[InitialEntryCapacity];

	private bool _disposed;

	/// <summary>
	/// Creates tag storage for a world and subscribes to its entity
	/// destruction notifications.
	/// </summary>
	/// <param name="world">The world whose entities can be tagged.</param>
	/// <exception cref="ArgumentNullException">
	/// The world is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// Dispose the store before disposing the world.
	/// </remarks>
	public GameplayTagStore(World world)
	{
		ArgumentNullException.ThrowIfNull(world);

		_world = world;
		_allocatorId = world.AllocatorId;

		_world.EntityDestroyed += HandleEntityDestroyed;
	}

	/// <summary>
	/// Gets the number of entities that currently have at least one
	/// tag.
	/// </summary>
	public int TaggedEntityCount => _taggedCount;

	/// <summary>
	/// Adds one grant of a tag to an entity.
	/// </summary>
	/// <param name="entity">The living entity.</param>
	/// <param name="tag">The tag to grant.</param>
	/// <returns>
	/// <see langword="true"/> when the grant was recorded;
	/// <see langword="false"/> for the invalid tag.
	/// </returns>
	/// <exception cref="ArgumentException">
	/// The entity is not alive in this store's world.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// The store is disposed.
	/// </exception>
	/// <remarks>
	/// Grants are counted, not set. Adding the same tag twice requires
	/// two removals. Tagging an entity that had none allocates its
	/// entry array; adding a tag the entity already has does not.
	/// </remarks>
	public bool AddTag(Entity entity, GameplayTag tag)
	{
		ThrowIfDisposed();

		if (!tag.IsValid)
		{
			return false;
		}

		if (!_world.IsAlive(entity))
		{
			throw new ArgumentException(
				"The entity is not alive in this store's world. A tag " +
				"cannot be granted to a destroyed or recycled handle.",
				nameof(entity));
		}

		ref Slot slot = ref GetOrCreateSlot(entity);

		int entryIndex = EnsureEntry(ref slot.Entries, ref slot.Count, tag);

		ref TagEntry entry = ref slot.Entries[entryIndex];

		entry.ExplicitCount++;
		entry.TotalCount++;

		foreach (GameplayTag ancestor in GameplayTagRegistry.Shared.GetAncestors(tag))
		{
			int ancestorIndex = EnsureEntry(
				ref slot.Entries,
				ref slot.Count,
				ancestor);

			slot.Entries[ancestorIndex].TotalCount++;
		}

		if (slot.DenseIndex < 0)
		{
			slot.DenseIndex = _taggedCount;

			AppendTaggedIndex((int)entity.Index);
		}

		return true;
	}

	/// <summary>
	/// Removes one grant of a tag from an entity.
	/// </summary>
	/// <param name="entity">The entity.</param>
	/// <param name="tag">The tag to remove.</param>
	/// <returns>
	/// <see langword="true"/> when a grant was removed;
	/// <see langword="false"/> when the entity has no explicit grant
	/// of the tag or is not alive.
	/// </returns>
	/// <remarks>
	/// Ancestor entries are decremented as well and dropped when they
	/// reach zero, so a parent stops matching exactly when the last
	/// descendant grant is removed.
	/// </remarks>
	public bool RemoveTag(Entity entity, GameplayTag tag)
	{
		ThrowIfDisposed();

		if (!tag.IsValid || !TryGetSlotLocation(entity, out int segmentIndex, out int offset))
		{
			return false;
		}

		ref Slot slot = ref _segments[segmentIndex]![offset];

		int entryIndex = FindEntry(slot.Entries, slot.Count, tag);

		if (entryIndex < 0 || slot.Entries[entryIndex].ExplicitCount == 0)
		{
			return false;
		}

		slot.Entries[entryIndex].ExplicitCount--;
		slot.Entries[entryIndex].TotalCount--;

		foreach (GameplayTag ancestor in GameplayTagRegistry.Shared.GetAncestors(tag))
		{
			int ancestorIndex = FindEntry(slot.Entries, slot.Count, ancestor);

			// The ancestor entry always exists while a descendant is
			// granted. The guard keeps a future invariant violation
			// from producing a negative count that would make the
			// parent match forever.
			if (ancestorIndex >= 0 && slot.Entries[ancestorIndex].TotalCount > 0)
			{
				slot.Entries[ancestorIndex].TotalCount--;
			}
		}

		Compact(ref slot);

		if (slot.Count == 0)
		{
			ReleaseDenseIndex(ref slot);
		}

		return true;
	}

	/// <summary>
	/// Determines whether an entity satisfies a tag, directly or
	/// through a granted descendant.
	/// </summary>
	/// <param name="entity">The entity.</param>
	/// <param name="tag">The tag to test for.</param>
	/// <returns>
	/// <see langword="false"/> for the invalid tag, a dead handle, or
	/// an entity with no matching grant.
	/// </returns>
	public bool HasTag(Entity entity, GameplayTag tag)
	{
		ThrowIfDisposed();

		if (!tag.IsValid || !TryGetSlotLocation(entity, out int segmentIndex, out int offset))
		{
			return false;
		}

		ref Slot slot = ref _segments[segmentIndex]![offset];

		int entryIndex = FindEntry(slot.Entries, slot.Count, tag);

		return entryIndex >= 0 && slot.Entries[entryIndex].TotalCount > 0;
	}

	/// <summary>
	/// Determines whether an entity has an explicit grant of a tag.
	/// </summary>
	/// <param name="entity">The entity.</param>
	/// <param name="tag">The tag to test for.</param>
	public bool HasTagExact(Entity entity, GameplayTag tag)
	{
		ThrowIfDisposed();

		if (!tag.IsValid || !TryGetSlotLocation(entity, out int segmentIndex, out int offset))
		{
			return false;
		}

		ref Slot slot = ref _segments[segmentIndex]![offset];

		int entryIndex = FindEntry(slot.Entries, slot.Count, tag);

		return entryIndex >= 0 && slot.Entries[entryIndex].ExplicitCount > 0;
	}

	/// <summary>
	/// Gets the number of explicit grants of a tag.
	/// </summary>
	/// <param name="entity">The entity.</param>
	/// <param name="tag">The tag.</param>
	/// <returns>
	/// The count, or zero when no grant is present.
	/// </returns>
	public int GetTagCount(Entity entity, GameplayTag tag)
	{
		ThrowIfDisposed();

		if (!tag.IsValid || !TryGetSlotLocation(entity, out int segmentIndex, out int offset))
		{
			return 0;
		}

		ref Slot slot = ref _segments[segmentIndex]![offset];

		int entryIndex = FindEntry(slot.Entries, slot.Count, tag);

		return entryIndex >= 0 ? slot.Entries[entryIndex].ExplicitCount : 0;
	}

	/// <summary>
	/// Determines whether an entity satisfies any tag of a set.
	/// </summary>
	/// <param name="entity">The entity.</param>
	/// <param name="tags">The tags to test for.</param>
	public bool HasAny(Entity entity, GameplayTagSet tags)
	{
		ArgumentNullException.ThrowIfNull(tags);

		foreach (GameplayTag tag in tags.Tags)
		{
			if (HasTag(entity, tag))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Determines whether an entity satisfies every tag of a set.
	/// </summary>
	/// <param name="entity">The entity.</param>
	/// <param name="tags">The tags to test for.</param>
	/// <returns>
	/// <see langword="true"/> when the set is empty.
	/// </returns>
	public bool HasAll(Entity entity, GameplayTagSet tags)
	{
		ArgumentNullException.ThrowIfNull(tags);

		foreach (GameplayTag tag in tags.Tags)
		{
			if (!HasTag(entity, tag))
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Determines whether an entity satisfies any tag of a set
	/// exactly.
	/// </summary>
	/// <param name="entity">The entity.</param>
	/// <param name="tags">The tags to test for.</param>
	public bool HasAnyExact(Entity entity, GameplayTagSet tags)
	{
		ArgumentNullException.ThrowIfNull(tags);

		foreach (GameplayTag tag in tags.Tags)
		{
			if (HasTagExact(entity, tag))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Determines whether an entity satisfies every tag of a set
	/// exactly.
	/// </summary>
	/// <param name="entity">The entity.</param>
	/// <param name="tags">The tags to test for.</param>
	public bool HasAllExact(Entity entity, GameplayTagSet tags)
	{
		ArgumentNullException.ThrowIfNull(tags);

		foreach (GameplayTag tag in tags.Tags)
		{
			if (!HasTagExact(entity, tag))
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Evaluates a tag query against an entity.
	/// </summary>
	/// <param name="entity">The entity.</param>
	/// <param name="query">The query to evaluate.</param>
	/// <returns>
	/// The query result. An entity with no tags evaluates as an empty
	/// container: <c>AllTags()</c> matches, <c>AnyTags()</c> does not.
	/// </returns>
	/// <remarks>
	/// Evaluation reuses store-owned scratch arrays, so a warmed-up
	/// query of any depth allocates nothing. That makes the store
	/// single-threaded for queries, which the gameplay runtime already
	/// requires.
	/// </remarks>
	public bool Matches(Entity entity, GameplayTagQuery query)
	{
		ArgumentNullException.ThrowIfNull(query);
		ThrowIfDisposed();

		if (!TryGetSlotLocation(entity, out int segmentIndex, out int offset))
		{
			return query.Matches(
				ReadOnlySpan<GameplayTag>.Empty,
				ReadOnlySpan<GameplayTag>.Empty);
		}

		ref Slot slot = ref _segments[segmentIndex]![offset];

		EnsureScratchCapacity(slot.Count);

		int explicitCount = 0;
		int presentCount = 0;

		for (int index = 0; index < slot.Count; index++)
		{
			ref TagEntry entry = ref slot.Entries[index];

			if (entry.ExplicitCount > 0)
			{
				_explicitScratch[explicitCount++] = entry.Tag;
			}

			if (entry.TotalCount > 0)
			{
				_presentScratch[presentCount++] = entry.Tag;
			}
		}

		return query.Matches(
			_explicitScratch.AsSpan(0, explicitCount),
			_presentScratch.AsSpan(0, presentCount));
	}

	/// <summary>
	/// Copies an entity's explicit tags into an immutable set.
	/// </summary>
	/// <param name="entity">The entity.</param>
	/// <returns>
	/// The explicitly granted tags, or the empty set.
	/// </returns>
	/// <remarks>
	/// The set contains explicit grants only, exactly like a
	/// container's explicit tags; ancestors are cached by the set.
	/// This is an inspection API and allocates.
	/// </remarks>
	public GameplayTagSet GetTagSet(Entity entity)
	{
		ThrowIfDisposed();

		if (!TryGetSlotLocation(entity, out int segmentIndex, out int offset))
		{
			return GameplayTagSet.Empty;
		}

		ref Slot slot = ref _segments[segmentIndex]![offset];

		int count = 0;

		for (int index = 0; index < slot.Count; index++)
		{
			if (slot.Entries[index].ExplicitCount > 0)
			{
				count++;
			}
		}

		if (count == 0)
		{
			return GameplayTagSet.Empty;
		}

		GameplayTag[] tags = new GameplayTag[count];
		int destination = 0;

		for (int index = 0; index < slot.Count; index++)
		{
			if (slot.Entries[index].ExplicitCount > 0)
			{
				tags[destination++] = slot.Entries[index].Tag;
			}
		}

		return GameplayTagSet.Create(tags);
	}

	/// <summary>
	/// Removes every tag and grant from an entity.
	/// </summary>
	/// <param name="entity">The entity.</param>
	/// <returns>
	/// <see langword="true"/> when the entity had at least one tag.
	/// </returns>
	/// <remarks>
	/// The entry array is kept for reuse, so tagging the same entity
	/// again does not allocate unless it needs more capacity.
	/// </remarks>
	public bool Clear(Entity entity)
	{
		ThrowIfDisposed();

		if (!TryGetSlotLocation(entity, out int segmentIndex, out int offset))
		{
			return false;
		}

		ref Slot slot = ref _segments[segmentIndex]![offset];

		bool hadTags = slot.Count > 0;

		ClearSlot(ref slot);

		return hadTags;
	}

	/// <summary>
	/// Copies the handles of all tagged entities into a span.
	/// </summary>
	/// <param name="destination">The destination span.</param>
	/// <returns>
	/// The number of handles written, which is less than
	/// <see cref="TaggedEntityCount"/> when the span is too small.
	/// </returns>
	/// <remarks>
	/// The copy follows the store's dense index, so its cost is
	/// proportional to the number of tagged entities rather than to
	/// the number of entities in the world.
	/// </remarks>
	public int CopyTaggedEntities(Span<Entity> destination)
	{
		ThrowIfDisposed();

		int count = Math.Min(destination.Length, _taggedCount);

		for (int index = 0; index < count; index++)
		{
			int entityIndex = _taggedIndices[index];
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

		_segments = [];
		_taggedCount = 0;
		_disposed = true;

		GC.SuppressFinalize(this);
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
			// The index was never used, or it belonged to an older
			// generation whose destruction may never have been
			// observed. The previous contents are unreachable and are
			// discarded; the entry array is reused.
			slot.Generation = entity.Generation;
			slot.Count = 0;
			slot.DenseIndex = -1;
			slot.Entries ??= new TagEntry[InitialEntryCapacity];
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
		if (slot.Count > 0)
		{
			Array.Clear(slot.Entries, 0, slot.Count);
			slot.Count = 0;

			ReleaseDenseIndex(ref slot);
		}
	}

	private void ReleaseDenseIndex(ref Slot slot)
	{
		int denseIndex = slot.DenseIndex;

		slot.DenseIndex = -1;

		if (denseIndex < 0 || denseIndex >= _taggedCount)
		{
			return;
		}

		int last = _taggedCount - 1;
		int movedEntityIndex = _taggedIndices[last];

		if (denseIndex != last)
		{
			_taggedIndices[denseIndex] = movedEntityIndex;

			int movedSegmentIndex = movedEntityIndex >> SegmentShift;

			ref Slot moved = ref _segments[movedSegmentIndex]![
				movedEntityIndex & SegmentMask];

			moved.DenseIndex = denseIndex;
		}

		_taggedCount = last;
	}

	private void AppendTaggedIndex(int entityIndex)
	{
		if (_taggedCount == _taggedIndices.Length)
		{
			Array.Resize(ref _taggedIndices, _taggedIndices.Length * 2);
		}

		_taggedIndices[_taggedCount++] = entityIndex;
	}

	private void EnsureScratchCapacity(int required)
	{
		if (required <= _explicitScratch.Length)
		{
			return;
		}

		int capacity = Math.Max(required, _explicitScratch.Length * 2);

		Array.Resize(ref _explicitScratch, capacity);
		Array.Resize(ref _presentScratch, capacity);
	}

	private static int EnsureEntry(
		ref TagEntry[] entries,
		ref int count,
		GameplayTag tag)
	{
		int index = FindEntry(entries, count, tag);

		if (index >= 0)
		{
			return index;
		}

		int insertion = ~index;

		if (count == entries.Length)
		{
			Array.Resize(ref entries, entries.Length * 2);
		}

		Array.Copy(entries, insertion, entries, insertion + 1, count - insertion);

		entries[insertion] = new TagEntry { Tag = tag };
		count++;

		return insertion;
	}

	private static int FindEntry(
		TagEntry[] entries,
		int count,
		GameplayTag tag)
	{
		int low = 0;
		int high = count - 1;

		while (low <= high)
		{
			int middle = low + ((high - low) >> 1);
			uint middleId = entries[middle].Tag.Id;
			uint id = tag.Id;

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

	private static void Compact(ref Slot slot)
	{
		TagEntry[] entries = slot.Entries;
		int count = slot.Count;
		int write = 0;

		for (int read = 0; read < count; read++)
		{
			ref TagEntry entry = ref entries[read];

			if (entry.TotalCount <= 0)
			{
				continue;
			}

			if (write != read)
			{
				entries[write] = entry;
			}

			write++;
		}

		for (int index = write; index < count; index++)
		{
			entries[index] = default;
		}

		slot.Count = write;
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}

	private struct Slot
	{
		public uint Generation;
		public int DenseIndex;
		public TagEntry[] Entries;
		public int Count;
	}

	private struct TagEntry
	{
		public GameplayTag Tag;
		public int ExplicitCount;
		public int TotalCount;
	}
}
