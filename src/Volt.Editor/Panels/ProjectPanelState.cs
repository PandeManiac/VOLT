namespace Volt.Editor.Panels;

/// <summary>
/// View state the project browser keeps across an interface-scale
/// rebuild, when the panel widget itself is recreated.
/// </summary>
internal sealed class ProjectPanelState
{
	public Guid? ProjectId { get; init; }

	public string Folder { get; init; } = string.Empty;

	public bool FolderSelectionActive { get; init; }

	public bool GridMode { get; init; } = true;

	public string SelectedId { get; init; } = string.Empty;

	public bool SelectedIsFolder { get; init; }

	public IReadOnlyList<string> SelectedAssetIds { get; init; } = [];

	public string Search { get; init; } = string.Empty;

	public IReadOnlyList<string> CollapsedFolders { get; init; } = [];
}
