
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Volt.Graphics.Vulkan.Instance;

namespace Volt.Graphics.Vulkan.Surface;

/// <summary>
/// Inspects Vulkan queue-family presentation support for a surface.
/// </summary>
/// <remarks>
/// Presentation support is specific to a physical device, queue family,
/// and Vulkan surface.
///
/// The physical device and surface must belong to the supplied instance.
///
/// This implementation currently requires one queue family capable
/// of both graphics and presentation.
/// </remarks>
public static class VulkanPresentationSupport
{
	/// <summary>
	/// Finds a queue family capable of both graphics and presentation
	/// to the specified Vulkan surface.
	/// </summary>
	/// <returns>
	/// The first suitable queue family index, or null if none exists.
	/// </returns>
	public static uint? FindGraphicsPresentationQueueFamily(
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

		PhysicalDevice[] physicalDevices =
			instance.EnumeratePhysicalDevices();

		bool belongsToInstance = false;

		foreach (PhysicalDevice candidate in physicalDevices)
		{
			if (candidate.Handle == physicalDevice.Handle)
			{
				belongsToInstance = true;
				break;
			}
		}

		if (!belongsToInstance)
		{
			throw new ArgumentException(
				"The physical device does not belong to " +
				"the supplied Vulkan instance.",
				nameof(physicalDevice));
		}

		if (!api.TryGetInstanceExtension<KhrSurface>(
				instance.Handle,
				out KhrSurface? surfaceExtension)
			|| surfaceExtension is null)
		{
			throw new NotSupportedException(
				"The Vulkan VK_KHR_surface extension is unavailable.");
		}

		using (surfaceExtension)
		{
			QueueFamilyProperties[] queueFamilies =
				EnumerateQueueFamilies(api, physicalDevice);

			bool[] presentationSupport =
				new bool[queueFamilies.Length];

			for (int index = 0; index < queueFamilies.Length; index++)
			{
				if (queueFamilies[index].QueueCount == 0)
				{
					continue;
				}

				Result result =
					surfaceExtension.GetPhysicalDeviceSurfaceSupport(
						physicalDevice,
						(uint)index,
						surfaceHandle,
						out Silk.NET.Core.Bool32 supported);

				if (result != Result.Success)
				{
					throw new InvalidOperationException(
						"Querying Vulkan presentation support " +
						$"for queue family {index} failed: {result}.");
				}

				presentationSupport[index] = supported;
			}

			return FindGraphicsPresentationQueueFamily(
				queueFamilies,
				presentationSupport);
		}
	}

	/// <summary>
	/// Finds the first queue family supporting both graphics
	/// commands and presentation.
	/// </summary>
	/// <remarks>
	/// This overload operates entirely on managed input data
	/// and does not access Vulkan.
	/// </remarks>
	public static uint? FindGraphicsPresentationQueueFamily(
		ReadOnlySpan<QueueFamilyProperties> queueFamilies,
		ReadOnlySpan<bool> presentationSupport)
	{
		if (queueFamilies.Length != presentationSupport.Length)
		{
			throw new ArgumentException(
				"Presentation support must contain one entry " +
				"for each queue family.",
				nameof(presentationSupport));
		}

		for (int index = 0; index < queueFamilies.Length; index++)
		{
			ref readonly QueueFamilyProperties family =
				ref queueFamilies[index];

			if (family.QueueCount == 0)
			{
				continue;
			}

			if ((family.QueueFlags & QueueFlags.GraphicsBit) == 0)
			{
				continue;
			}

			if (!presentationSupport[index])
			{
				continue;
			}

			return (uint)index;
		}

		return null;
	}

	private static unsafe QueueFamilyProperties[] EnumerateQueueFamilies(
		Vk api,
		PhysicalDevice physicalDevice)
	{
		uint count = 0;

		api.GetPhysicalDeviceQueueFamilyProperties(
			physicalDevice,
			ref count,
			null);

		if (count == 0)
		{
			return [];
		}

		QueueFamilyProperties[] families =
			new QueueFamilyProperties[checked((int)count)];

		fixed (QueueFamilyProperties* pointer = families)
		{
			api.GetPhysicalDeviceQueueFamilyProperties(
				physicalDevice,
				ref count,
				pointer);
		}

		if (count > families.Length)
		{
			throw new InvalidOperationException(
				"The Vulkan queue-family count changed " +
				"during enumeration.");
		}

		if (count < families.Length)
		{
			Array.Resize(
				ref families,
				checked((int)count));
		}

		return families;
	}
}
