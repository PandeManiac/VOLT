
using System.Threading;

namespace Volt.Ecs;

/// <summary>
/// Creates and destroys generational entity identifiers.
/// Recycles entity indices while protecting against stale
/// handles and handles belonging to other allocators.
/// </summary>
/// <remarks>
/// Entity creation, destruction, and validation are not
/// thread-safe. Concurrent structural changes will be
/// coordinated by the ECS world in a later stage.
///
/// Allocator identity assignment is thread-safe.
/// </remarks>
public sealed class EntityAllocator
{
	private const uint InitialGeneration = 1;

	private readonly List<Slot> _slots = new();
	private readonly Stack<uint> _freeIndices = new();

	private int _count;

	/// <summary>
	/// Gets the unique identity of this allocator.
	/// </summary>
	public uint AllocatorId { get; }

	/// <summary>
	/// Gets the number of currently living entities.
	/// </summary>
	public int Count => _count;

	/// <summary>
	/// Gets the number of entity slots created so far,
	/// including living, recycled, and retired slots.
	/// </summary>
	public int Capacity => _slots.Count;

	// A process-wide, monotonically increasing allocator ID.
	// Never wraps or reuses an ID.
	private static long s_nextAllocatorId;

	private struct Slot(uint generation)
	{
		public uint Generation = generation;
		public bool IsAlive = true;
	}

	public EntityAllocator()
	{
		long id = Interlocked.Increment(ref s_nextAllocatorId);

		if (id > uint.MaxValue)
		{
			throw new InvalidOperationException(
				"The process has exhausted its entity allocator IDs.");
		}

		AllocatorId = (uint)id;
	}

	/// <summary>
	/// Creates a new entity, reusing a destroyed slot
	/// whenever one is available.
	/// </summary>
	public Entity Create()
	{
		if (_freeIndices.TryPop(out uint index))
		{
			int slotIndex = (int)index;

			Slot slot = _slots[slotIndex];
			slot.IsAlive = true;
			_slots[slotIndex] = slot;

			_count++;

			return new Entity(AllocatorId, index, slot.Generation);
		}

		if (_slots.Count == int.MaxValue)
		{
			throw new InvalidOperationException(
				"The entity allocator has exhausted its index space.");
		}

		uint newIndex = (uint)_slots.Count;

		_slots.Add(new Slot(InitialGeneration));
		_count++;

		return new Entity(AllocatorId, newIndex, InitialGeneration);
	}

	/// <summary>
	/// Destroys an entity if it belongs to this allocator
	/// and is currently alive.
	/// </summary>
	/// <returns>
	/// True if the entity was destroyed; otherwise false.
	/// </returns>
	public bool Destroy(Entity entity)
	{
		if (!IsAlive(entity))
		{
			return false;
		}

		int index = (int)entity.Index;

		Slot slot = _slots[index];
		slot.IsAlive = false;

		if (slot.Generation == uint.MaxValue)
		{
			slot.Generation = 0;
		}

		else
		{
			slot.Generation++;
			_freeIndices.Push(entity.Index);
		}

		_slots[index] = slot;
		_count--;

		return true;
	}

	/// <summary>
	/// Returns whether an entity belongs to this allocator
	/// and is currently alive.
	/// </summary>
	/// <remarks>
	/// Runs in O(1) time without scanning recycled slots.
	/// </remarks>
	public bool IsAlive(Entity entity)
	{
		if (entity.AllocatorId != AllocatorId)
		{
			return false;
		}

		if (entity.Index >= (uint)_slots.Count)
		{
			return false;
		}

		Slot slot = _slots[(int)entity.Index];

		return slot.IsAlive
			&& slot.Generation == entity.Generation;
	}

	/// <summary>
	/// Reserves storage for at least the specified number
	/// of entity slots without creating any entities.
	/// </summary>
	public void EnsureCapacity(int capacity)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(capacity);

		_slots.EnsureCapacity(capacity);
		_freeIndices.EnsureCapacity(capacity);
	}
}
