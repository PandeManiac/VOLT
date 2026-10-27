using System.Numerics;

namespace Volt.Ui.Rendering;

/// <summary>
/// One UI vertex: logical position, texture coordinate, and packed
/// straight-alpha RGBA color (R in the low byte).
/// </summary>
public readonly record struct UiVertex(
	Vector2 Position,
	Vector2 Uv,
	uint Color);
