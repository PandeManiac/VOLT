using System.Numerics;
using Volt.Ecs.Component;
using Volt.Ecs.Components.Spatial;

namespace Volt.Ecs.Tests;

/// <summary>
/// Covers the safe, non-throwing reads and measured storage statistics
/// that editor tooling uses to inspect a live world.
/// </summary>
public sealed class WorldIntrospectionTests
{
	[Fact]
	public void TryReadComponent_ReturnsTheStoredValue()
	{
		using World world = new();
		Entity entity = world.CreateEntity();

		world.AddComponent(entity, new PositionComponent(new Vector3(1f, 2f, 3f)));

		Assert.True(world.TryReadComponent<PositionComponent>(entity, out PositionComponent position));
		Assert.Equal(1f, position.Value.X);
		Assert.Equal(2f, position.Value.Y);
		Assert.Equal(3f, position.Value.Z);
	}

	[Fact]
	public void TryReadComponent_ReturnsFalseForAnAbsentComponent()
	{
		using World world = new();
		Entity entity = world.CreateEntity();

		world.AddComponent(entity, new PositionComponent(Vector3.One));

		Assert.False(world.TryReadComponent<RotationComponent>(entity, out _));
	}

	[Fact]
	public void TryReadComponent_ReturnsFalseForADestroyedEntity()
	{
		using World world = new();
		Entity entity = world.CreateEntity();

		world.AddComponent(entity, new PositionComponent(Vector3.One));
		world.DestroyEntity(entity);

		Assert.False(world.TryReadComponent<PositionComponent>(entity, out _));
	}

	[Fact]
	public void TryGetSignature_ReportsEveryComponentOnTheArchetype()
	{
		using World world = new();
		Entity entity = world.CreateEntity();

		world.AddComponent(entity, new PositionComponent(Vector3.One));
		world.AddComponent(entity, new RotationComponent(Quaternion.Identity));

		Assert.True(world.TryGetSignature(entity, out ComponentSignature signature));
		Assert.Equal(2, signature.Count);
		Assert.True(signature.Contains(ComponentRegistry.Get<PositionComponent>()));
		Assert.True(signature.Contains(ComponentRegistry.Get<RotationComponent>()));
	}

	[Fact]
	public void TryGetSignature_ReturnsFalseForADestroyedEntity()
	{
		using World world = new();
		Entity entity = world.CreateEntity();

		world.DestroyEntity(entity);

		Assert.False(world.TryGetSignature(entity, out ComponentSignature signature));
		Assert.Equal(0, signature.Count);
	}

	[Fact]
	public void GetArchetypes_ReportsTheArchetypeTheEntityMigratedTo()
	{
		using World world = new();
		Entity entity = world.CreateEntity();

		world.AddComponent(entity, new PositionComponent(Vector3.One));

		Assert.Contains(
			world.GetArchetypes(),
			archetype => archetype.Contains(
				ComponentRegistry.Get<PositionComponent>()));
	}

	[Fact]
	public void TryGetArchetypeStorage_MeasuresChunksCapacityAndBytes()
	{
		// Two entities share one archetype, so the measurement must
		// report both rows in the same chunk rather than one per entity.
		using World world = new();

		for (int index = 0; index < 2; index++)
		{
			Entity entity = world.CreateEntity();
			world.AddComponent(entity, new PositionComponent(Vector3.One));
		}

		Archetype archetype = world
			.GetArchetypes()
			.Single(candidate => candidate.Contains(
				ComponentRegistry.Get<PositionComponent>()));

		Assert.True(world.TryGetArchetypeStorage(archetype, out ArchetypeStorageInfo storage));
		Assert.Equal(1, storage.ChunkCount);
		Assert.Equal(2, storage.EntityCount);
		Assert.True(storage.Capacity >= storage.EntityCount);
		Assert.True(storage.AllocatedBytes > 0);
		Assert.InRange(storage.Occupancy, 0f, 1f);
	}

	[Fact]
	public void GetRegisteredComponents_IncludesTheComponentsInUse()
	{
		using World world = new();
		Entity entity = world.CreateEntity();

		world.AddComponent(entity, new PositionComponent(Vector3.One));

		Assert.Contains(
			ComponentRegistry.GetRegisteredComponents(),
			info => info.ClrType == typeof(PositionComponent));

		Assert.True(ComponentRegistry.TryGetInfoForType(
			typeof(PositionComponent),
			out ComponentRegistry.ComponentInfo position));

		Assert.Equal(
			ComponentRegistry.Get<PositionComponent>(),
			position.Type);
	}
}
