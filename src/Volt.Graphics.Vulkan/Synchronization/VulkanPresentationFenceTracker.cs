
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Commands;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Swapchain;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Volt.Graphics.Vulkan.Synchronization;

/// <summary>
/// Owns presentation fences for one Vulkan swapchain generation.
/// </summary>
/// <remarks>
/// Requires VK_EXT_swapchain_maintenance1 to be enabled on the
/// logical device.
///
/// Each swapchain image has an independent presentation fence.
/// A fence is reused only after its previous presentation completes.
///
/// This class does not own the swapchain or logical device.
/// Both must outlive this tracker.
///
/// If a presentation call throws, its completion state may be
/// unknown. The tracker becomes unusable and refuses ordinary
/// cleanup rather than potentially destroying live resources.
/// </remarks>
public sealed unsafe class VulkanPresentationFenceTracker : IDisposable
{
	private const ulong DefaultFenceTimeoutNanoseconds =
		5_000_000_000UL;

	private readonly VulkanInstance _instance;
	private readonly VulkanLogicalDevice _device;
	private readonly VulkanSwapchain _swapchain;
	private readonly Vk _api;
	private readonly Fence[] _fences;
	private readonly bool[] _pendingPresentations;
	private readonly bool[] _preparedImages;

	private bool _failed;
	private bool _disposed;
	private Exception? _failure;
	private int _unresolvedImage = -1;

	/// <summary>
	/// Gets the exception that left presentation completion unresolved,
	/// or null when every presentation call returned normally.
	/// </summary>
	public Exception? Failure => _failure;

	/// <summary>
	/// Gets why the native fences were retained instead of destroyed,
	/// or null when they were released.
	/// </summary>
	public string? UnsafeRetirementReason { get; private set; }

	/// <summary>
	/// Gets the number of tracked swapchain images.
	/// </summary>
	public int ImageCount
	{
		get
		{
			ThrowIfUnavailable();
			return _fences.Length;
		}
	}

	/// <summary>
	/// Creates an initially unsignaled presentation fence
	/// for each swapchain image.
	/// </summary>
	public VulkanPresentationFenceTracker(
		VulkanInstance instance,
		VulkanLogicalDevice device,
		VulkanSwapchain swapchain)
	{
		ArgumentNullException.ThrowIfNull(instance);
		ArgumentNullException.ThrowIfNull(device);
		ArgumentNullException.ThrowIfNull(swapchain);

		if (!device.SwapchainMaintenance1Enabled)
		{
			throw new NotSupportedException(
				"Presentation fences require the " +
				"swapchainMaintenance1 device feature.");
		}

		int imageCount = checked((int)swapchain.ImageCount);

		if (imageCount <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(swapchain),
				"The swapchain must contain at least one image.");
		}

		Vk api = instance.Api;
		Device deviceHandle = device.Handle;

		Fence[] fences = new Fence[imageCount];

		FenceCreateInfo description =
			CreateFenceDescription();

		try
		{
			for (int index = 0; index < fences.Length; index++)
			{
				Result result = api.CreateFence(
					deviceHandle,
					in description,
					null,
					out Fence fence);

				if (result != Result.Success)
				{
					throw new InvalidOperationException(
						$"Create presentation fence failed: {result}.");
				}

				if (fence.Handle == 0)
				{
					throw new InvalidOperationException(
						"Vulkan returned a null presentation fence.");
				}

				fences[index] = fence;
			}
		}
		catch
		{
			DestroyFences(
				api,
				deviceHandle,
				fences);

			throw;
		}

		_instance = instance;
		_device = device;
		_swapchain = swapchain;
		_api = api;
		_fences = fences;
		_pendingPresentations = new bool[imageCount];
		_preparedImages = new bool[imageCount];
	}

	/// <summary>
	/// Establishes that the presentation resources associated
	/// with an acquired swapchain image can be reused.
	/// </summary>
	/// <remarks>
	/// Must be called after image acquisition and BEFORE
	/// submitting graphics work that signals the image's
	/// render-finished semaphore.
	///
	/// An outstanding presentation fence is waited upon and
	/// reset before the image is marked as prepared.
	///
	/// Every successful preparation must be followed by
	/// exactly one presentation of that image.
	/// </remarks>
	public void PrepareImage(
		uint imageIndex,
		ulong timeoutNanoseconds = DefaultFenceTimeoutNanoseconds)
	{
		ThrowIfUnavailable();

		int index = ValidateImageIndex(
			imageIndex,
			_fences.Length);

		if (_preparedImages[index])
		{
			throw new InvalidOperationException(
				"This swapchain image is already prepared " +
				"for presentation.");
		}

		if (_pendingPresentations[index])
		{
			Fence fence = _fences[index];

			VulkanQueueSubmission.WaitForFence(
				_instance,
				_device,
				fence,
				timeoutNanoseconds);

			VulkanQueueSubmission.ResetFence(
				_instance,
				_device,
				fence);

			_pendingPresentations[index] = false;
		}

		_preparedImages[index] = true;
	}


	/// <summary>
	/// Presents a previously prepared swapchain image.
	/// </summary>
	/// <remarks>
	/// PrepareImage must be called before graphics submission.
	/// This method is called after that submission has been
	/// queued and will signal renderFinishedSemaphore.
	///
	/// A successful call, including a suboptimal or out-of-date
	/// presentation result, registers a pending presentation
	/// fence for eventual completion.
	///
	/// An unexpected exception leaves native completion
	/// uncertain and permanently invalidates the tracker.
	/// </remarks>
	public VulkanSwapchainPresentationResult Present(
		uint imageIndex,
		Semaphore renderFinishedSemaphore)
	{
		ThrowIfUnavailable();

		int index = ValidateImageIndex(
			imageIndex,
			_fences.Length);

		if (renderFinishedSemaphore.Handle == 0)
		{
			throw new ArgumentException(
				"A valid render-finished semaphore is required.",
				nameof(renderFinishedSemaphore));
		}

		if (!_preparedImages[index])
		{
			throw new InvalidOperationException(
				"The swapchain image must be prepared " +
				"before presentation.");
		}

		Fence fence = _fences[index];

		try
		{
			VulkanSwapchainPresentationResult result =
				VulkanSwapchainPresentation.Present(
					_instance,
					_device,
					_swapchain,
					imageIndex,
					renderFinishedSemaphore,
					fence);

			_pendingPresentations[index] = true;
			_preparedImages[index] = false;

			return result;
		}

		catch (Exception exception)
		{
			// The presentation may still have been queued before the
			// error was reported, so this image's completion is unknown
			// until its fence signals.
			_failed = true;
			_failure ??= exception;
			_unresolvedImage = index;

			throw;
		}
	}

	/// <summary>
	/// Waits for every outstanding presentation operation.
	/// </summary>
	/// <remarks>
	/// Waiting does not reset the fences. A later presentation
	/// resets its fence immediately before reusing it.
	///
	/// This must complete before resources referenced by the
	/// tracked presentations are destroyed.
	/// </remarks>
	public void WaitForAll(
		ulong timeoutNanoseconds = DefaultFenceTimeoutNanoseconds)
	{
		ThrowIfUnavailable();

		for (int index = 0;
			 index < _pendingPresentations.Length;
			 index++)
		{
			if (!_pendingPresentations[index])
			{
				continue;
			}

			VulkanQueueSubmission.WaitForFence(
				_instance,
				_device,
				_fences[index],
				timeoutNanoseconds);
		}
	}

	/// <summary>
	/// Releases the presentation fences when every tracked presentation
	/// has completed.
	/// </summary>
	/// <param name="reason">
	/// Why the fences were retained, or null when they were destroyed.
	/// </param>
	/// <returns>True when the fences were destroyed.</returns>
	/// <remarks>
	/// A fence must not be destroyed while the presentation engine can
	/// still signal it, so a presentation whose completion cannot be
	/// proven keeps its fence. The device frees it when the device is
	/// destroyed, which is why retaining is safer than guessing.
	/// </remarks>
	public bool TryRetire(out string? reason)
	{
		if (_disposed)
		{
			reason = null;
			return true;
		}

		if (!TryWaitForCompletions(out reason))
		{
			UnsafeRetirementReason = reason;
			return false;
		}

		DestroyFences(
			_api,
			_device.Handle,
			_fences);

		_disposed = true;

		return true;
	}

	/// <summary>
	/// Releases all presentation fences after completion.
	/// </summary>
	/// <remarks>
	/// Disposal never throws: an exception here would replace the frame
	/// failure that made cleanup unsafe in the first place. When a
	/// presentation cannot be proven complete, the native fences are
	/// retained and <see cref="UnsafeRetirementReason"/> explains why.
	/// </remarks>
	public void Dispose()
	{
		_ = TryRetire(out _);
	}

	private bool TryWaitForCompletions(out string? reason)
	{
		reason = null;

		for (int index = 0;
			 index < _pendingPresentations.Length;
			 index++)
		{
			bool pending = _pendingPresentations[index];

			// A failed present may have been queued without being
			// recorded as pending, so its fence is waited on as well.
			if (!pending && index != _unresolvedImage)
			{
				continue;
			}

			try
			{
				VulkanQueueSubmission.WaitForFence(
					_instance,
					_device,
					_fences[index],
					DefaultFenceTimeoutNanoseconds);

				_pendingPresentations[index] = false;
			}

			catch (Exception exception)
			{
				reason =
					$"presentation of swapchain image {index} did not " +
					$"complete ({exception.Message})";

				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Creates an initially unsignaled presentation fence.
	/// </summary>
	internal static FenceCreateInfo CreateFenceDescription()
	{
		return new FenceCreateInfo
		{
			SType = StructureType.FenceCreateInfo,
			Flags = FenceCreateFlags.None
		};
	}

	/// <summary>
	/// Validates a swapchain image index without native access.
	/// </summary>
	internal static int ValidateImageIndex(
		uint imageIndex,
		int imageCount)
	{
		if (imageCount <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(imageCount));
		}

		if (imageIndex >= (uint)imageCount)
		{
			throw new ArgumentOutOfRangeException(
				nameof(imageIndex));
		}

		return checked((int)imageIndex);
	}

	private static void DestroyFences(
		Vk api,
		Device device,
		Fence[] fences)
	{
		for (int index = fences.Length - 1;
			 index >= 0;
			 index--)
		{
			if (fences[index].Handle == 0)
			{
				continue;
			}

			api.DestroyFence(
				device,
				fences[index],
				null);

			fences[index] = default;
		}
	}

	private void ThrowIfUnavailable()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		if (_failed)
		{
			throw new InvalidOperationException(
				"Presentation completion is unresolved. " +
				"The tracker cannot be safely reused or disposed.");
		}
	}
}
