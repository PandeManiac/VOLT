namespace Volt.Ui.Input;

/// <summary>
/// A physical key press or release.
/// </summary>
public readonly record struct UiKeyEvent(
	UiKey Key,
	bool IsDown,
	bool IsRepeat,
	UiKeyModifiers Modifiers)
{
	public bool HasModifier(UiKeyModifiers modifier)
	{
		return (Modifiers & modifier) == modifier;
	}
}
