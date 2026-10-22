
using Volt.Ecs;
using Xunit;

namespace Volt.Ecs.Tests;

public sealed class EntityAllocatorTests
{
	[Fact]
	public void NewAllocator_IsEmpty()
	{
		var allocator = new EntityAllocator();

		Assert.Equal(0, allocator.Count);
		Assert.Equal(0, allocator.Capacity);
	}

	[Fact]
	public void Create_ReturnsAliveEntity()
	{
		var allocator = new EntityAllocator();

		Entity entity = allocator.Create();

		Assert.True(allocator.IsAlive(entity));
		Assert.Equal(0u, entity.Index);
		Assert.Equal(1u, entity.Generation);
		Assert.Equal(1, allocator.Count);
		Assert.Equal(1, allocator.Capacity);
	}

	[Fact]
	public void CreateMultiple_ProducesUniqueIndices()
	{
		var allocator = new EntityAllocator();
		var entities = new HashSet<Entity>();

		for (int i = 0; i < 1000; i++)
		{
			Entity entity = allocator.Create();

			Assert.True(entities.Add(entity));
			Assert.True(allocator.IsAlive(entity));
		}

		Assert.Equal(1000, allocator.Count);
		Assert.Equal(1000, allocator.Capacity);
	}

	[Fact]
	public void Destroy_LivingEntity_ReturnsTrue()
	{
		var allocator = new EntityAllocator();
		Entity entity = allocator.Create();

		Assert.True(allocator.Destroy(entity));
		Assert.False(allocator.IsAlive(entity));
		Assert.Equal(0, allocator.Count);
		Assert.Equal(1, allocator.Capacity);
	}

	[Fact]
	public void Destroy_SameEntityTwice_ReturnsFalse()
	{
		var allocator = new EntityAllocator();
		Entity entity = allocator.Create();

		Assert.True(allocator.Destroy(entity));
		Assert.False(allocator.Destroy(entity));
		Assert.Equal(0, allocator.Count);
	}

	[Fact]
	public void Destroy_DefaultEntity_ReturnsFalse()
	{
		var allocator = new EntityAllocator();

		Assert.False(allocator.IsAlive(default));
		Assert.False(allocator.Destroy(default));
	}

	[Fact]
	public void Destroy_OutOfRangeEntity_ReturnsFalse()
	{
		var allocator = new EntityAllocator();
		var entity = new Entity(allocator.AllocatorId, uint.MaxValue, 1);

		Assert.False(allocator.IsAlive(entity));
		Assert.False(allocator.Destroy(entity));
	}

	[Fact]
	public void Destroy_WrongGeneration_ReturnsFalse()
	{
		var allocator = new EntityAllocator();
		Entity entity = allocator.Create();

		var forged = new Entity(allocator.AllocatorId, entity.Index, entity.Generation + 1);

		Assert.False(allocator.IsAlive(forged));
		Assert.False(allocator.Destroy(forged));

		Assert.True(allocator.IsAlive(entity));
		Assert.Equal(1, allocator.Count);
	}

	[Fact]
	public void Create_ReusesDestroyedIndex()
	{
		var allocator = new EntityAllocator();

		Entity original = allocator.Create();
		Assert.True(allocator.Destroy(original));
		Entity replacement = allocator.Create();

		Assert.Equal(original.Index, replacement.Index);
		Assert.Equal(original.Generation + 1, replacement.Generation);
		Assert.Equal(1, allocator.Count);
		Assert.Equal(1, allocator.Capacity);
	}

	[Fact]
	public void ReusedIndex_DoesNotReviveOldHandle()
	{
		var allocator = new EntityAllocator();

		Entity original = allocator.Create();
		allocator.Destroy(original);
		Entity replacement = allocator.Create();

		Assert.False(allocator.IsAlive(original));
		Assert.False(allocator.Destroy(original));
		Assert.True(allocator.IsAlive(replacement));
	}

	[Fact]
	public void RecycledIndices_AreReusedInLifoOrder()
	{
		var allocator = new EntityAllocator();

		Entity first = allocator.Create();
		Entity second = allocator.Create();
		Entity third = allocator.Create();

		allocator.Destroy(first);
		allocator.Destroy(second);
		allocator.Destroy(third);

		Entity a = allocator.Create();
		Entity b = allocator.Create();
		Entity c = allocator.Create();

		Assert.Equal(third.Index, a.Index);
		Assert.Equal(second.Index, b.Index);
		Assert.Equal(first.Index, c.Index);
	}

	[Fact]
	public void Destroy_DoesNotInvalidateOtherEntities()
	{
		var allocator = new EntityAllocator();

		Entity first = allocator.Create();
		Entity second = allocator.Create();
		allocator.Destroy(first);

		Assert.False(allocator.IsAlive(first));
		Assert.True(allocator.IsAlive(second));
		Assert.Equal(1, allocator.Count);
	}

	[Fact]
	public void EnsureCapacity_DoesNotCreateEntities()
	{
		var allocator = new EntityAllocator();
		allocator.EnsureCapacity(1000);

		Assert.Equal(0, allocator.Count);
		Assert.Equal(0, allocator.Capacity);
	}

	[Fact]
	public void EnsureCapacity_NegativeValue_Throws()
	{
		var allocator = new EntityAllocator();

		Assert.Throws<ArgumentOutOfRangeException>(
			() => allocator.EnsureCapacity(-1));
	}

	[Fact]
	public void CreateDestroyRepeatedly_IncrementsGeneration()
	{
		var allocator = new EntityAllocator();
		Entity entity = allocator.Create();

		for (uint generation = 1; generation <= 1000; generation++)
		{
			Assert.Equal(generation, entity.Generation);
			Assert.True(allocator.IsAlive(entity));

			Assert.True(allocator.Destroy(entity));
			Assert.False(allocator.IsAlive(entity));

			entity = allocator.Create();
			Assert.Equal(0u, entity.Index);
		}

		Assert.Equal(1001u, entity.Generation);
		Assert.Equal(1, allocator.Count);
		Assert.Equal(1, allocator.Capacity);
	}

	[Fact]
	public void MixedOperations_MaintainCorrectCount()
	{
		var allocator = new EntityAllocator();

		Entity a = allocator.Create();
		Entity b = allocator.Create();
		Entity c = allocator.Create();

		Assert.Equal(3, allocator.Count);

		Assert.True(allocator.Destroy(b));
		Assert.Equal(2, allocator.Count);

		Entity d = allocator.Create();
		Assert.Equal(3, allocator.Count);

		Assert.True(allocator.Destroy(a));
		Assert.True(allocator.Destroy(c));
		Assert.Equal(1, allocator.Count);

		Assert.True(allocator.IsAlive(d));
		Assert.False(allocator.IsAlive(a));
		Assert.False(allocator.IsAlive(b));
		Assert.False(allocator.IsAlive(c));
	}

	[Fact]
	public void ManyEntities_DestroyAndRecycleAll()
	{
		const int entityCount = 10_000;

		var allocator = new EntityAllocator();
		var entities = new Entity[entityCount];

		for (int i = 0; i < entityCount; i++)
		{
			entities[i] = allocator.Create();
		}

		Assert.Equal(entityCount, allocator.Count);

		foreach (Entity entity in entities)
		{
			Assert.True(allocator.Destroy(entity));
		}

		Assert.Equal(0, allocator.Count);

		for (int i = 0; i < entityCount; i++)
		{
			Entity replacement = allocator.Create();

			Assert.True(allocator.IsAlive(replacement));
			Assert.Equal(2u, replacement.Generation);
		}

		Assert.Equal(entityCount, allocator.Count);
		Assert.Equal(entityCount, allocator.Capacity);
	}


	[Fact]
	public void NewAllocators_HaveUniqueNonzeroIds()
	{
		var first = new EntityAllocator();
		var second = new EntityAllocator();

		Assert.NotEqual(0u, first.AllocatorId);
		Assert.NotEqual(0u, second.AllocatorId);
		Assert.NotEqual(first.AllocatorId, second.AllocatorId);
	}

	[Fact]
	public void Create_AssignsAllocatorId()
	{
		var allocator = new EntityAllocator();
		Entity entity = allocator.Create();

		Assert.Equal(allocator.AllocatorId, entity.AllocatorId);
		Assert.True(allocator.IsAlive(entity));
	}

	[Fact]
	public void DifferentAllocators_ProduceDistinctEntities()
	{
		var first = new EntityAllocator();
		var second = new EntityAllocator();

		Entity a = first.Create();
		Entity b = second.Create();

		Assert.Equal(a.Index, b.Index);
		Assert.Equal(a.Generation, b.Generation);

		Assert.NotEqual(a.AllocatorId, b.AllocatorId);
		Assert.NotEqual(a, b);
		Assert.False(a == b);
		Assert.True(a != b);
	}

	[Fact]
	public void IsAlive_RejectsForeignEntity()
	{
		var first = new EntityAllocator();
		var second = new EntityAllocator();

		Entity a = first.Create();
		Entity b = second.Create();

		Assert.True(first.IsAlive(a));
		Assert.True(second.IsAlive(b));

		Assert.False(first.IsAlive(b));
		Assert.False(second.IsAlive(a));
	}

	[Fact]
	public void Destroy_RejectsForeignEntity()
	{
		var first = new EntityAllocator();
		var second = new EntityAllocator();

		Entity a = first.Create();
		Entity b = second.Create();

		Assert.False(first.Destroy(b));
		Assert.False(second.Destroy(a));

		Assert.True(first.IsAlive(a));
		Assert.True(second.IsAlive(b));

		Assert.Equal(1, first.Count);
		Assert.Equal(1, second.Count);
	}

	[Fact]
	public void Destroy_ForeignEntity_DoesNotRecycleLocalSlot()
	{
		var first = new EntityAllocator();
		var second = new EntityAllocator();

		Entity local = first.Create();
		Entity foreign = second.Create();

		Assert.False(first.Destroy(foreign));

		Entity next = first.Create();

		Assert.Equal(1u, next.Index);
		Assert.Equal(1u, next.Generation);
		Assert.Equal(first.AllocatorId, next.AllocatorId);

		Assert.True(first.IsAlive(local));
		Assert.True(first.IsAlive(next));
		Assert.Equal(2, first.Count);
	}

	[Fact]
	public void ForgedAllocatorId_IsRejected()
	{
		var allocator = new EntityAllocator();
		Entity original = allocator.Create();

		var forged = new Entity(0, original.Index, original.Generation);

		Assert.False(allocator.IsAlive(forged));
		Assert.False(allocator.Destroy(forged));

		Assert.True(allocator.IsAlive(original));
		Assert.Equal(1, allocator.Count);
	}

	[Fact]
	public void RecycledEntity_PreservesAllocatorIdentity()
	{
		var allocator = new EntityAllocator();
		Entity original = allocator.Create();

		Assert.True(allocator.Destroy(original));
		Entity replacement = allocator.Create();

		Assert.Equal(original.AllocatorId, replacement.AllocatorId);
		Assert.Equal(original.Index, replacement.Index);
		Assert.Equal(original.Generation + 1, replacement.Generation);

		Assert.False(allocator.IsAlive(original));
		Assert.True(allocator.IsAlive(replacement));
	}

	[Fact]
	public void EntityEquality_IncludesAllocatorId()
	{
		var first = new Entity(1, 42, 7);
		var second = new Entity(2, 42, 7);
		var equivalent = new Entity(1, 42, 7);

		Assert.NotEqual(first, second);
		Assert.Equal(first, equivalent);

		Assert.True(first == equivalent);
		Assert.False(first != equivalent);

		Assert.False(first == second);
		Assert.True(first != second);
	}

	[Fact]
	public void EntityHashSet_DistinguishesAllocatorIds()
	{
		var entities = new HashSet<Entity>
		{
			new(1, 42, 7),
			new(2, 42, 7),
			new(1, 42, 7),
		};

		Assert.Equal(2, entities.Count);
	}

	[Fact]
	public void ConcurrentAllocatorCreation_ProducesUniqueIds()
	{
		const int allocatorCount = 1000;
		uint[] ids = new uint[allocatorCount];

		Parallel.For(0, allocatorCount, i =>
		{
			var allocator = new EntityAllocator();
			ids[i] = allocator.AllocatorId;
		});

		Assert.DoesNotContain(0u, ids);
		Assert.Equal(allocatorCount, ids.Distinct().Count());
	}
}
