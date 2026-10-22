using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Memory;

namespace Volt.Graphics.Vulkan.Tests.Memory;

public sealed class VulkanStagingBufferCacheTests
{
	[Fact]
	public void SelectCapacity_RejectsZeroRequirement()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			VulkanStagingBufferCache.SelectCapacity(
				0,
				0));
	}

	[Fact]
	public void SelectCapacity_UsesRequiredInitialCapacity()
	{
		ulong capacity =
			VulkanStagingBufferCache.SelectCapacity(
				0,
				24);

		Assert.Equal(24UL, capacity);
	}

	[Fact]
	public void SelectCapacity_ReusesExistingCapacity()
	{
		ulong capacity =
			VulkanStagingBufferCache.SelectCapacity(
				1024,
				512);

		Assert.Equal(1024UL, capacity);
	}

	[Fact]
	public void SelectCapacity_ReusesExactCapacity()
	{
		ulong capacity =
			VulkanStagingBufferCache.SelectCapacity(
				1024,
				1024);

		Assert.Equal(1024UL, capacity);
	}

	[Fact]
	public void SelectCapacity_DoublesForSmallGrowth()
	{
		ulong capacity =
			VulkanStagingBufferCache.SelectCapacity(
				1024,
				1200);

		Assert.Equal(2048UL, capacity);
	}

	[Fact]
	public void SelectCapacity_AcceptsLargerRequirement()
	{
		ulong capacity =
			VulkanStagingBufferCache.SelectCapacity(
				1024,
				4096);

		Assert.Equal(4096UL, capacity);
	}

	[Fact]
	public void SelectCapacity_AvoidsUnsignedOverflow()
	{
		ulong current = ulong.MaxValue - 16;
		ulong required = ulong.MaxValue - 4;

		ulong capacity =
			VulkanStagingBufferCache.SelectCapacity(
				current,
				required);

		Assert.Equal(required, capacity);
	}

	[Fact]
	public void SelectCapacity_AcceptsMaximumRequirement()
	{
		ulong capacity =
			VulkanStagingBufferCache.SelectCapacity(
				0,
				ulong.MaxValue);

		Assert.Equal(ulong.MaxValue, capacity);
	}

	[Fact]
	public void SubmitAsync_NullInstanceDoesNotConsumeBatch()
	{
		VulkanBufferUploadBatch batch = new();

		batch.AddBytes(
			[1, 2, 3],
			BufferUsageFlags.StorageBufferBit);

		Assert.Throws<ArgumentNullException>(() =>
			batch.SubmitAsync(
				null!,
				null!,
				default));

		batch.AddBytes(
			[4, 5],
			BufferUsageFlags.StorageBufferBit);

		Assert.Equal(2, batch.UploadCount);
		Assert.Equal(5UL, batch.StagingByteCount);
	}
}
