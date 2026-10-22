
using Volt.Graphics.Vulkan.Synchronization;

namespace Volt.Graphics.Vulkan.Tests.Synchronization;

public sealed class VulkanSwapchainFrameSchedulerTests
{
	[Theory]
	[InlineData(2, 3, 2, 2)]
	[InlineData(4, 2, 4, 2)]
	[InlineData(1, 5, 1, 1)]
	[InlineData(3, 3, 3, 3)]
	public void ValidConfiguration_SelectsBoundedFrameCount(
		int requested,
		int images,
		int commandBuffers,
		int expected)
	{
		int actual = VulkanSwapchainFrameScheduler.SelectFramesInFlight(
			requested,
			images,
			commandBuffers);

		Assert.Equal(expected, actual);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-2)]
	public void InvalidMaximumFrameCount_IsRejected(int count)
	{
		ArgumentOutOfRangeException exception =
			Assert.Throws<ArgumentOutOfRangeException>(() =>
				VulkanSwapchainFrameScheduler.SelectFramesInFlight(
					count,
					3,
					2));

		Assert.Equal("maximumFramesInFlight", exception.ParamName);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-2)]
	public void InvalidSwapchainImageCount_IsRejected(int count)
	{
		ArgumentOutOfRangeException exception =
			Assert.Throws<ArgumentOutOfRangeException>(() =>
				VulkanSwapchainFrameScheduler.SelectFramesInFlight(
					2,
					count,
					2));

		Assert.Equal("swapchainImageCount", exception.ParamName);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	public void InsufficientCommandBuffers_AreRejected(int count)
	{
		ArgumentOutOfRangeException exception =
			Assert.Throws<ArgumentOutOfRangeException>(() =>
				VulkanSwapchainFrameScheduler.SelectFramesInFlight(
					2,
					3,
					count));

		Assert.Equal("commandBufferCount", exception.ParamName);
	}

	[Fact]
	public void NullInstance_IsRejectedBeforeNativeAccess()
	{
		Assert.Throws<ArgumentNullException>(() =>
			new VulkanSwapchainFrameScheduler(
				null!,
				null!,
				null!,
				null!,
				2));
	}
}
