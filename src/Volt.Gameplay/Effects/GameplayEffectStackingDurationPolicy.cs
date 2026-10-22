namespace Volt.Gameplay.Effects;

/// <summary>
/// Defines what a successful stack application does to an active
/// effect's remaining duration.
/// </summary>
public enum GameplayEffectStackingDurationPolicy : byte
{
	/// <summary>
	/// The duration restarts from the moment of application.
	/// </summary>
	/// <remarks>
	/// This is Unreal's default and the behavior most players expect
	/// from a refreshable aura: reapplying it keeps it alive.
	/// </remarks>
	RefreshOnSuccessfulApplication = 0,

	/// <summary>
	/// The remaining duration is untouched.
	/// </summary>
	NeverRefresh = 1,

	/// <summary>
	/// The new application's full duration is added to the remaining
	/// duration.
	/// </summary>
	ExtendDuration = 2
}
