using Volt.Ui.Rendering;
using Volt.Ui.Text;

namespace Volt.Ui.Vulkan;

/// <summary>
/// The shader inputs one geometry batch needs: which atlas layer to
/// sample, which sampling path to take, and which descriptor set
/// carries the image.
/// </summary>
/// <remarks>
/// <para>
/// One pipeline serves both sampling paths. A coverage batch samples
/// one channel of a font atlas layer and tints it with the command
/// color; an image batch samples an RGBA texture and is modulated by
/// the command color. <see cref="Mode"/> selects between them.
/// </para>
/// <para>
/// The image is carried by the descriptor set rather than by an index
/// into a descriptor array, so the fragment shader never indexes a
/// sampler array and the renderer needs no descriptor-indexing
/// feature. Set slot zero holds the font atlas and the opaque white
/// texel that solid and font batches use; slot <c>n</c> holds the
/// image registered as texture <c>n</c>.
/// </para>
/// <para>
/// This type is deliberately free of graphics API references so the
/// mapping can be tested without a device.
/// </para>
/// </remarks>
public readonly record struct UiTextureShaderInput(
	float Layer,
	float Mode,
	int SetSlot)
{
	/// <summary>
	/// Samples one channel of a font atlas layer.
	/// </summary>
	public const float CoverageMode = 0f;

	/// <summary>
	/// Samples an RGBA image texture.
	/// </summary>
	public const float ImageMode = 1f;

	/// <summary>
	/// The set slot that carries the font atlas and the white texel.
	/// </summary>
	public const int CoverageSetSlot = 0;

	/// <summary>
	/// Gets the number of descriptor sets a renderer binds from.
	/// </summary>
	public static int SetSlotCount => UiTextureRegistry.MaximumTextures + 1;

	/// <summary>
	/// Resolves the shader inputs for one batch.
	/// </summary>
	/// <param name="texture">The texture the batch was built for.</param>
	/// <param name="fontLayers">
	/// The atlas layer assigned to each font, as the renderer uploaded
	/// them.
	/// </param>
	/// <remarks>
	/// A batch that names no known texture falls back to the opaque
	/// white texel, which is what solid geometry needs: drawing an
	/// unregistered or unknown texture leaves the shape filled with its
	/// command color rather than sampling whatever occupies that slot.
	/// </remarks>
	public static UiTextureShaderInput Resolve(
		UiTextureKey texture,
		IReadOnlyDictionary<UiFont, int> fontLayers)
	{
		ArgumentNullException.ThrowIfNull(fontLayers);

		if (texture.Kind == UiTextureKind.Font &&
			texture.Font is not null &&
			fontLayers.TryGetValue(texture.Font, out int layer))
		{
			return new UiTextureShaderInput(layer, CoverageMode, CoverageSetSlot);
		}

		if (texture.Kind == UiTextureKind.Image &&
			IsImageSlot(texture.ImageId))
		{
			return new UiTextureShaderInput(0f, ImageMode, texture.ImageId);
		}

		return new UiTextureShaderInput(
			0f,
			CoverageMode,
			CoverageSetSlot);
	}

	/// <summary>
	/// Gets whether a texture identifier names a registry slot.
	/// </summary>
	public static bool IsImageSlot(int imageId)
	{
		return imageId >= 1 && imageId <= UiTextureRegistry.MaximumTextures;
	}
}
