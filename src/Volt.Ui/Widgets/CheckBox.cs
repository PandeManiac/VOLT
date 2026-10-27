using Volt.Ui.Input;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Text;

namespace Volt.Ui.Widgets;

/// <summary>
/// A two-state checkbox with a text label.
/// </summary>
public class CheckBox : Widget
{
	private string _text = string.Empty;
	private bool _isChecked;
	private UiTextLayoutResult? _layout;
	private string? _layoutText;
	private UiFont? _layoutFont;
	private float _layoutWidth = -1f;

	public CheckBox()
	{
		IsFocusable = true;
		Cursor = UiCursor.Hand;
		MinHeight = 20f;
	}

	public CheckBox(string text)
		: this()
	{
		Text = text;
	}

	public event Action<CheckBox>? CheckedChanged;

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

	public UiFont? Font { get; set; }

	public UiColor? TextColor { get; set; }

	public bool IsChecked
	{
		get => _isChecked;
		set
		{
			if (_isChecked == value)
			{
				return;
			}

			_isChecked = value;
			InvalidateVisual();
		}
	}

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		UiFont font = ResolveFont();
		UiTextLayoutResult layout = EnsureLayout(font, 0f);
		float box = BoxSize;
		float spacing = Context?.Theme.Metrics.Spacing ?? 6f;

		return new UiSize(
			box + spacing + layout.Width,
			MathF.Max(box, layout.Height));
	}

	protected override void RenderSelf(UiDrawList drawList)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		UiFont font = ResolveFont();
		UiTextLayoutResult layout = EnsureLayout(
			font,
			MathF.Max(0f, ContentBounds.Width - BoxSize - 6f));

		float box = BoxSize;
		float boxY = ContentBounds.Y +
			MathF.Max(0f, (ContentBounds.Height - box) * 0.5f);

		UiRect boxRect = new(
			ContentBounds.X,
			boxY,
			box,
			box);

		UiWidgetState state = this.GetVisualState();
		UiColor border = state == UiWidgetState.Hovered
			? theme.Palette.BorderStrong
			: theme.Palette.Border;

		drawList.AddRoundedRect(
			boxRect,
			3f,
			_isChecked
				? theme.Palette.Accent
				: theme.Palette.WindowBackground);

		drawList.AddBorder(
			boxRect,
			_isChecked ? theme.Palette.Accent : border,
			1f,
			3f);

		if (_isChecked)
		{
			UiColor mark = theme.Palette.AccentText;
			float left = boxRect.X + (box * 0.24f);
			float middle = boxRect.X + (box * 0.43f);
			float right = boxRect.X + (box * 0.76f);

			drawList.AddLine(
				new UiPoint(left, boxRect.Y + (box * 0.53f)),
				new UiPoint(middle, boxRect.Y + (box * 0.71f)),
				1.7f,
				mark);

			drawList.AddLine(
				new UiPoint(middle, boxRect.Y + (box * 0.71f)),
				new UiPoint(right, boxRect.Y + (box * 0.31f)),
				1.7f,
				mark);
		}

		float textX = boxRect.Right + 6f;
		float textY = ContentBounds.Y +
			MathF.Max(0f, (ContentBounds.Height - layout.Height) * 0.5f);

		drawList.AddText(
			layout,
			new UiPoint(textX, textY),
			TextColor ?? (state == UiWidgetState.Disabled
				? theme.Palette.TextDisabled
				: theme.Palette.Text));
	}

	protected internal override void OnClick(int clickCount)
	{
		IsChecked = !IsChecked;
		CheckedChanged?.Invoke(this);
	}

	protected internal override bool OnKeyDown(UiKeyEvent key)
	{
		if (key.Key is UiKey.Space or UiKey.Enter)
		{
			OnClick(1);
			return true;
		}

		return false;
	}

	private float BoxSize =>
		Context?.Theme.Fonts.Body.LineHeight ?? 16f;

	private UiFont ResolveFont()
	{
		return Font ??
			Context?.Theme.Fonts.Body ??
			throw new InvalidOperationException(
				"The checkbox is not attached to a UI context.");
	}

	private UiTextLayoutResult EnsureLayout(UiFont font, float width)
	{
		width = MathF.Max(0.001f, width);

		if (_layout is not null &&
			_layoutText == _text &&
			_layoutFont == font &&
			_layoutWidth == width)
		{
			return _layout;
		}

		_layout = UiTextLayout.Create(
			_text,
			font,
			width,
			UiTextWrap.NoWrap,
			UiTextAlignment.Start);

		_layoutText = _text;
		_layoutFont = font;
		_layoutWidth = width;

		return _layout;
	}
}
