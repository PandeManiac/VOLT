
using System.Numerics;

namespace Volt.Ecs.Components.Motion;

/// <summary>
/// Represents an entity's linear velocity in three-dimensional space.
/// </summary>
/// <remarks>
/// Velocity is expressed in world-space units per second.
/// A zero vector represents a stationary entity.
/// </remarks>
public struct VelocityComponent(Vector3 value)
{
	public Vector3 Value = value;

	/// <summary>
	/// Gets a component representing zero linear velocity.
	/// </summary>
	public static VelocityComponent Zero => new(Vector3.Zero);
}
