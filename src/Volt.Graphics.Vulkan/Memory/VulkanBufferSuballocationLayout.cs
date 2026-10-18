using Silk.NET.Vulkan;

namespace Volt.Graphics.Vulkan.Memory;

/// <summary>
/// Calculates aligned offsets for Vulkan buffers sharing one
/// device-memory allocation.
/// </summary>
/// <remarks>
/// Each input describes the actual memory requirements returned
/// by Vulkan, not the logical size of an upload payload.
///
/// Every buffer must support the selected memory type.
///
/// This class does not allocate memory, bind buffers, or manage
/// resource lifetimes.
/// </remarks>
internal sealed class VulkanBufferSuballocationLayout
{
    private readonly ulong[] _offsets;

    /// <summary>
    /// Gets the memory type shared by every buffer in the layout.
    /// </summary>
    public uint MemoryTypeIndex { get; }

    /// <summary>
    /// Gets the total number of bytes required by the allocation.
    /// </summary>
    public ulong AllocationSize { get; }

    /// <summary>
    /// Gets the number of buffers in the layout.
    /// </summary>
    public int BufferCount => _offsets.Length;

    private VulkanBufferSuballocationLayout(
        ulong[] offsets,
        uint memoryTypeIndex,
        ulong allocationSize)
    {
        _offsets = offsets;

        MemoryTypeIndex = memoryTypeIndex;
        AllocationSize = allocationSize;
    }

    /// <summary>
    /// Creates an aligned layout for one Vulkan memory type.
    /// </summary>
    internal static VulkanBufferSuballocationLayout Create(
        ReadOnlySpan<MemoryRequirements> requirements,
        uint memoryTypeIndex)
    {
        if (requirements.IsEmpty)
        {
            throw new ArgumentException(
                "At least one buffer is required.",
                nameof(requirements));
        }

        if (memoryTypeIndex >= 32)
        {
            throw new ArgumentOutOfRangeException(
                nameof(memoryTypeIndex));
        }

        uint memoryTypeBit = 1u << (int)memoryTypeIndex;

        ulong[] offsets = new ulong[requirements.Length];
        ulong nextOffset = 0;

        for (int index = 0; index < requirements.Length; index++)
        {
            MemoryRequirements requirement = requirements[index];

            if (requirement.Size == 0)
            {
                throw new ArgumentException(
                    "Buffer memory requirements must have " +
                    "a positive size.",
                    nameof(requirements));
            }

            if (requirement.Alignment == 0)
            {
                throw new ArgumentException(
                    "Buffer memory alignment must be positive.",
                    nameof(requirements));
            }

            if ((requirement.MemoryTypeBits & memoryTypeBit) == 0)
            {
                throw new ArgumentException(
                    "A buffer is incompatible with the selected " +
                    "Vulkan memory type.",
                    nameof(requirements));
            }

            ulong offset = AlignUp(
                nextOffset,
                requirement.Alignment);

            offsets[index] = offset;

            nextOffset = checked(offset + requirement.Size);
        }

        return new VulkanBufferSuballocationLayout(
            offsets,
            memoryTypeIndex,
            nextOffset);
    }

    /// <summary>
    /// Gets the memory-binding offset of the selected buffer.
    /// </summary>
    internal ulong GetOffset(int index)
    {
        if ((uint)index >= (uint)_offsets.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index));
        }

        return _offsets[index];
    }

    /// <summary>
    /// Rounds a byte offset upward to the required alignment.
    /// </summary>
    internal static ulong AlignUp(
        ulong value,
        ulong alignment)
    {
        if (alignment == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(alignment));
        }

        ulong remainder = value % alignment;

        if (remainder == 0)
        {
            return value;
        }

        return checked(
            value + (alignment - remainder));
    }
}
