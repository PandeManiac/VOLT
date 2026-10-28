using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Commands;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Swapchain;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Volt.Graphics.Vulkan.Synchronization;

/// <summary>
/// Reports the result of drawing and presenting one swapchain frame.
/// </summary>
public readonly record struct VulkanSwapchainFrameResult(
	bool Submitted,
	bool PresentationAccepted,
	bool RequiresRecreation);

/// <summary>
/// Describes how far a frame that could not be completed progressed.
/// </summary>
public enum VulkanFrameFailurePhase
{
	None = 0,
	ImageAcquired = 1,
	Recorded = 2,
	Submitted = 3,
	Presented = 4
}

/// <summary>
/// Coordinates frame-slot reuse, swapchain image acquisition,
/// graphics submission, and presentation for one swapchain generation.
/// </summary>
/// <remarks>
/// This scheduler borrows its command buffers and swapchain, and owns
/// its frame synchronization objects. The caller must ensure that all
/// GPU and presentation operations are finished before disposing it.
///
/// Graphics submission fences do not prove presentation completion.
/// When swapchainMaintenance1 is enabled, this scheduler owns explicit
/// presentation fences and waits for them during normal frame reuse and
/// retirement. Otherwise, presentation retirement remains the caller's
/// responsibility.
///
/// If recording, submission, or presentation throws, this scheduler
/// becomes unusable; callers must not attempt to continue drawing with
/// a potentially acquired but unpresented swapchain image.
///
/// The graphics and presentation queue must be externally synchronized
/// against unrelated host submissions.
/// </remarks>
public sealed class VulkanSwapchainFrameScheduler : IDisposable
{
	private const ulong DefaultFenceTimeoutNanoseconds = 5_000_000_000UL;
	private const ulong DefaultAcquireTimeoutNanoseconds = 100_000_000UL;

	private readonly VulkanInstance _instance;
	private readonly VulkanLogicalDevice _device;
	private readonly VulkanSwapchain _swapchain;
	private readonly VulkanCommandBuffers _commandBuffers;
	private readonly VulkanFrameSynchronization _synchronization;
	private readonly VulkanPresentationFenceTracker? _presentationFences;
	private readonly Fence[] _imageInFlightFences;

	private int _frameIndex;
	private bool _failed;
	private bool _disposed;
	private Exception? _failure;
	private VulkanFrameFailurePhase _failurePhase;

	/// <summary>
	/// Gets the exception that abandoned the current frame, or null when
	/// the last frame completed.
	/// </summary>
	public Exception? Failure => _failure;

	/// <summary>
	/// Gets how far the abandoned frame progressed before it failed.
	/// </summary>
	public VulkanFrameFailurePhase FailurePhase => _failurePhase;

	/// <summary>
	/// Gets why the owned synchronization objects were retained instead
	/// of destroyed, or null when they were released.
	/// </summary>
	public string? UnsafeRetirementReason { get; private set; }

	public int FramesInFlight
	{
		get
		{
			ThrowIfUnavailable();
			return _synchronization.FramesInFlight;
		}
	}

	public VulkanSwapchainFrameScheduler(
		VulkanInstance instance,
		VulkanLogicalDevice device,
		VulkanSwapchain swapchain,
		VulkanCommandBuffers commandBuffers,
		int maximumFramesInFlight)
	{
		ArgumentNullException.ThrowIfNull(instance);
		ArgumentNullException.ThrowIfNull(device);
		ArgumentNullException.ThrowIfNull(swapchain);
		ArgumentNullException.ThrowIfNull(commandBuffers);

		int imageCount = checked((int)swapchain.ImageCount);

		int framesInFlight = SelectFramesInFlight(
			maximumFramesInFlight,
			imageCount,
			commandBuffers.Count);

		VulkanFrameSynchronization synchronization = new(
			instance,
			device,
			framesInFlight,
			imageCount);

		VulkanPresentationFenceTracker? presentationFences = null;

		try
		{
			if (device.SwapchainMaintenance1Enabled)
			{
				presentationFences = new VulkanPresentationFenceTracker(
					instance,
					device,
					swapchain);
			}
		}
		catch
		{
			synchronization.Dispose();
			throw;
		}

		_instance = instance;
		_device = device;
		_swapchain = swapchain;
		_commandBuffers = commandBuffers;
		_synchronization = synchronization;
		_presentationFences = presentationFences;
		_imageInFlightFences = new Fence[imageCount];
	}

	/// <summary>
	/// Records, submits, and presents one frame if an image is available.
	/// </summary>
	/// <remarks>
	/// The record callback must finish all its Vulkan commands without
	/// changing the command buffer's recording state. It must not access
	/// unrelated queues from another host thread without synchronization.
	/// The callback's integer argument is the acquired image index.
	///
	/// A suboptimal acquisition still owns an image. The scheduler
	/// submits and presents it before reporting a recreation request.
	/// A timeout or not-ready acquisition does not advance the frame slot
	/// or reset its fence.
	/// </remarks>
	public VulkanSwapchainFrameResult Draw(
		Action<CommandBuffer, int> record,
		ulong fenceTimeoutNanoseconds =
			DefaultFenceTimeoutNanoseconds,
		ulong acquireTimeoutNanoseconds =
			DefaultAcquireTimeoutNanoseconds)
	{
		ThrowIfUnavailable();
		ArgumentNullException.ThrowIfNull(record);

		try
		{
			Fence frameFence =
				_synchronization.GetInFlightFence(_frameIndex);

			VulkanQueueSubmission.WaitForFence(
				_instance,
				_device,
				frameFence,
				fenceTimeoutNanoseconds);

			Semaphore imageAvailable =
				_synchronization.GetImageAvailableSemaphore(
					_frameIndex);

			VulkanImageAcquisitionResult acquisition =
				VulkanSwapchainAcquisition.Acquire(
					_instance,
					_device,
					_swapchain,
					imageAvailable,
					acquireTimeoutNanoseconds);

			if (!acquisition.HasImage)
			{
				return new VulkanSwapchainFrameResult(
					false,
					false,
					acquisition.RequiresRecreation);
			}

			// From this point, failure cannot simply return to the
			// normal frame loop: a swapchain image is now acquired.
			_failed = true;
			_failurePhase = VulkanFrameFailurePhase.ImageAcquired;

			int imageIndex = checked((int)(acquisition.ImageIndex
				?? throw new InvalidOperationException(
					"Acquisition returned no image index.")));

			Fence previousFence = _imageInFlightFences[imageIndex];

			if (previousFence.Handle != 0 &&
				previousFence.Handle != frameFence.Handle)
			{
				VulkanQueueSubmission.WaitForFence(
					_instance,
					_device,
					previousFence,
					fenceTimeoutNanoseconds);
			}

			// Presentation fences also protect the render-finished
			// semaphore associated with this swapchain image. Wait
			// before submitting graphics work that will signal it.
			_presentationFences?.PrepareImage(
				checked((uint)imageIndex),
				fenceTimeoutNanoseconds);

			CommandBuffer commandBuffer =
				_commandBuffers.GetBuffer(_frameIndex);

			Result resetResult = _instance.Api.ResetCommandBuffer(
				commandBuffer,
				CommandBufferResetFlags.None);

			if (resetResult != Result.Success)
			{
				throw new InvalidOperationException(
					$"Command buffer reset failed: {resetResult}.");
			}

			VulkanCommandRecording.Begin(_instance, commandBuffer);
			record(commandBuffer, imageIndex);
			VulkanCommandRecording.End(_instance, commandBuffer);

			_failurePhase = VulkanFrameFailurePhase.Recorded;

			Semaphore renderFinished =
				_synchronization.GetRenderFinishedSemaphore(imageIndex);

			VulkanQueueSubmission.ResetFence(
				_instance,
				_device,
				frameFence);

			VulkanQueueSubmission.Submit(
				_instance,
				_device,
				commandBuffer,
				imageAvailable,
				PipelineStageFlags.AllCommandsBit,
				renderFinished,
				frameFence);

			_imageInFlightFences[imageIndex] = frameFence;

			_failurePhase = VulkanFrameFailurePhase.Submitted;

			VulkanSwapchainPresentationResult presentation;

			if (_presentationFences is not null)
			{
				presentation = _presentationFences.Present(
					checked((uint)imageIndex),
					renderFinished);
			}
			else
			{
				presentation = VulkanSwapchainPresentation.Present(
					_instance,
					_device,
					_swapchain,
					checked((uint)imageIndex),
					renderFinished);
			}

			_frameIndex =
				(_frameIndex + 1) % _synchronization.FramesInFlight;

			_failed = false;
			_failurePhase = VulkanFrameFailurePhase.None;

			return new VulkanSwapchainFrameResult(
				true,
				presentation.WasAccepted,
				acquisition.RequiresRecreation ||
					presentation.RequiresRecreation);
		}
		catch (TimeoutException) when (!_failed)
		{
			// Waiting for the frame-slot fence timed out before
			// acquiring an image. The pending GPU work is still
			// outstanding, but no new image ownership was taken.
			throw;
		}
		catch (Exception exception)
		{
			// Any other failure can leave ownership or native
			// synchronization state unresolved. The first exception is
			// what the caller has to act on, so it is preserved.
			_failed = true;
			_failure ??= exception;

			throw;
		}
	}

	/// <summary>
	/// Releases the owned frame and presentation synchronization objects.
	/// </summary>
	/// <remarks>
	/// The caller must first establish graphics completion. When
	/// swapchainMaintenance1 is enabled, the presentation fence tracker
	/// waits for pending presents before frame semaphores are destroyed.
	/// Without the extension, WSI retirement is the caller's responsibility.
	/// A scheduler that failed after image acquisition cannot be safely
	/// retired through this normal path.
	/// </remarks>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;

		string? unresolved = null;

		if (_failed && !TryQuiesce(out string? quiesceFailure))
		{
			unresolved = quiesceFailure;
		}

		if (unresolved is null &&
			!RetirePresentation(out string? presentationFailure))
		{
			unresolved = presentationFailure;
		}

		if (unresolved is not null)
		{
			// Destroying a semaphore or fence that a pending frame or
			// presentation may still reference is undefined behaviour.
			UnsafeRetirementReason = DescribeUnsafeRetirement(
				_failurePhase,
				_failure,
				unresolved);

			return;
		}

		_synchronization.Dispose();
	}

	/// <summary>
	/// Selects the number of usable frame slots before native allocation.
	/// </summary>
	internal static int SelectFramesInFlight(
		int maximumFramesInFlight,
		int swapchainImageCount,
		int commandBufferCount)
	{
		if (maximumFramesInFlight <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(maximumFramesInFlight));
		}

		if (swapchainImageCount <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(swapchainImageCount));
		}

		int frameCount = Math.Min(
			maximumFramesInFlight,
			swapchainImageCount);

		if (commandBufferCount < frameCount)
		{
			throw new ArgumentOutOfRangeException(
				nameof(commandBufferCount),
				"There are fewer command buffers than frame slots.");
		}

		return frameCount;
	}

	private void ThrowIfUnavailable()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		if (_failed)
		{
			throw new InvalidOperationException(
				$"The previous frame operation failed at " +
				$"{_failurePhase}. The swapchain generation cannot be reused.",
				_failure);
		}
	}

	/// <summary>
	/// Establishes that the GPU and the presentation queue have finished
	/// the abandoned frame.
	/// </summary>
	private bool TryQuiesce(out string? reason)
	{
		reason = null;

		try
		{
			_device.WaitIdle();

			// A queue wait is what makes presentation completion
			// checkable: the presentation engine holds its semaphores
			// until the present operation completes.
			Result result = _instance.Api.QueueWaitIdle(
				_device.GraphicsQueue);

			if (result != Result.Success)
			{
				reason =
					"waiting for the graphics and presentation queue " +
					$"failed ({result})";

				return false;
			}

			return true;
		}

		catch (Exception exception)
		{
			reason =
				"establishing GPU and presentation completion failed " +
				$"({exception.Message})";

			return false;
		}
	}

	private bool RetirePresentation(out string? reason)
	{
		reason = null;

		if (_presentationFences is null)
		{
			return true;
		}

		return _presentationFences.TryRetire(out reason);
	}

	/// <summary>
	/// Describes why a generation could not be retired safely and
	/// restates the failure that caused it.
	/// </summary>
	internal static string DescribeUnsafeRetirement(
		VulkanFrameFailurePhase phase,
		Exception? failure,
		string unresolvedReason)
	{
		string original = failure is null
			? "the frame did not complete"
			: $"{failure.GetType().Name}: {failure.Message}";

		return
			"Retained the synchronization objects of a frame that reached " +
			$"{phase} because {unresolvedReason}. The device releases " +
			$"them when it is destroyed. Original failure: {original}";
	}
}
