
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Commands;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;

namespace Volt.Graphics.Vulkan.Memory;

/// <summary>
/// Tracks one asynchronous Vulkan buffer-copy submission.
/// </summary>
/// <remarks>
/// Submission returns before GPU completion.
///
/// This ticket owns its fence, command buffers, and command pool.
/// It does not own source or destination buffers.
///
/// The caller must keep both buffers alive and avoid conflicting
/// accesses until the copy finishes.
///
/// Dispose waits for completion before releasing native resources.
/// If completion cannot be established, resources are deferred
/// through the logical device's retirement mechanism.
///
/// The ticket is not thread-safe. The logical device and instance
/// must outlive the ticket.
/// </remarks>
public sealed unsafe class VulkanBufferCopyTicket : IDisposable
{
	private const ulong DisposalWaitTimeoutNanoseconds =
		5_000_000_000UL;

	private sealed class NativeResources
	{
		private readonly Vk _api;
		private readonly Device _deviceHandle;

		public VulkanCommandPool? CommandPool;
		public VulkanCommandBuffers? CommandBuffers;
		public Fence Fence;

		public NativeResources(
			Vk api,
			Device deviceHandle)
		{
			_api = api;
			_deviceHandle = deviceHandle;
		}

		public void Release()
		{
			try
			{
				if (Fence.Handle != 0)
				{
					_api.DestroyFence(
						_deviceHandle,
						Fence,
						null);

					Fence = default;
				}
			}

			finally
			{
				try
				{
					if (CommandBuffers is not null)
					{
						CommandBuffers.Dispose();
						CommandBuffers = null;
					}
				}

				finally
				{
					if (CommandPool is not null)
					{
						CommandPool.Dispose();
						CommandPool = null;
					}
				}
			}
		}
	}

	private readonly VulkanInstance _instance;
	private readonly VulkanLogicalDevice _device;
	private readonly NativeResources _resources;

	private bool _completed;
	private bool _disposed;

	/// <summary>
	/// Gets whether the associated GPU submission has completed.
	/// </summary>
	/// <remarks>
	/// Polls the native fence unless completion was already observed.
	/// A device-lost result is reported as an error, not success.
	/// </remarks>
	public bool IsCompleted
	{
		get
		{
			ThrowIfDisposed();

			if (_completed)
			{
				return true;
			}

			Result result = _instance.Api.GetFenceStatus(
				_device.Handle,
				_resources.Fence);

			switch (result)
			{
				case Result.Success:
					_completed = true;
					return true;

				case Result.NotReady:
					return false;

				case Result.ErrorDeviceLost:
					_device.MarkDeviceLost();

					throw new InvalidOperationException(
						"The Vulkan device was lost while " +
						"polling the transfer fence.");

				default:
					throw new InvalidOperationException(
						$"Querying the transfer fence failed: {result}.");
			}
		}
	}

	private VulkanBufferCopyTicket(
		VulkanInstance instance,
		VulkanLogicalDevice device,
		NativeResources resources)
	{
		_instance = instance;
		_device = device;
		_resources = resources;
	}

	/// <summary>
	/// Waits for the submitted copy to complete.
	/// </summary>
	/// <returns>
	/// True when the fence signals, or false after a timeout.
	/// </returns>
	/// <remarks>
	/// A timeout does not cancel the copy. The caller may
	/// poll or wait again later.
	/// </remarks>
	public bool Wait(
		ulong timeoutNanoseconds = ulong.MaxValue)
	{
		ThrowIfDisposed();

		if (_completed)
		{
			return true;
		}

		Fence fence = _resources.Fence;

		Result result = _instance.Api.WaitForFences(
			_device.Handle,
			1,
			in fence,
			Vk.True,
			timeoutNanoseconds);

		switch (result)
		{
			case Result.Success:
				_completed = true;
				return true;

			case Result.Timeout:
				return false;

			case Result.ErrorDeviceLost:
				_device.MarkDeviceLost();

				throw new InvalidOperationException(
					"The Vulkan device was lost while " +
					"waiting for the transfer fence.");

			default:
				throw new InvalidOperationException(
					$"Waiting for the transfer fence failed: {result}.");
		}
	}

	/// <summary>
	/// Waits for completion and releases owned submission resources.
	/// </summary>
	/// <remarks>
	/// Disposal can block.
	///
	/// If the fence wait times out, the device-idle fallback is
	/// attempted before cleanup. An unsuccessful fallback leaves
	/// resources deferred rather than destroying objects that
	/// may still be in use.
	/// </remarks>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		Exception? completionError = null;

		if (!_completed && !_device.IsLost)
		{
			try
			{
				Wait(DisposalWaitTimeoutNanoseconds);
			}

			catch (Exception exception)
			{
				completionError = exception;
			}

			if (!_completed && !_device.IsLost)
			{
				try
				{
					Result idleResult =
						_instance.Api.DeviceWaitIdle(_device.Handle);

					if (idleResult == Result.Success)
					{
						_completed = true;
					}

					else if (idleResult == Result.ErrorDeviceLost)
					{
						_device.MarkDeviceLost();

						completionError ??=
							new InvalidOperationException(
								"The Vulkan device was lost during " +
								"transfer ticket disposal.");
					}

					else
					{
						_device.MarkTransferUnresolved();

						completionError =
							new InvalidOperationException(
								"Transfer completion could not be " +
								$"established: {idleResult}.",
								completionError);
					}
				}

				catch (Exception exception)
				{
					_device.MarkTransferUnresolved();

					Exception[] errors = completionError is null
						? [exception]
						: [completionError, exception];

					completionError = new AggregateException(
						"Transfer completion could not be " +
						"established during disposal.",
						errors);
				}
			}
		}

		try
		{
			_device.ReleaseOrDefer(
				_resources.Release);
		}

		finally
		{
			_device.UnregisterPendingTransferTicket();
			_disposed = true;
		}

		if (completionError is not null)
		{
			throw completionError;
		}
	}

	/// <summary>
	/// Records and submits a copy without waiting for execution.
	/// </summary>
	internal static VulkanBufferCopyTicket Submit<TState>(
		VulkanInstance instance,
		VulkanLogicalDevice device,
		TState state,
		Action<Vk, CommandBuffer, TState> recordCommands)
	{
		ArgumentNullException.ThrowIfNull(instance);
		ArgumentNullException.ThrowIfNull(device);
		ArgumentNullException.ThrowIfNull(recordCommands);

		device.EnsureTransferAvailable();

		Vk api = instance.Api;
		Device deviceHandle = device.Handle;

		NativeResources resources = new(
			api,
			deviceHandle);

		bool registered = false;
		bool submissionAttempted = false;

		try
		{
			resources.CommandPool = new VulkanCommandPool(
				instance,
				device,
				CommandPoolCreateFlags.TransientBit);

			resources.CommandBuffers = new VulkanCommandBuffers(
				instance,
				device,
				resources.CommandPool,
				1);

			CommandBuffer commandBuffer =
				resources.CommandBuffers.GetBuffer(0);

			VulkanCommandRecording.Begin(
				instance,
				commandBuffer);

			recordCommands(
				api,
				commandBuffer,
				state);

			VulkanCommandRecording.End(
				instance,
				commandBuffer);

			FenceCreateInfo fenceDescription = new()
			{
				SType = StructureType.FenceCreateInfo,
				Flags = FenceCreateFlags.None
			};

			Result fenceResult = api.CreateFence(
				deviceHandle,
				in fenceDescription,
				null,
				out Fence fence);

			resources.Fence = fence;

			if (fenceResult != Result.Success)
			{
				throw new InvalidOperationException(
					$"Creating the transfer fence failed: {fenceResult}.");
			}

			if (fence.Handle == 0)
			{
				throw new InvalidOperationException(
					"Vulkan returned a null transfer fence.");
			}

			device.RegisterPendingTransferTicket();
			registered = true;

			submissionAttempted = true;

			VulkanQueueSubmission.Submit(
				instance,
				device,
				commandBuffer,
				fence);

			return new VulkanBufferCopyTicket(
				instance,
				device,
				resources);
		}

		catch (Exception submissionError)
		{
			Exception? recoveryError = null;

			if (submissionAttempted)
			{
				try
				{
					Result idleResult =
						api.DeviceWaitIdle(deviceHandle);

					if (idleResult == Result.ErrorDeviceLost)
					{
						device.MarkDeviceLost();
					}

					else if (idleResult != Result.Success)
					{
						device.MarkTransferUnresolved();

						recoveryError =
							new InvalidOperationException(
								"GPU completion is unresolved after " +
								$"failed submission: {idleResult}.");
					}
				}

				catch (Exception exception)
				{
					device.MarkTransferUnresolved();
					recoveryError = exception;
				}
			}

			try
			{
				device.ReleaseOrDefer(
					resources.Release);
			}

			finally
			{
				if (registered)
				{
					device.UnregisterPendingTransferTicket();
				}
			}

			if (recoveryError is not null)
			{
				throw new AggregateException(
					"Vulkan transfer submission failed, and " +
					"completion could not be established.",
					submissionError,
					recoveryError);
			}

			throw;
		}
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}
}
