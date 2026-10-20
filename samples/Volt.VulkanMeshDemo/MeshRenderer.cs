using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Rendering;
using Volt.Graphics.Vulkan.Swapchain;

namespace Volt.VulkanMeshDemo;

/// <summary>
/// Identifies how one planned mesh draw is recorded.
/// </summary>
internal enum MeshDrawKind
{
	/// <summary>
	/// The complete mesh is recorded through the whole-mesh
	/// overload.
	/// </summary>
	CompleteMesh,

	/// <summary>
	/// A selected element range is recorded through the range
	/// overload.
	/// </summary>
	ElementRange
}

/// <summary>
/// Describes one draw the sample records each frame.
/// </summary>
/// <remarks>
/// MeshIndex selects the uploaded mesh. FirstElement is a first
/// vertex for a non-indexed mesh and a first index for an indexed
/// mesh. Model places that mesh instance in world space; two draws
/// of the same mesh may use different model transforms.
/// </remarks>
internal readonly record struct MeshDraw(
	MeshDrawKind Kind,
	int MeshIndex,
	uint FirstElement,
	uint ElementCount,
	Matrix4x4 Model);

/// <summary>
/// Push constants shared by every draw of the sample pipeline.
/// </summary>
/// <remarks>
/// One 64-byte <see cref="Matrix4x4"/> per draw. The bytes are pushed
/// in natural <see cref="Matrix4x4"/> memory order and read by GLSL
/// as a column-major <c>mat4</c>, which transposes them implicitly;
/// see <see cref="CameraTransforms"/>. This stays well inside
/// Vulkan's guaranteed minimum of 128 push-constant bytes.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct MeshPushConstants
{
	public readonly Matrix4x4 Mvp;

	public MeshPushConstants(Matrix4x4 mvp)
	{
		Mvp = mvp;
	}
}

/// <summary>
/// Owns the graphics pipeline and framebuffers for one swapchain
/// generation.
/// </summary>
/// <remarks>
/// Mesh buffers are owned by the application and uploaded once.
/// Each frame records one render pass across the three uploaded
/// meshes through <see cref="VulkanMeshDrawCommands"/>. The gallery
/// and depth-occlusion scenes record four draws: the complete
/// non-indexed triangle, the two constituent triangles of the
/// 16-bit indexed quad as two partial index ranges, and the complete
/// 32-bit indexed hexagon. The camera scene records five draws: the
/// triangle twice with different model matrices, the same two quad
/// ranges, and the hexagon. Every draw shares one graphics pipeline,
/// the uploaded mesh buffers, and one 64-byte MVP push constant that
/// is rewritten before each draw. The camera scene reads the
/// application's camera controller on every recorded frame, so
/// movement reaches the GPU without recreating the renderer.
///
/// The render pass has a color attachment and a depth attachment.
/// One <see cref="VulkanDepthAttachment"/> is created per swapchain
/// image so concurrently active frames never share a depth image,
/// and all of them use one depth format selected for this
/// generation. Depth testing uses a clear value of 1.0 and the
/// conventional less-than comparison.
///
/// The sample-only negative control can disable depth testing and
/// depth writes for this pipeline. The depth attachment, render
/// pass, draw plan, and geometry are unchanged in that mode.
///
/// The swapchain, image views, logical device, and instance must
/// outlive this renderer. The caller must establish GPU completion
/// before disposing it, because the meshes and depth attachments
/// are referenced by submitted command buffers. Framebuffers are
/// destroyed before the depth views they reference.
/// </remarks>
internal sealed unsafe class MeshRenderer : IDisposable
{
	private readonly VulkanInstance _instance;
	private readonly VulkanLogicalDevice _device;
	private readonly Vk _api;
	private readonly Framebuffer[] _framebuffers;
	private readonly DemoMeshes _meshes;
	private readonly MeshDraw[] _drawPlan;
	private readonly VulkanDepthAttachment[] _depthAttachments;
	private readonly DemoScene _scene;
	private readonly CameraController? _camera;

	private PipelineLayout _pipelineLayout;
	private RenderPass _renderPass;
	private Pipeline _pipeline;

	private bool _disposed;

	public Extent2D Extent { get; }

	/// <summary>
	/// Gets the depth-only format shared by every attachment in this
	/// swapchain generation.
	/// </summary>
	public Format DepthFormat { get; }

	/// <summary>
	/// Gets the number of depth attachments, one per swapchain image.
	/// </summary>
	public int DepthAttachmentCount => _depthAttachments.Length;

	public MeshRenderer(
		VulkanInstance instance,
		VulkanLogicalDevice device,
		PhysicalDevice physicalDevice,
		VulkanSwapchain swapchain,
		VulkanSwapchainImageViews imageViews,
		DemoMeshes meshes,
		CameraController? camera,
		string vertexShaderPath,
		string fragmentShaderPath,
		bool depthTestEnabled = true)
	{
		ArgumentNullException.ThrowIfNull(instance);
		ArgumentNullException.ThrowIfNull(device);
		ArgumentNullException.ThrowIfNull(swapchain);
		ArgumentNullException.ThrowIfNull(imageViews);
		ArgumentNullException.ThrowIfNull(meshes);
		ArgumentException.ThrowIfNullOrWhiteSpace(vertexShaderPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(fragmentShaderPath);

		if (physicalDevice.Handle == 0)
		{
			throw new ArgumentException(
				"A valid physical device is required.",
				nameof(physicalDevice));
		}

		if (imageViews.Count != checked((int)swapchain.ImageCount))
		{
			throw new ArgumentException(
				"Every swapchain image requires an image view.",
				nameof(imageViews));
		}

		if ((swapchain.ImageUsage & ImageUsageFlags.ColorAttachmentBit) == 0)
		{
			throw new NotSupportedException(
				"The swapchain must support color attachments.");
		}

		MeshDraw[] drawPlan = CreateDrawPlan(meshes.Scene, meshes.Geometries);

		Vk api = instance.Api;
		Device deviceHandle = device.Handle;

		Format depthFormat =
			VulkanDepthAttachment.SelectSupportedFormat(
				instance,
				physicalDevice);

		VulkanDepthAttachment[] depthAttachments =
			new VulkanDepthAttachment[imageViews.Count];

		RenderPass renderPass = default;
		PipelineLayout pipelineLayout = default;
		Pipeline pipeline = default;

		Framebuffer[] framebuffers =
			new Framebuffer[imageViews.Count];

		try
		{
			for (int index = 0;
				index < depthAttachments.Length;
				index++)
			{
				depthAttachments[index] = new VulkanDepthAttachment(
					instance,
					device,
					physicalDevice,
					swapchain.Extent,
					depthFormat);
			}

			renderPass = CreateRenderPass(
				api,
				deviceHandle,
				swapchain.ImageFormat,
				depthFormat);

			pipelineLayout = CreatePipelineLayout(api, deviceHandle);

			pipeline = CreatePipeline(
				api,
				deviceHandle,
				renderPass,
				pipelineLayout,
				swapchain.Extent,
				vertexShaderPath,
				fragmentShaderPath,
				depthTestEnabled);

			for (int index = 0; index < framebuffers.Length; index++)
			{
				framebuffers[index] = CreateFramebuffer(
					api,
					deviceHandle,
					renderPass,
					imageViews.GetView(index),
					depthAttachments[index].View,
					swapchain.Extent);
			}
		}

		catch
		{
			// Framebuffers reference the depth views, so they are
			// destroyed before the attachments are released.
			DestroyResources(
				api,
				deviceHandle,
				framebuffers,
				pipeline,
				pipelineLayout,
				renderPass);

			DestroyDepthAttachments(depthAttachments);

			throw;
		}

		_instance = instance;
		_device = device;
		_api = api;
		_framebuffers = framebuffers;
		_meshes = meshes;
		_drawPlan = drawPlan;
		_depthAttachments = depthAttachments;
		_scene = meshes.Scene;
		_camera = camera;

		_renderPass = renderPass;
		_pipelineLayout = pipelineLayout;
		_pipeline = pipeline;

		Extent = swapchain.Extent;
		DepthFormat = depthFormat;
	}

	/// <summary>
	/// Builds the ordered draw plan recorded every frame.
	/// </summary>
	/// <remarks>
	/// The plan is derived from the scene selection and the uploaded
	/// mesh geometries, so the sample's draw sequence and every draw
	/// instance's model matrix can be inspected and tested without a
	/// Vulkan device. The complete non-indexed triangle and the
	/// complete 32-bit indexed hexagon use the whole-mesh overload.
	/// The 16-bit indexed quad is drawn as its two constituent
	/// triangles: two partial index ranges of the same uploaded mesh.
	/// The camera scene adds a second triangle instance that reuses
	/// the same uploaded mesh with a different model transform.
	/// </remarks>
	internal static MeshDraw[] CreateDrawPlan(
		DemoScene scene,
		IReadOnlyList<MeshGeometry> geometries)
	{
		ArgumentNullException.ThrowIfNull(geometries);

		if (geometries.Count != 3 ||
			geometries[0].IndexFormat != MeshIndexFormat.None ||
			geometries[1].IndexFormat != MeshIndexFormat.UInt16 ||
			geometries[2].IndexFormat != MeshIndexFormat.UInt32)
		{
			throw new ArgumentException(
				"The sample requires a non-indexed, a 16-bit indexed, " +
				"and a 32-bit indexed mesh, in that order.",
				nameof(geometries));
		}

		uint triangleVertexCount =
			checked((uint)geometries[0].Vertices.Length);

		uint quadIndexCount =
			checked((uint)geometries[1].Indices16.Length);

		uint quadTriangleIndexCount = quadIndexCount / 2;

		if (quadTriangleIndexCount == 0 ||
			quadTriangleIndexCount * 2 != quadIndexCount ||
			quadTriangleIndexCount % 3 != 0)
		{
			throw new ArgumentException(
				"The quad must split into two equal halves that are " +
				"whole triangles.",
				nameof(geometries));
		}

		uint hexagonIndexCount =
			checked((uint)geometries[2].Indices32.Length);

		if (scene == DemoScene.CameraTransforms)
		{
			// Triangle A and triangle B are two draws of the same
			// uploaded mesh (MeshIndex 0) with different model
			// matrices. Both quad ranges share one model matrix so
			// the split draw still covers a single transformed quad.
			return
			[
				new(
					MeshDrawKind.CompleteMesh,
					MeshIndex: 0,
					FirstElement: 0,
					triangleVertexCount,
					CameraTransforms.TriangleAModel),
				new(
					MeshDrawKind.CompleteMesh,
					MeshIndex: 0,
					FirstElement: 0,
					triangleVertexCount,
					CameraTransforms.TriangleBModel),
				new(
					MeshDrawKind.ElementRange,
					MeshIndex: 1,
					FirstElement: 0,
					quadTriangleIndexCount,
					CameraTransforms.QuadModel),
				new(
					MeshDrawKind.ElementRange,
					MeshIndex: 1,
					FirstElement: quadTriangleIndexCount,
					quadIndexCount - quadTriangleIndexCount,
					CameraTransforms.QuadModel),
				new(
					MeshDrawKind.CompleteMesh,
					MeshIndex: 2,
					FirstElement: 0,
					hexagonIndexCount,
					CameraTransforms.HexagonModel)
			];
		}

		if (scene is not (DemoScene.Gallery or DemoScene.DepthOcclusion))
		{
			throw new ArgumentOutOfRangeException(
				nameof(scene),
				scene,
				"Unsupported demo scene.");
		}

		// The clip-space scenes keep their authored positions, so
		// every instance uses the identity model transform.
		return
		[
			new(
				MeshDrawKind.CompleteMesh,
				MeshIndex: 0,
				FirstElement: 0,
				triangleVertexCount,
				Matrix4x4.Identity),
			new(
				MeshDrawKind.ElementRange,
				MeshIndex: 1,
				FirstElement: 0,
				quadTriangleIndexCount,
				Matrix4x4.Identity),
			new(
				MeshDrawKind.ElementRange,
				MeshIndex: 1,
				FirstElement: quadTriangleIndexCount,
				quadIndexCount - quadTriangleIndexCount,
				Matrix4x4.Identity),
			new(
				MeshDrawKind.CompleteMesh,
				MeshIndex: 2,
				FirstElement: 0,
				hexagonIndexCount,
				Matrix4x4.Identity)
		];
	}

	/// <summary>
	/// Records one render pass that clears the image and draws the
	/// planned mesh instances.
	/// </summary>
	/// <remarks>
	/// The projection is rebuilt from this generation's current
	/// extent every frame, so a recreated swapchain can never use a
	/// stale aspect ratio. The view comes from the application's
	/// camera controller on the same frame, so movement is visible
	/// without recreating this renderer. Each draw receives its own
	/// composed MVP; no matrix is allocated on the heap.
	/// </remarks>
	public void Record(
		CommandBuffer commandBuffer,
		int imageIndex)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		if (commandBuffer.Handle == 0)
		{
			throw new ArgumentException(
				"A valid command buffer is required.",
				nameof(commandBuffer));
		}

		if ((uint)imageIndex >= (uint)_framebuffers.Length)
		{
			throw new ArgumentOutOfRangeException(nameof(imageIndex));
		}

		float aspect = Extent.Width / (float)Extent.Height;

		Matrix4x4 view = CameraTransforms.CreateView(_scene, _camera);

		Matrix4x4 projection =
			CameraTransforms.CreateProjection(_scene, aspect);

		ClearValue* clearValues = stackalloc ClearValue[2];

		clearValues[0] = new ClearValue
		{
			Color = new ClearColorValue(
				float32_0: 0.035f,
				float32_1: 0.055f,
				float32_2: 0.095f,
				float32_3: 1.0f)
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
			Framebuffer = _framebuffers[imageIndex],

			RenderArea = new Rect2D
			{
				Offset = new Offset2D(0, 0),
				Extent = Extent
			},

			ClearValueCount = 2,
			PClearValues = clearValues
		};

		_api.CmdBeginRenderPass(
			commandBuffer,
			in begin,
			SubpassContents.Inline);

		_api.CmdBindPipeline(
			commandBuffer,
			PipelineBindPoint.Graphics,
			_pipeline);

		for (int index = 0; index < _drawPlan.Length; index++)
		{
			MeshDraw draw = _drawPlan[index];

			Matrix4x4 mvp = draw.Model * view * projection;

			MeshPushConstants constants = new(mvp);

			_api.CmdPushConstants(
				commandBuffer,
				_pipelineLayout,
				ShaderStageFlags.VertexBit,
				0,
				(uint)sizeof(MeshPushConstants),
				&constants);

			VulkanMeshBuffers mesh =
				_meshes.Buffers[draw.MeshIndex];

			if (draw.Kind == MeshDrawKind.CompleteMesh)
			{
				VulkanMeshDrawCommands.Record(
					_instance,
					commandBuffer,
					mesh);
			}

			else
			{
				VulkanMeshDrawCommands.RecordRange(
					_instance,
					commandBuffer,
					mesh,
					draw.FirstElement,
					draw.ElementCount);
			}
		}

		_api.CmdEndRenderPass(commandBuffer);
	}

	/// <summary>
	/// Releases the framebuffers, pipeline, and render pass.
	/// </summary>
	/// <remarks>
	/// Uploaded meshes are owned by the caller and outlive this
	/// renderer. The caller must establish GPU completion first.
	/// </remarks>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		DestroyResources(
			_api,
			_device.Handle,
			_framebuffers,
			_pipeline,
			_pipelineLayout,
			_renderPass);

		DestroyDepthAttachments(_depthAttachments);

		_pipeline = default;
		_pipelineLayout = default;
		_renderPass = default;

		_disposed = true;
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
			InitialLayout = ImageLayout.Undefined,
			FinalLayout = ImageLayout.PresentSrcKhr
		};

		attachments[1] = new AttachmentDescription
		{
			Format = depthFormat,
			Samples = SampleCountFlags.Count1Bit,
			LoadOp = AttachmentLoadOp.Clear,
			StoreOp = AttachmentStoreOp.DontCare,
			StencilLoadOp = AttachmentLoadOp.DontCare,
			StencilStoreOp = AttachmentStoreOp.DontCare,
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

		SubpassDependency* dependencies =
			stackalloc SubpassDependency[2];

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

			DstStageMask = PipelineStageFlags.BottomOfPipeBit,

			SrcAccessMask =
				AccessFlags.ColorAttachmentWriteBit |
				AccessFlags.DepthStencilAttachmentWriteBit,

			DstAccessMask = AccessFlags.None
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
				in description,
				null,
				out RenderPass renderPass),
			"Create mesh render pass");

		if (renderPass.Handle == 0)
		{
			throw new InvalidOperationException(
				"Vulkan returned a null render pass.");
		}

		return renderPass;
	}

	private static PipelineLayout CreatePipelineLayout(
		Vk api,
		Device device)
	{
		// One 4x4 matrix per draw: 64 bytes, well inside the 128
		// bytes every Vulkan implementation must support.
		PushConstantRange range = new()
		{
			StageFlags = ShaderStageFlags.VertexBit,
			Offset = 0,
			Size = (uint)sizeof(MeshPushConstants)
		};

		PipelineLayoutCreateInfo description = new()
		{
			SType = StructureType.PipelineLayoutCreateInfo,
			PushConstantRangeCount = 1,
			PPushConstantRanges = &range
		};

		EnsureSuccess(
			api.CreatePipelineLayout(
				device,
				in description,
				null,
				out PipelineLayout layout),
			"Create mesh pipeline layout");

		if (layout.Handle == 0)
		{
			throw new InvalidOperationException(
				"Vulkan returned a null pipeline layout.");
		}

		return layout;
	}

	private static Pipeline CreatePipeline(
		Vk api,
		Device device,
		RenderPass renderPass,
		PipelineLayout layout,
		Extent2D extent,
		string vertexShaderPath,
		string fragmentShaderPath,
		bool depthTestEnabled)
	{
		ShaderModule vertexShader = default;
		ShaderModule fragmentShader = default;

		try
		{
			vertexShader = CreateShaderModule(
				api,
				device,
				vertexShaderPath);

			fragmentShader = CreateShaderModule(
				api,
				device,
				fragmentShaderPath);

			byte* entryPoint = stackalloc byte[]
			{
				(byte)'m',
				(byte)'a',
				(byte)'i',
				(byte)'n',
				0
			};

			PipelineShaderStageCreateInfo* stages =
				stackalloc PipelineShaderStageCreateInfo[2];

			stages[0] = new PipelineShaderStageCreateInfo
			{
				SType = StructureType.PipelineShaderStageCreateInfo,
				Stage = ShaderStageFlags.VertexBit,
				Module = vertexShader,
				PName = entryPoint
			};

			stages[1] = new PipelineShaderStageCreateInfo
			{
				SType = StructureType.PipelineShaderStageCreateInfo,
				Stage = ShaderStageFlags.FragmentBit,
				Module = fragmentShader,
				PName = entryPoint
			};

			VertexInputBindingDescription binding = new()
			{
				Binding = 0,
				Stride = checked((uint)sizeof(MeshVertex)),
				InputRate = VertexInputRate.Vertex
			};

			VertexInputAttributeDescription* attributes =
				stackalloc VertexInputAttributeDescription[2];

			attributes[0] = new VertexInputAttributeDescription
			{
				Location = 0,
				Binding = 0,
				Format = Format.R32G32B32Sfloat,

				Offset = checked((uint)Marshal.OffsetOf<MeshVertex>(
					nameof(MeshVertex.Position)))
			};

			attributes[1] = new VertexInputAttributeDescription
			{
				Location = 1,
				Binding = 0,
				Format = Format.R32G32B32Sfloat,

				Offset = checked((uint)Marshal.OffsetOf<MeshVertex>(
					nameof(MeshVertex.Color)))
			};

			PipelineVertexInputStateCreateInfo vertexInput = new()
			{
				SType = StructureType.PipelineVertexInputStateCreateInfo,
				VertexBindingDescriptionCount = 1,
				PVertexBindingDescriptions = &binding,
				VertexAttributeDescriptionCount = 2,
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
				X = 0.0f,
				Y = 0.0f,
				Width = extent.Width,
				Height = extent.Height,
				MinDepth = 0.0f,
				MaxDepth = 1.0f
			};

			Rect2D scissor = new()
			{
				Offset = new Offset2D(0, 0),
				Extent = extent
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
				LineWidth = 1.0f
			};

			PipelineMultisampleStateCreateInfo multisampling = new()
			{
				SType = StructureType.PipelineMultisampleStateCreateInfo,
				RasterizationSamples = SampleCountFlags.Count1Bit,
				SampleShadingEnable = Vk.False
			};

			// Vulkan requires depth writes off when depth testing is
			// disabled. The depth attachment stays bound and cleared.
			PipelineDepthStencilStateCreateInfo depthState = new()
			{
				SType = StructureType.PipelineDepthStencilStateCreateInfo,
				DepthTestEnable = depthTestEnabled ? Vk.True : Vk.False,
				DepthWriteEnable = depthTestEnabled ? Vk.True : Vk.False,
				DepthCompareOp = CompareOp.Less,
				DepthBoundsTestEnable = Vk.False,
				StencilTestEnable = Vk.False
			};

			PipelineColorBlendAttachmentState colorAttachment = new()
			{
				BlendEnable = Vk.False,

				ColorWriteMask =
					ColorComponentFlags.RBit |
					ColorComponentFlags.GBit |
					ColorComponentFlags.BBit |
					ColorComponentFlags.ABit
			};

			PipelineColorBlendStateCreateInfo blending = new()
			{
				SType = StructureType.PipelineColorBlendStateCreateInfo,
				LogicOpEnable = Vk.False,
				AttachmentCount = 1,
				PAttachments = &colorAttachment
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
				PMultisampleState = &multisampling,
				PDepthStencilState = &depthState,
				PColorBlendState = &blending,
				Layout = layout,
				RenderPass = renderPass,
				Subpass = 0
			};

			Pipeline pipeline = default;

			Result result = api.CreateGraphicsPipelines(
				device,
				default,
				1,
				in description,
				null,
				out pipeline);

			if (result != Result.Success)
			{
				if (pipeline.Handle != 0)
				{
					api.DestroyPipeline(device, pipeline, null);
				}

				throw new InvalidOperationException(
					$"Create mesh graphics pipeline failed: {result}.");
			}

			if (pipeline.Handle == 0)
			{
				throw new InvalidOperationException(
					"Vulkan returned a null graphics pipeline.");
			}

			return pipeline;
		}

		finally
		{
			if (fragmentShader.Handle != 0)
			{
				api.DestroyShaderModule(
					device,
					fragmentShader,
					null);
			}

			if (vertexShader.Handle != 0)
			{
				api.DestroyShaderModule(
					device,
					vertexShader,
					null);
			}
		}
	}

	private static ShaderModule CreateShaderModule(
		Vk api,
		Device device,
		string path)
	{
		byte[] bytes = File.ReadAllBytes(path);

		if (bytes.Length < 20 ||
			bytes.Length % sizeof(uint) != 0 ||
			BitConverter.ToUInt32(bytes, 0) != 0x07230203u)
		{
			throw new InvalidDataException(
				$"Shader file is not valid SPIR-V: {path}");
		}

		fixed (byte* bytecode = bytes)
		{
			ShaderModuleCreateInfo description = new()
			{
				SType = StructureType.ShaderModuleCreateInfo,
				CodeSize = checked((nuint)bytes.Length),
				PCode = (uint*)bytecode
			};

			EnsureSuccess(
				api.CreateShaderModule(
					device,
					in description,
					null,
					out ShaderModule shader),
				"Create mesh shader module");

			if (shader.Handle == 0)
			{
				throw new InvalidOperationException(
					"Vulkan returned a null shader module.");
			}

			return shader;
		}
	}

	private static Framebuffer CreateFramebuffer(
		Vk api,
		Device device,
		RenderPass renderPass,
		ImageView colorView,
		ImageView depthView,
		Extent2D extent)
	{
		ImageView* attachments = stackalloc ImageView[2];

		attachments[0] = colorView;
		attachments[1] = depthView;

		FramebufferCreateInfo description = new()
		{
			SType = StructureType.FramebufferCreateInfo,
			RenderPass = renderPass,
			AttachmentCount = 2,
			PAttachments = attachments,
			Width = extent.Width,
			Height = extent.Height,
			Layers = 1
		};

		EnsureSuccess(
			api.CreateFramebuffer(
				device,
				in description,
				null,
				out Framebuffer framebuffer),
			"Create mesh framebuffer");

		if (framebuffer.Handle == 0)
		{
			throw new InvalidOperationException(
				"Vulkan returned a null framebuffer.");
		}

		return framebuffer;
	}

	private static void DestroyResources(
		Vk api,
		Device device,
		Framebuffer[] framebuffers,
		Pipeline pipeline,
		PipelineLayout layout,
		RenderPass renderPass)
	{
		for (int index = framebuffers.Length - 1; index >= 0; index--)
		{
			if (framebuffers[index].Handle != 0)
			{
				api.DestroyFramebuffer(
					device,
					framebuffers[index],
					null);
			}
		}

		if (pipeline.Handle != 0)
		{
			api.DestroyPipeline(device, pipeline, null);
		}

		if (layout.Handle != 0)
		{
			api.DestroyPipelineLayout(device, layout, null);
		}

		if (renderPass.Handle != 0)
		{
			api.DestroyRenderPass(device, renderPass, null);
		}
	}

	private static void DestroyDepthAttachments(
		VulkanDepthAttachment?[] attachments)
	{
		for (int index = attachments.Length - 1;
			index >= 0;
			index--)
		{
			attachments[index]?.Dispose();
		}
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
}
