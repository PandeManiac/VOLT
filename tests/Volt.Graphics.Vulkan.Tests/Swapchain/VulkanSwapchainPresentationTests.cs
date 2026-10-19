using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Swapchain;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Volt.Graphics.Vulkan.Tests.Swapchain;

public sealed class VulkanSwapchainPresentationTests
{
    [Fact]
    public void NullInstance_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => VulkanSwapchainPresentation.Present(
                null!,
                null!,
                null!,
                0,
                new Semaphore(1UL)));
    }

    [Fact]
    public void Success_IsAcceptedWithoutRecreation()
    {
        VulkanSwapchainPresentationResult presentation =
            VulkanSwapchainPresentation.InterpretResult(
                Result.Success);

        Assert.Equal(
            VulkanSwapchainPresentationStatus.Success,
            presentation.Status);

        Assert.True(presentation.WasAccepted);
        Assert.False(presentation.RequiresRecreation);
    }

    [Fact]
    public void Suboptimal_IsAcceptedAndRequiresRecreation()
    {
        VulkanSwapchainPresentationResult presentation =
            VulkanSwapchainPresentation.InterpretResult(
                Result.SuboptimalKhr);

        Assert.Equal(
            VulkanSwapchainPresentationStatus.Suboptimal,
            presentation.Status);

        Assert.True(presentation.WasAccepted);
        Assert.True(presentation.RequiresRecreation);
    }

    [Fact]
    public void OutOfDate_RequiresRecreation()
    {
        VulkanSwapchainPresentationResult presentation =
            VulkanSwapchainPresentation.InterpretResult(
                Result.ErrorOutOfDateKhr);

        Assert.Equal(
            VulkanSwapchainPresentationStatus.OutOfDate,
            presentation.Status);

        Assert.False(presentation.WasAccepted);
        Assert.True(presentation.RequiresRecreation);
    }

    [Theory]
    [InlineData(Result.ErrorDeviceLost)]
    [InlineData(Result.ErrorSurfaceLostKhr)]
    [InlineData(Result.Timeout)]
    [InlineData(Result.NotReady)]
    public void UnexpectedResults_Throw(Result result)
    {
        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(
                () => VulkanSwapchainPresentation.InterpretResult(
                    result));

        Assert.Contains(
            result.ToString(),
            exception.Message);
    }

    [Fact]
    public void NullSwapchainHandle_IsRejected()
    {
        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () => VulkanSwapchainPresentation.ValidateParameters(
                    default,
                    0,
                    4,
                    new Semaphore(1UL)));

        Assert.Equal("swapchain", exception.ParamName);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(3u)]
    public void ValidImageIndices_AreAccepted(uint imageIndex)
    {
        VulkanSwapchainPresentation.ValidateParameters(
            new SwapchainKHR(2UL),
            imageIndex,
            4,
            new Semaphore(1UL));
    }

    [Theory]
    [InlineData(4u)]
    [InlineData(5u)]
    [InlineData(uint.MaxValue)]
    public void OutOfRangeImageIndices_AreRejected(uint imageIndex)
    {
        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () => VulkanSwapchainPresentation.ValidateParameters(
                    new SwapchainKHR(2UL),
                    imageIndex,
                    4,
                    new Semaphore(1UL)));

        Assert.Equal("imageIndex", exception.ParamName);
    }

    [Fact]
    public void EmptySwapchain_HasNoValidImageIndex()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VulkanSwapchainPresentation.ValidateParameters(
                new SwapchainKHR(2UL),
                0,
                0,
                new Semaphore(1UL)));
    }

    [Fact]
    public void NullWaitSemaphore_IsRejected()
    {
        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () => VulkanSwapchainPresentation.ValidateParameters(
                    new SwapchainKHR(2UL),
                    0,
                    4,
                    default));

        Assert.Equal("waitSemaphore", exception.ParamName);
    }

    [Fact]
    public unsafe void Description_ContainsOneSwapchainImageAndSemaphore()
    {
        SwapchainKHR swapchain = new(2UL);
        uint imageIndex = 3;
        Semaphore waitSemaphore = new(9UL);

        PresentInfoKHR description =
            VulkanSwapchainPresentation.CreateDescription(
                &swapchain,
                &imageIndex,
                4,
                &waitSemaphore);

        Assert.Equal(
            StructureType.PresentInfoKhr,
            description.SType);

        Assert.Equal(1u, description.SwapchainCount);
        Assert.Equal(1u, description.WaitSemaphoreCount);

        Assert.True(description.PSwapchains != null);
        Assert.True(description.PImageIndices != null);
        Assert.True(description.PWaitSemaphores != null);

        Assert.Equal(2UL, description.PSwapchains[0].Handle);
        Assert.Equal(3u, description.PImageIndices[0]);
        Assert.Equal(9UL, description.PWaitSemaphores[0].Handle);
    }

    [Fact]
    public unsafe void Description_HasNoResultArrayOrExtensionChain()
    {
        SwapchainKHR swapchain = new(2UL);
        uint imageIndex = 0;
        Semaphore waitSemaphore = new(9UL);

        PresentInfoKHR description =
            VulkanSwapchainPresentation.CreateDescription(
                &swapchain,
                &imageIndex,
                4,
                &waitSemaphore);

        Assert.True(description.PResults == null);
        Assert.True(description.PNext == null);
    }

    [Fact]
    public void Description_RejectsNullSwapchainPointer()
    {
        Assert.Throws<ArgumentException>(
            CreateDescriptionWithNullSwapchainPointer);
    }

    [Fact]
    public void Description_RejectsNullImageIndexPointer()
    {
        Assert.Throws<ArgumentException>(
            CreateDescriptionWithNullImageIndexPointer);
    }

    [Fact]
    public void Description_RejectsNullWaitSemaphorePointer()
    {
        Assert.Throws<ArgumentException>(
            CreateDescriptionWithNullWaitSemaphorePointer);
    }

    [Fact]
    public void Description_RejectsNullSwapchainHandle()
    {
        Assert.Throws<ArgumentException>(
            CreateDescriptionWithNullSwapchainHandle);
    }


    [Fact]
    public unsafe void PresentFenceDescription_UsesCorrectStructureType()
    {
        Fence fence = new(42UL);

        SwapchainPresentFenceInfoEXT description =
            VulkanSwapchainPresentation.CreatePresentFenceDescription(
                &fence);

        Assert.Equal(
            StructureType.SwapchainPresentFenceInfoKhr,
            description.SType);

        Assert.Equal(1u, description.SwapchainCount);
        Assert.Equal(42UL, description.PFences[0].Handle);
        Assert.True(description.PNext == null);
    }

    [Fact]
    public void PresentFenceDescription_RejectsNullPointer()
    {
        Assert.Throws<ArgumentException>(() =>
            CreateInvalidPresentFenceDescription(
                useNullPointer: true));
    }

    [Fact]
    public void PresentFenceDescription_RejectsNullHandle()
    {
        Assert.Throws<ArgumentException>(() =>
            CreateInvalidPresentFenceDescription(
                useNullPointer: false));
    }

    private static unsafe void CreateInvalidPresentFenceDescription(
        bool useNullPointer)
    {
        Fence fence = default;

        VulkanSwapchainPresentation.CreatePresentFenceDescription(
            useNullPointer ? null : &fence);
    }

    private static unsafe void CreateDescriptionWithNullSwapchainPointer()
    {
        uint imageIndex = 0;
        Semaphore waitSemaphore = new(1UL);

        VulkanSwapchainPresentation.CreateDescription(
            null,
            &imageIndex,
            4,
            &waitSemaphore);
    }

    private static unsafe void CreateDescriptionWithNullImageIndexPointer()
    {
        SwapchainKHR swapchain = new(2UL);
        Semaphore waitSemaphore = new(1UL);

        VulkanSwapchainPresentation.CreateDescription(
            &swapchain,
            null,
            4,
            &waitSemaphore);
    }

    private static unsafe void CreateDescriptionWithNullWaitSemaphorePointer()
    {
        SwapchainKHR swapchain = new(2UL);
        uint imageIndex = 0;

        VulkanSwapchainPresentation.CreateDescription(
            &swapchain,
            &imageIndex,
            4,
            null);
    }

    private static unsafe void CreateDescriptionWithNullSwapchainHandle()
    {
        SwapchainKHR swapchain = default;
        uint imageIndex = 0;
        Semaphore waitSemaphore = new(1UL);

        VulkanSwapchainPresentation.CreateDescription(
            &swapchain,
            &imageIndex,
            4,
            &waitSemaphore);
    }
}
