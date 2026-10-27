namespace Volt.Ui;

/// <summary>
/// Identifies a caller-registered image texture.
/// </summary>
/// <remarks>
/// Zero is reserved for the renderer's one-texel white texture used
/// by solid geometry. Font glyphs are identified by their owning font
/// instead, so image identifiers and font atlases can never collide.
/// </remarks>
public readonly record struct UiTextureId(int Value)
{
	public static UiTextureId White => default;

	public bool IsWhite => Value == 0;

	public static UiTextureId FromIndex(int index)
	{
		if (index <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(index),
				"Image texture identifiers start at one.");
		}

		return new UiTextureId(index);
	}
}
