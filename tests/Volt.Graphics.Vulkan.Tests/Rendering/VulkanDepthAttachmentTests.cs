using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Rendering;
using Volt.Graphics.Vulkan.Tests.Memory;

namespace Volt.Graphics.Vulkan.Tests.Rendering;

public sealed class VulkanDepthAttachmentTests
{
	private static FormatProperties DepthSupported => new()
	{
		OptimalTilingFeatures =
			FormatFeatureFlags.DepthStencilAttachmentBit
	};

	private static FormatProperties DepthUnsupported => new()
	{
		OptimalTilingFeatures = FormatFeatureFlags.SampledImageBit
	};

	[Fact]
	public void SupportedFormats_PrefersD32SfloatThenD16Unorm()
	{
		Format[] expected = [Format.D32Sfloat, Format.D16Unorm];

		Assert.Equal(
			expected,
			VulkanDepthAttachment.SupportedFormats.ToArray());
	}

	[Fact]
	public void SelectPreferredFormat_PrefersD32Sfloat()
	{
		Format? selected =
			VulkanDepthAttachment.SelectPreferredFormat(
				[Format.D32Sfloat, Format.D16Unorm],
				[DepthSupported, DepthSupported]);

		Assert.Equal(Format.D32Sfloat, selected);
	}

	[Fact]
	public void SelectPreferredFormat_FallsBackToD16Unorm()
	{
		Format? selected =
			VulkanDepthAttachment.SelectPreferredFormat(
				[Format.D32Sfloat, Format.D16Unorm],
				[DepthUnsupported, DepthSupported]);

		Assert.Equal(Format.D16Unorm, selected);
	}

	[Fact]
	public void SelectPreferredFormat_ReturnsNullWithoutSupportedCandidate()
	{
		Format? selected =
			VulkanDepthAttachment.SelectPreferredFormat(
				[Format.D32Sfloat, Format.D16Unorm],
				[DepthUnsupported, DepthUnsupported]);

		Assert.Null(selected);
	}

	[Fact]
	public void SelectPreferredFormat_RequiresOptimalTilingSupport()
	{
		FormatProperties linearOnly = new()
		{
			LinearTilingFeatures =
				FormatFeatureFlags.DepthStencilAttachmentBit,
			OptimalTilingFeatures = FormatFeatureFlags.SampledImageBit
		};

		Format? selected =
			VulkanDepthAttachment.SelectPreferredFormat(
				[Format.D32Sfloat],
				[linearOnly]);

		Assert.Null(selected);
	}

	[Fact]
	public void SelectPreferredFormat_RespectsCandidateOrder()
	{
		Format? selected =
			VulkanDepthAttachment.SelectPreferredFormat(
				[Format.D16Unorm, Format.D32Sfloat],
				[DepthSupported, DepthSupported]);

		Assert.Equal(Format.D16Unorm, selected);
	}

	[Fact]
	public void SelectPreferredFormat_RejectsMismatchedPropertyCount()
	{
		Assert.Throws<ArgumentException>(() =>
			VulkanDepthAttachment.SelectPreferredFormat(
				[Format.D32Sfloat, Format.D16Unorm],
				[DepthSupported]));
	}

	[Fact]
	public void SelectPreferredFormat_EmptyCandidatesReturnsNull()
	{
		Format? selected =
			VulkanDepthAttachment.SelectPreferredFormat(
				ReadOnlySpan<Format>.Empty,
				ReadOnlySpan<FormatProperties>.Empty);

		Assert.Null(selected);
	}

	[Theory]
	[InlineData(Format.D32Sfloat)]
	[InlineData(Format.D16Unorm)]
	public void ValidateFormat_AcceptsDepthOnlyFormats(Format format)
	{
		Assert.True(VulkanDepthAttachment.IsSupportedFormat(format));

		VulkanDepthAttachment.ValidateFormat(format);
	}

	[Theory]
	[InlineData(Format.Undefined)]
	[InlineData(Format.D24UnormS8Uint)]
	[InlineData(Format.D32SfloatS8Uint)]
	[InlineData(Format.R8G8B8A8Unorm)]
	public void ValidateFormat_RejectsOtherFormats(Format format)
	{
		Assert.False(VulkanDepthAttachment.IsSupportedFormat(format));

		Assert.Throws<ArgumentOutOfRangeException>(
			() => VulkanDepthAttachment.ValidateFormat(format));
	}

	[Theory]
	[InlineData(1u, 1u)]
	[InlineData(1280u, 720u)]
	[InlineData(3840u, 2160u)]
	public void ValidateExtent_AcceptsPositiveExtent(uint width, uint height)
	{
		VulkanDepthAttachment.ValidateExtent(
			new Extent2D(width, height));
	}

	[Fact]
	public void ValidateExtent_RejectsZeroWidth()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			VulkanDepthAttachment.ValidateExtent(
				new Extent2D(0, 720)));
	}

	[Fact]
	public void ValidateExtent_RejectsZeroHeight()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			VulkanDepthAttachment.ValidateExtent(
				new Extent2D(1280, 0)));
	}

	[Fact]
	public void Constructor_RejectsNullInstance()
	{
		Assert.Throws<ArgumentNullException>(() =>
			new VulkanDepthAttachment(
				null!,
				null!,
				default,
				new Extent2D(1, 1),
				Format.D32Sfloat));
	}

	[VulkanIntegrationFact]
	public void SelectSupportedFormat_ReturnsSupportedDepthFormat()
	{
		RunWithDevice((instance, _device, physicalDevice) =>
		{
			Format format =
				VulkanDepthAttachment.SelectSupportedFormat(
					instance,
					physicalDevice);

			Assert.True(
				VulkanDepthAttachment.IsSupportedFormat(format),
				$"Unexpected depth format: {format}.");

			FormatProperties properties =
				instance.Api.GetPhysicalDeviceFormatProperties(
					physicalDevice,
					format);

			Assert.NotEqual(
				0u,
				(uint)(properties.OptimalTilingFeatures &
					FormatFeatureFlags.DepthStencilAttachmentBit));
		});
	}

	[VulkanIntegrationFact]
	public void SelectSupportedFormat_RejectsInvalidDependencies()
	{
		RunWithDevice((instance, _device, physicalDevice) =>
		{
			Assert.Throws<ArgumentNullException>(() =>
				VulkanDepthAttachment.SelectSupportedFormat(
					null!,
					physicalDevice));

			Assert.Throws<ArgumentException>(() =>
				VulkanDepthAttachment.SelectSupportedFormat(
					instance,
					default));
		});
	}

	[VulkanIntegrationFact]
	public void Create_CreatesNativeDepthResources()
	{
		RunWithDevice((instance, device, physicalDevice) =>
		{
			Format format =
				VulkanDepthAttachment.SelectSupportedFormat(
					instance,
					physicalDevice);

			Extent2D extent = new(1280, 720);

			using VulkanDepthAttachment attachment = new(
				instance,
				device,
				physicalDevice,
				extent,
				format);

			Assert.Equal(format, attachment.Format);
			Assert.Equal(extent, attachment.Extent);
			Assert.Equal(1280u, attachment.Extent.Width);
			Assert.Equal(720u, attachment.Extent.Height);
			Assert.NotEqual(0UL, attachment.View.Handle);
		});
	}

	[VulkanIntegrationFact]
	public void Create_AllocatesOneAttachmentPerSwapchainImage()
	{
		RunWithDevice((instance, device, physicalDevice) =>
		{
			Format format =
				VulkanDepthAttachment.SelectSupportedFormat(
					instance,
					physicalDevice);

			Extent2D extent = new(800, 600);

			VulkanDepthAttachment[] attachments =
				new VulkanDepthAttachment[3];

			try
			{
				for (int index = 0;
					 index < attachments.Length;
					 index++)
				{
					attachments[index] = new VulkanDepthAttachment(
						instance,
						device,
						physicalDevice,
						extent,
						format);

					Assert.NotEqual(
						0UL,
						attachments[index].View.Handle);

					Assert.Equal(
						extent,
						attachments[index].Extent);
				}
			}

			finally
			{
				DisposeAll(attachments);
			}
		});
	}

	[VulkanIntegrationFact]
	public void Create_SupportsRecreatedExtent()
	{
		RunWithDevice((instance, device, physicalDevice) =>
		{
			Format format =
				VulkanDepthAttachment.SelectSupportedFormat(
					instance,
					physicalDevice);

			using (VulkanDepthAttachment first = new(
				instance,
				device,
				physicalDevice,
				new Extent2D(1280, 720),
				format))
			{
				Assert.Equal(1280u, first.Extent.Width);
				Assert.Equal(720u, first.Extent.Height);
			}

			using (VulkanDepthAttachment second = new(
				instance,
				device,
				physicalDevice,
				new Extent2D(1024, 576),
				format))
			{
				Assert.Equal(1024u, second.Extent.Width);
				Assert.Equal(576u, second.Extent.Height);
				Assert.NotEqual(0UL, second.View.Handle);
			}
		});
	}

	[VulkanIntegrationFact]
	public void Create_RejectsInvalidArgumentsBeforeAllocating()
	{
		RunWithDevice((instance, device, physicalDevice) =>
		{
			Format format =
				VulkanDepthAttachment.SelectSupportedFormat(
					instance,
					physicalDevice);

			Extent2D extent = new(64, 64);

			Assert.Throws<ArgumentNullException>(() =>
				new VulkanDepthAttachment(
					null!,
					device,
					physicalDevice,
					extent,
					format));

			Assert.Throws<ArgumentNullException>(() =>
				new VulkanDepthAttachment(
					instance,
					null!,
					physicalDevice,
					extent,
					format));

			Assert.Throws<ArgumentException>(() =>
				new VulkanDepthAttachment(
					instance,
					device,
					default,
					extent,
					format));

			Assert.Throws<ArgumentOutOfRangeException>(() =>
				new VulkanDepthAttachment(
					instance,
					device,
					physicalDevice,
					new Extent2D(0, 64),
					format));

			Assert.Throws<ArgumentOutOfRangeException>(() =>
				new VulkanDepthAttachment(
					instance,
					device,
					physicalDevice,
					new Extent2D(64, 0),
					format));

			Assert.Throws<ArgumentOutOfRangeException>(() =>
				new VulkanDepthAttachment(
					instance,
					device,
					physicalDevice,
					extent,
					Format.Undefined));

			Assert.Throws<ArgumentOutOfRangeException>(() =>
				new VulkanDepthAttachment(
					instance,
					device,
					physicalDevice,
					extent,
					Format.D24UnormS8Uint));
		});
	}

	[VulkanIntegrationFact]
	public void Dispose_IsIdempotentAndGuardsAccess()
	{
		RunWithDevice((instance, device, physicalDevice) =>
		{
			Format format =
				VulkanDepthAttachment.SelectSupportedFormat(
					instance,
					physicalDevice);

			VulkanDepthAttachment attachment = new(
				instance,
				device,
				physicalDevice,
				new Extent2D(64, 64),
				format);

			attachment.Dispose();
			attachment.Dispose();

			Assert.Throws<ObjectDisposedException>(
				() => _ = attachment.View);

			Assert.Throws<ObjectDisposedException>(
				() => _ = attachment.Format);

			Assert.Throws<ObjectDisposedException>(
				() => _ = attachment.Extent);
		});
	}

	private static void DisposeAll(VulkanDepthAttachment[] attachments)
	{
		for (int index = attachments.Length - 1;
			 index >= 0;
			 index--)
		{
			attachments[index]?.Dispose();
		}
	}

	private static void RunWithDevice(
		Action<VulkanInstance, VulkanLogicalDevice, PhysicalDevice> test)
	{
		bool enableValidation =
			Environment.GetEnvironmentVariable(
				"VOLT_VULKAN_VALIDATION") == "1";

		VulkanInstanceDescription description = new(
			ApplicationName: "VOLT Vulkan Depth Attachment Tests",
			EnableValidation: enableValidation);

		using VulkanInstance instance = new(description);

		PhysicalDevice[] nativeDevices =
			instance.EnumeratePhysicalDevices();

		VulkanPhysicalDeviceInfo[] devices =
			new VulkanPhysicalDeviceInfo[nativeDevices.Length];

		for (int index = 0; index < nativeDevices.Length; index++)
		{
			devices[index] =
				VulkanPhysicalDeviceInspector.Inspect(
					instance,
					nativeDevices[index]);
		}

		int? selectedIndex =
			VulkanPhysicalDeviceSelector.SelectIndex(
				devices,
				requiredApiMajor: description.ApiMajor,
				requiredApiMinor: description.ApiMinor);

		if (selectedIndex is not int indexOfSelected)
		{
			throw new InvalidOperationException(
				"No compatible Vulkan graphics device was found.");
		}

		VulkanPhysicalDeviceInfo selected =
			devices[indexOfSelected];

		uint queueFamilyIndex =
			selected.GraphicsQueueFamilyIndex ??
			throw new InvalidOperationException(
				"The selected device has no graphics queue.");

		PhysicalDevice physicalDevice =
			nativeDevices[indexOfSelected];

		using VulkanLogicalDevice device = new(
			instance,
			physicalDevice,
			queueFamilyIndex);

		test(
			instance,
			device,
			physicalDevice);
	}
}
