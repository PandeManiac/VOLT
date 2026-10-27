using Volt.Ui.Text;

namespace Volt.Ui.Rendering;

/// <summary>
/// Identifies the texture a geometry batch samples.
/// </summary>
public enum UiTextureKind
{
	White = 0,
	Image = 1,
	Font = 2
}

/// <summary>
/// A renderer-independent texture binding for one UI batch.
/// </summary>
/// <remarks>
/// Font batches carry the <see cref="UiFont"/> itself, because each
/// font size owns its own atlas. Two different font objects never
/// share a batch.
/// </remarks>
public readonly record struct UiTextureKey(
	UiTextureKind Kind,
	int ImageId,
	UiFont? Font)
{
	public static UiTextureKey White => default;

	public static UiTextureKey Image(UiTextureId texture)
	{
		return new UiTextureKey(
			UiTextureKind.Image,
			texture.Value,
			null);
	}

	public static UiTextureKey FromFont(UiFont font)
	{
		ArgumentNullException.ThrowIfNull(font);

		return new UiTextureKey(
			UiTextureKind.Font,
			0,
			font);
	}
}
