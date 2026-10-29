using Silk.NET.Vulkan;
using Volt.Ui.Vulkan;

namespace Volt.Ui.Vulkan.Tests;

/// <summary>
/// Covers the target-format decision that drives sRGB decoding in the
/// UI fragment shader.
/// </summary>
/// <remarks>
/// The palette is authored in sRGB. A framebuffer that already applies
/// the transfer function on write must receive linear values, or every
/// surface is encoded twice and renders far brighter than its palette.
/// A UNORM fallback must receive the authored bytes untouched. These
/// assertions pin both halves of that decision without needing a device.
/// </remarks>
public sealed class UiSurfaceColorSpaceTests
{
	[Theory]
	[InlineData(Format.B8G8R8A8Srgb)]
	[InlineData(Format.R8G8B8A8Srgb)]
	[InlineData(Format.A8B8G8R8SrgbPack32)]
	public void SrgbTargets_RequireDecoding(Format format)
	{
		Assert.True(UiSurfaceColorSpace.IsSrgb(format));
	}

	[Theory]
	[InlineData(Format.B8G8R8A8Unorm)]
	[InlineData(Format.R8G8B8A8Unorm)]
	[InlineData(Format.R16G16B16A16Sfloat)]
	[InlineData(Format.Undefined)]
	public void OtherTargets_PassAuthoredBytesThrough(Format format)
	{
		Assert.False(UiSurfaceColorSpace.IsSrgb(format));
	}
}
