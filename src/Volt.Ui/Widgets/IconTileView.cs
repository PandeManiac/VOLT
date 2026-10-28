using Volt.Ui.Input;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Text;

namespace Volt.Ui.Widgets;

/// <summary>
/// A fixed-size tile presenting an asset or folder.
/// </summary>
/// <remarks>
/// Tiles are the unit of the project browser's grid view. The icon is
/// tinted per asset kind and the caption is elided to the tile width,
/// so a directory with hundreds of assets stays scannable. Activation
/// is a double click or Enter, matching file browsers; a single click
/// selects.
/// </remarks>
public sealed class IconTileView : Widget
{
	private string _text = string.Empty;
	private string? _detail;
	private UiColor? _iconColor;
	private UiTextLayoutResult? _layout;
	private string? _layoutText;
	private UiFont? _layoutFont;
	private float _layoutWidth = -1f;

	public IconTileView()
	{
		IsFocusable = true;
		FocusOnPointerPress = true;
		Cursor = UiCursor.Hand;
		MinWidth = 48f;
		MinHeight = 48f;
	}

	public IconTileView(
		UiIcon icon,
		string text,
		object? tag = null)
		: this()
	{
		Icon = icon;
		Text = text;
		Tag = tag;
	}

	public event Action<IconTileView>? Clicked;

	public event Action<IconTileView>? Activated;

	public UiIcon Icon { get; set; }

	public object? Tag { get; set; }

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

	/// <summary>
	/// Gets or sets a secondary caption such as an asset kind.
	/// </summary>
	public string? Detail
	{
		get => _detail;
		set => _detail = value;
	}

	public UiColor? IconColor
	{
		get => _iconColor;
		set => _iconColor = value;
	}

	public bool IsSelected { get; set; }

	public bool IsOpen { get; set; }

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();

		return new UiSize(
			MathF.Min(theme.Metrics.TileWidth, contentAvailable.Width),
			MathF.Min(theme.Metrics.TileHeight, contentAvailable.Height));
	}

	protected override void RenderSelf(UiDrawList drawList)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		UiStyle style = theme.GetTileStyle(IsSelected, IsHovered);
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
				IsSelected ? 1.5f : style.BorderWidth,
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

		float iconSize = MathF.Min(
			theme.Metrics.LargeIconSize * 2f,
			bounds.Height * 0.5f);

		float iconTop = bounds.Y + 10f;

		drawList.AddRoundedRect(
			new UiRect(
				bounds.Center.X - (iconSize * 0.5f),
				iconTop,
				iconSize,
				iconSize),
			theme.Metrics.SmallCornerRadius,
			theme.Palette.Well);

		UiIconPainter.Draw(
			drawList,
			Icon,
			new UiRect(
				bounds.Center.X - (iconSize * 0.42f),
				iconTop + (iconSize * 0.08f),
				iconSize * 0.84f,
				iconSize * 0.84f),
			_iconColor ?? theme.Palette.TextMuted,
			1.5f);

		UiFont font = theme.Fonts.Small;
		UiTextLayoutResult layout = EnsureLayout(font, bounds.Width - 10f);
		float textY = iconTop + iconSize + 5f;
		float textX = bounds.X +
			MathF.Max(5f, (bounds.Width - layout.Width) * 0.5f);

		drawList.PushClip(bounds);

		try
		{
			drawList.AddText(
				layout,
				new UiPoint(textX, textY),
				IsSelected ? theme.Palette.Text : theme.Palette.TextMuted);

			if (_detail is { Length: > 0 })
			{
				drawList.AddText(
					UiTextLayout.Create(
						_detail,
						theme.Fonts.MonoSmall,
						MathF.Max(1f, bounds.Width - 10f),
						UiTextWrap.NoWrap,
						UiTextAlignment.Center),
					new UiPoint(bounds.X + 5f, textY + layout.Height + 1f),
					theme.Palette.TextDisabled);
			}
		}

		finally
		{
			drawList.PopClip();
		}
	}

	protected internal override void OnClick(int clickCount)
	{
		base.OnClick(clickCount);

		Clicked?.Invoke(this);

		if (clickCount >= 2)
		{
			Activated?.Invoke(this);
		}
	}

	protected internal override bool OnKeyDown(UiKeyEvent key)
	{
		if (key.Key is UiKey.Enter or UiKey.Space)
		{
			Activated?.Invoke(this);
			return true;
		}

		return false;
	}

	private UiTextLayoutResult EnsureLayout(UiFont font, float width)
	{
		width = MathF.Max(1f, width);

		if (_layout is not null &&
			_layoutText == _text &&
			_layoutFont == font &&
			MathF.Abs(_layoutWidth - width) < 0.01f)
		{
			return _layout;
		}

		_layout = UiTextLayout.Create(
			_text,
			font,
			width,
			UiTextWrap.NoWrap,
			UiTextAlignment.Center);

		_layoutText = _text;
		_layoutFont = font;
		_layoutWidth = width;

		return _layout;
	}
}
