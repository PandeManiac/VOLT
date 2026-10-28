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

	/// <summary>The XZ plane handle, used by translation.</summary>
	PlaneXZ
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
/// Whether both ends projected in front of the camera.
/// </param>
/// <param name="DistancePixels">
/// The pointer distance to this handle, filled in by picking.
/// </param>
internal record struct GizmoHandleHit(
	GizmoHandle Handle,
	Vector3 Origin,
	Vector3 End,
	UiPoint StartPixel,
	UiPoint EndPixel,
	bool Visible,
	float DistancePixels);

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
	/// Side length of the plane handle in pixels.
	/// </summary>
	public const float PlaneSizePixels = 34f;

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

		Quaternion orientation = local
			? rotation
			: Quaternion.Identity;

		if (type == EditorTool.Rotate)
		{
			// Rotation reuses the axis handles: dragging one rotates
			// about that axis rather than moving along it, so the same
			// pick geometry serves both tools.
			AddAxis(center, bounds, viewProjection, orientation, Vector3.UnitX, GizmoHandle.AxisX, origin);
			AddAxis(center, bounds, viewProjection, orientation, Vector3.UnitY, GizmoHandle.AxisY, origin);
			AddAxis(center, bounds, viewProjection, orientation, Vector3.UnitZ, GizmoHandle.AxisZ, origin);

			IsVisible = _handles.Count > 0;

			return IsVisible;
		}

		AddAxis(center, bounds, viewProjection, orientation, Vector3.UnitX, GizmoHandle.AxisX, origin);
		AddAxis(center, bounds, viewProjection, orientation, Vector3.UnitY, GizmoHandle.AxisY, origin);
		AddAxis(center, bounds, viewProjection, orientation, Vector3.UnitZ, GizmoHandle.AxisZ, origin);

		if (type == EditorTool.Translate)
		{
			AddPlane(center, bounds, viewProjection, orientation, origin);
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
	public GizmoHandleHit Pick(UiPoint pointer, float tolerancePixels)
	{
		GizmoHandleHit best = default;
		float bestDistance = float.MaxValue;

		// Planes are tested first so their inner area wins over the
		// axes that border it, then axes are compared by distance.
		for (int pass = 0; pass < 2; pass++)
		{
			bool planePass = pass == 0;

			foreach (GizmoHandleHit handle in _handles)
			{
				if (!handle.Visible ||
					(handle.Handle == GizmoHandle.PlaneXZ) != planePass)
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
				}
			}

			if (bestDistance < float.MaxValue && planePass)
			{
				// A plane hit is unambiguous; stop before the axes.
				break;
			}
		}

		return best;
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
		Vector3 origin)
	{
		Vector3 x = Vector3.Transform(Vector3.UnitX, orientation);
		Vector3 z = Vector3.Transform(Vector3.UnitZ, orientation);

		float size = PlaneSizePixels / AxisLengthPixels * WorldLength;

		// The plane handle is the square between the X and Z axes,
		// inset so it never overlaps the axis lines the axes own.
		Vector3 start = origin + ((x + z) * (size * 0.35f));
		Vector3 end = origin + ((x + z) * (size * 1.15f));

		bool visible = ViewportMath.TryProject(
			viewProjection,
			end,
			bounds,
			out UiPoint endPixel);

		_handles.Add(new GizmoHandleHit(
			GizmoHandle.PlaneXZ,
			start,
			end,
			center,
			visible ? endPixel : center,
			visible,
			0f));
	}
}
