using System.Numerics;
using Volt.Ui;

namespace Volt.Editor.Viewport;

/// <summary>
/// Identifies one interactive part of a transform gizmo.
/// </summary>
internal enum GizmoHandle
{
	/// <summary>No handle is under the pointer.</summary>
	None,

	/// <summary>The X axis handle.</summary>
	AxisX,

	/// <summary>The Y axis handle.</summary>
	AxisY,

	/// <summary>The Z axis handle.</summary>
	AxisZ,

	/// <summary>The XY plane handle, which moves along X and Y.</summary>
	PlaneXY,

	/// <summary>The YZ plane handle, which moves along Y and Z.</summary>
	PlaneYZ,

	/// <summary>The XZ plane handle, which moves along X and Z.</summary>
	PlaneXZ
}

/// <summary>
/// The four projected corners of a plane handle, in order around its
/// edge.
/// </summary>
/// <param name="A">The corner nearest the gizmo origin on both axes.</param>
/// <param name="B">The corner at the first axis's far end.</param>
/// <param name="C">The far corner, diagonal from <paramref name="A"/>.</param>
/// <param name="D">The corner at the second axis's far end.</param>
internal readonly record struct GizmoPlaneQuad(
	UiPoint A,
	UiPoint B,
	UiPoint C,
	UiPoint D)
{
	/// <summary>
	/// Gets the quad's centre in viewport pixels.
	/// </summary>
	public UiPoint Center => new(
		(A.X + C.X) * 0.5f,
		(A.Y + C.Y) * 0.5f);

	/// <summary>
	/// Gets whether a point lies inside the quad.
	/// </summary>
	/// <remarks>
	/// A parallelogram projected from a plane stays convex while all
	/// four corners are in front of the camera, which the gizmo already
	/// requires before it makes a quad pickable, so a same-side-of-every
	/// -edge test is exact here.
	/// </remarks>
	public bool Contains(UiPoint point)
	{
		float a = Cross(A, B, point);
		float b = Cross(B, C, point);
		float c = Cross(C, D, point);
		float d = Cross(D, A, point);

		bool positive = a >= 0f && b >= 0f && c >= 0f && d >= 0f;
		bool negative = a <= 0f && b <= 0f && c <= 0f && d <= 0f;

		return positive || negative;
	}

	/// <summary>
	/// Gets the distance from a point to the quad, zero when inside.
	/// </summary>
	public float DistanceTo(UiPoint point)
	{
		if (Contains(point))
		{
			return 0f;
		}

		return MathF.Min(
			MathF.Min(
				ViewportMath.DistanceToSegment(point, A, B),
				ViewportMath.DistanceToSegment(point, B, C)),
			MathF.Min(
				ViewportMath.DistanceToSegment(point, C, D),
				ViewportMath.DistanceToSegment(point, D, A)));
	}

	private static float Cross(UiPoint start, UiPoint end, UiPoint point)
	{
		return ((end.X - start.X) * (point.Y - start.Y)) -
			((end.Y - start.Y) * (point.X - start.X));
	}
}

/// <summary>
/// One gizmo handle in world space, paired with the pixels it covers.
/// </summary>
/// <param name="Handle">Which part of the gizmo this is.</param>
/// <param name="Origin">The handle's start in world space.</param>
/// <param name="End">The handle's end in world space.</param>
/// <param name="StartPixel">The start in viewport pixels.</param>
/// <param name="EndPixel">The end in viewport pixels.</param>
/// <param name="Visible">
/// Whether the drawing the pick test needs projected in front of the
/// camera.
/// </param>
/// <param name="DistancePixels">
/// The pointer distance to this handle, filled in by picking.
/// </param>
/// <param name="Quad">
/// The projected corners of a plane handle, or the default value for
/// an axis handle.
/// </param>
internal record struct GizmoHandleHit(
	GizmoHandle Handle,
	Vector3 Origin,
	Vector3 End,
	UiPoint StartPixel,
	UiPoint EndPixel,
	bool Visible,
	float DistancePixels,
	GizmoPlaneQuad Quad = default)
{
	/// <summary>
	/// Gets whether this handle is a plane rather than an axis.
	/// </summary>
	public bool IsPlane => ViewportGizmo.IsPlaneHandle(Handle);
}

/// <summary>
/// The screen-space geometry of one transform gizmo.
/// </summary>
/// <remarks>
/// The gizmo is editor tooling: it is never an entity, it is never
/// serialized, and it only exists while a tool is active and something
/// is selected. It is built from pure math so its geometry, picking,
/// and drag behaviour can be tested without a window.
/// </remarks>
internal sealed class ViewportGizmo
{
	/// <summary>
	/// Length of one axis handle in pixels.
	/// </summary>
	public const float AxisLengthPixels = 90f;

	/// <summary>
	/// Side length of a plane handle in pixels.
	/// </summary>
	public const float PlaneSizePixels = 34f;

	/// <summary>
	/// Smallest projected area, in square pixels, that still counts as
	/// a plane handle.
	/// </summary>
	/// <remarks>
	/// A plane seen edge-on projects to a line. It has no area to grab,
	/// and treating that line as a pickable quad would let it steal the
	/// axis handles it runs along.
	/// </remarks>
	public const float MinimumPlaneAreaPixels = 8f;

	private readonly List<GizmoHandleHit> _handles = [];

	/// <summary>
	/// Gets the gizmo origin in world space.
	/// </summary>
	public Vector3 Origin { get; private set; }

	/// <summary>
	/// Gets the world-space length of one axis handle.
	/// </summary>
	public float WorldLength { get; private set; }

	/// <summary>
	/// Gets whether the gizmo has a valid pose to draw.
	/// </summary>
	public bool IsVisible { get; private set; }

	/// <summary>
	/// Gets the handles built by the last <see cref="Build"/> call.
	/// </summary>
	public IReadOnlyList<GizmoHandleHit> Handles => _handles;

	/// <summary>
	/// Gets whether a handle is one of the plane handles.
	/// </summary>
	public static bool IsPlaneHandle(GizmoHandle handle)
	{
		return handle is GizmoHandle.PlaneXY or
			GizmoHandle.PlaneYZ or
			GizmoHandle.PlaneXZ;
	}

	/// <summary>
	/// Gets whether a handle is one of the axis handles.
	/// </summary>
	public static bool IsAxisHandle(GizmoHandle handle)
	{
		return handle is GizmoHandle.AxisX or
			GizmoHandle.AxisY or
			GizmoHandle.AxisZ;
	}

	/// <summary>
	/// Gets the local unit axis a handle names.
	/// </summary>
	/// <remarks>
	/// A plane handle reports its first axis; callers that need both
	/// use <see cref="PlaneAxes"/>.
	/// </remarks>
	public static Vector3 LocalAxis(GizmoHandle handle)
	{
		return handle switch
		{
			GizmoHandle.AxisY => Vector3.UnitY,
			GizmoHandle.AxisZ => Vector3.UnitZ,
			GizmoHandle.PlaneYZ => Vector3.UnitY,
			GizmoHandle.PlaneXZ => Vector3.UnitX,
			_ => Vector3.UnitX
		};
	}

	/// <summary>
	/// Gets the two local unit axes a plane handle spans.
	/// </summary>
	public static (Vector3 First, Vector3 Second) PlaneAxes(
		GizmoHandle handle)
	{
		return handle switch
		{
			GizmoHandle.PlaneXY => (Vector3.UnitX, Vector3.UnitY),
			GizmoHandle.PlaneYZ => (Vector3.UnitY, Vector3.UnitZ),
			_ => (Vector3.UnitX, Vector3.UnitZ)
		};
	}

	/// <summary>
	/// Builds the gizmo for one selected object.
	/// </summary>
	/// <param name="type">The gizmo type to build.</param>
	/// <param name="origin">The object's world-space origin.</param>
	/// <param name="rotation">
	/// The object's world rotation, used by local mode.
	/// </param>
	/// <param name="local">
	/// True to orient the handles along the object's own axes.
	/// </param>
	/// <param name="scale">
	/// The object's world scale magnitude, which keeps the handles
	/// clear of a large object.
	/// </param>
	/// <param name="viewProjection">
	/// The overlay view-projection, built with <c>yDown: false</c>.
	/// </param>
	/// <param name="bounds">The viewport rectangle.</param>
	/// <param name="worldPerPixel">
	/// World units per vertical pixel at the gizmo's distance.
	/// </param>
	/// <returns>False when the gizmo cannot be built.</returns>
	public bool Build(
		EditorTool type,
		Vector3 origin,
		Quaternion rotation,
		bool local,
		float scale,
		in Matrix4x4 viewProjection,
		UiRect bounds,
		float worldPerPixel)
	{
		_handles.Clear();
		IsVisible = false;

		if (type == EditorTool.Select ||
			worldPerPixel <= 0f ||
			!float.IsFinite(worldPerPixel))
		{
			return false;
		}

		Origin = origin;

		// Screen-constant handles, held clear of a scaled object.
		float screenLength = AxisLengthPixels * worldPerPixel;
		WorldLength = MathF.Max(screenLength, scale * 0.9f);

		if (!ViewportMath.TryProject(
				viewProjection,
				origin,
				bounds,
				out UiPoint center))
		{
			return false;
		}

		// Scale always reads along the object's own axes: scaling a
		// rotated object along a world axis would shear its local
		// components, which the authored transform cannot express.
		Quaternion orientation = local || type == EditorTool.Scale
			? rotation
			: Quaternion.Identity;

		if (type == EditorTool.Rotate)
		{
			// Rotation rings and picking share the same projected
			// geometry; every segment is tagged with its rotation axis.
			AddRotationRing(bounds, viewProjection, orientation, origin, Vector3.UnitX, GizmoHandle.AxisX);
			AddRotationRing(bounds, viewProjection, orientation, origin, Vector3.UnitY, GizmoHandle.AxisY);
			AddRotationRing(bounds, viewProjection, orientation, origin, Vector3.UnitZ, GizmoHandle.AxisZ);

			IsVisible = _handles.Count > 0;

			return IsVisible;
		}

		AddAxis(center, bounds, viewProjection, orientation, Vector3.UnitX, GizmoHandle.AxisX, origin);
		AddAxis(center, bounds, viewProjection, orientation, Vector3.UnitY, GizmoHandle.AxisY, origin);
		AddAxis(center, bounds, viewProjection, orientation, Vector3.UnitZ, GizmoHandle.AxisZ, origin);

		if (type == EditorTool.Translate)
		{
			AddPlane(center, bounds, viewProjection, orientation, origin, GizmoHandle.PlaneXY, Vector3.UnitX, Vector3.UnitY);
			AddPlane(center, bounds, viewProjection, orientation, origin, GizmoHandle.PlaneYZ, Vector3.UnitY, Vector3.UnitZ);
			AddPlane(center, bounds, viewProjection, orientation, origin, GizmoHandle.PlaneXZ, Vector3.UnitX, Vector3.UnitZ);
		}

		IsVisible = _handles.Count > 0;

		return IsVisible;
	}

	/// <summary>
	/// Finds the handle under a pointer, preferring the closest one.
	/// </summary>
	/// <param name="pointer">The pointer position in UI units.</param>
	/// <param name="tolerancePixels">
	/// How far from a handle the pointer may be.
	/// </param>
	/// <returns>
	/// The hit handle, or a hit whose
	/// <see cref="GizmoHandleHit.Handle"/> is
	/// <see cref="GizmoHandle.None"/> when nothing is close enough.
	/// </returns>
	/// <remarks>
	/// Planes win over axes wherever their area covers the pointer,
	/// which is what keeps the inner square from being stolen by the
	/// axis lines that border it. Between overlapping planes the one
	/// whose centre is nearest wins, so three quads that share the
	/// origin still have one predictable answer.
	/// </remarks>
	public GizmoHandleHit Pick(UiPoint pointer, float tolerancePixels)
	{
		GizmoHandleHit best = default;
		float bestDistance = float.MaxValue;
		float bestCentre = float.MaxValue;
		bool found = false;

		foreach (GizmoHandleHit handle in _handles)
		{
			if (!handle.Visible || !handle.IsPlane)
			{
				continue;
			}

			float distance = handle.Quad.DistanceTo(pointer);

			if (distance > tolerancePixels)
			{
				continue;
			}

			float centre = ViewportMath.DistanceToSegment(
				pointer,
				handle.Quad.Center,
				handle.Quad.Center);

			bool closer = !found || distance < bestDistance - 0.5f;
			bool tied = found &&
				MathF.Abs(distance - bestDistance) <= 0.5f &&
				centre < bestCentre;

			if (closer || tied)
			{
				bestDistance = distance;
				bestCentre = centre;
				best = handle with { DistancePixels = distance };
				found = true;
			}
		}

		foreach (GizmoHandleHit handle in _handles)
		{
			if (!handle.Visible || handle.IsPlane)
			{
				continue;
			}

			float distance = ViewportMath.DistanceToSegment(
				pointer,
				handle.StartPixel,
				handle.EndPixel);

			if (distance <= tolerancePixels && distance < bestDistance)
			{
				bestDistance = distance;
				best = handle with { DistancePixels = distance };
				found = true;
			}
		}

		return found ? best : default;
	}

	/// <summary>
	/// Computes the size a primitive's picking box should use.
	/// </summary>
	/// <param name="halfExtent">The primitive's local half extent.</param>
	/// <param name="scale">The entity's world scale.</param>
	/// <remarks>
	/// The largest scale component is used on every axis, so the box
	/// bounds the primitive rather than shrinking below it on a
	/// non-uniform scale.
	/// </remarks>
	public static Vector3 PickingHalfExtent(
		Vector3 halfExtent,
		Vector3 scale)
	{
		float magnitude = MathF.Max(
			MathF.Abs(scale.X),
			MathF.Max(MathF.Abs(scale.Y), MathF.Abs(scale.Z)));

		return halfExtent * magnitude;
	}

	private void AddRotationRing(
		UiRect bounds,
		in Matrix4x4 viewProjection,
		Quaternion orientation,
		Vector3 origin,
		Vector3 normal,
		GizmoHandle handle)
	{
		const int Segments = 48;
		Vector3 first = normal == Vector3.UnitX
			? Vector3.UnitY
			: Vector3.UnitX;
		Vector3 second = Vector3.Normalize(Vector3.Cross(normal, first));
		first = Vector3.Transform(first, orientation);
		second = Vector3.Transform(second, orientation);

		float radius = WorldLength * 0.78f;
		bool hasPrevious = false;
		Vector3 previousWorld = default;
		UiPoint previousPixel = default;

		for (int segment = 0; segment <= Segments; segment++)
		{
			float angle = segment * (MathF.Tau / Segments);
			Vector3 point = origin + radius *
				((first * MathF.Cos(angle)) + (second * MathF.Sin(angle)));
			bool visible = ViewportMath.TryProject(
				viewProjection, point, bounds, out UiPoint pixel);

			if (visible && hasPrevious)
			{
				_handles.Add(new GizmoHandleHit(
					handle,
					previousWorld,
					point,
					previousPixel,
					pixel,
					true,
					0f));
			}

			hasPrevious = visible;
			previousWorld = point;
			previousPixel = pixel;
		}
	}

	private void AddAxis(
		UiPoint center,
		UiRect bounds,
		in Matrix4x4 viewProjection,
		Quaternion orientation,
		Vector3 axis,
		GizmoHandle handle,
		Vector3 origin)
	{
		Vector3 direction = Vector3.Transform(axis, orientation);
		Vector3 end = origin + (direction * WorldLength);

		bool visible = ViewportMath.TryProject(
			viewProjection,
			end,
			bounds,
			out UiPoint endPixel);

		_handles.Add(new GizmoHandleHit(
			handle,
			origin,
			end,
			center,
			visible ? endPixel : center,
			visible,
			0f));
	}

	private void AddPlane(
		UiPoint center,
		UiRect bounds,
		in Matrix4x4 viewProjection,
		Quaternion orientation,
		Vector3 origin,
		GizmoHandle handle,
		Vector3 firstAxis,
		Vector3 secondAxis)
	{
		Vector3 first = Vector3.Transform(firstAxis, orientation);
		Vector3 second = Vector3.Transform(secondAxis, orientation);

		float size = PlaneSizePixels / AxisLengthPixels * WorldLength;

		// The quad is inset from the origin and stops before the axis
		// tips, so it reads as the square between the two axes rather
		// than as another handle on top of them.
		Vector3 near = (first + second) * (size * 0.35f);
		Vector3 far = (first + second) * (size * 1.15f);

		Vector3 cornerA = origin + near;
		Vector3 cornerB = origin + (first * (size * 1.15f)) + (second * (size * 0.35f));
		Vector3 cornerC = origin + far;
		Vector3 cornerD = origin + (first * (size * 0.35f)) + (second * (size * 1.15f));

		UiPoint pixelA = center;
		UiPoint pixelB = center;
		UiPoint pixelC = center;
		UiPoint pixelD = center;

		bool visible =
			ViewportMath.TryProject(viewProjection, cornerA, bounds, out pixelA) &&
			ViewportMath.TryProject(viewProjection, cornerB, bounds, out pixelB) &&
			ViewportMath.TryProject(viewProjection, cornerC, bounds, out pixelC) &&
			ViewportMath.TryProject(viewProjection, cornerD, bounds, out pixelD);

		if (!visible ||
			MathF.Abs(QuadArea(pixelA, pixelB, pixelC, pixelD)) <
				MinimumPlaneAreaPixels)
		{
			// A quad with a corner behind the camera, or one seen
			// edge-on, has no reliable screen shape, so it is neither
			// drawn nor pickable.
			_handles.Add(new GizmoHandleHit(
				handle,
				cornerA,
				cornerC,
				center,
				center,
				false,
				0f));

			return;
		}

		_handles.Add(new GizmoHandleHit(
			handle,
			cornerA,
			cornerC,
			pixelA,
			pixelC,
			true,
			0f,
			new GizmoPlaneQuad(pixelA, pixelB, pixelC, pixelD)));
	}

	/// <summary>
	/// Gets the signed area of a projected quad, in square pixels.
	/// </summary>
	private static float QuadArea(
		UiPoint a,
		UiPoint b,
		UiPoint c,
		UiPoint d)
	{
		return 0.5f * (
			((a.X * b.Y) - (b.X * a.Y)) +
			((b.X * c.Y) - (c.X * b.Y)) +
			((c.X * d.Y) - (d.X * c.Y)) +
			((d.X * a.Y) - (a.X * d.Y)));
	}
}
