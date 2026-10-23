namespace Volt.Gameplay.Abilities;

/// <summary>
/// Identifies the action of one authored ability plan step.
/// </summary>
public enum GameplayAbilityPlanStepKind : byte
{
	/// <summary>
	/// Waits for a simulation duration.
	/// </summary>
	WaitDuration = 0,

	/// <summary>
	/// Waits for a gameplay event.
	/// </summary>
	WaitEvent = 1,

	/// <summary>
	/// Waits for a tag condition.
	/// </summary>
	WaitTag = 2,

	/// <summary>
	/// Applies a gameplay effect.
	/// </summary>
	ApplyEffect = 3,

	/// <summary>
	/// Publishes a gameplay event.
	/// </summary>
	PublishEvent = 4,

	/// <summary>
	/// Ends the ability normally.
	/// </summary>
	End = 5
}
