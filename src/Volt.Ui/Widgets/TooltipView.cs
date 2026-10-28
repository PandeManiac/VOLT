using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Text;

namespace Volt.Ui.Widgets;

/// <summary>
/// A small non-interactive tooltip presented by the UI context.
/// </summary>
public sealed class TooltipView : Widget
{
	private UiTextLayoutResult? _layout;
	private string? _layoutText;

	public TooltipView()
	{
		IsHitTestVisible = false;
		ClipToBounds = true;
		Padding = new UiThickness(8f, 4f);
	}

	public string Text { get; set; } = string.Empty;

	public UiColor? TextColor { get; set; }

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		UiFont font = Context?.Theme.Fonts.Small ??
			throw new InvalidOperationException(
				"The tooltip is not attached to a UI context.");

		if (_layout is null || _layoutText != Text)
		{
			_layout = UiTextLayout.Create(
				Text,
				font,
				320f,
				UiTextWrap.Wrap,
				UiTextAlignment.Start);
			_layoutText = Text;
		}

		return new UiSize(
			MathF.Min(320f, _layout.Width),
			_layout.Height);
	}

	protected override void RenderSelf(UiDrawList drawList)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		UiStyle style = theme.GetTooltipStyle();

		drawList.AddRoundedRect(
			GlobalVisualBounds,
			style.CornerRadius,
			style.Background);

		drawList.AddBorder(
			GlobalVisualBounds,
			style.Border,
			style.BorderWidth,
			style.CornerRadius);

		UiFont font = theme.Fonts.Small;

		if (_layout is null || _layoutText != Text)
		{
			_layout = UiTextLayout.Create(
				Text,
				font,
				320f,
				UiTextWrap.Wrap,
				UiTextAlignment.Start);
			_layoutText = Text;
		}

		// The draw list is in root coordinates, so the text is placed
		// with the global content rectangle.
		UiRect content = GlobalContentBounds;

		drawList.AddText(
			_layout,
			new UiPoint(content.X, content.Y),
			TextColor ?? theme.Palette.Text);
	}
}
