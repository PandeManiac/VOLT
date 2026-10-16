
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;

namespace Volt.Graphics.Vulkan.Commands;

/// <summary>
/// Provides basic Vulkan graphics queue submission and
/// fence synchronization operations.
/// </summary>
/// <remarks>
/// The caller must externally synchronize host access to
/// the graphics queue and command buffers.
///
/// Submitted command buffers must be executable and must
/// not be reset or freed while their execution is pending.
///
/// A submission fence must be unsignaled and must not
/// already be associated with pending GPU work.
///
/// This initial implementation does not support semaphore
/// waits or signals.
/// </remarks>
public static unsafe class VulkanQueueSubmission
{
    /// <summary>
    /// Submits one executable primary command buffer to
    /// the logical device's graphics queue.
    /// </summary>
    /// <remarks>
    /// The completion fence must be unsignaled before this
    /// method is called.
    ///
    /// A successful return means submission was accepted,
    /// not that GPU execution has completed.
    /// </remarks>
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

        Vk api = instance.Api;

        // Keep this command buffer handle on the stack
        // for the duration of the native submission call.
        CommandBuffer submittedBuffer = buffer;

        SubmitInfo description =
            CreateDescription(&submittedBuffer);

        Result result = api.QueueSubmit(
            device.GraphicsQueue,
            1,
            in description,
            completionFence);

        EnsureSuccess(
            result,
            "Submit Vulkan graphics commands");
    }

    /// <summary>
    /// Waits for a single graphics submission fence.
    /// </summary>
    /// <param name="timeoutNanoseconds">
    /// Maximum Vulkan wait duration in nanoseconds.
    /// ulong.MaxValue requests an indefinite wait.
    /// </param>
    /// <remarks>
    /// A timeout does not cancel pending GPU work.
    ///
    /// If the wait times out, the caller must still
    /// synchronize outstanding GPU work before destroying
    /// or reusing the associated resources.
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
    /// Resets a signaled fence for a future submission.
    /// </summary>
    /// <remarks>
    /// The caller must ensure that the fence is no longer
    /// associated with pending GPU work.
    ///
    /// Reset the fence only when a submission that will
    /// signal it is ready to be issued.
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
    /// Creates a managed description for submitting one
    /// primary command buffer without semaphores.
    /// </summary>
    /// <remarks>
    /// The command buffer pointer must remain valid until
    /// vkQueueSubmit returns.
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
