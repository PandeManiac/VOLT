namespace Volt.Gameplay.Attributes;

/// <summary>
/// Defines how a gameplay modifier changes an attribute.
/// </summary>
/// <remarks>
/// <para>
/// Operations are evaluated in a fixed order, reproducing Unreal's
/// aggregator:
/// </para>
/// <code>
/// ((Base + Sum(AddBase)) * MultiplyAdditive / DivideAdditive
///   * Product(MultiplyCompound)) + Sum(AddFinal)
/// </code>
/// <para>
/// Any active <see cref="Override"/> replaces the entire result; base
/// and every other modifier are then ignored. If several overrides
/// are active, the one added first wins.
/// </para>
/// <para>
/// Multiplicative operations use a bias of one. A magnitude of 1.5
/// means +50%, two such modifiers sum to 2.0 (not 1.5 × 1.5), and a
/// divisor magnitude of 2.0 halves the result. This is Unreal's
/// "multiply additive" and "divide additive" behavior.
/// </para>
/// </remarks>
public enum GameplayModifierOperation : byte
{
	/// <summary>
	/// Adds to the base value before multiplication.
	/// </summary>
	AddBase = 0,

	/// <summary>
	/// Adds a multiplier to the additive-multiplier sum.
	/// </summary>
	MultiplyAdditive = 1,

	/// <summary>
	/// Adds a divisor to the additive-divisor sum.
	/// </summary>
	DivideAdditive = 2,

	/// <summary>
	/// Replaces the computed value entirely.
	/// </summary>
	Override = 3,

	/// <summary>
	/// Multiplies the running result by this factor.
	/// </summary>
	MultiplyCompound = 4,

	/// <summary>
	/// Adds to the final result.
	/// </summary>
	AddFinal = 5
}
