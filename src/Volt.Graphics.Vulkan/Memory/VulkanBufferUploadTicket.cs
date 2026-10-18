
using System.Runtime.ExceptionServices;

namespace Volt.Graphics.Vulkan.Memory;

/// <summary>
/// Owns a pending asynchronous device-local buffer upload.
/// </summary>
/// <remarks>
/// The ticket owns its staging allocation and destination buffer
/// until the destination is explicitly taken by the caller.
///
/// Completion can be polled or awaited through a Vulkan fence.
/// TakeBuffer requires successful GPU completion.
///
/// Dispose releases the staging allocation, pending submission
/// resources, and any destination buffer not taken by the caller.
///
/// Disposing before completion may block. If GPU completion
/// cannot be established, native resource destruction follows
/// the logical device's deferred-retirement mechanism.
///
/// The ticket is not thread-safe. Its Vulkan device and instance
/// must outlive it.
/// </remarks>
public sealed class VulkanBufferUploadTicket : IDisposable
{
    private readonly VulkanBufferCopyTicket _copyTicket;
    private readonly VulkanBuffer _staging;

    private VulkanBuffer? _destination;
    private bool _disposed;

    /// <summary>
    /// Gets whether the upload's GPU copy has completed.
    /// </summary>
    /// <remarks>
    /// This queries the underlying fence until completion
    /// has been observed.
    /// </remarks>
    public bool IsCompleted
    {
        get
        {
            ThrowIfDisposed();
            return _copyTicket.IsCompleted;
        }
    }

    /// <summary>
    /// Creates a ticket that owns both upload allocations.
    /// </summary>
    internal VulkanBufferUploadTicket(
        VulkanBufferCopyTicket copyTicket,
        VulkanBuffer staging,
        VulkanBuffer destination)
    {
        ArgumentNullException.ThrowIfNull(copyTicket);
        ArgumentNullException.ThrowIfNull(staging);
        ArgumentNullException.ThrowIfNull(destination);

        _copyTicket = copyTicket;
        _staging = staging;
        _destination = destination;
    }

    /// <summary>
    /// Waits for GPU completion.
    /// </summary>
    /// <param name="timeoutNanoseconds">
    /// Maximum wait duration in nanoseconds.
    /// Zero performs a nonblocking wait.
    /// </param>
    /// <returns>
    /// True when completed, or false when the wait times out.
    /// </returns>
    public bool Wait(
        ulong timeoutNanoseconds = ulong.MaxValue)
    {
        ThrowIfDisposed();

        return _copyTicket.Wait(timeoutNanoseconds);
    }

    /// <summary>
    /// Transfers ownership of the completed destination buffer
    /// to the caller.
    /// </summary>
    /// <returns>
    /// An owned device-local Vulkan buffer.
    /// </returns>
    /// <remarks>
    /// This method requires GPU completion and does not
    /// implicitly wait for pending work.
    ///
    /// On success, the ticket releases its staging and command
    /// resources and is considered disposed.
    ///
    /// The caller must dispose the returned VulkanBuffer.
    /// </remarks>
    public VulkanBuffer TakeBuffer()
    {
        ThrowIfDisposed();

        if (!_copyTicket.IsCompleted)
        {
            throw new InvalidOperationException(
                "The upload has not completed. " +
                "Wait for completion before taking the buffer.");
        }

        // The GPU is finished with both allocations.
        // Release submission resources before transferring
        // ownership of the destination.
        _copyTicket.Dispose();
        _staging.Dispose();

        VulkanBuffer destination = _destination ??
            throw new InvalidOperationException(
                "The upload destination is no longer available.");

        _destination = null;
        _disposed = true;

        return destination;
    }

    /// <summary>
    /// Releases all resources still owned by this upload.
    /// </summary>
    /// <remarks>
    /// Disposal can block while waiting for GPU completion.
    ///
    /// The underlying copy ticket establishes safe native
    /// resource retirement before the staging and destination
    /// buffers are released.
    ///
    /// Cleanup continues when an earlier cleanup operation
    /// throws. Multiple failures are reported together.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        List<Exception>? failures = null;

        try
        {
            _copyTicket.Dispose();
        }

        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        try
        {
            _staging.Dispose();
        }

        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        try
        {
            _destination?.Dispose();
        }

        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        _destination = null;
        _disposed = true;

        if (failures is { Count: 1 })
        {
            ExceptionDispatchInfo.Capture(
                failures[0]).Throw();
        }

        if (failures is { Count: > 1 })
        {
            throw new AggregateException(
                "Multiple failures occurred while disposing " +
                "an asynchronous Vulkan upload.",
                failures);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
