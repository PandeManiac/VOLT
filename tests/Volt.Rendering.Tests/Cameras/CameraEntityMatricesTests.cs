using System.Numerics;
using Volt.Ecs;
using Volt.Ecs.Components.Rendering;
using Volt.Ecs.Components.Spatial;
using Volt.Rendering.Cameras;
using Xunit;

namespace Volt.Rendering.Tests.Cameras;

public sealed class CameraEntityMatricesTests
{
	private const float WideAspect = 1280.0f / 720.0f;

	private static readonly CameraComponent Settings = new(
		MathF.PI / 3.0f,
		0.1f,
		100.0f);

	private static readonly ViewportYOrientation Vulkan =
		ViewportYOrientation.YDown;

	[Fact]
	public void CreateMatrices_IdentityCameraAtOriginProducesIdentityView()
	{
		using World world = new();

		Entity camera = CreateCamera(
			world,
			Settings,
			Vector3.Zero,
			Quaternion.Identity);

		CameraEntityMatrices.CreateMatrices(
			world,
			camera,
			WideAspect,
			Vulkan,
			out Matrix4x4 view,
			out Matrix4x4 projection);

		Assert.Equal(Matrix4x4.Identity, view);
		Assert.NotEqual(Matrix4x4.Identity, projection);

		Assert.Equal(
			CameraProjection.CreatePerspective(
				Settings,
				WideAspect,
				Vulkan),
			projection);
	}

	[Fact]
	public void CreateMatrices_NonzeroPositionMovesTheWorld()
	{
		using World world = new();

		Entity camera = CreateCamera(
			world,
			Settings,
			new Vector3(0.0f, 0.0f, 5.0f),
			Quaternion.Identity);

		CameraEntityMatrices.CreateMatrices(
			world,
			camera,
			WideAspect,
			Vulkan,
			out Matrix4x4 view,
			out _);

		AssertVectorNear(
			new Vector3(0.0f, 0.0f, -5.0f),
			Vector3.Transform(Vector3.Zero, view));

		AssertVectorNear(
			new Vector3(1.0f, 2.0f, -4.0f),
			Vector3.Transform(new Vector3(1.0f, 2.0f, 1.0f), view));
	}

	[Fact]
	public void CreateMatrices_HonoursAnAsymmetricRotation()
	{
		using World world = new();

		Vector3 position = new(3.0f, 2.0f, 5.0f);

		Quaternion rotation = Quaternion.CreateFromYawPitchRoll(
			0.6f,
			-0.35f,
			0.0f);

		Entity camera = CreateCamera(
			world,
			Settings,
			position,
			rotation);

		CameraEntityMatrices.CreateMatrices(
			world,
			camera,
			WideAspect,
			Vulkan,
			out Matrix4x4 view,
			out _);

		// A point five units along the camera's forward axis must map
		// onto the negative view-space Z axis.
		Vector3 worldPoint = position +
			Vector3.Transform(-Vector3.UnitZ * 5.0f, rotation);

		AssertVectorNear(
			new Vector3(0.0f, 0.0f, -5.0f),
			Vector3.Transform(worldPoint, view));

		// A quarter turn about +Y makes -Z point along -X.
		Entity quarterTurn = CreateCamera(
			world,
			Settings,
			Vector3.Zero,
			Quaternion.CreateFromAxisAngle(
				Vector3.UnitY,
				MathF.PI / 2.0f));

		CameraEntityMatrices.CreateMatrices(
			world,
			quarterTurn,
			WideAspect,
			Vulkan,
			out Matrix4x4 turnedView,
			out _);

		AssertVectorNear(
			new Vector3(0.0f, 0.0f, -5.0f),
			Vector3.Transform(new Vector3(-5.0f, 0.0f, 0.0f), turnedView));
	}

	[Fact]
	public void CreateMatrices_MapsNearAndFarToDepthRange()
	{
		using World world = new();

		Entity camera = CreateCamera(
			world,
			Settings,
			Vector3.Zero,
			Quaternion.Identity);

		CameraEntityMatrices.CreateMatrices(
			world,
			camera,
			WideAspect,
			Vulkan,
			out _,
			out Matrix4x4 projection);

		Vector4 nearPlane = Vector4.Transform(
			new Vector4(0.0f, 0.0f, -Settings.NearPlane, 1.0f),
			projection);

		Vector4 farPlane = Vector4.Transform(
			new Vector4(0.0f, 0.0f, -Settings.FarPlane, 1.0f),
			projection);

		Assert.Equal(0.0f, nearPlane.Z / nearPlane.W, 4);
		Assert.Equal(1.0f, farPlane.Z / farPlane.W, 4);
	}

	[Fact]
	public void CreateMatrices_UsesTheRequestedViewportYOrientation()
	{
		using World world = new();

		Entity camera = CreateCamera(
			world,
			Settings,
			Vector3.Zero,
			Quaternion.Identity);

		CameraEntityMatrices.CreateMatrices(
			world,
			camera,
			WideAspect,
			ViewportYOrientation.YUp,
			out _,
			out Matrix4x4 up);

		CameraEntityMatrices.CreateMatrices(
			world,
			camera,
			WideAspect,
			Vulkan,
			out _,
			out Matrix4x4 down);

		Assert.Equal(-up.M22, down.M22);
		Assert.Equal(up.M11, down.M11);
		Assert.Equal(up.M33, down.M33);
		Assert.Equal(up.M34, down.M34);

		// World-space +Y must reach a negative NDC Y in Vulkan.
		Vector4 above = Vector4.Transform(
			new Vector4(0.0f, 1.0f, -3.0f, 1.0f),
			down);

		Assert.True(above.Y / above.W < 0.0f);

		Vector4 aboveUp = Vector4.Transform(
			new Vector4(0.0f, 1.0f, -3.0f, 1.0f),
			up);

		Assert.True(aboveUp.Y / aboveUp.W > 0.0f);
	}

	[Fact]
	public void CreateMatrices_ReflectsComponentUpdates()
	{
		using World world = new();

		Entity camera = CreateCamera(
			world,
			Settings,
			Vector3.Zero,
			Quaternion.Identity);

		CameraEntityMatrices.CreateMatrices(
			world,
			camera,
			WideAspect,
			Vulkan,
			out Matrix4x4 firstView,
			out Matrix4x4 firstProjection);

		world.GetComponent<PositionComponent>(camera).Value =
			new Vector3(2.0f, 0.0f, 0.0f);

		world.GetComponent<RotationComponent>(camera).Value =
			Quaternion.CreateFromAxisAngle(
				Vector3.UnitY,
				0.5f);

		ref CameraComponent settings =
			ref world.GetComponent<CameraComponent>(camera);

		settings.FarPlane = 250.0f;

		CameraEntityMatrices.CreateMatrices(
			world,
			camera,
			WideAspect,
			Vulkan,
			out Matrix4x4 secondView,
			out Matrix4x4 secondProjection);

		Assert.NotEqual(firstView, secondView);
		Assert.NotEqual(firstProjection, secondProjection);

		Assert.NotEqual(
			firstProjection.M33,
			secondProjection.M33);
	}

	[Fact]
	public void TryCreateMatrices_RejectsMissingComponents()
	{
		using World world = new();

		Entity complete = CreateCamera(
			world,
			Settings,
			Vector3.Zero,
			Quaternion.Identity);

		Entity missingCamera = world.CreateEntity();
		world.AddComponent(missingCamera, new PositionComponent(Vector3.Zero));
		world.AddComponent(
			missingCamera,
			new RotationComponent(Quaternion.Identity));

		Entity missingPosition = world.CreateEntity();
		world.AddComponent(missingPosition, Settings);
		world.AddComponent(
			missingPosition,
			new RotationComponent(Quaternion.Identity));

		Entity missingRotation = world.CreateEntity();
		world.AddComponent(missingRotation, Settings);
		world.AddComponent(
			missingRotation,
			new PositionComponent(Vector3.Zero));

		Assert.True(CameraEntityMatrices.TryCreateMatrices(
			world,
			complete,
			WideAspect,
			Vulkan,
			out _,
			out _));

		foreach (Entity rejected in new[]
		{
			missingCamera,
			missingPosition,
			missingRotation
		})
		{
			Assert.False(CameraEntityMatrices.TryCreateMatrices(
				world,
				rejected,
				WideAspect,
				Vulkan,
				out Matrix4x4 view,
				out Matrix4x4 projection));

			// Failure leaves the outputs in a defined state.
			Assert.Equal(Matrix4x4.Identity, view);
			Assert.Equal(Matrix4x4.Identity, projection);
		}
	}

	[Fact]
	public void CreateMatrices_NamesTheMissingComponent()
	{
		using World world = new();

		Entity entity = world.CreateEntity();
		world.AddComponent(entity, Settings);
		world.AddComponent(entity, new PositionComponent(Vector3.Zero));

		InvalidOperationException error =
			Assert.Throws<InvalidOperationException>(
				() => CameraEntityMatrices.CreateMatrices(
					world,
					entity,
					WideAspect,
					Vulkan,
					out _,
					out _));

		Assert.Contains(
			nameof(RotationComponent),
			error.Message,
			StringComparison.Ordinal);
	}

	[Fact]
	public void Matrices_RejectDestroyedAndStaleCameraEntities()
	{
		using World world = new();

		Entity camera = CreateCamera(
			world,
			Settings,
			Vector3.Zero,
			Quaternion.Identity);

		Entity stale = camera;

		Assert.True(world.DestroyEntity(camera));

		Entity replacement = CreateCamera(
			world,
			Settings,
			Vector3.UnitZ,
			Quaternion.Identity);

		// The old handle must not resolve to the replacement.
		Assert.False(CameraEntityMatrices.TryCreateMatrices(
			world,
			stale,
			WideAspect,
			Vulkan,
			out Matrix4x4 staleView,
			out Matrix4x4 staleProjection));

		Assert.Equal(Matrix4x4.Identity, staleView);
		Assert.Equal(Matrix4x4.Identity, staleProjection);

		Assert.True(CameraEntityMatrices.TryCreateMatrices(
			world,
			replacement,
			WideAspect,
			Vulkan,
			out _,
			out _));

		Assert.Throws<InvalidOperationException>(
			() => CameraEntityMatrices.CreateMatrices(
				world,
				stale,
				WideAspect,
				Vulkan,
				out _,
				out _));
	}

	[Fact]
	public void CreateMatrices_RejectsInvalidAspect()
	{
		using World world = new();

		Entity camera = CreateCamera(
			world,
			Settings,
			Vector3.Zero,
			Quaternion.Identity);

		Assert.Throws<ArgumentOutOfRangeException>(
			() => CameraEntityMatrices.CreateMatrices(
				world,
				camera,
				0.0f,
				Vulkan,
				out _,
				out _));

		Assert.Throws<ArgumentOutOfRangeException>(
			() => CameraEntityMatrices.TryCreateMatrices(
				world,
				camera,
				float.NaN,
				Vulkan,
				out _,
				out _));
	}

	[Fact]
	public void TryCreateMatrices_RejectsNullWorld()
	{
		Assert.Throws<ArgumentNullException>(
			() => CameraEntityMatrices.TryCreateMatrices(
				null!,
				default,
				WideAspect,
				Vulkan,
				out _,
				out _));
	}

	private static Entity CreateCamera(
		World world,
		CameraComponent settings,
		Vector3 position,
		Quaternion rotation)
	{
		Entity entity = world.CreateEntity();

		world.AddComponent(entity, settings);
		world.AddComponent(entity, new PositionComponent(position));
		world.AddComponent(entity, new RotationComponent(rotation));

		return entity;
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
