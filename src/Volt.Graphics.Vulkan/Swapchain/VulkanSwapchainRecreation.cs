using Silk.NET.Vulkan;

namespace Volt.Graphics.Vulkan.Swapchain;

/// <summary>
/// Describes the surface parameters selected for a recreated swapchain.
/// </summary>
/// <remarks>
/// Reusing the previous format, color space, and present mode keeps the
/// existing render pass and pipelines valid. When any of them changes,
/// <see cref="RequiresDependentRebuild"/> is set so callers know that
/// dependent objects must be rebuilt as well.
/// </remarks>
public readonly record struct VulkanSwapchainRecreationPlan(
	Extent2D Extent,
	uint ImageCount,
	SurfaceFormatKHR Format,
	PresentModeKHR PresentMode,
	ImageUsageFlags ImageUsage,
	CompositeAlphaFlagsKHR CompositeAlpha,
	SurfaceTransformFlagsKHR PreTransform,
	bool RequiresDependentRebuild);

/// <summary>
/// Chooses the parameters for an in-place swapchain recreation.
/// </summary>
/// <remarks>
/// Capabilities are queried again before every recreation. The previous
/// format, present mode, usage, and composite alpha are retained whenever
/// the surface still supports them, so a plain window resize only needs
/// new images and image views rather than new pipelines.
/// </remarks>
public static class VulkanSwapchainRecreation
{
	/// <summary>
	/// Plans a recreation at the supplied framebuffer size.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="support"/> is null.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// A framebuffer dimension is zero.
	/// </exception>
	/// <exception cref="NotSupportedException">
	/// The surface cannot host a color swapchain.
	/// </exception>
	public static VulkanSwapchainRecreationPlan Plan(
		VulkanSwapchainSupport support,
		uint framebufferWidth,
		uint framebufferHeight,
		SurfaceFormatKHR previousFormat,
		PresentModeKHR previousPresentMode,
		ImageUsageFlags previousImageUsage,
		CompositeAlphaFlagsKHR previousCompositeAlpha)
	{
		ArgumentNullException.ThrowIfNull(support);

		if (framebufferWidth == 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(framebufferWidth),
				"Framebuffer width must be positive.");
		}

		if (framebufferHeight == 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(framebufferHeight),
				"Framebuffer height must be positive.");
		}

		if (!support.CanCreateColorSwapchain)
		{
			throw new NotSupportedException(
				"The surface does not support a color swapchain.");
		}

		SurfaceCapabilitiesKHR capabilities = support.Capabilities;

		SurfaceFormatKHR format = ContainsFormat(
			support.Formats.Span,
			previousFormat)
			? previousFormat
			: VulkanSwapchainSelector.SelectSurfaceFormat(
				support.Formats.Span);

		PresentModeKHR presentMode = ContainsPresentMode(
			support.PresentModes.Span,
			previousPresentMode)
			? previousPresentMode
			: VulkanSwapchainSelector.SelectPresentMode(
				support.PresentModes.Span,
				preferMailbox: false);

		ImageUsageFlags imageUsage =
			ImageUsageFlags.ColorAttachmentBit;

		if ((capabilities.SupportedUsageFlags &
			 ImageUsageFlags.TransferDstBit) != 0)
		{
			imageUsage |= ImageUsageFlags.TransferDstBit;
		}

		CompositeAlphaFlagsKHR compositeAlpha =
			(capabilities.SupportedCompositeAlpha & previousCompositeAlpha)
				!= 0
			? previousCompositeAlpha
			: SelectCompositeAlpha(
				capabilities.SupportedCompositeAlpha);

		Extent2D extent = VulkanSwapchainSelector.SelectExtent(
			capabilities,
			framebufferWidth,
			framebufferHeight);

		if (extent.Width == 0 || extent.Height == 0)
		{
			throw new InvalidOperationException(
				"The selected swapchain extent is zero. Defer " +
				"recreation until the framebuffer has positive " +
				"dimensions.");
		}

		bool requiresDependentRebuild =
			format.Format != previousFormat.Format ||
			format.ColorSpace != previousFormat.ColorSpace ||
			presentMode != previousPresentMode ||
			imageUsage != previousImageUsage;

		return new VulkanSwapchainRecreationPlan(
			extent,
			VulkanSwapchainSelector.SelectImageCount(capabilities),
			format,
			presentMode,
			imageUsage,
			compositeAlpha,
			capabilities.CurrentTransform,
			requiresDependentRebuild);
	}

	private static bool ContainsFormat(
		ReadOnlySpan<SurfaceFormatKHR> formats,
		SurfaceFormatKHR candidate)
	{
		foreach (SurfaceFormatKHR format in formats)
		{
			if (format.Format == candidate.Format &&
				format.ColorSpace == candidate.ColorSpace)
			{
				return true;
			}
		}

		return false;
	}

	private static bool ContainsPresentMode(
		ReadOnlySpan<PresentModeKHR> presentModes,
		PresentModeKHR candidate)
	{
		foreach (PresentModeKHR presentMode in presentModes)
		{
			if (presentMode == candidate)
			{
				return true;
			}
		}

		return false;
	}

	private static CompositeAlphaFlagsKHR SelectCompositeAlpha(
		CompositeAlphaFlagsKHR supported)
	{
		CompositeAlphaFlagsKHR[] preferences =
		[
			CompositeAlphaFlagsKHR.OpaqueBitKhr,
			CompositeAlphaFlagsKHR.PreMultipliedBitKhr,
			CompositeAlphaFlagsKHR.PostMultipliedBitKhr,
			CompositeAlphaFlagsKHR.InheritBitKhr
		];

		foreach (CompositeAlphaFlagsKHR candidate in preferences)
		{
			if ((supported & candidate) != 0)
			{
				return candidate;
			}
		}

		throw new NotSupportedException(
			"The surface does not expose a supported composite " +
			"alpha mode.");
	}
}
