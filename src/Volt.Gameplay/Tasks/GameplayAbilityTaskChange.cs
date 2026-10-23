using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Events;

namespace Volt.Gameplay.Tasks;

/// <summary>
/// Describes one ability task transition.
/// </summary>
/// <remarks>
/// <para>
/// The payload is a value type, so a task notification allocates
/// nothing. It is delivered after the transition is committed: a
/// completed or cancelled task is already unlinked, unsubscribed, and
/// removed from the deadline heap, and its handle no longer resolves.
/// </para>
/// <para>
/// The notification for an event-driven completion carries the event
/// that completed the task, so a handler can read its payload without
/// storing it separately.
/// </para>
/// </remarks>
public readonly struct GameplayAbilityTaskChange
{
	internal GameplayAbilityTaskChange(
		GameplayAbilityTaskChangeKind kind,
		GameplayAbilityTaskHandle handle,
		GameplayAbilityActivation activation,
		GameplayAbilityTaskKind taskKind,
		GameplayAbilityTaskCompletionReason reason,
		int planStep,
		GameplayEvent gameplayEvent)
	{
		Kind = kind;
		Handle = handle;
		Activation = activation;
		Owner = handle.Owner;
		TaskKind = taskKind;
		Reason = reason;
		PlanStep = planStep;
		Event = gameplayEvent;
	}

	/// <summary>
	/// Gets what happened to the task.
	/// </summary>
	public GameplayAbilityTaskChangeKind Kind { get; }

	/// <summary>
	/// Gets the task handle. It is stale after a terminal change.
	/// </summary>
	public GameplayAbilityTaskHandle Handle { get; }

	/// <summary>
	/// Gets the owning activation.
	/// </summary>
	public GameplayAbilityActivation Activation { get; }

	/// <summary>
	/// Gets the entity that owns the ability.
	/// </summary>
	public Entity Owner { get; }

	/// <summary>
	/// Gets the task kind.
	/// </summary>
	public GameplayAbilityTaskKind TaskKind { get; }

	/// <summary>
	/// Gets why the task reached a terminal state, or
	/// <see cref="GameplayAbilityTaskCompletionReason.ConditionMet"/>
	/// for a start notification, where it carries no meaning.
	/// </summary>
	public GameplayAbilityTaskCompletionReason Reason { get; }

	/// <summary>
	/// Gets the plan step that started the task, or <c>-1</c> when the
	/// task was started by application code.
	/// </summary>
	public int PlanStep { get; }

	/// <summary>
	/// Gets the event that completed the task, or a default event when
	/// the task completed for another reason.
	/// </summary>
	public GameplayEvent Event { get; }

	/// <inheritdoc />
	public override string ToString()
	{
		return $"{Kind} {TaskKind} {Handle} ({Reason})";
	}
}
