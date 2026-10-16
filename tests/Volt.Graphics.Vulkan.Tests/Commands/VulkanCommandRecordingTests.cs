
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Commands;

namespace Volt.Graphics.Vulkan.Tests.Commands;

public sealed class VulkanCommandRecordingTests
{
    [Fact]
    public void Begin_NullInstance_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => VulkanCommandRecording.Begin(
                null!,
                new CommandBuffer(1)));
    }

    [Fact]
    public void End_NullInstance_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => VulkanCommandRecording.End(
                null!,
                new CommandBuffer(1)));
    }

    [Fact]
    public void Description_UsesCorrectStructureType()
    {
        CommandBufferBeginInfo description =
            VulkanCommandRecording.CreateBeginDescription();

        Assert.Equal(
            StructureType.CommandBufferBeginInfo,
            description.SType);
    }

    [Fact]
    public void Description_DefaultsToOneTimeSubmit()
    {
        CommandBufferBeginInfo description =
            VulkanCommandRecording.CreateBeginDescription();

        Assert.Equal(
            CommandBufferUsageFlags.OneTimeSubmitBit,
            description.Flags);
    }

    [Fact]
    public void Description_AllowsNoUsageFlags()
    {
        CommandBufferBeginInfo description =
            VulkanCommandRecording.CreateBeginDescription(
                CommandBufferUsageFlags.None);

        Assert.Equal(
            CommandBufferUsageFlags.None,
            description.Flags);
    }

    [Fact]
    public void Description_AllowsSimultaneousUse()
    {
        CommandBufferBeginInfo description =
            VulkanCommandRecording.CreateBeginDescription(
                CommandBufferUsageFlags.SimultaneousUseBit);

        Assert.Equal(
            CommandBufferUsageFlags.SimultaneousUseBit,
            description.Flags);
    }

    [Fact]
    public void Description_RejectsRenderPassContinue()
    {
        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () => VulkanCommandRecording.CreateBeginDescription(
                    CommandBufferUsageFlags.RenderPassContinueBit));

        Assert.Equal("flags", exception.ParamName);
    }

    [Fact]
    public void Description_RejectsUnknownFlags()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VulkanCommandRecording.CreateBeginDescription(
                (CommandBufferUsageFlags)0x4000));
    }

    [Fact]
    public unsafe void Description_HasNoInheritanceInformation()
    {
        CommandBufferBeginInfo description =
            VulkanCommandRecording.CreateBeginDescription();

        Assert.True(description.PNext == null);
        Assert.True(description.PInheritanceInfo == null);
    }
}
