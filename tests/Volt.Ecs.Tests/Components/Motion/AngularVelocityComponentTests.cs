
using System.Numerics;
using Volt.Ecs.Component;
using Volt.Ecs.Components.Motion;
using Xunit;

namespace Volt.Ecs.Tests.Components.Motion;

public sealed class AngularVelocityComponentTests
{
    private struct TestTag
    {
        public int Value;
    }

    [Fact]
    public void Zero_HasExpectedValue()
    {
        AngularVelocityComponent velocity =
            AngularVelocityComponent.Zero;

        Assert.Equal(Vector3.Zero, velocity.Value);
    }

    [Fact]
    public void DefaultAngularVelocity_IsZero()
    {
        AngularVelocityComponent velocity = default;
        Assert.Equal(Vector3.Zero, velocity.Value);
    }

    [Fact]
    public void Constructor_StoresAngularVelocity()
    {
        Vector3 expected = new(1, 2, 3);
        AngularVelocityComponent velocity = new(expected);
        Assert.Equal(expected, velocity.Value);
    }

    [Fact]
    public void Constructor_SupportsNegativeAngularVelocity()
    {
        Vector3 expected = new(-1, -2, -3);
        AngularVelocityComponent velocity = new(expected);
        Assert.Equal(expected, velocity.Value);
    }

    [Fact]
    public void AngularVelocity_MagnitudeRepresentsSpeed()
    {
        AngularVelocityComponent velocity = new(
            new Vector3(0, MathF.PI, 0));

        float speed = velocity.Value.Length();
        Assert.Equal(MathF.PI, speed, 5);
    }

    [Fact]
    public void AngularVelocity_DirectionRepresentsAxis()
    {
        AngularVelocityComponent velocity = new(
            new Vector3(0, MathF.PI, 0));

        Vector3 axis = Vector3.Normalize(velocity.Value);
        Assert.Equal(Vector3.UnitY, axis);
    }

    [Fact]
    public void ComponentRegistry_ReportsExpectedSize()
    {
        ComponentType type =
            ComponentRegistry.Register<AngularVelocityComponent>();

        ComponentRegistry.ComponentInfo info =
            ComponentRegistry.GetInfo(type);

        Assert.Equal(typeof(AngularVelocityComponent), info.ClrType);
        Assert.Equal(12, info.Size);
    }

    [Fact]
    public void World_CanStoreAndModifyAngularVelocity()
    {
        using World world = new();
        Entity entity = world.CreateEntity();

        world.AddComponent(
            entity,
            AngularVelocityComponent.Zero);

        Assert.True(
            world.HasComponent<AngularVelocityComponent>(entity));

        Vector3 expected = new(0, MathF.PI, 0);

        ref AngularVelocityComponent velocity =
            ref world.GetComponent<AngularVelocityComponent>(entity);

        velocity.Value = expected;

        Assert.Equal(
            expected,
            world.GetComponent<AngularVelocityComponent>(entity).Value);
    }

    [Fact]
    public void AngularVelocity_SurvivesArchetypeMigration()
    {
        using World world = new();
        Entity entity = world.CreateEntity();

        Vector3 expected = new(1, -2, 3);

        world.AddComponent(
            entity,
            new AngularVelocityComponent(expected));

        world.AddComponent(
            entity,
            new TestTag { Value = 42 });

        Assert.Equal(
            expected,
            world.GetComponent<AngularVelocityComponent>(entity).Value);

        Assert.True(world.RemoveComponent<TestTag>(entity));

        Assert.Equal(
            expected,
            world.GetComponent<AngularVelocityComponent>(entity).Value);
    }
}
