namespace Volt.Ui.Input;

/// <summary>
/// Modifier keys held during an input event.
/// </summary>
[Flags]
public enum UiKeyModifiers
{
	None = 0,
	Shift = 1 << 0,
	Control = 1 << 1,
	Alt = 1 << 2,
	Super = 1 << 3
}
