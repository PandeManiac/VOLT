using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Volt.Graphics.Vulkan.Swapchain;

/// <summary>
/// Describes the outcome of a Vulkan swapchain presentation request.
/// </summary>
public enum VulkanSwapchainPresentationStatus
{
    Success,
    Suboptimal,
    OutOfDate
}

/// <summary>
/// Represents a Vulkan swapchain presentation result.
/// </summary>
public readonly record struct VulkanSwapchainPresentationResult(
    VulkanSwapchainPresentationStatus Status)
{
    /// <summary>
    /// Gets whether the presentation request was accepted by Vulkan.
    /// This does not mean that the image has already appeared on screen.
    /// </summary>
    public bool WasAccepted =>
        Status == VulkanSwapchainPresentationStatus.Success
        || Status == VulkanSwapchainPresentationStatus.Suboptimal;

    /// <summary>
    /// Gets whether the swapchain should be recreated.
    /// </summary>
    public bool RequiresRecreation =>
        Status == VulkanSwapchainPresentationStatus.Suboptimal
        || Status == VulkanSwapchainPresentationStatus.OutOfDate;
}

/// <summary>
/// Provides Vulkan swapchain presentation operations.
/// </summary>
/// <remarks>
/// The image must previously have been acquired from the swapchain.
/// The caller must record and submit the appropriate image-layout
/// transitions before requesting presentation.
///
/// The supplied wait semaphore must be signaled by GPU work that
/// finishes rendering the image. A successful queue presentation
/// request does not imply that presentation has completed.
///
/// Host access to the presentation queue must be externally
/// synchronized. The instance, device, swapchain, and semaphore
/// must remain valid for the required native operations.
/// </remarks>
public static unsafe class VulkanSwapchainPresentation
{
    /// <summary>
    /// Queues one acquired swapchain image for presentation.
    /// </summary>
    /// <remarks>
    /// The wait semaphore must be a valid binary semaphore that
    /// will be signaled before presentation uses the image.
    ///
    /// A Suboptimal result indicates that presentation was accepted
    /// but swapchain recreation is advisable. An OutOfDate result
    /// requires swapchain recreation.
    ///
    /// Do not reuse or destroy a presentation-wait semaphore solely
    /// because the graphics submission fence has signaled.
    /// </remarks>
    public static VulkanSwapchainPresentationResult Present(
        VulkanInstance instance,
        VulkanLogicalDevice device,
        VulkanSwapchain swapchain,
        uint imageIndex,
        Semaphore renderFinishedSemaphore,
        Fence presentationFence = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(swapchain);

        SwapchainKHR swapchainHandle = swapchain.Handle;

        ValidateParameters(
            swapchainHandle,
            imageIndex,
            swapchain.ImageCount,
            renderFinishedSemaphore);

        if (presentationFence.Handle != 0
                && !device.SwapchainMaintenance1Enabled)
        {
            throw new NotSupportedException(
                "Presentation fences require the swapchainMaintenance1 " +
                "feature to be enabled on the logical device.");
        }

        Vk api = instance.Api;
        Silk.NET.Vulkan.Instance instanceHandle = instance.Handle;
        Device deviceHandle = device.Handle;

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
            uint nativeImageIndex = imageIndex;
            Semaphore nativeWaitSemaphore = renderFinishedSemaphore;

            PresentInfoKHR description = CreateDescription(
                &swapchainHandle,
                &nativeImageIndex,
                swapchain.ImageCount,
                &nativeWaitSemaphore);

            Fence nativePresentationFence = presentationFence;
            SwapchainPresentFenceInfoEXT fenceDescription = default;

            if (nativePresentationFence.Handle != 0)
            {
                fenceDescription = CreatePresentFenceDescription(
                    &nativePresentationFence);

                description.PNext = &fenceDescription;
            }

            Result result = extension.QueuePresent(
                device.GraphicsQueue,
                in description);

            return InterpretResult(result);
        }
    }

    /// <summary>
    /// Creates the native description for presenting one image,
    /// waiting on one render-finished semaphore.
    /// </summary>
    /// <remarks>
    /// All pointer targets must remain valid until vkQueuePresentKHR
    /// returns. Do not store or reuse the returned description.
    /// </remarks>
    internal static PresentInfoKHR CreateDescription(
        SwapchainKHR* swapchain,
        uint* imageIndex,
        uint imageCount,
        Semaphore* waitSemaphore)
    {
        if (swapchain == null)
        {
            throw new ArgumentException(
                "A valid swapchain pointer is required.",
                nameof(swapchain));
        }

        if (imageIndex == null)
        {
            throw new ArgumentException(
                "A valid image index pointer is required.",
                nameof(imageIndex));
        }

        if (waitSemaphore == null)
        {
            throw new ArgumentException(
                "A valid wait semaphore pointer is required.",
                nameof(waitSemaphore));
        }

        ValidateParameters(
            *swapchain,
            *imageIndex,
            imageCount,
            *waitSemaphore);

        return new PresentInfoKHR
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = waitSemaphore,
            SwapchainCount = 1,
            PSwapchains = swapchain,
            PImageIndices = imageIndex
        };
    }

    /// <summary>
    /// Validates the handles and image index for presentation.
    /// </summary>
    internal static void ValidateParameters(
        SwapchainKHR swapchain,
        uint imageIndex,
        uint imageCount,
        Semaphore waitSemaphore)
    {
        if (swapchain.Handle == 0)
        {
            throw new ArgumentException(
                "A valid Vulkan swapchain handle is required.",
                nameof(swapchain));
        }

        if (imageIndex >= imageCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(imageIndex),
                imageIndex,
                $"Image index must be smaller than {imageCount}.");
        }

        if (waitSemaphore.Handle == 0)
        {
            throw new ArgumentException(
                "A valid Vulkan render-finished semaphore is required.",
                nameof(waitSemaphore));
        }
    }

    /// <summary>
    /// Converts the native Vulkan result to a managed presentation
    /// outcome, retaining expected swapchain-recreation signals.
    /// </summary>
    internal static VulkanSwapchainPresentationResult InterpretResult(
        Result result)
    {
        return result switch
        {
            Result.Success => new VulkanSwapchainPresentationResult(
                VulkanSwapchainPresentationStatus.Success),

            Result.SuboptimalKhr => new VulkanSwapchainPresentationResult(
                VulkanSwapchainPresentationStatus.Suboptimal),

            Result.ErrorOutOfDateKhr => new VulkanSwapchainPresentationResult(
                VulkanSwapchainPresentationStatus.OutOfDate),

            _ => throw new InvalidOperationException(
                $"Vulkan swapchain presentation failed: {result}.")
        };
    }

    /// <summary>
    /// Constructs the optional presentation-fence extension structure.
    /// </summary>
    /// <remarks>
    /// The returned structure borrows its fence pointer. Both objects
    /// must remain alive until vkQueuePresentKHR returns.
    /// </remarks>
    internal static SwapchainPresentFenceInfoEXT
        CreatePresentFenceDescription(Fence* presentationFence)
    {
        if (presentationFence == null
                || presentationFence->Handle == 0)
        {
            throw new ArgumentException(
                "A valid presentation fence is required.",
                nameof(presentationFence));
        }

        return new SwapchainPresentFenceInfoEXT
        {
            SType = StructureType.SwapchainPresentFenceInfoKhr,
            SwapchainCount = 1,
            PFences = presentationFence
        };
    }
}
