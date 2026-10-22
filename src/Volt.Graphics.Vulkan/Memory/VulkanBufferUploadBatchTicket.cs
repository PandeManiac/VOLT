
using System.Runtime.ExceptionServices;

namespace Volt.Graphics.Vulkan.Memory;

/// <summary>
/// Owns one asynchronous batched device-local buffer upload.
/// </summary>
/// <remarks>
/// The ticket retains shared staging storage and all destination
/// buffers until the GPU submission completes.
///
/// The caller receives destination ownership through TakeBuffers.
/// The returned buffers are ordered by their original batch indices.
///
/// Disposing an unfinished ticket may block. Native resources
/// whose completion cannot be established follow the logical
/// device's deferred-retirement mechanism.
///
/// This class is not thread-safe. The associated Vulkan instance
/// and logical device must outlive it.
/// </remarks>
public sealed class VulkanBufferUploadBatchTicket : IDisposable
{
	private readonly VulkanBufferCopyTicket _copyTicket;
	private readonly VulkanBuffer _staging;

	private VulkanBuffer[] _destinations;
	private bool _disposed;

	/// <summary>
	/// Gets the number of destination buffers in the upload.
	/// </summary>
	public int UploadCount { get; }

	/// <summary>
	/// Gets whether the GPU has completed the upload.
	/// </summary>
	public bool IsCompleted
	{
		get
		{
			ThrowIfDisposed();
			return _copyTicket.IsCompleted;
		}
	}

	/// <summary>
	/// Takes ownership of staging and destination allocations.
	/// </summary>
	internal VulkanBufferUploadBatchTicket(
		VulkanBufferCopyTicket copyTicket,
		VulkanBuffer staging,
		VulkanBuffer[] destinations)
	{
		ArgumentNullException.ThrowIfNull(copyTicket);
		ArgumentNullException.ThrowIfNull(staging);
		ArgumentNullException.ThrowIfNull(destinations);

		if (destinations.Length == 0)
		{
			throw new ArgumentException(
				"An upload ticket requires destination buffers.",
				nameof(destinations));
		}

		_copyTicket = copyTicket;
		_staging = staging;
		_destinations = destinations;

		UploadCount = destinations.Length;
	}

	/// <summary>
	/// Waits for the GPU submission to complete.
	/// </summary>
	/// <returns>
	/// True when complete, or false if the wait times out.
	/// </returns>
	/// <remarks>
	/// A timeout does not cancel the submission.
	/// </remarks>
	public bool Wait(
		ulong timeoutNanoseconds = ulong.MaxValue)
	{
		ThrowIfDisposed();

		return _copyTicket.Wait(timeoutNanoseconds);
	}

	/// <summary>
	/// Transfers ownership of all completed destination buffers
	/// to the caller.
	/// </summary>
	/// <returns>
	/// Owned Vulkan buffers in their original batch order.
	/// </returns>
	/// <remarks>
	/// This method does not wait for unfinished GPU work.
	///
	/// The caller must dispose each returned buffer.
	///
	/// If cleanup throws, this ticket retains destination
	/// ownership until successfully completed or disposed.
	/// </remarks>
	public VulkanBuffer[] TakeBuffers()
	{
		ThrowIfDisposed();

		if (!_copyTicket.IsCompleted)
		{
			throw new InvalidOperationException(
				"The batched upload has not completed. " +
				"Wait before taking its destination buffers.");
		}

		_copyTicket.Dispose();
		_staging.Dispose();

		VulkanBuffer[] destinations = _destinations;

		_destinations = [];
		_disposed = true;

		return destinations;
	}

	/// <summary>
	/// Releases all resources still owned by the ticket.
	/// </summary>
	/// <remarks>
	/// An unfinished upload may require a blocking wait.
	///
	/// Cleanup continues after individual disposal failures.
	/// Multiple failures are reported as an AggregateException.
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

		foreach (VulkanBuffer destination in _destinations)
		{
			try
			{
				destination.Dispose();
			}

			catch (Exception exception)
			{
				(failures ??= []).Add(exception);
			}
		}

		_destinations = [];
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
				"an asynchronous Vulkan upload batch.",
				failures);
		}
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}
}
