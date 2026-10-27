using Volt.Ecs;

namespace Volt.Editor.Core.Runtime;

/// <summary>
/// Connects a stable authored identity to a disposable runtime ECS
/// handle.
/// </summary>
public sealed class RuntimeEntityInfo
{
	internal RuntimeEntityInfo(
		Guid authoredId,
		string name,
		Entity entity,
		Guid? parentId)
	{
		AuthoredId = authoredId;
		Name = name;
		Entity = entity;
		ParentId = parentId;
	}

	public Guid AuthoredId { get; }

	public string Name { get; }

	public Entity Entity { get; }

	public Guid? ParentId { get; }

	public List<IRuntimeBehaviour> Behaviours { get; } = [];
}

/// <summary>
/// A gameplay behaviour operating on one runtime entity.
/// </summary>
public interface IRuntimeBehaviour
{
	void Update(RuntimeScene scene, float deltaSeconds);
}

/// <summary>
/// Creates a runtime behaviour from an authored component.
/// </summary>
/// <remarks>
/// Game assemblies can register factories to run their own component
/// types without changing the editor. A factory returns false when it
/// does not recognize the component.
/// </remarks>
public interface IRuntimeBehaviourFactory
{
	bool TryCreate(
		RuntimeScene scene,
		RuntimeEntityInfo entity,
		Scenes.AuthoredComponent component,
		out IRuntimeBehaviour? behaviour);
}
