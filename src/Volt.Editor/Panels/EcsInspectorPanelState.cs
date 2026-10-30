namespace Volt.Editor.Panels;

/// <summary>
/// How the ECS panel orders the entities a query matched.
/// </summary>
internal enum EcsQuerySort
{
	SceneOrder,
	Name,
	Archetype
}

/// <summary>
/// View state the ECS panel keeps across an interface-scale rebuild,
/// when the panel widget itself is recreated.
/// </summary>
internal sealed class EcsInspectorPanelState
{
	public string Filter { get; init; } = string.Empty;

	public EcsQuerySort Sort { get; init; } = EcsQuerySort.SceneOrder;
}
