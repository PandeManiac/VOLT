
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Commands;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;

namespace Volt.Graphics.Vulkan.Memory;

/// <summary>
/// Provides synchronous transfers between Vulkan buffers.
/// </summary>
/// <remarks>
/// UploadDeviceLocal uses a temporary host-visible staging buffer
/// and creates a destination allocation requiring device-local memory.
///
/// Copy records a buffer copy and a transfer-to-memory dependency
/// that includes subsequent device accesses and host reads.
/// It submits on the logical device's graphics queue and waits for
/// completion before returning.
///
/// This is a synchronous initialization path, not a streaming uploader.
/// It must not race with other GPU operations that access the buffers.
///
/// Source and destination buffers must belong to the supplied device.
/// Queue submissions must be externally synchronized by the caller.
/// </remarks>
public static unsafe class VulkanBufferTransfer
{
    private const ulong SubmissionTimeoutNanoseconds =
        5_000_000_000UL;

    /// <summary>
    /// Uploads unmanaged values into a newly allocated device-local
    /// buffer and returns ownership of that buffer to the caller.
    /// </summary>
    /// <remarks>
    /// The destination receives TransferDstBit in addition to
    /// the supplied usage flags.
    ///
    /// The temporary staging buffer is destroyed only after the
    /// graphics queue has completed the copy.
    /// </remarks>
    public static VulkanBuffer UploadDeviceLocal<T>(
        VulkanInstance instance,
        VulkanLogicalDevice device,
        PhysicalDevice physicalDevice,
        ReadOnlySpan<T> values,
        BufferUsageFlags destinationUsage)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(device);

        ulong size = checked(
            (ulong)values.Length * (ulong)sizeof(T));

        ValidateUpload(size, destinationUsage);

        using VulkanBuffer staging = new(
            instance,
            device,
            physicalDevice,
            size,
            BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit,
            MemoryPropertyFlags.HostCoherentBit);

        staging.Write(values);

        VulkanBuffer destination = new(
            instance,
            device,
            physicalDevice,
            size,
            destinationUsage | BufferUsageFlags.TransferDstBit,
            MemoryPropertyFlags.DeviceLocalBit);

        try
        {
            Copy(
                instance,
                device,
                staging,
                destination,
                size);

            return destination;
        }

        catch
        {
            destination.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Copies bytes between two existing Vulkan buffers.
    /// </summary>
    /// <remarks>
    /// Both buffers must belong to the same supplied logical device.
    ///
    /// The source must support TransferSrcBit. The destination must
    /// support TransferDstBit.
    ///
    /// The copy is submitted to the graphics queue and completed
    /// before this method returns.
    ///
    /// An explicit buffer memory barrier makes transfer writes
    /// available and visible to subsequent device-memory accesses
    /// and host reads. Host access must still wait for completion.
    ///
    /// No synchronization is provided against unrelated pending
    /// GPU work that may already be using either buffer.
    /// </remarks>
    public static void Copy(
        VulkanInstance instance,
        VulkanLogicalDevice device,
        VulkanBuffer source,
        VulkanBuffer destination,
        ulong byteCount,
        ulong sourceOffset = 0,
        ulong destinationOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        if (ReferenceEquals(source, destination))
        {
            throw new ArgumentException(
                "This transfer operation requires distinct buffers.",
                nameof(destination));
        }

        ValidateCopyRegion(
            source.Size,
            destination.Size,
            source.Usage,
            destination.Usage,
            byteCount,
            sourceOffset,
            destinationOffset);

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

        BufferCopy region = new()
        {
            SrcOffset = sourceOffset,
            DstOffset = destinationOffset,
            Size = byteCount
        };

        api.CmdCopyBuffer(
            commandBuffer,
            source.Handle,
            destination.Handle,
            1,
            in region);

        // Transfer writes become available to subsequent device
        // accesses and to the host memory domain. The host must
        // still wait for submission completion before reading.
        BufferMemoryBarrier memoryBarrier = new()
        {
            SType = StructureType.BufferMemoryBarrier,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask =
                AccessFlags.MemoryReadBit |
                AccessFlags.MemoryWriteBit |
                AccessFlags.HostReadBit,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Buffer = destination.Handle,
            Offset = destinationOffset,
            Size = byteCount
        };

        api.CmdPipelineBarrier(
            commandBuffer,
            PipelineStageFlags.TransferBit,
            PipelineStageFlags.AllCommandsBit |
                PipelineStageFlags.HostBit,
            DependencyFlags.None,
            0,
            null,
            1,
            &memoryBarrier,
            0,
            null);

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
                out Fence completionFence),
            "Create buffer transfer fence");

        if (completionFence.Handle == 0)
        {
            throw new InvalidOperationException(
                "Vulkan returned a null transfer fence.");
        }

        bool submissionAttempted = false;
        bool submissionCompleted = false;

        try
        {
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
                    // A failed wait or submission does not prove
                    // that the command buffer is no longer pending.
                    // Drain the device before releasing its resources.
                    device.WaitIdle();
                }
            }

            finally
            {
                api.DestroyFence(
                    deviceHandle,
                    completionFence,
                    null);
            }
        }
    }

    /// <summary>
    /// Validates the parameters for creating a device-local upload.
    /// </summary>
    internal static void ValidateUpload(
        ulong byteCount,
        BufferUsageFlags destinationUsage)
    {
        if (byteCount == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(byteCount),
                "An upload must contain at least one byte.");
        }

        if (destinationUsage == BufferUsageFlags.None)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destinationUsage),
                "Destination buffer usage cannot be empty.");
        }
    }

    /// <summary>
    /// Validates a Vulkan buffer-copy region without calling Vulkan.
    /// </summary>
    internal static void ValidateCopyRegion(
        ulong sourceSize,
        ulong destinationSize,
        BufferUsageFlags sourceUsage,
        BufferUsageFlags destinationUsage,
        ulong byteCount,
        ulong sourceOffset = 0,
        ulong destinationOffset = 0)
    {
        if ((sourceUsage & BufferUsageFlags.TransferSrcBit) == 0)
        {
            throw new ArgumentException(
                "The source buffer requires TransferSrcBit.",
                nameof(sourceUsage));
        }

        if ((destinationUsage & BufferUsageFlags.TransferDstBit) == 0)
        {
            throw new ArgumentException(
                "The destination buffer requires TransferDstBit.",
                nameof(destinationUsage));
        }

        if (byteCount == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(byteCount),
                "A buffer copy must contain at least one byte.");
        }

        if (sourceOffset >= sourceSize ||
            byteCount > sourceSize - sourceOffset)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceOffset),
                "The source copy region exceeds the buffer.");
        }

        if (destinationOffset >= destinationSize ||
            byteCount > destinationSize - destinationOffset)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destinationOffset),
                "The destination copy region exceeds the buffer.");
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
