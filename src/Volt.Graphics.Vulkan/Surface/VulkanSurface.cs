
using SDL3;
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Instance;
using Volt.Platform.Windows;

namespace Volt.Graphics.Vulkan.Surface;

/// <summary>
/// Owns a Vulkan presentation surface associated with an SDL window.
/// </summary>
/// <remarks>
/// The Vulkan instance and SDL window must remain alive until
/// this surface has been disposed.
///
/// All operations must be performed on the application's main thread.
///
/// A presentation surface does not own a logical device, graphics
/// queue, or swapchain.
/// </remarks>
public sealed class VulkanSurface : IDisposable
{
	private readonly VulkanInstance _instance;
	private readonly Window _window;

	private IntPtr _nativeHandle;
	private bool _disposed;

	/// <summary>
	/// Gets the borrowed Vulkan surface handle.
	/// </summary>
	/// <remarks>
	/// This handle becomes invalid when the surface is disposed.
	/// The caller must not destroy it independently.
	/// </remarks>
	public SurfaceKHR Handle
	{
		get
		{
			ThrowIfDisposed();

			return new SurfaceKHR(
				unchecked((ulong)_nativeHandle.ToInt64()));
		}
	}

	/// <summary>
	/// Creates a presentation surface for a Vulkan-compatible window.
	/// </summary>
	/// <param name="instance">
	/// A live Vulkan instance created with SDL's required
	/// Vulkan instance extensions enabled.
	/// </param>
	/// <param name="window">
	/// A live SDL window created with VulkanCompatible enabled.
	/// </param>
	public VulkanSurface(
		VulkanInstance instance,
		Window window)
	{
		ArgumentNullException.ThrowIfNull(instance);
		ArgumentNullException.ThrowIfNull(window);

		if (IntPtr.Size != 8)
		{
			throw new PlatformNotSupportedException(
				"VOLT Vulkan surfaces require a 64-bit process.");
		}

		IntPtr windowHandle = window.NativeHandle;
		IntPtr instanceHandle = instance.Handle.Handle;

		SDL.WindowFlags windowFlags =
			SDL.GetWindowFlags(windowHandle);

		if ((windowFlags & SDL.WindowFlags.Vulkan) == 0)
		{
			throw new ArgumentException(
				"The window was not created with " +
				"VulkanCompatible enabled.",
				nameof(window));
		}

		if (!SDL.VulkanCreateSurface(
			windowHandle,
			instanceHandle,
			IntPtr.Zero,
			out IntPtr nativeSurface))
		{
			throw new InvalidOperationException(
				"SDL could not create a Vulkan presentation " +
				$"surface: {SDL.GetError()}. " +
				"Ensure the Vulkan instance was created with " +
				"SDL's required instance extensions.");
		}

		if (nativeSurface == IntPtr.Zero)
		{
			throw new InvalidOperationException(
				"SDL reported successful Vulkan surface creation " +
				"but returned a null surface handle.");
		}

		_instance = instance;
		_window = window;
		_nativeHandle = nativeSurface;
	}

	/// <summary>
	/// Destroys the Vulkan presentation surface.
	/// </summary>
	/// <remarks>
	/// Any swapchains created for this surface must already
	/// have been destroyed.
	/// </remarks>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_ = _window.NativeHandle;
		IntPtr instanceHandle = _instance.Handle.Handle;

		SDL.VulkanDestroySurface(
			instanceHandle,
			_nativeHandle,
			IntPtr.Zero);

		_nativeHandle = IntPtr.Zero;
		_disposed = true;
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}
}
