using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Swapchain;

namespace Volt.Graphics.Vulkan.Tests.Swapchain;

public sealed class VulkanSwapchainRecreationTests
{
	private static readonly SurfaceFormatKHR Bgra = new(
		Format.B8G8R8A8Srgb,
		ColorSpaceKHR.SpaceSrgbNonlinearKhr);

	private static readonly SurfaceFormatKHR Rgba = new(
		Format.R8G8B8A8Srgb,
		ColorSpaceKHR.SpaceSrgbNonlinearKhr);

	private static VulkanSwapchainSupport Support(
		ImageUsageFlags? usage = null,
		uint minImageCount = 2,
		uint maxImageCount = 4,
		SurfaceFormatKHR[]? formats = null,
		PresentModeKHR[]? presentModes = null)
	{
		SurfaceCapabilitiesKHR capabilities = new()
		{
			MinImageCount = minImageCount,
			MaxImageCount = maxImageCount,
			CurrentExtent = new Extent2D(
				uint.MaxValue,
				uint.MaxValue),
			MinImageExtent = new Extent2D(1, 1),
			MaxImageExtent = new Extent2D(8192, 8192),
			SupportedUsageFlags = usage ??
				(ImageUsageFlags.ColorAttachmentBit |
				 ImageUsageFlags.TransferDstBit),
			SupportedCompositeAlpha =
				CompositeAlphaFlagsKHR.OpaqueBitKhr,
			CurrentTransform =
				SurfaceTransformFlagsKHR.IdentityBitKhr
		};

		return new VulkanSwapchainSupport(
			capabilities,
			formats ?? [Bgra],
			presentModes ??
				[PresentModeKHR.FifoKhr, PresentModeKHR.MailboxKhr]);
	}

	[Fact]
	public void Plan_ReusesSupportedFormatAndPresentMode()
	{
		VulkanSwapchainRecreationPlan plan = VulkanSwapchainRecreation.Plan(
			Support(),
			1280,
			720,
			Bgra,
			PresentModeKHR.FifoKhr,
			ImageUsageFlags.ColorAttachmentBit |
				ImageUsageFlags.TransferDstBit,
			CompositeAlphaFlagsKHR.OpaqueBitKhr);

		Assert.Equal(Format.B8G8R8A8Srgb, plan.Format.Format);
		Assert.Equal(
			ColorSpaceKHR.SpaceSrgbNonlinearKhr,
			plan.Format.ColorSpace);
		Assert.Equal(PresentModeKHR.FifoKhr, plan.PresentMode);
		Assert.Equal(1280u, plan.Extent.Width);
		Assert.Equal(720u, plan.Extent.Height);
		Assert.False(plan.RequiresDependentRebuild);
	}

	[Fact]
	public void Plan_FlagsRebuildWhenPreviousFormatIsUnavailable()
	{
		VulkanSwapchainRecreationPlan plan = VulkanSwapchainRecreation.Plan(
			Support(),
			800,
			600,
			Rgba,
			PresentModeKHR.FifoKhr,
			ImageUsageFlags.ColorAttachmentBit |
				ImageUsageFlags.TransferDstBit,
			CompositeAlphaFlagsKHR.OpaqueBitKhr);

		Assert.Equal(Format.B8G8R8A8Srgb, plan.Format.Format);
		Assert.True(plan.RequiresDependentRebuild);
	}

	[Fact]
	public void Plan_FlagsRebuildWhenUsageChanges()
	{
		VulkanSwapchainRecreationPlan plan = VulkanSwapchainRecreation.Plan(
			Support(),
			800,
			600,
			Bgra,
			PresentModeKHR.FifoKhr,
			ImageUsageFlags.ColorAttachmentBit,
			CompositeAlphaFlagsKHR.OpaqueBitKhr);

		Assert.Equal(
			ImageUsageFlags.ColorAttachmentBit |
				ImageUsageFlags.TransferDstBit,
			plan.ImageUsage);
		Assert.True(plan.RequiresDependentRebuild);
	}

	[Fact]
	public void Plan_FallsBackWhenPreviousPresentModeIsUnavailable()
	{
		VulkanSwapchainRecreationPlan plan = VulkanSwapchainRecreation.Plan(
			Support(),
			800,
			600,
			Bgra,
			PresentModeKHR.ImmediateKhr,
			ImageUsageFlags.ColorAttachmentBit |
				ImageUsageFlags.TransferDstBit,
			CompositeAlphaFlagsKHR.OpaqueBitKhr);

		Assert.Equal(PresentModeKHR.FifoKhr, plan.PresentMode);
		Assert.True(plan.RequiresDependentRebuild);
	}

	[Fact]
	public void Plan_UsesReportedImageCountBounds()
	{
		VulkanSwapchainRecreationPlan plan = VulkanSwapchainRecreation.Plan(
			Support(minImageCount: 3, maxImageCount: 5),
			800,
			600,
			Bgra,
			PresentModeKHR.FifoKhr,
			ImageUsageFlags.ColorAttachmentBit |
				ImageUsageFlags.TransferDstBit,
			CompositeAlphaFlagsKHR.OpaqueBitKhr);

		Assert.InRange(plan.ImageCount, 3u, 5u);
	}

	[Fact]
	public void Plan_RejectsZeroFramebufferWidth()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			VulkanSwapchainRecreation.Plan(
				Support(),
				0,
				600,
				Bgra,
				PresentModeKHR.FifoKhr,
				ImageUsageFlags.ColorAttachmentBit,
				CompositeAlphaFlagsKHR.OpaqueBitKhr));
	}

	[Fact]
	public void Plan_RejectsSurfaceWithoutColorAttachment()
	{
		Assert.Throws<NotSupportedException>(() =>
			VulkanSwapchainRecreation.Plan(
				Support(usage: ImageUsageFlags.TransferDstBit),
				800,
				600,
				Bgra,
				PresentModeKHR.FifoKhr,
				ImageUsageFlags.TransferDstBit,
				CompositeAlphaFlagsKHR.OpaqueBitKhr));
	}

	[Fact]
	public void Plan_FallsBackWhenPreviousCompositeAlphaIsUnsupported()
	{
		SurfaceCapabilitiesKHR capabilities = new()
		{
			MinImageCount = 2,
			MaxImageCount = 3,
			CurrentExtent = new Extent2D(uint.MaxValue, uint.MaxValue),
			MinImageExtent = new Extent2D(1, 1),
			MaxImageExtent = new Extent2D(4096, 4096),
			SupportedUsageFlags = ImageUsageFlags.ColorAttachmentBit,
			SupportedCompositeAlpha =
				CompositeAlphaFlagsKHR.PreMultipliedBitKhr,
			CurrentTransform =
				SurfaceTransformFlagsKHR.IdentityBitKhr
		};

		VulkanSwapchainSupport support = new(
			capabilities,
			[Bgra],
			[PresentModeKHR.FifoKhr]);

		VulkanSwapchainRecreationPlan plan =
			VulkanSwapchainRecreation.Plan(
				support,
				640,
				480,
				Bgra,
				PresentModeKHR.FifoKhr,
				ImageUsageFlags.ColorAttachmentBit,
				CompositeAlphaFlagsKHR.OpaqueBitKhr);

		Assert.Equal(
			CompositeAlphaFlagsKHR.PreMultipliedBitKhr,
			plan.CompositeAlpha);
		Assert.False(plan.RequiresDependentRebuild);
	}
}
