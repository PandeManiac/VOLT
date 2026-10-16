
using Silk.NET.Vulkan;

namespace Volt.Graphics.Vulkan.Swapchain;

/// <summary>
/// Selects swapchain parameters from supported Vulkan surface capabilities.
/// </summary>
/// <remarks>
/// This class makes no native Vulkan calls. Callers must first
/// query capabilities, formats, and present modes for the actual surface.
/// </remarks>
public static class VulkanSwapchainSelector
{
    /// <summary>
    /// Prefers an sRGB BGRA format, then an sRGB RGBA format,
    /// before accepting another supported format.
    /// </summary>
    public static SurfaceFormatKHR SelectSurfaceFormat(
        ReadOnlySpan<SurfaceFormatKHR> formats)
    {
        if (formats.IsEmpty)
        {
            throw new ArgumentException(
                "At least one surface format is required.",
                nameof(formats));
        }

        if (formats.Length == 1
                && formats[0].Format == Format.Undefined)
        {
            return new SurfaceFormatKHR
            {
                Format = Format.B8G8R8A8Srgb,
                ColorSpace = ColorSpaceKHR.SpaceSrgbNonlinearKhr
            };
        }

        foreach (Format preferred in new[]
        {
            Format.B8G8R8A8Srgb,
            Format.R8G8B8A8Srgb
        })
        {
            foreach (SurfaceFormatKHR candidate in formats)
            {
                if (candidate.Format == preferred
                        && candidate.ColorSpace
                            == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
                {
                    return candidate;
                }
            }
        }

        return formats[0];
    }

    /// <summary>
    /// Selects FIFO presentation by default, or Mailbox when requested
    /// and available.
    /// </summary>
    public static PresentModeKHR SelectPresentMode(
        ReadOnlySpan<PresentModeKHR> modes,
        bool preferMailbox = false)
    {
        if (modes.IsEmpty)
        {
            throw new ArgumentException(
                "At least one presentation mode is required.",
                nameof(modes));
        }

        if (preferMailbox)
        {
            foreach (PresentModeKHR mode in modes)
            {
                if (mode == PresentModeKHR.MailboxKhr)
                {
                    return mode;
                }
            }
        }

        foreach (PresentModeKHR mode in modes)
        {
            if (mode == PresentModeKHR.FifoKhr)
            {
                return mode;
            }
        }

        throw new NotSupportedException(
            "The surface does not report FIFO presentation support.");
    }

    /// <summary>
    /// Requests one more image than the surface minimum, subject
    /// to the surface's maximum image count.
    /// </summary>
    public static uint SelectImageCount(
        SurfaceCapabilitiesKHR capabilities)
    {
        if (capabilities.MinImageCount == 0)
        {
            throw new ArgumentException(
                "The surface minimum image count must be positive.",
                nameof(capabilities));
        }

        if (capabilities.MaxImageCount != 0
                && capabilities.MaxImageCount < capabilities.MinImageCount)
        {
            throw new ArgumentException(
                "The surface maximum image count is below its minimum.",
                nameof(capabilities));
        }

        uint count = capabilities.MinImageCount;

        if (count < uint.MaxValue)
        {
            count++;
        }

        if (capabilities.MaxImageCount != 0)
        {
            count = Math.Min(count, capabilities.MaxImageCount);
        }

        return count;
    }

    /// <summary>
    /// Uses the surface's fixed extent when provided; otherwise
    /// clamps the requested framebuffer size to supported bounds.
    /// </summary>
    public static Extent2D SelectExtent(
        SurfaceCapabilitiesKHR capabilities,
        uint framebufferWidth,
        uint framebufferHeight)
    {
        if (framebufferWidth == 0 || framebufferHeight == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(framebufferWidth),
                "The framebuffer dimensions must be positive. " +
                "Defer swapchain creation while the window is minimized.");
        }

        if (capabilities.CurrentExtent.Width != uint.MaxValue)
        {
            return capabilities.CurrentExtent;
        }

        if (capabilities.MinImageExtent.Width >
                capabilities.MaxImageExtent.Width ||
            capabilities.MinImageExtent.Height >
                capabilities.MaxImageExtent.Height)
        {
            throw new ArgumentException(
                "The surface extent bounds are invalid.",
                nameof(capabilities));
        }

        return new Extent2D
        {
            Width = Math.Clamp(
                framebufferWidth,
                capabilities.MinImageExtent.Width,
                capabilities.MaxImageExtent.Width),

            Height = Math.Clamp(
                framebufferHeight,
                capabilities.MinImageExtent.Height,
                capabilities.MaxImageExtent.Height)
        };
    }
}
