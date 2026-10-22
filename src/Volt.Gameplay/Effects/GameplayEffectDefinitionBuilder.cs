using Volt.Gameplay.Attributes;
using Volt.Gameplay.Tags;

namespace Volt.Gameplay.Effects;

/// <summary>
/// Builds an immutable <see cref="GameplayEffectDefinition"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is authoring code. It may allocate and throw on configuration
/// mistakes, because it runs during initialization rather than during
/// gameplay. Every value is validated when <see cref="Build"/> is
/// called, so an invalid definition cannot reach the runtime.
/// </para>
/// <para>
/// The builder is mutable and single-use; call <see cref="Build"/>
/// once per definition and keep the result.
/// </para>
/// </remarks>
public sealed class GameplayEffectDefinitionBuilder
{
	private readonly string _name;
	private readonly List<GameplayEffectModifier> _modifiers = [];

	private GameplayEffectDurationType _durationType =
		GameplayEffectDurationType.Instant;

	private float _duration;
	private float _period;
	private GameplayTagSet _grantedTags = GameplayTagSet.Empty;
	private GameplayTagRequirements _applicationRequirements =
		GameplayTagRequirements.Empty;
	private GameplayTagRequirements _ongoingRequirements =
		GameplayTagRequirements.Empty;
	private GameplayTagRequirements _removalRequirements =
		GameplayTagRequirements.Empty;

	private GameplayEffectStackingType _stackingType =
		GameplayEffectStackingType.None;

	private int _stackLimit;

	private GameplayEffectStackingDurationPolicy _stackDurationRefreshPolicy =
		GameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication;

	private GameplayEffectStackingPeriodPolicy _stackPeriodResetPolicy =
		GameplayEffectStackingPeriodPolicy.ResetOnSuccessfulApplication;

	private GameplayEffectStackingExpirationPolicy _stackExpirationPolicy =
		GameplayEffectStackingExpirationPolicy.ClearEntireStack;

	private GameplayEffectPeriodInhibitionPolicy _periodicInhibitionPolicy =
		GameplayEffectPeriodInhibitionPolicy.NeverReset;

	private bool _factorInStackCount = true;
	private bool _executePeriodicOnApplication = true;

	internal GameplayEffectDefinitionBuilder(string name)
	{
		_name = name;
	}

	/// <summary>
	/// Makes the effect execute once, immediately.
	/// </summary>
	/// <remarks>
	/// This is the default. An instant effect cannot have a period:
	/// there is no tracked instance for a timer to fire against, and
	/// Unreal silently ignores the period instead. VOLT rejects the
	/// combination when <see cref="Build"/> is called.
	/// </remarks>
	public GameplayEffectDefinitionBuilder Instant()
	{
		_durationType = GameplayEffectDurationType.Instant;
		_duration = 0f;
		return this;
	}

	/// <summary>
	/// Makes the effect stay active for a fixed number of seconds.
	/// </summary>
	/// <param name="seconds">The duration, which must be finite and
	/// greater than zero.</param>
	public GameplayEffectDefinitionBuilder HasDuration(float seconds)
	{
		_durationType = GameplayEffectDurationType.HasDuration;
		_duration = seconds;
		return this;
	}

	/// <summary>
	/// Makes the effect stay active until it is removed.
	/// </summary>
	public GameplayEffectDefinitionBuilder Infinite()
	{
		_durationType = GameplayEffectDurationType.Infinite;
		_duration = 0f;
		return this;
	}

	/// <summary>
	/// Makes the effect execute its modifiers once every period.
	/// </summary>
	/// <param name="seconds">
	/// The period, which must be finite and greater than zero.
	/// </param>
	/// <remarks>
	/// A periodic effect does not hold temporary modifiers; each tick
	/// applies its modifiers as instant changes to the base value, so
	/// damage over time is permanent damage per tick. This matches
	/// Unreal, where a spec with a period skips the aggregator.
	/// </remarks>
	public GameplayEffectDefinitionBuilder Period(float seconds)
	{
		_period = seconds;
		return this;
	}

	/// <summary>
	/// Adds an attribute change.
	/// </summary>
	/// <param name="attribute">The registered attribute.</param>
	/// <param name="operation">How the magnitude changes it.</param>
	/// <param name="magnitude">The declared magnitude.</param>
	public GameplayEffectDefinitionBuilder Modifier(
		GameplayAttribute attribute,
		GameplayModifierOperation operation,
		float magnitude)
	{
		_modifiers.Add(new GameplayEffectModifier(
			attribute,
			operation,
			magnitude));

		return this;
	}

	/// <summary>
	/// Grants tags for as long as the effect is active.
	/// </summary>
	/// <param name="tags">The tags to grant.</param>
	/// <remarks>
	/// Each active instance contributes one grant, whatever its stack
	/// count, matching Unreal's per-instance
	/// <c>UpdateTagMap(..., 1)</c>. Two effects granting the same tag
	/// therefore leave it present when one of them ends.
	/// </remarks>
	public GameplayEffectDefinitionBuilder GrantTags(GameplayTagSet tags)
	{
		ArgumentNullException.ThrowIfNull(tags);

		_grantedTags = tags;
		return this;
	}

	/// <summary>
	/// Grants tags for as long as the effect is active.
	/// </summary>
	/// <param name="tags">The tags to grant.</param>
	public GameplayEffectDefinitionBuilder GrantTags(params GameplayTag[] tags)
	{
		return GrantTags(GameplayTagSet.Create(tags));
	}

	/// <summary>
	/// Sets the tags the target must satisfy for the effect to apply.
	/// </summary>
	/// <param name="requirements">The requirements.</param>
	public GameplayEffectDefinitionBuilder RequireToApply(
		GameplayTagRequirements requirements)
	{
		ArgumentNullException.ThrowIfNull(requirements);

		_applicationRequirements = requirements;
		return this;
	}

	/// <summary>
	/// Sets the tags the target must satisfy for the effect to stay
	/// active.
	/// </summary>
	/// <param name="requirements">The requirements.</param>
	/// <remarks>
	/// An active effect that stops satisfying them is inhibited, not
	/// removed: its modifiers and granted tags are withdrawn and
	/// restored when the requirements hold again. Use
	/// <see cref="RemoveWhen"/> for a hard removal.
	/// </remarks>
	public GameplayEffectDefinitionBuilder RequireWhileActive(
		GameplayTagRequirements requirements)
	{
		ArgumentNullException.ThrowIfNull(requirements);

		_ongoingRequirements = requirements;
		return this;
	}

	/// <summary>
	/// Sets the tags that remove an active effect as soon as they are
	/// present on the target.
	/// </summary>
	/// <param name="requirements">The removal requirements.</param>
	public GameplayEffectDefinitionBuilder RemoveWhen(
		GameplayTagRequirements requirements)
	{
		ArgumentNullException.ThrowIfNull(requirements);

		_removalRequirements = requirements;
		return this;
	}

	/// <summary>
	/// Configures how re-applications combine.
	/// </summary>
	/// <param name="type">The stacking type.</param>
	/// <param name="limit">
	/// The maximum stack count, or zero for no limit.
	/// </param>
	/// <param name="durationRefresh">
	/// What a stack application does to the duration.
	/// </param>
	/// <param name="periodReset">
	/// What a stack application does to the period.
	/// </param>
	/// <param name="expiration">
	/// What reaching the end of the duration does to the stack.
	/// </param>
	public GameplayEffectDefinitionBuilder Stacking(
		GameplayEffectStackingType type,
		int limit = 0,
		GameplayEffectStackingDurationPolicy durationRefresh =
			GameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication,
		GameplayEffectStackingPeriodPolicy periodReset =
			GameplayEffectStackingPeriodPolicy.ResetOnSuccessfulApplication,
		GameplayEffectStackingExpirationPolicy expiration =
			GameplayEffectStackingExpirationPolicy.ClearEntireStack)
	{
		_stackingType = type;
		_stackLimit = limit;
		_stackDurationRefreshPolicy = durationRefresh;
		_stackPeriodResetPolicy = periodReset;
		_stackExpirationPolicy = expiration;

		return this;
	}

	/// <summary>
	/// Sets whether modifier magnitudes scale with the stack count.
	/// </summary>
	/// <param name="value">
	/// <see langword="true"/> to scale, which is the default.
	/// </param>
	public GameplayEffectDefinitionBuilder FactorInStackCount(bool value)
	{
		_factorInStackCount = value;
		return this;
	}

	/// <summary>
	/// Sets whether a periodic effect executes once at application.
	/// </summary>
	/// <param name="value">
	/// <see langword="true"/> to execute immediately, which is the
	/// default, matching Unreal's
	/// <c>bExecutePeriodicEffectOnApplication</c>.
	/// </param>
	public GameplayEffectDefinitionBuilder ExecuteOnApplication(bool value)
	{
		_executePeriodicOnApplication = value;
		return this;
	}

	/// <summary>
	/// Sets what happens to a periodic effect's schedule when
	/// inhibition ends.
	/// </summary>
	/// <param name="policy">The policy.</param>
	public GameplayEffectDefinitionBuilder PeriodicInhibition(
		GameplayEffectPeriodInhibitionPolicy policy)
	{
		_periodicInhibitionPolicy = policy;
		return this;
	}

	/// <summary>
	/// Validates and creates the definition.
	/// </summary>
	/// <returns>The immutable definition.</returns>
	/// <exception cref="ArgumentException">
	/// A value is missing or out of range.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// A modifier names an unregistered attribute.
	/// </exception>
	public GameplayEffectDefinition Build()
	{
		if (string.IsNullOrWhiteSpace(_name))
		{
			throw new ArgumentException(
				"A gameplay effect needs a non-empty diagnostic name.",
				nameof(_name));
		}

		if (_period < 0f || !float.IsFinite(_period))
		{
			throw new ArgumentException(
				$"The period of '{_name}' must be finite and not " +
				"negative. Use zero for a non-periodic effect.",
				nameof(_period));
		}

		switch (_durationType)
		{
			case GameplayEffectDurationType.HasDuration:
				if (!(_duration > 0f) || !float.IsFinite(_duration))
				{
					throw new ArgumentException(
						$"The duration of '{_name}' must be finite and " +
						"greater than zero. Use Infinite() for an " +
						"effect that never expires.",
						nameof(_duration));
				}

				break;

			case GameplayEffectDurationType.Instant:
				if (_period != 0f)
				{
					throw new ArgumentException(
						$"The instant effect '{_name}' cannot have a " +
						"period. An instant effect is never tracked, so " +
						"a period would never fire; use HasDuration or " +
						"Infinite instead.");
				}

				break;
		}

		if (_stackingType == GameplayEffectStackingType.None
			&& _stackLimit != 0)
		{
			throw new ArgumentException(
				$"The effect '{_name}' sets a stack limit without a " +
				"stacking type, so the limit would be ignored. Choose " +
				"AggregateBySource or AggregateByTarget, or remove the " +
				"limit.",
				nameof(_stackLimit));
		}

		if (_stackLimit < 0)
		{
			throw new ArgumentException(
				$"The stack limit of '{_name}' must not be negative.",
				nameof(_stackLimit));
		}

		if (!_grantedTags.IsEmpty
			&& !_ongoingRequirements.Ignored.IsEmpty
			&& _grantedTags.HasAny(_ongoingRequirements.Ignored))
		{
			// Such an effect withdraws its own grant as soon as it
			// becomes active, which satisfies the requirement again, so
			// the instance would oscillate between active and inhibited
			// forever. Reject the definition instead of paying for a
			// runtime guard on every tag change.
			throw new InvalidOperationException(
				$"The effect '{_name}' grants a tag that it also " +
				"forbids while active. Its own grant would immediately " +
				"inhibit it, which would remove the grant again; the " +
				"state could never settle. Remove the tag from the " +
				"ongoing requirements or stop granting it.");
		}

		GameplayEffectModifier[] modifiers = [.. _modifiers];

		for (int index = 0; index < modifiers.Length; index++)
		{
			GameplayEffectModifier modifier = modifiers[index];

			if (!modifier.Attribute.IsValid
				|| !GameplayAttributeRegistry.Shared.TryGetDefinition(
					modifier.Attribute,
					out _))
			{
				throw new InvalidOperationException(
					$"Modifier {index} of '{_name}' targets an " +
					"unregistered attribute. Register attributes " +
					"before defining effects that change them.");
			}

			if ((uint)modifier.Operation
				> (uint)GameplayModifierOperation.AddFinal)
			{
				throw new ArgumentException(
					$"Modifier {index} of '{_name}' uses an undefined " +
					"operation.",
					nameof(_modifiers));
			}

			if (!float.IsFinite(modifier.Magnitude))
			{
				throw new ArgumentException(
					$"Modifier {index} of '{_name}' has a magnitude " +
					"that is not finite.",
					nameof(_modifiers));
			}
		}

		return new GameplayEffectDefinition(
			_name,
			_durationType,
			_duration,
			_period,
			modifiers,
			_grantedTags,
			_applicationRequirements,
			_ongoingRequirements,
			_removalRequirements,
			_stackingType,
			_stackLimit,
			_stackDurationRefreshPolicy,
			_stackPeriodResetPolicy,
			_stackExpirationPolicy,
			_periodicInhibitionPolicy,
			_factorInStackCount,
			_executePeriodicOnApplication);
	}
}
