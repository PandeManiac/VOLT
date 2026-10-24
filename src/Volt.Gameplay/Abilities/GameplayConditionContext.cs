using Volt.Ecs;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Tags;

namespace Volt.Gameplay.Abilities;

/// <summary>
/// The live state a condition is evaluated against.
/// </summary>
/// <remarks>
/// This is a stack value built by the plan executor for one evaluation.
/// It holds references to the world's stores and the two entities the
/// condition may read, so evaluation allocates nothing. The target
/// aliveness flag is captured when the context is built; a condition
/// cannot observe a target that is destroyed later in the same
/// evaluation.
/// </remarks>
internal readonly ref struct GameplayConditionContext
{
	public GameplayConditionContext(
		GameplayTagStore tags,
		GameplayAttributeStore attributes,
		Entity owner,
		Entity target,
		bool targetAlive)
	{
		Tags = tags;
		Attributes = attributes;
		Owner = owner;
		Target = target;
		TargetAlive = targetAlive;
	}

	public GameplayTagStore Tags { get; }

	public GameplayAttributeStore Attributes { get; }

	public Entity Owner { get; }

	public Entity Target { get; }

	public bool TargetAlive { get; }
}
