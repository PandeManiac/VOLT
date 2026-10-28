namespace Volt.Platform.Windows;

/// <summary>
/// Represents the top-left position of a window in desktop coordinates.
/// </summary>
/// <remarks>
/// The coordinates are logical desktop units, not framebuffer pixels.
/// Negative values are normal on a display arranged left of or above
/// the primary display.
/// </remarks>
public readonly record struct WindowPosition(int X, int Y);
