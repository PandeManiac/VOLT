using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Commands;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Memory;
using Volt.Graphics.Vulkan.Swapchain;
using Volt.Ui.Rendering;
using Volt.Ui.Text;

namespace Volt.Ui.Vulkan;

/// <summary>
/// Uploads <see cref="UiGeometry"/> batches and draws them in one
/// Vulkan render pass over the current swapchain image.
/// </summary>
/// <remarks>
/// <para>
/// Solid batches sample a white texel in layer zero of a single R8
/// texture array. Each UI font owns one additional layer; glyph
/// coverage is uploaded when an atlas revision changes. One descriptor
/// set and one pipeline are shared by every batch, while a scissor and
/// a layer push constant change per batch.
/// </para>
/// <para>
/// Vertex and index buffers are host-visible and duplicated per frame
/// slot so a CPU upload cannot overwrite data still being read by the
/// previous submission. Font updates synchronously wait for the device
/// because they are exceptional and must not race a frame.
/// </para>
/// <para>
/// The renderer does not own the swapchain, image views, logical
/// device, or instance. They must outlive it, and all submitted frames
/// must be idle before disposal.
/// </para>
/// </remarks>
public sealed unsafe class VulkanUiRenderer : IDisposable
{
	private const int FramesInFlight = 2;
	private const ulong InitialVertexBytes = 2 * 1024 * 1024;
	private const ulong InitialIndexBytes = 1024 * 1024;

	private readonly VulkanInstance _instance;
	private readonly VulkanLogicalDevice _device;
	private readonly PhysicalDevice _physicalDevice;
	private readonly Vk _api;
	private readonly Format _format;
	private readonly Extent2D _extent;
	private readonly Framebuffer[] _framebuffers;
	private readonly VulkanBuffer[] _vertexBuffers;
	private readonly VulkanBuffer[] _indexBuffers;
	private readonly Dictionary<UiFont, int> _fontLayers = [];
	private readonly Dictionary<UiFont, int> _uploadedRevisions = [];
	private UiVertex[] _clipSpaceVertices = [];

	private RenderPass _renderPass;
	private PipelineLayout _pipelineLayout;
	private Pipeline _pipeline;
	private DescriptorSetLayout _descriptorSetLayout;
	private DescriptorPool _descriptorPool;
	private DescriptorSet _descriptorSet;
	private Sampler _sampler;
	private Image _atlasImage;
	private DeviceMemory _atlasMemory;
	private ImageView _atlasView;
	private ImageLayout _atlasLayout = ImageLayout.Undefined;
	private ulong _atlasBytes;
	private bool _disposed;

	public VulkanUiRenderer(
		VulkanInstance instance,
		VulkanLogicalDevice device,
		PhysicalDevice physicalDevice,
		VulkanSwapchain swapchain,
		VulkanSwapchainImageViews imageViews,
		IReadOnlyList<UiFont> fonts,
		string vertexShaderPath,
		string fragmentShaderPath)
	{
		ArgumentNullException.ThrowIfNull(instance);
		ArgumentNullException.ThrowIfNull(device);
		ArgumentNullException.ThrowIfNull(swapchain);
		ArgumentNullException.ThrowIfNull(imageViews);
		ArgumentNullException.ThrowIfNull(fonts);
		ArgumentException.ThrowIfNullOrWhiteSpace(vertexShaderPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(fragmentShaderPath);

		if (fonts.Count == 0)
		{
			throw new ArgumentException(
				"At least one UI font is required.",
				nameof(fonts));
		}

		if (imageViews.Count != checked((int)swapchain.ImageCount))
		{
			throw new ArgumentException(
				"Every swapchain image requires an image view.",
				nameof(imageViews));
		}

		_instance = instance;
		_device = device;
		_physicalDevice = physicalDevice;
		_api = instance.Api;
		_format = swapchain.ImageFormat;
		_extent = swapchain.Extent;
		_framebuffers = new Framebuffer[imageViews.Count];
		_vertexBuffers = new VulkanBuffer[FramesInFlight];
		_indexBuffers = new VulkanBuffer[FramesInFlight];
		_atlasBytes = checked(
			(ulong)UiFontAtlas.Size *
			(ulong)UiFontAtlas.Size *
			(ulong)(fonts.Count + 1));

		for (int index = 0; index < fonts.Count; index++)
		{
			if (!_fontLayers.ContainsKey(fonts[index]))
			{
				_fontLayers.Add(fonts[index], index + 1);
			}
		}

		int layerCount = _fontLayers.Count + 1;

		try
		{
			CreateAtlasImage(layerCount);
			CreateSampler();
			UploadFontAtlases();

			_descriptorSetLayout = CreateDescriptorSetLayout();
			_descriptorPool = CreateDescriptorPool();
			_descriptorSet = AllocateDescriptorSet();
			UpdateDescriptorSet();

			_renderPass = CreateRenderPass();
			_pipelineLayout = CreatePipelineLayout();
			_pipeline = CreatePipeline(
				vertexShaderPath,
				fragmentShaderPath);

			for (int index = 0; index < _framebuffers.Length; index++)
			{
				_framebuffers[index] = CreateFramebuffer(
					imageViews.GetView(index));
			}

			for (int index = 0; index < FramesInFlight; index++)
			{
				_vertexBuffers[index] = CreateBuffer(
					InitialVertexBytes,
					BufferUsageFlags.VertexBufferBit);

				_indexBuffers[index] = CreateBuffer(
					InitialIndexBytes,
					BufferUsageFlags.IndexBufferBit);
			}
		}

		catch
		{
			DisposeResources();
			throw;
		}
	}

	public Extent2D Extent => _extent;

	/// <summary>
	/// Records a complete UI pass into an acquired swapchain image.
	/// </summary>
	/// <param name="frameSlot">
	/// Frame index modulo the number of internal buffer slots.
	/// </param>
	public void Render(
		CommandBuffer commandBuffer,
		int imageIndex,
		int frameSlot,
		ReadOnlySpan<UiVertex> vertices,
		ReadOnlySpan<uint> indices,
		ReadOnlySpan<UiBatch> batches)
	{
		ThrowIfDisposed();

		if (commandBuffer.Handle == 0)
		{
			throw new ArgumentException(
				"A valid command buffer is required.",
				nameof(commandBuffer));
		}

		if (imageIndex < 0 || imageIndex >= _framebuffers.Length)
		{
			throw new ArgumentOutOfRangeException(nameof(imageIndex));
		}

		if (frameSlot < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(frameSlot));
		}

		bool hasGeometry = vertices.Length > 0 && indices.Length > 0;
		int slot = frameSlot % FramesInFlight;

		if (hasGeometry)
		{
			EnsureBufferCapacity(
				slot,
				(ulong)vertices.Length * (ulong)sizeof(UiVertex),
				(ulong)indices.Length * sizeof(uint));

			_vertexBuffers[slot].Write(
				ToClipSpace(vertices, _extent));

			_indexBuffers[slot].Write(indices);
		}

		if (HasNewGlyphs())
		{
			UploadFontAtlases();
		}

		ClearValue clear = new()
		{
			Color = new ClearColorValue(
				float32_0: 0.063f,
				float32_1: 0.075f,
				float32_2: 0.094f,
				float32_3: 1.0f)
		};

		RenderPassBeginInfo begin = new()
		{
			SType = StructureType.RenderPassBeginInfo,
			RenderPass = _renderPass,
			Framebuffer = _framebuffers[imageIndex],
			RenderArea = new Rect2D
			{
				Offset = new Offset2D(0, 0),
				Extent = _extent
			},
			ClearValueCount = 1,
			PClearValues = &clear
		};

		_api.CmdBeginRenderPass(
			commandBuffer,
			&begin,
			SubpassContents.Inline);

		if (hasGeometry)
		{
			_api.CmdBindPipeline(
				commandBuffer,
				PipelineBindPoint.Graphics,
				_pipeline);

			DescriptorSet descriptorSet = _descriptorSet;

			_api.CmdBindDescriptorSets(
				commandBuffer,
				PipelineBindPoint.Graphics,
				_pipelineLayout,
				firstSet: 0,
				descriptorSetCount: 1,
				&descriptorSet,
				dynamicOffsetCount: 0,
				pDynamicOffsets: null);

			Silk.NET.Vulkan.Buffer vertexBuffer =
				_vertexBuffers[slot].Handle;

			_api.CmdBindVertexBuffers(
				commandBuffer,
				firstBinding: 0,
				bindingCount: 1,
				&vertexBuffer,
				null);

			_api.CmdBindIndexBuffer(
				commandBuffer,
				_indexBuffers[slot].Handle,
				0,
				IndexType.Uint32);

			foreach (UiBatch batch in batches)
			{
				if (batch.IndexCount <= 0)
				{
					continue;
				}

				int left = Math.Clamp(batch.Scissor.X, 0, (int)_extent.Width);
				int top = Math.Clamp(batch.Scissor.Y, 0, (int)_extent.Height);
				int right = Math.Clamp(
					batch.Scissor.Right,
					left,
					(int)_extent.Width);
				int bottom = Math.Clamp(
					batch.Scissor.Bottom,
					top,
					(int)_extent.Height);

				if (right <= left || bottom <= top)
				{
					continue;
				}

				Rect2D scissor = new()
				{
					Offset = new Offset2D(left, top),
					Extent = new Extent2D(
						(uint)(right - left),
						(uint)(bottom - top))
				};

				_api.CmdSetScissor(
					commandBuffer,
					0,
					1,
					&scissor);

				float layer = ResolveLayer(batch.Texture);					_api.CmdPushConstants(
						commandBuffer,
						_pipelineLayout,
						ShaderStageFlags.FragmentBit,
						0,
						sizeof(float),
						&layer);

				_api.CmdDrawIndexed(
					commandBuffer,
					(uint)batch.IndexCount,
					instanceCount: 1,
					(uint)batch.FirstIndex,
					vertexOffset: 0,
					firstInstance: 0);
			}
		}

		_api.CmdEndRenderPass(commandBuffer);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		DisposeResources();
		_disposed = true;
	}

	private void DisposeResources()
	{
		for (int index = 0; index < _vertexBuffers.Length; index++)
		{
			_vertexBuffers[index]?.Dispose();
			_indexBuffers[index]?.Dispose();
		}

		foreach (Framebuffer framebuffer in _framebuffers)
		{
			if (framebuffer.Handle != 0)
			{
				_api.DestroyFramebuffer(
					_device.Handle,
					framebuffer,
					null);
			}
		}

		if (_pipeline.Handle != 0)
		{
			_api.DestroyPipeline(_device.Handle, _pipeline, null);
			_pipeline = default;
		}

		if (_pipelineLayout.Handle != 0)
		{
			_api.DestroyPipelineLayout(
				_device.Handle,
				_pipelineLayout,
				null);
			_pipelineLayout = default;
		}

		if (_descriptorPool.Handle != 0)
		{
			_api.DestroyDescriptorPool(
				_device.Handle,
				_descriptorPool,
				null);
			_descriptorPool = default;
		}

		if (_descriptorSetLayout.Handle != 0)
		{
			_api.DestroyDescriptorSetLayout(
				_device.Handle,
				_descriptorSetLayout,
				null);
			_descriptorSetLayout = default;
		}

		if (_renderPass.Handle != 0)
		{
			_api.DestroyRenderPass(
				_device.Handle,
				_renderPass,
				null);
			_renderPass = default;
		}

		if (_sampler.Handle != 0)
		{
			_api.DestroySampler(_device.Handle, _sampler, null);
			_sampler = default;
		}

		if (_atlasView.Handle != 0)
		{
			_api.DestroyImageView(_device.Handle, _atlasView, null);
			_atlasView = default;
		}

		if (_atlasImage.Handle != 0)
		{
			_api.DestroyImage(_device.Handle, _atlasImage, null);
			_atlasImage = default;
		}

		if (_atlasMemory.Handle != 0)
		{
			_api.FreeMemory(_device.Handle, _atlasMemory, null);
			_atlasMemory = default;
		}
	}

	private void CreateAtlasImage(int layerCount)
	{
		ImageCreateInfo imageInfo = new()
		{
			SType = StructureType.ImageCreateInfo,
			ImageType = ImageType.Type2D,
			Format = Format.R8Unorm,
			Extent = new Extent3D(
				UiFontAtlas.Size,
				UiFontAtlas.Size,
				1),
			MipLevels = 1,
			ArrayLayers = (uint)layerCount,
			Samples = SampleCountFlags.Count1Bit,
			Tiling = ImageTiling.Optimal,
			Usage = ImageUsageFlags.SampledBit |
				ImageUsageFlags.TransferDstBit,
			SharingMode = SharingMode.Exclusive,
			InitialLayout = ImageLayout.Undefined
		};

		EnsureSuccess(
			_api.CreateImage(
				_device.Handle,
				&imageInfo,
				null,
				out _atlasImage),
			"Create UI atlas image");

		_api.GetImageMemoryRequirements(
			_device.Handle,
			_atlasImage,
			out MemoryRequirements requirements);

		uint memoryType = checked((uint)_device.FindMemoryTypeIndex(
			_physicalDevice,
			requirements.MemoryTypeBits,
			MemoryPropertyFlags.DeviceLocalBit));

		MemoryAllocateInfo allocate = new()
		{
			SType = StructureType.MemoryAllocateInfo,
			AllocationSize = requirements.Size,
			MemoryTypeIndex = memoryType
		};

		EnsureSuccess(
			_api.AllocateMemory(
				_device.Handle,
				&allocate,
				null,
				out _atlasMemory),
			"Allocate UI atlas memory");

		EnsureSuccess(
			_api.BindImageMemory(
				_device.Handle,
				_atlasImage,
				_atlasMemory,
				0),
			"Bind UI atlas memory");

		ImageViewCreateInfo viewInfo = new()
		{
			SType = StructureType.ImageViewCreateInfo,
			Image = _atlasImage,
			ViewType = ImageViewType.Type2DArray,
			Format = Format.R8Unorm,
			SubresourceRange = new ImageSubresourceRange(
				ImageAspectFlags.ColorBit,
				0,
				1,
				0,
				(uint)layerCount)
		};

		EnsureSuccess(
			_api.CreateImageView(
				_device.Handle,
				&viewInfo,
				null,
				out _atlasView),
			"Create UI atlas view");
	}

	private void CreateSampler()
	{
		SamplerCreateInfo samplerInfo = new()
		{
			SType = StructureType.SamplerCreateInfo,
			MagFilter = Filter.Linear,
			MinFilter = Filter.Linear,
			MipmapMode = SamplerMipmapMode.Linear,
			AddressModeU = SamplerAddressMode.ClampToEdge,
			AddressModeV = SamplerAddressMode.ClampToEdge,
			AddressModeW = SamplerAddressMode.ClampToEdge,
			MinLod = 0f,
			MaxLod = 0f
		};

		EnsureSuccess(
			_api.CreateSampler(
				_device.Handle,
				&samplerInfo,
				null,
				out _sampler),
			"Create UI atlas sampler");
	}

	private DescriptorSetLayout CreateDescriptorSetLayout()
	{
		DescriptorSetLayoutBinding binding = new()
		{
			Binding = 0,
			DescriptorType = DescriptorType.CombinedImageSampler,
			DescriptorCount = 1,
			StageFlags = ShaderStageFlags.FragmentBit
		};

		DescriptorSetLayoutCreateInfo description = new()
		{
			SType = StructureType.DescriptorSetLayoutCreateInfo,
			BindingCount = 1,
			PBindings = &binding
		};

		EnsureSuccess(
			_api.CreateDescriptorSetLayout(
				_device.Handle,
				&description,
				null,
				out DescriptorSetLayout layout),
			"Create UI descriptor set layout");

		return layout;
	}

	private DescriptorPool CreateDescriptorPool()
	{
		DescriptorPoolSize size = new()
		{
			Type = DescriptorType.CombinedImageSampler,
			DescriptorCount = 1
		};

		DescriptorPoolCreateInfo description = new()
		{
			SType = StructureType.DescriptorPoolCreateInfo,
			MaxSets = 1,
			PoolSizeCount = 1,
			PPoolSizes = &size
		};

		EnsureSuccess(
			_api.CreateDescriptorPool(
				_device.Handle,
				&description,
				null,
				out DescriptorPool pool),
			"Create UI descriptor pool");

		return pool;
	}

	private DescriptorSet AllocateDescriptorSet()
	{
		DescriptorSetLayout layout = _descriptorSetLayout;

		DescriptorSetAllocateInfo description = new()
		{
			SType = StructureType.DescriptorSetAllocateInfo,
			DescriptorPool = _descriptorPool,
			DescriptorSetCount = 1,
			PSetLayouts = &layout
		};

		EnsureSuccess(
			_api.AllocateDescriptorSets(
				_device.Handle,
				&description,
				out DescriptorSet set),
			"Allocate UI descriptor set");

		return set;
	}

	private void UpdateDescriptorSet()
	{
		DescriptorImageInfo image = new()
		{
			Sampler = _sampler,
			ImageView = _atlasView,
			ImageLayout = ImageLayout.ShaderReadOnlyOptimal
		};

		WriteDescriptorSet write = new()
		{
			SType = StructureType.WriteDescriptorSet,
			DstSet = _descriptorSet,
			DstBinding = 0,
			DstArrayElement = 0,
			DescriptorCount = 1,
			DescriptorType = DescriptorType.CombinedImageSampler,
			PImageInfo = &image
		};

		_api.UpdateDescriptorSets(
			_device.Handle,
			1,
			&write,
			0,
			null);
	}

	private RenderPass CreateRenderPass()
	{
		AttachmentDescription color = new()
		{
			Format = _format,
			Samples = SampleCountFlags.Count1Bit,
			LoadOp = AttachmentLoadOp.Clear,
			StoreOp = AttachmentStoreOp.Store,
			StencilLoadOp = AttachmentLoadOp.DontCare,
			StencilStoreOp = AttachmentStoreOp.DontCare,
			InitialLayout = ImageLayout.Undefined,
			FinalLayout = ImageLayout.PresentSrcKhr
		};

		AttachmentReference colorReference = new()
		{
			Attachment = 0,
			Layout = ImageLayout.ColorAttachmentOptimal
		};

		SubpassDescription subpass = new()
		{
			PipelineBindPoint = PipelineBindPoint.Graphics,
			ColorAttachmentCount = 1,
			PColorAttachments = &colorReference
		};

		SubpassDependency* dependencies = stackalloc SubpassDependency[2];

		dependencies[0] = new SubpassDependency
		{
			SrcSubpass = Vk.SubpassExternal,
			DstSubpass = 0,
			SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
			DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
			DstAccessMask = AccessFlags.ColorAttachmentWriteBit
		};

		dependencies[1] = new SubpassDependency
		{
			SrcSubpass = 0,
			DstSubpass = Vk.SubpassExternal,
			SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
			DstStageMask = PipelineStageFlags.BottomOfPipeBit,
			SrcAccessMask = AccessFlags.ColorAttachmentWriteBit
		};

		RenderPassCreateInfo description = new()
		{
			SType = StructureType.RenderPassCreateInfo,
			AttachmentCount = 1,
			PAttachments = &color,
			SubpassCount = 1,
			PSubpasses = &subpass,
			DependencyCount = 2,
			PDependencies = dependencies
		};

		EnsureSuccess(
			_api.CreateRenderPass(
				_device.Handle,
				&description,
				null,
				out RenderPass pass),
			"Create UI render pass");

		return pass;
	}

	private PipelineLayout CreatePipelineLayout()
	{
		PushConstantRange range = new()
		{
			StageFlags = ShaderStageFlags.FragmentBit,
			Offset = 0,
			Size = sizeof(float)
		};

		DescriptorSetLayout layout = _descriptorSetLayout;

		PipelineLayoutCreateInfo description = new()
		{
			SType = StructureType.PipelineLayoutCreateInfo,
			SetLayoutCount = 1,
			PSetLayouts = &layout,
			PushConstantRangeCount = 1,
			PPushConstantRanges = &range
		};

		EnsureSuccess(
			_api.CreatePipelineLayout(
				_device.Handle,
				&description,
				null,
				out PipelineLayout pipelineLayout),
			"Create UI pipeline layout");

		return pipelineLayout;
	}

	private Pipeline CreatePipeline(
		string vertexShaderPath,
		string fragmentShaderPath)
	{
		ShaderModule vertex = CreateShaderModule(vertexShaderPath);
		ShaderModule fragment = CreateShaderModule(fragmentShaderPath);

		try
		{
			PipelineShaderStageCreateInfo* stages =
				stackalloc PipelineShaderStageCreateInfo[2];

			byte* entryPoint = stackalloc byte[]
			{
				(byte)'m',
				(byte)'a',
				(byte)'i',
				(byte)'n',
				0
			};

			stages[0] = new PipelineShaderStageCreateInfo
			{
				SType = StructureType.PipelineShaderStageCreateInfo,
				Stage = ShaderStageFlags.VertexBit,
				Module = vertex,
				PName = entryPoint
			};

			stages[1] = new PipelineShaderStageCreateInfo
			{
				SType = StructureType.PipelineShaderStageCreateInfo,
				Stage = ShaderStageFlags.FragmentBit,
				Module = fragment,
				PName = entryPoint
			};

			VertexInputBindingDescription binding = new()
			{
				Binding = 0,
				Stride = (uint)Unsafe.SizeOf<UiVertex>(),
				InputRate = VertexInputRate.Vertex
			};

			VertexInputAttributeDescription* attributes =
				stackalloc VertexInputAttributeDescription[3];

			attributes[0] = new VertexInputAttributeDescription
			{
				Location = 0,
				Binding = 0,
				Format = Format.R32G32Sfloat,
				Offset = 0
			};

			attributes[1] = new VertexInputAttributeDescription
			{
				Location = 1,
				Binding = 0,
				Format = Format.R32G32Sfloat,
				Offset = 8
			};

			attributes[2] = new VertexInputAttributeDescription
			{
				Location = 2,
				Binding = 0,
				Format = Format.R8G8B8A8Unorm,
				Offset = 16
			};

			PipelineVertexInputStateCreateInfo vertexInput = new()
			{
				SType = StructureType.PipelineVertexInputStateCreateInfo,
				VertexBindingDescriptionCount = 1,
				PVertexBindingDescriptions = &binding,
				VertexAttributeDescriptionCount = 3,
				PVertexAttributeDescriptions = attributes
			};

			PipelineInputAssemblyStateCreateInfo assembly = new()
			{
				SType = StructureType.PipelineInputAssemblyStateCreateInfo,
				Topology = PrimitiveTopology.TriangleList,
				PrimitiveRestartEnable = Vk.False
			};

			Viewport viewport = new()
			{
				X = 0f,
				Y = 0f,
				Width = _extent.Width,
				Height = _extent.Height,
				MinDepth = 0f,
				MaxDepth = 1f
			};

			Rect2D scissor = new()
			{
				Offset = new Offset2D(0, 0),
				Extent = _extent
			};

			PipelineViewportStateCreateInfo viewportState = new()
			{
				SType = StructureType.PipelineViewportStateCreateInfo,
				ViewportCount = 1,
				PViewports = &viewport,
				ScissorCount = 1,
				PScissors = &scissor
			};

			PipelineRasterizationStateCreateInfo rasterization = new()
			{
				SType = StructureType.PipelineRasterizationStateCreateInfo,
				DepthClampEnable = Vk.False,
				RasterizerDiscardEnable = Vk.False,
				PolygonMode = PolygonMode.Fill,
				CullMode = CullModeFlags.None,
				FrontFace = FrontFace.CounterClockwise,
				DepthBiasEnable = Vk.False,
				LineWidth = 1f
			};

			PipelineMultisampleStateCreateInfo multisample = new()
			{
				SType = StructureType.PipelineMultisampleStateCreateInfo,
				RasterizationSamples = SampleCountFlags.Count1Bit,
				SampleShadingEnable = Vk.False
			};

			PipelineColorBlendAttachmentState attachment = new()
			{
				BlendEnable = Vk.True,
				SrcColorBlendFactor = BlendFactor.SrcAlpha,
				DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
				ColorBlendOp = BlendOp.Add,
				SrcAlphaBlendFactor = BlendFactor.One,
				DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
				AlphaBlendOp = BlendOp.Add,
				ColorWriteMask =
					ColorComponentFlags.RBit |
					ColorComponentFlags.GBit |
					ColorComponentFlags.BBit |
					ColorComponentFlags.ABit
			};

			PipelineColorBlendStateCreateInfo blend = new()
			{
				SType = StructureType.PipelineColorBlendStateCreateInfo,
				LogicOpEnable = Vk.False,
				AttachmentCount = 1,
				PAttachments = &attachment
			};

			DynamicState dynamicState = DynamicState.Scissor;

			PipelineDynamicStateCreateInfo dynamic = new()
			{
				SType = StructureType.PipelineDynamicStateCreateInfo,
				DynamicStateCount = 1,
				PDynamicStates = &dynamicState
			};

			GraphicsPipelineCreateInfo description = new()
			{
				SType = StructureType.GraphicsPipelineCreateInfo,
				StageCount = 2,
				PStages = stages,
				PVertexInputState = &vertexInput,
				PInputAssemblyState = &assembly,
				PViewportState = &viewportState,
				PRasterizationState = &rasterization,
				PMultisampleState = &multisample,
				PColorBlendState = &blend,
				PDynamicState = &dynamic,
				Layout = _pipelineLayout,
				RenderPass = _renderPass,
				Subpass = 0
			};

			Pipeline pipeline = default;
			Result result = _api.CreateGraphicsPipelines(
				_device.Handle,
				default,
				1,
				&description,
				null,
				&pipeline);

			EnsureSuccess(result, "Create UI graphics pipeline");

			return pipeline;
		}

		finally
		{
			if (fragment.Handle != 0)
			{
				_api.DestroyShaderModule(
					_device.Handle,
					fragment,
					null);
			}

			if (vertex.Handle != 0)
			{
				_api.DestroyShaderModule(
					_device.Handle,
					vertex,
					null);
			}
		}
	}

	private Framebuffer CreateFramebuffer(ImageView view)
	{
		ImageView attachment = view;

		FramebufferCreateInfo description = new()
		{
			SType = StructureType.FramebufferCreateInfo,
			RenderPass = _renderPass,
			AttachmentCount = 1,
			PAttachments = &attachment,
			Width = _extent.Width,
			Height = _extent.Height,
			Layers = 1
		};

		EnsureSuccess(
			_api.CreateFramebuffer(
				_device.Handle,
				&description,
				null,
				out Framebuffer framebuffer),
			"Create UI framebuffer");

		return framebuffer;
	}

	private ShaderModule CreateShaderModule(string path)
	{
		byte[] spirv = File.ReadAllBytes(path);

		if (spirv.Length < 20 ||
			spirv.Length % sizeof(uint) != 0 ||
			BitConverter.ToUInt32(spirv, 0) != 0x07230203u)
		{
			throw new InvalidDataException(
				$"Shader is not valid SPIR-V: '{path}'.");
		}

		fixed (byte* code = spirv)
		{
			ShaderModuleCreateInfo description = new()
			{
				SType = StructureType.ShaderModuleCreateInfo,
				CodeSize = checked((nuint)spirv.Length),
				PCode = (uint*)code
			};

			EnsureSuccess(
				_api.CreateShaderModule(
					_device.Handle,
					&description,
					null,
					out ShaderModule module),
				$"Create shader module '{path}'");

			return module;
		}
	}

	private void UploadFontAtlases()
	{
		if (_atlasImage.Handle == 0)
		{
			return;
		}

		_device.WaitIdle();

		byte[] pixels = new byte[_atlasBytes];

		// Layer zero is the shared opaque texel used by solid and
		// unsupported image batches.
		int layerSize = UiFontAtlas.Size * UiFontAtlas.Size;
		pixels.AsSpan(0, layerSize).Fill(0xFF);

		foreach (KeyValuePair<UiFont, int> pair in _fontLayers)
		{
			pair.Key.Atlas.Pixels.CopyTo(
				pixels,
				pair.Value * layerSize);

			_uploadedRevisions[pair.Key] =
				pair.Key.Atlas.Revision;
		}

		using VulkanBuffer staging = new(
			_instance,
			_device,
			_physicalDevice,
			_atlasBytes,
			BufferUsageFlags.TransferSrcBit,
			MemoryPropertyFlags.HostVisibleBit |
				MemoryPropertyFlags.HostCoherentBit);

		staging.Write(pixels);

		using VulkanCommandPool pool = new(
			_instance,
			_device,
			CommandPoolCreateFlags.TransientBit);

		using VulkanCommandBuffers buffers = new(
			_instance,
			_device,
			pool,
			1);

		CommandBuffer commandBuffer = buffers.GetBuffer(0);

		VulkanCommandRecording.Begin(_instance, commandBuffer);

		ImageLayout oldLayout = _atlasLayout;

		ImageMemoryBarrier toTransfer = new()
		{
			SType = StructureType.ImageMemoryBarrier,
			OldLayout = oldLayout,
			NewLayout = ImageLayout.TransferDstOptimal,
			SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
			DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
			Image = _atlasImage,
			SubresourceRange = new ImageSubresourceRange(
				ImageAspectFlags.ColorBit,
				0,
				1,
				0,
				(uint)(_fontLayers.Count + 1)),
			SrcAccessMask = oldLayout == ImageLayout.Undefined
				? AccessFlags.None
				: AccessFlags.ShaderReadBit,
			DstAccessMask = AccessFlags.TransferWriteBit
		};

		_api.CmdPipelineBarrier(
			commandBuffer,
			oldLayout == ImageLayout.Undefined
				? PipelineStageFlags.TopOfPipeBit
				: PipelineStageFlags.FragmentShaderBit,
			PipelineStageFlags.TransferBit,
			0,
			0,
			null,
			0,
			null,
			1,
			&toTransfer);

		BufferImageCopy copy = new()
		{
			BufferOffset = 0,
			BufferRowLength = 0,
			BufferImageHeight = 0,
			ImageSubresource = new ImageSubresourceLayers(
				ImageAspectFlags.ColorBit,
				0,
				0,
				(uint)(_fontLayers.Count + 1)),
			ImageOffset = new Offset3D(0, 0, 0),
			ImageExtent = new Extent3D(
				UiFontAtlas.Size,
				UiFontAtlas.Size,
				1)
		};

		_api.CmdCopyBufferToImage(
			commandBuffer,
			staging.Handle,
			_atlasImage,
			ImageLayout.TransferDstOptimal,
			1,
			&copy);

		ImageMemoryBarrier toShader = new()
		{
			SType = StructureType.ImageMemoryBarrier,
			OldLayout = ImageLayout.TransferDstOptimal,
			NewLayout = ImageLayout.ShaderReadOnlyOptimal,
			SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
			DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
			Image = _atlasImage,
			SubresourceRange = new ImageSubresourceRange(
				ImageAspectFlags.ColorBit,
				0,
				1,
				0,
				(uint)(_fontLayers.Count + 1)),
			SrcAccessMask = AccessFlags.TransferWriteBit,
			DstAccessMask = AccessFlags.ShaderReadBit
		};

		_api.CmdPipelineBarrier(
			commandBuffer,
			PipelineStageFlags.TransferBit,
			PipelineStageFlags.FragmentShaderBit,
			0,
			0,
			null,
			0,
			null,
			1,
			&toShader);

		_atlasLayout = ImageLayout.ShaderReadOnlyOptimal;

		VulkanCommandRecording.End(_instance, commandBuffer);

		FenceCreateInfo fenceInfo = new()
		{
			SType = StructureType.FenceCreateInfo
		};

		EnsureSuccess(
			_api.CreateFence(
				_device.Handle,
				&fenceInfo,
				null,
				out Fence fence),
			"Create UI atlas upload fence");

		try
		{
			VulkanQueueSubmission.Submit(
				_instance,
				_device,
				commandBuffer,
				fence);

			VulkanQueueSubmission.WaitForFence(
				_instance,
				_device,
				fence,
				5_000_000_000UL);
		}

		finally
		{
			_api.DestroyFence(_device.Handle, fence, null);
		}
	}

	private bool HasNewGlyphs()
	{
		foreach (KeyValuePair<UiFont, int> pair in _fontLayers)
		{
			if (!_uploadedRevisions.TryGetValue(
					pair.Key,
					out int revision) ||
				revision != pair.Key.Atlas.Revision)
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Converts pixel-space UI vertices into Vulkan clip space.
	/// </summary>
	/// <remarks>
	/// <see cref="UiGeometry"/> deliberately emits physical pixels so
	/// any backend can consume it and so clipping stays in one
	/// coordinate system. The vertex shader forwards positions
	/// unchanged, so this backend performs the single Y flip and
	/// normalization here. The scratch array is reused across frames.
	/// </remarks>
	private ReadOnlySpan<UiVertex> ToClipSpace(
		ReadOnlySpan<UiVertex> vertices,
		Extent2D extent)
	{
		if (_clipSpaceVertices.Length < vertices.Length)
		{
			_clipSpaceVertices = new UiVertex[vertices.Length];
		}

		float width = MathF.Max(1f, extent.Width);
		float height = MathF.Max(1f, extent.Height);

		for (int index = 0; index < vertices.Length; index++)
		{
			UiVertex vertex = vertices[index];

			_clipSpaceVertices[index] = new UiVertex(
				new System.Numerics.Vector2(
					(vertex.Position.X / width * 2f) - 1f,
					1f - (vertex.Position.Y / height * 2f)),
				vertex.Uv,
				vertex.Color);
		}

		return _clipSpaceVertices.AsSpan(0, vertices.Length);
	}

	private float ResolveLayer(UiTextureKey texture)
	{
		if (texture.Kind == UiTextureKind.Font &&
			texture.Font is not null &&
			_fontLayers.TryGetValue(texture.Font, out int layer))
		{
			return layer;
		}

		return 0f;
	}

	private VulkanBuffer CreateBuffer(
		ulong size,
		BufferUsageFlags usage)
	{
		return new VulkanBuffer(
			_instance,
			_device,
			_physicalDevice,
			size,
			usage,
			MemoryPropertyFlags.HostVisibleBit |
				MemoryPropertyFlags.HostCoherentBit);
	}

	private void EnsureBufferCapacity(
		int slot,
		ulong vertexBytes,
		ulong indexBytes)
	{
		if (_vertexBuffers[slot].Size < vertexBytes ||
			_indexBuffers[slot].Size < indexBytes)
		{
			_device.WaitIdle();

			_vertexBuffers[slot].Dispose();
			_indexBuffers[slot].Dispose();

			_vertexBuffers[slot] = CreateBuffer(
				Math.Max(InitialVertexBytes, vertexBytes * 2),
				BufferUsageFlags.VertexBufferBit);

			_indexBuffers[slot] = CreateBuffer(
				Math.Max(InitialIndexBytes, indexBytes * 2),
				BufferUsageFlags.IndexBufferBit);
		}
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
