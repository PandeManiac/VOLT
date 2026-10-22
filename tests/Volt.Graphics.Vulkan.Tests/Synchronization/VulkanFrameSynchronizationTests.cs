
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Synchronization;

namespace Volt.Graphics.Vulkan.Tests.Synchronization;

public sealed class VulkanFrameSynchronizationTests
{
	[Fact]
	public void NullInstance_ThrowsBeforeNativeAccess()
	{
		Assert.Throws<ArgumentNullException>(
			() => new VulkanFrameSynchronization(
				null!,
				null!,
				2,
				4));
	}

	[Fact]
	public void SemaphoreDescription_UsesCorrectStructureType()
	{
		SemaphoreCreateInfo description =
			VulkanFrameSynchronization.CreateSemaphoreDescription();

		Assert.Equal(
			StructureType.SemaphoreCreateInfo,
			description.SType);
	}

	[Fact]
	public void SemaphoreDescription_UsesDefaultFlags()
	{
		SemaphoreCreateInfo description =
			VulkanFrameSynchronization.CreateSemaphoreDescription();

		Assert.Equal(
			SemaphoreCreateFlags.None,
			description.Flags);
	}

	[Fact]
	public void FenceDescription_UsesCorrectStructureType()
	{
		FenceCreateInfo description =
			VulkanFrameSynchronization.CreateFenceDescription();

		Assert.Equal(
			StructureType.FenceCreateInfo,
			description.SType);
	}

	[Fact]
	public void FenceDescription_IsInitiallySignaledByDefault()
	{
		FenceCreateInfo description =
			VulkanFrameSynchronization.CreateFenceDescription();

		Assert.Equal(
			FenceCreateFlags.SignaledBit,
			description.Flags);
	}

	[Fact]
	public void FenceDescription_AllowsUnsignaledFence()
	{
		FenceCreateInfo description =
			VulkanFrameSynchronization.CreateFenceDescription(
				initiallySignaled: false);

		Assert.Equal(
			FenceCreateFlags.None,
			description.Flags);
	}

	[Theory]
	[InlineData(1, 1)]
	[InlineData(1, 4)]
	[InlineData(2, 3)]
	[InlineData(2, 4)]
	[InlineData(3, 4)]
	public void ValidCounts_AreAccepted(
		int framesInFlight,
		int swapchainImageCount)
	{
		VulkanFrameSynchronization.ValidateCounts(
			framesInFlight,
			swapchainImageCount);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(-5)]
	public void InvalidFrameCount_IsRejected(int framesInFlight)
	{
		ArgumentOutOfRangeException exception =
			Assert.Throws<ArgumentOutOfRangeException>(
				() => VulkanFrameSynchronization.ValidateCounts(
					framesInFlight,
					4));

		Assert.Equal(
			"framesInFlight",
			exception.ParamName);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(-5)]
	public void InvalidSwapchainImageCount_IsRejected(
		int swapchainImageCount)
	{
		ArgumentOutOfRangeException exception =
			Assert.Throws<ArgumentOutOfRangeException>(
				() => VulkanFrameSynchronization.ValidateCounts(
					2,
					swapchainImageCount));

		Assert.Equal(
			"swapchainImageCount",
			exception.ParamName);
	}

	[Fact]
	public unsafe void SemaphoreDescription_HasNoExtensionChain()
	{
		SemaphoreCreateInfo description =
			VulkanFrameSynchronization.CreateSemaphoreDescription();

		Assert.True(description.PNext == null);
	}

	[Fact]
	public unsafe void FenceDescription_HasNoExtensionChain()
	{
		FenceCreateInfo description =
			VulkanFrameSynchronization.CreateFenceDescription();

		Assert.True(description.PNext == null);
	}
}
