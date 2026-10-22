using System.Numerics;
using Volt.Ecs.Component;
using Volt.Ecs.Components.Spatial;
using Xunit;

namespace Volt.Ecs.Tests.Components.Spatial;

public sealed class PositionComponentTests
{
	private struct TestTag
	{
		public int Value;
	}

	[Fact]
	public void DefaultPosition_IsOrigin()
	{
		PositionComponent position = default;
		Assert.Equal(Vector3.Zero, position.Value);
	}

	[Fact]
	public void Constructor_StoresPosition()
	{
		Vector3 expected = new(10, 20, -30);
		PositionComponent position = new(expected);
		Assert.Equal(expected, position.Value);
	}

	[Fact]
	public void ComponentRegistry_ReportsExpectedSize()
	{
		ComponentType type = ComponentRegistry.Register<PositionComponent>();
		ComponentRegistry.ComponentInfo info = ComponentRegistry.GetInfo(type);

		Assert.Equal(typeof(PositionComponent), info.ClrType);
		Assert.Equal(12, info.Size);
	}

	[Fact]
	public void World_CanStoreAndModifyPosition()
	{
		using World world = new();
		Entity entity = world.CreateEntity();

		world.AddComponent(
			entity,
			new PositionComponent(new Vector3(1, 2, 3)));

		Assert.True(world.HasComponent<PositionComponent>(entity));

		ref PositionComponent position =
			ref world.GetComponent<PositionComponent>(entity);

		position.Value = new Vector3(4, 5, 6);

		Assert.Equal(
			new Vector3(4, 5, 6),
			world.GetComponent<PositionComponent>(entity).Value);
	}

	[Fact]
	public void Position_SurvivesArchetypeMigration()
	{
		using World world = new();
		Entity entity = world.CreateEntity();

		Vector3 expected = new(7, 8, 9);

		world.AddComponent(entity, new PositionComponent(expected));
		world.AddComponent(entity, new TestTag { Value = 42 });

		Assert.Equal(
			expected,
			world.GetComponent<PositionComponent>(entity).Value);

		Assert.True(world.RemoveComponent<TestTag>(entity));

		Assert.Equal(
			expected,
			world.GetComponent<PositionComponent>(entity).Value);
	}
}
