
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Synchronization;

namespace Volt.Graphics.Vulkan.Tests.Synchronization;

public sealed class VulkanPresentationFenceTrackerTests
{
    [Fact]
    public void FenceDescription_UsesCorrectStructureType()
    {
        FenceCreateInfo description =
            VulkanPresentationFenceTracker.CreateFenceDescription();

        Assert.Equal(
            StructureType.FenceCreateInfo,
            description.SType);
    }

    [Fact]
    public void FenceDescription_IsInitiallyUnsignaled()
    {
        FenceCreateInfo description =
            VulkanPresentationFenceTracker.CreateFenceDescription();

        Assert.Equal(
            FenceCreateFlags.None,
            description.Flags);
    }

    [Fact]
    public unsafe void FenceDescription_HasNoExtensionChain()
    {
        FenceCreateInfo description =
            VulkanPresentationFenceTracker.CreateFenceDescription();

        Assert.True(description.PNext == null);
    }

    [Theory]
    [InlineData(0u, 3)]
    [InlineData(1u, 3)]
    [InlineData(2u, 3)]
    public void ValidImageIndices_AreAccepted(
        uint imageIndex,
        int imageCount)
    {
        int result =
            VulkanPresentationFenceTracker.ValidateImageIndex(
                imageIndex,
                imageCount);

        Assert.Equal((int)imageIndex, result);
    }

    [Theory]
    [InlineData(3u, 3)]
    [InlineData(4u, 3)]
    [InlineData(uint.MaxValue, 3)]
    public void InvalidImageIndices_AreRejected(
        uint imageIndex,
        int imageCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VulkanPresentationFenceTracker.ValidateImageIndex(
                imageIndex,
                imageCount));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidImageCounts_AreRejected(int imageCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VulkanPresentationFenceTracker.ValidateImageIndex(
                0,
                imageCount));
    }

    [Fact]
    public void NullInstance_IsRejectedBeforeNativeAccess()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new VulkanPresentationFenceTracker(
                null!,
                null!,
                null!));
    }
}
