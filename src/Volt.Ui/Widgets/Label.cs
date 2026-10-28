using Volt.Ui.Rendering;
using Volt.Ui.Text;

namespace Volt.Ui.Widgets;

/// <summary>
/// Displays static, optionally wrapped text.
/// </summary>
/// <remarks>
/// A label caches its layout result until its text, font, wrap mode,
/// alignment, or available width changes. Painting therefore performs
/// no shaping or measurement work on idle frames.
/// </remarks>
public class Label : Widget
{
	private string _text = string.Empty;
	private UiFont? _font;
	private UiTextWrap _wrap;
	private UiTextAlignment _textAlignment;
	private UiTextVerticalAlignment _verticalTextAlignment =
		UiTextVerticalAlignment.Center;

	private UiTextLayoutResult? _layout;
	private string? _layoutText;
	private UiFont? _layoutFont;
	private float _layoutWidth = -1f;
	private UiTextWrap _layoutWrap;
	private UiTextAlignment _layoutAlignment;

	public Label()
	{
		ClipToBounds = true;
	}

	public Label(string text)
		: this()
	{
		Text = text;
	}

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
			InvalidateLayout();
		}
	}

	/// <summary>
	/// Gets or sets an explicit font. When null, the theme body font
	/// is used.
	/// </summary>
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
			InvalidateLayout();
		}
	}

	public UiTextWrap Wrap
	{
		get => _wrap;
		set
		{
			if (_wrap == value)
			{
				return;
			}

			_wrap = value;
			InvalidateLayout();
		}
	}

	public UiTextAlignment TextAlignment
	{
		get => _textAlignment;
		set
		{
			if (_textAlignment == value)
			{
				return;
			}

			_textAlignment = value;
			InvalidateLayout();
		}
	}

	public UiTextVerticalAlignment VerticalTextAlignment
	{
		get => _verticalTextAlignment;
		set => _verticalTextAlignment = value;
	}

	public UiColor? TextColor { get; set; }

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		UiTextLayoutResult layout = EnsureLayout(
			contentAvailable.Width);

		float width = _wrap == UiTextWrap.Wrap
			? MathF.Min(layout.Width, contentAvailable.Width)
			: layout.Width;

		return new UiSize(width, layout.Height);
	}

	protected override void RenderSelf(UiDrawList drawList)
	{
		if (_text.Length == 0)
		{
			return;
		}

		// The draw list is in root coordinates, so text is placed with
		// the global content rectangle, not the widget-local one.
		UiRect content = GlobalContentBounds;

		UiTextLayoutResult layout = EnsureLayout(content.Width);
		float y = _verticalTextAlignment switch
		{
			UiTextVerticalAlignment.Center => content.Y +
				MathF.Max(0f, (content.Height - layout.Height) * 0.5f),
			UiTextVerticalAlignment.End => content.Y +
				MathF.Max(0f, content.Height - layout.Height),
			_ => content.Y
		};

		drawList.AddText(
			layout,
			new UiPoint(content.X, y),
			TextColor ?? Context?.Theme.Palette.Text ?? UiColor.White);
	}

	private void InvalidateLayout()
	{
		_layout = null;
		InvalidateMeasure();
	}

	private UiTextLayoutResult EnsureLayout(float availableWidth)
	{
		UiFont font = _font ??
			Context?.Theme.Fonts.Body ??
			throw new InvalidOperationException(
				"The label is not attached to a UI context.");

		availableWidth = float.IsFinite(availableWidth)
			? MathF.Max(0f, availableWidth)
			: float.PositiveInfinity;

		if (_layout is not null &&
			_layoutText == _text &&
			_layoutFont == font &&
			_layoutWidth == availableWidth &&
			_layoutWrap == _wrap &&
			_layoutAlignment == _textAlignment)
		{
			return _layout;
		}

		float layoutWidth = availableWidth;

		if (_wrap == UiTextWrap.NoWrap)
		{
			if (!float.IsFinite(layoutWidth) ||
				layoutWidth <= 0f)
			{
				layoutWidth = MathF.Max(
					font.MeasureText(_text),
					0.001f);
			}
		}

		if (!float.IsFinite(layoutWidth))
		{
			layoutWidth = MathF.Max(font.MeasureText(_text), 0.001f);
		}

		_layout = UiTextLayout.Create(
			_text,
			font,
			layoutWidth,
			_wrap,
			_textAlignment);

		_layoutText = _text;
		_layoutFont = font;
		_layoutWidth = availableWidth;
		_layoutWrap = _wrap;
		_layoutAlignment = _textAlignment;

		return _layout;
	}
}
