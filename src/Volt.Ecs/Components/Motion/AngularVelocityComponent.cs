
using System.Numerics;

namespace Volt.Ecs.Components.Motion;

/// <summary>
/// Represents an entity's angular velocity in three-dimensional space.
/// </summary>
/// <remarks>
/// Angular velocity is expressed in world-space radians per second.
///
/// The vector's direction specifies the rotation axis,
/// and its magnitude specifies the angular speed.
///
/// A zero vector represents no angular motion.
/// </remarks>
public struct AngularVelocityComponent(Vector3 value)
{
    public Vector3 Value = value;

    /// <summary>
    /// Gets a component representing zero angular velocity.
    /// </summary>
    public static AngularVelocityComponent Zero => new(Vector3.Zero);
}
