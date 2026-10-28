using System.Numerics;

namespace Volt.Editor.Core.Scenes;

/// <summary>
/// The change one authored transform went through, ready to be
/// replayed on the rest of a multi-selection.
/// </summary>
/// <remarks>
/// <para>
/// A multi-object edit is applied as a delta rather than as an
/// absolute transform: every other object keeps its own placement and
/// is moved by exactly what the edited object moved, so a group keeps
/// its spacing.
/// </para>
/// <para>
/// The delta is expressed in the edited object's parent space, which
/// is the space its own transform is authored in. The editor does not
/// invent a world-space solve for a selection that mixes parents, and
/// a group rotation turns each object in place rather than orbiting
/// it around the edited object.
/// </para>
/// </remarks>
public readonly record struct TransformDelta(
	Vector3 Position,
	Vector3 EulerAngles,
	Vector3 ScaleRatio)
{
	/// <summary>
	/// A delta that changes nothing.
	/// </summary>
	public static TransformDelta None => new(
		Vector3.Zero,
		Vector3.Zero,
		Vector3.One);

	/// <summary>
	/// Gets whether applying this delta would leave an object alone.
	/// </summary>
	public bool IsIdentity =>
		Position == Vector3.Zero &&
		EulerAngles == Vector3.Zero &&
		ScaleRatio == Vector3.One;

	/// <summary>
	/// Captures the change from one authored transform to another.
	/// </summary>
	/// <param name="before">The transform at the start of the edit.</param>
	/// <param name="after">The transform the developer produced.</param>
	public static TransformDelta Between(
		TransformData before,
		TransformData after)
	{
		ArgumentNullException.ThrowIfNull(before);
		ArgumentNullException.ThrowIfNull(after);

		return new TransformDelta(
			after.Position - before.Position,
			Euler(after.EulerAngles - before.EulerAngles),
			Ratio(before.Scale, after.Scale));
	}

	/// <summary>
	/// Replays this delta on another object.
	/// </summary>
	/// <param name="target">The transform to change in place.</param>
	public void ApplyTo(TransformData target)
	{
		ArgumentNullException.ThrowIfNull(target);

		target.Position += Position;
		target.EulerAngles = Euler(target.EulerAngles + EulerAngles);
		target.Scale = new Vector3(
			target.Scale.X * ScaleRatio.X,
			target.Scale.Y * ScaleRatio.Y,
			target.Scale.Z * ScaleRatio.Z);
	}

	/// <summary>
	/// Wraps each angle into (-180, 180], the shortest turn.
	/// </summary>
	/// <remarks>
	/// Wrapping keeps a stored Euler angle in the range the inspector
	/// shows, and stops a group rotation from drifting to thousands of
	/// degrees over a long drag.
	/// </remarks>
	public static Vector3 Euler(Vector3 degrees)
	{
		return new Vector3(
			Wrap(degrees.X),
			Wrap(degrees.Y),
			Wrap(degrees.Z));
	}

	private static float Wrap(float degrees)
	{
		if (!float.IsFinite(degrees))
		{
			return 0f;
		}

		float wrapped = degrees % 360f;

		if (wrapped > 180f)
		{
			wrapped -= 360f;
		}

		else if (wrapped <= -180f)
		{
			wrapped += 360f;
		}

		return wrapped;
	}

	private static Vector3 Ratio(Vector3 before, Vector3 after)
	{
		return new Vector3(
			Ratio(before.X, after.X),
			Ratio(before.Y, after.Y),
			Ratio(before.Z, after.Z));
	}

	private static float Ratio(float before, float after)
	{
		// A flat object has no meaningful ratio to copy, so it is left
		// at its own scale rather than collapsing to zero.
		if (!float.IsFinite(before) || MathF.Abs(before) < 1e-5f)
		{
			return 1f;
		}

		float ratio = after / before;
		return float.IsFinite(ratio) ? ratio : 1f;
	}
}
