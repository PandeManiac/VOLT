using Volt.Gameplay.Effects;
using Volt.Gameplay.Tags;

namespace Volt.Gameplay.Abilities;

/// <summary>
/// Builds an immutable <see cref="GameplayAbilityDefinition"/>.
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
public sealed class GameplayAbilityDefinitionBuilder
{
	private readonly string _name;

	private GameplayTagSet _assetTags = GameplayTagSet.Empty;
	private GameplayTagRequirements _activationRequirements =
		GameplayTagRequirements.Empty;
	private GameplayTagRequirements _targetRequirements =
		GameplayTagRequirements.Empty;
	private GameplayEffectDefinition? _costEffect;
	private GameplayEffectDefinition? _cooldownEffect;
	private GameplayTagSet _cancelAbilitiesWithTags = GameplayTagSet.Empty;
	private GameplayTagSet _blockAbilitiesWithTags = GameplayTagSet.Empty;
	private GameplayTagSet _cancelOnTags = GameplayTagSet.Empty;
	private bool _canRetrigger;
	private bool _endsOnActivate = true;

	internal GameplayAbilityDefinitionBuilder(string name)
	{
		_name = name;
	}

	/// <summary>
	/// Sets the tags that identify the ability.
	/// </summary>
	/// <param name="tags">The asset tags.</param>
	/// <remarks>
	/// Another ability's block and cancel tags match these, so an
	/// ability without asset tags can never be blocked or cancelled by
	/// tag.
	/// </remarks>
	public GameplayAbilityDefinitionBuilder AssetTags(GameplayTagSet tags)
	{
		ArgumentNullException.ThrowIfNull(tags);

		_assetTags = tags;
		return this;
	}

	/// <summary>
	/// Sets the tags that identify the ability.
	/// </summary>
	/// <param name="tags">The asset tags.</param>
	public GameplayAbilityDefinitionBuilder AssetTags(params GameplayTag[] tags)
	{
		return AssetTags(GameplayTagSet.Create(tags));
	}

	/// <summary>
	/// Sets the tags the owner must satisfy to activate.
	/// </summary>
	/// <param name="requirements">The requirements.</param>
	/// <remarks>
	/// This is the ability's only gate on the owner's state, so a
	/// "cannot dash while stunned" rule belongs here rather than in
	/// <see cref="CancelOnTags"/>.
	/// </remarks>
	public GameplayAbilityDefinitionBuilder RequireToActivate(
		GameplayTagRequirements requirements)
	{
		ArgumentNullException.ThrowIfNull(requirements);

		_activationRequirements = requirements;
		return this;
	}

	/// <summary>
	/// Sets the tags the activation target must satisfy.
	/// </summary>
	/// <param name="requirements">The requirements.</param>
	public GameplayAbilityDefinitionBuilder RequireOnTarget(
		GameplayTagRequirements requirements)
	{
		ArgumentNullException.ThrowIfNull(requirements);

		_targetRequirements = requirements;
		return this;
	}

	/// <summary>
	/// Sets the effect applied to the owner on activation.
	/// </summary>
	/// <param name="effect">The cost effect.</param>
	/// <remarks>
	/// The effect must be instant, because a cost is paid once and is
	/// not tracked. Its additive modifiers are also checked before
	/// activation: a cost that would drive an attribute below zero
	/// rejects the activation without applying anything.
	/// </remarks>
	public GameplayAbilityDefinitionBuilder Cost(GameplayEffectDefinition effect)
	{
		ArgumentNullException.ThrowIfNull(effect);

		_costEffect = effect;
		return this;
	}

	/// <summary>
	/// Sets the effect applied to the owner on activation that puts the
	/// ability on cooldown.
	/// </summary>
	/// <param name="effect">The cooldown effect.</param>
	/// <remarks>
	/// The effect must not be instant and must grant at least one tag,
	/// because Unreal's cooldown check is "the owner has one of the
	/// cooldown effect's granted tags". An instant effect is never
	/// tracked and grants nothing, so it could never block anything.
	/// </remarks>
	public GameplayAbilityDefinitionBuilder Cooldown(
		GameplayEffectDefinition effect)
	{
		ArgumentNullException.ThrowIfNull(effect);

		_cooldownEffect = effect;
		return this;
	}

	/// <summary>
	/// Sets the asset tags of abilities this ability cancels when it
	/// activates.
	/// </summary>
	/// <param name="tags">The tags to cancel.</param>
	public GameplayAbilityDefinitionBuilder CancelAbilitiesWithTags(
		GameplayTagSet tags)
	{
		ArgumentNullException.ThrowIfNull(tags);

		_cancelAbilitiesWithTags = tags;
		return this;
	}

	/// <summary>
	/// Sets the asset tags of abilities this ability cancels when it
	/// activates.
	/// </summary>
	/// <param name="tags">The tags to cancel.</param>
	public GameplayAbilityDefinitionBuilder CancelAbilitiesWithTags(
		params GameplayTag[] tags)
	{
		return CancelAbilitiesWithTags(GameplayTagSet.Create(tags));
	}

	/// <summary>
	/// Sets the asset tags this ability blocks while it is active.
	/// </summary>
	/// <param name="tags">The tags to block.</param>
	public GameplayAbilityDefinitionBuilder BlockAbilitiesWithTags(
		GameplayTagSet tags)
	{
		ArgumentNullException.ThrowIfNull(tags);

		_blockAbilitiesWithTags = tags;
		return this;
	}

	/// <summary>
	/// Sets the asset tags this ability blocks while it is active.
	/// </summary>
	/// <param name="tags">The tags to block.</param>
	public GameplayAbilityDefinitionBuilder BlockAbilitiesWithTags(
		params GameplayTag[] tags)
	{
		return BlockAbilitiesWithTags(GameplayTagSet.Create(tags));
	}

	/// <summary>
	/// Sets the tags that cancel an active instance of this ability.
	/// </summary>
	/// <param name="tags">The cancelling tags.</param>
	/// <remarks>
	/// A cooldown effect cannot grant one of these tags: it applies
	/// during activation, so the ability would cancel itself
	/// immediately after starting. <see cref="Build"/> rejects that
	/// combination.
	/// </remarks>
	public GameplayAbilityDefinitionBuilder CancelOnTags(GameplayTagSet tags)
	{
		ArgumentNullException.ThrowIfNull(tags);

		_cancelOnTags = tags;
		return this;
	}

	/// <summary>
	/// Sets the tags that cancel an active instance of this ability.
	/// </summary>
	/// <param name="tags">The cancelling tags.</param>
	public GameplayAbilityDefinitionBuilder CancelOnTags(
		params GameplayTag[] tags)
	{
		return CancelOnTags(GameplayTagSet.Create(tags));
	}

	/// <summary>
	/// Sets whether activating an active instance ends it and starts a
	/// new one.
	/// </summary>
	/// <param name="value">
	/// <see langword="true"/> to retrigger; the default is
	/// <see langword="false"/>, which rejects the activation with
	/// <see cref="GameplayAbilityActivationStatus.AlreadyActive"/>.
	/// </param>
	public GameplayAbilityDefinitionBuilder Retrigger(bool value)
	{
		_canRetrigger = value;
		return this;
	}

	/// <summary>
	/// Sets whether activation ends as soon as it is committed.
	/// </summary>
	/// <param name="value">
	/// <see langword="true"/> to end immediately, which is the
	/// default; <see langword="false"/> to keep the ability active
	/// until it is ended or cancelled.
	/// </param>
	public GameplayAbilityDefinitionBuilder EndOnActivate(bool value)
	{
		_endsOnActivate = value;
		return this;
	}

	/// <summary>
	/// Validates and creates the definition.
	/// </summary>
	/// <returns>The immutable definition.</returns>
	/// <exception cref="ArgumentException">
	/// A value is missing or has the wrong duration.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The configuration contradicts itself, such as a cooldown effect
	/// that grants no tags.
	/// </exception>
	public GameplayAbilityDefinition Build()
	{
		if (string.IsNullOrWhiteSpace(_name))
		{
			throw new ArgumentException(
				"A gameplay ability needs a non-empty diagnostic name.",
				nameof(_name));
		}

		if (_costEffect is not null && !_costEffect.IsInstant)
		{
			throw new ArgumentException(
				$"The cost effect of '{_name}' must be instant. A " +
				"cost is paid once and is never tracked; use an " +
				"instant effect without a period.",
				nameof(_costEffect));
		}

		if (_cooldownEffect is not null)
		{
			if (_cooldownEffect.IsInstant)
			{
				throw new ArgumentException(
					$"The cooldown effect of '{_name}' must not be " +
					"instant. Unreal's cooldown check reads the tags " +
					"granted by the cooldown effect, and an instant " +
					"effect is never tracked, so it could never block " +
					"the ability again.",
					nameof(_cooldownEffect));
			}

			if (_cooldownEffect.GrantedTags.IsEmpty)
			{
				throw new InvalidOperationException(
					$"The cooldown effect of '{_name}' grants no " +
					"tags. An ability is on cooldown while the owner " +
					"has one of those tags, so a tagless cooldown " +
					"effect would never block a subsequent " +
					"activation.");
			}

			if (_cooldownEffect.GrantedTags.HasAny(_cancelOnTags))
			{
				// The cooldown applies during activation, so one of
				// its grants would immediately cancel the instance
				// that just started.
				throw new InvalidOperationException(
					$"The ability '{_name}' is cancelled by a tag its " +
					"own cooldown effect grants. Applying the cooldown " +
					"would cancel the activation that just started. " +
					"Remove the tag from the cancel tags or from the " +
					"cooldown effect's granted tags.");
			}
		}

		return new GameplayAbilityDefinition(
			_name,
			_assetTags,
			_activationRequirements,
			_targetRequirements,
			_costEffect,
			_cooldownEffect,
			_cancelAbilitiesWithTags,
			_blockAbilitiesWithTags,
			_cancelOnTags,
			_canRetrigger,
			_endsOnActivate);
	}
}
