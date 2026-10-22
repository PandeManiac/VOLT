
using System.Numerics;

namespace Volt.Ecs.Components.Spatial;

/// <summary>
/// Represents an entity's orientation in three-dimensional space.
/// </summary>
/// <remarks>
/// Rotations use quaternions and are expected to be normalized.
///
/// VOLT uses a right-handed coordinate system with +Y as up
/// and -Z as forward.
/// </remarks>
public struct RotationComponent(Quaternion value)
{
	public Quaternion Value = value;

	/// <summary>
	/// Gets a rotation with no angular displacement.
	/// </summary>
	public static RotationComponent Identity => new(Quaternion.Identity);
}
