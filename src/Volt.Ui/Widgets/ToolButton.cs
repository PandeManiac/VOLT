using Volt.Ui.Input;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Text;

namespace Volt.Ui.Widgets;

/// <summary>
/// A button that pairs an icon with a caption.
/// </summary>
/// <remarks>
/// The toolbar uses this shape for named tools and labeled actions,
/// where an icon alone would be ambiguous but a full-width button
/// would be too heavy. The active treatment fills with the accent
/// surface so the current tool is unmistakable.
/// </remarks>
public sealed class ToolButton : Widget
{
	private UiTextLayoutResult? _layout;
	private string _text = string.Empty;
	private string? _layoutText;
	private UiFont? _layoutFont;
	private float _layoutWidth = -1f;

	public ToolButton()
	{
		IsFocusable = true;
		Cursor = UiCursor.Hand;
		MinHeight = 22f;
	}

	public ToolButton(UiIcon icon, string text)
		: this()
	{
		Icon = icon;
		Text = text;
	}

	public event Action<ToolButton>? Clicked;

	public UiIcon Icon { get; set; }

	public string Text
	{
		get => _text;
		set
		{
			string next = value ?? string.Empty;

			if (_text == next)
			{
				return;
			}

			_text = next;
			_layout = null;
			InvalidateMeasure();
		}
	}

	public bool IsActive { get; set; }

	public bool ShowLabel { get; set; } = true;

	public UiColor? IconColor { get; set; }

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		float icon = theme.Metrics.IconSize;
		float width = icon + 14f;

		if (ShowLabel)
		{
			UiFont font = theme.Fonts.Body;
			UiTextLayoutResult layout = EnsureLayout(font);
			width += layout.Width + 4f;
		}

		float height = MathF.Max(
			theme.Metrics.IconSize + 8f,
			ShowLabel ? theme.Fonts.Body.LineHeight + 8f : icon + 8f);

		return new UiSize(
			MathF.Min(width, contentAvailable.Width),
			MathF.Min(height, contentAvailable.Height));
	}

	protected override void RenderSelf(UiDrawList drawList)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		UiStyle style = theme.GetToolButtonStyle(
			this.GetVisualState(),
			IsActive);

		UiRect bounds = GlobalVisualBounds;

		drawList.AddRoundedRect(
			bounds,
			style.CornerRadius,
			style.Background);

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

		float icon = theme.Metrics.IconSize;
		float x = bounds.X + 8f;

		UiIconPainter.Draw(
			drawList,
			Icon,
			new UiRect(
				x,
				bounds.Center.Y - (icon * 0.5f),
				icon,
				icon),
			IconColor ?? style.Foreground,
			1.5f);

		x += icon + 6f;

		if (!ShowLabel || _text.Length == 0)
		{
			return;
		}

		UiTextLayoutResult layout = EnsureLayout(theme.Fonts.Body);

		drawList.AddText(
			layout,
			new UiPoint(
				x,
				bounds.Center.Y - (layout.Height * 0.5f)),
			style.Foreground);
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

	private UiTextLayoutResult EnsureLayout(UiFont font)
	{
		if (_layout is not null &&
			_layoutText == _text &&
			_layoutFont == font &&
			_layoutWidth == -1f)
		{
			return _layout;
		}

		_layout = UiTextLayout.Create(
			_text,
			font,
			MathF.Max(1f, font.MeasureText(_text)),
			UiTextWrap.NoWrap,
			UiTextAlignment.Start);

		_layoutText = _text;
		_layoutFont = font;
		_layoutWidth = -1f;

		return _layout;
	}
}
