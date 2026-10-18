
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using VkBuffer = Silk.NET.Vulkan.Buffer;

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
    private readonly record struct CopyRecording(
        VkBuffer Source,
        VkBuffer Destination,
        ulong ByteCount,
        ulong SourceOffset,
        ulong DestinationOffset);

    private static readonly Action<Vk, CommandBuffer, CopyRecording>
        CopyRecorder = RecordCopy;

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

        device.EnsureTransferAvailable();

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
    /// Submits an asynchronous upload into a newly allocated
    /// device-local Vulkan buffer.
    /// </summary>
    /// <returns>
    /// A ticket that owns staging and destination resources
    /// until GPU completion or disposal.
    /// </returns>
    /// <remarks>
    /// The input span may be reused after this method returns.
    /// GPU completion is not awaited during successful submission.
    ///
    /// The graphics queue requires external host synchronization.
    /// </remarks>
    public static VulkanBufferUploadTicket UploadDeviceLocalAsync<T>(
        VulkanInstance instance,
        VulkanLogicalDevice device,
        PhysicalDevice physicalDevice,
        ReadOnlySpan<T> values,
        BufferUsageFlags destinationUsage)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(device);

        device.EnsureTransferAvailable();

        ulong size = checked(
            (ulong)values.Length * (ulong)sizeof(T));

        ValidateUpload(size, destinationUsage);

        VulkanBuffer? staging = null;
        VulkanBuffer? destination = null;
        VulkanBufferCopyTicket? copyTicket = null;

        try
        {
            staging = new VulkanBuffer(
                instance,
                device,
                physicalDevice,
                size,
                BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.HostVisibleBit,
                MemoryPropertyFlags.HostCoherentBit);

            staging.Write(values);

            destination = new VulkanBuffer(
                instance,
                device,
                physicalDevice,
                size,
                destinationUsage | BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.DeviceLocalBit);

            copyTicket = CopyAsync(
                instance,
                device,
                staging,
                destination,
                size);

            VulkanBufferUploadTicket uploadTicket = new(
                copyTicket,
                staging,
                destination);

            copyTicket = null;
            staging = null;
            destination = null;

            return uploadTicket;
        }

        catch
        {
            try
            {
                copyTicket?.Dispose();
            }

            finally
            {
                try
                {
                    destination?.Dispose();
                }

                finally
                {
                    staging?.Dispose();
                }
            }

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

        CopyRecording recording = new(
            source.Handle,
            destination.Handle,
            byteCount,
            sourceOffset,
            destinationOffset);

        VulkanSynchronousTransferSubmission.Execute(
            instance,
            device,
            recording,
            CopyRecorder);
    }

    /// <summary>
    /// Submits an asynchronous copy between two existing buffers.
    /// </summary>
    /// <returns>
    /// A ticket that tracks completion and owns temporary
    /// command and synchronization resources.
    /// </returns>
    /// <remarks>
    /// The copy uses the graphics queue and returns without
    /// waiting for GPU completion.
    ///
    /// The caller retains ownership of source and destination.
    /// Both buffers must remain alive until the ticket completes,
    /// and must not be accessed incompatibly while the GPU copy
    /// is pending.
    ///
    /// The caller must dispose the returned ticket before
    /// destroying its Vulkan logical device.
    ///
    /// Host-side submissions to the graphics queue require
    /// external synchronization.
    /// </remarks>
    public static VulkanBufferCopyTicket CopyAsync(
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

        device.EnsureTransferAvailable();

        CopyRecording recording = new(
            source.Handle,
            destination.Handle,
            byteCount,
            sourceOffset,
            destinationOffset);

        return VulkanBufferCopyTicket.Submit(
            instance,
            device,
            recording,
            CopyRecorder);
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

    private static void RecordCopy(
        Vk api,
        CommandBuffer commandBuffer,
        CopyRecording recording)
    {
        BufferCopy region = new()
        {
            SrcOffset = recording.SourceOffset,
            DstOffset = recording.DestinationOffset,
            Size = recording.ByteCount
        };

        api.CmdCopyBuffer(
            commandBuffer,
            recording.Source,
            recording.Destination,
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
            Buffer = recording.Destination,
            Offset = recording.DestinationOffset,
            Size = recording.ByteCount
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
    }
}
