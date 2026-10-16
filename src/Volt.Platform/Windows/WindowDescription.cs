
namespace Volt.Platform.Windows;

/// <summary>
/// Describes the requested properties of a new application window.
/// </summary>
/// <remarks>
/// Size is expressed in logical window coordinates, not framebuffer pixels.
/// Set VulkanCompatible before creation when a Vulkan surface will be required.
/// </remarks>
public readonly record struct WindowDescription(
    string Title,
    WindowSize Size,
    bool Resizable = true,
    bool HighPixelDensity = true,
    bool VulkanCompatible = false)
{
    /// <summary>
    /// Validates the requested window properties.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            throw new ArgumentException(
                "A nonempty window title is required.",
                nameof(Title));
        }

        if (!Size.IsPositive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Size),
                Size,
                "Window dimensions must be positive.");
        }
    }
}
