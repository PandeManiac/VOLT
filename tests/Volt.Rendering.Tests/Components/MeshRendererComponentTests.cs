
using System.Runtime.CompilerServices;
using Volt.Ecs;
using Volt.Rendering.Components;
using Volt.Rendering.Resources;
using Xunit;

namespace Volt.Rendering.Tests.Components;

public sealed class MeshRendererComponentTests
{
    private struct TestTag
    {
        public int Value;
    }

    [Fact]
    public void Default_HasInvalidHandles()
    {
        MeshRendererComponent component = default;
        Assert.False(component.Mesh.IsValid);
        Assert.False(component.Material.IsValid);
    }

    [Fact]
    public void Constructor_StoresHandles()
    {
        MeshHandle mesh = new(12, 1);
        MaterialHandle material = new(24, 2);

        MeshRendererComponent component = new(
            mesh,
            material);

        Assert.Equal(mesh, component.Mesh);
        Assert.Equal(material, component.Material);
    }

    [Fact]
    public void Component_AllowsIndependentHandleModification()
    {
        MeshHandle originalMesh = new(1, 1);
        MaterialHandle originalMaterial = new(2, 1);

        MeshRendererComponent component = new(
            originalMesh,
            originalMaterial);

        MeshHandle replacementMesh = new(3, 2);
        component.Mesh = replacementMesh;

        Assert.Equal(replacementMesh, component.Mesh);
        Assert.Equal(originalMaterial, component.Material);

        MaterialHandle replacementMaterial = new(4, 2);
        component.Material = replacementMaterial;

        Assert.Equal(replacementMesh, component.Mesh);
        Assert.Equal(replacementMaterial, component.Material);
    }

    [Fact]
    public void Component_PreservesHandleGenerations()
    {
        MeshRendererComponent component = new(
            new MeshHandle(10, 5),
            new MaterialHandle(20, 7));

        Assert.Equal(5u, component.Mesh.Generation);
        Assert.Equal(7u, component.Material.Generation);
    }

    [Fact]
    public void Component_HasExpectedSize()
    {
        Assert.Equal(
            16,
            Unsafe.SizeOf<MeshRendererComponent>());
    }

    [Fact]
    public void Component_ContainsNoManagedReferences()
    {
        Assert.False(
            RuntimeHelpers.IsReferenceOrContainsReferences<
                MeshRendererComponent>());
    }

    [Fact]
    public void World_CanStoreComponent()
    {
        using World world = new();
        Entity entity = world.CreateEntity();

        MeshRendererComponent expected = new(
            new MeshHandle(10, 1),
            new MaterialHandle(20, 1));

        world.AddComponent(entity, expected);

        Assert.True(
            world.HasComponent<MeshRendererComponent>(entity));

        MeshRendererComponent actual =
            world.GetComponent<MeshRendererComponent>(entity);

        Assert.Equal(expected.Mesh, actual.Mesh);
        Assert.Equal(expected.Material, actual.Material);
    }

    [Fact]
    public void World_CanModifyComponent()
    {
        using World world = new();
        Entity entity = world.CreateEntity();

        world.AddComponent(
            entity,
            new MeshRendererComponent(
                new MeshHandle(1, 1),
                new MaterialHandle(2, 1)));

        ref MeshRendererComponent component =
            ref world.GetComponent<MeshRendererComponent>(entity);

        component.Mesh = new MeshHandle(3, 2);
        component.Material = new MaterialHandle(4, 2);

        MeshRendererComponent actual =
            world.GetComponent<MeshRendererComponent>(entity);

        Assert.Equal(new MeshHandle(3, 2), actual.Mesh);
        Assert.Equal(new MaterialHandle(4, 2), actual.Material);
    }

    [Fact]
    public void Component_SurvivesArchetypeMigration()
    {
        using World world = new();
        Entity entity = world.CreateEntity();

        MeshRendererComponent expected = new(
            new MeshHandle(10, 3),
            new MaterialHandle(20, 4));

        world.AddComponent(entity, expected);

        world.AddComponent(
            entity,
            new TestTag { Value = 42 });

        Assert.True(world.RemoveComponent<TestTag>(entity));

        MeshRendererComponent actual =
            world.GetComponent<MeshRendererComponent>(entity);

        Assert.Equal(expected.Mesh, actual.Mesh);
        Assert.Equal(expected.Material, actual.Material);
    }

    [Fact]
    public void World_CanRemoveComponent()
    {
        using World world = new();
        Entity entity = world.CreateEntity();

        world.AddComponent(
            entity,
            new MeshRendererComponent(
                new MeshHandle(1, 1),
                new MaterialHandle(2, 1)));

        Assert.True(
            world.RemoveComponent<MeshRendererComponent>(entity));

        Assert.False(
            world.HasComponent<MeshRendererComponent>(entity));

        Assert.True(world.IsAlive(entity));
    }
}
