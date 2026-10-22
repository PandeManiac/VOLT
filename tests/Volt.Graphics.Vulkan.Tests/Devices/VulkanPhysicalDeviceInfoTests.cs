
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;

namespace Volt.Graphics.Vulkan.Tests.Devices;

public sealed class VulkanPhysicalDeviceInfoTests
{
	[Fact]
	public void Constructor_StoresDeviceInformation()
	{
		VulkanPhysicalDeviceInfo device = CreateDevice();

		Assert.Equal("VOLT Test GPU", device.Name);
		Assert.Equal(PhysicalDeviceType.DiscreteGpu, device.DeviceType);
		Assert.Equal(0x10DEu, device.VendorId);
		Assert.Equal(0x2684u, device.DeviceId);
		Assert.Equal(0u, device.GraphicsQueueFamilyIndex);

		Assert.Equal(16384u, device.MaxTexture2DDimension);
		Assert.Equal(8u, device.MaxColorAttachments);

		Assert.True(device.SupportsWireframe);
		Assert.True(device.SupportsTimestampQueries);
	}

	[Fact]
	public void ApiVersion_ExposesComponents()
	{
		VulkanPhysicalDeviceInfo device = CreateDevice();
		Assert.Equal(1u, device.ApiMajor);
		Assert.Equal(3u, device.ApiMinor);
		Assert.Equal(280u, device.ApiPatch);
	}

	[Fact]
	public void ApiVersion_IgnoresVariantBits()
	{
		VulkanPhysicalDeviceInfo device = CreateDevice() with
		{
			ApiVersion =
				(3u << 29) |
				(1u << 22) |
				(2u << 12) |
				15u
		};

		Assert.Equal(1u, device.ApiMajor);
		Assert.Equal(2u, device.ApiMinor);
		Assert.Equal(15u, device.ApiPatch);
	}

	[Fact]
	public void DiscreteGpu_IsRecognized()
	{
		VulkanPhysicalDeviceInfo device = CreateDevice();
		Assert.True(device.IsDiscreteGpu);
	}

	[Theory]
	[InlineData(PhysicalDeviceType.IntegratedGpu)]
	[InlineData(PhysicalDeviceType.VirtualGpu)]
	[InlineData(PhysicalDeviceType.Cpu)]
	[InlineData(PhysicalDeviceType.Other)]
	public void NonDiscreteDevices_AreRecognized(
		PhysicalDeviceType type)
	{
		VulkanPhysicalDeviceInfo device = CreateDevice() with
		{
			DeviceType = type
		};

		Assert.False(device.IsDiscreteGpu);
	}

	[Fact]
	public void GraphicsQueueFamilyZero_IsValid()
	{
		VulkanPhysicalDeviceInfo device = CreateDevice() with
		{
			GraphicsQueueFamilyIndex = 0
		};

		Assert.True(device.HasGraphicsQueue);
		Assert.True(device.IsSuitableForGraphics);
	}

	[Fact]
	public void MissingGraphicsQueue_IsUnsuitable()
	{
		VulkanPhysicalDeviceInfo device = CreateDevice() with
		{
			GraphicsQueueFamilyIndex = null
		};

		Assert.False(device.HasGraphicsQueue);
		Assert.False(device.IsSuitableForGraphics);
	}

	[Fact]
	public void MissingTextureCapability_IsUnsuitable()
	{
		VulkanPhysicalDeviceInfo device = CreateDevice() with
		{
			MaxTexture2DDimension = 0
		};

		Assert.False(device.IsSuitableForGraphics);
	}

	[Fact]
	public void MissingColorAttachments_IsUnsuitable()
	{
		VulkanPhysicalDeviceInfo device = CreateDevice() with
		{
			MaxColorAttachments = 0
		};

		Assert.False(device.IsSuitableForGraphics);
	}

	[Fact]
	public void ValidDevice_PassesValidation()
	{
		VulkanPhysicalDeviceInfo device = CreateDevice();
		device.Validate();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void InvalidName_Throws(string? name)
	{
		VulkanPhysicalDeviceInfo device = CreateDevice() with
		{
			Name = name!
		};

		ArgumentException exception =
			Assert.Throws<ArgumentException>(device.Validate);

		Assert.Equal("Name", exception.ParamName);
	}

	[Fact]
	public void EmbeddedNullName_Throws()
	{
		VulkanPhysicalDeviceInfo device = CreateDevice() with
		{
			Name = "VOLT\0GPU"
		};

		Assert.Throws<ArgumentException>(device.Validate);
	}

	[Fact]
	public void InvalidApiVersion_Throws()
	{
		VulkanPhysicalDeviceInfo device = CreateDevice() with
		{
			ApiVersion = 0
		};

		ArgumentOutOfRangeException exception =
			Assert.Throws<ArgumentOutOfRangeException>(
				device.Validate);

		Assert.Equal("ApiVersion", exception.ParamName);
	}

	[Fact]
	public void DeviceWithoutGraphicsQueue_CanStillBeValid()
	{
		VulkanPhysicalDeviceInfo device = CreateDevice() with
		{
			GraphicsQueueFamilyIndex = null
		};

		device.Validate();
		Assert.False(device.IsSuitableForGraphics);
	}

	[Fact]
	public void EqualDeviceSnapshots_CompareEqual()
	{
		VulkanPhysicalDeviceInfo first = CreateDevice();
		VulkanPhysicalDeviceInfo second = CreateDevice();
		Assert.Equal(first, second);
	}

	[Fact]
	public void DifferentDeviceSnapshots_CompareUnequal()
	{
		VulkanPhysicalDeviceInfo first = CreateDevice();

		VulkanPhysicalDeviceInfo second = CreateDevice() with
		{
			DeviceId = 0x1234
		};

		Assert.NotEqual(first, second);
	}

	private static VulkanPhysicalDeviceInfo CreateDevice()
	{
		return new VulkanPhysicalDeviceInfo(
			Name: "VOLT Test GPU",
			DeviceType: PhysicalDeviceType.DiscreteGpu,
			VendorId: 0x10DE,
			DeviceId: 0x2684,
			ApiVersion: (1u << 22) | (3u << 12) | 280u,
			GraphicsQueueFamilyIndex: 0,
			MaxTexture2DDimension: 16384,
			MaxColorAttachments: 8,
			SupportsWireframe: true,
			SupportsTimestampQueries: true);
	}
}
