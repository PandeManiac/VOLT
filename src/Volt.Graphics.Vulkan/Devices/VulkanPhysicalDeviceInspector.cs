
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Instance;

namespace Volt.Graphics.Vulkan.Devices;

/// <summary>
/// Inspects native Vulkan physical devices and produces managed
/// snapshots of their identity and graphics capabilities.
/// </summary>
/// <remarks>
/// This class does not create logical devices, queues, or other
/// GPU resources. Physical-device handles must originate from
/// the supplied Vulkan instance.
/// </remarks>
public static unsafe class VulkanPhysicalDeviceInspector
{
	/// <summary>
	/// Inspects a Vulkan physical device.
	/// </summary>
	/// <param name="instance">
	/// The live Vulkan instance that owns the physical device.
	/// </param>
	/// <param name="device">
	/// A physical-device handle enumerated from the instance.
	/// </param>
	/// <returns>
	/// A managed snapshot containing the device's reported
	/// properties, features, and graphics queue information.
	/// </returns>
	public static VulkanPhysicalDeviceInfo Inspect(
		VulkanInstance instance,
		PhysicalDevice device)
	{
		ArgumentNullException.ThrowIfNull(instance);
		Vk api = instance.Api;

		if (device.Handle == 0)
		{
			throw new ArgumentException(
				"A valid Vulkan physical-device handle is required.",
				nameof(device));
		}

		api.GetPhysicalDeviceProperties(
			device,
			out PhysicalDeviceProperties properties);

		api.GetPhysicalDeviceFeatures(
			device,
			out PhysicalDeviceFeatures features);

		QueueFamilyProperties[] queueFamilies =
			EnumerateQueueFamilies(api, device);

		uint? graphicsQueueFamilyIndex =
			FindGraphicsQueueFamily(queueFamilies);

		bool supportsTimestampQueries =
			graphicsQueueFamilyIndex.HasValue &&
			queueFamilies[checked((int)graphicsQueueFamilyIndex.Value)]
				.TimestampValidBits > 0;

		string name = Marshal.PtrToStringUTF8(
			(nint)properties.DeviceName) ?? string.Empty;

		VulkanPhysicalDeviceInfo information = new(
			Name: name,
			DeviceType: properties.DeviceType,
			VendorId: properties.VendorID,
			DeviceId: properties.DeviceID,
			ApiVersion: properties.ApiVersion,
			GraphicsQueueFamilyIndex: graphicsQueueFamilyIndex,

			MaxTexture2DDimension:
				properties.Limits.MaxImageDimension2D,

			MaxColorAttachments:
				properties.Limits.MaxColorAttachments,

			SupportsWireframe: features.FillModeNonSolid,
			SupportsTimestampQueries: supportsTimestampQueries);

		information.Validate();
		return information;
	}

	/// <summary>
	/// Finds the first queue family that supports graphics commands
	/// and exposes at least one queue.
	/// </summary>
	/// <remarks>
	/// Queue family indices are specific to their physical device.
	///
	/// Graphics capability alone does not guarantee presentation
	/// support for a particular window surface.
	/// </remarks>
	public static uint? FindGraphicsQueueFamily(
		ReadOnlySpan<QueueFamilyProperties> queueFamilies)
	{
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

			return (uint)index;
		}

		return null;
	}

	private static QueueFamilyProperties[] EnumerateQueueFamilies(
		Vk api,
		PhysicalDevice device)
	{
		uint count = 0;

		api.GetPhysicalDeviceQueueFamilyProperties(
			device,
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
				device,
				ref count,
				pointer);
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
