namespace Volt.Editor.Panels;

/// <summary>
/// View state the hierarchy keeps across an interface-scale rebuild,
/// when the panel widget itself is recreated.
/// </summary>
/// <remarks>
/// Expansion and the scroll position belong to the scene they were
/// authored against, so the state carries the scene path: a rebuild of
/// the same scene restores what the author had open, while a different
/// scene starts from its own defaults instead of inheriting another
/// scene's collapsed rows and offset.
/// </remarks>
internal sealed class HierarchyPanelState
{
	public string ScenePath { get; init; } = string.Empty;

	public string Search { get; init; } = string.Empty;

	public float ScrollOffset { get; init; }

	public IReadOnlyList<Guid> Expanded { get; init; } = [];

	public IReadOnlyList<Guid> Known { get; init; } = [];
}
