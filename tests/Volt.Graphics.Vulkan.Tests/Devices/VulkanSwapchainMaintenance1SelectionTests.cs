using Volt.Graphics.Vulkan.Devices;

namespace Volt.Graphics.Vulkan.Tests.Devices;

public sealed class VulkanSwapchainMaintenance1SelectionTests
{
	[Theory]
	[InlineData(
		true, true, true, true, true,
		"VK_KHR_swapchain_maintenance1")]
	[InlineData(
		true, false, true, false, true,
		"VK_KHR_swapchain_maintenance1")]
	[InlineData(
		false, true, false, true, true,
		"VK_EXT_swapchain_maintenance1")]
	[InlineData(
		true, true, false, true, true,
		"VK_EXT_swapchain_maintenance1")]
	[InlineData(
		true, true, true, true, false,
		null)]
	[InlineData(
		false, false, true, true, true,
		null)]
	[InlineData(
		true, false, false, true, true,
		null)]
	[InlineData(
		false, true, true, false, true,
		null)]
	public void Selection_RequiresCompatibleExtensionsAndFeature(
		bool khrSurfaceEnabled,
		bool extSurfaceEnabled,
		bool khrDeviceSupported,
		bool extDeviceSupported,
		bool featureSupported,
		string? expected)
	{
		string? actual =
			VulkanLogicalDevice.SelectSwapchainMaintenance1Extension(
				khrSurfaceEnabled,
				extSurfaceEnabled,
				khrDeviceSupported,
				extDeviceSupported,
				featureSupported);

		Assert.Equal(expected, actual);
	}

	[Fact]
	public void NullInstance_IsRejectedBeforeNativeAccess()
	{
		Assert.Throws<ArgumentNullException>(() =>
			VulkanLogicalDevice.GetSupportedSwapchainMaintenance1Extension(
				null!,
				default));
	}

	[Fact]
	public void NullInstance_BooleanSupportCheck_IsRejected()
	{
		Assert.Throws<ArgumentNullException>(() =>
			VulkanLogicalDevice.SupportsSwapchainMaintenance1(
				null!,
				default));
	}
}
