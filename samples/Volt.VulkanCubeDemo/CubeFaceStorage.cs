
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Memory;

namespace Volt.VulkanCubeDemo;

/// <summary>
/// Owns six packed cube faces and their Vulkan descriptor binding.
/// </summary>
/// <remarks>
/// Each uint represents one quad:
/// bits 0-1: face axis (X, Y, Z)
/// bit 2: positive or negative axis direction
/// bits 3-5: face color index
///
/// The face descriptors are uploaded to device-local memory
/// through a temporary host-visible staging buffer.
///
/// This packing format belongs to the sample, not VOLT's buffer API.
/// </remarks>
internal sealed unsafe class CubeFaceStorage : IDisposable
{
    private readonly VulkanLogicalDevice _device;
    private readonly Vk _api;
    private readonly VulkanBuffer _buffer;

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

        VulkanBuffer? buffer = null;
        DescriptorSetLayout layout = default;
        DescriptorPool pool = default;
        DescriptorSet set;

        Device deviceHandle = device.Handle;

        try
        {
            buffer = VulkanBufferTransfer.UploadDeviceLocal<uint>(
                instance,
                device,
                physicalDevice,
                faces,
                BufferUsageFlags.StorageBufferBit);

            DescriptorSetLayoutBinding binding = new()
            {
                Binding = 0,
                DescriptorType = DescriptorType.StorageBuffer,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.VertexBit
            };

            DescriptorSetLayoutCreateInfo layoutDescription = new()
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = 1,
                PBindings = &binding
            };

            EnsureSuccess(
                _api.CreateDescriptorSetLayout(
                    deviceHandle,
                    in layoutDescription,
                    null,
                    out layout),
                "Create cube face descriptor layout");

            if (layout.Handle == 0)
            {
                throw new InvalidOperationException(
                    "Vulkan returned a null descriptor set layout.");
            }

            DescriptorPoolSize poolSize = new()
            {
                Type = DescriptorType.StorageBuffer,
                DescriptorCount = 1
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
                "Create cube face descriptor pool");

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
                "Allocate cube face descriptor set");

            if (set.Handle == 0)
            {
                throw new InvalidOperationException(
                    "Vulkan returned a null descriptor set.");
            }

            DescriptorBufferInfo bufferInformation = new()
            {
                Buffer = buffer.Handle,
                Offset = 0,
                Range = buffer.Size
            };

            WriteDescriptorSet write = new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = set,
                DstBinding = 0,
                DstArrayElement = 0,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.StorageBuffer,
                PBufferInfo = &bufferInformation
            };

            _api.UpdateDescriptorSets(
                deviceHandle,
                1,
                &write,
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

            buffer?.Dispose();
            throw;
        }

        _buffer = buffer ?? throw new InvalidOperationException(
            "Cube face buffer initialization failed.");

        _layout = layout;
        _pool = pool;
        _set = set;
    }

    /// <summary>
    /// Binds the face storage buffer to set zero of the pipeline.
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

        _buffer.Dispose();

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
