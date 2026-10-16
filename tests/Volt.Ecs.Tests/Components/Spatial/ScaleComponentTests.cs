
using System.Numerics;
using Volt.Ecs.Component;
using Volt.Ecs.Components.Spatial;
using Xunit;

namespace Volt.Ecs.Tests.Components.Spatial;

public sealed class ScaleComponentTests
{
    private struct TestTag
    {
        public int Value;
    }

    [Fact]
    public void Identity_HasExpectedValue()
    {
        ScaleComponent scale = ScaleComponent.Identity;
        Assert.Equal(Vector3.One, scale.Value);
    }

    [Fact]
    public void DefaultScale_IsZero()
    {
        ScaleComponent scale = default;
        Assert.Equal(Vector3.Zero, scale.Value);
    }

    [Fact]
    public void Constructor_StoresScale()
    {
        Vector3 expected = new(2, 3, 4);
        ScaleComponent scale = new(expected);
        Assert.Equal(expected, scale.Value);
    }

    [Fact]
    public void Constructor_SupportsNonuniformAndNegativeScale()
    {
        Vector3 expected = new(-2, 0.5f, 3);
        ScaleComponent scale = new(expected);
        Assert.Equal(expected, scale.Value);
    }

    [Fact]
    public void Scale_CanBeUsedToConstructScalingMatrix()
    {
        ScaleComponent scale = new(new Vector3(2, 3, 4));
        Matrix4x4 matrix = Matrix4x4.CreateScale(scale.Value);

        Vector3 result = Vector3.Transform(
            Vector3.One,
            matrix);

        Assert.Equal(new Vector3(2, 3, 4), result);
    }

    [Fact]
    public void ComponentRegistry_ReportsExpectedSize()
    {
        ComponentType type =
            ComponentRegistry.Register<ScaleComponent>();

        ComponentRegistry.ComponentInfo info =
            ComponentRegistry.GetInfo(type);

        Assert.Equal(typeof(ScaleComponent), info.ClrType);
        Assert.Equal(12, info.Size);
    }

    [Fact]
    public void World_CanStoreAndModifyScale()
    {
        using World world = new();
        Entity entity = world.CreateEntity();

        world.AddComponent(
            entity,
            ScaleComponent.Identity);

        Assert.True(
            world.HasComponent<ScaleComponent>(entity));

        ref ScaleComponent scale =
            ref world.GetComponent<ScaleComponent>(entity);

        scale.Value = new Vector3(2, 3, 4);

        Assert.Equal(
            new Vector3(2, 3, 4),
            world.GetComponent<ScaleComponent>(entity).Value);
    }

    [Fact]
    public void Scale_SurvivesArchetypeMigration()
    {
        using World world = new();
        Entity entity = world.CreateEntity();

        Vector3 expected = new(2, 3, 4);

        world.AddComponent(
            entity,
            new ScaleComponent(expected));

        world.AddComponent(
            entity,
            new TestTag { Value = 42 });

        Assert.Equal(
            expected,
            world.GetComponent<ScaleComponent>(entity).Value);

        Assert.True(world.RemoveComponent<TestTag>(entity));

        Assert.Equal(
            expected,
            world.GetComponent<ScaleComponent>(entity).Value);
    }
}
