namespace Volt.Gameplay.Effects;

/// <summary>
/// Defines how long a gameplay effect stays active.
/// </summary>
/// <remarks>
/// Unreal's <c>EGameplayEffectDurationType</c> has the same three
/// values. An instant effect is executed once and never tracked; the
/// other two create an active effect that owns modifiers and granted
/// tags until it is removed or expires.
/// </remarks>
public enum GameplayEffectDurationType : byte
{
	/// <summary>
	/// The effect executes once, immediately, and is not tracked.
	/// </summary>
	Instant = 0,

	/// <summary>
	/// The effect stays active until it is explicitly removed.
	/// </summary>
	Infinite = 1,

	/// <summary>
	/// The effect stays active for a fixed number of seconds.
	/// </summary>
	HasDuration = 2
}
