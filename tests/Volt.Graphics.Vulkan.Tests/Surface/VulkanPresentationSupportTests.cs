
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Surface;

namespace Volt.Graphics.Vulkan.Tests.Surface;

public sealed class VulkanPresentationSupportTests
{
    [Fact]
    public void EmptyFamilies_ReturnNull()
    {
        uint? selected =
            VulkanPresentationSupport
                .FindGraphicsPresentationQueueFamily(
                    [],
                    []);

        Assert.Null(selected);
    }

    [Fact]
    public void SingleGraphicsAndPresentFamily_IsSelected()
    {
        QueueFamilyProperties[] families =
        [
            CreateFamily(QueueFlags.GraphicsBit)
        ];

        bool[] presentationSupport = [true];

        uint? selected =
            VulkanPresentationSupport
                .FindGraphicsPresentationQueueFamily(
                    families,
                    presentationSupport);

        Assert.Equal(0u, selected);
    }

    [Fact]
    public void GraphicsWithoutPresentation_IsRejected()
    {
        QueueFamilyProperties[] families =
        [
            CreateFamily(QueueFlags.GraphicsBit)
        ];

        bool[] presentationSupport = [false];

        Assert.Null(
            VulkanPresentationSupport
                .FindGraphicsPresentationQueueFamily(
                    families,
                    presentationSupport));
    }

    [Fact]
    public void PresentationWithoutGraphics_IsRejected()
    {
        QueueFamilyProperties[] families =
        [
            CreateFamily(QueueFlags.ComputeBit)
        ];

        bool[] presentationSupport = [true];

        Assert.Null(
            VulkanPresentationSupport
                .FindGraphicsPresentationQueueFamily(
                    families,
                    presentationSupport));
    }

    [Fact]
    public void QueueFamilyWithoutQueues_IsRejected()
    {
        QueueFamilyProperties[] families =
        [
            CreateFamily(
                QueueFlags.GraphicsBit,
                queueCount: 0)
        ];

        bool[] presentationSupport = [true];

        Assert.Null(
            VulkanPresentationSupport
                .FindGraphicsPresentationQueueFamily(
                    families,
                    presentationSupport));
    }

    [Fact]
    public void FirstCompatibleFamily_IsSelected()
    {
        QueueFamilyProperties[] families =
        [
            CreateFamily(QueueFlags.ComputeBit),
            CreateFamily(QueueFlags.GraphicsBit),
            CreateFamily(QueueFlags.GraphicsBit)
        ];

        bool[] presentationSupport =
        [
            true,
            true,
            true
        ];

        uint? selected =
            VulkanPresentationSupport
                .FindGraphicsPresentationQueueFamily(
                    families,
                    presentationSupport);

        Assert.Equal(1u, selected);
    }

    [Fact]
    public void UnsupportedGraphicsFamily_IsSkipped()
    {
        QueueFamilyProperties[] families =
        [
            CreateFamily(QueueFlags.GraphicsBit),
            CreateFamily(QueueFlags.GraphicsBit)
        ];

        bool[] presentationSupport =
        [
            false,
            true
        ];

        uint? selected =
            VulkanPresentationSupport
                .FindGraphicsPresentationQueueFamily(
                    families,
                    presentationSupport);

        Assert.Equal(1u, selected);
    }

    [Fact]
    public void GraphicsAndComputeFamily_IsAccepted()
    {
        QueueFamilyProperties[] families =
        [
            CreateFamily(
                QueueFlags.GraphicsBit | QueueFlags.ComputeBit)
        ];

        bool[] presentationSupport = [true];

        Assert.Equal(
            0u,
            VulkanPresentationSupport
                .FindGraphicsPresentationQueueFamily(
                    families,
                    presentationSupport));
    }

    [Fact]
    public void MismatchedInputLengths_Throw()
    {
        QueueFamilyProperties[] families =
        [
            CreateFamily(QueueFlags.GraphicsBit)
        ];

        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () => VulkanPresentationSupport
                    .FindGraphicsPresentationQueueFamily(
                        families,
                        []));

        Assert.Equal(
            "presentationSupport",
            exception.ParamName);
    }

    [Fact]
    public void NullInstance_ThrowsBeforeNativeAccess()
    {
        Assert.Throws<ArgumentNullException>(
            () => VulkanPresentationSupport
                .FindGraphicsPresentationQueueFamily(
                    null!,
                    default,
                    null!));
    }

    private static QueueFamilyProperties CreateFamily(
        QueueFlags flags,
        uint queueCount = 1)
    {
        return new QueueFamilyProperties
        {
            QueueFlags = flags,
            QueueCount = queueCount
        };
    }
}
