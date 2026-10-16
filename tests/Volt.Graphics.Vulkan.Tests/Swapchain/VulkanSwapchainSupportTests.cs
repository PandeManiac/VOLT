
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Swapchain;

namespace Volt.Graphics.Vulkan.Tests.Swapchain;

public sealed class VulkanSwapchainSupportTests
{
    [Fact]
    public void ValidCapabilities_AllowColorSwapchain()
    {
        VulkanSwapchainSupport support = CreateSupport(
            ImageUsageFlags.ColorAttachmentBit);

        Assert.True(support.SupportsColorAttachment);
        Assert.True(support.CanCreateColorSwapchain);
    }

    [Fact]
    public void MissingColorAttachmentSupport_IsRejected()
    {
        VulkanSwapchainSupport support = CreateSupport(
            ImageUsageFlags.TransferDstBit);

        Assert.False(support.SupportsColorAttachment);
        Assert.False(support.CanCreateColorSwapchain);
    }

    [Fact]
    public void MissingSurfaceFormats_IsRejected()
    {
        VulkanSwapchainSupport support = new(
            CreateCapabilities(),
            [],
            [PresentModeKHR.FifoKhr]);

        Assert.False(support.CanCreateColorSwapchain);
    }

    [Fact]
    public void MissingPresentModes_IsRejected()
    {
        VulkanSwapchainSupport support = new(
            CreateCapabilities(),
            [
                new SurfaceFormatKHR(
                    Format.B8G8R8A8Srgb,
                    ColorSpaceKHR.SpaceSrgbNonlinearKhr)
            ],

            []);

        Assert.False(support.CanCreateColorSwapchain);
    }

    [Fact]
    public void ZeroMinimumImageCount_IsRejected()
    {
        SurfaceCapabilitiesKHR capabilities =
            CreateCapabilities();

        capabilities.MinImageCount = 0;

        VulkanSwapchainSupport support = new(
            capabilities,
            [
                new SurfaceFormatKHR(
                    Format.B8G8R8A8Srgb,
                    ColorSpaceKHR.SpaceSrgbNonlinearKhr)
            ],

            [PresentModeKHR.FifoKhr]);

        Assert.False(support.CanCreateColorSwapchain);
    }

    [Fact]
    public void SurfaceFormats_AreCopied()
    {
        SurfaceFormatKHR[] formats =
        [
            new(
                Format.B8G8R8A8Srgb,
                ColorSpaceKHR.SpaceSrgbNonlinearKhr)
        ];

        VulkanSwapchainSupport support = new(
            CreateCapabilities(),
            formats,
            [PresentModeKHR.FifoKhr]);

        formats[0] = new SurfaceFormatKHR(
            Format.R8G8B8A8Unorm,
            ColorSpaceKHR.SpaceSrgbNonlinearKhr);

        Assert.Equal(
            Format.B8G8R8A8Srgb,
            support.Formats.Span[0].Format);
    }

    [Fact]
    public void PresentationModes_AreCopied()
    {
        PresentModeKHR[] modes =
        [
            PresentModeKHR.FifoKhr
        ];

        VulkanSwapchainSupport support = new(
            CreateCapabilities(),
            [
                new SurfaceFormatKHR(
                    Format.B8G8R8A8Srgb,
                    ColorSpaceKHR.SpaceSrgbNonlinearKhr)
            ],

            modes);

        modes[0] = PresentModeKHR.ImmediateKhr;

        Assert.Equal(
            PresentModeKHR.FifoKhr,
            support.PresentModes.Span[0]);
    }

    [Fact]
    public void NullFormatArray_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new VulkanSwapchainSupport(
                CreateCapabilities(),
                null!,
                [PresentModeKHR.FifoKhr]));
    }

    [Fact]
    public void NullPresentModeArray_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new VulkanSwapchainSupport(
                CreateCapabilities(),
                [],
                null!));
    }

    [Fact]
    public void NullInstance_ThrowsBeforeNativeAccess()
    {
        Assert.Throws<ArgumentNullException>(
            () => VulkanSwapchainSupport.Query(
                null!,
                default,
                null!));
    }

    private static VulkanSwapchainSupport CreateSupport(
        ImageUsageFlags usageFlags)
    {
        SurfaceCapabilitiesKHR capabilities =
            CreateCapabilities();

        capabilities.SupportedUsageFlags = usageFlags;

        return new VulkanSwapchainSupport(
            capabilities,
            [
                new SurfaceFormatKHR(
                    Format.B8G8R8A8Srgb,
                    ColorSpaceKHR.SpaceSrgbNonlinearKhr)
            ],
            [
                PresentModeKHR.FifoKhr,
                PresentModeKHR.MailboxKhr
            ]);
    }

    private static SurfaceCapabilitiesKHR CreateCapabilities()
    {
        return new SurfaceCapabilitiesKHR
        {
            MinImageCount = 2,
            MaxImageCount = 3,

            SupportedUsageFlags =
                ImageUsageFlags.ColorAttachmentBit
        };
    }
}
