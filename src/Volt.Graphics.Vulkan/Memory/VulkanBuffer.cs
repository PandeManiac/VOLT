
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using VkBuffer = Silk.NET.Vulkan.Buffer;

namespace Volt.Graphics.Vulkan.Memory;

/// <summary>
/// Owns a Vulkan buffer and a dedicated memory allocation.
/// </summary>
/// <remarks>
/// The caller chooses both buffer usage and required memory properties.
///
/// Host-visible allocations support direct writes and reads.
/// GPU-only allocations require an external transfer mechanism.
///
/// This class does not track individual GPU accesses. Callers must
/// synchronize uploads, reads, and destruction with GPU execution.
///
/// If the logical device has unresolved transfer execution,
/// native destruction is deferred until it is safe.
/// </remarks>
public sealed unsafe class VulkanBuffer : IDisposable
{
    private readonly VulkanInstance _instance;
    private readonly VulkanLogicalDevice _device;
    private readonly Vk _api;

    private VkBuffer _buffer;
    private DeviceMemory _memory;

    private bool _disposed;

    public ulong Size { get; }

    public BufferUsageFlags Usage { get; }

    public MemoryPropertyFlags MemoryProperties { get; }

    public VkBuffer Handle
    {
        get
        {
            ThrowIfDisposed();
            return _buffer;
        }
    }

    public VulkanBuffer(
        VulkanInstance instance,
        VulkanLogicalDevice device,
        PhysicalDevice physicalDevice,
        ulong size,
        BufferUsageFlags usage,
        MemoryPropertyFlags requiredMemoryProperties,
        MemoryPropertyFlags preferredMemoryProperties =
            MemoryPropertyFlags.None)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(device);

        if (physicalDevice.Handle == 0)
        {
            throw new ArgumentException(
                "A valid physical device is required.",
                nameof(physicalDevice));
        }

        if (size == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(size),
                "Vulkan buffers must have a positive size.");
        }

        if (usage == BufferUsageFlags.None)
        {
            throw new ArgumentOutOfRangeException(
                nameof(usage),
                "At least one buffer usage flag is required.");
        }

        _instance = instance;
        _device = device;
        _api = instance.Api;

        Device deviceHandle = device.Handle;

        VkBuffer buffer = default;
        DeviceMemory memory = default;
        MemoryPropertyFlags selectedProperties = default;

        try
        {
            BufferCreateInfo bufferDescription = new()
            {
                SType = StructureType.BufferCreateInfo,
                Size = size,
                Usage = usage,
                SharingMode = SharingMode.Exclusive
            };

            EnsureSuccess(
                _api.CreateBuffer(
                    deviceHandle,
                    in bufferDescription,
                    null,
                    out buffer),
                "Create Vulkan buffer");

            if (buffer.Handle == 0)
            {
                throw new InvalidOperationException(
                    "Vulkan returned a null buffer handle.");
            }

            MemoryRequirements requirements =
                _api.GetBufferMemoryRequirements(
                    deviceHandle,
                    buffer);

            PhysicalDeviceMemoryProperties nativeProperties =
                _api.GetPhysicalDeviceMemoryProperties(
                    physicalDevice);

            int memoryTypeCount =
                checked((int)nativeProperties.MemoryTypeCount);

            Span<MemoryPropertyFlags> availableProperties =
                stackalloc MemoryPropertyFlags[memoryTypeCount];

            for (int index = 0; index < memoryTypeCount; index++)
            {
                availableProperties[index] =
                    nativeProperties.MemoryTypes[index].PropertyFlags;
            }

            int memoryIndex = SelectMemoryType(
                requirements.MemoryTypeBits,
                availableProperties,
                requiredMemoryProperties,
                preferredMemoryProperties);

            if (memoryIndex < 0)
            {
                throw new NotSupportedException(
                    "No compatible Vulkan memory type satisfies " +
                    $"the requested properties: {requiredMemoryProperties}.");
            }

            selectedProperties = availableProperties[memoryIndex];

            MemoryAllocateInfo allocation = new()
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = requirements.Size,
                MemoryTypeIndex = checked((uint)memoryIndex)
            };

            EnsureSuccess(
                _api.AllocateMemory(
                    deviceHandle,
                    in allocation,
                    null,
                    out memory),
                "Allocate Vulkan buffer memory");

            if (memory.Handle == 0)
            {
                throw new InvalidOperationException(
                    "Vulkan returned a null memory allocation.");
            }

            EnsureSuccess(
                _api.BindBufferMemory(
                    deviceHandle,
                    buffer,
                    memory,
                    0),
                "Bind Vulkan buffer memory");
        }

        catch
        {
            if (buffer.Handle != 0)
            {
                _api.DestroyBuffer(deviceHandle, buffer, null);
            }

            if (memory.Handle != 0)
            {
                _api.FreeMemory(deviceHandle, memory, null);
            }

            throw;
        }

        _buffer = buffer;
        _memory = memory;

        Size = size;
        Usage = usage;
        MemoryProperties = selectedProperties;
    }

    /// <summary>
    /// Copies unmanaged values into host-visible Vulkan memory.
    /// </summary>
    public void Write<T>(
        ReadOnlySpan<T> values,
        ulong destinationOffset = 0)
        where T : unmanaged
    {
        Write(
            MemoryMarshal.AsBytes(values),
            destinationOffset);
    }

    /// <summary>
    /// Copies raw bytes into the allocation.
    /// </summary>
    /// <remarks>
    /// This method maps the allocation temporarily.
    /// For non-coherent memory, the mapped allocation is flushed.
    ///
    /// No implicit GPU synchronization is performed.
    /// Do not call while the GPU may access overlapping data.
    /// </remarks>
    public void Write(
        ReadOnlySpan<byte> data,
        ulong destinationOffset = 0)
    {
        ThrowIfDisposed();

        if ((MemoryProperties &
            MemoryPropertyFlags.HostVisibleBit) == 0)
        {
            throw new InvalidOperationException(
                "This buffer is not host-visible.");
        }

        if (destinationOffset > Size ||
            (ulong)data.Length > Size - destinationOffset)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destinationOffset),
                "The upload exceeds the buffer size.");
        }

        if (data.IsEmpty)
        {
            return;
        }

        if (destinationOffset > long.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destinationOffset));
        }

        Device deviceHandle = _device.Handle;

        void* mappedMemory = null;

        EnsureSuccess(
            _api.MapMemory(
                deviceHandle,
                _memory,
                0,
                Vk.WholeSize,
                MemoryMapFlags.None,
                &mappedMemory),
            "Map Vulkan buffer memory");

        try
        {
            if (mappedMemory == null)
            {
                throw new InvalidOperationException(
                    "Vulkan mapped memory to a null pointer.");
            }

            byte* destination =
                (byte*)mappedMemory +
                checked((long)destinationOffset);

            Span<byte> target = new(
                destination,
                data.Length);

            data.CopyTo(target);

            if ((MemoryProperties &
                MemoryPropertyFlags.HostCoherentBit) == 0)
            {
                MappedMemoryRange range = new()
                {
                    SType = StructureType.MappedMemoryRange,
                    Memory = _memory,
                    Offset = 0,
                    Size = Vk.WholeSize
                };

                EnsureSuccess(
                    _api.FlushMappedMemoryRanges(
                        deviceHandle,
                        1,
                        in range),
                    "Flush Vulkan buffer memory");
            }
        }

        finally
        {
            _api.UnmapMemory(
                deviceHandle,
                _memory);
        }
    }

    /// <summary>
    /// Copies bytes from host-visible Vulkan memory into a span.
    /// </summary>
    /// <remarks>
    /// The caller must establish completion and visibility of GPU
    /// writes before reading the allocation.
    ///
    /// Non-coherent mapped memory is invalidated before copying.
    /// Invalidating the entire allocation avoids partial-range
    /// nonCoherentAtomSize alignment requirements.
    ///
    /// This method does not wait for GPU work or synchronize
    /// against concurrent GPU or host access.
    /// </remarks>
    public void Read(
        Span<byte> destination,
        ulong sourceOffset = 0)
    {
        ThrowIfDisposed();

        if ((MemoryProperties &
            MemoryPropertyFlags.HostVisibleBit) == 0)
        {
            throw new InvalidOperationException(
                "This buffer is not host-visible.");
        }

        if (sourceOffset > Size ||
            (ulong)destination.Length > Size - sourceOffset)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceOffset),
                "The read exceeds the buffer size.");
        }

        if (destination.IsEmpty)
        {
            return;
        }

        if (sourceOffset > long.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceOffset));
        }

        Device deviceHandle = _device.Handle;

        void* mappedMemory = null;

        EnsureSuccess(
            _api.MapMemory(
                deviceHandle,
                _memory,
                0,
                Vk.WholeSize,
                MemoryMapFlags.None,
                &mappedMemory),
            "Map Vulkan buffer memory for reading");

        try
        {
            if (mappedMemory == null)
            {
                throw new InvalidOperationException(
                    "Vulkan mapped memory to a null pointer.");
            }

            if ((MemoryProperties &
                MemoryPropertyFlags.HostCoherentBit) == 0)
            {
                MappedMemoryRange range = new()
                {
                    SType = StructureType.MappedMemoryRange,
                    Memory = _memory,
                    Offset = 0,
                    Size = Vk.WholeSize
                };

                EnsureSuccess(
                    _api.InvalidateMappedMemoryRanges(
                        deviceHandle,
                        1,
                        in range),
                    "Invalidate Vulkan buffer memory");
            }

            byte* source =
                (byte*)mappedMemory +
                checked((long)sourceOffset);

            ReadOnlySpan<byte> bytes = new(
                source,
                destination.Length);

            bytes.CopyTo(destination);
        }

        finally
        {
            _api.UnmapMemory(
                deviceHandle,
                _memory);
        }
    }

    /// <summary>
    /// Releases the owned Vulkan buffer and allocation.
    /// </summary>
    /// <remarks>
    /// Native destruction may be deferred if a previous transfer
    /// has unresolved execution on the associated logical device.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _ = _instance.Handle;

        Device deviceHandle = _device.Handle;

        VkBuffer buffer = _buffer;
        DeviceMemory memory = _memory;

        _device.ReleaseOrDefer(() =>
        {
            _api.DestroyBuffer(
                deviceHandle,
                buffer,
                null);

            _api.FreeMemory(
                deviceHandle,
                memory,
                null);
        });

        _buffer = default;
        _memory = default;
        _disposed = true;
    }

    /// <summary>
    /// Selects a compatible memory type without accessing Vulkan.
    /// Returns -1 when no candidate qualifies.
    /// </summary>
    internal static int SelectMemoryType(
        uint compatibleTypeBits,
        ReadOnlySpan<MemoryPropertyFlags> memoryTypes,
        MemoryPropertyFlags required,
        MemoryPropertyFlags preferred)
    {
        if (memoryTypes.Length > 32)
        {
            throw new ArgumentOutOfRangeException(
                nameof(memoryTypes));
        }

        int fallback = -1;

        for (int index = 0; index < memoryTypes.Length; index++)
        {
            uint typeBit = 1u << index;

            if ((compatibleTypeBits & typeBit) == 0)
            {
                continue;
            }

            MemoryPropertyFlags properties = memoryTypes[index];

            if ((properties & required) != required)
            {
                continue;
            }

            if ((properties & preferred) == preferred)
            {
                return index;
            }

            if (fallback < 0)
            {
                fallback = index;
            }
        }

        return fallback;
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
