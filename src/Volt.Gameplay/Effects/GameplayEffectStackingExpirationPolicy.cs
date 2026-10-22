namespace Volt.Gameplay.Effects;

/// <summary>
/// Defines what happens when an active stacked effect reaches the end
/// of its duration.
/// </summary>
public enum GameplayEffectStackingExpirationPolicy : byte
{
	/// <summary>
	/// The whole instance, including every stack, is removed.
	/// </summary>
	ClearEntireStack = 0,

	/// <summary>
	/// One stack is removed and the duration restarts; the instance is
	/// removed when its last stack expires.
	/// </summary>
	RemoveSingleStackAndRefreshDuration = 1,

	/// <summary>
	/// The duration restarts and the stack count is untouched, which
	/// makes the effect effectively infinite unless game code removes
	/// stacks itself.
	/// </summary>
	RefreshDuration = 2
}
