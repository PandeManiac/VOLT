namespace Volt.Gameplay.Effects;

/// <summary>
/// Defines when a re-application of an effect merges with an existing
/// instance instead of creating a new one.
/// </summary>
/// <remarks>
/// Only effects with a non-instant duration can stack. An instant
/// effect is executed and forgotten, so there is nothing to merge
/// with.
/// </remarks>
public enum GameplayEffectStackingType : byte
{
	/// <summary>
	/// Every application is an independent instance.
	/// </summary>
	None = 0,

	/// <summary>
	/// Applications from the same source merge into one instance.
	/// </summary>
	/// <remarks>
	/// The same definition applied by two different sources produces
	/// two instances, each with its own stack count. This is the
	/// useful default for damage-over-time: two casters burn the target
	/// independently.
	/// </remarks>
	AggregateBySource = 1,

	/// <summary>
	/// Every application to the same target merges into one instance,
	/// whatever its source.
	/// </summary>
	AggregateByTarget = 2
}
