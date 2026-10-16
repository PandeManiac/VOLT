
using Volt.Platform.Windows;

namespace Volt.Platform.Events;

/// <summary>
/// Describes an event delivered to an application window.
/// </summary>
/// <remarks>
/// Size contains logical dimensions for Resized events and framebuffer pixel
/// dimensions for PixelSizeChanged events. It is unused for CloseRequested.
/// </remarks>
public readonly record struct WindowEvent(
    WindowEventType Type,
    WindowSize Size = default);
