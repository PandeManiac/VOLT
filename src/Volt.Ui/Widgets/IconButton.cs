using Volt.Ui.Input;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;

namespace Volt.Ui.Widgets;

/// <summary>
/// A square, icon-only button with hover, active, focus, and disabled
/// treatments.
/// </summary>
/// <remarks>
/// Icon buttons are the compact vocabulary of the editor chrome:
/// tools, panel actions, viewport controls, and play transport. The
/// glyph is drawn from vector geometry, so the same button works at
/// any interface scale. Every icon-only control is expected to carry
/// a tooltip.
/// </remarks>
public sealed class IconButton : Widget
{
	private UiIcon _icon;
	private float _iconSize;

	public IconButton()
	{
		IsFocusable = true;
		Cursor = UiCursor.Hand;
		MinWidth = 22f;
		MinHeight = 22f;
	}

	public IconButton(UiIcon icon)
		: this()
	{
		_icon = icon;
	}

	public IconButton(UiIcon icon, string tooltip)
		: this(icon)
	{
		TooltipText = tooltip;
	}

	public event Action<IconButton>? Clicked;

	public UiIcon Icon
	{
		get => _icon;
		set => _icon = value;
	}

	/// <summary>
	/// Gets or sets whether the control is shown activated, which is
	/// how a toolbar tool, a snap toggle, or the current play state
	/// reads at a glance.
	/// </summary>
	public bool IsActive { get; set; }

	/// <summary>
	/// Gets or sets the icon color override, used for the play
	/// transport and destructive actions.
	/// </summary>
	public UiColor? IconColor { get; set; }

	/// <summary>
	/// Gets or sets an explicit icon size. Zero uses the theme's
	/// standard icon size.
	/// </summary>
	public float IconSize
	{
		get => _iconSize;
		set => _iconSize = MathF.Max(0f, value);
	}

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		float size = theme.Metrics.ToolButtonSize;

		return new UiSize(
			MathF.Min(size, contentAvailable.Width),
			MathF.Min(size, contentAvailable.Height));
	}

	protected override void RenderSelf(UiDrawList drawList)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		UiStyle style = theme.GetIconButtonStyle(
			this.GetVisualState(),
			IsActive);

		UiRect bounds = GlobalVisualBounds;

		if (style.Background.A > 0)
		{
			drawList.AddRoundedRect(
				bounds,
				style.CornerRadius,
				style.Background);
		}

		if (style.BorderWidth > 0f && style.Border.A > 0)
		{
			drawList.AddBorder(
				bounds,
				style.Border,
				style.BorderWidth,
				style.CornerRadius);
		}

		if (HasFocus)
		{
			drawList.AddBorder(
				bounds,
				theme.Palette.FocusRing,
				1.5f,
				style.CornerRadius);
		}

		float iconSize = _iconSize > 0f
			? _iconSize
			: theme.Metrics.IconSize;

		float inset = MathF.Max(
			2f,
			(bounds.Width - iconSize) * 0.5f);

		UiIconPainter.Draw(
			drawList,
			_icon,
			new UiRect(
				bounds.X + inset,
				bounds.Center.Y - (iconSize * 0.5f),
				MathF.Min(iconSize, MathF.Max(0f, bounds.Width - (inset * 2f))),
				iconSize),
			IconColor ?? style.Foreground,
			1.5f);
	}

	protected internal override void OnClick(int clickCount)
	{
		base.OnClick(clickCount);
		Clicked?.Invoke(this);
	}

	protected internal override bool OnKeyDown(UiKeyEvent key)
	{
		if (key.Key is UiKey.Enter or UiKey.Space)
		{
			OnClick(1);
			return true;
		}

		return false;
	}
}
