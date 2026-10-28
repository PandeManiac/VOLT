namespace Volt.Ui.Input;

/// <summary>
/// Committed Unicode text produced by the platform's text-input
/// system.
/// </summary>
/// <remarks>
/// The payload can contain several code points (a composed character
/// or a paste-sized insertion). It is always committed text, never an
/// IME composition preview. Composition is exposed separately through
/// <see cref="UiContext.SetComposition"/> so a future platform bridge
/// can display it without inserting it into the document.
/// </remarks>
public readonly record struct UiTextEvent(string Text);
