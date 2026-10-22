using Volt.Ecs;

namespace Volt.Gameplay.Effects;

/// <summary>
/// A read-only snapshot of one active gameplay effect instance.
/// </summary>
/// <remarks>
/// The snapshot is copied out because the runtime owns the compact
/// instance storage and may move it. Holding a snapshot is safe; it
/// simply stops reflecting later changes. Use the handle to act on
/// the live instance.
/// </remarks>
public readonly struct ActiveGameplayEffect
{
	internal ActiveGameplayEffect(
		GameplayEffectHandle handle,
		GameplayEffectDefinition definition,
		Entity target,
		Entity source,
		int stackCount,
		double remainingDuration,
		double remainingPeriod,
		bool isInhibited)
	{
		Handle = handle;
		Definition = definition;
		Target = target;
		Source = source;
		StackCount = stackCount;
		RemainingDuration = remainingDuration;
		RemainingPeriod = remainingPeriod;
		IsInhibited = isInhibited;
	}

	/// <summary>
	/// Gets the handle that refers to the live instance.
	/// </summary>
	public GameplayEffectHandle Handle { get; }

	/// <summary>
	/// Gets the shared definition.
	/// </summary>
	public GameplayEffectDefinition Definition { get; }

	/// <summary>
	/// Gets the affected entity.
	/// </summary>
	public Entity Target { get; }

	/// <summary>
	/// Gets the entity that applied the effect, or
	/// <c>default(Entity)</c> when the application named no source.
	/// </summary>
	/// <remarks>
	/// The source is an identity, not a live reference. A damage over
	/// time effect keeps burning after its caster is destroyed, exactly
	/// as in Unreal.
	/// </remarks>
	public Entity Source { get; }

	/// <summary>
	/// Gets the current stack count, which is one for a non-stacked
	/// effect.
	/// </summary>
	public int StackCount { get; }

	/// <summary>
	/// Gets the remaining duration in seconds, or
	/// <see cref="double.PositiveInfinity"/> for an infinite effect.
	/// </summary>
	public double RemainingDuration { get; }

	/// <summary>
	/// Gets the time until the next periodic tick, or
	/// <see cref="double.PositiveInfinity"/> for a non-periodic effect.
	/// </summary>
	public double RemainingPeriod { get; }

	/// <summary>
	/// Gets whether the modifiers and granted tags are currently
	/// withdrawn because the ongoing requirements are unmet.
	/// </summary>
	public bool IsInhibited { get; }

	/// <inheritdoc />
	public override string ToString()
	{
		return $"{Definition.Name} on {Target}, {StackCount} stack(s), " +
			$"{(IsInhibited ? "inhibited" : "active")}";
	}
}
