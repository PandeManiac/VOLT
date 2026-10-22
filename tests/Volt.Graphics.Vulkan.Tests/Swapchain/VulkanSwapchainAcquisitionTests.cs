
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Swapchain;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Volt.Graphics.Vulkan.Tests.Swapchain;

public sealed class VulkanSwapchainAcquisitionTests
{
	[Fact]
	public void NullInstance_IsRejected()
	{
		Assert.Throws<ArgumentNullException>(
			() => VulkanSwapchainAcquisition.Acquire(
				null!,
				null!,
				null!,
				new Semaphore(1UL)));
	}

	[Fact]
	public void SuccessfulAcquisition_ReturnsImageIndex()
	{
		VulkanImageAcquisitionResult acquisition =
			VulkanSwapchainAcquisition.InterpretResult(
				Result.Success,
				2,
				4);

		Assert.Equal(
			VulkanImageAcquisitionStatus.Success,
			acquisition.Status);

		Assert.Equal(2u, acquisition.ImageIndex);
		Assert.True(acquisition.HasImage);
		Assert.False(acquisition.RequiresRecreation);
	}

	[Fact]
	public void SuboptimalAcquisition_StillReturnsImage()
	{
		VulkanImageAcquisitionResult acquisition =
			VulkanSwapchainAcquisition.InterpretResult(
				Result.SuboptimalKhr,
				3,
				4);

		Assert.Equal(
			VulkanImageAcquisitionStatus.Suboptimal,
			acquisition.Status);

		Assert.Equal(3u, acquisition.ImageIndex);
		Assert.True(acquisition.HasImage);
		Assert.True(acquisition.RequiresRecreation);
	}

	[Fact]
	public void OutOfDateAcquisition_RequiresRecreation()
	{
		VulkanImageAcquisitionResult acquisition =
			VulkanSwapchainAcquisition.InterpretResult(
				Result.ErrorOutOfDateKhr,
				0,
				4);

		Assert.Equal(
			VulkanImageAcquisitionStatus.OutOfDate,
			acquisition.Status);

		Assert.Null(acquisition.ImageIndex);
		Assert.False(acquisition.HasImage);
		Assert.True(acquisition.RequiresRecreation);
	}

	[Theory]
	[InlineData(Result.Timeout)]
	[InlineData(Result.NotReady)]
	public void UnavailableImage_DoesNotAcquireOwnership(
		Result result)
	{
		VulkanImageAcquisitionResult acquisition =
			VulkanSwapchainAcquisition.InterpretResult(
				result,
				uint.MaxValue,
				4);

		Assert.False(acquisition.HasImage);
		Assert.Null(acquisition.ImageIndex);
		Assert.False(acquisition.RequiresRecreation);
	}

	[Theory]
	[InlineData(Result.Success)]
	[InlineData(Result.SuboptimalKhr)]
	public void AcquiredImageIndex_MustBeWithinRange(
		Result result)
	{
		Assert.Throws<InvalidOperationException>(
			() => VulkanSwapchainAcquisition.InterpretResult(
				result,
				4,
				4));
	}

	[Fact]
	public void EmptySwapchain_CannotReturnAnAcquiredImage()
	{
		Assert.Throws<InvalidOperationException>(
			() => VulkanSwapchainAcquisition.InterpretResult(
				Result.Success,
				0,
				0));
	}

	[Theory]
	[InlineData(Result.ErrorDeviceLost)]
	[InlineData(Result.ErrorSurfaceLostKhr)]
	public void FatalAcquisitionErrors_AreReported(
		Result result)
	{
		InvalidOperationException exception =
			Assert.Throws<InvalidOperationException>(
				() => VulkanSwapchainAcquisition.InterpretResult(
					result,
					0,
					4));

		Assert.Contains(
			result.ToString(),
			exception.Message);
	}
}
