
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Memory;

namespace Volt.VulkanCubeDemo;

/// <summary>
/// Owns six packed cube faces, six packed colors, and their
/// Vulkan descriptor bindings.
/// </summary>
/// <remarks>
/// Each face descriptor represents one quad:
/// bits 0-1: face axis (X, Y, Z)
/// bit 2: positive or negative axis direction
/// bits 3-5: face color index
///
/// Colors are stored as 32-bit RGB values.
///
/// Both arrays are uploaded into separate device-local buffers
/// using one batched Vulkan transfer submission.
///
/// These packing formats belong to the sample, not VOLT's API.
/// </remarks>
internal sealed unsafe class CubeFaceStorage : IDisposable
{
	private readonly VulkanLogicalDevice _device;
	private readonly Vk _api;
	private readonly VulkanBuffer _faceBuffer;
	private readonly VulkanBuffer _colorBuffer;

	private DescriptorSetLayout _layout;
	private DescriptorPool _pool;
	private DescriptorSet _set;

	private bool _disposed;

	public DescriptorSetLayout Layout
	{
		get
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			return _layout;
		}
	}

	public CubeFaceStorage(
		VulkanInstance instance,
		VulkanLogicalDevice device,
		PhysicalDevice physicalDevice)
	{
		ArgumentNullException.ThrowIfNull(instance);
		ArgumentNullException.ThrowIfNull(device);

		_device = device;
		_api = instance.Api;

		uint[] faces =
		[
			PackFace(2, true, 0),
			PackFace(2, false, 1),
			PackFace(0, true, 2),
			PackFace(0, false, 3),
			PackFace(1, true, 4),
			PackFace(1, false, 5)
		];

		uint[] colors =
		[
			0x38BDFFu,
			0xF55978u,
			0x61F29Eu,
			0xFDC24Du,
			0xB378FAu,
			0xFA873Du
		];

		VulkanBuffer[]? buffers = null;
		DescriptorSetLayout layout = default;
		DescriptorPool pool = default;
		DescriptorSet set;

		Device deviceHandle = device.Handle;

		VulkanBufferUploadBatch batch = new();

		int faceBufferIndex = batch.Add<uint>(
			faces,
			BufferUsageFlags.StorageBufferBit);

		int colorBufferIndex = batch.Add<uint>(
			colors,
			BufferUsageFlags.StorageBufferBit);

		try
		{
			buffers = batch.Submit(
				instance,
				device,
				physicalDevice);

			DescriptorSetLayoutBinding* bindings =
				stackalloc DescriptorSetLayoutBinding[2];

			bindings[0] = new DescriptorSetLayoutBinding
			{
				Binding = 0,
				DescriptorType = DescriptorType.StorageBuffer,
				DescriptorCount = 1,
				StageFlags = ShaderStageFlags.VertexBit
			};

			bindings[1] = new DescriptorSetLayoutBinding
			{
				Binding = 1,
				DescriptorType = DescriptorType.StorageBuffer,
				DescriptorCount = 1,
				StageFlags = ShaderStageFlags.VertexBit
			};

			DescriptorSetLayoutCreateInfo layoutDescription = new()
			{
				SType = StructureType.DescriptorSetLayoutCreateInfo,
				BindingCount = 2,
				PBindings = bindings
			};

			EnsureSuccess(
				_api.CreateDescriptorSetLayout(
					deviceHandle,
					in layoutDescription,
					null,
					out layout),
				"Create cube descriptor layout");

			if (layout.Handle == 0)
			{
				throw new InvalidOperationException(
					"Vulkan returned a null descriptor set layout.");
			}

			DescriptorPoolSize poolSize = new()
			{
				Type = DescriptorType.StorageBuffer,
				DescriptorCount = 2
			};

			DescriptorPoolCreateInfo poolDescription = new()
			{
				SType = StructureType.DescriptorPoolCreateInfo,
				MaxSets = 1,
				PoolSizeCount = 1,
				PPoolSizes = &poolSize
			};

			EnsureSuccess(
				_api.CreateDescriptorPool(
					deviceHandle,
					in poolDescription,
					null,
					out pool),
				"Create cube descriptor pool");

			if (pool.Handle == 0)
			{
				throw new InvalidOperationException(
					"Vulkan returned a null descriptor pool.");
			}

			DescriptorSetAllocateInfo allocation = new()
			{
				SType = StructureType.DescriptorSetAllocateInfo,
				DescriptorPool = pool,
				DescriptorSetCount = 1,
				PSetLayouts = &layout
			};

			EnsureSuccess(
				_api.AllocateDescriptorSets(
					deviceHandle,
					in allocation,
					out set),
				"Allocate cube descriptor set");

			if (set.Handle == 0)
			{
				throw new InvalidOperationException(
					"Vulkan returned a null descriptor set.");
			}

			DescriptorBufferInfo* bufferInformation =
				stackalloc DescriptorBufferInfo[2];

			bufferInformation[0] = new DescriptorBufferInfo
			{
				Buffer = buffers[faceBufferIndex].Handle,
				Offset = 0,
				Range = buffers[faceBufferIndex].Size
			};

			bufferInformation[1] = new DescriptorBufferInfo
			{
				Buffer = buffers[colorBufferIndex].Handle,
				Offset = 0,
				Range = buffers[colorBufferIndex].Size
			};

			WriteDescriptorSet* writes =
				stackalloc WriteDescriptorSet[2];

			writes[0] = new WriteDescriptorSet
			{
				SType = StructureType.WriteDescriptorSet,
				DstSet = set,
				DstBinding = 0,
				DstArrayElement = 0,
				DescriptorCount = 1,
				DescriptorType = DescriptorType.StorageBuffer,
				PBufferInfo = &bufferInformation[0]
			};

			writes[1] = new WriteDescriptorSet
			{
				SType = StructureType.WriteDescriptorSet,
				DstSet = set,
				DstBinding = 1,
				DstArrayElement = 0,
				DescriptorCount = 1,
				DescriptorType = DescriptorType.StorageBuffer,
				PBufferInfo = &bufferInformation[1]
			};

			_api.UpdateDescriptorSets(
				deviceHandle,
				2,
				writes,
				0,
				null);
		}

		catch
		{
			if (pool.Handle != 0)
			{
				_api.DestroyDescriptorPool(
					deviceHandle,
					pool,
					null);
			}

			if (layout.Handle != 0)
			{
				_api.DestroyDescriptorSetLayout(
					deviceHandle,
					layout,
					null);
			}

			if (buffers is not null)
			{
				foreach (VulkanBuffer buffer in buffers)
				{
					buffer.Dispose();
				}
			}

			throw;
		}

		_faceBuffer = buffers[faceBufferIndex];
		_colorBuffer = buffers[colorBufferIndex];

		_layout = layout;
		_pool = pool;
		_set = set;
	}

	/// <summary>
	/// Binds the cube's face and color buffers through set zero.
	/// </summary>
	public void Bind(
		CommandBuffer commandBuffer,
		PipelineLayout pipelineLayout)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		if (commandBuffer.Handle == 0 ||
			pipelineLayout.Handle == 0)
		{
			throw new ArgumentException(
				"Valid Vulkan command and pipeline handles are required.");
		}

		DescriptorSet set = _set;

		_api.CmdBindDescriptorSets(
			commandBuffer,
			PipelineBindPoint.Graphics,
			pipelineLayout,
			0,
			1,
			&set,
			0,
			null);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		Device deviceHandle = _device.Handle;

		_api.DestroyDescriptorPool(
			deviceHandle,
			_pool,
			null);

		_api.DestroyDescriptorSetLayout(
			deviceHandle,
			_layout,
			null);

		_colorBuffer.Dispose();
		_faceBuffer.Dispose();

		_set = default;
		_pool = default;
		_layout = default;
		_disposed = true;
	}

	private static uint PackFace(
		uint axis,
		bool positive,
		uint colorIndex)
	{
		if (axis > 2)
		{
			throw new ArgumentOutOfRangeException(nameof(axis));
		}

		if (colorIndex > 5)
		{
			throw new ArgumentOutOfRangeException(nameof(colorIndex));
		}

		return axis
			| ((positive ? 1u : 0u) << 2)
			| (colorIndex << 3);
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
