using Volt.Ui.Text;

namespace Volt.Ui.Styling;

/// <summary>
/// A complete, replaceable description of how VOLT UI looks.
/// </summary>
/// <remarks>
/// Widgets resolve their colors, fonts, and metrics from the theme
/// active in their <see cref="UiContext"/>. They never hardcode a
/// palette, so an editor or game can supply its own theme without
/// changing widget implementations.
/// </remarks>
public sealed class UiTheme
{
	public UiTheme(
		UiPalette palette,
		UiMetrics metrics,
		UiFontSet fonts)
	{
		ArgumentNullException.ThrowIfNull(palette);
		ArgumentNullException.ThrowIfNull(metrics);
		ArgumentNullException.ThrowIfNull(fonts);

		Palette = palette;
		Metrics = metrics;
		Fonts = fonts;
	}

	public UiPalette Palette { get; }

	public UiMetrics Metrics { get; }

	public UiFontSet Fonts { get; }

	public float DpiScale => Fonts.DpiScale;

	/// <summary>
	/// Creates the default dark VOLT theme.
	/// </summary>
	public static UiTheme CreateDark(float dpiScale = 1f)
	{
		return new UiTheme(
			UiPalette.Dark,
			UiMetrics.Default,
			new UiFontSet(dpiScale));
	}

	/// <summary>
	/// Creates the same palette and metrics at another display scale.
	/// </summary>
	public UiTheme WithDpiScale(float dpiScale)
	{
		return new UiTheme(
			Palette,
			Metrics,
			new UiFontSet(dpiScale));
	}

	public UiStyle GetButtonStyle(
		UiWidgetState state,
		bool selected = false)
	{
		UiColor background = state switch
		{
			UiWidgetState.Hovered => Palette.Hover,
			UiWidgetState.Pressed => Palette.Pressed,
			UiWidgetState.Disabled => Palette.Surface,
			_ => selected ? Palette.Selection : Palette.SurfaceRaised
		};

		if (selected && state == UiWidgetState.Hovered)
		{
			background = Palette.Selection;
		}

		return new UiStyle(
			background,
			state == UiWidgetState.Disabled
				? Palette.TextDisabled
				: selected ? Palette.Accent : Palette.Text,
			selected ? Palette.Accent : Palette.Border,
			Metrics.BorderWidth,
			Metrics.SmallCornerRadius,
			new UiThickness(10f, 4f));
	}

	public UiStyle GetPrimaryButtonStyle(UiWidgetState state)
	{
		UiColor background = state switch
		{
			UiWidgetState.Hovered => Palette.AccentHover,
			UiWidgetState.Pressed => Palette.AccentPressed,
			UiWidgetState.Disabled => Palette.SurfaceRaised,
			_ => Palette.Accent
		};

		return new UiStyle(
			background,
			state == UiWidgetState.Disabled
				? Palette.TextDisabled
				: Palette.AccentText,
			UiColor.Transparent,
			0f,
			Metrics.SmallCornerRadius,
			new UiThickness(12f, 4f));
	}

	public UiStyle GetTextFieldStyle(
		UiWidgetState state,
		bool focused)
	{
		UiColor border = focused
			? Palette.Accent
			: state == UiWidgetState.Hovered
				? Palette.BorderStrong
				: Palette.Border;

		return new UiStyle(
			state == UiWidgetState.Disabled
				? Palette.Surface
				: Palette.WindowBackground,
			state == UiWidgetState.Disabled
				? Palette.TextDisabled
				: Palette.Text,
			border,
			Metrics.BorderWidth,
			Metrics.SmallCornerRadius,
			new UiThickness(7f, 3f));
	}

	public UiStyle GetPanelStyle()
	{
		return new UiStyle(
			Palette.Surface,
			Palette.Text,
			Palette.Border,
			Metrics.BorderWidth,
			0f,
			default);
	}

	public UiStyle GetRowStyle(bool selected, bool hovered)
	{
		UiColor background = selected
			? Palette.Selection
			: hovered
				? Palette.Hover
				: UiColor.Transparent;

		return new UiStyle(
			background,
			selected ? Palette.Accent : Palette.Text,
			UiColor.Transparent,
			0f,
			Metrics.SmallCornerRadius,
			new UiThickness(4f, 1f));
	}

	public UiStyle GetTabStyle(bool selected, bool hovered)
	{
		return new UiStyle(
			selected
				? Palette.SurfaceRaised
				: hovered ? Palette.Hover : Palette.Header,
			selected ? Palette.Text : Palette.TextMuted,
			UiColor.Transparent,
			0f,
			0f,
			new UiThickness(10f, 4f));
	}

	public UiStyle GetMenuStyle(bool hovered)
	{
		return new UiStyle(
			hovered ? Palette.Hover : UiColor.Transparent,
			Palette.Text,
			UiColor.Transparent,
			0f,
			Metrics.SmallCornerRadius,
			new UiThickness(7f, 3f));
	}

	public UiStyle GetPopupStyle()
	{
		return new UiStyle(
			Palette.SurfaceOverlay,
			Palette.Text,
			Palette.BorderStrong,
			Metrics.BorderWidth,
			Metrics.CornerRadius,
			new UiThickness(4f, 4f));
	}

	public UiStyle GetToolbarStyle()
	{
		return new UiStyle(
			Palette.Header,
			Palette.Text,
			Palette.Border,
			Metrics.BorderWidth,
			0f,
			new UiThickness(8f, 4f));
	}

	public UiStyle GetTooltipStyle()
	{
		return new UiStyle(
			Palette.SurfaceOverlay,
			Palette.Text,
			Palette.BorderStrong,
			Metrics.BorderWidth,
			Metrics.SmallCornerRadius,
			new UiThickness(8f, 4f));
	}
}
