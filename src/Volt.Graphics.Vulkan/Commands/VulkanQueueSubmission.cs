using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Volt.Graphics.Vulkan.Commands;

/// <summary>
/// Provides Vulkan graphics queue submission and fence
/// synchronization operations.
/// </summary>
/// <remarks>
/// The caller must externally synchronize host access to the
/// graphics queue and command buffers.
///
/// Submitted command buffers must be executable and must not
/// be reset or freed while their execution is pending.
///
/// Submission fences must be unsignaled and must not already
/// be associated with pending GPU work.
///
/// This class does not reset fences automatically or track
/// ownership of command buffers and synchronization objects.
/// </remarks>
public static unsafe class VulkanQueueSubmission
{
    /// <summary>
    /// Submits one executable primary command buffer without
    /// semaphore dependencies.
    /// </summary>
    public static void Submit(
        VulkanInstance instance,
        VulkanLogicalDevice device,
        CommandBuffer buffer,
        Fence completionFence)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(device);

        ValidateBuffer(buffer);
        ValidateFence(completionFence);

        CommandBuffer submittedBuffer = buffer;

        SubmitInfo description =
            CreateDescription(&submittedBuffer);

        Result result = instance.Api.QueueSubmit(
            device.GraphicsQueue,
            1,
            in description,
            completionFence);

        EnsureSuccess(
            result,
            "Submit Vulkan graphics commands");
    }

    /// <summary>
    /// Submits one executable primary command buffer, waiting
    /// for image acquisition and signaling render completion.
    /// </summary>
    /// <remarks>
    /// The wait semaphore must be signaled by a previously
    /// submitted operation, such as swapchain image acquisition.
    ///
    /// The signal semaphore must be unsignaled when its signal
    /// operation executes, and it must not still be in use by
    /// presentation from an earlier frame.
    ///
    /// The wait stage mask must include stages necessary to
    /// synchronize all operations accessing the acquired image.
    /// AllCommandsBit is a conservative option for an initial
    /// Vulkan presentation smoke test.
    ///
    /// Successful return means the queue accepted the work;
    /// it does not mean GPU execution has completed.
    /// </remarks>
    public static void Submit(
        VulkanInstance instance,
        VulkanLogicalDevice device,
        CommandBuffer buffer,
        Semaphore waitSemaphore,
        PipelineStageFlags waitStageMask,
        Semaphore signalSemaphore,
        Fence completionFence)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(device);

        ValidateBuffer(buffer);
        ValidateFence(completionFence);

        ValidateSynchronizedParameters(
            waitSemaphore,
            waitStageMask,
            signalSemaphore);

        CommandBuffer submittedBuffer = buffer;
        Semaphore submittedWaitSemaphore = waitSemaphore;
        PipelineStageFlags submittedWaitStageMask = waitStageMask;
        Semaphore submittedSignalSemaphore = signalSemaphore;

        SubmitInfo description =
            CreateSynchronizedDescription(
                &submittedBuffer,
                &submittedWaitSemaphore,
                &submittedWaitStageMask,
                &submittedSignalSemaphore);

        Result result = instance.Api.QueueSubmit(
            device.GraphicsQueue,
            1,
            in description,
            completionFence);

        EnsureSuccess(
            result,
            "Submit synchronized Vulkan graphics commands");
    }

    /// <summary>
    /// Waits for a graphics submission fence.
    /// </summary>
    /// <remarks>
    /// A timeout does not cancel work pending on the GPU.
    /// Resources associated with an unfinished submission
    /// must not be destroyed or reused.
    /// </remarks>
    public static void WaitForFence(
        VulkanInstance instance,
        VulkanLogicalDevice device,
        Fence fence,
        ulong timeoutNanoseconds = ulong.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(device);

        ValidateFence(fence);

        Result result = instance.Api.WaitForFences(
            device.Handle,
            1,
            in fence,
            Vk.True,
            timeoutNanoseconds);

        if (result == Result.Timeout)
        {
            throw new TimeoutException(
                "Timed out waiting for Vulkan graphics " +
                "submission to complete.");
        }

        EnsureSuccess(
            result,
            "Wait for Vulkan submission fence");
    }

    /// <summary>
    /// Resets a fence for a future submission.
    /// </summary>
    /// <remarks>
    /// The caller must ensure no pending work is associated
    /// with this fence. Reset it only when a submission that
    /// will signal it is ready to be issued.
    /// </remarks>
    public static void ResetFence(
        VulkanInstance instance,
        VulkanLogicalDevice device,
        Fence fence)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(device);

        ValidateFence(fence);

        Result result = instance.Api.ResetFences(
            device.Handle,
            1,
            in fence);

        EnsureSuccess(
            result,
            "Reset Vulkan submission fence");
    }

    /// <summary>
    /// Creates a submission description without semaphore
    /// dependencies.
    /// </summary>
    /// <remarks>
    /// The command buffer pointer must remain valid until
    /// vkQueueSubmit returns. Do not retain this structure
    /// beyond the lifetime of its pointed-to storage.
    /// </remarks>
    internal static SubmitInfo CreateDescription(
        CommandBuffer* commandBuffer)
    {
        if (commandBuffer == null ||
            commandBuffer->Handle == 0)
        {
            throw new ArgumentException(
                "A valid Vulkan command buffer pointer is required.",
                nameof(commandBuffer));
        }

        return new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = commandBuffer,
            WaitSemaphoreCount = 0,
            SignalSemaphoreCount = 0
        };
    }

    /// <summary>
    /// Creates a submission description with one wait and
    /// one signal binary semaphore.
    /// </summary>
    /// <remarks>
    /// Every supplied pointer must remain valid until
    /// vkQueueSubmit returns. Do not retain this structure
    /// beyond the lifetime of its pointed-to storage.
    /// </remarks>
    internal static SubmitInfo CreateSynchronizedDescription(
        CommandBuffer* commandBuffer,
        Semaphore* waitSemaphore,
        PipelineStageFlags* waitStageMask,
        Semaphore* signalSemaphore)
    {
        SubmitInfo description =
            CreateDescription(commandBuffer);

        if (waitSemaphore == null)
        {
            throw new ArgumentException(
                "A valid wait semaphore pointer is required.",
                nameof(waitSemaphore));
        }

        if (waitStageMask == null)
        {
            throw new ArgumentException(
                "A valid wait stage mask pointer is required.",
                nameof(waitStageMask));
        }

        if (signalSemaphore == null)
        {
            throw new ArgumentException(
                "A valid signal semaphore pointer is required.",
                nameof(signalSemaphore));
        }

        ValidateSynchronizedParameters(
            *waitSemaphore,
            *waitStageMask,
            *signalSemaphore);

        description.WaitSemaphoreCount = 1;
        description.PWaitSemaphores = waitSemaphore;
        description.PWaitDstStageMask = waitStageMask;
        description.SignalSemaphoreCount = 1;
        description.PSignalSemaphores = signalSemaphore;

        return description;
    }

    /// <summary>
    /// Validates parameters for a single binary-semaphore
    /// wait and signal submission.
    /// </summary>
    internal static void ValidateSynchronizedParameters(
        Semaphore waitSemaphore,
        PipelineStageFlags waitStageMask,
        Semaphore signalSemaphore)
    {
        if (waitSemaphore.Handle == 0)
        {
            throw new ArgumentException(
                "A valid Vulkan wait semaphore is required.",
                nameof(waitSemaphore));
        }

        if (waitStageMask == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(waitStageMask),
                waitStageMask,
                "The semaphore wait stage mask cannot be empty.");
        }

        if (signalSemaphore.Handle == 0)
        {
            throw new ArgumentException(
                "A valid Vulkan signal semaphore is required.",
                nameof(signalSemaphore));
        }

        if (waitSemaphore.Handle == signalSemaphore.Handle)
        {
            throw new ArgumentException(
                "The wait and signal semaphores must be different.",
                nameof(signalSemaphore));
        }
    }

    private static void ValidateBuffer(CommandBuffer buffer)
    {
        if (buffer.Handle == 0)
        {
            throw new ArgumentException(
                "A valid Vulkan command buffer handle is required.",
                nameof(buffer));
        }
    }

    private static void ValidateFence(Fence fence)
    {
        if (fence.Handle == 0)
        {
            throw new ArgumentException(
                "A valid Vulkan fence handle is required.",
                nameof(fence));
        }
    }

    private static void EnsureSuccess(
        Result result,
        string operation)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException(
                $"{operation} failed: {result}.");
        }
    }
}
