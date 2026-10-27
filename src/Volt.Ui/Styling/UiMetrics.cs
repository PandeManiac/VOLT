namespace Volt.Ui.Styling;

/// <summary>
/// Layout constants shared by the widgets and editor chrome.
/// </summary>
public sealed record UiMetrics
{
	public float RowHeight { get; init; } = 22f;

	public float FieldHeight { get; init; } = 24f;

	public float ButtonHeight { get; init; } = 24f;

	public float MenuBarHeight { get; init; } = 28f;

	public float ToolbarHeight { get; init; } = 34f;

	public float PanelHeaderHeight { get; init; } = 28f;

	public float TabHeight { get; init; } = 30f;

	public float StatusBarHeight { get; init; } = 24f;

	public float ScrollBarThickness { get; init; } = 10f;

	public float DockSplitterThickness { get; init; } = 4f;

	public float DividerThickness { get; init; } = 1f;

	public float CornerRadius { get; init; } = 4f;

	public float SmallCornerRadius { get; init; } = 3f;

	public float BorderWidth { get; init; } = 1f;

	public float Spacing { get; init; } = 6f;

	public float TightSpacing { get; init; } = 3f;

	public float Padding { get; init; } = 8f;

	public float Indent { get; init; } = 14f;

	public float IconSize { get; init; } = 16f;

	public float MinimumPanelWidth { get; init; } = 120f;

	public float MinimumPanelHeight { get; init; } = 80f;

	public static UiMetrics Default => new();
}
