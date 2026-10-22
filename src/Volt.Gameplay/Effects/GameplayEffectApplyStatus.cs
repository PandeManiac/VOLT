namespace Volt.Gameplay.Effects;

/// <summary>
/// Describes what an attempt to apply a gameplay effect did.
/// </summary>
public enum GameplayEffectApplyStatus : byte
{
	/// <summary>
	/// The effect executed once and was not tracked.
	/// </summary>
	InstantExecuted = 0,

	/// <summary>
	/// A new active instance was created.
	/// </summary>
	Applied = 1,

	/// <summary>
	/// The application merged into an existing instance as one or
	/// more stacks.
	/// </summary>
	/// <remarks>
	/// A stack limit clamps the count instead of rejecting the
	/// application: an application at the limit still refreshes the
	/// duration and period according to the definition's policies, and
	/// leaves the count unchanged. That is Unreal's behavior with
	/// <c>bDenyOverflowApplication</c> left at its default.
	/// </remarks>
	Stacked = 2,

	/// <summary>
	/// The target did not satisfy the application requirements.
	/// </summary>
	RejectedByRequirements = 3,

	/// <summary>
	/// The instance ended while it was still being applied, because a
	/// notification removed it, destroyed the target, or changed a tag
	/// that its removal requirements name.
	/// </summary>
	/// <remarks>
	/// This is not a failure of the request: the effect applied and
	/// then legitimately ended before the call returned. A listener
	/// that kills the target on the first tick of a damage effect
	/// produces exactly this outcome.
	/// </remarks>
	Discarded = 4
}
