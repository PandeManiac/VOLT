
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Memory;

namespace Volt.Graphics.Vulkan.Tests.Memory;

public sealed class VulkanBufferTests
{
    [Fact]
    public void SelectMemoryType_ReturnsCompatibleType()
    {
        MemoryPropertyFlags[] types =
        [
            MemoryPropertyFlags.DeviceLocalBit,
            MemoryPropertyFlags.HostVisibleBit |
                MemoryPropertyFlags.HostCoherentBit
        ];

        int result = VulkanBuffer.SelectMemoryType(
            0b10u,
            types,
            MemoryPropertyFlags.HostVisibleBit,
            MemoryPropertyFlags.None);

        Assert.Equal(1, result);
    }

    [Fact]
    public void SelectMemoryType_RespectsCompatibilityBits()
    {
        MemoryPropertyFlags[] types =
        [
            MemoryPropertyFlags.HostVisibleBit,
            MemoryPropertyFlags.HostVisibleBit
        ];

        int result = VulkanBuffer.SelectMemoryType(
            0b10u,
            types,
            MemoryPropertyFlags.HostVisibleBit,
            MemoryPropertyFlags.None);

        Assert.Equal(1, result);
    }

    [Fact]
    public void SelectMemoryType_PrefersRequestedProperties()
    {
        MemoryPropertyFlags[] types =
        [
            MemoryPropertyFlags.HostVisibleBit,
            MemoryPropertyFlags.HostVisibleBit |
                MemoryPropertyFlags.HostCoherentBit
        ];

        int result = VulkanBuffer.SelectMemoryType(
            0b11u,
            types,
            MemoryPropertyFlags.HostVisibleBit,
            MemoryPropertyFlags.HostCoherentBit);

        Assert.Equal(1, result);
    }

    [Fact]
    public void SelectMemoryType_FallsBackWhenPreferenceUnavailable()
    {
        MemoryPropertyFlags[] types =
        [
            MemoryPropertyFlags.DeviceLocalBit,
            MemoryPropertyFlags.HostVisibleBit
        ];

        int result = VulkanBuffer.SelectMemoryType(
            0b11u,
            types,
            MemoryPropertyFlags.HostVisibleBit,
            MemoryPropertyFlags.HostCoherentBit);

        Assert.Equal(1, result);
    }

    [Fact]
    public void SelectMemoryType_RejectsMissingRequiredProperties()
    {
        MemoryPropertyFlags[] types =
        [
            MemoryPropertyFlags.DeviceLocalBit,
            MemoryPropertyFlags.HostVisibleBit
        ];

        int result = VulkanBuffer.SelectMemoryType(
            0b11u,
            types,
            MemoryPropertyFlags.HostVisibleBit |
                MemoryPropertyFlags.HostCoherentBit,
            MemoryPropertyFlags.None);

        Assert.Equal(-1, result);
    }

    [Fact]
    public void SelectMemoryType_RejectsIncompatibleMemory()
    {
        MemoryPropertyFlags[] types =
        [
            MemoryPropertyFlags.HostVisibleBit |
                MemoryPropertyFlags.HostCoherentBit
        ];

        int result = VulkanBuffer.SelectMemoryType(
            0u,
            types,
            MemoryPropertyFlags.HostVisibleBit,
            MemoryPropertyFlags.None);

        Assert.Equal(-1, result);
    }

    [Fact]
    public void SelectMemoryType_HandlesHighestMemoryTypeBit()
    {
        MemoryPropertyFlags[] types =
            new MemoryPropertyFlags[32];

        types[31] = MemoryPropertyFlags.HostVisibleBit;

        int result = VulkanBuffer.SelectMemoryType(
            0x80000000u,
            types,
            MemoryPropertyFlags.HostVisibleBit,
            MemoryPropertyFlags.None);

        Assert.Equal(31, result);
    }

    [Fact]
    public void SelectMemoryType_RejectsTooManyMemoryTypes()
    {
        MemoryPropertyFlags[] types =
            new MemoryPropertyFlags[33];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VulkanBuffer.SelectMemoryType(
                uint.MaxValue,
                types,
                MemoryPropertyFlags.None,
                MemoryPropertyFlags.None));
    }

    [Fact]
    public void SelectMemoryType_EmptyListReturnsNoMatch()
    {
        int result = VulkanBuffer.SelectMemoryType(
            uint.MaxValue,
            ReadOnlySpan<MemoryPropertyFlags>.Empty,
            MemoryPropertyFlags.None,
            MemoryPropertyFlags.None);

        Assert.Equal(-1, result);
    }
}
