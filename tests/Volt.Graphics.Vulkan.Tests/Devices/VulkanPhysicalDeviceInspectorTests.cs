
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;

namespace Volt.Graphics.Vulkan.Tests.Devices;

public sealed class VulkanPhysicalDeviceInspectorTests
{
    [Fact]
    public void EmptyQueueFamilies_ReturnsNull()
    {
        uint? index =
            VulkanPhysicalDeviceInspector.FindGraphicsQueueFamily(
                []);

        Assert.Null(index);
    }

    [Fact]
    public void SingleGraphicsQueueFamily_IsSelected()
    {
        QueueFamilyProperties[] families =
        [
            CreateFamily(QueueFlags.GraphicsBit)
        ];

        uint? index =
            VulkanPhysicalDeviceInspector.FindGraphicsQueueFamily(
                families);

        Assert.Equal(0u, index);
    }

    [Fact]
    public void QueueFamilyIndexZero_IsValid()
    {
        QueueFamilyProperties[] families =
        [
            CreateFamily(QueueFlags.GraphicsBit),
            CreateFamily(QueueFlags.ComputeBit)
        ];

        uint? index =
            VulkanPhysicalDeviceInspector.FindGraphicsQueueFamily(
                families);

        Assert.Equal(0u, index);
    }

    [Fact]
    public void ComputeOnlyFamily_IsNotSelected()
    {
        QueueFamilyProperties[] families =
        [
            CreateFamily(QueueFlags.ComputeBit)
        ];

        uint? index =
            VulkanPhysicalDeviceInspector.FindGraphicsQueueFamily(
                families);

        Assert.Null(index);
    }

    [Fact]
    public void TransferOnlyFamily_IsNotSelected()
    {
        QueueFamilyProperties[] families =
        [
            CreateFamily(QueueFlags.TransferBit)
        ];

        uint? index =
            VulkanPhysicalDeviceInspector.FindGraphicsQueueFamily(
                families);

        Assert.Null(index);
    }

    [Fact]
    public void GraphicsFamilyAfterComputeFamily_IsSelected()
    {
        QueueFamilyProperties[] families =
        [
            CreateFamily(QueueFlags.ComputeBit),
            CreateFamily(QueueFlags.GraphicsBit)
        ];

        uint? index =
            VulkanPhysicalDeviceInspector.FindGraphicsQueueFamily(
                families);

        Assert.Equal(1u, index);
    }

    [Fact]
    public void GraphicsAndComputeFamily_IsRecognized()
    {
        QueueFamilyProperties[] families =
        [
            CreateFamily(
                QueueFlags.GraphicsBit | QueueFlags.ComputeBit)
        ];

        uint? index =
            VulkanPhysicalDeviceInspector.FindGraphicsQueueFamily(
                families);

        Assert.Equal(0u, index);
    }

    [Fact]
    public void QueueFamilyWithoutQueues_IsIgnored()
    {
        QueueFamilyProperties[] families =
        [
            CreateFamily(
                QueueFlags.GraphicsBit,
                queueCount: 0)
        ];

        uint? index =
            VulkanPhysicalDeviceInspector.FindGraphicsQueueFamily(
                families);

        Assert.Null(index);
    }

    [Fact]
    public void EmptyGraphicsFamily_IsSkipped()
    {
        QueueFamilyProperties[] families =
        [
            CreateFamily(
                QueueFlags.GraphicsBit,
                queueCount: 0),
            CreateFamily(
                QueueFlags.GraphicsBit,
                queueCount: 1)
        ];

        uint? index =
            VulkanPhysicalDeviceInspector.FindGraphicsQueueFamily(
                families);

        Assert.Equal(1u, index);
    }

    [Fact]
    public void FirstUsableGraphicsFamily_IsPreferred()
    {
        QueueFamilyProperties[] families =
        [
            CreateFamily(QueueFlags.ComputeBit),
            CreateFamily(QueueFlags.GraphicsBit),
            CreateFamily(QueueFlags.GraphicsBit)
        ];

        uint? index =
            VulkanPhysicalDeviceInspector.FindGraphicsQueueFamily(
                families);

        Assert.Equal(1u, index);
    }

    [Fact]
    public void NullInstance_ThrowsBeforeNativeAccess()
    {
        Assert.Throws<ArgumentNullException>(
            () => VulkanPhysicalDeviceInspector.Inspect(
                null!,
                default));
    }

    private static QueueFamilyProperties CreateFamily(
        QueueFlags flags,
        uint queueCount = 1,
        uint timestampValidBits = 0)
    {
        return new QueueFamilyProperties
        {
            QueueFlags = flags,
            QueueCount = queueCount,
            TimestampValidBits = timestampValidBits
        };
    }
}
