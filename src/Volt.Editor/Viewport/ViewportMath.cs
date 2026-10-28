using System.Numerics;
using Volt.Editor.Core.Scenes;
using Volt.Editor.Rendering;
using Volt.Ui;
using Volt.Ui.Rendering;

namespace Volt.Editor.Viewport;

/// <summary>
/// Projection, picking, and authoring math shared by the viewports.
/// </summary>
/// <remarks>
/// Everything here is pure math over value types, so viewport
/// behaviour can be tested without a window, a GPU, or a widget tree.
/// Matrices follow the engine convention: right-handed, +Y up, -Z
/// forward, with row-vector composition
/// (<c>scale * rotation * translation</c>).
/// </remarks>
internal static class ViewportMath
{
	/// <summary>
	/// Width of one gizmo handle in pixels, measured from the object's
	/// origin. It is deliberately generous: the handles are drawn 1-2
	/// pixels wide, so the pick test has to be more forgiving than the
	/// drawing.
	/// </summary>
	public const float GizmoPickTolerancePixels = 8f;

	/// <summary>
	/// Projects a world point into viewport pixels.
	/// </summary>
	/// <param name="viewProjection">
	/// A view-projection built with <c>yDown: false</c>, which is what
	/// this mapping assumes.
	/// </param>
	/// <param name="world">The world-space point.</param>
	/// <param name="bounds">The viewport rectangle in UI units.</param>
	/// <param name="screen">Receives the point in UI units.</param>
	/// <returns>
	/// False when the point is behind the camera or far outside the
	/// viewport.
	/// </returns>
	public static bool TryProject(
		in Matrix4x4 viewProjection,
		Vector3 world,
		UiRect bounds,
		out UiPoint screen)
	{
		Vector4 clip = Vector4.Transform(
			new Vector4(world, 1f),
			viewProjection);

		if (clip.W <= 0.0001f || !float.IsFinite(clip.W))
		{
			screen = default;
			return false;
		}

		float x = clip.X / clip.W;
		float y = clip.Y / clip.W;

		if (x < -1.5f || x > 1.5f || y < -1.5f || y > 1.5f)
		{
			screen = default;
			return false;
		}

		screen = new UiPoint(
			bounds.X + ((x * 0.5f) + 0.5f) * bounds.Width,
			bounds.Y + ((1f - ((y * 0.5f) + 0.5f)) * bounds.Height));

		return true;
	}

	/// <summary>
	/// Builds a world-space ray through a viewport pixel.
	/// </summary>
	/// <param name="viewProjection">
	/// A view-projection built with <c>yDown: false</c>.
	/// </param>
	/// <param name="bounds">The viewport rectangle in UI units.</param>
	/// <param name="point">The pointer position in UI units.</param>
	/// <param name="origin">Receives the ray origin on the near plane.</param>
	/// <param name="direction">Receives the unit ray direction.</param>
	/// <returns>False when the matrices are not invertible.</returns>
	public static bool TryCreateRay(
		in Matrix4x4 viewProjection,
		UiRect bounds,
		UiPoint point,
		out Vector3 origin,
		out Vector3 direction)
	{
		origin = default;
		direction = default;

		if (bounds.Width <= 0f || bounds.Height <= 0f)
		{
			return false;
		}

		if (!Matrix4x4.Invert(viewProjection, out Matrix4x4 inverse))
		{
			return false;
		}

		float ndcX = (((point.X - bounds.X) / bounds.Width) * 2f) - 1f;
		float ndcY = 1f - (((point.Y - bounds.Y) / bounds.Height) * 2f);

		Vector4 near = Vector4.Transform(
			new Vector4(ndcX, ndcY, 0f, 1f),
			inverse);

		Vector4 far = Vector4.Transform(
			new Vector4(ndcX, ndcY, 1f, 1f),
			inverse);

		if (near.W == 0f || far.W == 0f ||
			!float.IsFinite(near.W) || !float.IsFinite(far.W))
		{
			return false;
		}

		Vector3 nearPoint = new(near.X, near.Y, near.Z);
		Vector3 farPoint = new(far.X, far.Y, far.Z);

		nearPoint /= near.W;
		farPoint /= far.W;

		Vector3 delta = farPoint - nearPoint;

		if (delta.LengthSquared() < 1e-12f)
		{
			return false;
		}

		origin = nearPoint;
		direction = Vector3.Normalize(delta);

		return true;
	}

	/// <summary>
	/// Tests a ray against an oriented box and reports the nearest
	/// front hit.
	/// </summary>
	/// <param name="world">The box's world transform.</param>
	/// <param name="center">The box centre in local space.</param>
	/// <param name="halfExtent">The box half extent in local space.</param>
	/// <param name="rayOrigin">The ray origin in world space.</param>
	/// <param name="rayDirection">The unit ray direction.</param>
	/// <param name="distance">
	/// Receives the distance from the ray origin to the hit point.
	/// </param>
	/// <returns>False when the ray misses the box.</returns>
	/// <remarks>
	/// The ray is transformed into the box's local space, which makes
	/// the test exact for any rotation and any non-degenerate scale,
	/// and the hit point is transformed back so the reported distance
	/// is measured in world units and can be compared between objects.
	/// </remarks>
	public static bool TryIntersectBox(
		in Matrix4x4 world,
		Vector3 center,
		Vector3 halfExtent,
		Vector3 rayOrigin,
		Vector3 rayDirection,
		out float distance)
	{
		distance = 0f;

		if (!Matrix4x4.Invert(world, out Matrix4x4 inverse))
		{
			return false;
		}

		Vector3 localOrigin = Vector3.Transform(rayOrigin, inverse);
		Vector3 localDirection = Vector3.TransformNormal(rayDirection, inverse);

		if (localDirection.LengthSquared() < 1e-12f)
		{
			return false;
		}

		Vector3 minimum = center - halfExtent;
		Vector3 maximum = center + halfExtent;

		float near = float.NegativeInfinity;
		float far = float.PositiveInfinity;

		for (int axis = 0; axis < 3; axis++)
		{
			float origin = Component(localOrigin, axis);
			float direction = Component(localDirection, axis);
			float low = Component(minimum, axis);
			float high = Component(maximum, axis);

			if (MathF.Abs(direction) < 1e-9f)
			{
				// The ray stays in this slab's span; outside it there
				// is no intersection at all.
				if (origin < low || origin > high)
				{
					return false;
				}

				continue;
			}

			float inverseDirection = 1f / direction;

			float first = (low - origin) * inverseDirection;
			float second = (high - origin) * inverseDirection;

			if (first > second)
			{
				(first, second) = (second, first);
			}

			near = MathF.Max(near, first);
			far = MathF.Min(far, second);

			if (near > far)
			{
				return false;
			}
		}

		// A hit behind the ray origin is not visible; a ray that
		// starts inside the box reports the exit as its first surface.
		float localDistance = near >= 0f ? near : far;

		if (localDistance < 0f)
		{
			return false;
		}

		Vector3 localHit = localOrigin + (localDirection * localDistance);
		Vector3 worldHit = Vector3.Transform(localHit, world);

		distance = Vector3.Distance(rayOrigin, worldHit);

		return true;
	}

	/// <summary>
	/// Projects a pointer onto an axis line in world space.
	/// </summary>
	/// <param name="rayOrigin">The ray origin in world space.</param>
	/// <param name="rayDirection">The unit ray direction.</param>
	/// <param name="cameraPosition">
	/// The camera position, used to choose a plane that faces it.
	/// </param>
	/// <param name="axisOrigin">A point on the axis.</param>
	/// <param name="axisDirection">The unit axis direction.</param>
	/// <param name="distance">
	/// Receives the signed distance along the axis from
	/// <paramref name="axisOrigin"/> to the projected point.
	/// </param>
	/// <returns>
	/// False when the axis is nearly parallel to the view direction, in
	/// which case the projection would be unstable.
	/// </returns>
	/// <remarks>
	/// This is the standard constrained-drag solve: the pointer ray is
	/// intersected with the plane that contains the axis and faces the
	/// camera as squarely as possible, and the hit is projected onto the
	/// axis. Dragging therefore follows the pointer exactly, and an axis
	/// pointing almost straight at the camera is rejected instead of
	/// producing a wild jump.
	/// </remarks>
	public static bool TryProjectOntoAxis(
		Vector3 rayOrigin,
		Vector3 rayDirection,
		Vector3 cameraPosition,
		Vector3 axisOrigin,
		Vector3 axisDirection,
		out float distance)
	{
		distance = 0f;

		if (axisDirection.LengthSquared() < 1e-9f)
		{
			return false;
		}

		Vector3 axis = Vector3.Normalize(axisDirection);
		Vector3 toCamera = cameraPosition - axisOrigin;
		Vector3 planar = toCamera - (axis * Vector3.Dot(toCamera, axis));

		if (planar.LengthSquared() < 1e-6f)
		{
			return false;
		}

		Vector3 normal = Vector3.Normalize(planar);

		float denominator = Vector3.Dot(rayDirection, normal);

		if (MathF.Abs(denominator) < 1e-4f)
		{
			return false;
		}

		float t = Vector3.Dot(axisOrigin - rayOrigin, normal) / denominator;

		if (!float.IsFinite(t))
		{
			return false;
		}

		Vector3 hit = rayOrigin + (rayDirection * t);

		distance = Vector3.Dot(hit - axisOrigin, axis);

		return float.IsFinite(distance);
	}

	/// <summary>
	/// Intersects a ray with an infinite plane.
	/// </summary>
	/// <param name="rayOrigin">The ray origin in world space.</param>
	/// <param name="rayDirection">The unit ray direction.</param>
	/// <param name="planeOrigin">A point on the plane.</param>
	/// <param name="planeNormal">The plane normal; need not be unit.</param>
	/// <param name="point">Receives the hit point in world space.</param>
	/// <returns>
	/// False when the ray is nearly parallel to the plane or the hit is
	/// not finite.
	/// </returns>
	/// <remarks>
	/// Plane drags use a real ray-plane intersection rather than two
	/// axis projections: projecting each axis separately makes a
	/// pointer that moves across the plane slide at a different rate
	/// on each axis, which is what makes plane dragging feel detached.
	/// A view that grazes the plane is refused, because the
	/// intersection then runs off to infinity for a one-pixel move.
	/// </remarks>
	public static bool TryIntersectPlane(
		Vector3 rayOrigin,
		Vector3 rayDirection,
		Vector3 planeOrigin,
		Vector3 planeNormal,
		out Vector3 point)
	{
		point = default;

		if (planeNormal.LengthSquared() < 1e-9f)
		{
			return false;
		}

		Vector3 normal = Vector3.Normalize(planeNormal);
		float alignment = Vector3.Dot(rayDirection, normal);

		if (MathF.Abs(alignment) < MinimumPlaneAlignment)
		{
			return false;
		}

		float distance = Vector3.Dot(planeOrigin - rayOrigin, normal) /
			alignment;

		if (!float.IsFinite(distance))
		{
			return false;
		}

		point = rayOrigin + (rayDirection * distance);

		return float.IsFinite(point.X) &&
			float.IsFinite(point.Y) &&
			float.IsFinite(point.Z);
	}

	/// <summary>
	/// Converts a pure rotation matrix into the Euler angles the
	/// authored transform stores.
	/// </summary>
	/// <param name="rotation">The rotation matrix, with no scale.</param>
	/// <returns>
	/// Pitch, yaw, and roll in degrees, in that order, which is the
	/// order <see cref="TransformData.EulerAngles"/> uses.
	/// </returns>
	/// <remarks>
	/// Rotation is authored as a quaternion and shown as Euler angles,
	/// so a gizmo drag has to come back to Euler exactly once, at the
	/// end of its own solve. Doing it through the forward axis and one
	/// residual roll keeps the mapping consistent with
	/// <c>Quaternion.CreateFromYawPitchRoll</c> without a second set of
	/// trigonometric branches that could disagree with it.
	/// </remarks>
	public static Vector3 EulerDegrees(Matrix4x4 rotation)
	{
		// Reading the axes the rotation produces avoids depending on a
		// hand-expanded matrix formula that can silently disagree with
		// CreateFromYawPitchRoll. For the authored order the local axes
		// transform as:
		//   +Z -> ( sin(yaw) cos(pitch), -sin(pitch), cos(yaw) cos(pitch) )
		//   +X -> ( cos(yaw) cos(roll) - ..., sin(roll) cos(pitch), .. )
		//   +Y -> ( ..., cos(roll) cos(pitch), ... )
		Vector3 zAxis = Vector3.TransformNormal(Vector3.UnitZ, rotation);
		Vector3 xAxis = Vector3.TransformNormal(Vector3.UnitX, rotation);
		Vector3 yAxis = Vector3.TransformNormal(Vector3.UnitY, rotation);

		float pitch = -MathF.Asin(Math.Clamp(zAxis.Y, -1f, 1f));

		float yaw;
		float roll;

		if (MathF.Abs(zAxis.Y) > 0.99999f)
		{
			// Looking straight up or down, yaw and roll describe the
			// same turn. Keeping roll at zero and folding it into yaw
			// reproduces the orientation without a discontinuity.
			roll = 0f;
			yaw = MathF.Atan2(xAxis.Z, xAxis.X);
		}

		else
		{
			yaw = MathF.Atan2(zAxis.X, zAxis.Z);
			roll = MathF.Atan2(xAxis.Y, yAxis.Y);
		}

		const float RadiansToDegrees = 180f / MathF.PI;

		return new Vector3(
			pitch * RadiansToDegrees,
			yaw * RadiansToDegrees,
			roll * RadiansToDegrees);
	}

	/// <summary>
	/// The smallest cosine between a pointer ray and a manipulation
	/// plane that still counts as a usable intersection.
	/// </summary>
	/// <remarks>
	/// At 0.15 the ray meets the plane at about 8.6 degrees or more,
	/// which bounds how far a single pixel can move the hit point. A
	/// flatter view refuses the drag instead of letting the object fly
	/// off when the pointer crosses the horizon.
	/// </remarks>
	public const float MinimumPlaneAlignment = 0.15f;

	/// <summary>
	/// Measures the distance from a point to a line segment in screen
	/// space.
	/// </summary>
	public static float DistanceToSegment(
		UiPoint point,
		UiPoint start,
		UiPoint end)
	{
		float dx = end.X - start.X;
		float dy = end.Y - start.Y;

		float lengthSquared = (dx * dx) + (dy * dy);

		if (lengthSquared < 1e-6f)
		{
			return Distance(point, start);
		}

		float t = (
			((point.X - start.X) * dx) +
			((point.Y - start.Y) * dy)) / lengthSquared;

		t = Math.Clamp(t, 0f, 1f);

		UiPoint closest = new(
			start.X + (dx * t),
			start.Y + (dy * t));

		return Distance(point, closest);
	}

	/// <summary>
	/// Computes how many world units one vertical pixel covers at a
	/// given distance from the camera.
	/// </summary>
	/// <param name="verticalFieldOfView">The field of view in radians.</param>
	/// <param name="viewportHeightPixels">The viewport height in pixels.</param>
	/// <param name="distance">The distance from the camera.</param>
	/// <remarks>
	/// Gizmos use this to keep a constant on-screen size, which is what
	/// makes their handles reachable at any zoom level.
	/// </remarks>
	public static float WorldPerPixel(
		float verticalFieldOfView,
		float viewportHeightPixels,
		float distance)
	{
		if (!float.IsFinite(distance) || distance <= 0f ||
			viewportHeightPixels <= 0f)
		{
			return 0f;
		}

		float halfHeight = MathF.Tan(verticalFieldOfView * 0.5f) * distance;

		return (halfHeight * 2f) / viewportHeightPixels;
	}

	/// <summary>
	/// Builds every authored entity's world matrix in hierarchy order.
	/// </summary>
	/// <remarks>
	/// A child inherits its parent's transform, and the order comes from
	/// the document so a parent is always resolved before its children.
	/// </remarks>
	public static Dictionary<Guid, Matrix4x4> BuildWorldMatrices(
		SceneDocument scene)
	{
		Dictionary<Guid, Matrix4x4> matrices = [];

		foreach (SceneEntity entity in scene.BuildHierarchyOrder())
		{
			Matrix4x4 local = CreateLocalMatrix(entity.Transform);

			matrices[entity.Id] = entity.ParentId is Guid parentId &&
				matrices.TryGetValue(parentId, out Matrix4x4 parent)
					? local * parent
					: local;
		}

		return matrices;
	}

	/// <summary>
	/// Resolves the scene's first enabled directional light.
	/// </summary>
	/// <param name="scene">The authored scene, or null.</param>
	/// <param name="worldMatrices">
	/// Already resolved world matrices, when the caller has them; the
	/// method builds a fresh set otherwise.
	/// </param>
	/// <returns>
	/// The resolved light, or <see cref="ViewportLight.Default"/> when
	/// the scene has no enabled light.
	/// </returns>
	/// <remarks>
	/// A light shines along its world +Z axis, so a parent's rotation
	/// and scale are inherited. Only one directional light is supported;
	/// selecting the first enabled light in hierarchy order is
	/// deterministic and keeps the renderer's cost independent of the
	/// number of lights in the scene.
	/// </remarks>
	public static ViewportLight ResolveLight(
		SceneDocument? scene,
		IReadOnlyDictionary<Guid, Matrix4x4>? worldMatrices = null)
	{
		if (scene is null)
		{
			return ViewportLight.Default;
		}

		worldMatrices ??= BuildWorldMatrices(scene);

		foreach (SceneEntity entity in scene.BuildHierarchyOrder())
		{
			if (entity.GetComponent<LightData>() is not
				{ Enabled: true } light)
			{
				continue;
			}

			Matrix4x4 world = worldMatrices.TryGetValue(
				entity.Id,
				out Matrix4x4 matrix)
					? matrix
					: Matrix4x4.Identity;

			// Authored forward is the direction the light travels.
			// The renderer wants the direction from the surface toward
			// the light, which is the opposite vector.
			Vector3 forward = Vector3.TransformNormal(
				Vector3.UnitZ,
				world);

			return new ViewportLight(
				-forward,
				light.Color,
				light.Intensity);
		}

		return ViewportLight.Default;
	}

	/// <summary>
	/// Builds one local transform matrix.
	/// </summary>
	public static Matrix4x4 CreateLocalMatrix(TransformData transform)
	{
		return Matrix4x4.CreateScale(transform.Scale) *
			Matrix4x4.CreateFromQuaternion(transform.Rotation) *
			Matrix4x4.CreateTranslation(transform.Position);
	}

	/// <summary>
	/// Extracts one component of a vector by index.
	/// </summary>
	public static float Component(Vector3 value, int axis)
	{
		return axis switch
		{
			0 => value.X,
			1 => value.Y,
			2 => value.Z,
			_ => throw new ArgumentOutOfRangeException(nameof(axis))
		};
	}

	/// <summary>
	/// Draws a wire box for one entity's bounds.
	/// </summary>
	/// <param name="drawList">The draw list to append to.</param>
	/// <param name="world">The entity's world transform.</param>
	/// <param name="viewProjection">The overlay view-projection.</param>
	/// <param name="bounds">The viewport rectangle.</param>
	/// <param name="color">The line colour.</param>
	/// <param name="center">The box centre in local space.</param>
	/// <param name="halfExtent">The box half extent in local space.</param>
	/// <param name="thickness">The line thickness in pixels.</param>
	public static void DrawWireBox(
		UiDrawList drawList,
		in Matrix4x4 world,
		in Matrix4x4 viewProjection,
		UiRect bounds,
		UiColor color,
		Vector3 center,
		Vector3 halfExtent,
		float thickness)
	{
		Span<Vector3> local = stackalloc Vector3[8]
		{
			center + new Vector3(-halfExtent.X, -halfExtent.Y, -halfExtent.Z),
			center + new Vector3(halfExtent.X, -halfExtent.Y, -halfExtent.Z),
			center + new Vector3(halfExtent.X, halfExtent.Y, -halfExtent.Z),
			center + new Vector3(-halfExtent.X, halfExtent.Y, -halfExtent.Z),
			center + new Vector3(-halfExtent.X, -halfExtent.Y, halfExtent.Z),
			center + new Vector3(halfExtent.X, -halfExtent.Y, halfExtent.Z),
			center + new Vector3(halfExtent.X, halfExtent.Y, halfExtent.Z),
			center + new Vector3(-halfExtent.X, halfExtent.Y, halfExtent.Z)
		};

		Span<UiPoint> projected = stackalloc UiPoint[8];
		Span<bool> visible = stackalloc bool[8];

		for (int index = 0; index < 8; index++)
		{
			visible[index] = TryProject(
				viewProjection,
				Vector3.Transform(local[index], world),
				bounds,
				out projected[index]);
		}

		ReadOnlySpan<int> edges =
		[
			0, 1, 1, 2, 2, 3, 3, 0,
			4, 5, 5, 6, 6, 7, 7, 4,
			0, 4, 1, 5, 2, 6, 3, 7
		];

		for (int index = 0; index < edges.Length; index += 2)
		{
			int from = edges[index];
			int to = edges[index + 1];

			if (!visible[from] || !visible[to])
			{
				continue;
			}

			drawList.AddLine(
				projected[from],
				projected[to],
				thickness,
				color);
		}
	}

	/// <summary>
	/// Draws a wire box with the unit-primitive extent.
	/// </summary>
	/// <remarks>
	/// Kept for callers that only know an entity's transform, such as
	/// viewports without a resolved mesh.
	/// </remarks>
	public static void DrawWireCube(
		UiDrawList drawList,
		in Matrix4x4 world,
		in Matrix4x4 viewProjection,
		UiRect bounds,
		UiColor color,
		float size = 1f)
	{
		DrawWireBox(
			drawList,
			world,
			viewProjection,
			bounds,
			color,
			Vector3.Zero,
			new Vector3(size * 0.5f),
			1f);
	}

	private static float Distance(UiPoint first, UiPoint second)
	{
		float dx = first.X - second.X;
		float dy = first.Y - second.Y;

		return MathF.Sqrt((dx * dx) + (dy * dy));
	}
}
