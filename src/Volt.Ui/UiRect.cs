using System.Globalization;

namespace Volt.Ui;

/// <summary>
/// A rectangle in UI logical coordinates.
/// </summary>
/// <remarks>
/// A rectangle is empty when either dimension is zero or negative.
/// <see cref="Contains(UiPoint)"/> uses half-open bounds, so adjacent
/// rectangles never both claim the same point.
/// </remarks>
public readonly record struct UiRect(float X, float Y, float Width, float Height)
{
	public static UiRect Empty => default;

	public float Right => X + Width;

	public float Bottom => Y + Height;

	public UiPoint Center => new(X + (Width * 0.5f), Y + (Height * 0.5f));

	public UiSize Size => new(Width, Height);

	public bool IsEmpty => Width <= 0f || Height <= 0f;

	public static UiRect FromLtrb(float left, float top, float right, float bottom)
	{
		return new UiRect(left, top, right - left, bottom - top);
	}

	public bool Contains(UiPoint point)
	{
		return point.X >= X &&
			point.Y >= Y &&
			point.X < Right &&
			point.Y < Bottom;
	}

	public bool Contains(UiRect rectangle)
	{
		return rectangle.X >= X &&
			rectangle.Y >= Y &&
			rectangle.Right <= Right &&
			rectangle.Bottom <= Bottom;
	}

	public bool IntersectsWith(UiRect rectangle)
	{
		return rectangle.X < Right &&
			rectangle.Right > X &&
			rectangle.Y < Bottom &&
			rectangle.Bottom > Y;
	}

	/// <summary>
	/// Returns the overlapping area, or <see cref="Empty"/> when the
	/// rectangles do not overlap.
	/// </summary>
	public UiRect Intersect(UiRect rectangle)
	{
		float left = MathF.Max(X, rectangle.X);
		float top = MathF.Max(Y, rectangle.Y);
		float right = MathF.Min(Right, rectangle.Right);
		float bottom = MathF.Min(Bottom, rectangle.Bottom);

		if (right <= left || bottom <= top)
		{
			return Empty;
		}

		return FromLtrb(left, top, right, bottom);
	}

	public UiRect Inset(float left, float top, float right, float bottom)
	{
		return FromLtrb(X + left, Y + top, Right - right, Bottom - bottom);
	}

	public UiRect Inset(float amount)
	{
		return Inset(amount, amount, amount, amount);
	}

	public UiRect Offset(float x, float y)
	{
		return new UiRect(X + x, Y + y, Width, Height);
	}

	public UiRect Offset(UiPoint offset)
	{
		return Offset(offset.X, offset.Y);
	}

	public override string ToString()
	{
		return string.Create(
			CultureInfo.InvariantCulture,
			$"[{X:0.##}, {Y:0.##}, {Width:0.##}x{Height:0.##}]");
	}
}
