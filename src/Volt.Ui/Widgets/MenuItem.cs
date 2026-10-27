namespace Volt.Ui.Widgets;

/// <summary>
/// A menu entry or separator.
/// </summary>
/// <remarks>
/// Submenus are represented in the model but the current popup
/// renders one level. The editor uses flat menus; nested menus are a
/// documented future extension.
/// </remarks>
public sealed class MenuItem
{
	public MenuItem(string label, Action? action = null)
	{
		Label = label ?? throw new ArgumentNullException(nameof(label));
		Action = action;
	}

	public string Label { get; }

	public string Shortcut { get; set; } = string.Empty;

	public Action? Action { get; set; }

	public bool IsEnabled { get; set; } = true;

	public bool IsChecked { get; set; }

	public bool IsSeparator { get; init; }

	public List<MenuItem> Children { get; } = [];

	public static MenuItem Separator => new(string.Empty)
	{
		IsSeparator = true
	};

	public MenuItem AddChild(MenuItem child)
	{
		ArgumentNullException.ThrowIfNull(child);
		Children.Add(child);

		return this;
	}
}
