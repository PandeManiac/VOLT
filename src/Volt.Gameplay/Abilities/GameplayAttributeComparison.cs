namespace Volt.Gameplay.Abilities;

/// <summary>
/// Compares a live attribute value against a constant in a
/// <see cref="GameplayCondition"/>.
/// </summary>
/// <remarks>
/// The comparison reads the attribute store's cached current value, so
/// evaluating it costs one binary search and no aggregation. The
/// constant is authored once and stored in the immutable condition.
/// </remarks>
public enum GameplayAttributeComparison : byte
{
	/// <summary>
	/// The value is strictly less than the constant.
	/// </summary>
	LessThan = 0,

	/// <summary>
	/// The value is less than or equal to the constant.
	/// </summary>
	LessThanOrEqual = 1,

	/// <summary>
	/// The value is strictly greater than the constant.
	/// </summary>
	GreaterThan = 2,

	/// <summary>
	/// The value is greater than or equal to the constant.
	/// </summary>
	GreaterThanOrEqual = 3,

	/// <summary>
	/// The value equals the constant exactly.
	/// </summary>
	Equal = 4,

	/// <summary>
	/// The value differs from the constant.
	/// </summary>
	NotEqual = 5
}
