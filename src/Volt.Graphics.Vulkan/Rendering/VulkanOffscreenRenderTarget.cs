using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Memory;
using Volt.Graphics.Vulkan.Swapchain;

namespace Volt.Graphics.Vulkan.Rendering;

/// <summary>
/// Owns one offscreen colour target, its depth attachment, render
/// pass, and framebuffer.
/// </summary>
/// <remarks>
/// <para>
/// The colour image is created with colour-attachment and sampled
/// usage and is exposed through a single-layer 2D-array view, which is
/// equally valid as a framebuffer attachment and as a
/// <c>sampler2DArray</c> texture. That is what lets one image be both
/// rendered into and sampled by the UI.
/// </para>
/// <para>
/// The target tracks the layout of both attachments so a caller only
/// has to state intent: <see cref="EnsureRenderingLayout"/> before
/// recording a pass, and <see cref="EnsureSampled"/> (or the
/// <see cref="IVulkanSampledImage"/> contract) before another renderer
/// samples the colour image. Every transition goes through
/// <see cref="VulkanImageLayoutTransition"/>, so stage and access masks
/// are never guessed at the call site.
/// </para>
/// <para>
/// The render pass clears the colour attachment to the caller's clear
/// value and the depth attachment to one with a less-than comparison,
/// and leaves both attachments in their attachment-optimal layouts.
/// </para>
/// <para>
/// Resizing and disposal wait for the device. Callers must not hold
/// command buffers recorded against the target across a resize. This
/// class is not thread-safe and must be created, resized, and disposed
/// on the thread that records frames. The instance and logical device
/// must outlive it.
/// </para>
/// </remarks>
public sealed unsafe class VulkanOffscreenRenderTarget :
	IVulkanSampledImage,
	IDisposable
{
	/// <summary>
	/// Gets the colour formats this target supports, in preference
	/// order.
	/// </summary>
	/// <remarks>
	/// B8G8R8A8_UNORM has mandatory colour-attachment and sampled
	/// support in Vulkan, and R8G8B8A8_UNORM is the widely supported
	/// fallback.
	/// </remarks>
	public static ReadOnlySpan<Format> SupportedColorFormats =>
		[Format.B8G8R8A8Unorm, Format.R8G8B8A8Unorm];

	private readonly VulkanInstance _instance;
	private readonly VulkanLogicalDevice _device;
	private readonly PhysicalDevice _physicalDevice;
	private readonly Vk _api;
	private readonly Format _colorFormat;
	private readonly Format _depthFormat;
	private readonly RenderPass _renderPass;

	private Image _colorImage;
	private DeviceMemory _colorMemory;
	private ImageView _colorView;
	private ImageLayout _colorLayout = ImageLayout.Undefined;

	private VulkanDepthAttachment? _depth;

	private Framebuffer _framebuffer;
	private Extent2D _extent;
	private bool _disposed;

	/// <summary>
	/// Gets the current target extent in pixels.
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
	/// Gets the colour format of this target.
	/// </summary>
	public Format ColorFormat
	{
		get
		{
			ThrowIfDisposed();
			return _colorFormat;
		}
	}

	/// <summary>
	/// Gets the depth format of this target.
	/// </summary>
	public Format DepthFormat
	{
		get
		{
			ThrowIfDisposed();
			return _depthFormat;
		}
	}

	/// <summary>
	/// Gets the colour format another renderer samples.
	/// </summary>
	/// <remarks>
	/// This is the explicit implementation of
	/// <see cref="IVulkanSampledImage.Format"/>; the public member is
	/// named <see cref="ColorFormat"/> so it cannot be confused with
	/// <see cref="DepthFormat"/>.
	/// </remarks>
	Format IVulkanSampledImage.Format => _colorFormat;

	/// <summary>
	/// Gets the render pass that renders into this target.
	/// </summary>
	public RenderPass RenderPass
	{
		get
		{
			ThrowIfDisposed();
			return _renderPass;
		}
	}

	/// <summary>
	/// Gets the framebuffer that binds this target's attachments.
	/// </summary>
	public Framebuffer Framebuffer
	{
		get
		{
			ThrowIfDisposed();
			return _framebuffer;
		}
	}

	/// <summary>
	/// Gets the owned colour image handle.
	/// </summary>
	public Image Image
	{
		get
		{
			ThrowIfDisposed();
			return _colorImage;
		}
	}

	/// <summary>
	/// Gets the owned colour image view handle.
	/// </summary>
	public ImageView View
	{
		get
		{
			ThrowIfDisposed();
			return _colorView;
		}
	}

	/// <summary>
	/// Gets the layout the colour image is currently in.
	/// </summary>
	public ImageLayout ColorLayout
	{
		get
		{
			ThrowIfDisposed();
			return _colorLayout;
		}
	}

	/// <summary>
	/// Creates a target and its attachments for a positive extent.
	/// </summary>
	/// <param name="instance">The Vulkan instance.</param>
	/// <param name="device">The logical device that owns the images.</param>
	/// <param name="physicalDevice">
	/// The physical device behind the logical device. Used for format
	/// support queries and memory-type selection.
	/// </param>
	/// <param name="extent">The positive target extent.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="instance"/> or <paramref name="device"/> is null.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="physicalDevice"/> is not a valid handle.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="extent"/> has a zero width or height.
	/// </exception>
	/// <exception cref="NotSupportedException">
	/// No supported colour or depth format is usable on this device.
	/// </exception>
	public VulkanOffscreenRenderTarget(
		VulkanInstance instance,
		VulkanLogicalDevice device,
		PhysicalDevice physicalDevice,
		Extent2D extent)
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

		_instance = instance;
		_device = device;
		_physicalDevice = physicalDevice;
		_api = instance.Api;
		_extent = extent;

		_colorFormat = SelectSupportedColorFormat(
			instance,
			physicalDevice);

		_depthFormat = VulkanDepthAttachment.SelectSupportedFormat(
			instance,
			physicalDevice);

		RenderPass renderPass = default;

		try
		{
			renderPass = CreateRenderPass(
				_api,
				device.Handle,
				_colorFormat,
				_depthFormat);

			CreateAttachments();
		}

		catch
		{
			if (renderPass.Handle != 0)
			{
				_api.DestroyRenderPass(device.Handle, renderPass, null);
			}

			DestroyAttachments();

			throw;
		}

		_renderPass = renderPass;
	}

	/// <summary>
	/// Selects the preferred colour format this device supports for an
	/// optimal-tiling attachment that can also be sampled.
	/// </summary>
	/// <exception cref="NotSupportedException">
	/// No candidate format qualifies.
	/// </exception>
	/// <remarks>
	/// This helper performs no Vulkan access beyond format queries, and
	/// its preference logic is exercised without a device through
	/// <see cref="SelectPreferredColorFormat"/>.
	/// </remarks>
	public static Format SelectSupportedColorFormat(
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

		ReadOnlySpan<Format> candidates = SupportedColorFormats;

		FormatProperties* properties =
			stackalloc FormatProperties[candidates.Length];

		for (int index = 0; index < candidates.Length; index++)
		{
			properties[index] =
				instance.Api.GetPhysicalDeviceFormatProperties(
					physicalDevice,
					candidates[index]);
		}

		Format? selected = SelectPreferredColorFormat(
			candidates,
			new ReadOnlySpan<FormatProperties>(
				properties,
				candidates.Length));

		return selected ?? throw new NotSupportedException(
			"No candidate colour format (B8G8R8A8_UNORM, " +
			"R8G8B8A8_UNORM) supports both optimal-tiling colour " +
			"attachments and linear sampling on this physical device.");
	}

	/// <summary>
	/// Selects the first candidate format that supports colour
	/// attachments and linear sampling.
	/// </summary>
	/// <param name="candidates">Format candidates in preference order.</param>
	/// <param name="formatProperties">
	/// The queried properties, one entry per candidate and in the same
	/// order.
	/// </param>
	/// <returns>The selected format, or null when none qualifies.</returns>
	/// <remarks>
	/// This helper performs no Vulkan access, so format preference can
	/// be validated without a device.
	/// </remarks>
	internal static Format? SelectPreferredColorFormat(
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
			FormatFeatureFlags required =
				FormatFeatureFlags.ColorAttachmentBit |
				FormatFeatureFlags.SampledImageFilterLinearBit;

			if ((formatProperties[index].OptimalTilingFeatures &
				required) == required)
			{
				return candidates[index];
			}
		}

		return null;
	}

	/// <summary>
	/// Validates that an extent describes a positive 2D target.
	/// </summary>
	internal static void ValidateExtent(Extent2D extent)
	{
		if (extent.Width == 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(extent),
				extent,
				"An offscreen target requires a positive width.");
		}

		if (extent.Height == 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(extent),
				extent,
				"An offscreen target requires a positive height.");
		}
	}

	/// <summary>
	/// Records the transition to the layout the render pass declares
	/// for its colour attachment.
	/// </summary>
	/// <param name="commandBuffer">
	/// A command buffer in the recording state.
	/// </param>
	/// <returns>
	/// True when a barrier was recorded, false when the colour image was
	/// already attachment-optimal.
	/// </returns>
	/// <remarks>
	/// This must be recorded outside a render pass and before
	/// <see cref="RecordBegin"/>. The depth attachment declares an
	/// undefined initial layout because the pass always clears it, which
	/// is the same contract the engine's swapchain-backed mesh renderer
	/// uses.
	/// </remarks>
	public bool EnsureRenderingLayout(CommandBuffer commandBuffer)
	{
		ThrowIfDisposed();

		return TransitionColor(
			commandBuffer,
			ImageLayout.ColorAttachmentOptimal);
	}

	/// <summary>
	/// Records the transition that makes the colour image sampleable.
	/// </summary>
	/// <param name="commandBuffer">
	/// A command buffer in the recording state.
	/// </param>
	/// <returns>
	/// True when a barrier was recorded, false when the colour image
	/// was already sampleable.
	/// </returns>
	public bool EnsureSampled(CommandBuffer commandBuffer)
	{
		ThrowIfDisposed();

		return TransitionColor(
			commandBuffer,
			ImageLayout.ShaderReadOnlyOptimal);
	}

	/// <summary>
	/// Records the start of a render pass that clears and draws this
	/// target.
	/// </summary>
	/// <param name="commandBuffer">
	/// A command buffer in the recording state, outside any render pass.
	/// </param>
	/// <param name="clearColor">The colour to clear to.</param>
	public void RecordBegin(
		CommandBuffer commandBuffer,
		in ClearColorValue clearColor)
	{
		ThrowIfDisposed();

		ClearValue* clearValues = stackalloc ClearValue[2];

		clearValues[0] = new ClearValue
		{
			Color = clearColor
		};

		clearValues[1] = new ClearValue
		{
			DepthStencil = new ClearDepthStencilValue
			{
				Depth = 1.0f,
				Stencil = 0
			}
		};

		RenderPassBeginInfo begin = new()
		{
			SType = StructureType.RenderPassBeginInfo,
			RenderPass = _renderPass,
			Framebuffer = _framebuffer,
			RenderArea = new Rect2D
			{
				Offset = new Offset2D(0, 0),
				Extent = _extent
			},
			ClearValueCount = 2,
			PClearValues = clearValues
		};

		_api.CmdBeginRenderPass(
			commandBuffer,
			&begin,
			SubpassContents.Inline);
	}

	/// <summary>
	/// Records the end of the pass started by
	/// <see cref="RecordBegin"/>.
	/// </summary>
	public void RecordEnd(CommandBuffer commandBuffer)
	{
		ThrowIfDisposed();

		_api.CmdEndRenderPass(commandBuffer);
	}

	/// <summary>
	/// Recreates the attachments at a new extent.
	/// </summary>
	/// <param name="extent">The new positive extent.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="extent"/> has a zero width or height.
	/// </exception>
	/// <remarks>
	/// A resize waits for the device because submitted frames may still
	/// reference the old attachments. Framebuffers recorded against the
	/// old view must be discarded, which is why the target owns the
	/// framebuffer rather than the caller.
	/// </remarks>
	public void Resize(Extent2D extent)
	{
		ThrowIfDisposed();
		ValidateExtent(extent);

		if (extent.Width == _extent.Width &&
			extent.Height == _extent.Height)
		{
			return;
		}

		_device.WaitIdle();

		DestroyAttachments();

		_extent = extent;
		_colorLayout = ImageLayout.Undefined;

		CreateAttachments();
	}

	/// <summary>
	/// Destroys the attachments, framebuffer, and render pass.
	/// </summary>
	/// <remarks>
	/// Disposal waits for the device so no submitted frame can still
	/// reference the images, and is idempotent. The caller must not
	/// sample the target afterwards.
	/// </remarks>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;

		try
		{
			_device.WaitIdle();
		}

		catch
		{
			// Device loss during shutdown must not prevent the
			// remaining native resources from being released.
		}

		DestroyAttachments();

		if (_renderPass.Handle != 0)
		{
			_api.DestroyRenderPass(_device.Handle, _renderPass, null);
		}
	}

	private bool TransitionColor(
		CommandBuffer commandBuffer,
		ImageLayout target)
	{
		if (_colorLayout == target)
		{
			return false;
		}

		VulkanImageLayoutTransition.Record(
			_instance,
			commandBuffer,
			_colorImage,
			_colorLayout,
			target,
			ImageAspectFlags.ColorBit);

		_colorLayout = target;

		return true;
	}

	private void CreateAttachments()
	{
		CreateColorImage();

		_depth = new VulkanDepthAttachment(
			_instance,
			_device,
			_physicalDevice,
			_extent,
			_depthFormat);

		ImageView* attachments = stackalloc ImageView[2];

		attachments[0] = _colorView;
		attachments[1] = _depth.View;

		FramebufferCreateInfo description = new()
		{
			SType = StructureType.FramebufferCreateInfo,
			RenderPass = _renderPass,
			AttachmentCount = 2,
			PAttachments = attachments,
			Width = _extent.Width,
			Height = _extent.Height,
			Layers = 1
		};

		EnsureSuccess(
			_api.CreateFramebuffer(
				_device.Handle,
				&description,
				null,
				out _framebuffer),
			"Create offscreen framebuffer");
	}

	private void CreateColorImage()
	{
		ImageCreateInfo imageInfo = new()
		{
			SType = StructureType.ImageCreateInfo,
			ImageType = ImageType.Type2D,
			Format = _colorFormat,
			Extent = new Extent3D(
				_extent.Width,
				_extent.Height,
				1),
			MipLevels = 1,
			ArrayLayers = 1,
			Samples = SampleCountFlags.Count1Bit,
			Tiling = ImageTiling.Optimal,
			Usage = ImageUsageFlags.ColorAttachmentBit |
				ImageUsageFlags.SampledBit,
			SharingMode = SharingMode.Exclusive,
			InitialLayout = ImageLayout.Undefined
		};

		EnsureSuccess(
			_api.CreateImage(
				_device.Handle,
				&imageInfo,
				null,
				out _colorImage),
			"Create offscreen colour image");

		_api.GetImageMemoryRequirements(
			_device.Handle,
			_colorImage,
			out MemoryRequirements requirements);

		ReadOnlySpan<MemoryPropertyFlags> memoryTypes =
			_device.GetMemoryTypeFlags(_physicalDevice);

		int memoryIndex = VulkanBuffer.SelectMemoryType(
			requirements.MemoryTypeBits,
			memoryTypes,
			required: MemoryPropertyFlags.DeviceLocalBit,
			preferred: MemoryPropertyFlags.None);

		if (memoryIndex < 0)
		{
			throw new NotSupportedException(
				"No device-local Vulkan memory type satisfies the " +
				"offscreen colour image requirements.");
		}

		MemoryAllocateInfo allocate = new()
		{
			SType = StructureType.MemoryAllocateInfo,
			AllocationSize = requirements.Size,
			MemoryTypeIndex = checked((uint)memoryIndex)
		};

		EnsureSuccess(
			_api.AllocateMemory(
				_device.Handle,
				&allocate,
				null,
				out _colorMemory),
			"Allocate offscreen colour memory");

		EnsureSuccess(
			_api.BindImageMemory(
				_device.Handle,
				_colorImage,
				_colorMemory,
				0),
			"Bind offscreen colour memory");

		// A single-layer 2D-array view serves both purposes: it is a
		// valid framebuffer colour attachment and the arrayed type the
		// UI's sampler2DArray declaration requires.
		ImageViewCreateInfo viewInfo = new()
		{
			SType = StructureType.ImageViewCreateInfo,
			Image = _colorImage,
			ViewType = ImageViewType.Type2DArray,
			Format = _colorFormat,
			SubresourceRange = new ImageSubresourceRange(
				ImageAspectFlags.ColorBit,
				0,
				1,
				0,
				1)
		};

		EnsureSuccess(
			_api.CreateImageView(
				_device.Handle,
				&viewInfo,
				null,
				out _colorView),
			"Create offscreen colour view");
	}

	private void DestroyAttachments()
	{
		if (_framebuffer.Handle != 0)
		{
			_api.DestroyFramebuffer(
				_device.Handle,
				_framebuffer,
				null);

			_framebuffer = default;
		}

		// The depth attachment is referenced by the framebuffer, so it
		// is released after it.
		_depth?.Dispose();
		_depth = null;

		if (_colorView.Handle != 0)
		{
			_api.DestroyImageView(
				_device.Handle,
				_colorView,
				null);

			_colorView = default;
		}

		if (_colorImage.Handle != 0)
		{
			_api.DestroyImage(
				_device.Handle,
				_colorImage,
				null);

			_colorImage = default;
		}

		if (_colorMemory.Handle != 0)
		{
			_api.FreeMemory(
				_device.Handle,
				_colorMemory,
				null);

			_colorMemory = default;
		}
	}

	private static RenderPass CreateRenderPass(
		Vk api,
		Device device,
		Format colorFormat,
		Format depthFormat)
	{
		AttachmentDescription* attachments =
			stackalloc AttachmentDescription[2];

		attachments[0] = new AttachmentDescription
		{
			Format = colorFormat,
			Samples = SampleCountFlags.Count1Bit,
			LoadOp = AttachmentLoadOp.Clear,
			StoreOp = AttachmentStoreOp.Store,
			StencilLoadOp = AttachmentLoadOp.DontCare,
			StencilStoreOp = AttachmentStoreOp.DontCare,

			// The caller records an explicit transition to this layout,
			// so the pass can declare it rather than Undefined.
			InitialLayout = ImageLayout.ColorAttachmentOptimal,
			FinalLayout = ImageLayout.ColorAttachmentOptimal
		};

		attachments[1] = new AttachmentDescription
		{
			Format = depthFormat,
			Samples = SampleCountFlags.Count1Bit,
			LoadOp = AttachmentLoadOp.Clear,
			StoreOp = AttachmentStoreOp.DontCare,
			StencilLoadOp = AttachmentLoadOp.DontCare,
			StencilStoreOp = AttachmentStoreOp.DontCare,

			// A cleared depth attachment carries no data between
			// frames, so declaring the contents undefined is both true
			// and one transition cheaper per frame.
			InitialLayout = ImageLayout.Undefined,
			FinalLayout = ImageLayout.DepthStencilAttachmentOptimal
		};

		AttachmentReference colorReference = new()
		{
			Attachment = 0,
			Layout = ImageLayout.ColorAttachmentOptimal
		};

		AttachmentReference depthReference = new()
		{
			Attachment = 1,
			Layout = ImageLayout.DepthStencilAttachmentOptimal
		};

		SubpassDescription subpass = new()
		{
			PipelineBindPoint = PipelineBindPoint.Graphics,
			ColorAttachmentCount = 1,
			PColorAttachments = &colorReference,
			PDepthStencilAttachment = &depthReference
		};

		SubpassDependency* dependencies = stackalloc SubpassDependency[2];

		dependencies[0] = new SubpassDependency
		{
			SrcSubpass = Vk.SubpassExternal,
			DstSubpass = 0,
			SrcStageMask = PipelineStageFlags.TopOfPipeBit,
			DstStageMask =
				PipelineStageFlags.ColorAttachmentOutputBit |
				PipelineStageFlags.EarlyFragmentTestsBit,
			SrcAccessMask = AccessFlags.None,
			DstAccessMask =
				AccessFlags.ColorAttachmentWriteBit |
				AccessFlags.DepthStencilAttachmentWriteBit
		};

		dependencies[1] = new SubpassDependency
		{
			SrcSubpass = 0,
			DstSubpass = Vk.SubpassExternal,
			SrcStageMask =
				PipelineStageFlags.ColorAttachmentOutputBit |
				PipelineStageFlags.EarlyFragmentTestsBit |
				PipelineStageFlags.LateFragmentTestsBit,
			DstStageMask = PipelineStageFlags.FragmentShaderBit,
			SrcAccessMask =
				AccessFlags.ColorAttachmentWriteBit |
				AccessFlags.DepthStencilAttachmentWriteBit,
			DstAccessMask = AccessFlags.ShaderReadBit
		};

		RenderPassCreateInfo description = new()
		{
			SType = StructureType.RenderPassCreateInfo,
			AttachmentCount = 2,
			PAttachments = attachments,
			SubpassCount = 1,
			PSubpasses = &subpass,
			DependencyCount = 2,
			PDependencies = dependencies
		};

		EnsureSuccess(
			api.CreateRenderPass(
				device,
				&description,
				null,
				out RenderPass renderPass),
			"Create offscreen render pass");

		return renderPass;
	}

	private static void EnsureSuccess(Result result, string operation)
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
