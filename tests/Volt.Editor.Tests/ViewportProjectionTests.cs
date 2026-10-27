using System.Numerics;
using Volt.Editor.Viewport;
using Volt.Ui;

namespace Volt.Editor.Tests;

/// <summary>
/// Verifies that the scene viewport projects, draws, and hit-tests in
/// the same pixel space that Volt.Ui lays out and paints in.
/// </summary>
public sealed class ViewportProjectionTests
{
	[Fact]
	public void ClipSpace_CornersMapToBoundsCorners()
	{
		// TryProject is an affine map from clip space to pixel space,
		// so an identity view-projection hands clip coordinates
		// straight to it. Clip top-left is pixel top-left, matching
		// UiClipSpace and the scissor rectangles.
		UiRect bounds = new(40f, 24f, 800f, 600f);
		Matrix4x4 identity = Matrix4x4.Identity;

		Assert.True(ViewportMath.TryProject(
			identity,
			new Vector3(-1f, 1f, 0f),
			bounds,
			out UiPoint topLeft));

		Assert.Equal(bounds.X, topLeft.X, 3);
		Assert.Equal(bounds.Y, topLeft.Y, 3);

		Assert.True(ViewportMath.TryProject(
			identity,
			new Vector3(1f, -1f, 0f),
			bounds,
			out UiPoint bottomRight));

		Assert.Equal(bounds.Right, bottomRight.X, 3);
		Assert.Equal(bounds.Bottom, bottomRight.Y, 3);

		Assert.True(ViewportMath.TryProject(
			identity,
			Vector3.Zero,
			bounds,
			out UiPoint center));

		Assert.Equal(bounds.Center.X, center.X, 3);
		Assert.Equal(bounds.Center.Y, center.Y, 3);
	}

	[Fact]
	public void CameraTarget_StaysCentered_AndWorldUpMovesUpTheScreen()
	{
		OrbitCamera camera = new();
		UiRect bounds = new(0f, 0f, 960f, 540f);
		Matrix4x4 viewProjection = camera.CreateViewProjection(bounds);

		Assert.True(ViewportMath.TryProject(
			viewProjection,
			camera.Target,
			bounds,
			out UiPoint center));

		Assert.Equal(bounds.Center.X, center.X, 2);
		Assert.Equal(bounds.Center.Y, center.Y, 2);

		// Pixel space is Y-down, so world up must move toward a
		// smaller pixel Y. An inverted projection would mirror the
		// scene and its hit testing together.
		Assert.True(ViewportMath.TryProject(
			viewProjection,
			camera.Target + (camera.Up * 2f),
			bounds,
			out UiPoint above));

		Assert.True(above.Y < center.Y);

		Assert.True(ViewportMath.TryProject(
			viewProjection,
			camera.Target + (camera.Right * 2f),
			bounds,
			out UiPoint right));

		Assert.True(right.X > center.X);
	}

	[Fact]
	public void OrbitZoomAndFocus_KeepTheTargetCentered()
	{
		OrbitCamera camera = new();
		UiRect bounds = new(64f, 32f, 1280f, 720f);

		camera.Orbit(37f, -18f);
		camera.Zoom(3f);
		camera.Focus(new Vector3(3f, 1f, -2f));

		Matrix4x4 viewProjection = camera.CreateViewProjection(bounds);

		Assert.True(ViewportMath.TryProject(
			viewProjection,
			camera.Target,
			bounds,
			out UiPoint screen));

		Assert.Equal(bounds.Center.X, screen.X, 2);
		Assert.Equal(bounds.Center.Y, screen.Y, 2);
	}

	[Fact]
	public void Projection_IsRelativeToTheViewportBounds()
	{
		// The viewport panel sits somewhere inside the dock layout, so
		// the same world point must land at the same spot inside the
		// panel wherever that panel is: drawing and hit testing both
		// use the panel's global rectangle.
		OrbitCamera camera = new();
		UiRect origin = new(0f, 0f, 800f, 600f);
		UiRect docked = new(317f, 121f, 800f, 600f);
		Matrix4x4 viewProjection = camera.CreateViewProjection(origin);
		Vector3 world = new(2.5f, 1.5f, -3.25f);

		Assert.True(ViewportMath.TryProject(
			viewProjection,
			world,
			origin,
			out UiPoint local));

		Assert.True(ViewportMath.TryProject(
			viewProjection,
			world,
			docked,
			out UiPoint global));

		Assert.Equal(local.X + docked.X, global.X, 3);
		Assert.Equal(local.Y + docked.Y, global.Y, 3);
	}

	[Fact]
	public void PointsBehindOrFarOutsideTheViewport_AreRejected()
	{
		OrbitCamera camera = new();
		UiRect bounds = new(0f, 0f, 800f, 600f);
		Matrix4x4 viewProjection = camera.CreateViewProjection(bounds);

		Assert.False(ViewportMath.TryProject(
			viewProjection,
			camera.Target - (camera.Forward * 50f),
			bounds,
			out _));

		Assert.False(ViewportMath.TryProject(
			viewProjection,
			camera.Target + (camera.Right * 2000f),
			bounds,
			out _));

		Assert.True(ViewportMath.TryProject(
			viewProjection,
			camera.Target,
			bounds,
			out _));
	}
}
