
using Silk.NET.Vulkan;
using Volt.Graphics.Devices;
using Volt.Graphics.Vulkan.Devices;

namespace Volt.Graphics.Vulkan.Tests.Devices;

public sealed class VulkanCapabilityMapperTests
{
    [Fact]
    public void Map_UsesVulkanBackend()
    {
        VulkanPhysicalDeviceInfo device = CreateDevice();

        GraphicsDeviceCapabilities capabilities =
            VulkanCapabilityMapper.Map(device);

        Assert.Equal(
            GraphicsBackend.Vulkan,
            capabilities.Backend);
    }

    [Fact]
    public void Map_PreservesAdapterName()
    {
        VulkanPhysicalDeviceInfo device = CreateDevice();

        GraphicsDeviceCapabilities capabilities =
            VulkanCapabilityMapper.Map(device);

        Assert.Equal(
            device.Name,
            capabilities.AdapterName);
    }

    [Fact]
    public void Map_PreservesTextureDimensionLimit()
    {
        VulkanPhysicalDeviceInfo device = CreateDevice() with
        {
            MaxTexture2DDimension = 32768
        };

        GraphicsDeviceCapabilities capabilities =
            VulkanCapabilityMapper.Map(device);

        Assert.Equal(
            32768u,
            capabilities.MaxTexture2DDimension);
    }

    [Fact]
    public void Map_PreservesColorAttachmentLimit()
    {
        VulkanPhysicalDeviceInfo device = CreateDevice() with
        {
            MaxColorAttachments = 4
        };

        GraphicsDeviceCapabilities capabilities =
            VulkanCapabilityMapper.Map(device);

        Assert.Equal(
            4u,
            capabilities.MaxColorAttachments);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Map_PreservesWireframeSupport(bool supported)
    {
        VulkanPhysicalDeviceInfo device = CreateDevice() with
        {
            SupportsWireframe = supported
        };

        GraphicsDeviceCapabilities capabilities =
            VulkanCapabilityMapper.Map(device);

        Assert.Equal(
            supported,
            capabilities.SupportsWireframe);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Map_PreservesTimestampQuerySupport(bool supported)
    {
        VulkanPhysicalDeviceInfo device = CreateDevice() with
        {
            SupportsTimestampQueries = supported
        };

        GraphicsDeviceCapabilities capabilities =
            VulkanCapabilityMapper.Map(device);

        Assert.Equal(
            supported,
            capabilities.SupportsTimestampQueries);
    }

    [Fact]
    public void Map_AllowsOptionalFeaturesToBeUnavailable()
    {
        VulkanPhysicalDeviceInfo device = CreateDevice() with
        {
            SupportsWireframe = false,
            SupportsTimestampQueries = false
        };

        GraphicsDeviceCapabilities capabilities =
            VulkanCapabilityMapper.Map(device);

        Assert.False(capabilities.SupportsWireframe);
        Assert.False(capabilities.SupportsTimestampQueries);

        capabilities.Validate();
    }

    [Fact]
    public void Map_AcceptsGraphicsQueueFamilyZero()
    {
        VulkanPhysicalDeviceInfo device = CreateDevice() with
        {
            GraphicsQueueFamilyIndex = 0
        };

        GraphicsDeviceCapabilities capabilities =
            VulkanCapabilityMapper.Map(device);

        Assert.Equal(
            GraphicsBackend.Vulkan,
            capabilities.Backend);
    }

    [Fact]
    public void Map_RejectsMissingGraphicsQueue()
    {
        VulkanPhysicalDeviceInfo device = CreateDevice() with
        {
            GraphicsQueueFamilyIndex = null
        };

        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () => VulkanCapabilityMapper.Map(device));

        Assert.Equal("device", exception.ParamName);
    }

    [Fact]
    public void Map_RejectsMissingTextureCapability()
    {
        VulkanPhysicalDeviceInfo device = CreateDevice() with
        {
            MaxTexture2DDimension = 0
        };

        Assert.Throws<ArgumentException>(
            () => VulkanCapabilityMapper.Map(device));
    }

    [Fact]
    public void Map_RejectsMissingColorAttachments()
    {
        VulkanPhysicalDeviceInfo device = CreateDevice() with
        {
            MaxColorAttachments = 0
        };

        Assert.Throws<ArgumentException>(
            () => VulkanCapabilityMapper.Map(device));
    }

    [Fact]
    public void Map_RejectsInvalidDeviceName()
    {
        VulkanPhysicalDeviceInfo device = CreateDevice() with
        {
            Name = ""
        };

        Assert.Throws<ArgumentException>(
            () => VulkanCapabilityMapper.Map(device));
    }

    [Fact]
    public void Map_RejectsInvalidApiVersion()
    {
        VulkanPhysicalDeviceInfo device = CreateDevice() with
        {
            ApiVersion = 0
        };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => VulkanCapabilityMapper.Map(device));
    }

    [Fact]
    public void Map_ProducesValidGraphicsCapabilities()
    {
        VulkanPhysicalDeviceInfo device = CreateDevice();

        GraphicsDeviceCapabilities capabilities =
            VulkanCapabilityMapper.Map(device);

        capabilities.Validate();
    }

    [Fact]
    public void Map_IsDeterministic()
    {
        VulkanPhysicalDeviceInfo device = CreateDevice();

        GraphicsDeviceCapabilities first =
            VulkanCapabilityMapper.Map(device);

        GraphicsDeviceCapabilities second =
            VulkanCapabilityMapper.Map(device);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Map_DoesNotModifySourceSnapshot()
    {
        VulkanPhysicalDeviceInfo device = CreateDevice();
        VulkanPhysicalDeviceInfo original = device;

        VulkanCapabilityMapper.Map(device);
        Assert.Equal(original, device);
    }

    private static VulkanPhysicalDeviceInfo CreateDevice()
    {
        return new VulkanPhysicalDeviceInfo(
            Name: "VOLT Test GPU",
            DeviceType: PhysicalDeviceType.DiscreteGpu,
            VendorId: 0x10DE,
            DeviceId: 0x1234,
            ApiVersion: (1u << 22) | (3u << 12),
            GraphicsQueueFamilyIndex: 0,
            MaxTexture2DDimension: 16384,
            MaxColorAttachments: 8,
            SupportsWireframe: true,
            SupportsTimestampQueries: true);
    }
}
