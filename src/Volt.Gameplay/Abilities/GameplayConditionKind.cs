namespace Volt.Gameplay.Abilities;

/// <summary>
/// Identifies the kind of node in a <see cref="GameplayCondition"/>
/// tree.
/// </summary>
/// <remarks>
/// The kinds are a closed set: a condition is data, not a delegate, so
/// evaluating a plan does not dispatch through user code and cannot
/// allocate. Adding a kind adds a branch to the evaluator.
/// </remarks>
public enum GameplayConditionKind : byte
{
	/// <summary>
	/// The owner has the tag, exactly or through a descendant.
	/// </summary>
	OwnerHasTag = 0,

	/// <summary>
	/// The owner lacks the tag, exactly or through a descendant.
	/// </summary>
	OwnerMissingTag = 1,

	/// <summary>
	/// The activation target has the tag.
	/// </summary>
	TargetHasTag = 2,

	/// <summary>
	/// The activation target lacks the tag.
	/// </summary>
	TargetMissingTag = 3,

	/// <summary>
	/// The activation target still exists in the world.
	/// </summary>
	TargetAlive = 4,

	/// <summary>
	/// One of the owner's attributes compares as requested.
	/// </summary>
	OwnerAttribute = 5,

	/// <summary>
	/// One of the target's attributes compares as requested.
	/// </summary>
	TargetAttribute = 6,

	/// <summary>
	/// Every child holds.
	/// </summary>
	All = 7,

	/// <summary>
	/// At least one child holds.
	/// </summary>
	Any = 8,

	/// <summary>
	/// The child does not hold.
	/// </summary>
	Not = 9
}
