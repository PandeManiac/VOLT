
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Surface;

namespace Volt.Graphics.Vulkan.Swapchain;

/// <summary>
/// Owns a Vulkan swapchain and exposes its borrowed images.
/// </summary>
/// <remarks>
/// The logical device, Vulkan instance, and presentation surface
/// must outlive this swapchain.
///
/// This initial implementation assumes that the logical device's
/// graphics queue family also supports presentation.
///
/// Dispose before destroying the logical device or surface.
/// Callers must destroy any image views and other dependent
/// resources before disposing this swapchain.
/// </remarks>
public sealed unsafe class VulkanSwapchain : IDisposable
{
	private const int MaxEnumerationAttempts = 4;

	private readonly VulkanInstance _instance;
	private readonly PhysicalDevice _physicalDevice;
	private readonly VulkanLogicalDevice _device;
	private readonly VulkanSurface _surface;
	private readonly KhrSwapchain _extension;

	private Image[] _images;
	private CompositeAlphaFlagsKHR _compositeAlpha;
	private SwapchainKHR _handle;
	private bool _disposed;

	public Format ImageFormat { get; private set; }

	public ColorSpaceKHR ColorSpace { get; private set; }

	public PresentModeKHR PresentMode { get; private set; }

	public Extent2D Extent { get; private set; }

	public ImageUsageFlags ImageUsage { get; private set; }

	public uint ImageCount => (uint)_images.Length;

	/// <summary>
	/// Gets the borrowed native swapchain handle.
	/// </summary>
	public SwapchainKHR Handle
	{
		get
		{
			ThrowIfDisposed();
			return _handle;
		}
	}

	public VulkanSwapchain(
		VulkanInstance instance,
		PhysicalDevice physicalDevice,
		VulkanLogicalDevice device,
		VulkanSurface surface,
		uint framebufferWidth,
		uint framebufferHeight,
		bool preferMailbox = false)
	{
		ArgumentNullException.ThrowIfNull(instance);
		ArgumentNullException.ThrowIfNull(device);
		ArgumentNullException.ThrowIfNull(surface);

		if (physicalDevice.Handle == 0)
		{
			throw new ArgumentException(
				"A valid physical-device handle is required.",
				nameof(physicalDevice));
		}

		if (framebufferWidth == 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(framebufferWidth),
				"Framebuffer width must be positive.");
		}

		if (framebufferHeight == 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(framebufferHeight),
				"Framebuffer height must be positive.");
		}

		Vk api = instance.Api;
		Silk.NET.Vulkan.Instance instanceHandle = instance.Handle;
		Device deviceHandle = device.Handle;
		SurfaceKHR surfaceHandle = surface.Handle;

		VulkanSwapchainSupport support =
			VulkanSwapchainSupport.Query(
				instance,
				physicalDevice,
				surface);

		if (!support.CanCreateColorSwapchain)
		{
			throw new NotSupportedException(
				"The surface does not support a color swapchain.");
		}

		SurfaceFormatKHR format =
			VulkanSwapchainSelector.SelectSurfaceFormat(
				support.Formats.Span);

		PresentModeKHR presentMode =
			VulkanSwapchainSelector.SelectPresentMode(
				support.PresentModes.Span,
				preferMailbox);

		uint imageCount =
			VulkanSwapchainSelector.SelectImageCount(
				support.Capabilities);

		Extent2D extent =
			VulkanSwapchainSelector.SelectExtent(
				support.Capabilities,
				framebufferWidth,
				framebufferHeight);

		SurfaceCapabilitiesKHR capabilities =
			support.Capabilities;

		ImageUsageFlags imageUsage =
			ImageUsageFlags.ColorAttachmentBit;

		if ((capabilities.SupportedUsageFlags &
			 ImageUsageFlags.TransferDstBit) != 0)
		{
			imageUsage |= ImageUsageFlags.TransferDstBit;
		}

		CompositeAlphaFlagsKHR compositeAlpha =
			SelectCompositeAlpha(
				capabilities.SupportedCompositeAlpha);

		if (extent.Width == 0 || extent.Height == 0)
		{
			throw new InvalidOperationException(
				"The selected swapchain extent is zero. " +
				"Defer creation until the framebuffer has " +
				"positive dimensions.");
		}

		if (!api.TryGetDeviceExtension<KhrSwapchain>(
				instanceHandle,
				deviceHandle,
				out KhrSwapchain? extension)
			|| extension is null)
		{
			throw new NotSupportedException(
				"VK_KHR_swapchain is unavailable for the " +
				"selected Vulkan device.");
		}

		SwapchainKHR createdHandle = default;

		try
		{
			SwapchainCreateInfoKHR createInfo = new()
			{
				SType = StructureType.SwapchainCreateInfoKhr,
				Surface = surfaceHandle,
				MinImageCount = imageCount,
				ImageFormat = format.Format,
				ImageColorSpace = format.ColorSpace,
				ImageExtent = extent,
				ImageArrayLayers = 1,
				ImageUsage = imageUsage,
				ImageSharingMode = SharingMode.Exclusive,
				PreTransform = capabilities.CurrentTransform,
				CompositeAlpha = compositeAlpha,
				PresentMode = presentMode,
				Clipped = Vk.True,
				OldSwapchain = default
			};

			Result result = extension.CreateSwapchain(
				deviceHandle,
				in createInfo,
				null,
				out createdHandle);

			EnsureSuccess(result, "Create Vulkan swapchain");

			if (createdHandle.Handle == 0)
			{
				throw new InvalidOperationException(
					"Vulkan returned a null swapchain handle.");
			}

			Image[] images = EnumerateImages(
				extension,
				deviceHandle,
				createdHandle);

			if (images.Length == 0)
			{
				throw new InvalidOperationException(
					"Vulkan returned a swapchain without images.");
			}

			_instance = instance;
			_physicalDevice = physicalDevice;
			_device = device;
			_surface = surface;
			_extension = extension;
			_compositeAlpha = compositeAlpha;
			_handle = createdHandle;
			_images = images;

			ImageFormat = format.Format;
			ColorSpace = format.ColorSpace;
			PresentMode = presentMode;
			Extent = extent;
			ImageUsage = imageUsage;
		}

		catch
		{
			if (createdHandle.Handle != 0)
			{
				extension.DestroySwapchain(
					deviceHandle,
					createdHandle,
					null);
			}

			extension.Dispose();
			throw;
		}
	}

	/// <summary>
	/// Recreates the swapchain in place at a new framebuffer size.
	/// </summary>
	/// <remarks>
	/// The caller must first ensure that no acquired image or submitted
	/// frame still references the current swapchain. This method does
	/// not wait for the device.
	///
	/// The previous handle is offered to Vulkan as the old swapchain so
	/// the driver can carry over internal resources, then destroyed once
	/// the replacement exists. A failed recreation leaves this instance
	/// unchanged and usable.
	///
	/// When the plan reports that dependent objects must be rebuilt,
	/// image views, render passes, framebuffers, and pipelines that were
	/// created for the previous format or present mode are stale.
	/// </remarks>
	public VulkanSwapchainRecreationPlan Recreate(
		uint framebufferWidth,
		uint framebufferHeight)
	{
		ThrowIfDisposed();

		VulkanSwapchainSupport support = VulkanSwapchainSupport.Query(
			_instance,
			_physicalDevice,
			_surface);

		VulkanSwapchainRecreationPlan plan =
			VulkanSwapchainRecreation.Plan(
				support,
				framebufferWidth,
				framebufferHeight,
				new SurfaceFormatKHR(ImageFormat, ColorSpace),
				PresentMode,
				ImageUsage,
				_compositeAlpha);

		SwapchainCreateInfoKHR createInfo = new()
		{
			SType = StructureType.SwapchainCreateInfoKhr,
			Surface = _surface.Handle,
			MinImageCount = plan.ImageCount,
			ImageFormat = plan.Format.Format,
			ImageColorSpace = plan.Format.ColorSpace,
			ImageExtent = plan.Extent,
			ImageArrayLayers = 1,
			ImageUsage = plan.ImageUsage,
			ImageSharingMode = SharingMode.Exclusive,
			PreTransform = plan.PreTransform,
			CompositeAlpha = plan.CompositeAlpha,
			PresentMode = plan.PresentMode,
			Clipped = Vk.True,
			OldSwapchain = _handle
		};

		SwapchainKHR createdHandle = default;

		Result result = _extension.CreateSwapchain(
			_device.Handle,
			in createInfo,
			null,
			out createdHandle);

		if (result != Result.Success)
		{
			throw new InvalidOperationException(
				$"Recreate Vulkan swapchain failed: {result}.");
		}

		if (createdHandle.Handle == 0)
		{
			throw new InvalidOperationException(
				"Vulkan returned a null swapchain handle.");
		}

		Image[] images;

		try
		{
			images = EnumerateImages(
				_extension,
				_device.Handle,
				createdHandle);

			if (images.Length == 0)
			{
				throw new InvalidOperationException(
					"Vulkan returned a swapchain without images.");
			}
		}

		catch
		{
			_extension.DestroySwapchain(
				_device.Handle,
				createdHandle,
				null);

			throw;
		}

		SwapchainKHR previousHandle = _handle;

		_handle = createdHandle;
		_images = images;
		ImageFormat = plan.Format.Format;
		ColorSpace = plan.Format.ColorSpace;
		PresentMode = plan.PresentMode;
		Extent = plan.Extent;
		ImageUsage = plan.ImageUsage;
		_compositeAlpha = plan.CompositeAlpha;

		_extension.DestroySwapchain(
			_device.Handle,
			previousHandle,
			null);

		return plan;
	}

	/// <summary>
	/// Gets a borrowed image owned by the swapchain.
	/// </summary>
	/// <remarks>
	/// Swapchain images must not be destroyed independently.
	/// </remarks>
	public Image GetImage(int index)
	{
		ThrowIfDisposed();
		return _images[index];
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_ = _instance.Handle;
		_ = _surface.Handle;

		try
		{
			_device.WaitIdle();
		}

		finally
		{
			try
			{
				_extension.DestroySwapchain(
					_device.Handle,
					_handle,
					null);
			}

			finally
			{
				_extension.Dispose();
				_handle = default;
				_disposed = true;
			}
		}
	}

	private static CompositeAlphaFlagsKHR SelectCompositeAlpha(
		CompositeAlphaFlagsKHR supported)
	{
		CompositeAlphaFlagsKHR[] preferences =
		[
			CompositeAlphaFlagsKHR.OpaqueBitKhr,
			CompositeAlphaFlagsKHR.PreMultipliedBitKhr,
			CompositeAlphaFlagsKHR.PostMultipliedBitKhr,
			CompositeAlphaFlagsKHR.InheritBitKhr
		];

		foreach (CompositeAlphaFlagsKHR candidate in preferences)
		{
			if ((supported & candidate) != 0)
			{
				return candidate;
			}
		}

		throw new NotSupportedException(
			"The surface does not expose a supported " +
			"composite alpha mode.");
	}

	private static Image[] EnumerateImages(
		KhrSwapchain extension,
		Device device,
		SwapchainKHR swapchain)
	{
		for (int attempt = 0;
			 attempt < MaxEnumerationAttempts;
			 attempt++)
		{
			uint count = 0;

			EnsureSuccess(
				extension.GetSwapchainImages(
					device,
					swapchain,
					ref count,
					null),
				"Count Vulkan swapchain images");

			if (count == 0)
			{
				return [];
			}

			Image[] images =
				new Image[checked((int)count)];

			fixed (Image* pointer = images)
			{
				Result result = extension.GetSwapchainImages(
					device,
					swapchain,
					ref count,
					pointer);

				if (result == Result.Incomplete ||
					count > images.Length)
				{
					continue;
				}

				EnsureSuccess(
					result,
					"Enumerate Vulkan swapchain images");
			}

			if (count < images.Length)
			{
				Array.Resize(
					ref images,
					checked((int)count));
			}

			return images;
		}

		throw new InvalidOperationException(
			"The Vulkan swapchain image list kept changing.");
	}

	private static void EnsureSuccess(
		Result result,
		string operation)
	{
		if (result != Result.Success)
		{
			throw new InvalidOperationException(
				$"{operation} failed: {result}.");
		}
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}
}
