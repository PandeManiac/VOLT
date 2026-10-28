using Volt.Ui.Widgets;

namespace Volt.Ui.Styling;

/// <summary>
/// Resolves the interaction state used for style lookup.
/// </summary>
public static class WidgetStateExtensions
{
	public static UiWidgetState GetVisualState(this Widget widget)
	{
		ArgumentNullException.ThrowIfNull(widget);

		if (!widget.IsEnabled)
		{
			return UiWidgetState.Disabled;
		}

		if (widget.IsPressed)
		{
			return UiWidgetState.Pressed;
		}

		return widget.IsHovered
			? UiWidgetState.Hovered
			: UiWidgetState.Normal;
	}
}
