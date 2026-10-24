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
	End = 5,

	/// <summary>
	/// Waits for the first of several sources.
	/// </summary>
	WaitAny = 6,

	/// <summary>
	/// Waits for a registered custom task.
	/// </summary>
	WaitCustom = 7,

	/// <summary>
	/// Continues at another step when a condition does not hold.
	/// </summary>
	Branch = 8,

	/// <summary>
	/// Continues at another step unconditionally.
	/// </summary>
	Goto = 9,

	/// <summary>Executes a one-shot gameplay cue.</summary>
	ExecuteCue = 10,

	/// <summary>Adds a persistent cue owned by this activation.</summary>
	AddCue = 11
}
