using System.Globalization;

namespace Volt.Ui;

/// <summary>
/// A point in UI logical coordinates.
/// </summary>
public readonly record struct UiPoint(float X, float Y)
{
	public static UiPoint Zero => default;

	public static UiPoint operator +(UiPoint left, UiPoint right)
	{
		return new UiPoint(left.X + right.X, left.Y + right.Y);
	}

	public static UiPoint operator -(UiPoint left, UiPoint right)
	{
		return new UiPoint(left.X - right.X, left.Y - right.Y);
	}

	public override string ToString()
	{
		return string.Create(
			CultureInfo.InvariantCulture,
			$"({X:0.##}, {Y:0.##})");
	}
}
