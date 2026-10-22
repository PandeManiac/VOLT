
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;

namespace Volt.Graphics.Vulkan.Commands;

/// <summary>
/// Owns a Vulkan command pool associated with a logical device's
/// graphics queue family.
/// </summary>
/// <remarks>
/// The Vulkan instance and logical device must outlive this pool.
///
/// Command pools require externally synchronized host access.
/// Do not concurrently allocate, reset, or free command buffers
/// from the same pool.
///
/// All GPU work involving command buffers from this pool must
/// complete before the pool is destroyed.
/// </remarks>
public sealed unsafe class VulkanCommandPool : IDisposable
{
	private const CommandPoolCreateFlags AllowedFlags =
		CommandPoolCreateFlags.TransientBit |
		CommandPoolCreateFlags.ResetCommandBufferBit;

	private readonly VulkanInstance _instance;
	private readonly VulkanLogicalDevice _device;
	private readonly Vk _api;

	private CommandPool _handle;
	private bool _disposed;

	/// <summary>
	/// Gets the queue family associated with this pool.
	/// </summary>
	public uint QueueFamilyIndex
	{
		get
		{
			ThrowIfDisposed();
			return _device.GraphicsQueueFamilyIndex;
		}
	}

	/// <summary>
	/// Gets the command pool creation flags.
	/// </summary>
	public CommandPoolCreateFlags Flags { get; }

	/// <summary>
	/// Gets the borrowed native Vulkan command pool handle.
	/// </summary>
	public CommandPool Handle
	{
		get
		{
			ThrowIfDisposed();
			return _handle;
		}
	}

	/// <summary>
	/// Creates a command pool for the logical device's
	/// graphics queue family.
	/// </summary>
	public VulkanCommandPool(
		VulkanInstance instance,
		VulkanLogicalDevice device,
		CommandPoolCreateFlags flags =
			CommandPoolCreateFlags.ResetCommandBufferBit)
	{
		ArgumentNullException.ThrowIfNull(instance);
		ArgumentNullException.ThrowIfNull(device);

		Vk api = instance.Api;
		Device deviceHandle = device.Handle;

		CommandPoolCreateInfo createInfo =
			CreateDescription(
				device.GraphicsQueueFamilyIndex,
				flags);

		Result result = api.CreateCommandPool(
			deviceHandle,
			in createInfo,
			null,
			out CommandPool pool);

		if (result != Result.Success)
		{
			throw new InvalidOperationException(
				$"Vulkan command pool creation failed: {result}.");
		}

		if (pool.Handle == 0)
		{
			throw new InvalidOperationException(
				"Vulkan reported successful command pool creation " +
				"but returned a null handle.");
		}

		_instance = instance;
		_device = device;
		_api = api;
		_handle = pool;

		Flags = flags;
	}

	/// <summary>
	/// Destroys the command pool.
	/// </summary>
	/// <remarks>
	/// Vulkan automatically frees command buffers allocated
	/// from this pool when it is destroyed.
	///
	/// Those command buffers must not be pending execution,
	/// and their borrowed handles must no longer be used.
	/// </remarks>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_ = _instance.Handle;
		Device deviceHandle = _device.Handle;

		_api.DestroyCommandPool(
			deviceHandle,
			_handle,
			null);

		_handle = default;
		_disposed = true;
	}

	/// <summary>
	/// Creates a managed Vulkan command pool description.
	/// </summary>
	/// <remarks>
	/// Protected command pools are intentionally unsupported
	/// because VOLT has not enabled protected queue features.
	/// </remarks>
	internal static CommandPoolCreateInfo CreateDescription(
		uint queueFamilyIndex,
		CommandPoolCreateFlags flags =
			CommandPoolCreateFlags.ResetCommandBufferBit)
	{
		if ((flags & ~AllowedFlags) != 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(flags),
				flags,
				"Unsupported Vulkan command pool creation flags.");
		}

		return new CommandPoolCreateInfo
		{
			SType = StructureType.CommandPoolCreateInfo,
			QueueFamilyIndex = queueFamilyIndex,
			Flags = flags
		};
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}
}
