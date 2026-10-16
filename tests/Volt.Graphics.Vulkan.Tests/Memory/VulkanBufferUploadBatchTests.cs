
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Memory;

namespace Volt.Graphics.Vulkan.Tests.Memory;

public sealed class VulkanBufferUploadBatchTests
{
    [Fact]
    public void NewBatch_IsEmpty()
    {
        VulkanBufferUploadBatch batch = new();

        Assert.Equal(0, batch.UploadCount);
        Assert.Equal(0UL, batch.StagingByteCount);
    }

    [Fact]
    public void AddBytes_ReturnsSequentialIndices()
    {
        VulkanBufferUploadBatch batch = new();

        int first = batch.AddBytes(
            new byte[] { 1, 2, 3, 4 },
            BufferUsageFlags.StorageBufferBit);

        int second = batch.AddBytes(
            new byte[] { 5, 6, 7, 8 },
            BufferUsageFlags.VertexBufferBit);

        Assert.Equal(0, first);
        Assert.Equal(1, second);
        Assert.Equal(2, batch.UploadCount);
        Assert.Equal(8UL, batch.StagingByteCount);
    }

    [Fact]
    public void AddBytes_PreservesPayloadOrder()
    {
        VulkanBufferUploadBatch batch = new();

        batch.AddBytes(
            new byte[] { 1, 2, 3, 4 },
            BufferUsageFlags.StorageBufferBit);

        batch.AddBytes(
            new byte[] { 5, 6, 7, 8 },
            BufferUsageFlags.StorageBufferBit);

        Assert.Equal(
            new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
            batch.GetStagingBytes());
    }

    [Fact]
    public void AddBytes_ProducesContiguousCopyRegions()
    {
        VulkanBufferUploadBatch batch = new();

        batch.AddBytes(
            new byte[8],
            BufferUsageFlags.StorageBufferBit);

        batch.AddBytes(
            new byte[12],
            BufferUsageFlags.StorageBufferBit);

        BufferCopy first = batch.GetCopyRegion(0);
        BufferCopy second = batch.GetCopyRegion(1);

        Assert.Equal(0UL, first.SrcOffset);
        Assert.Equal(0UL, first.DstOffset);
        Assert.Equal(8UL, first.Size);

        Assert.Equal(8UL, second.SrcOffset);
        Assert.Equal(0UL, second.DstOffset);
        Assert.Equal(12UL, second.Size);

        Assert.Equal(20UL, batch.StagingByteCount);
    }

    [Fact]
    public void Add_AcceptsPackedUIntDescriptors()
    {
        VulkanBufferUploadBatch batch = new();

        uint[] faces =
        [
            0x00000001,
            0x00000002,
            0x00000003,
            0x00000004,
            0x00000005,
            0x00000006
        ];

        int index = batch.Add<uint>(
            faces,
            BufferUsageFlags.StorageBufferBit);

        Assert.Equal(0, index);
        Assert.Equal(24UL, batch.StagingByteCount);
        Assert.Equal(24UL, batch.GetCopyRegion(0).Size);
    }

    [Fact]
    public void Add_AcceptsMultipleValuesThatFormFourBytes()
    {
        VulkanBufferUploadBatch batch = new();

        ushort[] values = [1, 2];

        batch.Add<ushort>(
            values,
            BufferUsageFlags.StorageBufferBit);

        Assert.Equal(4UL, batch.StagingByteCount);
    }

    [Fact]
    public void AddBytes_RejectsEmptyData()
    {
        VulkanBufferUploadBatch batch = new();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            batch.AddBytes(
                Array.Empty<byte>(),
                BufferUsageFlags.StorageBufferBit));
    }

    [Fact]
    public void AddBytes_RejectsUnalignedPayload()
    {
        VulkanBufferUploadBatch batch = new();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            batch.AddBytes(
                new byte[3],
                BufferUsageFlags.StorageBufferBit));

        Assert.Equal(0, batch.UploadCount);
        Assert.Equal(0UL, batch.StagingByteCount);
    }

    [Fact]
    public void AddBytes_RejectsMissingUsage()
    {
        VulkanBufferUploadBatch batch = new();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            batch.AddBytes(
                new byte[4],
                BufferUsageFlags.None));
    }

    [Fact]
    public void ValidateUpload_RejectsUnalignedSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VulkanBufferUploadBatch.ValidateUpload(
                7,
                BufferUsageFlags.StorageBufferBit));
    }

    [Fact]
    public void GetCopyRegion_RejectsInvalidIndex()
    {
        VulkanBufferUploadBatch batch = new();

        batch.AddBytes(
            new byte[4],
            BufferUsageFlags.StorageBufferBit);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            batch.GetCopyRegion(-1));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            batch.GetCopyRegion(1));
    }
}
