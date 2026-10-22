
namespace Volt.Platform.Windows;

/// <summary>
/// Represents the width and height of a window, in logical units or pixels.
/// </summary>
/// <remarks>
/// A zero dimension is permitted because minimized windows may have no drawable area.
/// </remarks>
public readonly record struct WindowSize(int Width, int Height)
{
	/// <summary>
	/// Gets whether both dimensions are positive.
	/// </summary>
	public bool IsPositive => Width > 0 && Height > 0;
}
