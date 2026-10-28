using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Memory;

namespace Volt.Graphics.Vulkan.Rendering;

/// <summary>
/// The graphics pipeline that draws scene meshes into an offscreen
/// render target with depth testing.
/// </summary>
/// <remarks>
/// <para>
/// One pipeline serves every mesh instance. Placement and projection
/// arrive as two 4x4 push constants, so no per-object resource is
/// needed to draw an arbitrary number of instances. The viewport and
/// scissor are dynamic, which means one pipeline survives a target
/// resize.
/// </para>
/// <para>
/// The authored directional light travels separately through one
/// 32-byte uniform buffer per frame slot, bound as descriptor set 0.
/// Keeping the light out of push constants leaves the 128-byte budget
/// available for the two matrices on every conformant device.
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

	private const int LightSlotCount = 2;
	private const uint LightUniformBytes = 32;

	private readonly VulkanInstance _instance;
	private readonly VulkanLogicalDevice _device;
	private readonly Vk _api;
	private readonly VulkanBuffer?[] _lightBuffers =
		new VulkanBuffer?[LightSlotCount];
	private readonly DescriptorSet[] _lightSets =
		new DescriptorSet[LightSlotCount];
	private readonly VulkanSceneLight[] _lightCache =
		new VulkanSceneLight[LightSlotCount];
	private readonly bool[] _lightWritten = new bool[LightSlotCount];

	private DescriptorSetLayout _lightLayout;
	private DescriptorPool _lightPool;
	private PipelineLayout _pipelineLayout;

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
	/// <param name="physicalDevice">
	/// The physical device behind <paramref name="device"/>. Used to
	/// allocate the per-slot light uniform buffers.
	/// </param>
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
		PhysicalDevice physicalDevice,
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

		if (physicalDevice.Handle == 0)
		{
			throw new ArgumentException(
				"A valid physical device is required.",
				nameof(physicalDevice));
		}

		_instance = instance;
		_device = device;
		_api = instance.Api;

		try
		{
			_lightLayout = CreateLightDescriptorSetLayout();
			_lightPool = CreateLightDescriptorPool();
			CreateLightSets(physicalDevice);
			_pipelineLayout = CreatePipelineLayout();
			_pipeline = CreatePipeline(
				renderPass,
				vertexShaderPath,
				fragmentShaderPath);
		}

		catch
		{
			Dispose();
			throw;
		}
	}

	/// <summary>
	/// Uploads a frame slot's light and binds descriptor set 0.
	/// </summary>
	/// <param name="commandBuffer">A recording command buffer.</param>
	/// <param name="slot">The frame slot being recorded.</param>
	/// <param name="light">The authored light for the viewport.</param>
	/// <remarks>
	/// Call this once before the first <see cref="Record"/> call in a
	/// viewport pass. Each slot owns its buffer and descriptor set, so
	/// writing the next frame cannot race an earlier frame still in
	/// flight.
	/// </remarks>
	public void BeginViewport(
		CommandBuffer commandBuffer,
		int slot,
		in VulkanSceneLight light)
	{
		ThrowIfDisposed();

		if (commandBuffer.Handle == 0)
		{
			throw new ArgumentException(
				"A valid command buffer is required.",
				nameof(commandBuffer));
		}

		int index = NormalizeSlot(slot);

		if (!_lightWritten[index] ||
			!_lightCache[index].Equals(light))
		{
			VulkanBuffer buffer = _lightBuffers[index] ??
				throw new InvalidOperationException(
					"The scene light buffer was not created.");

			SceneLightUniform uniform = new(light);

			ReadOnlySpan<SceneLightUniform> values =
				MemoryMarshal.CreateReadOnlySpan(ref uniform, 1);

			buffer.Write(values);

			_lightCache[index] = light;
			_lightWritten[index] = true;
		}

		_api.CmdBindPipeline(
			commandBuffer,
			PipelineBindPoint.Graphics,
			_pipeline);

		DescriptorSet set = _lightSets[index];

		_api.CmdBindDescriptorSets(
			commandBuffer,
			PipelineBindPoint.Graphics,
			_pipelineLayout,
			firstSet: 0,
			descriptorSetCount: 1,
			&set,
			dynamicOffsetCount: 0,
			pDynamicOffsets: null);
	}

	/// <summary>
	/// Records one mesh draw with its own transforms.
	/// </summary>
	/// <remarks>
	/// Call <see cref="BeginViewport"/> for the frame slot before the
	/// first draw; it binds the light descriptor set this pipeline's
	/// layout expects.
	/// </remarks>
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
	/// Destroys the pipeline, its layout, and the light resources.
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

			_pipelineLayout = default;
		}

		ReleaseLightResources();
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

	/// <summary>
	/// One frame slot's light, in the order the shader declares it.
	/// </summary>
	[StructLayout(LayoutKind.Sequential)]
	private readonly struct SceneLightUniform
	{
		public readonly Vector4 Direction;
		public readonly Vector4 Diffuse;

		public SceneLightUniform(in VulkanSceneLight light)
		{
			Direction = new Vector4(light.Direction, 0f);
			Diffuse = new Vector4(light.DiffuseColor, 0f);
		}
	}

	private DescriptorSetLayout CreateLightDescriptorSetLayout()
	{
		DescriptorSetLayoutBinding binding = new()
		{
			Binding = 0,
			DescriptorType = DescriptorType.UniformBuffer,
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
			"Create scene light descriptor set layout");

		return layout;
	}

	private DescriptorPool CreateLightDescriptorPool()
	{
		DescriptorPoolSize size = new()
		{
			Type = DescriptorType.UniformBuffer,
			DescriptorCount = LightSlotCount
		};

		DescriptorPoolCreateInfo description = new()
		{
			SType = StructureType.DescriptorPoolCreateInfo,
			MaxSets = LightSlotCount,
			PoolSizeCount = 1,
			PPoolSizes = &size
		};

		EnsureSuccess(
			_api.CreateDescriptorPool(
				_device.Handle,
				&description,
				null,
				out DescriptorPool pool),
			"Create scene light descriptor pool");

		return pool;
	}

	private void CreateLightSets(PhysicalDevice physicalDevice)
	{
		for (int index = 0; index < LightSlotCount; index++)
		{
			VulkanBuffer buffer = new(
				_instance,
				_device,
				physicalDevice,
				LightUniformBytes,
				BufferUsageFlags.UniformBufferBit,
				MemoryPropertyFlags.HostVisibleBit);

			_lightBuffers[index] = buffer;

			DescriptorSetLayout layout = _lightLayout;

			DescriptorSetAllocateInfo allocate = new()
			{
				SType = StructureType.DescriptorSetAllocateInfo,
				DescriptorPool = _lightPool,
				DescriptorSetCount = 1,
				PSetLayouts = &layout
			};

			EnsureSuccess(
				_api.AllocateDescriptorSets(
					_device.Handle,
					&allocate,
					out DescriptorSet set),
				$"Allocate scene light descriptor set {index}");

			if (set.Handle == 0)
			{
				throw new InvalidOperationException(
					"Vulkan returned a null scene light descriptor set.");
			}

			_lightSets[index] = set;

			DescriptorBufferInfo bufferInfo = new()
			{
				Buffer = buffer.Handle,
				Offset = 0,
				Range = LightUniformBytes
			};

			WriteDescriptorSet write = new()
			{
				SType = StructureType.WriteDescriptorSet,
				DstSet = set,
				DstBinding = 0,
				DstArrayElement = 0,
				DescriptorCount = 1,
				DescriptorType = DescriptorType.UniformBuffer,
				PBufferInfo = &bufferInfo
			};

			_api.UpdateDescriptorSets(
				_device.Handle,
				1,
				&write,
				0,
				null);
		}
	}

	private void ReleaseLightResources()
	{
		// Descriptor sets reference the uniform buffers, so release
		// the sets through their pool before destroying the buffers.
		if (_lightPool.Handle != 0)
		{
			_api.DestroyDescriptorPool(
				_device.Handle,
				_lightPool,
				null);

			_lightPool = default;
		}

		for (int index = 0; index < LightSlotCount; index++)
		{
			_lightBuffers[index]?.Dispose();
			_lightBuffers[index] = null;
			_lightSets[index] = default;
			_lightCache[index] = default;
			_lightWritten[index] = false;
		}

		if (_lightLayout.Handle != 0)
		{
			_api.DestroyDescriptorSetLayout(
				_device.Handle,
				_lightLayout,
				null);

			_lightLayout = default;
		}
	}

	private static int NormalizeSlot(int slot)
	{
		int normalized = slot % LightSlotCount;

		return normalized < 0 ? normalized + LightSlotCount : normalized;
	}

	private PipelineLayout CreatePipelineLayout()
	{
		PushConstantRange range = new()
		{
			StageFlags = PushConstantStages,
			Offset = 0,
			Size = PushConstantBytes
		};

		DescriptorSetLayout setLayout = _lightLayout;

		PipelineLayoutCreateInfo description = new()
		{
			SType = StructureType.PipelineLayoutCreateInfo,
			SetLayoutCount = 1,
			PSetLayouts = &setLayout,
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
