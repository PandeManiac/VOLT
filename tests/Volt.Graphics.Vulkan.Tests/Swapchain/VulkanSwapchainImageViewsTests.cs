
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Swapchain;

namespace Volt.Graphics.Vulkan.Tests.Swapchain;

public sealed class VulkanSwapchainImageViewsTests
{
	[Fact]
	public void NullInstance_ThrowsBeforeNativeAccess()
	{
		Assert.Throws<ArgumentNullException>(
			() => new VulkanSwapchainImageViews(
				null!,
				null!,
				null!));
	}

	[Fact]
	public void Description_UsesCorrectImage()
	{
		Image image = new(123UL);

		ImageViewCreateInfo description =
			VulkanSwapchainImageViews.CreateDescription(
				image,
				Format.B8G8R8A8Srgb);

		Assert.Equal(
			image.Handle,
			description.Image.Handle);

		Assert.Equal(
			StructureType.ImageViewCreateInfo,
			description.SType);
	}

	[Fact]
	public void Description_UsesTwoDimensionalView()
	{
		ImageViewCreateInfo description =
			VulkanSwapchainImageViews.CreateDescription(
				new Image(123UL),
				Format.B8G8R8A8Srgb);

		Assert.Equal(
			ImageViewType.Type2D,
			description.ViewType);

		Assert.Equal(
			Format.B8G8R8A8Srgb,
			description.Format);
	}

	[Fact]
	public void Description_UsesIdentityComponents()
	{
		ImageViewCreateInfo description =
			VulkanSwapchainImageViews.CreateDescription(
				new Image(123UL),
				Format.B8G8R8A8Srgb);

		Assert.Equal(
			ComponentSwizzle.Identity,
			description.Components.R);

		Assert.Equal(
			ComponentSwizzle.Identity,
			description.Components.G);

		Assert.Equal(
			ComponentSwizzle.Identity,
			description.Components.B);

		Assert.Equal(
			ComponentSwizzle.Identity,
			description.Components.A);
	}

	[Fact]
	public void Description_UsesSingleColorSubresource()
	{
		ImageViewCreateInfo description =
			VulkanSwapchainImageViews.CreateDescription(
				new Image(123UL),
				Format.B8G8R8A8Srgb);

		ImageSubresourceRange range =
			description.SubresourceRange;

		Assert.Equal(
			ImageAspectFlags.ColorBit,
			range.AspectMask);

		Assert.Equal(0u, range.BaseMipLevel);
		Assert.Equal(1u, range.LevelCount);
		Assert.Equal(0u, range.BaseArrayLayer);
		Assert.Equal(1u, range.LayerCount);
	}

	[Fact]
	public void InvalidImageHandle_IsRejected()
	{
		ArgumentException exception =
			Assert.Throws<ArgumentException>(
				() => VulkanSwapchainImageViews.CreateDescription(
					default,
					Format.B8G8R8A8Srgb));

		Assert.Equal("image", exception.ParamName);
	}

	[Fact]
	public void UndefinedImageFormat_IsRejected()
	{
		ArgumentException exception =
			Assert.Throws<ArgumentException>(
				() => VulkanSwapchainImageViews.CreateDescription(
					new Image(123UL),
					Format.Undefined));

		Assert.Equal("format", exception.ParamName);
	}

	[Fact]
	public void AlternativeColorFormat_IsPreserved()
	{
		ImageViewCreateInfo description =
			VulkanSwapchainImageViews.CreateDescription(
				new Image(123UL),
				Format.R8G8B8A8Srgb);

		Assert.Equal(
			Format.R8G8B8A8Srgb,
			description.Format);
	}
}
