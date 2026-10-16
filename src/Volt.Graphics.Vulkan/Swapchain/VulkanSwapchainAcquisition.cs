
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Volt.Graphics.Vulkan.Swapchain;

/// <summary>
/// Describes the outcome of a Vulkan swapchain image acquisition.
/// </summary>
public enum VulkanImageAcquisitionStatus
{
    Success,
    Suboptimal,
    OutOfDate,
    Timeout,
    NotReady
}

/// <summary>
/// Represents a Vulkan swapchain image acquisition result.
/// </summary>
public readonly record struct VulkanImageAcquisitionResult(
    VulkanImageAcquisitionStatus Status,
    uint? ImageIndex)
{
    public bool HasImage => ImageIndex.HasValue;

    public bool RequiresRecreation =>
        Status == VulkanImageAcquisitionStatus.Suboptimal
        || Status == VulkanImageAcquisitionStatus.OutOfDate;
}

/// <summary>
/// Provides Vulkan swapchain image acquisition operations.
/// </summary>
/// <remarks>
/// A successful acquisition transfers temporary use of an image
/// from the presentation engine to the application.
///
/// The application must wait on the acquisition semaphore
/// before accessing the image.
///
/// An acquired image must be presented or otherwise released
/// before its acquisition can be considered complete.
///
/// The caller must externally synchronize swapchain and
/// semaphore access.
/// </remarks>
public static class VulkanSwapchainAcquisition
{
    /// <summary>
    /// Attempts to acquire the next available swapchain image.
    /// </summary>
    /// <remarks>
    /// The image-available semaphore must be an unsignaled
    /// binary semaphore belonging to the supplied device.
    ///
    /// Success and Suboptimal both acquire an image.
    ///
    /// If Suboptimal is returned, the acquired image must
    /// still be handled before recreating the swapchain.
    ///
    /// OutOfDate, Timeout, and NotReady do not acquire an image.
    /// </remarks>
    public static VulkanImageAcquisitionResult Acquire(
        VulkanInstance instance,
        VulkanLogicalDevice device,
        VulkanSwapchain swapchain,
        Semaphore imageAvailableSemaphore,
        ulong timeoutNanoseconds = ulong.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(swapchain);

        if (imageAvailableSemaphore.Handle == 0)
        {
            throw new ArgumentException(
                "A valid Vulkan image-available semaphore is required.",
                nameof(imageAvailableSemaphore));
        }

        Vk api = instance.Api;
        Silk.NET.Vulkan.Instance instanceHandle = instance.Handle;
        Device deviceHandle = device.Handle;
        SwapchainKHR swapchainHandle = swapchain.Handle;
        uint imageCount = swapchain.ImageCount;

        if (!api.TryGetDeviceExtension<KhrSwapchain>(
            instanceHandle,
            deviceHandle,
            out KhrSwapchain? extension)
            || extension is null)
        {
            throw new NotSupportedException(
                "VK_KHR_swapchain is unavailable for the " +
                "selected Vulkan device.");
        }

        using (extension)
        {
            uint imageIndex = 0;

            Result result = extension.AcquireNextImage(
                deviceHandle,
                swapchainHandle,
                timeoutNanoseconds,
                imageAvailableSemaphore,
                default,
                ref imageIndex);

            return InterpretResult(
                result,
                imageIndex,
                imageCount);
        }
    }

    /// <summary>
    /// Converts a native Vulkan acquisition result into
    /// a managed result with explicit image ownership status.
    /// </summary>
    internal static VulkanImageAcquisitionResult InterpretResult(
        Result result,
        uint imageIndex,
        uint imageCount)
    {
        switch (result)
        {
            case Result.Success:
            case Result.SuboptimalKhr:
                {
                    if (imageIndex >= imageCount)
                    {
                        throw new InvalidOperationException(
                            $"Vulkan acquired image index {imageIndex}, " +
                            $"but the swapchain contains only " +
                            $"{imageCount} images.");
                    }

                    VulkanImageAcquisitionStatus status =
                        result == Result.Success
                            ? VulkanImageAcquisitionStatus.Success
                            : VulkanImageAcquisitionStatus.Suboptimal;

                    return new VulkanImageAcquisitionResult(
                        status,
                        imageIndex);
                }

            case Result.ErrorOutOfDateKhr:
                {
                    return new VulkanImageAcquisitionResult(
                        VulkanImageAcquisitionStatus.OutOfDate,
                        null);
                }

            case Result.Timeout:
                {
                    return new VulkanImageAcquisitionResult(
                        VulkanImageAcquisitionStatus.Timeout,
                        null);
                }

            case Result.NotReady:
                {
                    return new VulkanImageAcquisitionResult(
                        VulkanImageAcquisitionStatus.NotReady,
                        null);
                }

            default:
                {
                    throw new InvalidOperationException(
                        $"Vulkan swapchain image acquisition failed: " +
                        $"{result}.");
                }
        }
    }
}
