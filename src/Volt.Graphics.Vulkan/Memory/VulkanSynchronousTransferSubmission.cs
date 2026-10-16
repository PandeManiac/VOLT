
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Commands;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;

namespace Volt.Graphics.Vulkan.Memory;

/// <summary>
/// Owns the command and synchronization resources required
/// for one synchronous Vulkan transfer submission.
/// </summary>
/// <remarks>
/// The caller records application-specific transfer commands
/// and memory dependencies through the supplied callback.
///
/// This helper creates a transient command pool, records one
/// primary command buffer, submits it to the graphics queue,
/// and waits for completion before returning.
///
/// The graphics queue must be externally synchronized against
/// unrelated host-side queue submissions.
///
/// If submission completion is not established, this helper
/// attempts to drain the device before releasing resources.
///
/// A failed fallback device-idle wait does not establish safe
/// resource reclamation. Full recovery from that state requires
/// additional device-lifetime management.
///
/// This helper is synchronous and does not track in-flight work.
/// </remarks>
internal static unsafe class VulkanSynchronousTransferSubmission
{
    private const ulong SubmissionTimeoutNanoseconds =
        5_000_000_000UL;

    /// <summary>
    /// Records and executes one synchronous transfer submission.
    /// </summary>
    /// <typeparam name="TState">
    /// Caller-defined command-recording context.
    /// </typeparam>
    /// <remarks>
    /// The recording callback executes synchronously while the
    /// command buffer is in the recording state.
    ///
    /// Successful return establishes completion of the submitted
    /// command buffer.
    ///
    /// The caller remains responsible for synchronizing unrelated
    /// GPU accesses to resources used by the transfer.
    /// </remarks>
    internal static void Execute<TState>(
        VulkanInstance instance,
        VulkanLogicalDevice device,
        TState state,
        Action<Vk, CommandBuffer, TState> recordCommands)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(recordCommands);

        Vk api = instance.Api;
        Device deviceHandle = device.Handle;

        using VulkanCommandPool commandPool = new(
            instance,
            device,
            CommandPoolCreateFlags.TransientBit);

        using VulkanCommandBuffers commandBuffers = new(
            instance,
            device,
            commandPool,
            1);

        CommandBuffer commandBuffer =
            commandBuffers.GetBuffer(0);

        VulkanCommandRecording.Begin(
            instance,
            commandBuffer);

        recordCommands(
            api,
            commandBuffer,
            state);

        VulkanCommandRecording.End(
            instance,
            commandBuffer);

        FenceCreateInfo fenceDescription = new()
        {
            SType = StructureType.FenceCreateInfo,
            Flags = FenceCreateFlags.None
        };

        Fence completionFence = default;

        bool submissionAttempted = false;
        bool submissionCompleted = false;

        try
        {
            EnsureSuccess(
                api.CreateFence(
                    deviceHandle,
                    in fenceDescription,
                    null,
                    out completionFence),
                "Create transfer submission fence");

            if (completionFence.Handle == 0)
            {
                throw new InvalidOperationException(
                    "Vulkan returned a null transfer fence.");
            }

            submissionAttempted = true;

            VulkanQueueSubmission.Submit(
                instance,
                device,
                commandBuffer,
                completionFence);

            VulkanQueueSubmission.WaitForFence(
                instance,
                device,
                completionFence,
                SubmissionTimeoutNanoseconds);

            submissionCompleted = true;
        }

        finally
        {
            try
            {
                if (submissionAttempted && !submissionCompleted)
                {
                    // A failed submission or wait does not prove
                    // that the GPU has stopped using these resources.
                    // Attempt to drain the device before cleanup.
                    device.WaitIdle();
                }
            }

            finally
            {
                if (completionFence.Handle != 0)
                {
                    api.DestroyFence(
                        deviceHandle,
                        completionFence,
                        null);
                }
            }
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
