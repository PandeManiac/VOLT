
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Commands;

namespace Volt.Graphics.Vulkan.Tests.Commands;

public sealed class VulkanCommandBuffersTests
{
    [Fact]
    public void NullInstance_ThrowsBeforeNativeAccess()
    {
        Assert.Throws<ArgumentNullException>(
            () => new VulkanCommandBuffers(
                null!,
                null!,
                null!,
                1));
    }

    [Fact]
    public void Description_UsesCorrectStructureType()
    {
        CommandBufferAllocateInfo description =
            VulkanCommandBuffers.CreateDescription(
                new CommandPool(1UL),
                1);

        Assert.Equal(
            StructureType.CommandBufferAllocateInfo,
            description.SType);
    }

    [Fact]
    public void Description_PreservesCommandPool()
    {
        CommandPool pool = new(123UL);

        CommandBufferAllocateInfo description =
            VulkanCommandBuffers.CreateDescription(
                pool,
                2);

        Assert.Equal(
            pool.Handle,
            description.CommandPool.Handle);
    }

    [Fact]
    public void Description_AllocatesPrimaryCommandBuffers()
    {
        CommandBufferAllocateInfo description =
            VulkanCommandBuffers.CreateDescription(
                new CommandPool(1UL),
                4);

        Assert.Equal(
            CommandBufferLevel.Primary,
            description.Level);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(16)]
    [InlineData(256)]
    public void Description_PreservesAllocationCount(int count)
    {
        CommandBufferAllocateInfo description =
            VulkanCommandBuffers.CreateDescription(
                new CommandPool(1UL),
                count);

        Assert.Equal(
            checked((uint)count),
            description.CommandBufferCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Description_RejectsInvalidCount(int count)
    {
        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () => VulkanCommandBuffers.CreateDescription(
                    new CommandPool(1UL),
                    count));

        Assert.Equal(
            "count",
            exception.ParamName);
    }

    [Fact]
    public void Description_RejectsNullCommandPoolHandle()
    {
        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () => VulkanCommandBuffers.CreateDescription(
                    default,
                    1));

        Assert.Equal(
            "pool",
            exception.ParamName);
    }

    [Fact]
    public void Description_DoesNotSpecifyExtensionChain()
    {
        CommandBufferAllocateInfo description =
            VulkanCommandBuffers.CreateDescription(
                new CommandPool(1UL),
                1);

        unsafe
        {
            Assert.True(description.PNext == null);
        }
    }
}
