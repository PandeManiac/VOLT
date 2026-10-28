using Volt.Ui.Widgets;

namespace Volt.Ui.Input;

/// <summary>
/// A pointer event delivered to a widget.
/// </summary>
/// <param name="Position">
/// Position in the receiving widget's local coordinates, with the
/// origin at its outer bounds.
/// </param>
/// <param name="GlobalPosition">Position in root UI coordinates.</param>
/// <param name="Delta">Movement since the previous frame.</param>
/// <param name="Button">The button involved, if any.</param>
/// <param name="Modifiers">Held modifier keys.</param>
/// <param name="ClickCount">
/// Two for a double click, three for a triple click, otherwise one.
/// </param>
public readonly record struct UiPointerEvent(
	UiPoint Position,
	UiPoint GlobalPosition,
	UiPoint Delta,
	UiMouseButton Button,
	UiKeyModifiers Modifiers,
	int ClickCount)
{
	internal UiPointerEvent ToLocal(Widget widget)
	{
		UiRect bounds = widget.GlobalBounds;

		return this with
		{
			Position = new UiPoint(
				GlobalPosition.X - bounds.X,
				GlobalPosition.Y - bounds.Y)
		};
	}
}
