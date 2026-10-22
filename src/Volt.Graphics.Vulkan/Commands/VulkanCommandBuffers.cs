
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;

namespace Volt.Graphics.Vulkan.Commands;

/// <summary>
/// Owns a batch of primary Vulkan command buffers allocated from
/// a Vulkan command pool.
/// </summary>
/// <remarks>
/// The Vulkan instance, logical device, and command pool must
/// outlive this allocation.
///
/// All command buffers must no longer be pending GPU execution
/// before they are freed.
///
/// Host access to the command pool must be externally synchronized.
/// This class does not provide thread synchronization.
///
/// Command buffer handles returned by GetBuffer are borrowed.
/// </remarks>
public sealed unsafe class VulkanCommandBuffers : IDisposable
{
	private readonly VulkanInstance _instance;
	private readonly VulkanLogicalDevice _device;
	private readonly VulkanCommandPool _pool;
	private readonly Vk _api;
	private readonly CommandBuffer[] _buffers;

	private bool _disposed;

	/// <summary>
	/// Gets the number of allocated command buffers.
	/// </summary>
	public int Count
	{
		get
		{
			ThrowIfDisposed();
			return _buffers.Length;
		}
	}

	/// <summary>
	/// Gets the Vulkan command buffer level.
	/// </summary>
	public CommandBufferLevel Level
	{
		get
		{
			ThrowIfDisposed();
			return CommandBufferLevel.Primary;
		}
	}

	/// <summary>
	/// Allocates primary command buffers from the specified pool.
	/// </summary>
	/// <remarks>
	/// The command pool must belong to the supplied logical device.
	/// The logical device must belong to the supplied instance.
	///
	/// Vulkan guarantees that a failed allocation frees any
	/// command buffers allocated during that same call.
	/// </remarks>
	public VulkanCommandBuffers(
		VulkanInstance instance,
		VulkanLogicalDevice device,
		VulkanCommandPool pool,
		int count)
	{
		ArgumentNullException.ThrowIfNull(instance);
		ArgumentNullException.ThrowIfNull(device);
		ArgumentNullException.ThrowIfNull(pool);

		if (count <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(count),
				count,
				"At least one command buffer must be requested.");
		}

		Vk api = instance.Api;
		Device deviceHandle = device.Handle;
		CommandPool poolHandle = pool.Handle;

		if (pool.QueueFamilyIndex !=
			device.GraphicsQueueFamilyIndex)
		{
			throw new ArgumentException(
				"The command pool queue family does not match " +
				"the logical device graphics queue family.",
				nameof(pool));
		}

		CommandBufferAllocateInfo allocationInfo =
			CreateDescription(poolHandle, count);

		CommandBuffer[] buffers = new CommandBuffer[count];

		fixed (CommandBuffer* pointer = buffers)
		{
			Result result = api.AllocateCommandBuffers(
				deviceHandle,
				in allocationInfo,
				pointer);

			if (result != Result.Success)
			{
				throw new InvalidOperationException(
					"Vulkan command buffer allocation failed: " +
					result);
			}

			for (int index = 0; index < buffers.Length; index++)
			{
				if (buffers[index].Handle != 0)
				{
					continue;
				}

				api.FreeCommandBuffers(
					deviceHandle,
					poolHandle,
					checked((uint)buffers.Length),
					pointer);

				throw new InvalidOperationException(
					$"Vulkan returned a null command buffer " +
					$"handle at index {index}.");
			}
		}

		_instance = instance;
		_device = device;
		_pool = pool;
		_api = api;
		_buffers = buffers;
	}

	/// <summary>
	/// Gets a borrowed Vulkan command buffer handle.
	/// </summary>
	/// <param name="index">
	/// Zero-based index into the allocated command buffer batch.
	/// </param>
	public CommandBuffer GetBuffer(int index)
	{
		ThrowIfDisposed();

		return _buffers[index];
	}

	/// <summary>
	/// Frees all command buffers in this allocation.
	/// </summary>
	/// <remarks>
	/// The caller must ensure that none of these command buffers
	/// are pending execution before calling Dispose.
	///
	/// Dispose this allocation before destroying its command pool.
	/// </remarks>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_ = _instance.Handle;
		Device deviceHandle = _device.Handle;
		CommandPool poolHandle = _pool.Handle;

		fixed (CommandBuffer* pointer = _buffers)
		{
			_api.FreeCommandBuffers(
				deviceHandle,
				poolHandle,
				checked((uint)_buffers.Length),
				pointer);
		}

		_disposed = true;
	}

	/// <summary>
	/// Creates a managed Vulkan primary command buffer
	/// allocation description.
	/// </summary>
	internal static CommandBufferAllocateInfo CreateDescription(
		CommandPool pool,
		int count)
	{
		if (pool.Handle == 0)
		{
			throw new ArgumentException(
				"A valid Vulkan command pool handle is required.",
				nameof(pool));
		}

		if (count <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(count),
				count,
				"At least one command buffer must be requested.");
		}

		return new CommandBufferAllocateInfo
		{
			SType = StructureType.CommandBufferAllocateInfo,
			CommandPool = pool,
			Level = CommandBufferLevel.Primary,
			CommandBufferCount = checked((uint)count)
		};
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}
}
