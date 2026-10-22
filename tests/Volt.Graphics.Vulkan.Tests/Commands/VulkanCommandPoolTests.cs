
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Commands;

namespace Volt.Graphics.Vulkan.Tests.Commands;

public sealed class VulkanCommandPoolTests
{
	[Fact]
	public void NullInstance_ThrowsBeforeNativeAccess()
	{
		Assert.Throws<ArgumentNullException>(
			() => new VulkanCommandPool(
				null!,
				null!));
	}

	[Fact]
	public void Description_UsesCommandPoolStructureType()
	{
		CommandPoolCreateInfo description =
			VulkanCommandPool.CreateDescription(0);

		Assert.Equal(
			StructureType.CommandPoolCreateInfo,
			description.SType);
	}

	[Theory]
	[InlineData(0u)]
	[InlineData(1u)]
	[InlineData(7u)]
	public void Description_PreservesQueueFamilyIndex(
		uint queueFamilyIndex)
	{
		CommandPoolCreateInfo description =
			VulkanCommandPool.CreateDescription(
				queueFamilyIndex);

		Assert.Equal(
			queueFamilyIndex,
			description.QueueFamilyIndex);
	}

	[Fact]
	public void Description_EnablesIndividualResetByDefault()
	{
		CommandPoolCreateInfo description =
			VulkanCommandPool.CreateDescription(0);

		Assert.Equal(
			CommandPoolCreateFlags.ResetCommandBufferBit,
			description.Flags);
	}

	[Fact]
	public void Description_AllowsTransientPools()
	{
		CommandPoolCreateInfo description =
			VulkanCommandPool.CreateDescription(
				0,
				CommandPoolCreateFlags.TransientBit);

		Assert.Equal(
			CommandPoolCreateFlags.TransientBit,
			description.Flags);
	}

	[Fact]
	public void Description_AllowsCombinedFlags()
	{
		CommandPoolCreateFlags flags =
			CommandPoolCreateFlags.TransientBit |
			CommandPoolCreateFlags.ResetCommandBufferBit;

		CommandPoolCreateInfo description =
			VulkanCommandPool.CreateDescription(0, flags);

		Assert.Equal(flags, description.Flags);
	}

	[Fact]
	public void Description_AllowsNoFlags()
	{
		CommandPoolCreateInfo description =
			VulkanCommandPool.CreateDescription(
				0,
				CommandPoolCreateFlags.None);

		Assert.Equal(
			CommandPoolCreateFlags.None,
			description.Flags);
	}

	[Fact]
	public void Description_RejectsProtectedPools()
	{
		ArgumentOutOfRangeException exception =
			Assert.Throws<ArgumentOutOfRangeException>(
				() => VulkanCommandPool.CreateDescription(
					0,
					CommandPoolCreateFlags.ProtectedBit));

		Assert.Equal("flags", exception.ParamName);
	}

	[Fact]
	public void Description_RejectsUnknownFlags()
	{
		Assert.Throws<ArgumentOutOfRangeException>(
			() => VulkanCommandPool.CreateDescription(
				0,
				(CommandPoolCreateFlags)0x4000));
	}
}
