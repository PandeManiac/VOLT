
using Silk.NET.Vulkan;

namespace Volt.Graphics.Vulkan.Devices;

/// <summary>
/// Represents an immutable snapshot of a Vulkan physical device's
/// identity, graphics support, and relevant hardware capabilities.
/// </summary>
/// <remarks>
/// This type contains no native handles and does not own Vulkan resources.
/// The graphics queue family does not necessarily support presentation
/// to a particular window surface.
/// </remarks>
public readonly record struct VulkanPhysicalDeviceInfo(
	string Name,
	PhysicalDeviceType DeviceType,
	uint VendorId,
	uint DeviceId,
	uint ApiVersion,
	uint? GraphicsQueueFamilyIndex,
	uint MaxTexture2DDimension,
	uint MaxColorAttachments,
	bool SupportsWireframe,
	bool SupportsTimestampQueries)
{
	/// <summary>
	/// Gets the major component of the Vulkan API version.
	/// </summary>
	public uint ApiMajor => (ApiVersion >> 22) & 0x7Fu;

	/// <summary>
	/// Gets the minor component of the Vulkan API version.
	/// </summary>
	public uint ApiMinor => (ApiVersion >> 12) & 0x3FFu;

	/// <summary>
	/// Gets the patch component of the Vulkan API version.
	/// </summary>
	public uint ApiPatch => ApiVersion & 0xFFFu;

	/// <summary>
	/// Gets whether this device is classified as a discrete GPU.
	/// </summary>
	public bool IsDiscreteGpu =>
		DeviceType == PhysicalDeviceType.DiscreteGpu;

	/// <summary>
	/// Gets whether the device exposes a graphics-capable queue family.
	/// </summary>
	public bool HasGraphicsQueue =>
		GraphicsQueueFamilyIndex.HasValue;

	/// <summary>
	/// Gets whether the device meets the initial requirements for
	/// VOLT's graphics backend.
	/// </summary>
	/// <remarks>
	/// This does not check presentation support, required device
	/// extensions, or later renderer-specific feature requirements.
	/// </remarks>
	public bool IsSuitableForGraphics =>
		HasGraphicsQueue &&
		MaxTexture2DDimension > 0 &&
		MaxColorAttachments > 0;

	/// <summary>
	/// Validates the structural integrity of the device snapshot.
	/// </summary>
	public void Validate()
	{
		if (string.IsNullOrWhiteSpace(Name))
		{
			throw new ArgumentException(
				"A physical-device name is required.",
				nameof(Name));
		}

		if (Name.Contains('\0'))
		{
			throw new ArgumentException(
				"The physical-device name cannot contain null characters.",
				nameof(Name));
		}

		if (ApiMajor == 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(ApiVersion),
				ApiVersion,
				"The Vulkan API major version must be positive.");
		}
	}
}
