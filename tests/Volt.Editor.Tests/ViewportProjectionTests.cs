using System.Numerics;
using Volt.Editor.Viewport;
using Volt.Ui;

namespace Volt.Editor.Tests;

/// <summary>
/// Verifies that the scene viewport projects and navigates in the same
/// pixel space that Volt.Ui lays out and paints in.
/// </summary>
public sealed class ViewportProjectionTests
{
	private static readonly UiRect Bounds = new(64f, 32f, 1280f, 720f);

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
	public void DefaultEditorCamera_SitsOnTheEngineConvention()
	{
		EditorCamera camera = new();

		// Right-handed, +Y up: the authoring camera and a game camera
		// must agree, because both project the same scene.
		Assert.Equal(EditorCamera.DefaultPosition, camera.Position);
		Assert.Equal(1f, camera.Forward.Length(), 4);
		Assert.Equal(1f, camera.Right.Length(), 4);
		Assert.Equal(1f, camera.Up.Length(), 4);
		Assert.True(MathF.Abs(Vector3.Dot(camera.Forward, camera.Right)) < 1e-4f);
		Assert.True(MathF.Abs(Vector3.Dot(camera.Forward, camera.Up)) < 1e-4f);
		Assert.True(Vector3.Dot(camera.Up, Vector3.UnitY) > 0f);
	}

	[Fact]
	public void DefaultEditorCamera_FacesTheAuthoredScene()
	{
		// The editor opens on the scene the developer is authoring, so
		// the origin has to be in front of the camera and in the middle
		// of the viewport. A camera that faced away would show an empty
		// viewport on startup.
		EditorCamera camera = new();

		Assert.True(Vector3.Dot(
			camera.Forward,
			Vector3.Normalize(Vector3.Zero - camera.Position)) > 0.999f);

		Matrix4x4 viewProjection = CreateViewProjection(camera);

		Assert.True(ViewportMath.TryProject(
			viewProjection,
			Vector3.Zero,
			Bounds,
			out UiPoint origin));

		Assert.Equal(Bounds.Center.X, origin.X, 1);
		Assert.Equal(Bounds.Center.Y, origin.Y, 1);
	}

	[Fact]
	public void OverlayProjection_IsYUpInPixelSpace()
	{
		EditorCamera camera = new();
		Matrix4x4 viewProjection = CreateViewProjection(camera);
		Vector3 target = camera.Position + (camera.Forward * 12f);

		Assert.True(ViewportMath.TryProject(
			viewProjection,
			target,
			Bounds,
			out UiPoint center));

		Assert.Equal(Bounds.Center.X, center.X, 2);
		Assert.Equal(Bounds.Center.Y, center.Y, 2);

		// Pixel space is Y-down, so world up must move toward a
		// smaller pixel Y. An inverted projection would mirror the
		// scene overlays and their hit testing together.
		Assert.True(ViewportMath.TryProject(
			viewProjection,
			target + (Vector3.UnitY * 2f),
			Bounds,
			out UiPoint above));

		Assert.True(above.Y < center.Y);

		Assert.True(ViewportMath.TryProject(
			viewProjection,
			target + (camera.Right * 2f),
			Bounds,
			out UiPoint right));

		Assert.True(right.X > center.X);
	}

	[Fact]
	public void Projection_IsRelativeToTheViewportBounds()
	{
		// The viewport panel sits somewhere inside the dock layout, so
		// the same world point must land at the same spot inside the
		// panel wherever that panel is: drawing and hit testing both
		// use the panel's global rectangle.
		EditorCamera camera = new();
		UiRect origin = new(0f, 0f, 800f, 600f);
		UiRect docked = new(317f, 121f, 800f, 600f);
		Matrix4x4 viewProjection = CreateViewProjection(camera, origin);

		// Anything in front of the camera and inside the frustum will
		// do; the assertion is about where the panel sits, not where
		// the camera is.
		Vector3 world =
			camera.Position +
			(camera.Forward * 8f) +
			(camera.Right * 2.5f) +
			(camera.Up * 1.5f);

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
		EditorCamera camera = new();
		Matrix4x4 viewProjection = CreateViewProjection(camera);

		Assert.False(ViewportMath.TryProject(
			viewProjection,
			camera.Position - (camera.Forward * 5f),
			Bounds,
			out _));

		Assert.False(ViewportMath.TryProject(
			viewProjection,
			camera.Position + (camera.Forward * 12f) +
				(camera.Right * 2000f),
			Bounds,
			out _));

		Assert.True(ViewportMath.TryProject(
			viewProjection,
			camera.Position + (camera.Forward * 12f),
			Bounds,
			out _));
	}

	[Fact]
	public void Look_TurnsWithThePointer_AndClampsPitch()
	{
		EditorCamera camera = new();

		camera.SetPose(Vector3.Zero, 0f, 0f);

		// SDL reports motion to the right as positive X, and the
		// pointer is grabbed in the middle of the viewport: dragging
		// right must look right.
		camera.Look(120f, 0f);

		Assert.True(camera.Yaw < 0f);
		Assert.True(camera.Forward.X > 0f);

		camera.Look(0f, -40f);

		Assert.True(camera.Pitch > 0f);

		// One wild delta must not flip the view over the pole.
		camera.Look(0f, -100000f);

		Assert.True(camera.Pitch <= (MathF.PI / 2f) - 0.001f);
		Assert.True(Vector3.Dot(camera.Up, Vector3.UnitY) > 0f);
	}

	[Fact]
	public void Move_TravelsAtTheDocumentedSpeed()
	{
		EditorCamera camera = new();

		camera.MoveSpeed = 4f;

		Vector3 start = camera.Position;
		Vector3 forward = camera.Forward;

		camera.Move(
			forward: true,
			backward: false,
			strafeLeft: false,
			strafeRight: false,
			up: false,
			down: false,
			sprint: false,
			deltaSeconds: 0.05f);

		Vector3 step = camera.Position - start;

		Assert.Equal(4f * 0.05f, step.Length(), 3);
		Assert.True(Vector3.Dot(Vector3.Normalize(step), forward) > 0.999f);

		start = camera.Position;

		camera.Move(
			forward: true,
			backward: false,
			strafeLeft: false,
			strafeRight: false,
			up: false,
			down: false,
			sprint: true,
			deltaSeconds: 0.05f);

		Assert.Equal(4f * 3f * 0.05f, Vector3.Distance(start, camera.Position), 3);
	}

	[Fact]
	public void Move_StrafesSideways_AndNormalizesDiagonals()
	{
		EditorCamera camera = new();

		camera.MoveSpeed = 4f;

		Vector3 start = camera.Position;
		Vector3 right = camera.Right;

		camera.Move(
			forward: false,
			backward: false,
			strafeLeft: false,
			strafeRight: true,
			up: false,
			down: false,
			sprint: false,
			deltaSeconds: 0.05f);

		Vector3 step = camera.Position - start;

		Assert.Equal(4f * 0.05f, step.Length(), 3);
		Assert.True(Vector3.Dot(Vector3.Normalize(step), right) > 0.999f);

		// Two axes at once must not travel faster than one.
		start = camera.Position;

		camera.Move(
			forward: true,
			backward: false,
			strafeLeft: false,
			strafeRight: true,
			up: false,
			down: false,
			sprint: false,
			deltaSeconds: 0.05f);

		Assert.Equal(4f * 0.05f, Vector3.Distance(start, camera.Position), 3);

		// Opposite keys cancel rather than fighting.
		start = camera.Position;

		camera.Move(
			forward: true,
			backward: true,
			strafeLeft: false,
			strafeRight: false,
			up: false,
			down: false,
			sprint: false,
			deltaSeconds: 0.05f);

		Assert.Equal(start, camera.Position);
	}

	[Fact]
	public void Move_IgnoresPausedFrames_AndClampsLongOnes()
	{
		EditorCamera camera = new();

		camera.MoveSpeed = 4f;

		Vector3 start = camera.Position;

		camera.Move(
			true,
			false,
			false,
			false,
			false,
			false,
			false,
			deltaSeconds: 0f);

		camera.Move(
			true,
			false,
			false,
			false,
			false,
			false,
			false,
			deltaSeconds: float.NaN);

		Assert.Equal(start, camera.Position);

		// Returning from a suspended window must not teleport the
		// camera across the scene.
		camera.Move(
			true,
			false,
			false,
			false,
			false,
			false,
			false,
			deltaSeconds: 30f);

		Assert.Equal(4f * 0.1f, Vector3.Distance(start, camera.Position), 3);
	}

	[Fact]
	public void Pan_MovesAgainstTheDrag_SoTheContentFollowsThePointer()
	{
		EditorCamera camera = new();

		camera.MoveSpeed = 4f;

		float unitsPerPixel = 4f * 0.0025f;

		Vector3 start = camera.Position;

		camera.Pan(100f, 0f);

		Vector3 draggedRight = camera.Position - start;

		Assert.True(Vector3.Dot(draggedRight, camera.Right) < 0f);
		Assert.Equal(100f * unitsPerPixel, draggedRight.Length(), 3);

		start = camera.Position;

		// Pointer Y is down, so dragging down must raise the camera
		// and push the scene down the screen with it.
		camera.Pan(0f, 50f);

		Vector3 draggedDown = camera.Position - start;

		Assert.True(Vector3.Dot(draggedDown, camera.Up) > 0f);
		Assert.Equal(50f * unitsPerPixel, draggedDown.Length(), 3);
	}

	[Fact]
	public void Zoom_TicksDollyAlongTheViewDirection()
	{
		EditorCamera camera = new();

		camera.MoveSpeed = 4f;

		Vector3 start = camera.Position;
		Vector3 forward = camera.Forward;

		camera.Zoom(2f);

		Vector3 forwardDelta = camera.Position - start;

		Assert.Equal(2f * 4f * 0.25f, forwardDelta.Length(), 3);
		Assert.True(Vector3.Dot(Vector3.Normalize(forwardDelta), forward) > 0.999f);

		start = camera.Position;

		camera.Zoom(-1f);

		Assert.True(Vector3.Dot(camera.Position - start, forward) < 0f);
	}

	[Fact]
	public void Focus_FramesTheTarget_WithoutChangingTheOrientation()
	{
		EditorCamera camera = new();

		camera.Look(90f, -30f);

		float yaw = camera.Yaw;
		float pitch = camera.Pitch;
		Vector3 target = new(3f, 1f, -2f);

		camera.Focus(target, 2f);

		Assert.Equal(yaw, camera.Yaw);
		Assert.Equal(pitch, camera.Pitch);
		Assert.Equal(6f, Vector3.Distance(camera.Position, target), 3);
		Assert.True(Vector3.Dot(
			camera.Forward,
			Vector3.Normalize(target - camera.Position)) > 0.999f);

		Matrix4x4 viewProjection = CreateViewProjection(camera);

		Assert.True(ViewportMath.TryProject(
			viewProjection,
			target,
			Bounds,
			out UiPoint screen));

		Assert.Equal(Bounds.Center.X, screen.X, 1);
		Assert.Equal(Bounds.Center.Y, screen.Y, 1);
	}

	[Fact]
	public void SetPose_RestoresADocumentedPose()
	{
		EditorCamera camera = new();

		camera.SetPose(new Vector3(1f, 2f, 3f), 0.5f, 0.25f);

		Assert.Equal(new Vector3(1f, 2f, 3f), camera.Position);
		Assert.Equal(0.5f, camera.Yaw, 5);
		Assert.Equal(0.25f, camera.Pitch, 5);

		camera.SetPose(Vector3.Zero, 0f, 0f);

		Assert.True(Vector3.Dot(camera.Forward, -Vector3.UnitZ) > 0.999f);
		Assert.Equal(Vector3.Zero, camera.Position);
	}

	[Fact]
	public void WorldPerPixel_ScalesWithDistanceAndViewportHeight()
	{
		float near = ViewportMath.WorldPerPixel(MathF.PI / 3f, 600f, 10f);

		Assert.True(near > 0f);
		Assert.Equal(
			near * 2f,
			ViewportMath.WorldPerPixel(MathF.PI / 3f, 600f, 20f),
			5);

		// A collapsed or invalid measurement must not scale a gizmo
		// into a degenerate one.
		Assert.Equal(0f, ViewportMath.WorldPerPixel(MathF.PI / 3f, 0f, 10f));
		Assert.Equal(0f, ViewportMath.WorldPerPixel(MathF.PI / 3f, 600f, 0f));
		Assert.Equal(0f, ViewportMath.WorldPerPixel(MathF.PI / 3f, 600f, -4f));
	}

	private static Matrix4x4 CreateViewProjection(
		EditorCamera camera,
		UiRect? bounds = null)
	{
		UiRect target = bounds ?? Bounds;

		return camera.CreateView() *
			camera.CreateProjection(
				target.Width / target.Height,
				yDown: false);
	}
}
