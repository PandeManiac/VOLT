namespace Volt.Ui;

/// <summary>
/// Text clipboard used by editable controls.
/// </summary>
/// <remarks>
/// The host application supplies platform integration. Volt.Ui's
/// default implementation is process-local, which makes headless
/// tests and samples deterministic.
/// </remarks>
public interface IUiClipboard
{
	string? GetText();

	void SetText(string text);
}
