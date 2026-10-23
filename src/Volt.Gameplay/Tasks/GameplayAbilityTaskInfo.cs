using Volt.Ecs;
using Volt.Gameplay.Abilities;

namespace Volt.Gameplay.Tasks;

/// <summary>
/// A read-only snapshot of one pending task.
/// </summary>
/// <remarks>
/// A snapshot copies the task's identity and schedule; it is not a
/// reference. A task that completes after the snapshot was taken does
/// not change the snapshot, and its handle no longer resolves.
/// </remarks>
public readonly struct GameplayAbilityTaskInfo
{
	internal GameplayAbilityTaskInfo(
		GameplayAbilityTaskHandle handle,
		GameplayAbilityActivation activation,
		GameplayAbilityTaskKind kind,
		int planStep,
		double deadline,
		double remainingTime)
	{
		Handle = handle;
		Activation = activation;
		Owner = handle.Owner;
		Kind = kind;
		PlanStep = planStep;
		Deadline = deadline;
		RemainingTime = remainingTime;
	}

	/// <summary>
	/// Gets the task handle.
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
	public GameplayAbilityTaskKind Kind { get; }

	/// <summary>
	/// Gets the plan step that started the task, or <c>-1</c> when the
	/// task was started by application code.
	/// </summary>
	public int PlanStep { get; }

	/// <summary>
	/// Gets the absolute simulation time the task is due, or
	/// <see cref="double.PositiveInfinity"/> when it waits for an event.
	/// </summary>
	public double Deadline { get; }

	/// <summary>
	/// Gets the simulation time left before the task is due, or
	/// <see cref="double.PositiveInfinity"/> when it waits for an event.
	/// </summary>
	public double RemainingTime { get; }
}
