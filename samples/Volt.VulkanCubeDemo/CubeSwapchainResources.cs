using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Commands;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Surface;
using Volt.Graphics.Vulkan.Swapchain;
using Volt.Graphics.Vulkan.Synchronization;
using Volt.Platform.Windows;

namespace Volt.VulkanCubeDemo;

/// <summary>
/// Owns the presentation resources for one swapchain generation.
/// </summary>
/// <remarks>
/// Command buffers and their command pool belong to the application
/// and outlive this generation. The caller must establish GPU and
/// presentation completion before disposing or replacing this object.
/// </remarks>
internal sealed class CubeSwapchainResources : IDisposable
{
    private readonly VulkanSwapchainImageViews _imageViews;

    private bool _disposed;

    public VulkanSwapchain Swapchain { get; }

    public VulkanSwapchainFrameScheduler Frames { get; }

    public CubeRenderer Renderer { get; }

    public CubeSwapchainResources(
        VulkanInstance instance,
        PhysicalDevice physicalDevice,
        VulkanLogicalDevice device,
        VulkanSurface surface,
        VulkanCommandBuffers commandBuffers,
        WindowSize framebufferSize,
        int maximumFramesInFlight,
        string vertexShaderPath,
        string fragmentShaderPath)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(commandBuffers);

        if (!framebufferSize.IsPositive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(framebufferSize),
                "Cannot create a swapchain for a zero-sized framebuffer.");
        }

        if (maximumFramesInFlight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumFramesInFlight));
        }

        VulkanSwapchain? swapchain = null;
        VulkanSwapchainImageViews? imageViews = null;
        VulkanSwapchainFrameScheduler? frames = null;
        CubeRenderer? renderer = null;

        try
        {
            swapchain = new VulkanSwapchain(
                instance,
                physicalDevice,
                device,
                surface,
                checked((uint)framebufferSize.Width),
                checked((uint)framebufferSize.Height));

            imageViews = new VulkanSwapchainImageViews(
                instance,
                device,
                swapchain);

            renderer = new CubeRenderer(
                instance,
                device,
                physicalDevice,
                swapchain,
                imageViews,
                vertexShaderPath,
                fragmentShaderPath);

            frames = new VulkanSwapchainFrameScheduler(
                instance,
                device,
                swapchain,
                commandBuffers,
                maximumFramesInFlight);
        }

        catch
        {
            try
            {
                renderer?.Dispose();
            }

            finally
            {
                try
                {
                    frames?.Dispose();
                }

                finally
                {
                    try
                    {
                        imageViews?.Dispose();
                    }
                    finally
                    {
                        swapchain?.Dispose();
                    }
                }
            }

            throw;
        }

        _imageViews = imageViews ?? throw new InvalidOperationException(
            "Swapchain image views were not initialized.");

        Swapchain = swapchain ?? throw new InvalidOperationException(
            "Swapchain was not initialized.");

        Frames = frames ?? throw new InvalidOperationException(
            "Frame scheduler was not initialized.");

        Renderer = renderer ?? throw new InvalidOperationException(
            "Cube renderer was not initialized.");
    }

    /// <summary>
    /// Releases resources belonging to this swapchain generation.
    /// </summary>
    /// <remarks>
    /// The caller must establish graphics completion first.
    ///
    /// The frame scheduler establishes presentation completion
    /// when presentation fences are enabled.
    ///
    /// If presentation retirement fails, dependent resources
    /// are intentionally retained rather than destroyed
    /// while they may still be referenced by Vulkan.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Frames.Dispose();
        Renderer.Dispose();
        _imageViews.Dispose();
        Swapchain.Dispose();

        _disposed = true;
    }
}
