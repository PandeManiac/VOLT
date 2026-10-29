using Silk.NET.Vulkan;

namespace Volt.Ui.Vulkan;

/// <summary>
/// Describes how a UI render target encodes colour.
/// </summary>
/// <remarks>
/// <para>
/// The UI palette, glyph coverage, and image texels are authored in
/// sRGB. A framebuffer whose format carries the sRGB transfer function
/// expects the fragment shader to emit linear values and encodes them
/// on write, so <see cref="VulkanUiRenderer"/> decodes its colours
/// before blending when this returns <see langword="true"/>.
/// </para>
/// <para>
/// When the surface only offers a UNORM format, the stored values are
/// display-referred already and must pass through untouched. That is
/// why this is a property of the chosen format rather than a constant
/// in the shader: darkening the palette to compensate for a double
/// gamma would break the UNORM fallback.
/// </para>
/// </remarks>
public static class UiSurfaceColorSpace
{
	/// <summary>
	/// Gets whether writes to this format apply the sRGB transfer
	/// function.
	/// </summary>
	public static bool IsSrgb(Format format)
	{
		return format is
			Format.B8G8R8A8Srgb or
			Format.R8G8B8A8Srgb or
			Format.A8B8G8R8SrgbPack32;
	}
}
