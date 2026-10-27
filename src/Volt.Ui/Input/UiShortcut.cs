namespace Volt.Ui.Input;

/// <summary>
/// A keyboard shortcut that runs after focused-widget handling.
/// </summary>
/// <remarks>
/// Focused text controls get the first chance to handle a key, so
/// Select All and Copy keep their local meaning. A shortcut is only
/// considered when the focused widget did not consume the key, which
/// lets global commands such as Save work from anywhere.
/// </remarks>
public sealed class UiShortcut
{
	public UiShortcut(
		UiKey key,
		UiKeyModifiers modifiers,
		Action action,
		Func<bool>? canInvoke = null)
	{
		ArgumentNullException.ThrowIfNull(action);

		if (key == UiKey.None)
		{
			throw new ArgumentException(
				"A shortcut requires a key.",
				nameof(key));
		}

		Key = key;
		Modifiers = modifiers;
		Action = action;
		CanInvoke = canInvoke;
	}

	public UiKey Key { get; }

	public UiKeyModifiers Modifiers { get; }

	public Action Action { get; }

	public Func<bool>? CanInvoke { get; }

	internal bool Matches(in UiKeyEvent keyEvent)
	{
		return keyEvent.Key == Key &&
			keyEvent.Modifiers == Modifiers &&
			(CanInvoke?.Invoke() ?? true);
	}
}
