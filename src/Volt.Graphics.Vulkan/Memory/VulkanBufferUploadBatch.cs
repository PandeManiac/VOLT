
using System.Buffers;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using VkBuffer = Silk.NET.Vulkan.Buffer;

namespace Volt.Graphics.Vulkan.Memory;

/// <summary>
/// Collects multiple buffer uploads into a single synchronous
/// Vulkan graphics queue submission.
/// </summary>
/// <remarks>
/// Application data is copied into managed staging storage when
/// added to the batch. Each submitted item receives its own
/// device-local destination buffer.
///
/// Buffer copies support arbitrary positive byte counts without
/// additional staging alignment or padding. Applications remain
/// responsible for alignment required by their buffer consumers.
///
/// The default submission path creates temporary staging storage.
/// An optional VulkanStagingBufferCache enables staging allocation
/// reuse across completed batches.
///
/// This class is a one-shot upload builder. It is not thread-safe,
/// does not synchronize unrelated GPU work, and does not
/// implement asynchronous transfers or device-memory suballocation.
///
/// The caller owns every destination buffer returned by Submit.
/// </remarks>
public sealed unsafe class VulkanBufferUploadBatch
{
    private readonly record struct UploadRequest(
        ulong StagingOffset,
        ulong ByteCount,
        BufferUsageFlags DestinationUsage);

    private readonly record struct BatchCopyRecording(
        VulkanBufferUploadBatch Batch,
        VkBuffer Staging,
        IReadOnlyList<VulkanBuffer> Destinations);

    private static readonly Action<Vk, CommandBuffer, BatchCopyRecording>
        CopyRecorder = RecordCopies;

    private readonly ArrayBufferWriter<byte> _stagingData = new();
    private readonly List<UploadRequest> _requests = [];

    private bool _submitted;

    /// <summary>
    /// Gets the number of queued uploads.
    /// </summary>
    public int UploadCount => _requests.Count;

    /// <summary>
    /// Gets the total number of staged bytes.
    /// </summary>
    public ulong StagingByteCount =>
        checked((ulong)_stagingData.WrittenCount);

    /// <summary>
    /// Adds unmanaged data to the upload batch.
    /// </summary>
    /// <returns>
    /// The index of the destination buffer in the array returned
    /// by Submit.
    /// </returns>
    public int Add<T>(
        ReadOnlySpan<T> values,
        BufferUsageFlags destinationUsage)
        where T : unmanaged
    {
        return AddBytes(
            MemoryMarshal.AsBytes(values),
            destinationUsage);
    }

    /// <summary>
    /// Adds raw bytes to the upload batch.
    /// </summary>
    /// <remarks>
    /// Payloads must be nonempty. No alignment padding is inserted
    /// between successive uploads.
    /// </remarks>
    public int AddBytes(
        ReadOnlySpan<byte> data,
        BufferUsageFlags destinationUsage)
    {
        ThrowIfSubmitted();

        ValidateUpload(
            checked((ulong)data.Length),
            destinationUsage);

        if (data.Length > int.MaxValue - _stagingData.WrittenCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(data),
                "The batch exceeds the supported staging size.");
        }

        int index = _requests.Count;

        ulong offset = checked(
            (ulong)_stagingData.WrittenCount);

        Span<byte> destination =
            _stagingData.GetSpan(data.Length);

        data.CopyTo(destination);

        _stagingData.Advance(data.Length);

        _requests.Add(new UploadRequest(
            offset,
            checked((ulong)data.Length),
            destinationUsage));

        return index;
    }

    /// <summary>
    /// Creates device-local buffers and uploads all queued data
    /// through one Vulkan command buffer submission.
    /// </summary>
    /// <returns>
    /// Owned destination buffers in the order items were added.
    /// </returns>
    /// <remarks>
    /// This method creates temporary staging storage and blocks
    /// until the transfer submission finishes.
    ///
    /// The graphics queue must be externally synchronized against
    /// other host-side submissions. Upload destinations must not
    /// be accessed by unrelated GPU operations during the transfer.
    ///
    /// A failed submission cannot be retried with this batch.
    /// </remarks>
    public VulkanBuffer[] Submit(
        VulkanInstance instance,
        VulkanLogicalDevice device,
        PhysicalDevice physicalDevice)
    {
        ValidateSubmission(instance, device);

        _submitted = true;

        using VulkanBuffer staging = new(
            instance,
            device,
            physicalDevice,
            StagingByteCount,
            BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit,
            MemoryPropertyFlags.HostCoherentBit);

        return SubmitUsingStaging(
            instance,
            device,
            physicalDevice,
            staging);
    }

    /// <summary>
    /// Uploads all queued data using reusable staging storage.
    /// </summary>
    /// <returns>
    /// Owned destination buffers in the order items were added.
    /// </returns>
    /// <remarks>
    /// The staging cache must belong to the supplied Vulkan
    /// instance, logical device, and physical device.
    ///
    /// The cache is acquired exclusively for this operation and
    /// released after the synchronous submission finishes.
    ///
    /// The cache owns its staging buffer. The caller owns the
    /// destination buffers returned by this method.
    ///
    /// Queue submission still requires external synchronization
    /// against unrelated host-side queue operations.
    ///
    /// A failed submission cannot be retried with this batch.
    /// </remarks>
    public VulkanBuffer[] Submit(
        VulkanInstance instance,
        VulkanLogicalDevice device,
        PhysicalDevice physicalDevice,
        VulkanStagingBufferCache stagingCache)
    {
        ValidateSubmission(instance, device);

        ArgumentNullException.ThrowIfNull(stagingCache);

        VulkanBuffer staging = stagingCache.Acquire(
            instance,
            device,
            physicalDevice,
            StagingByteCount);

        _submitted = true;

        try
        {
            return SubmitUsingStaging(
                instance,
                device,
                physicalDevice,
                staging);
        }

        finally
        {
            stagingCache.Release();
        }
    }

    /// <summary>
    /// Validates the requested payload size and destination usage.
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
    /// Gets the Vulkan copy region for a queued upload.
    /// </summary>
    internal BufferCopy GetCopyRegion(int index)
    {
        if ((uint)index >= (uint)_requests.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        UploadRequest request = _requests[index];

        return new BufferCopy
        {
            SrcOffset = request.StagingOffset,
            DstOffset = 0,
            Size = request.ByteCount
        };
    }

    /// <summary>
    /// Returns a copy of the accumulated staging payload.
    /// Used by device-independent tests.
    /// </summary>
    internal byte[] GetStagingBytes()
    {
        return _stagingData.WrittenSpan.ToArray();
    }

    private VulkanBuffer[] SubmitUsingStaging(
        VulkanInstance instance,
        VulkanLogicalDevice device,
        PhysicalDevice physicalDevice,
        VulkanBuffer staging)
    {
        List<VulkanBuffer> destinations =
            new(_requests.Count);

        try
        {
            staging.Write(_stagingData.WrittenSpan);

            foreach (UploadRequest request in _requests)
            {
                VulkanBuffer destination = new(
                    instance,
                    device,
                    physicalDevice,
                    request.ByteCount,
                    request.DestinationUsage |
                        BufferUsageFlags.TransferDstBit,
                    MemoryPropertyFlags.DeviceLocalBit);

                destinations.Add(destination);
            }

            ExecuteCopies(
                instance,
                device,
                staging,
                destinations);

            return destinations.ToArray();
        }

        catch
        {
            foreach (VulkanBuffer destination in destinations)
            {
                destination.Dispose();
            }

            throw;
        }
    }

    private void ValidateSubmission(
        VulkanInstance instance,
        VulkanLogicalDevice device)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(device);

        ThrowIfSubmitted();

        if (_requests.Count == 0)
        {
            throw new InvalidOperationException(
                "Cannot submit an empty upload batch.");
        }
    }

    private void ExecuteCopies(
        VulkanInstance instance,
        VulkanLogicalDevice device,
        VulkanBuffer staging,
        IReadOnlyList<VulkanBuffer> destinations)
    {
        BatchCopyRecording recording = new(
            this,
            staging.Handle,
            destinations);

        VulkanSynchronousTransferSubmission.Execute(
            instance,
            device,
            recording,
            CopyRecorder);
    }

    private static void RecordCopies(
        Vk api,
        CommandBuffer commandBuffer,
        BatchCopyRecording recording)
    {
        for (int index = 0;
             index < recording.Destinations.Count;
             index++)
        {
            BufferCopy region =
                recording.Batch.GetCopyRegion(index);

            api.CmdCopyBuffer(
                commandBuffer,
                recording.Staging,
                recording.Destinations[index].Handle,
                1,
                in region);
        }

        // All destination writes are made available and visible
        // to subsequent commands that consume these buffers.
        // This broad dependency is intentional for the initial
        // synchronous upload implementation.
        MemoryBarrier barrier = new()
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask =
                AccessFlags.MemoryReadBit |
                AccessFlags.MemoryWriteBit
        };

        api.CmdPipelineBarrier(
            commandBuffer,
            PipelineStageFlags.TransferBit,
            PipelineStageFlags.AllCommandsBit,
            DependencyFlags.None,
            1,
            &barrier,
            0,
            null,
            0,
            null);
    }

    private static void ThrowIfSubmitted(
        VulkanBufferUploadBatch batch)
    {
        if (batch._submitted)
        {
            throw new InvalidOperationException(
                "This upload batch has already been submitted.");
        }
    }

    private void ThrowIfSubmitted()
    {
        ThrowIfSubmitted(this);
    }
}
