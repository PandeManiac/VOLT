namespace Volt.Ui.Text;

/// <summary>
/// A rasterized glyph and its placement metrics.
/// </summary>
/// <remarks>
/// All placement values are logical units. <see cref="Uv"/> addresses
/// the glyph's coverage rectangle in the owning font atlas, with
/// (0, 0) at the top-left and (1, 1) at the bottom-right.
/// </remarks>
public readonly record struct UiGlyph(
	int Codepoint,
	float Advance,
	UiRect Uv,
	UiSize Size,
	UiPoint Bearing)
{
	/// <summary>
	/// Gets whether the glyph has no coverage (for example a space).
	/// </summary>
	public bool IsEmpty => Size.Width <= 0f || Size.Height <= 0f;
}
