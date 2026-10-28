using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;

namespace Volt.Graphics.Vulkan.Rendering;

/// <summary>
/// The graphics pipeline that draws scene meshes into an offscreen
/// render target with depth testing.
/// </summary>
/// <remarks>
/// <para>
/// One pipeline serves every mesh instance. Placement and projection
/// arrive as two 4x4 push constants, so no descriptor set, uniform
/// buffer, or per-object resource is needed to draw an arbitrary
/// number of instances. The viewport and scissor are dynamic, which
/// means one pipeline survives a target resize.
/// </para>
/// <para>
/// The pipeline is created for one render pass. It must only be used
/// with that pass or with a pass whose attachments match it, and it
/// must be disposed before the render pass and logical device are.
/// Meshes are owned by the caller, must outlive any recorded frame, and
/// must use <see cref="VulkanSceneVertex"/> as their vertex type.
/// </para>
/// <para>
/// This type is not thread-safe and must be created, recorded, and
/// disposed on the thread that records frames.
/// </para>
/// </remarks>
public sealed unsafe class VulkanSceneMeshPipeline : IDisposable
{
	/// <summary>
	/// Gets the push-constant size in bytes: one matrix for the
	/// composed model-view-projection and one for the model transform.
	/// </summary>
	/// <remarks>
	/// 128 bytes is exactly the guaranteed minimum every Vulkan
	/// implementation must support, so this pipeline never depends on
	/// a device-specific limit.
	/// </remarks>
	public const uint PushConstantBytes = 128;

	private const ShaderStageFlags PushConstantStages =
		ShaderStageFlags.VertexBit;

	private readonly VulkanInstance _instance;
	private readonly VulkanLogicalDevice _device;
	private readonly Vk _api;
	private readonly PipelineLayout _pipelineLayout;

	private Pipeline _pipeline;
	private bool _disposed;

	/// <summary>
	/// Gets the pipeline layout, so other renderers can bind
	/// compatible resources.
	/// </summary>
	public PipelineLayout PipelineLayout
	{
		get
		{
			ThrowIfDisposed();
			return _pipelineLayout;
		}
	}

	/// <summary>
	/// Creates the pipeline for one offscreen render pass.
	/// </summary>
	/// <param name="instance">The Vulkan instance.</param>
	/// <param name="device">The logical device.</param>
	/// <param name="renderPass">
	/// The render pass the pipeline is created for. It must have one
	/// colour attachment and one depth attachment.
	/// </param>
	/// <param name="vertexShaderPath">
	/// Path to the compiled vertex shader.
	/// </param>
	/// <param name="fragmentShaderPath">
	/// Path to the compiled fragment shader.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="instance"/> or <paramref name="device"/> is null.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// The render pass is not valid, or a shader path is empty or
	/// contains no valid SPIR-V.
	/// </exception>
	public VulkanSceneMeshPipeline(
		VulkanInstance instance,
		VulkanLogicalDevice device,
		RenderPass renderPass,
		string vertexShaderPath,
		string fragmentShaderPath)
	{
		ArgumentNullException.ThrowIfNull(instance);
		ArgumentNullException.ThrowIfNull(device);
		ArgumentException.ThrowIfNullOrWhiteSpace(vertexShaderPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(fragmentShaderPath);

		if (renderPass.Handle == 0)
		{
			throw new ArgumentException(
				"A valid render pass is required.",
				nameof(renderPass));
		}

		_instance = instance;
		_device = device;
		_api = instance.Api;

		_pipelineLayout = CreatePipelineLayout();
		_pipeline = CreatePipeline(renderPass, vertexShaderPath, fragmentShaderPath);
	}

	/// <summary>
	/// Records one mesh draw with its own transforms.
	/// </summary>
	/// <param name="commandBuffer">
	/// A command buffer inside a compatible render pass.
	/// </param>
	/// <param name="extent">
	/// The render target extent, used for the dynamic viewport and
	/// scissor.
	/// </param>
	/// <param name="mesh">The mesh to draw.</param>
	/// <param name="mvp">The composed model-view-projection matrix.</param>
	/// <param name="model">The world-space model matrix.</param>
	/// <exception cref="ArgumentException">
	/// The command buffer is not valid.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// <paramref name="extent"/> is zero-sized.
	/// </exception>
	public void Record(
		CommandBuffer commandBuffer,
		Extent2D extent,
		VulkanMeshBuffers mesh,
		in Matrix4x4 mvp,
		in Matrix4x4 model)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(mesh);

		if (commandBuffer.Handle == 0)
		{
			throw new ArgumentException(
				"A valid command buffer is required.",
				nameof(commandBuffer));
		}

		if (extent.Width == 0 || extent.Height == 0)
		{
			throw new InvalidOperationException(
				"A scene draw requires a positive target extent.");
		}

		SceneMeshPushConstants constants = new(mvp, model);

		_api.CmdBindPipeline(
			commandBuffer,
			PipelineBindPoint.Graphics,
			_pipeline);

		Viewport viewport = new()
		{
			X = 0f,
			Y = 0f,
			Width = extent.Width,
			Height = extent.Height,
			MinDepth = 0f,
			MaxDepth = 1f
		};

		Rect2D scissor = new()
		{
			Offset = new Offset2D(0, 0),
			Extent = extent
		};

		_api.CmdSetViewport(commandBuffer, 0, 1, &viewport);
		_api.CmdSetScissor(commandBuffer, 0, 1, &scissor);

		_api.CmdPushConstants(
			commandBuffer,
			_pipelineLayout,
			PushConstantStages,
			0,
			PushConstantBytes,
			&constants);

		VulkanMeshDrawCommands.Record(_instance, commandBuffer, mesh);
	}

	/// <summary>
	/// Destroys the pipeline and its layout.
	/// </summary>
	/// <remarks>
	/// Disposal is idempotent. The caller must establish GPU completion
	/// for any recorded frame first.
	/// </remarks>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;

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
		}
	}

	/// <summary>
	/// One draw's push constants, in the order the shader declares
	/// them.
	/// </summary>
	[StructLayout(LayoutKind.Sequential)]
	private readonly struct SceneMeshPushConstants
	{
		public readonly Matrix4x4 Mvp;
		public readonly Matrix4x4 Model;

		public SceneMeshPushConstants(Matrix4x4 mvp, Matrix4x4 model)
		{
			Mvp = mvp;
			Model = model;
		}
	}

	private PipelineLayout CreatePipelineLayout()
	{
		PushConstantRange range = new()
		{
			StageFlags = PushConstantStages,
			Offset = 0,
			Size = PushConstantBytes
		};

		PipelineLayoutCreateInfo description = new()
		{
			SType = StructureType.PipelineLayoutCreateInfo,
			PushConstantRangeCount = 1,
			PPushConstantRanges = &range
		};

		EnsureSuccess(
			_api.CreatePipelineLayout(
				_device.Handle,
				&description,
				null,
				out PipelineLayout layout),
			"Create scene pipeline layout");

		return layout;
	}

	private Pipeline CreatePipeline(
		RenderPass renderPass,
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
				Stride = checked((uint)Marshal.SizeOf<VulkanSceneVertex>()),
				InputRate = VertexInputRate.Vertex
			};

			VertexInputAttributeDescription* attributes =
				stackalloc VertexInputAttributeDescription[2];

			attributes[0] = new VertexInputAttributeDescription
			{
				Location = 0,
				Binding = 0,
				Format = Format.R32G32B32Sfloat,

				Offset = checked((uint)Marshal.OffsetOf<VulkanSceneVertex>(
					nameof(VulkanSceneVertex.Position)))
			};

			attributes[1] = new VertexInputAttributeDescription
			{
				Location = 1,
				Binding = 0,
				Format = Format.R32G32B32Sfloat,

				Offset = checked((uint)Marshal.OffsetOf<VulkanSceneVertex>(
					nameof(VulkanSceneVertex.Color)))
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

			PipelineViewportStateCreateInfo viewportState = new()
			{
				SType = StructureType.PipelineViewportStateCreateInfo,
				ViewportCount = 1,
				ScissorCount = 1
			};

			PipelineRasterizationStateCreateInfo rasterization = new()
			{
				SType = StructureType.PipelineRasterizationStateCreateInfo,
				DepthClampEnable = Vk.False,
				RasterizerDiscardEnable = Vk.False,
				PolygonMode = PolygonMode.Fill,

				// Both windings are rasterized: procedurally generated
				// primitives are not authored for a single winding, and
				// the derivative-based normal makes either side lit.
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

			PipelineDepthStencilStateCreateInfo depth = new()
			{
				SType = StructureType.PipelineDepthStencilStateCreateInfo,
				DepthTestEnable = Vk.True,
				DepthWriteEnable = Vk.True,
				DepthCompareOp = CompareOp.Less,
				DepthBoundsTestEnable = Vk.False,
				StencilTestEnable = Vk.False
			};

			PipelineColorBlendAttachmentState attachment = new()
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
				PAttachments = &attachment
			};

			DynamicState* dynamicStates = stackalloc DynamicState[2];

			dynamicStates[0] = DynamicState.Viewport;
			dynamicStates[1] = DynamicState.Scissor;

			PipelineDynamicStateCreateInfo dynamic = new()
			{
				SType = StructureType.PipelineDynamicStateCreateInfo,
				DynamicStateCount = 2,
				PDynamicStates = dynamicStates
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
				PDepthStencilState = &depth,
				PColorBlendState = &blending,
				PDynamicState = &dynamic,
				Layout = _pipelineLayout,
				RenderPass = renderPass,
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

			EnsureSuccess(result, "Create scene graphics pipeline");

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
				_api.DestroyShaderModule(_device.Handle, vertex, null);
			}
		}
	}

	private ShaderModule CreateShaderModule(string path)
	{
		byte[] spirv = File.ReadAllBytes(path);

		if (spirv.Length < 20 ||
			spirv.Length % sizeof(uint) != 0 ||
			BitConverter.ToUInt32(spirv, 0) != 0x07230203u)
		{
			throw new ArgumentException(
				$"Shader is not valid SPIR-V: '{path}'.",
				nameof(path));
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
