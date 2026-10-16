using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Swapchain;

namespace Volt.Graphics.Vulkan.Tests.Swapchain;

public sealed class VulkanSwapchainImageCommandsTests
{
    [Fact]
    public void ClearColor_PreservesFloatingPointComponents()
    {
        ClearColorValue color =
            VulkanSwapchainImageCommands.CreateClearColor(
                0.125f,
                0.25f,
                0.5f,
                0.75f);

        Assert.Equal(0.125f, color.Float32_0);
        Assert.Equal(0.25f, color.Float32_1);
        Assert.Equal(0.5f, color.Float32_2);
        Assert.Equal(0.75f, color.Float32_3);
    }

    [Fact]
    public void ClearColor_DefaultsAlphaToOne()
    {
        ClearColorValue color =
            VulkanSwapchainImageCommands.CreateClearColor(
                0.1f,
                0.2f,
                0.3f);

        Assert.Equal(1.0f, color.Float32_3);
    }

    [Theory]
    [InlineData(float.NaN, 0.0f, 0.0f, 1.0f)]
    [InlineData(float.PositiveInfinity, 0.0f, 0.0f, 1.0f)]
    [InlineData(0.0f, float.NegativeInfinity, 0.0f, 1.0f)]
    [InlineData(0.0f, 0.0f, float.NaN, 1.0f)]
    [InlineData(0.0f, 0.0f, 0.0f, float.NaN)]
    public void ClearColor_RejectsNonFiniteComponents(
        float red,
        float green,
        float blue,
        float alpha)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VulkanSwapchainImageCommands.CreateClearColor(
                red,
                green,
                blue,
                alpha));
    }

    [Fact]
    public void ColorRange_UsesSingleColorSubresource()
    {
        ImageSubresourceRange range =
            VulkanSwapchainImageCommands.CreateColorRange();

        Assert.Equal(ImageAspectFlags.ColorBit, range.AspectMask);
        Assert.Equal(0u, range.BaseMipLevel);
        Assert.Equal(1u, range.LevelCount);
        Assert.Equal(0u, range.BaseArrayLayer);
        Assert.Equal(1u, range.LayerCount);
    }

    [Fact]
    public void TransferBarrier_HasExpectedLayoutsAndAccess()
    {
        Image image = new(123UL);

        ImageMemoryBarrier barrier =
            VulkanSwapchainImageCommands.CreateTransferDestinationBarrier(
                image);

        Assert.Equal(StructureType.ImageMemoryBarrier, barrier.SType);
        Assert.Equal(ImageLayout.Undefined, barrier.OldLayout);
        Assert.Equal(ImageLayout.TransferDstOptimal, barrier.NewLayout);
        Assert.Equal(AccessFlags.None, barrier.SrcAccessMask);
        Assert.Equal(AccessFlags.TransferWriteBit, barrier.DstAccessMask);
        Assert.Equal(Vk.QueueFamilyIgnored, barrier.SrcQueueFamilyIndex);
        Assert.Equal(Vk.QueueFamilyIgnored, barrier.DstQueueFamilyIndex);
        Assert.Equal(image.Handle, barrier.Image.Handle);
        Assert.Equal(ImageAspectFlags.ColorBit, barrier.SubresourceRange.AspectMask);
    }

    [Fact]
    public void PresentBarrier_HasExpectedLayoutsAndAccess()
    {
        Image image = new(456UL);

        ImageMemoryBarrier barrier =
            VulkanSwapchainImageCommands.CreatePresentBarrier(image);

        Assert.Equal(StructureType.ImageMemoryBarrier, barrier.SType);
        Assert.Equal(ImageLayout.TransferDstOptimal, barrier.OldLayout);
        Assert.Equal(ImageLayout.PresentSrcKhr, barrier.NewLayout);
        Assert.Equal(AccessFlags.TransferWriteBit, barrier.SrcAccessMask);
        Assert.Equal(AccessFlags.None, barrier.DstAccessMask);
        Assert.Equal(Vk.QueueFamilyIgnored, barrier.SrcQueueFamilyIndex);
        Assert.Equal(Vk.QueueFamilyIgnored, barrier.DstQueueFamilyIndex);
        Assert.Equal(image.Handle, barrier.Image.Handle);
    }

    [Fact]
    public void ValidTransferDestinationUsage_IsAccepted()
    {
        VulkanSwapchainImageCommands.ValidateArguments(
            new CommandBuffer((nint)1),
            new Image(2UL),
            ImageUsageFlags.ColorAttachmentBit |
            ImageUsageFlags.TransferDstBit);
    }

    [Fact]
    public void MissingTransferDestinationUsage_IsRejected()
    {
        Assert.Throws<NotSupportedException>(
            () => VulkanSwapchainImageCommands.ValidateArguments(
                new CommandBuffer((nint)1),
                new Image(2UL),
                ImageUsageFlags.ColorAttachmentBit));
    }

    [Fact]
    public void NullCommandBuffer_IsRejected()
    {
        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () => VulkanSwapchainImageCommands.ValidateArguments(
                    default,
                    new Image(2UL),
                    ImageUsageFlags.TransferDstBit));

        Assert.Equal("commandBuffer", exception.ParamName);
    }

    [Fact]
    public void NullImage_IsRejected()
    {
        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () => VulkanSwapchainImageCommands.ValidateArguments(
                    new CommandBuffer((nint)1),
                    default,
                    ImageUsageFlags.TransferDstBit));

        Assert.Equal("image", exception.ParamName);
    }

    [Fact]
    public void TransferBarrier_RejectsNullImage()
    {
        Assert.Throws<ArgumentException>(
            () => VulkanSwapchainImageCommands
                .CreateTransferDestinationBarrier(default));
    }

    [Fact]
    public void PresentBarrier_RejectsNullImage()
    {
        Assert.Throws<ArgumentException>(
            () => VulkanSwapchainImageCommands
                .CreatePresentBarrier(default));
    }

    [Fact]
    public void RecordClear_RejectsNullInstanceBeforeNativeCalls()
    {
        Assert.Throws<ArgumentNullException>(
            () => VulkanSwapchainImageCommands.RecordClear(
                null!,
                new CommandBuffer((nint)1),
                new Image(2UL),
                ImageUsageFlags.TransferDstBit,
                default));
    }
}
