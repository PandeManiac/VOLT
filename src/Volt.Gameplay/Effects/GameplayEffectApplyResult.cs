using Volt.Ecs;

namespace Volt.Gameplay.Effects;

/// <summary>
/// Describes the outcome of one application attempt.
/// </summary>
/// <remarks>
/// A rejected application is a normal gameplay event, not an error,
/// so it is reported as data. Only a dead target or an unregistered
/// attribute is a programming mistake and throws instead.
/// </remarks>
public readonly struct GameplayEffectApplyResult
{
	internal GameplayEffectApplyResult(
		GameplayEffectApplyStatus status,
		GameplayEffectHandle handle,
		Entity target,
		int stackCount)
	{
		Status = status;
		Handle = handle;
		Target = target;
		StackCount = stackCount;
	}

	/// <summary>
	/// Gets the outcome.
	/// </summary>
	public GameplayEffectApplyStatus Status { get; }

	/// <summary>
	/// Gets the handle of the active instance, or
	/// <see cref="GameplayEffectHandle.None"/> for an instant or
	/// rejected application.
	/// </summary>
	public GameplayEffectHandle Handle { get; }

	/// <summary>
	/// Gets the target of the application.
	/// </summary>
	public Entity Target { get; }

	/// <summary>
	/// Gets the stack count after the application, or zero when
	/// nothing was applied.
	/// </summary>
	public int StackCount { get; }

	/// <summary>
	/// Gets whether the effect applied or executed.
	/// </summary>
	public bool Applied => Status != GameplayEffectApplyStatus.RejectedByRequirements;

	/// <inheritdoc />
	public override string ToString()
	{
		return $"{Status} on {Target}, stacks {StackCount}, {Handle}";
	}
}
