
using System.Threading;
using Silk.NET.Vulkan;

namespace Volt.Graphics.Vulkan.Memory;

/// <summary>
/// Owns device memory shared by a fixed number of Vulkan buffers.
/// </summary>
/// <remarks>
/// Every buffer relinquishes one reference after its native
/// VkBuffer has been destroyed.
///
/// The final release frees the shared device-memory allocation.
/// GPU completion and deferred destruction are managed by the
/// owning VulkanBuffer instances.
/// </remarks>
internal sealed unsafe class VulkanSharedBufferMemory
{
    private readonly Vk _api;
    private readonly Device _device;
    private readonly DeviceMemory _memory;

    private int _remainingBuffers;

    internal VulkanSharedBufferMemory(
        Vk api,
        Device device,
        DeviceMemory memory,
        int bufferCount)
    {
        ArgumentNullException.ThrowIfNull(api);

        if (device.Handle == 0)
        {
            throw new ArgumentException(
                "A valid device is required.",
                nameof(device));
        }

        if (memory.Handle == 0)
        {
            throw new ArgumentException(
                "A valid memory allocation is required.",
                nameof(memory));
        }

        if (bufferCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bufferCount));
        }

        _api = api;
        _device = device;
        _memory = memory;
        _remainingBuffers = bufferCount;
    }

    /// <summary>
    /// Releases one buffer's ownership of the allocation.
    /// </summary>
    /// <remarks>
    /// Must be called only after the associated native buffer
    /// has been destroyed and is no longer using the memory.
    /// </remarks>
    internal void ReleaseBuffer()
    {
        int remaining = Interlocked.Decrement(
            ref _remainingBuffers);

        if (remaining < 0)
        {
            throw new InvalidOperationException(
                "Shared Vulkan memory was released too many times.");
        }

        if (remaining == 0)
        {
            _api.FreeMemory(
                _device,
                _memory,
                null);
        }
    }
}
