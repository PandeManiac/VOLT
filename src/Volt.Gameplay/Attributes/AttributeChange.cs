using Volt.Ecs;

namespace Volt.Gameplay.Attributes;

/// <summary>
/// Describes one committed attribute value change.
/// </summary>
/// <remarks>
/// <para>
/// One notification is raised per mutation, after the cached current
/// value has been recalculated and clamped. Both old and new values
/// are supplied so a listener can animate a delta without keeping a
/// shadow copy for every entity and attribute.
/// </para>
/// <para>
/// A change to the base value that leaves the current value untouched
/// still raises the event, because the permanent value changed even
/// though a modifier currently hides it.
/// </para>
/// </remarks>
public readonly struct AttributeChange
{
	internal AttributeChange(
		Entity entity,
		GameplayAttribute attribute,
		float oldBaseValue,
		float newBaseValue,
		float oldCurrentValue,
		float newCurrentValue)
	{
		Entity = entity;
		Attribute = attribute;
		OldBaseValue = oldBaseValue;
		NewBaseValue = newBaseValue;
		OldCurrentValue = oldCurrentValue;
		NewCurrentValue = newCurrentValue;
	}

	/// <summary>
	/// Gets the entity whose attribute changed.
	/// </summary>
	public Entity Entity { get; }

	/// <summary>
	/// Gets the changed attribute.
	/// </summary>
	public GameplayAttribute Attribute { get; }

	/// <summary>
	/// Gets the base value before the change.
	/// </summary>
	public float OldBaseValue { get; }

	/// <summary>
	/// Gets the base value after the change.
	/// </summary>
	public float NewBaseValue { get; }

	/// <summary>
	/// Gets the current value before the change.
	/// </summary>
	public float OldCurrentValue { get; }

	/// <summary>
	/// Gets the current value after the change.
	/// </summary>
	public float NewCurrentValue { get; }

	/// <summary>
	/// Gets the signed change in current value.
	/// </summary>
	public float CurrentDelta => NewCurrentValue - OldCurrentValue;

	/// <inheritdoc />
	public override string ToString()
	{
		return $"{Attribute}: base {OldBaseValue} -> {NewBaseValue}, " +
			$"current {OldCurrentValue} -> {NewCurrentValue}";
	}
}
