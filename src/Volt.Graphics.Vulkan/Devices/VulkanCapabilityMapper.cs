
using Volt.Graphics.Devices;

namespace Volt.Graphics.Vulkan.Devices;

/// <summary>
/// Converts Vulkan physical-device capabilities into VOLT's
/// backend-independent graphics capability representation.
/// </summary>
/// <remarks>
/// All reported values originate from the supplied physical-device
/// snapshot. The mapper does not query Vulkan or allocate native
/// graphics resources.
/// </remarks>
public static class VulkanCapabilityMapper
{
	/// <summary>
	/// Creates a graphics capability snapshot for a suitable
	/// Vulkan physical device.
	/// </summary>
	/// <param name="device">
	/// The physical-device information obtained from Vulkan.
	/// </param>
	/// <returns>
	/// The corresponding backend-independent graphics capabilities.
	/// </returns>
	/// <exception cref="ArgumentException">
	/// The device does not satisfy VOLT's minimum graphics requirements.
	/// </exception>
	public static GraphicsDeviceCapabilities Map(
		VulkanPhysicalDeviceInfo device)
	{
		device.Validate();

		if (!device.IsSuitableForGraphics)
		{
			throw new ArgumentException(
				"The Vulkan physical device does not satisfy " +
				"VOLT's minimum graphics requirements.",
				nameof(device));
		}

		GraphicsDeviceCapabilities capabilities = new(
			Backend: GraphicsBackend.Vulkan,
			AdapterName: device.Name,
			MaxTexture2DDimension: device.MaxTexture2DDimension,
			MaxColorAttachments: device.MaxColorAttachments,
			SupportsWireframe: device.SupportsWireframe,
			SupportsTimestampQueries: device.SupportsTimestampQueries);

		capabilities.Validate();
		return capabilities;
	}
}
