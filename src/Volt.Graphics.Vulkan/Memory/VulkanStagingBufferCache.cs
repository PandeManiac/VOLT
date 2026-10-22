
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;

namespace Volt.Graphics.Vulkan.Memory;

/// <summary>
/// Retains a host-visible Vulkan staging buffer for reuse across
/// synchronous upload batches.
/// </summary>
/// <remarks>
/// The staging buffer is allocated lazily and grows when a batch
/// exceeds its current capacity.
///
/// A cache can serve only one upload at a time. It must outlive
/// any batch using it and must be disposed before its Vulkan
/// device is destroyed.
///
/// Capacity describes the requested buffer size, not the size
/// of the underlying Vulkan memory allocation.
///
/// This class does not support asynchronous reuse, overlapping
/// transfers, or device-memory suballocation.
/// </remarks>
public sealed class VulkanStagingBufferCache : IDisposable
{
	private readonly VulkanInstance _instance;
	private readonly VulkanLogicalDevice _device;
	private readonly PhysicalDevice _physicalDevice;
	private readonly object _gate = new();

	private VulkanBuffer? _buffer;

	private bool _inUse;
	private bool _disposed;

	/// <summary>
	/// Gets the current staging buffer capacity in bytes.
	/// </summary>
	public ulong Capacity
	{
		get
		{
			lock (_gate)
			{
				ThrowIfDisposed();
				return _buffer?.Size ?? 0;
			}
		}
	}

	public VulkanStagingBufferCache(
		VulkanInstance instance,
		VulkanLogicalDevice device,
		PhysicalDevice physicalDevice)
	{
		ArgumentNullException.ThrowIfNull(instance);
		ArgumentNullException.ThrowIfNull(device);

		if (physicalDevice.Handle == 0)
		{
			throw new ArgumentException(
				"A valid physical device is required.",
				nameof(physicalDevice));
		}

		_instance = instance;
		_device = device;
		_physicalDevice = physicalDevice;
	}

	public void Dispose()
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			if (_inUse)
			{
				throw new InvalidOperationException(
					"Cannot dispose staging storage during " +
					"an active upload.");
			}

			_buffer?.Dispose();
			_buffer = null;

			_disposed = true;
		}
	}

	/// <summary>
	/// Acquires the staging buffer for one synchronous upload.
	/// </summary>
	/// <remarks>
	/// The caller must invoke Release after the upload finishes
	/// or fails. A second acquisition is rejected until then.
	/// </remarks>
	internal VulkanBuffer Acquire(
		VulkanInstance instance,
		VulkanLogicalDevice device,
		PhysicalDevice physicalDevice,
		ulong requiredCapacity)
	{
		lock (_gate)
		{
			ThrowIfDisposed();

			if (_inUse)
			{
				throw new InvalidOperationException(
					"The staging buffer is already in use.");
			}

			if (!ReferenceEquals(instance, _instance) ||
				!ReferenceEquals(device, _device) ||
				physicalDevice.Handle != _physicalDevice.Handle)
			{
				throw new ArgumentException(
					"The staging cache belongs to a different " +
					"Vulkan instance or device.");
			}

			if (requiredCapacity == 0)
			{
				throw new ArgumentOutOfRangeException(
					nameof(requiredCapacity));
			}

			_inUse = true;

			try
			{
				if (_buffer is null ||
					_buffer.Size < requiredCapacity)
				{
					ulong capacity = SelectCapacity(
						_buffer?.Size ?? 0,
						requiredCapacity);

					VulkanBuffer replacement = new(
						_instance,
						_device,
						_physicalDevice,
						capacity,
						BufferUsageFlags.TransferSrcBit,
						MemoryPropertyFlags.HostVisibleBit,
						MemoryPropertyFlags.HostCoherentBit);

					VulkanBuffer? previous = _buffer;

					_buffer = replacement;

					previous?.Dispose();
				}

				return _buffer;
			}

			catch
			{
				_inUse = false;
				throw;
			}
		}
	}

	/// <summary>
	/// Releases exclusive access after a synchronous upload.
	/// </summary>
	internal void Release()
	{
		lock (_gate)
		{
			if (!_inUse)
			{
				throw new InvalidOperationException(
					"The staging buffer is not acquired.");
			}

			_inUse = false;
		}
	}

	/// <summary>
	/// Selects the retained staging capacity without allocating.
	/// </summary>
	internal static ulong SelectCapacity(
		ulong currentCapacity,
		ulong requiredCapacity)
	{
		if (requiredCapacity == 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(requiredCapacity));
		}

		if (currentCapacity >= requiredCapacity)
		{
			return currentCapacity;
		}

		if (currentCapacity == 0)
		{
			return requiredCapacity;
		}

		if (currentCapacity > ulong.MaxValue / 2)
		{
			return requiredCapacity;
		}

		return Math.Max(
			requiredCapacity,
			currentCapacity * 2);
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}
}
