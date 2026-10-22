namespace Volt.Gameplay.Effects;

/// <summary>
/// Defines what happens to a periodic effect's schedule when the
/// effect stops being inhibited.
/// </summary>
/// <remarks>
/// While an effect is inhibited its modifiers and granted tags are
/// absent but its clock keeps running. This policy decides how much of
/// that elapsed period still counts when it becomes active again.
/// </remarks>
public enum GameplayEffectPeriodInhibitionPolicy : byte
{
	/// <summary>
	/// The schedule continues as if the inhibition never happened.
	/// </summary>
	/// <remarks>
	/// A tick that came due during inhibition is skipped, not
	/// replayed, and the next tick happens at the originally scheduled
	/// boundary. This is Unreal's default.
	/// </remarks>
	NeverReset = 0,

	/// <summary>
	/// The next tick happens one full period after inhibition ends.
	/// </summary>
	ResetPeriod = 1,

	/// <summary>
	/// The effect executes once as inhibition ends and then restarts
	/// its period.
	/// </summary>
	ExecuteAndResetPeriod = 2
}
