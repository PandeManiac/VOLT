namespace Volt.Gameplay.Effects;

/// <summary>
/// Defines what a successful stack application does to a periodic
/// effect's progress toward its next tick.
/// </summary>
public enum GameplayEffectStackingPeriodPolicy : byte
{
	/// <summary>
	/// The next tick happens one full period after the application.
	/// </summary>
	ResetOnSuccessfulApplication = 0,

	/// <summary>
	/// The period schedule continues unchanged.
	/// </summary>
	NeverReset = 1
}
