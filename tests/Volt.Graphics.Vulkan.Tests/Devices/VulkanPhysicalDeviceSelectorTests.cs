
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;

namespace Volt.Graphics.Vulkan.Tests.Devices;

public sealed class VulkanPhysicalDeviceSelectorTests
{
    [Fact]
    public void EmptyDeviceList_ReturnsNull()
    {
        int? selected = VulkanPhysicalDeviceSelector.SelectIndex(
            []);

        Assert.Null(selected);
    }

    [Fact]
    public void NullDeviceList_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => VulkanPhysicalDeviceSelector.SelectIndex(
                null!));
    }

    [Fact]
    public void SingleSuitableDevice_IsSelected()
    {
        VulkanPhysicalDeviceInfo device = CreateDevice();

        int? selected = VulkanPhysicalDeviceSelector.SelectIndex(
            [device]);

        Assert.Equal(0, selected);
    }

    [Fact]
    public void DeviceWithoutGraphicsQueue_IsRejected()
    {
        VulkanPhysicalDeviceInfo device = CreateDevice() with
        {
            GraphicsQueueFamilyIndex = null
        };

        int? selected = VulkanPhysicalDeviceSelector.SelectIndex(
            [device]);

        Assert.Null(selected);
    }

    [Fact]
    public void DeviceWithoutTextureCapability_IsRejected()
    {
        VulkanPhysicalDeviceInfo device = CreateDevice() with
        {
            MaxTexture2DDimension = 0
        };

        Assert.Null(
            VulkanPhysicalDeviceSelector.SelectIndex([device]));
    }

    [Fact]
    public void DeviceWithoutColorAttachments_IsRejected()
    {
        VulkanPhysicalDeviceInfo device = CreateDevice() with
        {
            MaxColorAttachments = 0
        };

        Assert.Null(
            VulkanPhysicalDeviceSelector.SelectIndex([device]));
    }

    [Fact]
    public void VulkanVersionBelowMinimum_IsRejected()
    {
        VulkanPhysicalDeviceInfo device = CreateDevice() with
        {
            ApiVersion = MakeVersion(1, 1)
        };

        Assert.Null(
            VulkanPhysicalDeviceSelector.SelectIndex([device]));
    }

    [Fact]
    public void VulkanVersionAtMinimum_IsAccepted()
    {
        VulkanPhysicalDeviceInfo device = CreateDevice() with
        {
            ApiVersion = MakeVersion(1, 2)
        };

        Assert.Equal(
            0,
            VulkanPhysicalDeviceSelector.SelectIndex([device]));
    }

    [Fact]
    public void CustomMinimumVersion_IsEnforced()
    {
        VulkanPhysicalDeviceInfo older = CreateDevice() with
        {
            ApiVersion = MakeVersion(1, 2)
        };

        VulkanPhysicalDeviceInfo newer = CreateDevice() with
        {
            ApiVersion = MakeVersion(1, 3)
        };

        int? selected = VulkanPhysicalDeviceSelector.SelectIndex(
            [older, newer],
            requiredApiMajor: 1,
            requiredApiMinor: 3);

        Assert.Equal(1, selected);
    }

    [Fact]
    public void SuitableDiscreteGpu_BeatsIntegratedGpu()
    {
        VulkanPhysicalDeviceInfo integrated = CreateDevice() with
        {
            Name = "Integrated GPU",
            DeviceType = PhysicalDeviceType.IntegratedGpu
        };

        VulkanPhysicalDeviceInfo discrete = CreateDevice() with
        {
            Name = "Discrete GPU",
            DeviceType = PhysicalDeviceType.DiscreteGpu
        };

        int? selected = VulkanPhysicalDeviceSelector.SelectIndex(
            [integrated, discrete]);

        Assert.Equal(1, selected);
    }

    [Fact]
    public void IntegratedGpu_BeatsUnsuitableDiscreteGpu()
    {
        VulkanPhysicalDeviceInfo discrete = CreateDevice() with
        {
            DeviceType = PhysicalDeviceType.DiscreteGpu,
            GraphicsQueueFamilyIndex = null
        };

        VulkanPhysicalDeviceInfo integrated = CreateDevice() with
        {
            DeviceType = PhysicalDeviceType.IntegratedGpu
        };

        int? selected = VulkanPhysicalDeviceSelector.SelectIndex(
            [discrete, integrated]);

        Assert.Equal(1, selected);
    }

    [Fact]
    public void IntegratedGpu_BeatsCpuDevice()
    {
        VulkanPhysicalDeviceInfo cpu = CreateDevice() with
        {
            DeviceType = PhysicalDeviceType.Cpu
        };

        VulkanPhysicalDeviceInfo integrated = CreateDevice() with
        {
            DeviceType = PhysicalDeviceType.IntegratedGpu
        };

        int? selected = VulkanPhysicalDeviceSelector.SelectIndex(
            [cpu, integrated]);

        Assert.Equal(1, selected);
    }

    [Fact]
    public void CpuDevice_CanBeSelectedWhenOnlyOption()
    {
        VulkanPhysicalDeviceInfo cpu = CreateDevice() with
        {
            DeviceType = PhysicalDeviceType.Cpu
        };

        Assert.Equal(
            0,
            VulkanPhysicalDeviceSelector.SelectIndex([cpu]));
    }

    [Fact]
    public void NewerApiVersion_WinsWithinSameDeviceType()
    {
        VulkanPhysicalDeviceInfo older = CreateDevice() with
        {
            ApiVersion = MakeVersion(1, 2)
        };

        VulkanPhysicalDeviceInfo newer = CreateDevice() with
        {
            ApiVersion = MakeVersion(1, 3)
        };

        Assert.Equal(
            1,
            VulkanPhysicalDeviceSelector.SelectIndex(
                [older, newer]));
    }

    [Fact]
    public void WireframeSupport_WinsWhenOtherwiseEquivalent()
    {
        VulkanPhysicalDeviceInfo withoutWireframe = CreateDevice() with
        {
            SupportsWireframe = false
        };

        VulkanPhysicalDeviceInfo withWireframe = CreateDevice() with
        {
            SupportsWireframe = true
        };

        Assert.Equal(
            1,
            VulkanPhysicalDeviceSelector.SelectIndex(
                [withoutWireframe, withWireframe]));
    }

    [Fact]
    public void TimestampQueries_WinWhenOtherwiseEquivalent()
    {
        VulkanPhysicalDeviceInfo withoutTimestamps = CreateDevice() with
        {
            SupportsTimestampQueries = false
        };

        VulkanPhysicalDeviceInfo withTimestamps = CreateDevice() with
        {
            SupportsTimestampQueries = true
        };

        Assert.Equal(
            1,
            VulkanPhysicalDeviceSelector.SelectIndex(
                [withoutTimestamps, withTimestamps]));
    }

    [Fact]
    public void LargerTextureLimit_WinsWhenOtherwiseEquivalent()
    {
        VulkanPhysicalDeviceInfo smaller = CreateDevice() with
        {
            MaxTexture2DDimension = 8192
        };

        VulkanPhysicalDeviceInfo larger = CreateDevice() with
        {
            MaxTexture2DDimension = 16384
        };

        Assert.Equal(
            1,
            VulkanPhysicalDeviceSelector.SelectIndex(
                [smaller, larger]));
    }

    [Fact]
    public void MoreColorAttachments_WinWhenOtherwiseEquivalent()
    {
        VulkanPhysicalDeviceInfo fewer = CreateDevice() with
        {
            MaxColorAttachments = 4
        };

        VulkanPhysicalDeviceInfo more = CreateDevice() with
        {
            MaxColorAttachments = 8
        };

        Assert.Equal(
            1,
            VulkanPhysicalDeviceSelector.SelectIndex(
                [fewer, more]));
    }

    [Fact]
    public void Selection_IsIndependentOfEnumerationOrder()
    {
        VulkanPhysicalDeviceInfo integrated = CreateDevice() with
        {
            Name = "Integrated GPU",
            DeviceType = PhysicalDeviceType.IntegratedGpu
        };

        VulkanPhysicalDeviceInfo discrete = CreateDevice() with
        {
            Name = "Discrete GPU",
            DeviceType = PhysicalDeviceType.DiscreteGpu
        };

        VulkanPhysicalDeviceInfo virtualGpu = CreateDevice() with
        {
            Name = "Virtual GPU",
            DeviceType = PhysicalDeviceType.VirtualGpu
        };

        VulkanPhysicalDeviceInfo[] forward =
        [
            integrated,
            discrete,
            virtualGpu
        ];

        VulkanPhysicalDeviceInfo[] reversed =
        [
            virtualGpu,
            discrete,
            integrated
        ];

        int? forwardIndex =
            VulkanPhysicalDeviceSelector.SelectIndex(forward);

        int? reversedIndex =
            VulkanPhysicalDeviceSelector.SelectIndex(reversed);

        Assert.NotNull(forwardIndex);
        Assert.NotNull(reversedIndex);

        Assert.Equal(
            discrete,
            forward[forwardIndex.Value]);

        Assert.Equal(
            discrete,
            reversed[reversedIndex.Value]);
    }

    [Fact]
    public void VendorId_ProvidesDeterministicTieBreak()
    {
        VulkanPhysicalDeviceInfo first = CreateDevice() with
        {
            VendorId = 20
        };

        VulkanPhysicalDeviceInfo second = CreateDevice() with
        {
            VendorId = 10
        };

        Assert.Equal(
            1,
            VulkanPhysicalDeviceSelector.SelectIndex(
                [first, second]));

        Assert.Equal(
            0,
            VulkanPhysicalDeviceSelector.SelectIndex(
                [second, first]));
    }

    [Fact]
    public void DeviceId_ProvidesDeterministicTieBreak()
    {
        VulkanPhysicalDeviceInfo first = CreateDevice() with
        {
            DeviceId = 20
        };

        VulkanPhysicalDeviceInfo second = CreateDevice() with
        {
            DeviceId = 10
        };

        Assert.Equal(
            1,
            VulkanPhysicalDeviceSelector.SelectIndex(
                [first, second]));
    }

    [Fact]
    public void Name_ProvidesDeterministicTieBreak()
    {
        VulkanPhysicalDeviceInfo first = CreateDevice() with
        {
            Name = "GPU Z"
        };

        VulkanPhysicalDeviceInfo second = CreateDevice() with
        {
            Name = "GPU A"
        };

        Assert.Equal(
            1,
            VulkanPhysicalDeviceSelector.SelectIndex(
                [first, second]));
    }

    [Fact]
    public void IdenticalDevices_KeepFirstIndex()
    {
        VulkanPhysicalDeviceInfo device = CreateDevice();

        Assert.Equal(
            0,
            VulkanPhysicalDeviceSelector.SelectIndex(
                [device, device]));
    }

    [Fact]
    public void InvalidDeviceSnapshot_Throws()
    {
        VulkanPhysicalDeviceInfo invalid = CreateDevice() with
        {
            Name = ""
        };

        Assert.Throws<ArgumentException>(
            () => VulkanPhysicalDeviceSelector.SelectIndex(
                [invalid]));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(128, 2)]
    [InlineData(1, 1024)]
    public void InvalidRequiredVersion_Throws(
        int major,
        int minor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VulkanPhysicalDeviceSelector.SelectIndex(
                [],
                requiredApiMajor: (uint)major,
                requiredApiMinor: (uint)minor));
    }

    private static VulkanPhysicalDeviceInfo CreateDevice()
    {
        return new VulkanPhysicalDeviceInfo(
            Name: "VOLT Test GPU",
            DeviceType: PhysicalDeviceType.DiscreteGpu,
            VendorId: 0x10DE,
            DeviceId: 0x1234,
            ApiVersion: MakeVersion(1, 3),
            GraphicsQueueFamilyIndex: 0,
            MaxTexture2DDimension: 16384,
            MaxColorAttachments: 8,
            SupportsWireframe: true,
            SupportsTimestampQueries: true);
    }

    private static uint MakeVersion(
        uint major,
        uint minor,
        uint patch = 0)
    {
        return (major << 22) |
               (minor << 12) |
               patch;
    }
}
