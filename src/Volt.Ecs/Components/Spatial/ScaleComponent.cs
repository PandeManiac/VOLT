
using System.Numerics;

namespace Volt.Ecs.Components.Spatial;

/// <summary>
/// Represents an entity's scale in three-dimensional space.
/// </summary>
/// <remarks>
/// A scale of (1, 1, 1) preserves the original dimensions.
/// Nonuniform, zero, and negative scales are permitted.
/// </remarks>
public struct ScaleComponent(Vector3 value)
{
    public Vector3 Value = value;

    /// <summary>
    /// Gets a scale of one along every axis.
    /// </summary>
    public static ScaleComponent Identity => new(Vector3.One);
}
