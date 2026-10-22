using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Memory;

namespace Volt.Graphics.Vulkan.Tests.Memory;

public sealed class VulkanBufferSuballocationLayoutTests
{
	[Fact]
	public void Create_PacksBuffersWithDifferentAlignments()
	{
		MemoryRequirements[] requirements =
		[
			CreateRequirement(3, 1),
			CreateRequirement(5, 16),
			CreateRequirement(7, 4)
		];

		VulkanBufferSuballocationLayout layout =
			VulkanBufferSuballocationLayout.Create(
				requirements,
				memoryTypeIndex: 0);

		Assert.Equal(3, layout.BufferCount);
		Assert.Equal(0U, layout.MemoryTypeIndex);
		Assert.Equal(0UL, layout.GetOffset(0));
		Assert.Equal(16UL, layout.GetOffset(1));
		Assert.Equal(24UL, layout.GetOffset(2));
		Assert.Equal(31UL, layout.AllocationSize);
	}

	[Fact]
	public void Create_UsesMemoryRequirementsSize()
	{
		MemoryRequirements[] requirements =
		[
			CreateRequirement(256, 64),
			CreateRequirement(512, 256),
			CreateRequirement(128, 128)
		];

		VulkanBufferSuballocationLayout layout =
			VulkanBufferSuballocationLayout.Create(
				requirements,
				memoryTypeIndex: 0);

		Assert.Equal(0UL, layout.GetOffset(0));
		Assert.Equal(256UL, layout.GetOffset(1));
		Assert.Equal(768UL, layout.GetOffset(2));

		Assert.Equal(896UL, layout.AllocationSize);
	}

	[Fact]
	public void Create_RespectsMemoryTypeCompatibility()
	{
		MemoryRequirements[] requirements =
		[
			CreateRequirement(32, 16, 0b1010),
			CreateRequirement(48, 16, 0b0010)
		];

		VulkanBufferSuballocationLayout layout =
			VulkanBufferSuballocationLayout.Create(
				requirements,
				memoryTypeIndex: 1);

		Assert.Equal(1U, layout.MemoryTypeIndex);
		Assert.Equal(0UL, layout.GetOffset(0));
		Assert.Equal(32UL, layout.GetOffset(1));
		Assert.Equal(80UL, layout.AllocationSize);
	}

	[Fact]
	public void Create_RejectsIncompatibleMemoryType()
	{
		MemoryRequirements[] requirements =
		[
			CreateRequirement(32, 16, 0b0010),
			CreateRequirement(48, 16, 0b0100)
		];

		Assert.Throws<ArgumentException>(() =>
			VulkanBufferSuballocationLayout.Create(
				requirements,
				memoryTypeIndex: 1));
	}

	[Fact]
	public void Create_SupportsHighestMemoryTypeBit()
	{
		MemoryRequirements[] requirements =
		[
			CreateRequirement(
				64,
				32,
				0x80000000)
		];

		VulkanBufferSuballocationLayout layout =
			VulkanBufferSuballocationLayout.Create(
				requirements,
				memoryTypeIndex: 31);

		Assert.Equal(31U, layout.MemoryTypeIndex);
		Assert.Equal(64UL, layout.AllocationSize);
	}

	[Fact]
	public void Create_RejectsEmptyRequirements()
	{
		MemoryRequirements[] requirements = [];

		Assert.Throws<ArgumentException>(() =>
			VulkanBufferSuballocationLayout.Create(
				requirements,
				memoryTypeIndex: 0));
	}

	[Fact]
	public void Create_RejectsZeroSize()
	{
		MemoryRequirements[] requirements =
		[
			CreateRequirement(0, 16)
		];

		Assert.Throws<ArgumentException>(() =>
			VulkanBufferSuballocationLayout.Create(
				requirements,
				memoryTypeIndex: 0));
	}

	[Fact]
	public void Create_RejectsZeroAlignment()
	{
		MemoryRequirements[] requirements =
		[
			CreateRequirement(64, 0)
		];

		Assert.Throws<ArgumentException>(() =>
			VulkanBufferSuballocationLayout.Create(
				requirements,
				memoryTypeIndex: 0));
	}

	[Fact]
	public void Create_RejectsInvalidMemoryTypeIndex()
	{
		MemoryRequirements[] requirements =
		[
			CreateRequirement(64, 16)
		];

		Assert.Throws<ArgumentOutOfRangeException>(() =>
			VulkanBufferSuballocationLayout.Create(
				requirements,
				memoryTypeIndex: 32));
	}

	[Fact]
	public void Create_RejectsAllocationSizeOverflow()
	{
		MemoryRequirements[] requirements =
		[
			CreateRequirement(ulong.MaxValue, 1),
			CreateRequirement(1, 1)
		];

		Assert.Throws<OverflowException>(() =>
			VulkanBufferSuballocationLayout.Create(
				requirements,
				memoryTypeIndex: 0));
	}

	[Fact]
	public void AlignUp_RejectsOverflow()
	{
		Assert.Throws<OverflowException>(() =>
			VulkanBufferSuballocationLayout.AlignUp(
				ulong.MaxValue,
				8));
	}

	[Fact]
	public void AlignUp_AcceptsAlreadyAlignedValues()
	{
		Assert.Equal(
			256UL,
			VulkanBufferSuballocationLayout.AlignUp(
				256,
				64));
	}

	[Fact]
	public void GetOffset_RejectsInvalidIndices()
	{
		MemoryRequirements[] requirements =
		[
			CreateRequirement(64, 16)
		];

		VulkanBufferSuballocationLayout layout =
			VulkanBufferSuballocationLayout.Create(
				requirements,
				memoryTypeIndex: 0);

		Assert.Throws<ArgumentOutOfRangeException>(() =>
			layout.GetOffset(-1));

		Assert.Throws<ArgumentOutOfRangeException>(() =>
			layout.GetOffset(1));
	}

	private static MemoryRequirements CreateRequirement(
		ulong size,
		ulong alignment,
		uint memoryTypeBits = 1)
	{
		return new MemoryRequirements
		{
			Size = size,
			Alignment = alignment,
			MemoryTypeBits = memoryTypeBits
		};
	}
}
