using System.Globalization;

namespace Volt.Ui;

/// <summary>
/// A width and height in UI logical units.
/// </summary>
public readonly record struct UiSize(float Width, float Height)
{
	public static UiSize Zero => default;

	public static UiSize operator +(UiSize left, UiSize right)
	{
		return new UiSize(left.Width + right.Width, left.Height + right.Height);
	}

	public static UiSize operator -(UiSize left, UiSize right)
	{
		return new UiSize(left.Width - right.Width, left.Height - right.Height);
	}

	public override string ToString()
	{
		return string.Create(
			CultureInfo.InvariantCulture,
			$"{Width:0.##}x{Height:0.##}");
	}
}
