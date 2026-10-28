namespace Volt.Ui.Styling;

/// <summary>
/// Layout constants shared by the widgets and editor chrome.
/// </summary>
/// <remarks>
/// Metrics are the spacing and sizing half of the design system. A
/// widget reads its padding, row height, and icon size from here
/// instead of hardcoding values, so the whole interface tightens or
/// loosens from one place. Sizes are logical units; the font set
/// applies the display scale to text independently.
/// </remarks>
public sealed record UiMetrics
{
	// Control sizing.

	public float RowHeight { get; init; } = 24f;

	public float FieldHeight { get; init; } = 26f;

	public float ButtonHeight { get; init; } = 26f;

	public float MenuBarHeight { get; init; } = 34f;

	public float ToolbarHeight { get; init; } = 42f;

	public float PanelHeaderHeight { get; init; } = 30f;

	public float TabHeight { get; init; } = 32f;

	public float StatusBarHeight { get; init; } = 26f;

	public float ScrollBarThickness { get; init; } = 12f;

	public float DockSplitterThickness { get; init; } = 5f;

	public float DividerThickness { get; init; } = 1f;

	// Shape.

	public float CornerRadius { get; init; } = 6f;

	public float SmallCornerRadius { get; init; } = 4f;

	public float BorderWidth { get; init; } = 1f;

	/// <summary>
	/// Gets the width of the accent bar that marks an active tab.
	/// </summary>
	public float AccentBarThickness { get; init; } = 2f;

	// Spacing.

	public float Spacing { get; init; } = 8f;

	public float TightSpacing { get; init; } = 4f;

	public float SectionSpacing { get; init; } = 10f;

	public float Padding { get; init; } = 10f;

	public float Gutter { get; init; } = 6f;

	public float Indent { get; init; } = 16f;

	// Icons and tiles.

	public float IconSize { get; init; } = 16f;

	public float SmallIconSize { get; init; } = 12f;

	public float LargeIconSize { get; init; } = 20f;

	/// <summary>
	/// Gets the edge of a square toolbar icon button.
	/// </summary>
	public float ToolButtonSize { get; init; } = 30f;

	public float TileWidth { get; init; } = 86f;

	public float TileHeight { get; init; } = 90f;

	public float AxisLabelWidth { get; init; } = 14f;

	// Panels.

	public float MinimumPanelWidth { get; init; } = 140f;

	public float MinimumPanelHeight { get; init; } = 90f;

	public static UiMetrics Default => new();
}
