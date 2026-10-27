namespace Volt.Ui;

/// <summary>
/// An integer, physical-pixel rectangle suitable for a GPU scissor.
/// </summary>
public readonly record struct UiPixelRect(
	int X,
	int Y,
	int Width,
	int Height)
{
	public static UiPixelRect Empty => default;

	public bool IsEmpty => Width <= 0 || Height <= 0;

	public int Right => X + Width;

	public int Bottom => Y + Height;
}
