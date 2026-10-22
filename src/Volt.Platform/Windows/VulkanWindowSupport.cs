
using SDL3;

namespace Volt.Platform.Windows;

/// <summary>
/// Provides Vulkan-specific interoperability for SDL windows.
/// </summary>
/// <remarks>
/// This class exposes SDL's Vulkan window-system requirements
/// without depending on a particular managed Vulkan binding.
///
/// All operations must run on the application's main thread.
/// </remarks>
public static class VulkanWindowSupport
{
	/// <summary>
	/// Gets the Vulkan instance extensions required to create
	/// a presentation surface for the specified window.
	/// </summary>
	/// <remarks>
	/// The window must have been created with VulkanCompatible
	/// enabled. Call this method before creating the Vulkan instance.
	///
	/// Extension requirements depend on SDL's active video driver.
	/// The returned strings are independent of SDL's native memory.
	/// </remarks>
	public static string[] GetRequiredInstanceExtensions(
		Window window)
	{
		ArgumentNullException.ThrowIfNull(window);

		IntPtr handle = window.NativeHandle;
		SDL.WindowFlags flags = SDL.GetWindowFlags(handle);

		if ((flags & SDL.WindowFlags.Vulkan) == 0)
		{
			throw new ArgumentException(
				"The window must be created with " +
				"VulkanCompatible enabled.",
				nameof(window));
		}

		string[]? extensions =
			SDL.VulkanGetInstanceExtensions(out uint count);

		if (extensions is null)
		{
			throw new InvalidOperationException(
				"SDL could not query the required Vulkan " +
				$"instance extensions: {SDL.GetError()}");
		}

		if (count == 0 || extensions.Length != count)
		{
			throw new InvalidOperationException(
				"SDL returned an invalid Vulkan instance " +
				"extension list.");
		}

		return extensions;
	}
}
