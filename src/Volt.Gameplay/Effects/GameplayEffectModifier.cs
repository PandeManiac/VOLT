using Volt.Gameplay.Attributes;

namespace Volt.Gameplay.Effects;

/// <summary>
/// Describes one attribute change made by a gameplay effect.
/// </summary>
/// <remarks>
/// A modifier is declarative definition data, not runtime state. The
/// magnitude is a constant here; attribute-derived magnitudes are
/// deliberately not part of this milestone, because a definition that
/// reads another attribute must be re-evaluated when that attribute
/// changes, which is a different and more expensive update path.
/// </remarks>
public readonly struct GameplayEffectModifier
{
	internal GameplayEffectModifier(
		GameplayAttribute attribute,
		GameplayModifierOperation operation,
		float magnitude)
	{
		Attribute = attribute;
		Operation = operation;
		Magnitude = magnitude;
	}

	/// <summary>
	/// Gets the modified attribute.
	/// </summary>
	public GameplayAttribute Attribute { get; }

	/// <summary>
	/// Gets how the magnitude changes the attribute.
	/// </summary>
	public GameplayModifierOperation Operation { get; }

	/// <summary>
	/// Gets the declared magnitude, before stack scaling.
	/// </summary>
	public float Magnitude { get; }

	/// <inheritdoc />
	public override string ToString()
	{
		return $"{Attribute} {Operation} {Magnitude}";
	}
}
