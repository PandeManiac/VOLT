using Volt.Ui.Layout;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Text;

namespace Volt.Ui.Widgets;

/// <summary>
/// A deliberate empty state for a panel or viewport with no content.
/// </summary>
/// <remarks>
/// Panels that can legitimately have nothing to show use this instead
/// of leaving a blank rectangle. The icon, title, and description are
/// centered and clipped, and the whole view ignores input so it never
/// steals a click from the panel that owns it.
/// </remarks>
public sealed class EmptyStateView : Widget
{
	private readonly IconView _icon;

	private UiTextLayoutResult? _titleLayout;
	private UiTextLayoutResult? _descriptionLayout;
	private string? _layoutTitle;
	private string? _layoutDescription;
	private UiFont? _layoutTitleFont;
	private UiFont? _layoutDescriptionFont;
	private float _layoutWidth = -1f;

	public EmptyStateView()
	{
		IsHitTestVisible = false;
		ClipToBounds = true;

		_icon = new IconView
		{
			HorizontalAlignment = UiAlignment.Center,
			Width = UiLength.Points(40f),
			Height = UiLength.Points(40f),
			IconPadding = 2f
		};

		Add(_icon);
	}

	public EmptyStateView(
		UiIcon icon,
		string title,
		string description)
		: this()
	{
		Icon = icon;
		Title = title;
		Description = description;
	}

	public UiIcon Icon
	{
		get => _icon.Icon;
		set => _icon.Icon = value;
	}

	public string Title { get; set; } = string.Empty;

	public string Description { get; set; } = string.Empty;

	public UiColor? IconColor
	{
		get => _icon.Color;
		set => _icon.Color = value;
	}

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		float width = float.IsFinite(contentAvailable.Width)
			? MathF.Min(contentAvailable.Width, 360f)
			: 240f;

		UiTextLayoutResult title = EnsureTitle(theme.Fonts.Large, width);
		UiTextLayoutResult description = EnsureDescription(
			theme.Fonts.Small,
			width);

		return new UiSize(
			float.IsFinite(contentAvailable.Width)
				? contentAvailable.Width
				: MathF.Max(title.Width, description.Width),
			MathF.Min(
				contentAvailable.Height,
				40f + 10f + title.Height + 4f + description.Height));
	}

	protected override void ArrangeContent(UiRect contentBounds)
	{
		_icon.Width = UiLength.Points(40f);
		_icon.Height = UiLength.Points(40f);

		ArrangeChild(
			_icon,
			new UiRect(
				contentBounds.X,
				contentBounds.Y + 2f,
				contentBounds.Width,
				40f));
	}

	protected override void RenderSelf(UiDrawList drawList)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		UiRect bounds = GlobalVisualBounds;

		if (bounds.Width < 40f || bounds.Height < 40f)
		{
			return;
		}

		float width = MathF.Min(bounds.Width, 360f);
		UiTextLayoutResult title = EnsureTitle(theme.Fonts.Large, width);
		UiTextLayoutResult description = EnsureDescription(
			theme.Fonts.Small,
			width);

		float total = 40f + 10f + title.Height +
			(description.Height > 0f ? 4f + description.Height : 0f);

		float top = bounds.Y + MathF.Max(0f, (bounds.Height - total) * 0.5f);

		UiIconPainter.Draw(
			drawList,
			Icon,
			new UiRect(
				bounds.Center.X - 20f,
				top,
				40f,
				40f),
			IconColor ?? theme.Palette.TextDisabled,
			1.5f);

		float titleX = bounds.X +
			MathF.Max(0f, (bounds.Width - title.Width) * 0.5f);

		drawList.AddText(
			title,
			new UiPoint(titleX, top + 50f),
			theme.Palette.TextMuted);

		if (description.Height <= 0f)
		{
			return;
		}

		float descriptionX = bounds.X +
			MathF.Max(0f, (bounds.Width - description.Width) * 0.5f);

		drawList.AddText(
			description,
			new UiPoint(descriptionX, top + 50f + title.Height + 4f),
			theme.Palette.TextDisabled);
	}

	private UiTextLayoutResult EnsureTitle(UiFont font, float width)
	{
		Invalidate(font, width, Title, Description);

		if (_titleLayout is null)
		{
			_titleLayout = UiTextLayout.Create(
				Title,
				font,
				MathF.Max(1f, width),
				UiTextWrap.Wrap,
				UiTextAlignment.Center);
		}

		return _titleLayout;
	}

	private UiTextLayoutResult EnsureDescription(UiFont font, float width)
	{
		Invalidate(font, width, Title, Description);

		if (_descriptionLayout is null)
		{
			_descriptionLayout = UiTextLayout.Create(
				Description,
				font,
				MathF.Max(1f, width),
				UiTextWrap.Wrap,
				UiTextAlignment.Center);
		}

		return _descriptionLayout;
	}

	private void Invalidate(
		UiFont font,
		float width,
		string title,
		string description)
	{
		if (_titleLayout is not null &&
			_layoutTitle == title &&
			_layoutDescription == description &&
			_layoutTitleFont == font &&
			MathF.Abs(_layoutWidth - width) < 0.01f)
		{
			return;
		}

		_titleLayout = null;
		_descriptionLayout = null;
		_layoutTitle = title;
		_layoutDescription = description;
		_layoutTitleFont = font;
		_layoutDescriptionFont = font;
		_layoutWidth = width;
	}
}
