namespace Volt.Ui;

/// <summary>
/// Four independent edge values, used for margins, padding, and borders.
/// </summary>
public readonly record struct UiThickness(
	float Left,
	float Top,
	float Right,
	float Bottom)
{
	public static UiThickness Zero => default;

	public UiThickness(float uniform)
		: this(uniform, uniform, uniform, uniform)
	{
	}

	public UiThickness(float horizontal, float vertical)
		: this(horizontal, vertical, horizontal, vertical)
	{
	}

	public float Horizontal => Left + Right;

	public float Vertical => Top + Bottom;

	public UiSize Inflate(UiSize size)
	{
		return new UiSize(size.Width + Horizontal, size.Height + Vertical);
	}

	public UiRect Deflate(UiRect rectangle)
	{
		return rectangle.Inset(Left, Top, Right, Bottom);
	}
}
