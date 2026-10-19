using System.Runtime.ExceptionServices;
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Memory;

namespace Volt.Graphics.Vulkan.Rendering;

/// <summary>
/// Owns one depth-only Vulkan image, its backing device memory, and a
/// matching depth-aspect image view.
/// </summary>
/// <remarks>
/// The attachment is independent of any swapchain, framebuffer, or
/// render pass. A renderer typically creates one attachment per
/// swapchain image so concurrently recorded frames never write the
/// same depth image.
///
/// The image is a single-sample, single-mip, single-layer optimal
/// tiling image used as a depth-stencil attachment. The image view
/// exposes only the depth aspect and is suitable for framebuffer
/// attachment and for the
/// <see cref="ImageLayout.DepthStencilAttachmentOptimal"/> render-pass
/// layout. Only depth-only formats are supported; this class does not
/// manage a stencil aspect.
///
/// Creation prefers D32Sfloat and falls back to D16Unorm when the
/// physical device does not report depth-stencil attachment support
/// for the preferred format. Format support is queried per physical
/// device; support on one GPU does not imply support on another.
///
/// This class never waits for the GPU. Callers must establish GPU
/// completion before disposing an attachment that submitted command
/// buffers reference, and must destroy framebuffers that reference
/// <see cref="View"/> before disposing the attachment. The logical
/// device and instance must outlive this attachment.
///
/// This class is not thread-safe.
/// </remarks>
public sealed unsafe class VulkanDepthAttachment : IDisposable
{
	/// <summary>
	/// Gets the depth-only formats this resource supports, in
	/// preference order.
	/// </summary>
	internal static ReadOnlySpan<Format> SupportedFormats =>
		[Format.D32Sfloat, Format.D16Unorm];

	private readonly VulkanInstance _instance;
	private readonly VulkanLogicalDevice _device;
	private readonly Vk _api;

	private readonly Format _format;
	private readonly Extent2D _extent;

	private Image _image;
	private DeviceMemory _memory;
	private ImageView _view;

	private bool _disposed;

	/// <summary>
	/// Gets the depth-only format of the attachment.
	/// </summary>
	public Format Format
	{
		get
		{
			ThrowIfDisposed();
			return _format;
		}
	}

	/// <summary>
	/// Gets the image extent.
	/// </summary>
	public Extent2D Extent
	{
		get
		{
			ThrowIfDisposed();
			return _extent;
		}
	}

	/// <summary>
	/// Gets the owned depth-aspect image view.
	/// </summary>
	/// <remarks>
	/// Ownership remains with this attachment. The view is destroyed
	/// on disposal, so every framebuffer referencing it must be
	/// destroyed first.
	/// </remarks>
	public ImageView View
	{
		get
		{
			ThrowIfDisposed();
			return _view;
		}
	}

	/// <summary>
	/// Creates a depth attachment and allocates its image, memory,
	/// and view.
	/// </summary>
	/// <param name="instance">The Vulkan instance.</param>
	/// <param name="device">The logical device that owns the image.</param>
	/// <param name="physicalDevice">
	/// The physical device that backs the logical device. Used for
	/// format-support queries and memory-type selection.
	/// </param>
	/// <param name="extent">
	/// The positive image extent. Renderers pass the current
	/// swapchain extent.
	/// </param>
	/// <param name="format">
	/// A depth-only format from <see cref="SupportedFormats"/> that
	/// the physical device supports for optimal-tiling depth-stencil
	/// attachments.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="instance"/> or <paramref name="device"/> is
	/// null.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="physicalDevice"/> is not a valid handle.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="extent"/> is zero-sized or
	/// <paramref name="format"/> is not a supported depth-only
	/// format.
	/// </exception>
	/// <exception cref="NotSupportedException">
	/// The physical device does not support the requested format as
	/// an optimal-tiling depth-stencil attachment, or no device-local
	/// memory type satisfies the image requirements.
	/// </exception>
	/// <remarks>
	/// Invalid arguments are rejected before any native resource is
	/// created. Partial initialization failures release the image,
	/// memory, and view in dependency order.
	/// </remarks>
	public VulkanDepthAttachment(
		VulkanInstance instance,
		VulkanLogicalDevice device,
		PhysicalDevice physicalDevice,
		Extent2D extent,
		Format format)
	{
		ArgumentNullException.ThrowIfNull(instance);
		ArgumentNullException.ThrowIfNull(device);

		if (physicalDevice.Handle == 0)
		{
			throw new ArgumentException(
				"A valid physical device is required.",
				nameof(physicalDevice));
		}

		ValidateExtent(extent);
		ValidateFormat(format);

		Vk api = instance.Api;
		Device deviceHandle = device.Handle;

		FormatProperties properties =
			api.GetPhysicalDeviceFormatProperties(
				physicalDevice,
				format);

		if ((properties.OptimalTilingFeatures &
			FormatFeatureFlags.DepthStencilAttachmentBit) == 0)
		{
			throw new NotSupportedException(
				$"The physical device does not support {format} as " +
				"an optimal-tiling depth-stencil attachment.");
		}

		Image image = default;
		DeviceMemory memory = default;
		ImageView view = default;

		try
		{
			image = CreateImage(
				api,
				deviceHandle,
				extent,
				format);

			memory = AllocateMemory(
				api,
				device,
				physicalDevice,
				image);

			EnsureSuccess(
				api.BindImageMemory(
					deviceHandle,
					image,
					memory,
					0),
				"Bind Vulkan depth image memory");

			view = CreateView(
				api,
				deviceHandle,
				image,
				format);
		}

		catch
		{
			// Destroy dependents first, then the image, then the
			// backing allocation it used.
			if (view.Handle != 0)
			{
				api.DestroyImageView(deviceHandle, view, null);
			}

			if (image.Handle != 0)
			{
				api.DestroyImage(deviceHandle, image, null);
			}

			if (memory.Handle != 0)
			{
				api.FreeMemory(deviceHandle, memory, null);
			}

			throw;
		}

		_instance = instance;
		_device = device;
		_api = api;

		_image = image;
		_memory = memory;
		_view = view;

		_format = format;
		_extent = extent;
	}

	/// <summary>
	/// Selects the preferred depth-only format supported by a
	/// physical device.
	/// </summary>
	/// <param name="instance">The Vulkan instance.</param>
	/// <param name="physicalDevice">
	/// The physical device to query.
	/// </param>
	/// <returns>
	/// D32Sfloat when supported, otherwise D16Unorm.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="instance"/> is null.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="physicalDevice"/> is not a valid handle.
	/// </exception>
	/// <exception cref="NotSupportedException">
	/// No candidate format supports optimal-tiling depth-stencil
	/// attachments on this physical device.
	/// </exception>
	/// <remarks>
	/// Support is queried through
	/// <see cref="Vk.GetPhysicalDeviceFormatProperties"/> for every
	/// call, so a format selected for one device is never reused for
	/// another.
	/// </remarks>
	public static Format SelectSupportedFormat(
		VulkanInstance instance,
		PhysicalDevice physicalDevice)
	{
		ArgumentNullException.ThrowIfNull(instance);

		if (physicalDevice.Handle == 0)
		{
			throw new ArgumentException(
				"A valid physical device is required.",
				nameof(physicalDevice));
		}

		ReadOnlySpan<Format> candidates = SupportedFormats;

		FormatProperties* properties =
			stackalloc FormatProperties[candidates.Length];

		for (int index = 0; index < candidates.Length; index++)
		{
			properties[index] =
				instance.Api.GetPhysicalDeviceFormatProperties(
					physicalDevice,
					candidates[index]);
		}

		Format? selected = SelectPreferredFormat(
			candidates,
			new ReadOnlySpan<FormatProperties>(
				properties,
				candidates.Length));

		return selected ?? throw new NotSupportedException(
			"No candidate depth-only format (D32Sfloat, D16Unorm) " +
			"supports optimal-tiling depth-stencil attachments on " +
			"this physical device.");
	}

	/// <summary>
	/// Selects the first candidate format whose optimal-tiling
	/// features include depth-stencil attachment support.
	/// </summary>
	/// <param name="candidates">
	/// Format candidates in preference order.
	/// </param>
	/// <param name="formatProperties">
	/// The queried properties, one entry per candidate and in the
	/// same order.
	/// </param>
	/// <returns>
	/// The selected format, or null when no candidate qualifies.
	/// </returns>
	/// <remarks>
	/// This helper performs no Vulkan access, so format preference
	/// can be validated without a device.
	/// </remarks>
	internal static Format? SelectPreferredFormat(
		ReadOnlySpan<Format> candidates,
		ReadOnlySpan<FormatProperties> formatProperties)
	{
		if (candidates.Length != formatProperties.Length)
		{
			throw new ArgumentException(
				"One format properties entry is required for each " +
				"candidate.",
				nameof(formatProperties));
		}

		for (int index = 0; index < candidates.Length; index++)
		{
			if ((formatProperties[index].OptimalTilingFeatures &
				FormatFeatureFlags.DepthStencilAttachmentBit) != 0)
			{
				return candidates[index];
			}
		}

		return null;
	}

	/// <summary>
	/// Validates that a format is a supported depth-only format.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="format"/> is not in
	/// <see cref="SupportedFormats"/>.
	/// </exception>
	internal static void ValidateFormat(Format format)
	{
		if (!IsSupportedFormat(format))
		{
			throw new ArgumentOutOfRangeException(
				nameof(format),
				format,
				"Only D32Sfloat and D16Unorm are supported depth-only " +
				"formats.");
		}
	}

	/// <summary>
	/// Validates that an extent describes a positive 2D image.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="extent"/> has a zero width or height.
	/// </exception>
	internal static void ValidateExtent(Extent2D extent)
	{
		if (extent.Width == 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(extent),
				extent,
				"A depth attachment requires a positive width.");
		}

		if (extent.Height == 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(extent),
				extent,
				"A depth attachment requires a positive height.");
		}
	}

	/// <summary>
	/// Returns whether a format is one of the supported depth-only
	/// formats.
	/// </summary>
	internal static bool IsSupportedFormat(Format format)
	{
		ReadOnlySpan<Format> formats = SupportedFormats;

		for (int index = 0; index < formats.Length; index++)
		{
			if (formats[index] == format)
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Destroys the view, image, and backing memory in dependency
	/// order.
	/// </summary>
	/// <remarks>
	/// Disposal is idempotent and never waits for the GPU. The
	/// caller must establish completion for any submitted work that
	/// referenced this attachment.
	///
	/// Cleanup continues when one native destruction fails. Multiple
	/// failures are reported as an AggregateException.
	/// </remarks>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		Device deviceHandle = _device.Handle;

		List<Exception>? failures = null;

		try
		{
			if (_view.Handle != 0)
			{
				_api.DestroyImageView(
					deviceHandle,
					_view,
					null);
			}
		}

		catch (Exception exception)
		{
			(failures ??= []).Add(exception);
		}

		try
		{
			if (_image.Handle != 0)
			{
				_api.DestroyImage(
					deviceHandle,
					_image,
					null);
			}
		}

		catch (Exception exception)
		{
			(failures ??= []).Add(exception);
		}

		try
		{
			if (_memory.Handle != 0)
			{
				_api.FreeMemory(
					deviceHandle,
					_memory,
					null);
			}
		}

		catch (Exception exception)
		{
			(failures ??= []).Add(exception);
		}

		_view = default;
		_image = default;
		_memory = default;
		_disposed = true;

		GC.KeepAlive(_instance);

		if (failures is { Count: 1 })
		{
			ExceptionDispatchInfo.Capture(failures[0]).Throw();
		}

		if (failures is { Count: > 1 })
		{
			throw new AggregateException(
				"Multiple failures occurred while disposing a Vulkan " +
				"depth attachment.",
				failures);
		}
	}

	private static Image CreateImage(
		Vk api,
		Device device,
		Extent2D extent,
		Format format)
	{
		ImageCreateInfo description = new()
		{
			SType = StructureType.ImageCreateInfo,
			ImageType = ImageType.Type2D,
			Format = format,

			Extent = new Extent3D
			{
				Width = extent.Width,
				Height = extent.Height,
				Depth = 1
			},

			MipLevels = 1,
			ArrayLayers = 1,
			Samples = SampleCountFlags.Count1Bit,
			Tiling = ImageTiling.Optimal,
			Usage = ImageUsageFlags.DepthStencilAttachmentBit,
			SharingMode = SharingMode.Exclusive,
			InitialLayout = ImageLayout.Undefined
		};

		EnsureSuccess(
			api.CreateImage(
				device,
				in description,
				null,
				out Image image),
			"Create Vulkan depth image");

		if (image.Handle == 0)
		{
			throw new InvalidOperationException(
				"Vulkan returned a null depth image.");
		}

		return image;
	}

	private static DeviceMemory AllocateMemory(
		Vk api,
		VulkanLogicalDevice device,
		PhysicalDevice physicalDevice,
		Image image)
	{
		ImageMemoryRequirementsInfo2 requirementsInfo = new()
		{
			SType = StructureType.ImageMemoryRequirementsInfo2,
			Image = image
		};

		MemoryDedicatedRequirements dedicatedRequirements = new()
		{
			SType = StructureType.MemoryDedicatedRequirements
		};

		MemoryRequirements2 requirementsResult = new()
		{
			SType = StructureType.MemoryRequirements2,
			PNext = &dedicatedRequirements
		};

		api.GetImageMemoryRequirements2(
			device.Handle,
			&requirementsInfo,
			&requirementsResult);

		MemoryRequirements requirements =
			requirementsResult.MemoryRequirements;

		ReadOnlySpan<MemoryPropertyFlags> memoryTypes =
			device.GetMemoryTypeFlags(physicalDevice);

		// Depth images require device-local memory. Reuse the
		// existing tested selector so there is one memory-type
		// policy in the engine.
		int memoryIndex = VulkanBuffer.SelectMemoryType(
			requirements.MemoryTypeBits,
			memoryTypes,
			required: MemoryPropertyFlags.DeviceLocalBit,
			preferred: MemoryPropertyFlags.None);

		if (memoryIndex < 0)
		{
			throw new NotSupportedException(
				"No device-local Vulkan memory type satisfies the " +
				"depth image requirements.");
		}

		bool dedicated =
			dedicatedRequirements.RequiresDedicatedAllocation ||
			dedicatedRequirements.PrefersDedicatedAllocation;

		MemoryDedicatedAllocateInfo dedicatedAllocation = new()
		{
			SType = StructureType.MemoryDedicatedAllocateInfo,
			Image = image
		};

		MemoryAllocateInfo allocation = new()
		{
			SType = StructureType.MemoryAllocateInfo,
			PNext = dedicated ? &dedicatedAllocation : null,
			AllocationSize = requirements.Size,
			MemoryTypeIndex = checked((uint)memoryIndex)
		};

		EnsureSuccess(
			api.AllocateMemory(
				device.Handle,
				in allocation,
				null,
				out DeviceMemory memory),
			"Allocate Vulkan depth image memory");

		if (memory.Handle == 0)
		{
			throw new InvalidOperationException(
				"Vulkan returned a null depth memory allocation.");
		}

		return memory;
	}

	private static ImageView CreateView(
		Vk api,
		Device device,
		Image image,
		Format format)
	{
		ImageViewCreateInfo description = new()
		{
			SType = StructureType.ImageViewCreateInfo,
			Image = image,
			ViewType = ImageViewType.Type2D,
			Format = format,

			SubresourceRange = new ImageSubresourceRange
			{
				AspectMask = ImageAspectFlags.DepthBit,
				BaseMipLevel = 0,
				LevelCount = 1,
				BaseArrayLayer = 0,
				LayerCount = 1
			}
		};

		EnsureSuccess(
			api.CreateImageView(
				device,
				in description,
				null,
				out ImageView view),
			"Create Vulkan depth image view");

		if (view.Handle == 0)
		{
			throw new InvalidOperationException(
				"Vulkan returned a null depth image view.");
		}

		return view;
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
