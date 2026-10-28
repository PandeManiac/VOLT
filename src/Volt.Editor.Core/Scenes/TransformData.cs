using System.Numerics;

namespace Volt.Editor.Core.Scenes;

/// <summary>
/// Authored local transform. Euler angles are stored in degrees so
/// the value the developer typed is the value saved.
/// </summary>
public sealed class TransformData : IEquatable<TransformData>
{
	public Vector3 Position { get; set; } = Vector3.Zero;

	public Vector3 EulerAngles { get; set; } = Vector3.Zero;

	public Vector3 Scale { get; set; } = Vector3.One;

	public Quaternion Rotation
	{
		get
		{
			Vector3 radians = EulerAngles * (MathF.PI / 180f);

			return Quaternion.CreateFromYawPitchRoll(
				radians.Y,
				radians.X,
				radians.Z);
		}
	}

	public TransformData Clone()
	{
		return new TransformData
		{
			Position = Position,
			EulerAngles = EulerAngles,
			Scale = Scale
		};
	}

	public void CopyFrom(TransformData other)
	{
		ArgumentNullException.ThrowIfNull(other);

		Position = other.Position;
		EulerAngles = other.EulerAngles;
		Scale = other.Scale;
	}

	public bool Equals(TransformData? other)
	{
		return other is not null &&
			Position == other.Position &&
			EulerAngles == other.EulerAngles &&
			Scale == other.Scale;
	}

	public override bool Equals(object? obj)
	{
		return obj is TransformData other && Equals(other);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(Position, EulerAngles, Scale);
	}
}
