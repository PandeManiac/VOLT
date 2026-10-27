namespace Volt.Ui.Styling;

/// <summary>
/// The resolved visual properties of one widget state.
/// </summary>
public readonly record struct UiStyle(
	UiColor Background,
	UiColor Foreground,
	UiColor Border,
	float BorderWidth,
	float CornerRadius,
	UiThickness Padding)
{
	public static UiStyle Empty => default;
}
