namespace Volt.Gameplay.Effects;

/// <summary>
/// Classifies one gameplay effect state change.
/// </summary>
/// <remarks>
/// Removals report their cause so a listener can distinguish an
/// expiry from an explicit removal without keeping its own timers.
/// </remarks>
public enum GameplayEffectChangeKind : byte
{
	/// <summary>
	/// A new instance became active.
	/// </summary>
	Applied = 0,

	/// <summary>
	/// An existing instance gained a stack, or its stack count changed
	/// because of an expiration policy.
	/// </summary>
	StackChanged = 1,

	/// <summary>
	/// An instance's modifiers and granted tags were withdrawn.
	/// </summary>
	Inhibited = 2,

	/// <summary>
	/// An instance's modifiers and granted tags were restored.
	/// </summary>
	Uninhibited = 3,

	/// <summary>
	/// An instance was removed before its duration ended.
	/// </summary>
	Removed = 4,

	/// <summary>
	/// An instance reached the end of its duration.
	/// </summary>
	Expired = 5,

	/// <summary>
	/// A periodic effect executed its modifiers.
	/// </summary>
	PeriodExecuted = 6
}
