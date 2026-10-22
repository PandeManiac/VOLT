namespace Volt.Gameplay.Attributes;

/// <summary>
/// Describes the identity and numeric range of a registered
/// attribute.
/// </summary>
/// <remarks>
/// <para>
/// Unreal attributes are plain floats; range enforcement lives in the
/// game's <c>PreAttributeChange</c> override, which can implement any
/// policy but is easy to forget and cannot be inspected. VOLT keeps
/// the range in the definition, so base and current values are
/// clamped consistently and the policy is visible where the attribute
/// is declared. A range with infinite bounds means "unclamped".
/// </para>
/// <para>
/// A definition is immutable and shared; it is created once at
/// registration and never changes.
/// </para>
/// </remarks>
public readonly struct GameplayAttributeDefinition
{
	internal GameplayAttributeDefinition(
		GameplayAttribute attribute,
		string name,
		float defaultBaseValue,
		float minimum,
		float maximum)
	{
		Attribute = attribute;
		Name = name;
		DefaultBaseValue = defaultBaseValue;
		Minimum = minimum;
		Maximum = maximum;
	}

	/// <summary>
	/// Gets the attribute this definition describes.
	/// </summary>
	public GameplayAttribute Attribute { get; }

	/// <summary>
	/// Gets the canonical attribute name.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// Gets the base value used before an entity has explicit state.
	/// </summary>
	public float DefaultBaseValue { get; }

	/// <summary>
	/// Gets the lower bound applied to base and current values.
	/// </summary>
	/// <remarks>
	/// <see cref="float.NegativeInfinity"/> means no lower bound.
	/// </remarks>
	public float Minimum { get; }

	/// <summary>
	/// Gets the upper bound applied to base and current values.
	/// </summary>
	/// <remarks>
	/// <see cref="float.PositiveInfinity"/> means no upper bound.
	/// </remarks>
	public float Maximum { get; }

	/// <summary>
	/// Gets whether a finite lower bound is configured.
	/// </summary>
	public bool HasMinimum => float.IsFinite(Minimum);

	/// <summary>
	/// Gets whether a finite upper bound is configured.
	/// </summary>
	public bool HasMaximum => float.IsFinite(Maximum);

	/// <summary>
	/// Clamps a value into this definition's range.
	/// </summary>
	/// <param name="value">The value to clamp.</param>
	/// <returns>The clamped value.</returns>
	/// <remarks>
	/// NaN is mapped to <see cref="Minimum"/>. An attribute with a
	/// NaN value would make every comparison false, so a deterministic
	/// fallback is preferable to letting the value propagate into
	/// gameplay logic.
	/// </remarks>
	public float Clamp(float value)
	{
		if (float.IsNaN(value))
		{
			return Minimum;
		}

		if (value < Minimum)
		{
			return Minimum;
		}

		if (value > Maximum)
		{
			return Maximum;
		}

		return value;
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return $"{Name} [default {DefaultBaseValue}, " +
			$"range {Minimum}..{Maximum}]";
	}
}
