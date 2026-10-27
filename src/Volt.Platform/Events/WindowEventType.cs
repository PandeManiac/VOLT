
namespace Volt.Platform.Events;

/// <summary>
/// Identifies a window event independently of the native windowing library.
/// </summary>
public enum WindowEventType
{
	Unknown = 0,
	CloseRequested = 1,
	Resized = 2,
	PixelSizeChanged = 3,
	TextInput = 4
}
