namespace Volt.Ui.Docking;

/// <summary>
/// Persistent identity and title of a dockable panel.
/// </summary>
public sealed class DockPanel
{
	public DockPanel(string id, string title)
	{
		if (string.IsNullOrWhiteSpace(id))
		{
			throw new ArgumentException(
				"A dock panel requires a stable identifier.",
				nameof(id));
		}

		Id = id;
		Title = title ?? string.Empty;
	}

	public string Id { get; }

	public string Title { get; set; }

	/// <summary>
	/// Gets whether the panel currently appears in a tab group.
	/// </summary>
	public bool IsOpen { get; internal set; }
}
