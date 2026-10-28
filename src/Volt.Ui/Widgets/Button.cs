using Volt.Ui.Input;
using Volt.Ui.Layout;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Text;

namespace Volt.Ui.Widgets;

/// <summary>
/// A standard push button with themed states and keyboard activation.
/// </summary>
public class Button : Widget
{
	private string _text = string.Empty;
	private UiFont? _font;
	private UiTextLayoutResult? _layout;
	private string? _layoutText;
	private UiFont? _layoutFont;
	private float _layoutWidth = -1f;

	public Button()
	{
		Padding = new UiThickness(10f, 4f);
		IsFocusable = true;
		Cursor = UiCursor.Hand;
		MinHeight = 20f;
	}

	public Button(string text)
		: this()
	{
		Text = text;
	}

	public event Action<Button>? Clicked;

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

	public UiFont? Font
	{
		get => _font;
		set
		{
			if (_font == value)
			{
				return;
			}

			_font = value;
			_layout = null;
			InvalidateMeasure();
		}
	}

	/// <summary>
	/// Gets or sets whether the accent-filled primary treatment is
	/// used.
	/// </summary>
	public bool IsPrimary { get; set; }

	/// <summary>
	/// Gets or sets whether the selected button treatment is used.
	/// </summary>
	public bool IsSelected { get; set; }

	protected virtual UiStyle ResolveStyle(UiWidgetState state)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();

		return IsPrimary
			? theme.GetPrimaryButtonStyle(state)
			: theme.GetButtonStyle(state, IsSelected);
	}

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		UiFont font = ResolveFont();
		UiTextLayoutResult layout = EnsureLayout(font, Text, 0f);
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();

		float minimumHeight = MathF.Max(
			0f,
			theme.Metrics.ButtonHeight - Padding.Vertical);

		return new UiSize(
			layout.Width,
			MathF.Max(layout.Height, minimumHeight));
	}

	protected override void RenderSelf(UiDrawList drawList)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		UiStyle style = ResolveStyle(this.GetVisualState());
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

		UiFont font = _font ?? theme.Fonts.Body;

		// The draw list is in root coordinates, so the caption is
		// centered inside the global content rectangle.
		UiRect content = GlobalContentBounds;

		UiTextLayoutResult layout = EnsureLayout(
			font,
			Text,
			content.Width);

		float x = content.X +
			MathF.Max(0f, (content.Width - layout.Width) * 0.5f);

		float y = content.Y +
			MathF.Max(0f, (content.Height - layout.Height) * 0.5f);

		drawList.AddText(
			layout,
			new UiPoint(x, y),
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

	protected UiFont ResolveFont()
	{
		return _font ??
			Context?.Theme.Fonts.Body ??
			throw new InvalidOperationException(
				"The button is not attached to a UI context.");
	}

	protected static UiTextLayoutResult EnsureTextLayout(
		UiFont font,
		string text,
		float width)
	{
		width = float.IsFinite(width)
			? MathF.Max(0.001f, width)
			: MathF.Max(0.001f, font.MeasureText(text));

		return UiTextLayout.Create(
			text,
			font,
			width,
			UiTextWrap.NoWrap,
			UiTextAlignment.Center);
	}

	private UiTextLayoutResult EnsureLayout(
		UiFont font,
		string text,
		float width)
	{
		width = float.IsFinite(width)
			? MathF.Max(0.001f, width)
			: MathF.Max(0.001f, font.MeasureText(text));

		if (_layout is not null &&
			_layoutText == text &&
			_layoutFont == font &&
			_layoutWidth == width)
		{
			return _layout;
		}

		_layout = EnsureTextLayout(font, text, width);
		_layoutText = text;
		_layoutFont = font;
		_layoutWidth = width;

		return _layout;
	}
}
