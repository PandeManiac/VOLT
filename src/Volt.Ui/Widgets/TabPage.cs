namespace Volt.Ui.Widgets;

/// <summary>
/// One page in a <see cref="TabView"/>.
/// </summary>
public sealed class TabPage
{
	public TabPage(string id, string title, Widget content)
	{
		if (string.IsNullOrWhiteSpace(id))
		{
			throw new ArgumentException(
				"A tab page requires a stable identifier.",
				nameof(id));
		}

		ArgumentNullException.ThrowIfNull(content);

		Id = id;
		Title = title ?? string.Empty;
		Content = content;
	}

	public string Id { get; }

	public string Title { get; set; }

	public Widget Content { get; }

	public bool CanClose { get; set; } = true;

	public bool IsVisible => Content.IsVisible;
}
