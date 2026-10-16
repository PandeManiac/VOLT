
using System.Numerics;

namespace Volt.Ecs.Components.Spatial;

/// <summary>
/// Represents an entity's position in three-dimensional space.
/// </summary>
/// <remarks>
/// Positions use world-space coordinates by convention.
/// VOLT uses a right-handed coordinate system with +Y as up.
/// </remarks>
/// <remarks>
/// Initializes a position component with the specified position.
/// </remarks>
public struct PositionComponent(Vector3 value)
{
    public Vector3 Value = value;
}
