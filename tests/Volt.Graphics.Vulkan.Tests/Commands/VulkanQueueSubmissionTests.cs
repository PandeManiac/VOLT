using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Commands;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Volt.Graphics.Vulkan.Tests.Commands;

public sealed class VulkanQueueSubmissionTests
{
	[Fact]
	public void Submit_NullInstance_Throws()
	{
		Assert.Throws<ArgumentNullException>(
			() => VulkanQueueSubmission.Submit(
				null!,
				null!,
				new CommandBuffer(1),
				new Fence(1UL)));
	}

	[Fact]
	public void SynchronizedSubmit_NullInstance_Throws()
	{
		Assert.Throws<ArgumentNullException>(
			() => VulkanQueueSubmission.Submit(
				null!,
				null!,
				new CommandBuffer(1),
				new Semaphore(2UL),
				PipelineStageFlags.AllCommandsBit,
				new Semaphore(3UL),
				new Fence(1UL)));
	}

	[Fact]
	public void WaitForFence_NullInstance_Throws()
	{
		Assert.Throws<ArgumentNullException>(
			() => VulkanQueueSubmission.WaitForFence(
				null!,
				null!,
				new Fence(1UL)));
	}

	[Fact]
	public void ResetFence_NullInstance_Throws()
	{
		Assert.Throws<ArgumentNullException>(
			() => VulkanQueueSubmission.ResetFence(
				null!,
				null!,
				new Fence(1UL)));
	}

	[Fact]
	public unsafe void Description_UsesCorrectStructureType()
	{
		CommandBuffer buffer = new(123);

		SubmitInfo description =
			VulkanQueueSubmission.CreateDescription(&buffer);

		Assert.Equal(
			StructureType.SubmitInfo,
			description.SType);
	}

	[Fact]
	public unsafe void Description_ContainsOneCommandBuffer()
	{
		CommandBuffer buffer = new(123);

		SubmitInfo description =
			VulkanQueueSubmission.CreateDescription(&buffer);

		Assert.Equal(1u, description.CommandBufferCount);
		Assert.True(description.PCommandBuffers != null);

		Assert.Equal(
			buffer.Handle,
			description.PCommandBuffers[0].Handle);
	}

	[Fact]
	public unsafe void Description_HasNoSemaphoreDependencies()
	{
		CommandBuffer buffer = new(123);

		SubmitInfo description =
			VulkanQueueSubmission.CreateDescription(&buffer);

		Assert.Equal(0u, description.WaitSemaphoreCount);
		Assert.Equal(0u, description.SignalSemaphoreCount);

		Assert.True(description.PWaitSemaphores == null);
		Assert.True(description.PWaitDstStageMask == null);
		Assert.True(description.PSignalSemaphores == null);
	}

	[Fact]
	public unsafe void Description_HasNoExtensionChain()
	{
		CommandBuffer buffer = new(123);

		SubmitInfo description =
			VulkanQueueSubmission.CreateDescription(&buffer);

		Assert.True(description.PNext == null);
	}

	[Fact]
	public unsafe void Description_RejectsNullPointer()
	{
		Assert.Throws<ArgumentException>(
			() => VulkanQueueSubmission.CreateDescription(null));
	}

	[Fact]
	public void Description_RejectsNullBufferHandle()
	{
		Assert.Throws<ArgumentException>(
			CreateDescriptionWithNullBuffer);
	}

	[Fact]
	public unsafe void SynchronizedDescription_ContainsSemaphoreDependencies()
	{
		CommandBuffer buffer = new(123);
		Semaphore waitSemaphore = new(456UL);
		PipelineStageFlags waitStageMask =
			PipelineStageFlags.AllCommandsBit;
		Semaphore signalSemaphore = new(789UL);

		SubmitInfo description =
			VulkanQueueSubmission.CreateSynchronizedDescription(
				&buffer,
				&waitSemaphore,
				&waitStageMask,
				&signalSemaphore);

		Assert.Equal(
			StructureType.SubmitInfo,
			description.SType);

		Assert.Equal(1u, description.CommandBufferCount);
		Assert.Equal(1u, description.WaitSemaphoreCount);
		Assert.Equal(1u, description.SignalSemaphoreCount);

		Assert.True(description.PCommandBuffers != null);
		Assert.True(description.PWaitSemaphores != null);
		Assert.True(description.PWaitDstStageMask != null);
		Assert.True(description.PSignalSemaphores != null);

		Assert.Equal(
			buffer.Handle,
			description.PCommandBuffers[0].Handle);

		Assert.Equal(
			waitSemaphore.Handle,
			description.PWaitSemaphores[0].Handle);

		Assert.Equal(
			PipelineStageFlags.AllCommandsBit,
			description.PWaitDstStageMask[0]);

		Assert.Equal(
			signalSemaphore.Handle,
			description.PSignalSemaphores[0].Handle);

		Assert.True(description.PNext == null);
	}

	[Fact]
	public void SynchronizedParameters_AcceptTransferWaitStage()
	{
		VulkanQueueSubmission.ValidateSynchronizedParameters(
			new Semaphore(1UL),
			PipelineStageFlags.TransferBit,
			new Semaphore(2UL));
	}

	[Fact]
	public void SynchronizedParameters_RejectNullWaitSemaphore()
	{
		ArgumentException exception =
			Assert.Throws<ArgumentException>(
				() => VulkanQueueSubmission.ValidateSynchronizedParameters(
					default,
					PipelineStageFlags.AllCommandsBit,
					new Semaphore(2UL)));

		Assert.Equal("waitSemaphore", exception.ParamName);
	}

	[Fact]
	public void SynchronizedParameters_RejectEmptyWaitStageMask()
	{
		ArgumentOutOfRangeException exception =
			Assert.Throws<ArgumentOutOfRangeException>(
				() => VulkanQueueSubmission.ValidateSynchronizedParameters(
					new Semaphore(1UL),
					0,
					new Semaphore(2UL)));

		Assert.Equal("waitStageMask", exception.ParamName);
	}

	[Fact]
	public void SynchronizedParameters_RejectNullSignalSemaphore()
	{
		ArgumentException exception =
			Assert.Throws<ArgumentException>(
				() => VulkanQueueSubmission.ValidateSynchronizedParameters(
					new Semaphore(1UL),
					PipelineStageFlags.AllCommandsBit,
					default));

		Assert.Equal("signalSemaphore", exception.ParamName);
	}

	[Fact]
	public void SynchronizedParameters_RejectSameSemaphore()
	{
		ArgumentException exception =
			Assert.Throws<ArgumentException>(
				() => VulkanQueueSubmission.ValidateSynchronizedParameters(
					new Semaphore(1UL),
					PipelineStageFlags.AllCommandsBit,
					new Semaphore(1UL)));

		Assert.Equal("signalSemaphore", exception.ParamName);
	}

	[Fact]
	public void SynchronizedDescription_RejectsNullWaitPointer()
	{
		Assert.Throws<ArgumentException>(
			CreateDescriptionWithNullWaitPointer);
	}

	[Fact]
	public void SynchronizedDescription_RejectsNullStagePointer()
	{
		Assert.Throws<ArgumentException>(
			CreateDescriptionWithNullStagePointer);
	}

	[Fact]
	public void SynchronizedDescription_RejectsNullSignalPointer()
	{
		Assert.Throws<ArgumentException>(
			CreateDescriptionWithNullSignalPointer);
	}

	private static unsafe void CreateDescriptionWithNullBuffer()
	{
		CommandBuffer buffer = default;

		VulkanQueueSubmission.CreateDescription(&buffer);
	}

	private static unsafe void CreateDescriptionWithNullWaitPointer()
	{
		CommandBuffer buffer = new(1);
		PipelineStageFlags stage = PipelineStageFlags.AllCommandsBit;
		Semaphore signal = new(2UL);

		VulkanQueueSubmission.CreateSynchronizedDescription(
			&buffer,
			null,
			&stage,
			&signal);
	}

	private static unsafe void CreateDescriptionWithNullStagePointer()
	{
		CommandBuffer buffer = new(1);
		Semaphore wait = new(1UL);
		Semaphore signal = new(2UL);

		VulkanQueueSubmission.CreateSynchronizedDescription(
			&buffer,
			&wait,
			null,
			&signal);
	}

	private static unsafe void CreateDescriptionWithNullSignalPointer()
	{
		CommandBuffer buffer = new(1);
		Semaphore wait = new(1UL);
		PipelineStageFlags stage = PipelineStageFlags.AllCommandsBit;

		VulkanQueueSubmission.CreateSynchronizedDescription(
			&buffer,
			&wait,
			&stage,
			null);
	}
}
