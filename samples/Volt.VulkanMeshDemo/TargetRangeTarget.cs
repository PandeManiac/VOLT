using System.Numerics;
using Volt.Ecs;

namespace Volt.VulkanMeshDemo;

/// <summary>
/// Marks an entity as a target range target and records whether it has
/// been completed this round.
/// </summary>
/// <remarks>
/// <para>
/// The component is gameplay state only: it carries no transform, no
/// mesh, and no colour. A target's visible position, size, and mesh
/// stay in <see cref="Volt.Ecs.Components.Spatial.PositionComponent"/>,
/// <see cref="Volt.Ecs.Components.Spatial.ScaleComponent"/>, and
/// <see cref="Volt.Rendering.Components.MeshRendererComponent"/>, so
/// rendering and gameplay read and write the same source of truth.
/// </para>
/// <para>
/// A byte rather than a <see cref="bool"/> keeps the storage layout
/// explicit and avoids any marshalling questions in unmanaged
/// component columns. Zero means live and one means completed; the
/// scene never writes another value.
/// </para>
/// </remarks>
internal struct TargetStateComponent
{
	/// <summary>
	/// A target that is live and can still be hit.
	/// </summary>
	public const byte Live = 0;

	/// <summary>
	/// A target that has already been hit this round.
	/// </summary>
	public const byte Completed = 1;

	/// <summary>
	/// The target's completion state.
	/// </summary>
	public byte State;
}

/// <summary>
/// Describes one target's authored placement and effective radius.
/// </summary>
/// <remarks>
/// The definition is startup data. The live values are read back from
/// the ECS world for hit detection and rendering, so a definition is
/// never consulted for anything that can change during a round.
/// </remarks>
internal readonly record struct TargetDefinition(Vector3 Position, float Radius);

/// <summary>
/// Describes one target's collision sphere for raycasting.
/// </summary>
/// <remarks>
/// The center and radius are copied from the entity's own components
/// immediately before a shot, so the collision volume cannot drift
/// from what the GPU is drawing. The sphere tightly bounds the drawn
/// mesh: a unit-sphere mesh scaled by <c>s</c> reaches radius
/// <c>s</c> at its vertices, while its flat facets sit slightly
/// inside that sphere. A ray crossing the silhouette can therefore be
/// accepted a few percent of the radius outside the polygon surface.
/// The collider is never larger than the authored target and adds no
/// aiming assist on top of that bounding radius.
/// </remarks>
internal readonly record struct TargetSphere(
	Entity Entity,
	Vector3 Center,
	float Radius,
	bool Completed);

/// <summary>
/// Describes the outcome of one aiming ray.
/// </summary>
/// <remarks>
/// A miss leaves every field at its default and reports
/// <see cref="Hit"/> as false. <see cref="Distance"/> is measured
/// along the normalized ray from its origin.
/// </remarks>
internal readonly record struct TargetRaycastResult(
	bool Hit,
	Entity Entity,
	float Distance,
	Vector3 Point)
{
	/// <summary>
	/// Gets a result that hit nothing.
	/// </summary>
	public static TargetRaycastResult Miss => default;
}

/// <summary>
/// Performs CPU sphere casts for the target range's aiming ray.
/// </summary>
/// <remarks>
/// <para>
/// This type has no ECS, SDL, or Vulkan dependency on purpose: the
/// scene builds the sphere list from its components and this function
/// decides what the ray hits, so the geometry can be tested directly
/// and the gameplay decision stays independent of rendering.
/// </para>
/// <para>
/// The ray is a half-line: the origin is the camera's world-space
/// position and the direction is its normalized forward vector.
/// Intersections at or behind the origin are rejected, which is what
/// keeps a target behind the camera from being hit even though its
/// sphere is intersected mathematically. A ray that starts inside a
/// sphere hits on the way out, at the positive exit distance.
/// </para>
/// <para>
/// Targets nearer than an already found intersection replace it, so
/// overlapping targets always resolve to the nearest one. Targets
/// marked completed are ignored, as are intersections beyond the
/// caller's maximum distance.
/// </para>
/// <para>
/// Invalid input is an explicit miss rather than an exception or a
/// NaN distance: a non-finite origin, a non-finite or zero-length
/// direction, and a non-positive or non-finite maximum distance all
/// report no hit.
/// </para>
/// </remarks>
internal static class TargetRangeRaycast
{
	/// <summary>
	/// Largest distance the range can hit, in world units.
	/// </summary>
	/// <remarks>
	/// The deepest target sits about 19 units from the initial pose,
	/// so this limit leaves room to move while still rejecting shots
	/// well beyond the range.
	/// </remarks>
	public const float MaximumDistance = 40.0f;

	private const float MinimumDirectionLengthSquared = 1.0e-12f;

	/// <summary>
	/// Casts a ray against the supplied spheres.
	/// </summary>
	/// <param name="origin">The ray origin, in world space.</param>
	/// <param name="direction">
	/// The ray direction. It is normalized internally; a zero-length,
	/// non-finite direction misses.
	/// </param>
	/// <param name="maximumDistance">
	/// The largest accepted intersection distance. Must be finite and
	/// positive.
	/// </param>
	/// <param name="targets">The candidate spheres.</param>
	public static TargetRaycastResult Cast(
		Vector3 origin,
		Vector3 direction,
		float maximumDistance,
		ReadOnlySpan<TargetSphere> targets)
	{
		if (!IsFinite(origin) ||
			!IsFinite(direction) ||
			direction.LengthSquared() <= MinimumDirectionLengthSquared ||
			!float.IsFinite(maximumDistance) ||
			maximumDistance <= 0.0f)
		{
			return TargetRaycastResult.Miss;
		}

		Vector3 unitDirection = Vector3.Normalize(direction);

		float nearestDistance = float.PositiveInfinity;
		Entity nearestEntity = default;

		for (int index = 0; index < targets.Length; index++)
		{
			TargetSphere target = targets[index];

			if (target.Completed ||
				!IsFinite(target.Center) ||
				!float.IsFinite(target.Radius) ||
				target.Radius <= 0.0f)
			{
				continue;
			}

			Vector3 offset = origin - target.Center;

			float projection = Vector3.Dot(offset, unitDirection);
			float outside = offset.LengthSquared() -
				(target.Radius * target.Radius);

			float discriminant =
				(projection * projection) - outside;

			if (discriminant < 0.0f)
			{
				continue;
			}

			float root = MathF.Sqrt(discriminant);
			float entry = -projection - root;
			float exit = -projection + root;

			// A positive entry is the nearest surface in front of the
			// origin. When the origin is inside (or on) the sphere the
			// entry is not in front, so the exit is the hit instead.
			float distance = entry > 0.0f
				? entry
				: exit;

			if (distance <= 0.0f ||
				distance > maximumDistance ||
				distance >= nearestDistance)
			{
				continue;
			}

			nearestDistance = distance;
			nearestEntity = target.Entity;
		}

		if (!float.IsFinite(nearestDistance))
		{
			return TargetRaycastResult.Miss;
		}

		return new TargetRaycastResult(
			Hit: true,
			Entity: nearestEntity,
			Distance: nearestDistance,
			Point: origin + (unitDirection * nearestDistance));
	}

	private static bool IsFinite(Vector3 value)
	{
		return float.IsFinite(value.X) &&
			float.IsFinite(value.Y) &&
			float.IsFinite(value.Z);
	}
}
