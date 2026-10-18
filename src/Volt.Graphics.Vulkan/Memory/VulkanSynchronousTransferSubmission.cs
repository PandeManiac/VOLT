
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
/// through the supplied callback.
///
/// The graphics queue must be externally synchronized against
/// unrelated host-side queue submissions.
///
/// If submission completion cannot be established, command and
/// synchronization resources are retained by the logical device
/// until a later device-idle or device-lost result permits cleanup.
///
/// This helper does not implement asynchronous execution.
/// </remarks>
internal static unsafe class VulkanSynchronousTransferSubmission
{
    private const ulong SubmissionTimeoutNanoseconds =
        5_000_000_000UL;

    /// <summary>
    /// Records and executes one synchronous transfer submission.
    /// </summary>
    internal static void Execute<TState>(
        VulkanInstance instance,
        VulkanLogicalDevice device,
        TState state,
        Action<Vk, CommandBuffer, TState> recordCommands)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(recordCommands);

        device.EnsureTransferAvailable();

        Vk api = instance.Api;
        Device deviceHandle = device.Handle;

        VulkanCommandPool? commandPool = null;
        VulkanCommandBuffers? commandBuffers = null;

        Fence completionFence = default;

        bool submissionAttempted = false;
        bool submissionCompleted = false;

        try
        {
            commandPool = new VulkanCommandPool(
                instance,
                device,
                CommandPoolCreateFlags.TransientBit);

            commandBuffers = new VulkanCommandBuffers(
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

        catch (Exception submissionError)
            when (submissionAttempted && !submissionCompleted)
        {
            Result idleResult;

            try
            {
                idleResult = api.DeviceWaitIdle(deviceHandle);
            }

            catch (Exception idleError)
            {
                device.MarkTransferUnresolved();

                throw new AggregateException(
                    "Vulkan transfer failed, and device-idle " +
                    "completion could not be established. " +
                    "GPU resources have been retained.",
                    submissionError,
                    idleError);
            }

            if (idleResult == Result.ErrorDeviceLost)
            {
                device.MarkDeviceLost();

                throw new InvalidOperationException(
                    "Vulkan transfer failed because the device " +
                    "was lost. The logical device must be recreated.",
                    submissionError);
            }

            if (idleResult != Result.Success)
            {
                device.MarkTransferUnresolved();

                throw new AggregateException(
                    "Vulkan transfer failed, and the fallback " +
                    $"device-idle wait returned {idleResult}. " +
                    "GPU resources have been retained.",
                    submissionError,
                    new InvalidOperationException(
                        $"Device-idle result: {idleResult}."));
            }

            throw;
        }

        finally
        {
            VulkanCommandPool? ownedPool = commandPool;
            VulkanCommandBuffers? ownedBuffers = commandBuffers;
            Fence ownedFence = completionFence;

            device.ReleaseOrDefer(() =>
            {
                try
                {
                    if (ownedFence.Handle != 0)
                    {
                        api.DestroyFence(
                            deviceHandle,
                            ownedFence,
                            null);
                    }
                }

                finally
                {
                    try
                    {
                        ownedBuffers?.Dispose();
                    }

                    finally
                    {
                        ownedPool?.Dispose();
                    }
                }
            });
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
