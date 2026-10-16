
using System.Numerics;
using Volt.Ecs.Component;
using Volt.Ecs.Components.Motion;
using Xunit;

namespace Volt.Ecs.Tests.Components.Motion;

public sealed class VelocityComponentTests
{
    private struct TestTag
    {
        public int Value;
    }

    [Fact]
    public void Zero_HasExpectedValue()
    {
        VelocityComponent velocity = VelocityComponent.Zero;
        Assert.Equal(Vector3.Zero, velocity.Value);
    }

    [Fact]
    public void DefaultVelocity_IsZero()
    {
        VelocityComponent velocity = default;
        Assert.Equal(Vector3.Zero, velocity.Value);
    }

    [Fact]
    public void Constructor_StoresVelocity()
    {
        Vector3 expected = new(10, -5, 2.5f);
        VelocityComponent velocity = new(expected);
        Assert.Equal(expected, velocity.Value);
    }

    [Fact]
    public void Constructor_SupportsNegativeVelocity()
    {
        Vector3 expected = new(-10, -20, -30);
        VelocityComponent velocity = new(expected);
        Assert.Equal(expected, velocity.Value);
    }

    [Fact]
    public void Velocity_CanBeIntegratedOverTime()
    {
        VelocityComponent velocity = new(new Vector3(10, 0, -4));
        Vector3 position = new(1, 2, 3);

        const float deltaTime = 0.5f;
        position += velocity.Value * deltaTime;

        Assert.Equal(new Vector3(6, 2, 1), position);
    }

    [Fact]
    public void ComponentRegistry_ReportsExpectedSize()
    {
        ComponentType type =
            ComponentRegistry.Register<VelocityComponent>();

        ComponentRegistry.ComponentInfo info =
            ComponentRegistry.GetInfo(type);

        Assert.Equal(typeof(VelocityComponent), info.ClrType);
        Assert.Equal(12, info.Size);
    }

    [Fact]
    public void World_CanStoreAndModifyVelocity()
    {
        using World world = new();
        Entity entity = world.CreateEntity();

        world.AddComponent(
            entity,
            new VelocityComponent(new Vector3(1, 2, 3)));

        Assert.True(
            world.HasComponent<VelocityComponent>(entity));

        ref VelocityComponent velocity =
            ref world.GetComponent<VelocityComponent>(entity);

        velocity.Value = new Vector3(4, 5, 6);

        Assert.Equal(
            new Vector3(4, 5, 6),
            world.GetComponent<VelocityComponent>(entity).Value);
    }

    [Fact]
    public void Velocity_SurvivesArchetypeMigration()
    {
        using World world = new();
        Entity entity = world.CreateEntity();

        Vector3 expected = new(7, -8, 9);

        world.AddComponent(
            entity,
            new VelocityComponent(expected));

        world.AddComponent(
            entity,
            new TestTag { Value = 42 });

        Assert.Equal(
            expected,
            world.GetComponent<VelocityComponent>(entity).Value);

        Assert.True(world.RemoveComponent<TestTag>(entity));

        Assert.Equal(
            expected,
            world.GetComponent<VelocityComponent>(entity).Value);
    }
}
