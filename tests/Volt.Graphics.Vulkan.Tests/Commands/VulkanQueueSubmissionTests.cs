
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Commands;

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
                new CommandBuffer((nint)1),
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
        CommandBuffer buffer = new((nint)123);

        SubmitInfo description =
            VulkanQueueSubmission.CreateDescription(&buffer);

        Assert.Equal(
            StructureType.SubmitInfo,
            description.SType);
    }

    [Fact]
    public unsafe void Description_ContainsOneCommandBuffer()
    {
        CommandBuffer buffer = new((nint)123);

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
        CommandBuffer buffer = new((nint)123);

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

    private static unsafe void CreateDescriptionWithNullBuffer()
    {
        CommandBuffer buffer = default;
        VulkanQueueSubmission.CreateDescription(&buffer);
    }

}
