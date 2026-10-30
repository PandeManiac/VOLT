using Volt.Ecs.Component;

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

	/// <summary>
	/// Components the last query required. A non-empty list also means
	/// the developer expects that query back, so the panel runs it again
	/// against the next world Play Mode builds.
	/// </summary>
	public IReadOnlyList<ComponentType> RequiredComponents { get; init; } = [];
}
