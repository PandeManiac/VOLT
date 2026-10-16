
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;

namespace Volt.Graphics.Vulkan.Swapchain;

/// <summary>
/// Owns one Vulkan color image view for each swapchain image.
/// </summary>
/// <remarks>
/// The Vulkan instance, logical device, and swapchain must remain
/// alive until all image views have been destroyed.
///
/// The underlying swapchain images are borrowed and must never
/// be destroyed independently.
///
/// GPU work referencing these views must finish before disposal.
/// </remarks>
public sealed unsafe class VulkanSwapchainImageViews : IDisposable
{
    private readonly VulkanInstance _instance;
    private readonly VulkanLogicalDevice _device;
    private readonly VulkanSwapchain _swapchain;
    private readonly ImageView[] _views;

    private bool _disposed;

    /// <summary>
    /// Gets the number of owned image views.
    /// </summary>
    public int Count
    {
        get
        {
            ThrowIfDisposed();
            return _views.Length;
        }
    }

    /// <summary>
    /// Creates a 2D color image view for every swapchain image.
    /// </summary>
    public VulkanSwapchainImageViews(
        VulkanInstance instance,
        VulkanLogicalDevice device,
        VulkanSwapchain swapchain)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(swapchain);

        Vk api = instance.Api;
        Device deviceHandle = device.Handle;

        _ = swapchain.Handle;

        if (swapchain.ImageFormat == Format.Undefined)
        {
            throw new ArgumentException(
                "The swapchain must have a defined image format.",
                nameof(swapchain));
        }

        int imageCount = checked((int)swapchain.ImageCount);

        if (imageCount == 0)
        {
            throw new ArgumentException(
                "The swapchain contains no images.",
                nameof(swapchain));
        }

        ImageView[] views = new ImageView[imageCount];
        int createdCount = 0;

        try
        {
            for (int index = 0; index < imageCount; index++)
            {
                Image image = swapchain.GetImage(index);

                ImageViewCreateInfo createInfo =
                    CreateDescription(
                        image,
                        swapchain.ImageFormat);

                Result result = api.CreateImageView(
                    deviceHandle,
                    in createInfo,
                    null,
                    out ImageView view);

                if (result != Result.Success)
                {
                    throw new InvalidOperationException(
                        $"Creating Vulkan image view {index} " +
                        $"failed: {result}.");
                }

                if (view.Handle == 0)
                {
                    throw new InvalidOperationException(
                        $"Vulkan returned a null handle " +
                        $"for image view {index}.");
                }

                views[createdCount] = view;
                createdCount++;
            }
        }

        catch
        {
            for (int index = createdCount - 1; index >= 0; index--)
            {
                api.DestroyImageView(
                    deviceHandle,
                    views[index],
                    null);
            }

            throw;
        }

        _instance = instance;
        _device = device;
        _swapchain = swapchain;
        _views = views;
    }

    /// <summary>
    /// Gets a borrowed image view owned by this collection.
    /// </summary>
    public ImageView GetView(int index)
    {
        ThrowIfDisposed();
        return _views[index];
    }

    /// <summary>
    /// Destroys all image views in reverse creation order.
    /// </summary>
    /// <remarks>
    /// The caller is responsible for synchronizing outstanding
    /// GPU work before calling Dispose.
    ///
    /// Destroy image views before the owning swapchain.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Vk api = _instance.Api;
        Device deviceHandle = _device.Handle;
        _ = _swapchain.Handle;

        for (int index = _views.Length - 1; index >= 0; index--)
        {
            api.DestroyImageView(
                deviceHandle,
                _views[index],
                null);
        }

        _disposed = true;
    }

    /// <summary>
    /// Builds a single-mip, single-layer 2D color image view
    /// description without accessing native Vulkan APIs.
    /// </summary>
    internal static ImageViewCreateInfo CreateDescription(
        Image image,
        Format format)
    {
        if (image.Handle == 0)
        {
            throw new ArgumentException(
                "A valid Vulkan image handle is required.",
                nameof(image));
        }

        if (format == Format.Undefined)
        {
            throw new ArgumentException(
                "A defined Vulkan image format is required.",
                nameof(format));
        }

        return new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = format,

            Components = new ComponentMapping
            {
                R = ComponentSwizzle.Identity,
                G = ComponentSwizzle.Identity,
                B = ComponentSwizzle.Identity,
                A = ComponentSwizzle.Identity
            },

            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
