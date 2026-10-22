
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Swapchain;

namespace Volt.Graphics.Vulkan.Tests.Swapchain;

public sealed class VulkanSwapchainSelectorTests
{
	[Fact]
	public void PreferredBgraSrgbFormat_IsSelected()
	{
		SurfaceFormatKHR[] formats =
		[
			new(Format.R8G8B8A8Srgb,
				ColorSpaceKHR.SpaceSrgbNonlinearKhr),

			new(Format.B8G8R8A8Srgb,
				ColorSpaceKHR.SpaceSrgbNonlinearKhr)
		];

		SurfaceFormatKHR result =
			VulkanSwapchainSelector.SelectSurfaceFormat(formats);

		Assert.Equal(Format.B8G8R8A8Srgb, result.Format);
	}

	[Fact]
	public void UnsupportedPreferredFormat_FallsBack()
	{
		SurfaceFormatKHR[] formats =
		[
			new(
				Format.R8G8B8A8Unorm,
				ColorSpaceKHR.SpaceSrgbNonlinearKhr)
		];

		SurfaceFormatKHR result =
			VulkanSwapchainSelector.SelectSurfaceFormat(formats);

		Assert.Equal(formats[0].Format, result.Format);
	}

	[Fact]
	public void UndefinedSingleFormat_UsesPreferredFormat()
	{
		SurfaceFormatKHR[] formats =
		[
			new(Format.Undefined,
				ColorSpaceKHR.SpaceSrgbNonlinearKhr)
		];

		SurfaceFormatKHR result =
			VulkanSwapchainSelector.SelectSurfaceFormat(formats);

		Assert.Equal(Format.B8G8R8A8Srgb, result.Format);
	}

	[Fact]
	public void Fifo_IsDefaultPresentMode()
	{
		PresentModeKHR[] modes =
		[
			PresentModeKHR.MailboxKhr,
			PresentModeKHR.FifoKhr
		];

		Assert.Equal(
			PresentModeKHR.FifoKhr,
			VulkanSwapchainSelector.SelectPresentMode(modes));
	}

	[Fact]
	public void Mailbox_IsSelectedWhenPreferredAndAvailable()
	{
		PresentModeKHR[] modes =
		[
			PresentModeKHR.FifoKhr,
			PresentModeKHR.MailboxKhr
		];

		Assert.Equal(
			PresentModeKHR.MailboxKhr,
			VulkanSwapchainSelector.SelectPresentMode(
				modes,
				preferMailbox: true));
	}

	[Fact]
	public void MissingMailbox_FallsBackToFifo()
	{
		Assert.Equal(
			PresentModeKHR.FifoKhr,
			VulkanSwapchainSelector.SelectPresentMode(
				[PresentModeKHR.FifoKhr],
				preferMailbox: true));
	}

	[Fact]
	public void ImageCount_RequestsOneAboveMinimum()
	{
		SurfaceCapabilitiesKHR capabilities = new()
		{
			MinImageCount = 2,
			MaxImageCount = 0
		};

		Assert.Equal(
			3u,
			VulkanSwapchainSelector.SelectImageCount(capabilities));
	}

	[Fact]
	public void ImageCount_RespectsMaximum()
	{
		SurfaceCapabilitiesKHR capabilities = new()
		{
			MinImageCount = 2,
			MaxImageCount = 2
		};

		Assert.Equal(
			2u,
			VulkanSwapchainSelector.SelectImageCount(capabilities));
	}

	[Fact]
	public void FixedExtent_IsPreserved()
	{
		SurfaceCapabilitiesKHR capabilities = new()
		{
			CurrentExtent = new Extent2D(1920, 1080)
		};

		Extent2D result =
			VulkanSwapchainSelector.SelectExtent(
				capabilities,
				1280,
				720);

		Assert.Equal(1920u, result.Width);
		Assert.Equal(1080u, result.Height);
	}

	[Fact]
	public void VariableExtent_IsClamped()
	{
		SurfaceCapabilitiesKHR capabilities = new()
		{
			CurrentExtent = new Extent2D(
				uint.MaxValue,
				uint.MaxValue),

			MinImageExtent = new Extent2D(640, 480),
			MaxImageExtent = new Extent2D(1920, 1080)
		};

		Extent2D result =
			VulkanSwapchainSelector.SelectExtent(
				capabilities,
				3840,
				200);

		Assert.Equal(1920u, result.Width);
		Assert.Equal(480u, result.Height);
	}

	[Fact]
	public void ZeroFramebufferWidth_IsRejected()
	{
		SurfaceCapabilitiesKHR capabilities = new();

		Assert.Throws<ArgumentOutOfRangeException>(
			() => VulkanSwapchainSelector.SelectExtent(
				capabilities,
				0,
				720));
	}

	[Fact]
	public void EmptyFormatList_IsRejected()
	{
		Assert.Throws<ArgumentException>(
			() => VulkanSwapchainSelector.SelectSurfaceFormat([]));
	}

	[Fact]
	public void EmptyPresentModeList_IsRejected()
	{
		Assert.Throws<ArgumentException>(
			() => VulkanSwapchainSelector.SelectPresentMode([]));
	}
}
