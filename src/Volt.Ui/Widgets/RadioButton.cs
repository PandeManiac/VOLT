using Volt.Ui.Input;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Text;

namespace Volt.Ui.Widgets;

/// <summary>
/// A mutually exclusive radio option.
/// </summary>
public class RadioButton : Widget
{
	private string _text = string.Empty;
	private bool _isChecked;
	private UiTextLayoutResult? _layout;
	private string? _layoutText;
	private UiFont? _layoutFont;
	private float _layoutWidth = -1f;

	public RadioButton()
	{
		IsFocusable = true;
		Cursor = UiCursor.Hand;
		MinHeight = 20f;
	}

	public RadioButton(string text)
		: this()
	{
		Text = text;
	}

	public event Action<RadioButton>? Checked;

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

	public string GroupName { get; set; } = string.Empty;

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
		UiColor border = _isChecked
			? theme.Palette.Accent
			: state == UiWidgetState.Hovered
				? theme.Palette.BorderStrong
				: theme.Palette.Border;

		drawList.AddRoundedRect(
			boxRect,
			box * 0.5f,
			theme.Palette.WindowBackground);

		drawList.AddBorder(
			boxRect,
			border,
			1.5f,
			box * 0.5f);

		if (_isChecked)
		{
			float inset = box * 0.28f;

			drawList.AddRoundedRect(
				boxRect.Inset(inset),
				(box - (inset * 2f)) * 0.5f,
				theme.Palette.Accent);
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
		Select();

		if (!_isChecked)
		{
			IsChecked = true;
			Checked?.Invoke(this);
		}

		else
		{
			Checked?.Invoke(this);
		}
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

	private void Select()
	{
		if (Parent is null || GroupName.Length == 0)
		{
			return;
		}

		for (int index = 0; index < Parent.Children.Count; index++)
		{
			Widget child = Parent.Children[index];

			if (child is RadioButton radio &&
				radio != this &&
				radio.GroupName == GroupName)
			{
				radio.IsChecked = false;
			}
		}
	}

	private float BoxSize =>
		Context?.Theme.Fonts.Body.LineHeight ?? 16f;

	private UiFont ResolveFont()
	{
		return Font ??
			Context?.Theme.Fonts.Body ??
			throw new InvalidOperationException(
				"The radio button is not attached to a UI context.");
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
