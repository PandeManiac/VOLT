
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Volt.Graphics.Vulkan.Synchronization;

/// <summary>
/// Owns the synchronization primitives used for Vulkan frame
/// submission and swapchain presentation.
/// </summary>
/// <remarks>
/// Image acquisition semaphores and submission fences are
/// indexed by the frame-in-flight index.
///
/// Render-finished semaphores are indexed by the acquired
/// swapchain image index, not the frame-in-flight index.
///
/// The Vulkan instance and logical device must outlive
/// this collection.
///
/// GPU and presentation operations using these objects must
/// complete before disposal.
/// </remarks>
public sealed unsafe class VulkanFrameSynchronization : IDisposable
{
    private readonly VulkanInstance _instance;
    private readonly VulkanLogicalDevice _device;
    private readonly Vk _api;

    private readonly Semaphore[] _imageAvailableSemaphores;
    private readonly Semaphore[] _renderFinishedSemaphores;
    private readonly Fence[] _inFlightFences;

    private bool _disposed;

    /// <summary>
    /// Gets the number of independently synchronized frame slots.
    /// </summary>
    public int FramesInFlight
    {
        get
        {
            ThrowIfDisposed();
            return _imageAvailableSemaphores.Length;
        }
    }

    /// <summary>
    /// Gets the number of swapchain images supported by this
    /// synchronization collection.
    /// </summary>
    public int SwapchainImageCount
    {
        get
        {
            ThrowIfDisposed();
            return _renderFinishedSemaphores.Length;
        }
    }

    /// <summary>
    /// Creates synchronization resources for a swapchain.
    /// </summary>
    /// <param name="instance">
    /// The Vulkan instance that owns the logical device.
    /// </param>
    /// <param name="device">
    /// The logical device that will create the resources.
    /// </param>
    /// <param name="framesInFlight">
    /// Number of frame slots used for graphics submission.
    /// </param>
    /// <param name="swapchainImageCount">
    /// Actual number of images returned by the swapchain.
    /// </param>
    public VulkanFrameSynchronization(
        VulkanInstance instance,
        VulkanLogicalDevice device,
        int framesInFlight,
        int swapchainImageCount)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(device);

        ValidateCounts(
            framesInFlight,
            swapchainImageCount);

        Vk api = instance.Api;
        Device deviceHandle = device.Handle;

        Semaphore[] imageAvailableSemaphores =
            new Semaphore[framesInFlight];

        Semaphore[] renderFinishedSemaphores =
            new Semaphore[swapchainImageCount];

        Fence[] inFlightFences =
            new Fence[framesInFlight];

        SemaphoreCreateInfo semaphoreDescription =
            CreateSemaphoreDescription();

        FenceCreateInfo fenceDescription =
            CreateFenceDescription();

        try
        {
            for (int index = 0;
                 index < framesInFlight;
                 index++)
            {
                imageAvailableSemaphores[index] =
                    CreateSemaphore(
                        api,
                        deviceHandle,
                        in semaphoreDescription);

                inFlightFences[index] =
                    CreateFence(
                        api,
                        deviceHandle,
                        in fenceDescription);
            }

            for (int index = 0;
                 index < swapchainImageCount;
                 index++)
            {
                renderFinishedSemaphores[index] =
                    CreateSemaphore(
                        api,
                        deviceHandle,
                        in semaphoreDescription);
            }
        }

        catch
        {
            DestroyResources(
                api,
                deviceHandle,
                imageAvailableSemaphores,
                renderFinishedSemaphores,
                inFlightFences);

            throw;
        }

        _instance = instance;
        _device = device;
        _api = api;

        _imageAvailableSemaphores = imageAvailableSemaphores;
        _renderFinishedSemaphores = renderFinishedSemaphores;
        _inFlightFences = inFlightFences;
    }

    /// <summary>
    /// Gets the semaphore used to signal image acquisition
    /// for a frame slot.
    /// </summary>
    public Semaphore GetImageAvailableSemaphore(int frameIndex)
    {
        ThrowIfDisposed();
        return _imageAvailableSemaphores[frameIndex];
    }

    /// <summary>
    /// Gets the semaphore signaled by graphics submission
    /// and waited on by presentation for a swapchain image.
    /// </summary>
    public Semaphore GetRenderFinishedSemaphore(int imageIndex)
    {
        ThrowIfDisposed();
        return _renderFinishedSemaphores[imageIndex];
    }

    /// <summary>
    /// Gets the fence that tracks graphics submission completion
    /// for a frame slot.
    /// </summary>
    public Fence GetInFlightFence(int frameIndex)
    {
        ThrowIfDisposed();
        return _inFlightFences[frameIndex];
    }

    /// <summary>
    /// Destroys all owned synchronization resources.
    /// </summary>
    /// <remarks>
    /// The caller must ensure all relevant GPU and presentation
    /// operations are finished before disposing this collection.
    ///
    /// This method does not implicitly wait for device idle.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _ = _instance.Handle;
        Device deviceHandle = _device.Handle;

        DestroyResources(
            _api,
            deviceHandle,
            _imageAvailableSemaphores,
            _renderFinishedSemaphores,
            _inFlightFences);

        _disposed = true;
    }

    /// <summary>
    /// Creates a description for a binary Vulkan semaphore.
    /// </summary>
    internal static SemaphoreCreateInfo CreateSemaphoreDescription()
    {
        return new SemaphoreCreateInfo
        {
            SType = StructureType.SemaphoreCreateInfo,
            Flags = SemaphoreCreateFlags.None
        };
    }

    /// <summary>
    /// Creates a Vulkan fence description.
    /// </summary>
    /// <remarks>
    /// Frame fences are initially signaled so the first frame
    /// can pass its completion wait without a previous submission.
    /// </remarks>
    internal static FenceCreateInfo CreateFenceDescription(
        bool initiallySignaled = true)
    {
        return new FenceCreateInfo
        {
            SType = StructureType.FenceCreateInfo,
            Flags = initiallySignaled
                ? FenceCreateFlags.SignaledBit
                : FenceCreateFlags.None
        };
    }

    /// <summary>
    /// Validates the synchronization resource counts.
    /// </summary>
    internal static void ValidateCounts(
        int framesInFlight,
        int swapchainImageCount)
    {
        if (framesInFlight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(framesInFlight),
                framesInFlight,
                "At least one frame in flight is required.");
        }

        if (swapchainImageCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(swapchainImageCount),
                swapchainImageCount,
                "At least one swapchain image is required.");
        }
    }

    private static Semaphore CreateSemaphore(
        Vk api,
        Device device,
        in SemaphoreCreateInfo description)
    {
        Result result = api.CreateSemaphore(
            device,
            in description,
            null,
            out Semaphore semaphore);

        if (result != Result.Success)
        {
            throw new InvalidOperationException(
                $"Vulkan semaphore creation failed: {result}.");
        }

        if (semaphore.Handle == 0)
        {
            throw new InvalidOperationException(
                "Vulkan returned a null semaphore handle.");
        }

        return semaphore;
    }

    private static Fence CreateFence(
        Vk api,
        Device device,
        in FenceCreateInfo description)
    {
        Result result = api.CreateFence(
            device,
            in description,
            null,
            out Fence fence);

        if (result != Result.Success)
        {
            throw new InvalidOperationException(
                $"Vulkan fence creation failed: {result}.");
        }

        if (fence.Handle == 0)
        {
            throw new InvalidOperationException(
                "Vulkan returned a null fence handle.");
        }

        return fence;
    }

    private static void DestroyResources(
        Vk api,
        Device device,
        Semaphore[] imageAvailableSemaphores,
        Semaphore[] renderFinishedSemaphores,
        Fence[] inFlightFences)
    {
        for (int index = inFlightFences.Length - 1;
             index >= 0;
             index--)
        {
            Fence fence = inFlightFences[index];

            if (fence.Handle != 0)
            {
                api.DestroyFence(
                    device,
                    fence,
                    null);

                inFlightFences[index] = default;
            }
        }

        for (int index = renderFinishedSemaphores.Length - 1;
             index >= 0;
             index--)
        {
            Semaphore semaphore = renderFinishedSemaphores[index];

            if (semaphore.Handle != 0)
            {
                api.DestroySemaphore(
                    device,
                    semaphore,
                    null);

                renderFinishedSemaphores[index] = default;
            }
        }

        for (int index = imageAvailableSemaphores.Length - 1;
             index >= 0;
             index--)
        {
            Semaphore semaphore = imageAvailableSemaphores[index];

            if (semaphore.Handle != 0)
            {
                api.DestroySemaphore(
                    device,
                    semaphore,
                    null);

                imageAvailableSemaphores[index] = default;
            }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
