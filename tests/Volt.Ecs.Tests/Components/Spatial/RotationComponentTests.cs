
using System.Numerics;
using Volt.Ecs.Component;
using Volt.Ecs.Components.Spatial;
using Xunit;

namespace Volt.Ecs.Tests.Components.Spatial;

public sealed class RotationComponentTests
{
    private struct TestTag
    {
        public int Value;
    }

    [Fact]
    public void Identity_HasExpectedValue()
    {
        RotationComponent rotation = RotationComponent.Identity;
        Assert.Equal(Quaternion.Identity, rotation.Value);
    }

    [Fact]
    public void Constructor_StoresRotation()
    {
        Quaternion expected = Quaternion.CreateFromAxisAngle(
            Vector3.UnitY,
            MathF.PI / 2);

        RotationComponent rotation = new(expected);

        Assert.Equal(expected, rotation.Value);
    }

    [Fact]
    public void Rotation_TransformsDirectionCorrectly()
    {
        RotationComponent rotation = new(
            Quaternion.CreateFromAxisAngle(
                Vector3.UnitY,
                MathF.PI / 2));

        Vector3 forward = -Vector3.UnitZ;

        Vector3 result = Vector3.Transform(
            forward,
            rotation.Value);

        AssertVectorNear(-Vector3.UnitX, result);
    }

    [Fact]
    public void ComponentRegistry_ReportsExpectedSize()
    {
        ComponentType type =
            ComponentRegistry.Register<RotationComponent>();

        ComponentRegistry.ComponentInfo info =
            ComponentRegistry.GetInfo(type);

        Assert.Equal(typeof(RotationComponent), info.ClrType);
        Assert.Equal(16, info.Size);
    }

    [Fact]
    public void World_CanStoreAndModifyRotation()
    {
        using World world = new();
        Entity entity = world.CreateEntity();

        world.AddComponent(
            entity,
            RotationComponent.Identity);

        Assert.True(
            world.HasComponent<RotationComponent>(entity));

        Quaternion expected = Quaternion.CreateFromAxisAngle(
            Vector3.UnitX,
            MathF.PI / 4);

        ref RotationComponent rotation =
            ref world.GetComponent<RotationComponent>(entity);

        rotation.Value = expected;

        Assert.Equal(
            expected,
            world.GetComponent<RotationComponent>(entity).Value);
    }

    [Fact]
    public void Rotation_SurvivesArchetypeMigration()
    {
        using World world = new();
        Entity entity = world.CreateEntity();

        Quaternion expected = Quaternion.CreateFromAxisAngle(
            Vector3.UnitY,
            MathF.PI / 3);

        world.AddComponent(
            entity,
            new RotationComponent(expected));

        world.AddComponent(
            entity,
            new TestTag { Value = 42 });

        Assert.Equal(
            expected,
            world.GetComponent<RotationComponent>(entity).Value);

        Assert.True(world.RemoveComponent<TestTag>(entity));

        Assert.Equal(
            expected,
            world.GetComponent<RotationComponent>(entity).Value);
    }

    private static void AssertVectorNear(
        Vector3 expected,
        Vector3 actual,
        float tolerance = 0.00001f)
    {
        Assert.True(
            Vector3.Distance(expected, actual) <= tolerance,
            $"Expected {expected}, actual {actual}.");
    }
}
