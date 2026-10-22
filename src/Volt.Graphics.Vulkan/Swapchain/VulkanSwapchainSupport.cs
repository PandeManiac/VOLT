
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Surface;

namespace Volt.Graphics.Vulkan.Swapchain;

/// <summary>
/// Represents a snapshot of Vulkan swapchain support for a
/// physical device and presentation surface.
/// </summary>
/// <remarks>
/// Surface capabilities can change after window resizing or
/// display configuration changes.
///
/// Query them again before creating or recreating a swapchain.
/// This class does not own native Vulkan resources.
/// </remarks>
public sealed unsafe class VulkanSwapchainSupport
{
	private const int MaxEnumerationAttempts = 4;

	/// <summary>
	/// Gets the reported surface capabilities.
	/// </summary>
	public SurfaceCapabilitiesKHR Capabilities { get; }

	/// <summary>
	/// Gets the available surface formats.
	/// </summary>
	public ReadOnlyMemory<SurfaceFormatKHR> Formats { get; }

	/// <summary>
	/// Gets the available presentation modes.
	/// </summary>
	public ReadOnlyMemory<PresentModeKHR> PresentModes { get; }

	/// <summary>
	/// Gets whether color attachment images are supported.
	/// </summary>
	public bool SupportsColorAttachment =>
		(Capabilities.SupportedUsageFlags &
			ImageUsageFlags.ColorAttachmentBit) != 0;

	/// <summary>
	/// Gets whether the reported capabilities satisfy VOLT's
	/// initial color-rendering swapchain requirements.
	/// </summary>
	public bool CanCreateColorSwapchain =>
		Formats.Length > 0 &&
		PresentModes.Length > 0 &&
		Capabilities.MinImageCount > 0 &&
		SupportsColorAttachment;

	internal VulkanSwapchainSupport(
		SurfaceCapabilitiesKHR capabilities,
		SurfaceFormatKHR[] formats,
		PresentModeKHR[] presentModes)
	{
		ArgumentNullException.ThrowIfNull(formats);
		ArgumentNullException.ThrowIfNull(presentModes);

		Capabilities = capabilities;
		Formats = formats.ToArray();
		PresentModes = presentModes.ToArray();
	}

	/// <summary>
	/// Queries Vulkan swapchain support for a physical device
	/// and an existing presentation surface.
	/// </summary>
	public static VulkanSwapchainSupport Query(
		VulkanInstance instance,
		PhysicalDevice physicalDevice,
		VulkanSurface surface)
	{
		ArgumentNullException.ThrowIfNull(instance);
		ArgumentNullException.ThrowIfNull(surface);

		if (physicalDevice.Handle == 0)
		{
			throw new ArgumentException(
				"A valid Vulkan physical-device handle is required.",
				nameof(physicalDevice));
		}

		SurfaceKHR surfaceHandle = surface.Handle;
		Vk api = instance.Api;

		VerifyPhysicalDevice(instance, physicalDevice);

		if (!api.TryGetInstanceExtension<KhrSurface>(
				instance.Handle,
				out KhrSurface? extension)
			|| extension is null)
		{
			throw new NotSupportedException(
				"The Vulkan VK_KHR_surface extension is unavailable.");
		}

		using (extension)
		{
			Result result =
				extension.GetPhysicalDeviceSurfaceCapabilities(
					physicalDevice,
					surfaceHandle,
					out SurfaceCapabilitiesKHR capabilities);

			EnsureSuccess(
				result,
				"Query Vulkan surface capabilities");

			SurfaceFormatKHR[] formats =
				EnumerateSurfaceFormats(
					extension,
					physicalDevice,
					surfaceHandle);

			PresentModeKHR[] presentModes =
				EnumeratePresentModes(
					extension,
					physicalDevice,
					surfaceHandle);

			return new VulkanSwapchainSupport(
				capabilities,
				formats,
				presentModes);
		}
	}

	private static void VerifyPhysicalDevice(
		VulkanInstance instance,
		PhysicalDevice physicalDevice)
	{
		PhysicalDevice[] available =
			instance.EnumeratePhysicalDevices();

		foreach (PhysicalDevice candidate in available)
		{
			if (candidate.Handle == physicalDevice.Handle)
			{
				return;
			}
		}

		throw new ArgumentException(
			"The physical device does not belong to " +
			"the supplied Vulkan instance.",
			nameof(physicalDevice));
	}

	private static SurfaceFormatKHR[] EnumerateSurfaceFormats(
		KhrSurface extension,
		PhysicalDevice physicalDevice,
		SurfaceKHR surface)
	{
		for (int attempt = 0;
			 attempt < MaxEnumerationAttempts;
			 attempt++)
		{
			uint count = 0;

			EnsureSuccess(
				extension.GetPhysicalDeviceSurfaceFormats(
					physicalDevice,
					surface,
					ref count,
					null),
				"Count Vulkan surface formats");

			if (count == 0)
			{
				return [];
			}

			SurfaceFormatKHR[] formats =
				new SurfaceFormatKHR[checked((int)count)];

			fixed (SurfaceFormatKHR* pointer = formats)
			{
				Result result =
					extension.GetPhysicalDeviceSurfaceFormats(
						physicalDevice,
						surface,
						ref count,
						pointer);

				if (result == Result.Incomplete ||
					count > formats.Length)
				{
					continue;
				}

				EnsureSuccess(
					result,
					"Enumerate Vulkan surface formats");
			}

			if (count < formats.Length)
			{
				Array.Resize(
					ref formats,
					checked((int)count));
			}

			return formats;
		}

		throw new InvalidOperationException(
			"The Vulkan surface format list kept changing " +
			"during enumeration.");
	}

	private static PresentModeKHR[] EnumeratePresentModes(
		KhrSurface extension,
		PhysicalDevice physicalDevice,
		SurfaceKHR surface)
	{
		for (int attempt = 0;
			 attempt < MaxEnumerationAttempts;
			 attempt++)
		{
			uint count = 0;

			EnsureSuccess(
				extension.GetPhysicalDeviceSurfacePresentModes(
					physicalDevice,
					surface,
					ref count,
					null),
				"Count Vulkan presentation modes");

			if (count == 0)
			{
				return [];
			}

			PresentModeKHR[] modes =
				new PresentModeKHR[checked((int)count)];

			fixed (PresentModeKHR* pointer = modes)
			{
				Result result =
					extension.GetPhysicalDeviceSurfacePresentModes(
						physicalDevice,
						surface,
						ref count,
						pointer);

				if (result == Result.Incomplete ||
					count > modes.Length)
				{
					continue;
				}

				EnsureSuccess(
					result,
					"Enumerate Vulkan presentation modes");
			}

			if (count < modes.Length)
			{
				Array.Resize(
					ref modes,
					checked((int)count));
			}

			return modes;
		}

		throw new InvalidOperationException(
			"The Vulkan presentation mode list kept changing " +
			"during enumeration.");
	}

	private static void EnsureSuccess(
		Result result,
		string operation)
	{
		if (result != Result.Success)
		{
			throw new InvalidOperationException(
				$"{operation} failed: {result}.");
		}
	}
}
