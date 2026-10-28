using System.Numerics;
using Volt.Editor.Viewport;
using Volt.Ui;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the viewport's authoring math: the picking ray, the oriented
/// box test, the constrained axis drag, and the transform gizmo.
/// Every assertion runs without a window or a GPU.
/// </summary>
public sealed class ViewportPickingTests
{
	private static readonly UiRect Bounds = new(0f, 0f, 800f, 600f);

	/// <summary>The distance the test camera sits from the origin.</summary>
	private const float FramingDistance = 10f;

	private const float FieldOfView = MathF.PI / 3f;

	/// <summary>
	/// World units per pixel at the object being framed, which is what
	/// a caller passes to keep a gizmo's screen size constant.
	/// </summary>
	private static float UnitsPerPixel => ViewportMath.WorldPerPixel(
		FieldOfView,
		Bounds.Height,
		FramingDistance);

	[Fact]
	public void RayThroughTheCentre_HitsTheBoxUnderThePointer()
	{
		(EditorCamera camera, Matrix4x4 viewProjection) = CreateFramedView();

		Assert.True(ViewportMath.TryCreateRay(
			viewProjection,
			Bounds,
			Bounds.Center,
			out Vector3 origin,
			out Vector3 direction));

		// The camera looks down -Z from z = 10, so the ray must run
		// along -Z and start on the near plane.
		Assert.True(Vector3.Dot(direction, -Vector3.UnitZ) > 0.999f);
		Assert.Equal(10f - camera.NearPlane, origin.Z, 3);

		Assert.True(ViewportMath.TryIntersectBox(
			Matrix4x4.Identity,
			Vector3.Zero,
			new Vector3(0.5f),
			origin,
			direction,
			out float distance));

		// The unit cube's front face is at z = +0.5.
		Assert.Equal(10f - camera.NearPlane - 0.5f, distance, 3);
	}

	[Fact]
	public void RayNearTheEdge_MissesTheCentreObject()
	{
		(EditorCamera camera, Matrix4x4 viewProjection) = CreateFramedView();

		Assert.True(ViewportMath.TryCreateRay(
			viewProjection,
			Bounds,
			new UiPoint(Bounds.X + 2f, Bounds.Y + 2f),
			out Vector3 origin,
			out Vector3 direction));

		Assert.False(ViewportMath.TryIntersectBox(
			Matrix4x4.Identity,
			Vector3.Zero,
			new Vector3(0.5f),
			origin,
			direction,
			out _));
	}

	[Fact]
	public void PickingPrefersTheNearestObjectInFrontOfTheCamera()
	{
		(EditorCamera camera, Matrix4x4 viewProjection) = CreateFramedView();

		Assert.True(ViewportMath.TryCreateRay(
			viewProjection,
			Bounds,
			Bounds.Center,
			out Vector3 origin,
			out Vector3 direction));

		Assert.True(ViewportMath.TryIntersectBox(
			Matrix4x4.CreateTranslation(0f, 0f, 3f),
			Vector3.Zero,
			new Vector3(0.5f),
			origin,
			direction,
			out float nearDistance));

		Assert.True(ViewportMath.TryIntersectBox(
			Matrix4x4.CreateTranslation(0f, 0f, -3f),
			Vector3.Zero,
			new Vector3(0.5f),
			origin,
			direction,
			out float farDistance));

		Assert.True(nearDistance < farDistance);

		// A ray that starts behind an object still reports the surface
		// it meets, so overlapping objects resolve by distance alone.
		Assert.False(ViewportMath.TryIntersectBox(
			Matrix4x4.CreateTranslation(0f, 0f, 20f),
			Vector3.Zero,
			new Vector3(0.5f),
			origin,
			direction,
			out _));
	}

	[Fact]
	public void BoxTest_RespectsTheObjectOrientationAndScale()
	{
		Vector3 origin = new(1f, 0f, 10f);
		Vector3 direction = -Vector3.UnitZ;
		Vector3 halfExtent = new(0.5f, 0.5f, 1.5f);

		// Unrotated, the long axis runs along +Z, so a ray at x = 1
		// passes beside the object.
		Assert.False(ViewportMath.TryIntersectBox(
			Matrix4x4.Identity,
			Vector3.Zero,
			halfExtent,
			origin,
			direction,
			out _));

		// Turned a quarter turn about +Y, the long axis runs along X,
		// and the same ray hits the front face of the rotated box.
		Assert.True(ViewportMath.TryIntersectBox(
			Matrix4x4.CreateRotationY(MathF.PI / 2f),
			Vector3.Zero,
			halfExtent,
			origin,
			direction,
			out float distance));

		Assert.Equal(10f - 0.5f, distance, 3);
	}

	[Fact]
	public void BoxTest_ReportsTheExitSurfaceWhenTheRayStartsInside()
	{
		Assert.True(ViewportMath.TryIntersectBox(
			Matrix4x4.Identity,
			Vector3.Zero,
			new Vector3(0.5f),
			Vector3.Zero,
			Vector3.UnitX,
			out float distance));

		Assert.Equal(0.5f, distance, 3);
	}

	[Fact]
	public void AxisDrag_FollowsThePointerRayOntoTheAxis()
	{
		(EditorCamera camera, Matrix4x4 viewProjection) = CreateFramedView();

		Assert.True(ViewportMath.TryCreateRay(
			viewProjection,
			Bounds,
			Bounds.Center,
			out Vector3 rayOrigin,
			out Vector3 rayDirection));

		Assert.True(ViewportMath.TryProjectOntoAxis(
			rayOrigin,
			rayDirection,
			camera.Position,
			Vector3.Zero,
			Vector3.UnitX,
			out float start));

		Assert.Equal(0f, start, 3);

		// Point the pointer at a known point on the axis: the solve
		// has to land on that point exactly.
		Vector3 target = new(2f, 0f, 0f);

		Assert.True(ViewportMath.TryProject(
			viewProjection,
			target,
			Bounds,
			out UiPoint pixel));

		Assert.True(ViewportMath.TryCreateRay(
			viewProjection,
			Bounds,
			pixel,
			out rayOrigin,
			out rayDirection));

		Assert.True(ViewportMath.TryProjectOntoAxis(
			rayOrigin,
			rayDirection,
			camera.Position,
			Vector3.Zero,
			Vector3.UnitX,
			out float dragged));

		Assert.Equal(2f, dragged, 2);

		// An axis aimed straight at the camera has no stable plane to
		// solve against, so the drag is refused instead of jumping.
		Assert.False(ViewportMath.TryProjectOntoAxis(
			rayOrigin,
			rayDirection,
			camera.Position,
			Vector3.Zero,
			Vector3.UnitZ,
			out _));
	}

	[Fact]
	public void TranslateGizmo_BuildsFourHandlesAtAConstantScreenSize()
	{
		(EditorCamera camera, Matrix4x4 viewProjection) = CreateFramedView();

		ViewportGizmo gizmo = new();

		Assert.True(gizmo.Build(
			EditorTool.Translate,
			Vector3.Zero,
			Quaternion.Identity,
			local: false,
			scale: 1f,
			viewProjection,
			Bounds,
			UnitsPerPixel));

		Assert.True(gizmo.IsVisible);
		Assert.Equal(4, gizmo.Handles.Count);
		Assert.Equal(Vector3.Zero, gizmo.Origin);

		// The X handle is square to the camera, so its pixels measure
		// the documented handle length: the size is screen constant.
		GizmoHandleHit xAxis = gizmo.Handles.First(
			handle => handle.Handle == GizmoHandle.AxisX);

		Assert.True(xAxis.Visible);
		Assert.Equal(
			ViewportGizmo.AxisLengthPixels,
			Distance(xAxis.StartPixel, xAxis.EndPixel),
			1);

		Assert.True(xAxis.EndPixel.X > xAxis.StartPixel.X);
	}

	[Fact]
	public void GizmoPicking_FindsTheHandleUnderThePointer()
	{
		(EditorCamera camera, Matrix4x4 viewProjection) = CreateFramedView();

		ViewportGizmo gizmo = new();

		Assert.True(gizmo.Build(
			EditorTool.Translate,
			Vector3.Zero,
			Quaternion.Identity,
			local: false,
			scale: 1f,
			viewProjection,
			Bounds,
			UnitsPerPixel));

		GizmoHandleHit xAxis = gizmo.Handles.First(
			handle => handle.Handle == GizmoHandle.AxisX);

		// Taken near the far end of the handle: the XZ plane handle is
		// drawn close to the gizmo's own origin, and picking prefers it
		// wherever the two overlap.
		UiPoint onAxis = Lerp(xAxis.StartPixel, xAxis.EndPixel, 0.85f);

		Assert.Equal(
			GizmoHandle.AxisX,
			gizmo.Pick(onAxis, ViewportMath.GizmoPickTolerancePixels).Handle);

		GizmoHandleHit yAxis = gizmo.Handles.First(
			handle => handle.Handle == GizmoHandle.AxisY);

		Assert.Equal(
			GizmoHandle.AxisY,
			gizmo.Pick(
				Midpoint(yAxis.StartPixel, yAxis.EndPixel),
				ViewportMath.GizmoPickTolerancePixels).Handle);

		// The plane handle owns the square between its axes.
		GizmoHandleHit plane = gizmo.Handles.First(
			handle => handle.Handle == GizmoHandle.PlaneXZ);

		Assert.Equal(
			GizmoHandle.PlaneXZ,
			gizmo.Pick(
				Midpoint(plane.StartPixel, plane.EndPixel),
				ViewportMath.GizmoPickTolerancePixels).Handle);

		// Empty space, and pointer offsets beyond the tolerance: both
		// are misses.
		Assert.Equal(
			GizmoHandle.None,
			gizmo.Pick(
				new UiPoint(Bounds.Right - 4f, Bounds.Bottom - 4f),
				ViewportMath.GizmoPickTolerancePixels).Handle);

		Assert.Equal(
			GizmoHandle.None,
			gizmo.Pick(
				new UiPoint(
					onAxis.X,
					onAxis.Y - (ViewportMath.GizmoPickTolerancePixels * 3f)),
				ViewportMath.GizmoPickTolerancePixels).Handle);
	}

	[Fact]
	public void LocalGizmo_IsOrientedAlongTheObjectAxes()
	{
		(EditorCamera camera, Matrix4x4 viewProjection) = CreateFramedView();

		ViewportGizmo gizmo = new();

		Assert.True(gizmo.Build(
			EditorTool.Translate,
			Vector3.Zero,
			Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f),
			local: true,
			scale: 1f,
			viewProjection,
			Bounds,
			UnitsPerPixel));

		GizmoHandleHit xAxis = gizmo.Handles.First(
			handle => handle.Handle == GizmoHandle.AxisX);

		// A quarter turn about +Y sends the object's own X axis to -Z.
		Assert.True(Vector3.Dot(
			Vector3.Normalize(xAxis.End - xAxis.Origin),
			-Vector3.UnitZ) > 0.999f);
	}

	[Fact]
	public void GizmoStaysClearOfALargeObject_AndHidesForTheSelectTool()
	{
		(EditorCamera camera, Matrix4x4 viewProjection) = CreateFramedView();

		ViewportGizmo gizmo = new();

		Assert.True(gizmo.Build(
			EditorTool.Translate,
			Vector3.Zero,
			Quaternion.Identity,
			local: false,
			scale: 10f,
			viewProjection,
			Bounds,
			UnitsPerPixel));

		Assert.True(gizmo.WorldLength >= 9f);

		Assert.False(gizmo.Build(
			EditorTool.Select,
			Vector3.Zero,
			Quaternion.Identity,
			local: false,
			scale: 1f,
			viewProjection,
			Bounds,
			UnitsPerPixel));

		Assert.False(gizmo.IsVisible);
		Assert.Equal(
			GizmoHandle.None,
			gizmo.Pick(Bounds.Center, ViewportMath.GizmoPickTolerancePixels).Handle);
	}

	[Fact]
	public void RotateGizmo_UsesTheAxisHandlesWithoutAPlane()
	{
		(EditorCamera camera, Matrix4x4 viewProjection) = CreateFramedView();

		ViewportGizmo gizmo = new();

		Assert.True(gizmo.Build(
			EditorTool.Rotate,
			Vector3.Zero,
			Quaternion.Identity,
			local: false,
			scale: 1f,
			viewProjection,
			Bounds,
			UnitsPerPixel));

		Assert.Equal(3, gizmo.Handles.Count);
		Assert.DoesNotContain(
			gizmo.Handles,
			handle => handle.Handle == GizmoHandle.PlaneXZ);
	}

	[Fact]
	public void PickingBox_UsesTheLargestScaleOnEveryAxis()
	{
		// A stretched object must stay pickable everywhere: the box
		// bounds the primitive instead of shrinking below it.
		Assert.Equal(
			new Vector3(1.5f),
			ViewportGizmo.PickingHalfExtent(
				new Vector3(0.5f),
				new Vector3(3f, 1f, 1f)));
	}

	private static UiPoint Midpoint(UiPoint start, UiPoint end)
	{
		return Lerp(start, end, 0.5f);
	}

	private static UiPoint Lerp(UiPoint start, UiPoint end, float t)
	{
		return new UiPoint(
			start.X + ((end.X - start.X) * t),
			start.Y + ((end.Y - start.Y) * t));
	}

	private static float Distance(UiPoint start, UiPoint end)
	{
		float dx = end.X - start.X;
		float dy = end.Y - start.Y;

		return MathF.Sqrt((dx * dx) + (dy * dy));
	}

	private static (EditorCamera Camera, Matrix4x4 ViewProjection)
		CreateFramedView()
	{
		EditorCamera camera = new();

		// Ten units in front of the origin, looking down -Z, which
		// puts the origin exactly in the middle of the viewport.
		camera.SetPose(new Vector3(0f, 0f, FramingDistance), 0f, 0f);

		Matrix4x4 viewProjection =
			camera.CreateView() *
			camera.CreateProjection(
				Bounds.Width / Bounds.Height,
				yDown: false);

		return (camera, viewProjection);
	}
}
