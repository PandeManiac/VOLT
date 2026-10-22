using Volt.Gameplay.Attributes;
using Volt.Gameplay.Tags;

namespace Volt.Gameplay.Effects;

/// <summary>
/// An immutable description of a gameplay effect: what it changes, how
/// long it lasts, what it grants, and how re-applications combine.
/// </summary>
/// <remarks>
/// <para>
/// Definitions are shared, not per-instance state. An effect applied
/// to a thousand entities is one definition plus a thousand compact
/// instances that hold only the stack count, the clock, and the
/// modifier handles needed to undo the effect. Create definitions
/// during initialization with <see cref="GameplayEffectDefinitionBuilder"/>
/// and hold them for the process lifetime.
/// </para>
/// <para>
/// Unreal's <c>UGameplayEffect</c> is a data asset whose runtime spec
/// carries captured attribute values and set-by-caller magnitudes.
/// VOLT keeps the definition purely declarative and gives each active
/// instance its own values, so a definition can be shared across
/// worlds and threads without copying.
/// </para>
/// </remarks>
public sealed class GameplayEffectDefinition
{
	private readonly GameplayEffectModifier[] _modifiers;

	internal GameplayEffectDefinition(
		string name,
		GameplayEffectDurationType durationType,
		float duration,
		float period,
		GameplayEffectModifier[] modifiers,
		GameplayTagSet grantedTags,
		GameplayTagRequirements applicationRequirements,
		GameplayTagRequirements ongoingRequirements,
		GameplayTagRequirements removalRequirements,
		GameplayEffectStackingType stackingType,
		int stackLimit,
		GameplayEffectStackingDurationPolicy stackDurationRefreshPolicy,
		GameplayEffectStackingPeriodPolicy stackPeriodResetPolicy,
		GameplayEffectStackingExpirationPolicy stackExpirationPolicy,
		GameplayEffectPeriodInhibitionPolicy periodicInhibitionPolicy,
		bool factorInStackCount,
		bool executePeriodicOnApplication)
	{
		Name = name;
		DurationType = durationType;
		Duration = duration;
		Period = period;
		_modifiers = modifiers;
		GrantedTags = grantedTags;
		ApplicationRequirements = applicationRequirements;
		OngoingRequirements = ongoingRequirements;
		RemovalRequirements = removalRequirements;
		StackingType = stackingType;
		StackLimit = stackLimit;
		StackDurationRefreshPolicy = stackDurationRefreshPolicy;
		StackPeriodResetPolicy = stackPeriodResetPolicy;
		StackExpirationPolicy = stackExpirationPolicy;
		PeriodicInhibitionPolicy = periodicInhibitionPolicy;
		FactorInStackCount = factorInStackCount;
		ExecutePeriodicOnApplication = executePeriodicOnApplication;
	}

	/// <summary>
	/// Starts building a new effect definition.
	/// </summary>
	/// <param name="name">
	/// The diagnostic name, such as <c>Status.Burning</c>.
	/// </param>
	/// <returns>A builder for the definition.</returns>
	public static GameplayEffectDefinitionBuilder Create(string name)
	{
		return new GameplayEffectDefinitionBuilder(name);
	}

	/// <summary>
	/// Gets the diagnostic name of this effect.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// Gets how long the effect stays active.
	/// </summary>
	public GameplayEffectDurationType DurationType { get; }

	/// <summary>
	/// Gets the duration in seconds, or zero for instant and infinite
	/// effects.
	/// </summary>
	public float Duration { get; }

	/// <summary>
	/// Gets the period in seconds, or zero for a non-periodic effect.
	/// </summary>
	public float Period { get; }

	/// <summary>
	/// Gets the attribute changes this effect makes.
	/// </summary>
	/// <remarks>
	/// A periodic effect applies these once per tick as instant
	/// changes to the base value. A non-periodic effect holds them as
	/// temporary modifiers for as long as it is active.
	/// </remarks>
	public ReadOnlySpan<GameplayEffectModifier> Modifiers => _modifiers;

	/// <summary>
	/// Gets the tags granted for as long as the effect is active.
	/// </summary>
	public GameplayTagSet GrantedTags { get; }

	/// <summary>
	/// Gets the tags the target must satisfy for the effect to apply.
	/// </summary>
	public GameplayTagRequirements ApplicationRequirements { get; }

	/// <summary>
	/// Gets the tags the target must satisfy for the effect to stay
	/// active.
	/// </summary>
	/// <remarks>
	/// An active effect whose ongoing requirements are not met is
	/// inhibited: its modifiers and granted tags are withdrawn until
	/// the requirements hold again. Its clock keeps running.
	/// </remarks>
	public GameplayTagRequirements OngoingRequirements { get; }

	/// <summary>
	/// Gets the tags that remove an active effect as soon as they are
	/// present on the target.
	/// </summary>
	public GameplayTagRequirements RemovalRequirements { get; }

	/// <summary>
	/// Gets how re-applications combine.
	/// </summary>
	public GameplayEffectStackingType StackingType { get; }

	/// <summary>
	/// Gets the maximum stack count, or zero for no limit.
	/// </summary>
	public int StackLimit { get; }

	/// <summary>
	/// Gets what a successful stack application does to the duration.
	/// </summary>
	public GameplayEffectStackingDurationPolicy StackDurationRefreshPolicy { get; }

	/// <summary>
	/// Gets what a successful stack application does to the period.
	/// </summary>
	public GameplayEffectStackingPeriodPolicy StackPeriodResetPolicy { get; }

	/// <summary>
	/// Gets what happens when a stacked effect reaches the end of its
	/// duration.
	/// </summary>
	public GameplayEffectStackingExpirationPolicy StackExpirationPolicy { get; }

	/// <summary>
	/// Gets what happens to the period schedule when inhibition ends.
	/// </summary>
	public GameplayEffectPeriodInhibitionPolicy PeriodicInhibitionPolicy { get; }

	/// <summary>
	/// Gets whether modifier magnitudes scale with the stack count.
	/// </summary>
	public bool FactorInStackCount { get; }

	/// <summary>
	/// Gets whether a periodic effect executes once at application
	/// instead of waiting for its first period.
	/// </summary>
	public bool ExecutePeriodicOnApplication { get; }

	/// <summary>
	/// Gets whether the effect executes once and is not tracked.
	/// </summary>
	public bool IsInstant => DurationType == GameplayEffectDurationType.Instant;

	/// <summary>
	/// Gets whether the effect stays active until it is removed.
	/// </summary>
	public bool IsInfinite => DurationType == GameplayEffectDurationType.Infinite;

	/// <summary>
	/// Gets whether the effect executes its modifiers on a timer.
	/// </summary>
	public bool IsPeriodic => Period > 0f;

	/// <summary>
	/// Gets the number of modifiers.
	/// </summary>
	public int ModifierCount => _modifiers.Length;

	/// <summary>
	/// Gets the number of stacks one application adds.
	/// </summary>
	/// <remarks>
	/// VOLT applies exactly one stack per call, so a caller that wants
	/// three stacks applies three times. That keeps the application
	/// path free of a loop over a count that is almost always one, and
	/// it makes the stack limit behave identically whether stacks
	/// arrive one at a time or in a burst.
	/// </remarks>
	public int StacksPerApplication => 1;

	/// <summary>
	/// Gets the magnitude of a modifier at a given stack count.
	/// </summary>
	/// <param name="modifierIndex">The modifier index.</param>
	/// <param name="stackCount">The active stack count.</param>
	/// <remarks>
	/// The scaling reproduces Unreal's
	/// <c>GameplayEffectUtilities::ComputeStackedModifierMagnitude</c>:
	/// an override ignores stacks, a compound multiplier is raised to
	/// the stack count, and every other operation is multiplied by it
	/// after removing the operation's unit bias.
	/// </remarks>
	internal float GetStackedMagnitude(int modifierIndex, int stackCount)
	{
		GameplayEffectModifier modifier = _modifiers[modifierIndex];

		if (!FactorInStackCount || stackCount <= 1)
		{
			return modifier.Magnitude;
		}

		if (modifier.Operation == GameplayModifierOperation.Override)
		{
			return modifier.Magnitude;
		}

		float bias = modifier.Operation
			is GameplayModifierOperation.MultiplyAdditive
			or GameplayModifierOperation.DivideAdditive
			? 1f
			: 0f;

		float value = modifier.Magnitude - bias;

		value = modifier.Operation == GameplayModifierOperation.MultiplyCompound
			? MathF.Pow(value, stackCount)
			: value * stackCount;

		return value + bias;
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return $"GameplayEffect({Name})";
	}
}
