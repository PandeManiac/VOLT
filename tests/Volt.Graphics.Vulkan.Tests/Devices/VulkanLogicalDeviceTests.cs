
using Volt.Graphics.Vulkan.Devices;

namespace Volt.Graphics.Vulkan.Tests.Devices;

public sealed class VulkanLogicalDeviceTests
{
	[Fact]
	public void NullInstance_ThrowsBeforeNativeAccess()
	{
		Assert.Throws<ArgumentNullException>(
			() => new VulkanLogicalDevice(
				null!,
				default,
				0));
	}

	[Fact]
	public void EmptyExtensionList_PassesValidation()
	{
		VulkanLogicalDevice.ValidateExtensionNames([]);
	}

	[Fact]
	public void ValidExtensionName_PassesValidation()
	{
		VulkanLogicalDevice.ValidateExtensionNames(
			["VK_KHR_swapchain"]);
	}

	[Fact]
	public void MultipleUniqueExtensions_PassValidation()
	{
		VulkanLogicalDevice.ValidateExtensionNames(
			[
				"VK_KHR_swapchain",
				"VK_EXT_memory_budget"
			]);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(" ")]
	[InlineData("\t")]
	public void InvalidExtensionName_Throws(string? name)
	{
		ArgumentException exception =
			Assert.Throws<ArgumentException>(
				() => VulkanLogicalDevice.ValidateExtensionNames(
					[name!]));

		Assert.Equal("extensions", exception.ParamName);
	}

	[Fact]
	public void EmbeddedNullCharacter_Throws()
	{
		ArgumentException exception =
			Assert.Throws<ArgumentException>(
				() => VulkanLogicalDevice.ValidateExtensionNames(
					["VK_KHR_swapchain\0invalid"]));

		Assert.Equal("extensions", exception.ParamName);
	}

	[Fact]
	public void DuplicateExtensionName_Throws()
	{
		ArgumentException exception =
			Assert.Throws<ArgumentException>(
				() => VulkanLogicalDevice.ValidateExtensionNames(
					[
						"VK_KHR_swapchain",
						"VK_KHR_swapchain"
					]));

		Assert.Equal("extensions", exception.ParamName);
	}

	[Fact]
	public void ExtensionNames_AreCaseSensitive()
	{
		VulkanLogicalDevice.ValidateExtensionNames(
			[
				"VK_KHR_swapchain",
				"vk_KHR_swapchain"
			]);
	}

	[Fact]
	public void DuplicateCheck_UsesOrdinalComparison()
	{
		ArgumentException exception =
			Assert.Throws<ArgumentException>(
				() => VulkanLogicalDevice.ValidateExtensionNames(
					[
						"VK_EXT_memory_budget",
						"VK_KHR_swapchain",
						"VK_EXT_memory_budget"
					]));

		Assert.Equal("extensions", exception.ParamName);
	}

	[Fact]
	public void SwapchainMaintenanceSupport_NullInstance_IsRejected()
	{
		Assert.Throws<ArgumentNullException>(() =>
			VulkanLogicalDevice.SupportsSwapchainMaintenance1(
				null!,
				default));
	}
}
