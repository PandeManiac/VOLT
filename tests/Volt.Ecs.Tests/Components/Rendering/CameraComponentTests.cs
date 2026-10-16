
using System.Numerics;
using Volt.Ecs.Component;
using Volt.Ecs.Components.Rendering;
using Volt.Ecs.Components.Spatial;
using Xunit;

namespace Volt.Ecs.Tests.Components.Rendering;

public sealed class CameraComponentTests
{
    private struct TestTag
    {
        public int Value;
    }

    [Fact]
    public void Default_HasExpectedSettings()
    {
        CameraComponent camera = CameraComponent.Default;

        Assert.Equal(MathF.PI / 3f, camera.VerticalFieldOfView);
        Assert.Equal(0.1f, camera.NearPlane);
        Assert.Equal(1000f, camera.FarPlane);
    }

    [Fact]
    public void Constructor_StoresProjectionSettings()
    {
        CameraComponent camera = new(
            MathF.PI / 2f,
            0.5f,
            500f);

        Assert.Equal(MathF.PI / 2f, camera.VerticalFieldOfView);
        Assert.Equal(0.5f, camera.NearPlane);
        Assert.Equal(500f, camera.FarPlane);
    }

    [Fact]
    public void ProjectionMatrix_MatchesSystemNumerics()
    {
        CameraComponent camera = CameraComponent.Default;

        const float aspectRatio = 16f / 9f;

        Matrix4x4 expected =
            Matrix4x4.CreatePerspectiveFieldOfView(
                camera.VerticalFieldOfView,
                aspectRatio,
                camera.NearPlane,
                camera.FarPlane);

        Matrix4x4 actual =
            camera.CreateProjectionMatrix(aspectRatio);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ProjectionMatrix_ChangesWithAspectRatio()
    {
        CameraComponent camera = CameraComponent.Default;

        Matrix4x4 square =
            camera.CreateProjectionMatrix(1f);

        Matrix4x4 widescreen =
            camera.CreateProjectionMatrix(16f / 9f);

        Assert.NotEqual(square.M11, widescreen.M11);
        Assert.Equal(square.M22, widescreen.M22);
    }

    [Fact]
    public void ProjectionMatrix_MapsClippingPlanesToDepthRange()
    {
        CameraComponent camera = CameraComponent.Default;

        Matrix4x4 projection =
            camera.CreateProjectionMatrix(1f);

        Vector4 nearPoint = Vector4.Transform(
            new Vector4(0, 0, -camera.NearPlane, 1),
            projection);

        Vector4 farPoint = Vector4.Transform(
            new Vector4(0, 0, -camera.FarPlane, 1),
            projection);

        float nearDepth = nearPoint.Z / nearPoint.W;
        float farDepth = farPoint.Z / farPoint.W;

        Assert.InRange(nearDepth, -0.0001f, 0.0001f);
        Assert.InRange(farDepth, 0.9999f, 1.0001f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void ProjectionMatrix_RejectsInvalidAspectRatio(
        float aspectRatio)
    {
        CameraComponent camera = CameraComponent.Default;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.CreateProjectionMatrix(aspectRatio));
    }

    [Fact]
    public void ProjectionMatrix_RejectsInvalidClippingPlanes()
    {
        CameraComponent camera = new(
            MathF.PI / 3f,
            10f,
            1f);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.CreateProjectionMatrix(1f));
    }

    [Fact]
    public void ProjectionMatrix_RejectsInvalidFieldOfView()
    {
        CameraComponent camera = new(
            MathF.PI,
            0.1f,
            1000f);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.CreateProjectionMatrix(1f));
    }

    [Fact]
    public void ViewMatrix_IdentityTransformProducesIdentity()
    {
        PositionComponent position = new(Vector3.Zero);
        RotationComponent rotation = RotationComponent.Identity;

        Matrix4x4 view = CameraComponent.CreateViewMatrix(
            in position,
            in rotation);

        Assert.Equal(Matrix4x4.Identity, view);
    }

    [Fact]
    public void ViewMatrix_TranslatesWorldRelativeToCamera()
    {
        PositionComponent position = new(
            new Vector3(0, 0, 5));

        RotationComponent rotation = RotationComponent.Identity;

        Matrix4x4 view = CameraComponent.CreateViewMatrix(
            in position,
            in rotation);

        Vector3 result = Vector3.Transform(
            Vector3.Zero,
            view);

        AssertVectorNear(new Vector3(0, 0, -5), result);
    }

    [Fact]
    public void ViewMatrix_RespectsCameraRotationAndPosition()
    {
        PositionComponent position = new(
            new Vector3(3, 2, 5));

        RotationComponent rotation = new(
            Quaternion.CreateFromAxisAngle(
                Vector3.UnitY,
                MathF.PI / 2f));

        Matrix4x4 view = CameraComponent.CreateViewMatrix(
            in position,
            in rotation);

        Vector3 worldPoint = position.Value
            + Vector3.Transform(
                -Vector3.UnitZ * 5f,
                rotation.Value);

        Vector3 result = Vector3.Transform(worldPoint, view);

        AssertVectorNear(
            new Vector3(0, 0, -5),
            result);
    }

    [Fact]
    public void ComponentRegistry_ReportsExpectedSize()
    {
        ComponentType type =
            ComponentRegistry.Register<CameraComponent>();

        ComponentRegistry.ComponentInfo info =
            ComponentRegistry.GetInfo(type);

        Assert.Equal(typeof(CameraComponent), info.ClrType);
        Assert.Equal(12, info.Size);
    }

    [Fact]
    public void World_CanStoreAndModifyCamera()
    {
        using World world = new();
        Entity entity = world.CreateEntity();
        world.AddComponent(entity, CameraComponent.Default);

        Assert.True(world.HasComponent<CameraComponent>(entity));

        ref CameraComponent camera =
            ref world.GetComponent<CameraComponent>(entity);

        camera.FarPlane = 2000f;

        Assert.Equal(
            2000f,
            world.GetComponent<CameraComponent>(entity).FarPlane);
    }

    [Fact]
    public void Camera_SurvivesArchetypeMigration()
    {
        using World world = new();
        Entity entity = world.CreateEntity();

        CameraComponent expected = new(
            MathF.PI / 4f,
            0.25f,
            500f);

        world.AddComponent(entity, expected);

        world.AddComponent(
            entity,
            new TestTag { Value = 42 });

        Assert.True(world.RemoveComponent<TestTag>(entity));

        CameraComponent actual =
            world.GetComponent<CameraComponent>(entity);

        Assert.Equal(
            expected.VerticalFieldOfView,
            actual.VerticalFieldOfView);

        Assert.Equal(expected.NearPlane, actual.NearPlane);
        Assert.Equal(expected.FarPlane, actual.FarPlane);
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
