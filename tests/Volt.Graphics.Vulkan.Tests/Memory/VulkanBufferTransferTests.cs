
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Memory;

namespace Volt.Graphics.Vulkan.Tests.Memory;

public sealed class VulkanBufferTransferTests
{
    private const BufferUsageFlags SourceUsage =
        BufferUsageFlags.TransferSrcBit;

    private const BufferUsageFlags DestinationUsage =
        BufferUsageFlags.TransferDstBit |
        BufferUsageFlags.StorageBufferBit;

    [Fact]
    public void ValidateUpload_AcceptsStorageBuffer()
    {
        VulkanBufferTransfer.ValidateUpload(
            24,
            BufferUsageFlags.StorageBufferBit);
    }

    [Fact]
    public void ValidateUpload_RejectsEmptyData()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VulkanBufferTransfer.ValidateUpload(
                0,
                BufferUsageFlags.StorageBufferBit));
    }

    [Fact]
    public void ValidateUpload_RejectsEmptyUsage()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VulkanBufferTransfer.ValidateUpload(
                24,
                BufferUsageFlags.None));
    }

    [Fact]
    public void ValidateCopyRegion_AcceptsExactFit()
    {
        VulkanBufferTransfer.ValidateCopyRegion(
            24,
            24,
            SourceUsage,
            DestinationUsage,
            24);
    }

    [Fact]
    public void ValidateCopyRegion_AcceptsPartialCopy()
    {
        VulkanBufferTransfer.ValidateCopyRegion(
            128,
            128,
            SourceUsage,
            DestinationUsage,
            24,
            sourceOffset: 16,
            destinationOffset: 32);
    }

    [Fact]
    public void ValidateCopyRegion_RejectsMissingTransferSourceUsage()
    {
        Assert.Throws<ArgumentException>(() =>
            VulkanBufferTransfer.ValidateCopyRegion(
                24,
                24,
                BufferUsageFlags.StorageBufferBit,
                DestinationUsage,
                24));
    }

    [Fact]
    public void ValidateCopyRegion_RejectsMissingTransferDestinationUsage()
    {
        Assert.Throws<ArgumentException>(() =>
            VulkanBufferTransfer.ValidateCopyRegion(
                24,
                24,
                SourceUsage,
                BufferUsageFlags.StorageBufferBit,
                24));
    }

    [Fact]
    public void ValidateCopyRegion_RejectsZeroLength()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VulkanBufferTransfer.ValidateCopyRegion(
                24,
                24,
                SourceUsage,
                DestinationUsage,
                0));
    }

    [Fact]
    public void ValidateCopyRegion_RejectsSourceOverflow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VulkanBufferTransfer.ValidateCopyRegion(
                24,
                128,
                SourceUsage,
                DestinationUsage,
                16,
                sourceOffset: 12));
    }

    [Fact]
    public void ValidateCopyRegion_RejectsDestinationOverflow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VulkanBufferTransfer.ValidateCopyRegion(
                128,
                24,
                SourceUsage,
                DestinationUsage,
                16,
                destinationOffset: 12));
    }

    [Fact]
    public void ValidateCopyRegion_RejectsOffsetAtEnd()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VulkanBufferTransfer.ValidateCopyRegion(
                24,
                24,
                SourceUsage,
                DestinationUsage,
                1,
                sourceOffset: 24));
    }

    [Fact]
    public void ValidateCopyRegion_DoesNotOverflowOnLargeOffsets()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VulkanBufferTransfer.ValidateCopyRegion(
                24,
                24,
                SourceUsage,
                DestinationUsage,
                ulong.MaxValue,
                sourceOffset: ulong.MaxValue));
    }
}
