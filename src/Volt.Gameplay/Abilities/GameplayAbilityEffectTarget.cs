namespace Volt.Gameplay.Abilities;

/// <summary>
/// Selects which entity an ability plan applies an effect to.
/// </summary>
public enum GameplayAbilityEffectTarget : byte
{
	/// <summary>
	/// The entity that owns the ability.
	/// </summary>
	Owner = 0,

	/// <summary>
	/// The entity the activation targeted.
	/// </summary>
	Target = 1
}
