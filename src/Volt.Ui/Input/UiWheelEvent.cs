namespace Volt.Ui.Input;

/// <summary>
/// A mouse-wheel event in notches, with positive Y meaning up.
/// </summary>
public readonly record struct UiWheelEvent(
	UiPoint Position,
	UiPoint Delta,
	UiKeyModifiers Modifiers);
