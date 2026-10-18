using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using VkBuffer = Silk.NET.Vulkan.Buffer;

namespace Volt.Graphics.Vulkan.Memory;

/// <summary>
/// Collects multiple buffer uploads into a single Vulkan
/// graphics queue submission.
/// </summary>
/// <remarks>
/// Application data is copied into bounded managed staging chunks
/// when added to the batch. Growing staging storage does not
/// reallocate or copy previously staged payloads.
///
/// Each upload receives an independent device-local destination
/// buffer. Compatible destinations may share a native device-memory
/// allocation while retaining independent buffer ownership.
///
/// When sharing is unsuitable, destinations use dedicated memory.
///
/// Buffer copies support arbitrary positive byte counts without
/// additional staging alignment or padding. Applications remain
/// responsible for alignment required by their buffer consumers.
///
/// Synchronous submission creates temporary staging storage and
/// blocks until GPU execution completes. An optional
/// VulkanStagingBufferCache enables staging allocation reuse
/// across completed synchronous batches.
///
/// Asynchronous submission creates independently owned staging
/// storage and returns a VulkanBufferUploadBatchTicket without
/// waiting for GPU completion. The ticket retains the staging
/// allocation, destination buffers, and submission resources
/// until completion or disposal.
///
/// Synchronous Submit returns caller-owned destination buffers.
/// Asynchronous SubmitAsync transfers destination ownership
/// through VulkanBufferUploadBatchTicket.TakeBuffers.
///
/// This class is a one-shot upload builder. It is not thread-safe
/// and does not synchronize unrelated GPU work or concurrent
/// host-side graphics queue submissions.
///
/// It does not implement device-memory suballocation or
/// asynchronous staging allocation reuse.
/// </remarks>
public sealed unsafe class VulkanBufferUploadBatch
{
    private const int InitialStagingChunkBytes = 256;
    private const int StagingChunkBytes = 64 * 1024;

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

    private readonly List<byte[]> _stagingChunks = [];
    private readonly List<UploadRequest> _requests = [];

    private int _stagingByteCount;
    private int _nextChunkIndex;
    private int _nextChunkOffset;

    private bool _submitted;

    /// <summary>
    /// Gets the number of queued uploads.
    /// </summary>
    public int UploadCount => _requests.Count;

    /// <summary>
    /// Gets the total number of staged bytes.
    /// </summary>

    /// <summary>
    /// Gets the total number of staged bytes.
    /// </summary>
    public ulong StagingByteCount =>
        (ulong)_stagingByteCount;

    /// <summary>
    /// Gets the total managed staging capacity reserved
    /// across all chunks.
    /// </summary>
    internal ulong ReservedStagingByteCount
    {
        get
        {
            ulong capacity = 0;

            foreach (byte[] chunk in _stagingChunks)
            {
                capacity += (ulong)chunk.Length;
            }

            return capacity;
        }
    }

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
    /// Input data is copied into owned managed storage.
    /// The caller may modify or release the original data
    /// after this method returns.
    ///
    /// Staging storage grows by adding bounded chunks.
    /// Previously staged data is never copied during growth.
    ///
    /// Payloads must be nonempty. Copy offsets are contiguous
    /// regardless of internal chunk boundaries.
    /// </remarks>
    public int AddBytes(
        ReadOnlySpan<byte> data,
        BufferUsageFlags destinationUsage)
    {
        ThrowIfSubmitted();

        ValidateUpload(
            checked((ulong)data.Length),
            destinationUsage);

        if (data.Length > int.MaxValue - _stagingByteCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(data),
                "The batch exceeds the supported staging size.");
        }

        int index = _requests.Count;
        int originalByteCount = _stagingByteCount;

        int chunkIndex = _nextChunkIndex;
        int chunkOffset = _nextChunkOffset;

        ReadOnlySpan<byte> remaining = data;

        while (!remaining.IsEmpty)
        {
            if (chunkIndex == _stagingChunks.Count)
            {
                int capacity = _stagingChunks.Count == 0
                    ? Math.Min(
                        StagingChunkBytes,
                        Math.Max(
                            InitialStagingChunkBytes,
                            remaining.Length))
                    : StagingChunkBytes;

                _stagingChunks.Add(
                    new byte[capacity]);
            }

            byte[] chunk = _stagingChunks[chunkIndex];

            int count = Math.Min(
                remaining.Length,
                chunk.Length - chunkOffset);

            remaining[..count].CopyTo(
                chunk.AsSpan(chunkOffset, count));

            remaining = remaining[count..];
            chunkOffset += count;

            if (chunkOffset == chunk.Length)
            {
                chunkIndex++;
                chunkOffset = 0;
            }
        }

        _requests.Add(new UploadRequest(
            (ulong)originalByteCount,
            (ulong)data.Length,
            destinationUsage));

        _stagingByteCount = originalByteCount + data.Length;
        _nextChunkIndex = chunkIndex;
        _nextChunkOffset = chunkOffset;

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
    /// Submits all queued uploads through one asynchronous
    /// Vulkan graphics-queue submission.
    /// </summary>
    /// <returns>
    /// A ticket owning the shared staging buffer, submission,
    /// and destination buffers until completion.
    /// </returns>
    /// <remarks>
    /// Upload payloads are already copied into managed staging
    /// storage when they are added to the batch.
    ///
    /// This method creates one host-visible staging buffer and
    /// one device-local destination buffer per upload request.
    ///
    /// All buffer copies share one command buffer submission
    /// and one completion fence.
    ///
    /// Successful return does not imply GPU completion.
    ///
    /// The batch cannot be submitted again, even if submission
    /// fails. Graphics queue host access requires external
    /// synchronization.
    ///
    /// Destination ownership passes to the caller only through
    /// VulkanBufferUploadBatchTicket.TakeBuffers.
    /// </remarks>
    public VulkanBufferUploadBatchTicket SubmitAsync(
        VulkanInstance instance,
        VulkanLogicalDevice device,
        PhysicalDevice physicalDevice)
    {
        ValidateSubmission(instance, device);
        _submitted = true;

        VulkanBuffer? staging = null;
        VulkanBuffer[]? destinations = null;
        VulkanBufferCopyTicket? copyTicket = null;

        try
        {
            staging = new VulkanBuffer(
                instance,
                device,
                physicalDevice,
                StagingByteCount,
                BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.HostVisibleBit,
                MemoryPropertyFlags.HostCoherentBit);

            staging.WriteSegments(
                _stagingChunks,
                _stagingByteCount);


            destinations = CreateDestinations(
                instance,
                device,
                physicalDevice);

            BatchCopyRecording recording = new(
                this,
                staging.Handle,
                destinations);

            copyTicket = VulkanBufferCopyTicket.Submit(
                instance,
                device,
                recording,
                CopyRecorder);

            return new VulkanBufferUploadBatchTicket(
                copyTicket,
                staging,
                destinations);
        }

        catch (Exception submissionError)
        {
            List<Exception>? cleanupErrors = null;

            try
            {
                copyTicket?.Dispose();
            }

            catch (Exception exception)
            {
                (cleanupErrors ??= []).Add(exception);
            }

            if (destinations is not null)
            {
                foreach (VulkanBuffer? destination in destinations)
                {
                    try
                    {
                        destination?.Dispose();
                    }

                    catch (Exception exception)
                    {
                        (cleanupErrors ??= []).Add(exception);
                    }
                }
            }

            try
            {
                staging?.Dispose();
            }

            catch (Exception exception)
            {
                (cleanupErrors ??= []).Add(exception);
            }

            if (cleanupErrors is not null)
            {
                cleanupErrors.Insert(0, submissionError);

                throw new AggregateException(
                    "Batched Vulkan upload submission and " +
                    "resource cleanup both reported failures.",
                    cleanupErrors);
            }

            throw;
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
    /// Returns a contiguous copy of the accumulated payload.
    /// Used by device-independent tests.
    /// </summary>
    internal byte[] GetStagingBytes()
    {
        byte[] result = new byte[_stagingByteCount];
        int offset = 0;

        foreach (byte[] chunk in _stagingChunks)
        {
            int count = Math.Min(
                chunk.Length,
                result.Length - offset);

            if (count == 0)
            {
                break;
            }

            chunk.AsSpan(0, count).CopyTo(
                result.AsSpan(offset, count));

            offset += count;
        }

        return result;
    }

    private VulkanBuffer[] CreateDestinations(
        VulkanInstance instance,
        VulkanLogicalDevice device,
        PhysicalDevice physicalDevice)
    {
        VulkanBufferAllocationRequest[] requests =
            new VulkanBufferAllocationRequest[_requests.Count];

        for (int index = 0; index < requests.Length; index++)
        {
            UploadRequest request = _requests[index];

            requests[index] = new VulkanBufferAllocationRequest(
                request.ByteCount,
                request.DestinationUsage
                    | BufferUsageFlags.TransferDstBit);
        }

        return VulkanBatchDestinationAllocator.Create(
            instance,
            device,
            physicalDevice,
            requests);
    }

    private VulkanBuffer[] SubmitUsingStaging(
        VulkanInstance instance,
        VulkanLogicalDevice device,
        PhysicalDevice physicalDevice,
        VulkanBuffer staging)
    {
        staging.WriteSegments(
            _stagingChunks,
            _stagingByteCount);

        VulkanBuffer[] destinations = CreateDestinations(
            instance,
            device,
            physicalDevice);

        try
        {
            ExecuteCopies(
                instance,
                device,
                staging,
                destinations);

            return destinations;
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

        device.EnsureTransferAvailable();
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
