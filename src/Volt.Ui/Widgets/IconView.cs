using Volt.Ui.Layout;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;

namespace Volt.Ui.Widgets;

/// <summary>
/// Displays one <see cref="UiIcon"/> in the space it is given.
/// </summary>
/// <remarks>
/// The icon is painted from vector geometry at the widget's own size,
/// so it stays sharp at every interface scale. The default color is
/// the theme's secondary text; callers tint it for asset kinds and
/// diagnostic severities.
/// </remarks>
public sealed class IconView : Widget
{
	private UiColor? _color;
	private float _padding;

	public IconView()
	{
		IsHitTestVisible = false;
	}

	public IconView(UiIcon icon)
		: this()
	{
		Icon = icon;
	}

	public UiIcon Icon { get; set; }

	/// <summary>
	/// Gets or sets the icon color. Null uses the theme's muted text.
	/// </summary>
	public UiColor? Color
	{
		get => _color;
		set => _color = value;
	}

	/// <summary>
	/// Gets or sets the inset kept free around the glyph, which keeps
	/// icons visually aligned with adjacent text.
	/// </summary>
	public float IconPadding
	{
		get => _padding;
		set => _padding = MathF.Max(0f, value);
	}

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		float size = Context?.Theme.Metrics.IconSize ?? 16f;

		return new UiSize(
			MathF.Min(size, contentAvailable.Width),
			MathF.Min(size, contentAvailable.Height));
	}

	protected override void RenderSelf(UiDrawList drawList)
	{
		UiRect bounds = GlobalVisualBounds;

		if (bounds.Width <= 1f || bounds.Height <= 1f)
		{
			return;
		}

		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		UiRect target = new(
			bounds.X + _padding,
			bounds.Y + _padding,
			MathF.Max(0f, bounds.Width - (_padding * 2f)),
			MathF.Max(0f, bounds.Height - (_padding * 2f)));

		UiIconPainter.Draw(
			drawList,
			Icon,
			target,
			_color ?? theme.Palette.TextMuted,
			1.5f);
	}
}
