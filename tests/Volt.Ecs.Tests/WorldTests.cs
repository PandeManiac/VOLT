using Volt.Ecs;
using Xunit;

namespace Volt.Ecs.Tests;

public sealed class WorldTests
{
	private struct Position
	{
		public int X;
		public int Y;
	}

	private struct Velocity
	{
		public float X;
		public float Y;
	}

	private struct Health
	{
		public int Value;
	}

	private struct Tag
	{
		public byte Value;
	}

	private struct LargeComponent
	{
		public long A;
		public long B;
		public long C;
		public long D;
	}

	[Fact]
	public void NewWorld_IsEmpty()
	{
		using World world = new();
		Assert.Equal(0, world.Count);
	}

	[Fact]
	public void CreateEntity_ReturnsLivingEntity()
	{
		using World world = new();
		Entity entity = world.CreateEntity();

		Assert.True(world.IsAlive(entity));
		Assert.Equal(1, world.Count);
	}

	[Fact]
	public void CreateEntity_ReturnsDistinctHandles()
	{
		using World world = new();

		Entity first = world.CreateEntity();
		Entity second = world.CreateEntity();

		Assert.NotEqual(first, second);
		Assert.True(world.IsAlive(first));
		Assert.True(world.IsAlive(second));
		Assert.Equal(2, world.Count);
	}

	[Fact]
	public void DefaultEntity_IsNotAlive()
	{
		using World world = new();

		Assert.False(world.IsAlive(default));
		Assert.False(world.DestroyEntity(default));
	}

	[Fact]
	public void DestroyEntity_ChangesLifetimeAndCount()
	{
		using World world = new();
		Entity entity = world.CreateEntity();

		Assert.True(world.DestroyEntity(entity));
		Assert.False(world.IsAlive(entity));
		Assert.Equal(0, world.Count);
	}

	[Fact]
	public void DestroyEntity_Twice_ReturnsFalse()
	{
		using World world = new();
		Entity entity = world.CreateEntity();

		Assert.True(world.DestroyEntity(entity));
		Assert.False(world.DestroyEntity(entity));
	}

	[Fact]
	public void RecycledEntity_DoesNotReviveOldHandle()
	{
		using World world = new();
		Entity oldEntity = world.CreateEntity();

		Assert.True(world.DestroyEntity(oldEntity));

		Entity replacement = world.CreateEntity();

		Assert.Equal(oldEntity.Index, replacement.Index);
		Assert.NotEqual(oldEntity, replacement);
		Assert.False(world.IsAlive(oldEntity));
		Assert.True(world.IsAlive(replacement));
	}

	[Fact]
	public void DifferentWorlds_RejectEachOthersEntities()
	{
		using World first = new();
		using World second = new();

		Entity a = first.CreateEntity();
		Entity b = second.CreateEntity();

		Assert.NotEqual(a, b);
		Assert.False(first.IsAlive(b));
		Assert.False(second.IsAlive(a));
		Assert.False(first.DestroyEntity(b));
		Assert.False(second.DestroyEntity(a));
		Assert.True(first.IsAlive(a));
		Assert.True(second.IsAlive(b));
	}

	[Fact]
	public void NewEntity_HasNoComponents()
	{
		using World world = new();
		Entity entity = world.CreateEntity();

		Assert.False(world.HasComponent<Position>(entity));
		Assert.False(world.HasComponent<Health>(entity));
	}

	[Fact]
	public void AddComponent_MakesValueAvailable()
	{
		using World world = new();
		Entity entity = world.CreateEntity();
		world.AddComponent(entity, new Position { X = 12, Y = -7 });

		Assert.True(world.HasComponent<Position>(entity));
		Assert.Equal(12, world.GetComponent<Position>(entity).X);
		Assert.Equal(-7, world.GetComponent<Position>(entity).Y);
		Assert.Equal(1, world.Count);
	}

	[Fact]
	public void AddComponent_Duplicate_ThrowsWithoutChangingValue()
	{
		using World world = new();
		Entity entity = world.CreateEntity();
		world.AddComponent(entity, new Health { Value = 42 });

		Assert.Throws<InvalidOperationException>(() => world.AddComponent(entity, new Health { Value = 99 }));
		Assert.Equal(42, world.GetComponent<Health>(entity).Value);
	}

	[Fact]
	public void AddComponent_PreservesExistingComponents()
	{
		using World world = new();
		Entity entity = world.CreateEntity();

		world.AddComponent(entity, new Position { X = 11, Y = 22 });
		world.AddComponent(entity, new Health { Value = 83 });
		world.AddComponent(entity, new Velocity { X = 1.5f, Y = -2.5f });

		Assert.Equal(11, world.GetComponent<Position>(entity).X);
		Assert.Equal(22, world.GetComponent<Position>(entity).Y);
		Assert.Equal(83, world.GetComponent<Health>(entity).Value);
		Assert.Equal(1.5f, world.GetComponent<Velocity>(entity).X);
		Assert.Equal(-2.5f, world.GetComponent<Velocity>(entity).Y);
	}

	[Fact]
	public void RemoveComponent_RemovesOnlyRequestedComponent()
	{
		using World world = new();
		Entity entity = world.CreateEntity();

		world.AddComponent(entity, new Position { X = 6, Y = 9 });
		world.AddComponent(entity, new Health { Value = 75 });

		Assert.True(world.RemoveComponent<Position>(entity));
		Assert.False(world.HasComponent<Position>(entity));
		Assert.True(world.HasComponent<Health>(entity));
		Assert.Equal(75, world.GetComponent<Health>(entity).Value);
		Assert.True(world.IsAlive(entity));
	}

	[Fact]
	public void RemoveComponent_Missing_ReturnsFalse()
	{
		using World world = new();
		Entity entity = world.CreateEntity();

		Assert.False(world.RemoveComponent<Position>(entity));
		Assert.True(world.IsAlive(entity));
	}

	[Fact]
	public void RemoveComponent_LastComponent_ReturnsToEmptyArchetype()
	{
		using World world = new();
		Entity entity = world.CreateEntity();

		world.AddComponent(entity, new Tag { Value = 7 });

		Assert.True(world.RemoveComponent<Tag>(entity));
		Assert.False(world.HasComponent<Tag>(entity));
		Assert.True(world.IsAlive(entity));

		world.AddComponent(entity, new Health { Value = 19 });

		Assert.Equal(19, world.GetComponent<Health>(entity).Value);
	}

	[Fact]
	public void RepeatedComponentTransitions_PreserveEntityLocationsAndValues()
	{
		using World world = new();
		Entity first = world.CreateEntity();
		Entity second = world.CreateEntity();

		world.AddComponent(first, new Position { X = 10, Y = 20 });
		world.AddComponent(second, new Position { X = 30, Y = 40 });

		for (int i = 0; i < 20; i++)
		{
			world.AddComponent(first, new Health { Value = 100 + i });
			world.AddComponent(second, new Health { Value = 200 + i });

			Assert.True(world.RemoveComponent<Health>(first));
			Assert.True(world.RemoveComponent<Health>(second));

			Assert.False(world.HasComponent<Health>(first));
			Assert.False(world.HasComponent<Health>(second));
			Assert.Equal(new Position { X = 10, Y = 20 }, world.GetComponent<Position>(first));
			Assert.Equal(new Position { X = 30, Y = 40 }, world.GetComponent<Position>(second));
			Assert.Equal(2, world.Count);
		}
	}

	[Fact]
	public void GetComponent_Missing_Throws()
	{
		using World world = new();
		Entity entity = world.CreateEntity();
		Assert.Throws<ArgumentException>(() => { _ = world.GetComponent<Position>(entity); });
	}

	[Fact]
	public void SetComponent_UpdatesStoredValue()
	{
		using World world = new();
		Entity entity = world.CreateEntity();

		world.AddComponent(entity, new Health { Value = 1 });
		world.SetComponent(entity, new Health { Value = 200 });

		Assert.Equal(200, world.GetComponent<Health>(entity).Value);
	}

	[Fact]
	public void SetComponent_Missing_Throws()
	{
		using World world = new();
		Entity entity = world.CreateEntity();

		Assert.Throws<ArgumentException>(() => world.SetComponent(entity, new Health { Value = 1 }));
	}

	[Fact]
	public void GetComponent_ReturnedReference_AllowsInPlaceMutation()
	{
		using World world = new();
		Entity entity = world.CreateEntity();
		world.AddComponent(entity, new Position { X = 3, Y = 4 });

		ref Position position = ref world.GetComponent<Position>(entity);

		position.X = 77;
		position.Y = -12;

		Assert.Equal(77, world.GetComponent<Position>(entity).X);
		Assert.Equal(-12, world.GetComponent<Position>(entity).Y);
	}

	[Fact]
	public void InvalidEntity_ComponentOperations_Throw()
	{
		using World world = new();

		Assert.Throws<ArgumentException>(() => world.HasComponent<Position>(default));
		Assert.Throws<ArgumentException>(() => world.AddComponent(default, new Position()));
		Assert.Throws<ArgumentException>(() => world.RemoveComponent<Position>(default));
		Assert.Throws<ArgumentException>(() => { _ = world.GetComponent<Position>(default); });
		Assert.Throws<ArgumentException>(() => world.SetComponent(default, new Position()));
	}

	[Fact]
	public void DestroyedEntity_ComponentOperations_Throw()
	{
		using World world = new();
		Entity entity = world.CreateEntity();

		world.AddComponent(entity, new Health { Value = 5 });
		world.DestroyEntity(entity);

		Assert.Throws<ArgumentException>(() => world.HasComponent<Health>(entity));
		Assert.Throws<ArgumentException>(() => world.AddComponent(entity, new Health()));
		Assert.Throws<ArgumentException>(() => world.RemoveComponent<Health>(entity));
		Assert.Throws<ArgumentException>(() => { _ = world.GetComponent<Health>(entity); });
		Assert.Throws<ArgumentException>(() => world.SetComponent(entity, new Health()));
	}

	[Fact]
	public void ForeignEntity_ComponentOperations_Throw()
	{
		using World first = new();
		using World second = new();

		Entity foreign = second.CreateEntity();

		Assert.Throws<ArgumentException>(() => first.HasComponent<Health>(foreign));
		Assert.Throws<ArgumentException>(() => first.AddComponent(foreign, new Health()));
		Assert.Throws<ArgumentException>(() => first.RemoveComponent<Health>(foreign));
		Assert.Throws<ArgumentException>(() => { _ = first.GetComponent<Health>(foreign); });
		Assert.Throws<ArgumentException>(() => first.SetComponent(foreign, new Health()));
	}

	[Fact]
	public void DestroyEntity_SwapBack_PreservesMovedEntityComponents()
	{
		using World world = new();

		Entity first = world.CreateEntity();
		Entity second = world.CreateEntity();
		Entity third = world.CreateEntity();

		world.AddComponent(first, new Health { Value = 10 });
		world.AddComponent(second, new Health { Value = 20 });
		world.AddComponent(third, new Health { Value = 30 });

		Assert.True(world.DestroyEntity(first));
		Assert.True(world.IsAlive(second));
		Assert.True(world.IsAlive(third));
		Assert.Equal(20, world.GetComponent<Health>(second).Value);
		Assert.Equal(30, world.GetComponent<Health>(third).Value);

		world.SetComponent(third, new Health { Value = 31 });

		Assert.Equal(31, world.GetComponent<Health>(third).Value);
	}

	[Fact]
	public void DestroyEntity_MiddleRow_PreservesOtherEntities()
	{
		using World world = new();
		Entity[] entities = new Entity[12];

		for (int i = 0; i < entities.Length; i++)
		{
			entities[i] = world.CreateEntity();
			world.AddComponent(entities[i], new Position { X = i, Y = -i });
		}

		Assert.True(world.DestroyEntity(entities[4]));
		Assert.False(world.IsAlive(entities[4]));

		for (int i = 0; i < entities.Length; i++)
		{
			if (i == 4)
			{
				continue;
			}

			Assert.True(world.IsAlive(entities[i]));
			Assert.Equal(i, world.GetComponent<Position>(entities[i]).X);
			Assert.Equal(-i, world.GetComponent<Position>(entities[i]).Y);
		}
	}

	[Fact]
	public void Migration_SwapBack_PreservesOtherEntityLocation()
	{
		using World world = new();

		Entity first = world.CreateEntity();
		Entity second = world.CreateEntity();
		Entity third = world.CreateEntity();

		world.AddComponent(first, new Health { Value = 10 });
		world.AddComponent(second, new Health { Value = 20 });
		world.AddComponent(third, new Health { Value = 30 });
		world.AddComponent(first, new Position { X = 111 });

		Assert.Equal(20, world.GetComponent<Health>(second).Value);
		Assert.Equal(30, world.GetComponent<Health>(third).Value);

		world.AddComponent(third, new Position { X = 333 });

		Assert.Equal(333, world.GetComponent<Position>(third).X);
		Assert.Equal(30, world.GetComponent<Health>(third).Value);
	}

	[Fact]
	public void Migration_ChangesSignatureWithoutChangingHandle()
	{
		using World world = new();

		Entity entity = world.CreateEntity();
		Entity original = entity;

		world.AddComponent(entity, new Health { Value = 1 });
		world.AddComponent(entity, new Position { X = 2 });
		world.RemoveComponent<Health>(entity);

		Assert.Equal(original, entity);
		Assert.True(world.IsAlive(entity));
		Assert.True(world.HasComponent<Position>(entity));
		Assert.False(world.HasComponent<Health>(entity));
		Assert.Equal(2, world.GetComponent<Position>(entity).X);
	}

	[Fact]
	public void ReaddingRemovedComponent_DoesNotRestoreOldValue()
	{
		using World world = new();
		Entity entity = world.CreateEntity();

		world.AddComponent(entity, new Health { Value = 42 });
		world.RemoveComponent<Health>(entity);
		world.AddComponent(entity, new Health { Value = 7 });

		Assert.Equal(7, world.GetComponent<Health>(entity).Value);
	}

	[Fact]
	public void ComponentValues_DoNotLeakIntoReusedRows()
	{
		using World world = new();
		Entity oldEntity = world.CreateEntity();

		world.AddComponent(oldEntity, new LargeComponent { A = 1, B = 2, C = 3, D = 4 });
		world.DestroyEntity(oldEntity);

		Entity next = world.CreateEntity();
		world.AddComponent(next, default(LargeComponent));
		LargeComponent value = world.GetComponent<LargeComponent>(next);

		Assert.Equal(0, value.A);
		Assert.Equal(0, value.B);
		Assert.Equal(0, value.C);
		Assert.Equal(0, value.D);
	}

	[Fact]
	public void MultipleArchetypes_CanCoexist()
	{
		using World world = new();

		Entity a = world.CreateEntity();
		Entity b = world.CreateEntity();
		Entity c = world.CreateEntity();

		world.AddComponent(a, new Position { X = 1 });
		world.AddComponent(b, new Health { Value = 2 });
		world.AddComponent(c, new Position { X = 3 });
		world.AddComponent(c, new Health { Value = 4 });

		Assert.Equal(1, world.GetComponent<Position>(a).X);
		Assert.Equal(2, world.GetComponent<Health>(b).Value);
		Assert.Equal(3, world.GetComponent<Position>(c).X);
		Assert.Equal(4, world.GetComponent<Health>(c).Value);
		Assert.Equal(3, world.Count);
	}

	[Fact]
	public void MoreThanOneChunk_StoresAndRetrievesAllValues()
	{
		const int entityCount = 3000;
		using World world = new();
		Entity[] entities = new Entity[entityCount];

		for (int i = 0; i < entityCount; i++)
		{
			entities[i] = world.CreateEntity();
			world.AddComponent(entities[i], new LargeComponent { A = i, B = -i, C = i * 2L, D = 12345 });
		}

		Assert.Equal(entityCount, world.Count);

		for (int i = 0; i < entityCount; i++)
		{
			LargeComponent value = world.GetComponent<LargeComponent>(entities[i]);

			Assert.Equal(i, value.A);
			Assert.Equal(-i, value.B);
			Assert.Equal(i * 2L, value.C);
			Assert.Equal(12345, value.D);
		}
	}

	[Fact]
	public void ManyEntities_InterleavedDestroyAndMigration_PreserveData()
	{
		const int entityCount = 1200;
		using World world = new();
		Entity[] entities = new Entity[entityCount];

		for (int i = 0; i < entityCount; i++)
		{
			entities[i] = world.CreateEntity();
			world.AddComponent(entities[i], new Health { Value = i + 1 });

			if (i % 3 == 0)
			{
				world.AddComponent(entities[i], new Position { X = i, Y = -i });
			}
		}

		for (int i = 0; i < entityCount; i += 5)
		{
			Assert.True(world.DestroyEntity(entities[i]));
		}

		for (int i = 0; i < entityCount; i++)
		{
			if (i % 5 == 0)
			{
				Assert.False(world.IsAlive(entities[i]));
				continue;
			}

			Assert.True(world.IsAlive(entities[i]));
			Assert.Equal(i + 1, world.GetComponent<Health>(entities[i]).Value);
			Assert.Equal(i % 3 == 0, world.HasComponent<Position>(entities[i]));

			if (i % 3 == 0)
			{
				Assert.Equal(i, world.GetComponent<Position>(entities[i]).X);
				Assert.Equal(-i, world.GetComponent<Position>(entities[i]).Y);
			}
		}

		Assert.Equal(entityCount - (entityCount + 4) / 5, world.Count);
	}

	[Fact]
	public void RepeatedMigration_DoesNotCorruptOtherEntities()
	{
		using World world = new();

		Entity first = world.CreateEntity();
		Entity second = world.CreateEntity();

		world.AddComponent(first, new Health { Value = 11 });
		world.AddComponent(second, new Health { Value = 22 });

		for (int i = 0; i < 200; i++)
		{
			world.AddComponent(first, new Position { X = i, Y = -i });

			Assert.Equal(i, world.GetComponent<Position>(first).X);
			Assert.True(world.RemoveComponent<Position>(first));
			Assert.Equal(11, world.GetComponent<Health>(first).Value);
			Assert.Equal(22, world.GetComponent<Health>(second).Value);
		}
	}

	[Fact]
	public void Dispose_CanBeCalledTwice()
	{
		World world = new();
		world.CreateEntity();
		world.Dispose();
		world.Dispose();
	}

	[Fact]
	public void DisposedWorld_RejectsOperations()
	{
		World world = new();
		Entity entity = world.CreateEntity();

		world.AddComponent(entity, new Health { Value = 5 });
		world.Dispose();

		Assert.Throws<ObjectDisposedException>(() => { _ = world.Count; });
		Assert.Throws<ObjectDisposedException>(() => world.CreateEntity());
		Assert.Throws<ObjectDisposedException>(() => world.DestroyEntity(entity));
		Assert.Throws<ObjectDisposedException>(() => world.IsAlive(entity));
		Assert.Throws<ObjectDisposedException>(() => world.HasComponent<Health>(entity));
		Assert.Throws<ObjectDisposedException>(() => world.AddComponent(entity, new Health()));
		Assert.Throws<ObjectDisposedException>(() => world.RemoveComponent<Health>(entity));
		Assert.Throws<ObjectDisposedException>(() => { _ = world.GetComponent<Health>(entity); });
		Assert.Throws<ObjectDisposedException>(() => world.SetComponent(entity, new Health()));
	}


	[Fact]
	public void FreezeStructure_BlocksStructuralMutations()
	{
		using World world = new();

		Entity entity = world.CreateEntity();
		world.AddComponent(entity, new Health { Value = 42 });

		using (world.FreezeStructure())
		{
			Assert.Throws<InvalidOperationException>(
				() => world.CreateEntity());

			Assert.Throws<InvalidOperationException>(
				() => world.DestroyEntity(entity));

			Assert.Throws<InvalidOperationException>(
				() => world.AddComponent(entity, new Position()));

			Assert.Throws<InvalidOperationException>(
				() => world.RemoveComponent<Health>(entity));

			Assert.Throws<InvalidOperationException>(
				() => world.Dispose());

			Assert.True(world.IsAlive(entity));
			Assert.Equal(42, world.GetComponent<Health>(entity).Value);
			Assert.Equal(1, world.Count);
		}
	}

	[Fact]
	public void FreezeStructure_AllowsComponentValueAccess()
	{
		using World world = new();

		Entity entity = world.CreateEntity();
		world.AddComponent(entity, new Health { Value = 10 });

		using (world.FreezeStructure())
		{
			Assert.True(world.HasComponent<Health>(entity));

			world.SetComponent(entity, new Health { Value = 25 });

			ref Health health = ref world.GetComponent<Health>(entity);
			health.Value += 5;

			Assert.Equal(30, world.GetComponent<Health>(entity).Value);
		}
	}

	[Fact]
	public void FreezeStructure_ReleasingScopeAllowsStructuralChanges()
	{
		using World world = new();

		World.StructuralFreezeScope scope = world.FreezeStructure();

		Assert.Throws<InvalidOperationException>(
			() => world.CreateEntity());

		scope.Dispose();

		Entity entity = world.CreateEntity();

		Assert.True(world.IsAlive(entity));
		Assert.Equal(1, world.Count);
	}

	[Fact]
	public void FreezeStructure_NestedScopesRequireAllReleases()
	{
		using World world = new();

		World.StructuralFreezeScope first = world.FreezeStructure();
		World.StructuralFreezeScope second = world.FreezeStructure();

		first.Dispose();

		Assert.Throws<InvalidOperationException>(
			() => world.CreateEntity());

		second.Dispose();

		Entity entity = world.CreateEntity();

		Assert.True(world.IsAlive(entity));
	}

	[Fact]
	public void FreezeStructure_DisposeScopeTwiceIsSafe()
	{
		using World world = new();

		World.StructuralFreezeScope first = world.FreezeStructure();
		World.StructuralFreezeScope second = world.FreezeStructure();

		first.Dispose();
		first.Dispose();

		Assert.Throws<InvalidOperationException>(
			() => world.CreateEntity());

		second.Dispose();

		Assert.True(world.IsAlive(world.CreateEntity()));
	}

	[Fact]
	public async Task FreezeStructure_RejectsStructuralMutationFromAnotherThread()
	{
		using World world = new();

		using (world.FreezeStructure())
		{
			await Task.Run(() =>
			{
				Assert.Throws<InvalidOperationException>(
					() => world.CreateEntity());
			});
		}

		Assert.True(world.IsAlive(world.CreateEntity()));
	}
}
