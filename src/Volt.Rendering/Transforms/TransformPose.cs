using System.Numerics;
using Volt.Ecs.Components.Spatial;

namespace Volt.Rendering.Transforms;

/// <summary>
/// Describes one entity's translation, rotation, and scale as a
/// value that can be interpolated for rendering.
/// </summary>
/// <remarks>
/// <para>
/// A pose is presentation data derived from
/// <see cref="PositionComponent"/>, <see cref="RotationComponent"/>,
/// and <see cref="ScaleComponent"/>. Interpolating two poses never
/// writes to the ECS: the components remain the authoritative
/// simulation state.
/// </para>
/// <para>
/// Position and scale are interpolated component-wise. Rotation uses
/// shortest-path spherical interpolation, so a quaternion and its
/// negation produce the same orientation and never animate a spurious
/// full turn. The interpolated rotation is normalized even when an
/// authored component was not, but the repaired value exists only in
/// the returned pose.
/// </para>
/// </remarks>
public readonly struct TransformPose
{
	private const float MinimumRotationLengthSquared = 1e-12f;

	private const float LinearRotationThreshold = 0.9995f;

	/// <summary>
	/// Creates a pose from its three components.
	/// </summary>
	/// <param name="position">The world-space position.</param>
	/// <param name="rotation">The orientation.</param>
	/// <param name="scale">The local scale.</param>
	public TransformPose(
		Vector3 position,
		Quaternion rotation,
		Vector3 scale)
	{
		Position = position;
		Rotation = rotation;
		Scale = scale;
	}

	/// <summary>
	/// Gets the identity pose.
	/// </summary>
	public static TransformPose Identity => new(
		Vector3.Zero,
		Quaternion.Identity,
		Vector3.One);

	/// <summary>
	/// Gets the world-space position.
	/// </summary>
	public Vector3 Position { get; }

	/// <summary>
	/// Gets the orientation.
	/// </summary>
	public Quaternion Rotation { get; }

	/// <summary>
	/// Gets the local scale.
	/// </summary>
	public Vector3 Scale { get; }

	/// <summary>
	/// Reads a pose from an entity's spatial components.
	/// </summary>
	/// <param name="position">The position component.</param>
	/// <param name="rotation">The rotation component.</param>
	/// <param name="scale">The scale component.</param>
	/// <returns>
	/// A pose containing the component values exactly as stored.
	/// </returns>
	public static TransformPose FromComponents(
		in PositionComponent position,
		in RotationComponent rotation,
		in ScaleComponent scale)
	{
		return new TransformPose(
			position.Value,
			rotation.Value,
			scale.Value);
	}

	/// <summary>
	/// Interpolates between two poses.
	/// </summary>
	/// <param name="previous">
	/// The pose at the previous completed fixed step.
	/// </param>
	/// <param name="current">
	/// The pose at the latest completed fixed step.
	/// </param>
	/// <param name="alpha">
	/// The interpolation fraction. Values at or below zero return
	/// <paramref name="previous"/>; values at or above one return
	/// <paramref name="current"/>.
	/// </param>
	/// <returns>
	/// The interpolated pose, with a normalized rotation.
	/// </returns>
	/// <remarks>
	/// If an endpoint's rotation is zero-length or non-finite, the
	/// other endpoint's rotation is used. If both are invalid, the
	/// identity rotation is used. Neither component nor pose values
	/// are modified.
	/// </remarks>
	public static TransformPose Lerp(
		in TransformPose previous,
		in TransformPose current,
		float alpha)
	{
		if (!float.IsFinite(alpha) || alpha <= 0.0f)
		{
			return previous;
		}

		if (alpha >= 1.0f)
		{
			return current;
		}

		return new TransformPose(
			Vector3.Lerp(previous.Position, current.Position, alpha),
			LerpRotation(
				previous.Rotation,
				current.Rotation,
				alpha),
			Vector3.Lerp(previous.Scale, current.Scale, alpha));
	}

	/// <summary>
	/// Builds this pose's world-space model matrix.
	/// </summary>
	/// <returns>
	/// <c>scale * rotation * translation</c> in row-vector order.
	/// </returns>
	public Matrix4x4 CreateModelMatrix()
	{
		return TransformMatrices.CreateModelMatrix(this);
	}

	/// <summary>
	/// Interpolates two quaternions along the shortest arc.
	/// </summary>
	private static Quaternion LerpRotation(
		Quaternion previous,
		Quaternion current,
		float alpha)
	{
		bool previousValid = IsNormalizable(previous);
		bool currentValid = IsNormalizable(current);

		if (!previousValid && !currentValid)
		{
			return Quaternion.Identity;
		}

		if (!previousValid)
		{
			return Quaternion.Normalize(current);
		}

		if (!currentValid)
		{
			return Quaternion.Normalize(previous);
		}

		Quaternion from = Quaternion.Normalize(previous);
		Quaternion to = Quaternion.Normalize(current);

		float dot = (from.X * to.X) +
			(from.Y * to.Y) +
			(from.Z * to.Z) +
			(from.W * to.W);

		// q and -q describe the same orientation. Flipping one keeps
		// the interpolation on the short arc instead of rotating the
		// long way around.
		if (dot < 0.0f)
		{
			to = -to;
			dot = -dot;
		}

		dot = Math.Clamp(dot, -1.0f, 1.0f);

		if (dot > LinearRotationThreshold)
		{
			// Very small angles are numerically unstable in the
			// trigonometric form; a normalized lerp is accurate to
			// well below visible error there.
			return Quaternion.Normalize(Quaternion.Lerp(
				from,
				to,
				alpha));
		}

		float angle = MathF.Acos(dot);
		float sinAngle = MathF.Sin(angle);
		float previousWeight =
			MathF.Sin((1.0f - alpha) * angle) / sinAngle;
		float currentWeight = MathF.Sin(alpha * angle) / sinAngle;

		Quaternion result = new(
			(previousWeight * from.X) + (currentWeight * to.X),
			(previousWeight * from.Y) + (currentWeight * to.Y),
			(previousWeight * from.Z) + (currentWeight * to.Z),
			(previousWeight * from.W) + (currentWeight * to.W));

		return Quaternion.Normalize(result);
	}

	private static bool IsNormalizable(Quaternion rotation)
	{
		if (!float.IsFinite(rotation.X) ||
			!float.IsFinite(rotation.Y) ||
			!float.IsFinite(rotation.Z) ||
			!float.IsFinite(rotation.W))
		{
			return false;
		}

		float lengthSquared =
			(rotation.X * rotation.X) +
			(rotation.Y * rotation.Y) +
			(rotation.Z * rotation.Z) +
			(rotation.W * rotation.W);

		return lengthSquared > MinimumRotationLengthSquared;
	}
}
