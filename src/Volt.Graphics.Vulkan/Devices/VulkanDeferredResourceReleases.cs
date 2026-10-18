
using Silk.NET.Vulkan;

namespace Volt.Graphics.Vulkan.Devices;

/// <summary>
/// Tracks resource releases delayed by unresolved Vulkan execution.
/// </summary>
/// <remarks>
/// The owner must establish GPU completion or device loss before
/// calling Reclaim.
///
/// Cleanup callbacks must be safe to retry when they throw.
/// A failed callback is retained for a subsequent reclamation pass.
///
/// This class protects its internal state, but does not replace
/// Vulkan's external host synchronization requirements.
/// </remarks>
internal sealed class VulkanDeferredResourceReleases
{
    private readonly object _gate = new();
    private readonly Queue<Action> _pending = new();

    private bool _transferUnresolved;
    private bool _deviceLost;
    private bool _draining;

    public bool HasUnresolvedTransfer
    {
        get
        {
            lock (_gate)
            {
                return _transferUnresolved;
            }
        }
    }

    public bool IsLost
    {
        get
        {
            lock (_gate)
            {
                return _deviceLost;
            }
        }
    }

    internal int PendingReleaseCount
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count;
            }
        }
    }

    /// <summary>
    /// Ensures that new transfers can be submitted safely.
    /// </summary>
    public void EnsureTransferAvailable()
    {
        lock (_gate)
        {
            if (_deviceLost)
            {
                throw new InvalidOperationException(
                    "The Vulkan device has been lost and " +
                    "cannot accept new transfers.");
            }

            if (_transferUnresolved)
            {
                throw new InvalidOperationException(
                    "A previous Vulkan transfer has unresolved " +
                    "GPU execution.");
            }

            if (_draining || _pending.Count != 0)
            {
                throw new InvalidOperationException(
                    "Vulkan resource cleanup is incomplete. " +
                    "Complete reclamation before new transfers.");
            }
        }
    }

    /// <summary>
    /// Prevents resource reclamation until GPU execution
    /// can be established as complete or terminated.
    /// </summary>
    public void MarkTransferUnresolved()
    {
        lock (_gate)
        {
            _transferUnresolved = true;
        }
    }

    /// <summary>
    /// Permanently prevents new transfers on this device.
    /// </summary>
    public void MarkDeviceLost()
    {
        lock (_gate)
        {
            _deviceLost = true;
        }
    }

    /// <summary>
    /// Releases a resource immediately when safe, or queues
    /// its cleanup for later reclamation.
    /// </summary>
    /// <remarks>
    /// If immediate cleanup throws, its callback is retained
    /// so that a later device-idle operation can retry it.
    /// </remarks>
    public void ReleaseOrDefer(Action release)
    {
        ArgumentNullException.ThrowIfNull(release);

        lock (_gate)
        {
            if (_transferUnresolved ||
                _draining ||
                _pending.Count != 0)
            {
                _pending.Enqueue(release);
                return;
            }
        }

        try
        {
            release();
        }

        catch
        {
            lock (_gate)
            {
                _pending.Enqueue(release);
            }

            throw;
        }
    }

    /// <summary>
    /// Executes deferred releases after a safe device-idle result.
    /// </summary>
    /// <remarks>
    /// Every callback present at the beginning of the pass
    /// is attempted once, even if other callbacks fail.
    ///
    /// Failed callbacks and callbacks added during the pass
    /// remain queued for a later call.
    ///
    /// If cleanup fails, new transfers remain unavailable.
    /// </remarks>
    public void Reclaim(Result result)
    {
        if (!CanReclaimAfterWait(result))
        {
            throw new InvalidOperationException(
                "Cannot reclaim Vulkan resources without " +
                $"established GPU completion: {result}.");
        }

        int attemptCount;

        lock (_gate)
        {
            if (_draining)
            {
                throw new InvalidOperationException(
                    "Vulkan resource reclamation is already active.");
            }

            if (result == Result.ErrorDeviceLost)
            {
                _deviceLost = true;
            }

            _transferUnresolved = false;
            _draining = true;

            attemptCount = _pending.Count;
        }

        List<Exception>? failures = null;

        try
        {
            for (int index = 0; index < attemptCount; index++)
            {
                Action release;

                lock (_gate)
                {
                    release = _pending.Dequeue();
                }

                try
                {
                    release();
                }

                catch (Exception exception)
                {
                    lock (_gate)
                    {
                        _pending.Enqueue(release);
                    }

                    (failures ??= []).Add(exception);
                }
            }
        }

        finally
        {
            lock (_gate)
            {
                _draining = false;
            }
        }

        if (failures is not null)
        {
            throw new AggregateException(
                "One or more Vulkan resource releases failed. " +
                "Failed callbacks have been retained for retry.",
                failures);
        }
    }

    /// <summary>
    /// Determines whether a wait result permits resource reclamation.
    /// </summary>
    public static bool CanReclaimAfterWait(Result result)
    {
        return result is
            Result.Success or Result.ErrorDeviceLost;
    }
}
