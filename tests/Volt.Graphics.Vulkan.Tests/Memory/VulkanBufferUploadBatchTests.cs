
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
	public void AddBytes_AcceptsByteGranularPayloads()
	{
		VulkanBufferUploadBatch batch = new();

		int first = batch.AddBytes(
			new byte[] { 10 },
			BufferUsageFlags.StorageBufferBit);

		int second = batch.AddBytes(
			new byte[] { 20, 30, 40 },
			BufferUsageFlags.StorageBufferBit);

		int third = batch.AddBytes(
			new byte[] { 50, 60 },
			BufferUsageFlags.StorageBufferBit);

		BufferCopy firstRegion = batch.GetCopyRegion(first);
		BufferCopy secondRegion = batch.GetCopyRegion(second);
		BufferCopy thirdRegion = batch.GetCopyRegion(third);

		Assert.Equal(0, first);
		Assert.Equal(1, second);
		Assert.Equal(2, third);

		Assert.Equal(0UL, firstRegion.SrcOffset);
		Assert.Equal(1UL, firstRegion.Size);
		Assert.Equal(0UL, firstRegion.DstOffset);

		Assert.Equal(1UL, secondRegion.SrcOffset);
		Assert.Equal(3UL, secondRegion.Size);
		Assert.Equal(0UL, secondRegion.DstOffset);

		Assert.Equal(4UL, thirdRegion.SrcOffset);
		Assert.Equal(2UL, thirdRegion.Size);
		Assert.Equal(0UL, thirdRegion.DstOffset);

		Assert.Equal(3, batch.UploadCount);
		Assert.Equal(6UL, batch.StagingByteCount);

		Assert.Equal(
			new byte[] { 10, 20, 30, 40, 50, 60 },
			batch.GetStagingBytes());
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
	public void Add_AcceptsSingleByte()
	{
		VulkanBufferUploadBatch batch = new();

		byte[] values = [42];

		batch.Add<byte>(
			values,
			BufferUsageFlags.StorageBufferBit);

		Assert.Equal(1UL, batch.StagingByteCount);
		Assert.Equal(new byte[] { 42 }, batch.GetStagingBytes());
	}

	[Fact]
	public void Add_AcceptsSingleUShort()
	{
		VulkanBufferUploadBatch batch = new();

		ushort[] values = [42];

		batch.Add<ushort>(
			values,
			BufferUsageFlags.StorageBufferBit);

		Assert.Equal(2UL, batch.StagingByteCount);
		Assert.Equal(2UL, batch.GetCopyRegion(0).Size);
	}

	[Fact]
	public void AddBytes_RejectsEmptyData()
	{
		VulkanBufferUploadBatch batch = new();

		Assert.Throws<ArgumentOutOfRangeException>(() =>
			batch.AddBytes(
				Array.Empty<byte>(),
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
	public void ValidateUpload_AcceptsArbitraryPositiveSize()
	{
		VulkanBufferUploadBatch.ValidateUpload(
			1,
			BufferUsageFlags.StorageBufferBit);

		VulkanBufferUploadBatch.ValidateUpload(
			2,
			BufferUsageFlags.StorageBufferBit);

		VulkanBufferUploadBatch.ValidateUpload(
			3,
			BufferUsageFlags.StorageBufferBit);

		VulkanBufferUploadBatch.ValidateUpload(
			7,
			BufferUsageFlags.StorageBufferBit);
	}

	[Fact]
	public void ValidateUpload_RejectsZeroSize()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			VulkanBufferUploadBatch.ValidateUpload(
				0,
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

	[Fact]
	public void AddBytes_CrossesChunkBoundariesWithoutPadding()
	{
		VulkanBufferUploadBatch batch = new();

		byte[] first = new byte[65_535];
		byte[] second = [0xA1, 0xB2, 0xC3];
		byte[] third = new byte[65_537];

		for (int index = 0; index < first.Length; index++)
		{
			first[index] = unchecked((byte)(index * 17 + 11));
		}

		for (int index = 0; index < third.Length; index++)
		{
			third[index] = unchecked((byte)(index * 31 + 7));
		}

		byte[] expected = new byte[
			first.Length + second.Length + third.Length];

		first.CopyTo(expected, 0);
		second.CopyTo(expected, first.Length);
		third.CopyTo(
			expected,
			first.Length + second.Length);

		int firstIndex = batch.AddBytes(
			first,
			BufferUsageFlags.StorageBufferBit);

		int secondIndex = batch.AddBytes(
			second,
			BufferUsageFlags.StorageBufferBit);

		int thirdIndex = batch.AddBytes(
			third,
			BufferUsageFlags.StorageBufferBit);

		Array.Fill(first, (byte)0);
		Array.Fill(second, (byte)0);
		Array.Fill(third, (byte)0);

		Assert.Equal(3, batch.UploadCount);
		Assert.Equal((ulong)expected.Length, batch.StagingByteCount);

		Assert.Equal(
			0UL,
			batch.GetCopyRegion(firstIndex).SrcOffset);

		Assert.Equal(
			65_535UL,
			batch.GetCopyRegion(secondIndex).SrcOffset);

		Assert.Equal(
			65_538UL,
			batch.GetCopyRegion(thirdIndex).SrcOffset);

		Assert.Equal(expected, batch.GetStagingBytes());
	}

	[Fact]
	public void AddBytes_LargeBatchAvoidsGeometricGrowthOverhead()
	{
		VulkanBufferUploadBatch batch = new();
		byte[] payload = new byte[1024 * 1024];

		for (int index = 0; index < 8; index++)
		{
			batch.AddBytes(
				payload,
				BufferUsageFlags.StorageBufferBit);
		}

		ulong totalBytes = 8UL * 1024 * 1024;
		Assert.Equal(totalBytes, batch.StagingByteCount);

		Assert.InRange(
			batch.ReservedStagingByteCount,
			totalBytes,
			totalBytes + 64UL * 1024);
	}

	[Fact]
	public void AddBytes_TinyPayloadUsesSmallInitialChunk()
	{
		VulkanBufferUploadBatch batch = new();

		batch.AddBytes(
			[0x42],
			BufferUsageFlags.StorageBufferBit);

		Assert.Equal(1UL, batch.StagingByteCount);

		Assert.InRange(
			batch.ReservedStagingByteCount,
			1UL,
			256UL);

		Assert.Equal(
			new byte[] { 0x42 },
			batch.GetStagingBytes());
	}
}
