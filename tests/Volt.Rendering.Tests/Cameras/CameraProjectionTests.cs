using System.Numerics;
using Volt.Ecs.Components.Rendering;
using Volt.Rendering.Cameras;
using Xunit;

namespace Volt.Rendering.Tests.Cameras;

public sealed class CameraProjectionTests
{
	private const float WideAspect = 1280.0f / 720.0f;

	private static readonly CameraComponent Camera = new(
		MathF.PI / 3.0f,
		0.1f,
		100.0f);

	[Fact]
	public void CreatePerspective_YUpReusesTheComponentProjection()
	{
		Matrix4x4 expected = Camera.CreateProjectionMatrix(WideAspect);

		Matrix4x4 actual = CameraProjection.CreatePerspective(
			Camera,
			WideAspect,
			ViewportYOrientation.YUp);

		Assert.Equal(expected, actual);
	}

	[Fact]
	public void CreatePerspective_DefaultsToYUp()
	{
		Assert.Equal(
			CameraProjection.CreatePerspective(
				Camera,
				WideAspect,
				ViewportYOrientation.YUp),
			CameraProjection.CreatePerspective(Camera, WideAspect));
	}

	[Fact]
	public void CreatePerspective_YDownNegatesOnlyTheYRow()
	{
		Matrix4x4 up = Camera.CreateProjectionMatrix(WideAspect);

		Matrix4x4 down = CameraProjection.CreatePerspective(
			Camera,
			WideAspect,
			ViewportYOrientation.YDown);

		Assert.Equal(-up.M22, down.M22);

		Assert.Equal(up.M11, down.M11);
		Assert.Equal(up.M12, down.M12);
		Assert.Equal(up.M13, down.M13);
		Assert.Equal(up.M14, down.M14);

		Assert.Equal(up.M21, down.M21);
		Assert.Equal(up.M23, down.M23);
		Assert.Equal(up.M24, down.M24);

		Assert.Equal(up.M31, down.M31);
		Assert.Equal(up.M32, down.M32);
		Assert.Equal(up.M33, down.M33);
		Assert.Equal(up.M34, down.M34);

		Assert.Equal(up.M41, down.M41);
		Assert.Equal(up.M42, down.M42);
		Assert.Equal(up.M43, down.M43);
		Assert.Equal(up.M44, down.M44);
	}

	[Fact]
	public void CreatePerspective_YDownPutsWorldUpAtTheTopOfTheImage()
	{
		Matrix4x4 projection = CameraProjection.CreatePerspective(
			Camera,
			WideAspect,
			ViewportYOrientation.YDown);

		// A point one unit above the view centre, three units ahead.
		Vector4 clip = Vector4.Transform(
			new Vector4(0.0f, 1.0f, -3.0f, 1.0f),
			projection);

		// Vulkan viewports grow downwards, so world +Y must reach a
		// negative NDC Y. The vertical scale is 1 / tan(30 degrees)
		// and the point is three units ahead.
		Assert.True(clip.Y / clip.W < 0.0f);
		Assert.Equal(
			-1.0f / (MathF.Tan(MathF.PI / 6.0f) * 3.0f),
			clip.Y / clip.W,
			4);
	}

	[Theory]
	[InlineData(ViewportYOrientation.YUp)]
	[InlineData(ViewportYOrientation.YDown)]
	public void CreatePerspective_MapsNearAndFarToDepthInterval(
		ViewportYOrientation orientation)
	{
		Matrix4x4 projection = CameraProjection.CreatePerspective(
			Camera,
			WideAspect,
			orientation);

		Vector4 nearPlane = Vector4.Transform(
			new Vector4(0.0f, 0.0f, -Camera.NearPlane, 1.0f),
			projection);

		Vector4 farPlane = Vector4.Transform(
			new Vector4(0.0f, 0.0f, -Camera.FarPlane, 1.0f),
			projection);

		Assert.True(nearPlane.W > 0.0f);
		Assert.True(farPlane.W > 0.0f);

		Assert.Equal(0.0f, nearPlane.Z / nearPlane.W, 4);
		Assert.Equal(1.0f, farPlane.Z / farPlane.W, 4);
	}

	[Fact]
	public void CreatePerspective_UsesTheComponentsClippingPlanes()
	{
		CameraComponent longRange = CameraComponent.Default;

		Matrix4x4 shortRange = CameraProjection.CreatePerspective(
			Camera,
			WideAspect);

		Matrix4x4 defaultRange = CameraProjection.CreatePerspective(
			longRange,
			WideAspect);

		// CameraComponent.Default uses a far plane of 1000; the
		// sample uses 100, so the depth mapping must differ. M33 and
		// M43 encode near/far, while M34 is always -1 for a
		// right-handed perspective projection.
		Assert.NotEqual(shortRange.M33, defaultRange.M33);
		Assert.NotEqual(shortRange.M43, defaultRange.M43);
		Assert.Equal(-1.0f, shortRange.M34);
		Assert.Equal(-1.0f, defaultRange.M34);
	}

	[Fact]
	public void CreatePerspective_ChangesWithAspectRatio()
	{
		Matrix4x4 wide = CameraProjection.CreatePerspective(
			Camera,
			1280.0f / 720.0f,
			ViewportYOrientation.YDown);

		Matrix4x4 sameAspect = CameraProjection.CreatePerspective(
			Camera,
			960.0f / 540.0f,
			ViewportYOrientation.YDown);

		Matrix4x4 square = CameraProjection.CreatePerspective(
			Camera,
			1.0f,
			ViewportYOrientation.YDown);

		Assert.Equal(wide, sameAspect);
		Assert.NotEqual(wide, square);

		// A narrower viewport needs a larger horizontal scale.
		Assert.True(square.M11 > wide.M11);
	}

	[Theory]
	[InlineData(0.0f)]
	[InlineData(-1.0f)]
	[InlineData(float.NaN)]
	[InlineData(float.PositiveInfinity)]
	public void CreatePerspective_RejectsInvalidAspect(float aspectRatio)
	{
		Assert.Throws<ArgumentOutOfRangeException>(
			() => CameraProjection.CreatePerspective(
				Camera,
				aspectRatio));
	}

	[Fact]
	public void CreatePerspective_RejectsInvalidCameraSettings()
	{
		CameraComponent inverted = new(
			MathF.PI / 3.0f,
			10.0f,
			1.0f);

		Assert.Throws<ArgumentOutOfRangeException>(
			() => CameraProjection.CreatePerspective(
				inverted,
				WideAspect));
	}

	[Fact]
	public void CreatePerspective_RejectsUnknownOrientation()
	{
		Assert.Throws<ArgumentOutOfRangeException>(
			() => CameraProjection.CreatePerspective(
				Camera,
				WideAspect,
				(ViewportYOrientation)int.MaxValue));
	}

	[Fact]
	public void CreatePerspective_DoesNotAllocate()
	{
		for (int iteration = 0; iteration < 64; iteration++)
		{
			CameraProjection.CreatePerspective(
				Camera,
				WideAspect,
				ViewportYOrientation.YDown);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int iteration = 0; iteration < 1024; iteration++)
		{
			CameraProjection.CreatePerspective(
				Camera,
				WideAspect,
				ViewportYOrientation.YDown);
		}

		long after = GC.GetAllocatedBytesForCurrentThread();

		Assert.Equal(before, after);
	}
}
