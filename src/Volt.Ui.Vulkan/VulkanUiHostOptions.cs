using Volt.Platform.Windows;
using Volt.Ui.Styling;

namespace Volt.Ui.Vulkan;

/// <summary>
/// Describes one native VOLT UI window and its presentation settings.
/// </summary>
public sealed class VulkanUiHostOptions
{
	/// <summary>
	/// Gets the native window title.
	/// </summary>
	public string Title { get; init; } = "VOLT";

	/// <summary>
	/// Gets the initial logical window size.
	/// </summary>
	public WindowSize Size { get; init; } = new(1440, 900);

	/// <summary>
	/// Gets the preferred initial window position, or null to let the
	/// window manager place it.
	/// </summary>
	public WindowPosition? Position { get; init; }

	/// <summary>
	/// Gets whether Vulkan validation layers are requested.
	/// </summary>
	public bool EnableValidation { get; init; }

	/// <summary>
	/// Gets the theme used for the lifetime of the window.
	/// </summary>
	/// <remarks>
	/// The host replaces this theme's display scale whenever the ratio
	/// between logical and physical pixels changes, so the application
	/// should not install a new theme itself.
	/// </remarks>
	public UiTheme Theme { get; init; } = UiTheme.CreateDark();

	/// <summary>
	/// Gets an optional override for the compiled UI vertex shader.
	/// </summary>
	public string? VertexShaderPath { get; init; }

	/// <summary>
	/// Gets an optional override for the compiled UI fragment shader.
	/// </summary>
	public string? FragmentShaderPath { get; init; }

	/// <summary>
	/// Gets an optional sink for diagnostics reported while the host
	/// releases native resources.
	/// </summary>
	/// <remarks>
	/// A failure that makes native cleanup unsafe is reported here
	/// instead of being thrown from disposal, because a second exception
	/// would replace the frame failure that caused it. Applications that
	/// leave this unset can read <c>VulkanUiHost.Diagnostics</c>.
	/// </remarks>
	public Action<string>? DiagnosticSink { get; init; }

	/// <summary>
	/// Gets whether the presentation loop is driven by a fixed frame
	/// interval instead of running as fast as the display allows.
	/// </summary>
	/// <remarks>
	/// This exists for automated smoke runs that must terminate in a
	/// bounded amount of time. Interactive applications leave it unset
	/// and let presentation throttle the loop.
	/// </remarks>
	public TimeSpan? FrameInterval { get; init; }
}
