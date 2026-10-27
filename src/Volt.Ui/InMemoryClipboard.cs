namespace Volt.Ui;

/// <summary>
/// A process-local clipboard used when the host supplies none.
/// </summary>
public sealed class InMemoryClipboard : IUiClipboard
{
	private string _text = string.Empty;

	public string? GetText()
	{
		return _text;
	}

	public void SetText(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		_text = text;
	}
}
