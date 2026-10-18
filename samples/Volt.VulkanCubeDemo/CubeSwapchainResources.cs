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
/// and outlive this generation. The caller must establish GPU
/// completion before disposing or replacing this object.
/// </remarks>
internal sealed class CubeSwapchainResources : IDisposable
{
    private readonly VulkanSwapchainImageViews _imageViews;
    private readonly Fence[] _imageInFlightFences;

    private bool _disposed;

    public VulkanSwapchain Swapchain { get; }

    public VulkanFrameSynchronization Synchronization { get; }

    public CubeRenderer Renderer { get; }

    public CubeSwapchainResources(
        VulkanInstance instance,
        PhysicalDevice physicalDevice,
        VulkanLogicalDevice device,
        VulkanSurface surface,
        WindowSize framebufferSize,
        int maximumFramesInFlight,
        string vertexShaderPath,
        string fragmentShaderPath)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(surface);

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
        VulkanFrameSynchronization? synchronization = null;
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

            int framesInFlight = Math.Min(
                maximumFramesInFlight,
                checked((int)swapchain.ImageCount));

            synchronization = new VulkanFrameSynchronization(
                instance,
                device,
                framesInFlight,
                checked((int)swapchain.ImageCount));

            renderer = new CubeRenderer(
                instance,
                device,
                physicalDevice,
                swapchain,
                imageViews,
                vertexShaderPath,
                fragmentShaderPath);
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
                    synchronization?.Dispose();
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

        Synchronization = synchronization ?? throw new InvalidOperationException(
            "Frame synchronization was not initialized.");

        Renderer = renderer ?? throw new InvalidOperationException(
            "Cube renderer was not initialized.");

        _imageInFlightFences = new Fence[checked((int)Swapchain.ImageCount)];
    }

    /// <summary>
    /// Waits for a previous graphics submission using this image.
    /// </summary>
    public void WaitForImage(
        VulkanInstance instance,
        VulkanLogicalDevice device,
        int imageIndex,
        Fence currentFrameFence,
        ulong timeoutNanoseconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Fence previousFence = _imageInFlightFences[imageIndex];

        if (previousFence.Handle != 0
                && previousFence.Handle != currentFrameFence.Handle)
        {
            VulkanQueueSubmission.WaitForFence(
                instance,
                device,
                previousFence,
                timeoutNanoseconds);
        }
    }

    /// <summary>
    /// Records ownership only after successful queue submission.
    /// </summary>
    public void MarkImageSubmitted(int imageIndex, Fence frameFence)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _imageInFlightFences[imageIndex] = frameFence;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            Renderer.Dispose();
        }

        finally
        {
            try
            {
                Synchronization.Dispose();
            }

            finally
            {
                try
                {
                    _imageViews.Dispose();
                }

                finally
                {
                    Swapchain.Dispose();
                }
            }
        }

        _disposed = true;
    }
}
