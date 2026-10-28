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

	/// <summary>
	/// Scales the palette and metrics by an interface scale factor
	/// while keeping text rasterization tied to the display scale.
	/// </summary>
	/// <remarks>
	/// A UI scale is not a DPI scale: it makes the whole interface
	/// larger or smaller for reading comfort, including control sizes
	/// and spacing, whereas the display scale already matches physical
	/// pixels. The two multiply.
	/// </remarks>
	public UiTheme WithUiScale(float uiScale)
	{
		if (!float.IsFinite(uiScale) || uiScale <= 0f)
		{
			throw new ArgumentOutOfRangeException(nameof(uiScale));
		}

		UiMetrics m = Metrics;

		return new UiTheme(
			Palette,
			m with
			{
				RowHeight = m.RowHeight * uiScale,
				FieldHeight = m.FieldHeight * uiScale,
				ButtonHeight = m.ButtonHeight * uiScale,
				MenuBarHeight = m.MenuBarHeight * uiScale,
				ToolbarHeight = m.ToolbarHeight * uiScale,
				PanelHeaderHeight = m.PanelHeaderHeight * uiScale,
				TabHeight = m.TabHeight * uiScale,
				StatusBarHeight = m.StatusBarHeight * uiScale,
				ScrollBarThickness = m.ScrollBarThickness * uiScale,
				DockSplitterThickness = m.DockSplitterThickness * uiScale,
				AccentBarThickness = m.AccentBarThickness * uiScale,
				Spacing = m.Spacing * uiScale,
				TightSpacing = m.TightSpacing * uiScale,
				SectionSpacing = m.SectionSpacing * uiScale,
				Padding = m.Padding * uiScale,
				Gutter = m.Gutter * uiScale,
				Indent = m.Indent * uiScale,
				IconSize = m.IconSize * uiScale,
				SmallIconSize = m.SmallIconSize * uiScale,
				LargeIconSize = m.LargeIconSize * uiScale,
				ToolButtonSize = m.ToolButtonSize * uiScale,
				TileWidth = m.TileWidth * uiScale,
				TileHeight = m.TileHeight * uiScale,
				AxisLabelWidth = m.AxisLabelWidth * uiScale,
				MinimumPanelWidth = m.MinimumPanelWidth * uiScale,
				MinimumPanelHeight = m.MinimumPanelHeight * uiScale
			},
			new UiFontSet(DpiScale, uiScale));
	}

	public UiStyle GetButtonStyle(
		UiWidgetState state,
		bool selected = false)
	{
		UiColor background = state switch
		{
			UiWidgetState.Disabled => Palette.Surface,
			UiWidgetState.Pressed => selected
				? Palette.AccentPressed
				: Palette.Pressed,
			UiWidgetState.Hovered => selected
				? Palette.AccentHover
				: Palette.Hover,
			_ => selected ? Palette.AccentSurface : Palette.SurfaceRaised
		};

		return new UiStyle(
			background,
			state == UiWidgetState.Disabled
				? Palette.TextDisabled
				: selected ? Palette.AccentText : Palette.Text,
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
				: Palette.TextOnAccent,
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
			? Palette.FocusRing
			: state == UiWidgetState.Hovered
				? Palette.BorderStrong
				: Palette.Border;

		return new UiStyle(
			state == UiWidgetState.Disabled
				? Palette.Surface
				: Palette.Well,
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

	/// <summary>
	/// Gets the style of the header strip above a panel's content.
	/// </summary>
	public UiStyle GetPanelHeaderStyle()
	{
		return new UiStyle(
			Palette.Header,
			Palette.TextMuted,
			Palette.ChromeBorder,
			Metrics.BorderWidth,
			0f,
			new UiThickness(10f, 0f));
	}

	/// <summary>
	/// Gets the style of a collapsible component section header.
	/// </summary>
	public UiStyle GetSectionHeaderStyle(bool hovered)
	{
		return new UiStyle(
			hovered ? Palette.SurfaceRaised : Palette.Header,
			Palette.Text,
			Palette.ChromeBorder,
			Metrics.BorderWidth,
			0f,
			new UiThickness(8f, 3f));
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
			selected ? Palette.Text : Palette.Text,
			UiColor.Transparent,
			0f,
			Metrics.SmallCornerRadius,
			new UiThickness(4f, 1f));
	}

	public UiStyle GetTabStyle(bool selected, bool hovered)
	{
		return new UiStyle(
			selected
				? Palette.Surface
				: hovered ? Palette.SurfaceRaised : Palette.Header,
			selected ? Palette.Text : Palette.TextMuted,
			UiColor.Transparent,
			0f,
			0f,
			new UiThickness(12f, 4f));
	}

	/// <summary>
	/// Gets the style of a square, icon-only control such as a toolbar
	/// tool or a panel action.
	/// </summary>
	public UiStyle GetIconButtonStyle(
		UiWidgetState state,
		bool active)
	{
		UiColor background = state switch
		{
			UiWidgetState.Disabled => UiColor.Transparent,
			UiWidgetState.Pressed => active
				? Palette.AccentPressed
				: Palette.Pressed,
			UiWidgetState.Hovered => active
				? Palette.AccentHover
				: Palette.Hover,
			_ => active ? Palette.AccentSurface : UiColor.Transparent
		};

		return new UiStyle(
			background,
			state == UiWidgetState.Disabled
				? Palette.TextDisabled
				: active ? Palette.AccentText : Palette.TextMuted,
			active ? Palette.Accent : UiColor.Transparent,
			active ? Metrics.BorderWidth : 0f,
			Metrics.SmallCornerRadius,
			new UiThickness(5f));
	}

	/// <summary>
	/// Gets the style of an icon button that carries a text label.
	/// </summary>
	public UiStyle GetToolButtonStyle(
		UiWidgetState state,
		bool active)
	{
		UiColor background = state switch
		{
			UiWidgetState.Disabled => Palette.Header,
			UiWidgetState.Pressed => active
				? Palette.AccentPressed
				: Palette.Pressed,
			UiWidgetState.Hovered => active
				? Palette.AccentHover
				: Palette.Hover,
			_ => active ? Palette.AccentSurface : Palette.Header
		};

		return new UiStyle(
			background,
			state == UiWidgetState.Disabled
				? Palette.TextDisabled
				: active ? Palette.AccentText : Palette.TextMuted,
			active ? Palette.Accent : UiColor.Transparent,
			active ? Metrics.BorderWidth : 0f,
			Metrics.SmallCornerRadius,
			new UiThickness(8f, 4f));
	}

	public UiStyle GetMenuStyle(bool hovered)
	{
		return new UiStyle(
			hovered ? Palette.Hover : UiColor.Transparent,
			hovered ? Palette.Text : Palette.TextMuted,
			UiColor.Transparent,
			0f,
			Metrics.SmallCornerRadius,
			new UiThickness(9f, 3f));
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
			Palette.Chrome,
			Palette.Text,
			Palette.ChromeBorder,
			Metrics.BorderWidth,
			0f,
			new UiThickness(8f, 4f));
	}

	public UiStyle GetStatusBarStyle()
	{
		return new UiStyle(
			Palette.Chrome,
			Palette.TextMuted,
			Palette.ChromeBorder,
			Metrics.BorderWidth,
			0f,
			new UiThickness(10f, 0f));
	}

	/// <summary>
	/// Gets the style of a floating control cluster over a viewport.
	/// </summary>
	public UiStyle GetOverlayStyle()
	{
		return new UiStyle(
			Palette.Overlay,
			Palette.Text,
			Palette.OverlayBorder,
			Metrics.BorderWidth,
			Metrics.SmallCornerRadius,
			new UiThickness(4f));
	}

	/// <summary>
	/// Gets the style of a project browser tile.
	/// </summary>
	public UiStyle GetTileStyle(bool selected, bool hovered)
	{
		return new UiStyle(
			selected
				? Palette.Selection
				: hovered ? Palette.TileHover : Palette.Tile,
			selected ? Palette.Text : Palette.Text,
			selected ? Palette.Accent : Palette.ChromeBorder,
			Metrics.BorderWidth,
			Metrics.SmallCornerRadius,
			new UiThickness(4f));
	}

	public UiStyle GetScrollThumbStyle(bool hovered)
	{
		return new UiStyle(
			hovered ? Palette.ScrollThumbHover : Palette.ScrollThumb,
			Palette.Text,
			UiColor.Transparent,
			0f,
			Metrics.CornerRadius,
			default);
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

	/// <summary>
	/// Gets a filled badge style in a semantic tint.
	/// </summary>
	public UiStyle GetBadgeStyle(UiColor tint)
	{
		return new UiStyle(
			tint.ScaleAlpha(0.18f),
			tint,
			tint.ScaleAlpha(0.45f),
			Metrics.BorderWidth,
			Metrics.SmallCornerRadius,
			new UiThickness(6f, 1f));
	}
}
