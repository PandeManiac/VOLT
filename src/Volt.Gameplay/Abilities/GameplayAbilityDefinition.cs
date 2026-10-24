using Volt.Gameplay.Effects;
using Volt.Gameplay.Tags;

namespace Volt.Gameplay.Abilities;

/// <summary>
/// An immutable description of an ability: when it may activate, what
/// it costs, what it puts on cooldown, and which tags it cancels,
/// blocks, or is cancelled by.
/// </summary>
/// <remarks>
/// <para>
/// Definitions are shared, not per-instance state. Granting an ability
/// to a thousand entities is one definition plus a thousand small
/// grants that hold the owner, the level, and the input binding. Create
/// definitions during initialization with
/// <see cref="GameplayAbilityDefinitionBuilder"/> and hold them for the
/// process lifetime.
/// </para>
/// <para>
/// Unreal's <c>UGameplayAbility</c> is a class that also owns the
/// activation logic and its tasks. VOLT keeps the definition purely
/// declarative: the runtime raises a notification when an ability
/// activates, and game code reacts to it. Costs and cooldowns are
/// gameplay effects, exactly as in Unreal, so they reuse the effect
/// system's scheduling, stacking, and tag bookkeeping.
/// </para>
/// </remarks>
public sealed class GameplayAbilityDefinition
{
	internal GameplayAbilityDefinition(
		string name,
		GameplayTagSet assetTags,
		GameplayTagRequirements activationRequirements,
		GameplayTagRequirements targetRequirements,
		GameplayEffectDefinition? costEffect,
		GameplayEffectDefinition? cooldownEffect,
		GameplayTagSet cancelAbilitiesWithTags,
		GameplayTagSet blockAbilitiesWithTags,
		GameplayTagSet cancelOnTags,
		bool canRetrigger,
		bool endsOnActivate,
		GameplayAbilityPlan? plan)
	{
		Name = name;
		AssetTags = assetTags;
		ActivationRequirements = activationRequirements;
		TargetRequirements = targetRequirements;
		CostEffect = costEffect;
		CooldownEffect = cooldownEffect;
		CancelAbilitiesWithTags = cancelAbilitiesWithTags;
		BlockAbilitiesWithTags = blockAbilitiesWithTags;
		CancelOnTags = cancelOnTags;
		CanRetrigger = canRetrigger;
		EndsOnActivate = endsOnActivate;
		Plan = plan;
	}

	/// <summary>
	/// Starts building a new ability definition.
	/// </summary>
	/// <param name="name">
	/// The diagnostic name, such as <c>Ability.Dash</c>.
	/// </param>
	/// <returns>A builder for the definition.</returns>
	public static GameplayAbilityDefinitionBuilder Create(string name)
	{
		return new GameplayAbilityDefinitionBuilder(name);
	}

	/// <summary>
	/// Gets the diagnostic name of this ability.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// Gets the tags that identify the ability.
	/// </summary>
	/// <remarks>
	/// Asset tags are what another ability's
	/// <see cref="BlockAbilitiesWithTags"/> and
	/// <see cref="CancelAbilitiesWithTags"/> match against, mirroring
	/// Unreal's <c>GetAssetTags()</c>. They are not the tags the owner
	/// must have to activate; those are
	/// <see cref="ActivationRequirements"/>.
	/// </remarks>
	public GameplayTagSet AssetTags { get; }

	/// <summary>
	/// Gets the tags the owner must satisfy to activate.
	/// </summary>
	public GameplayTagRequirements ActivationRequirements { get; }

	/// <summary>
	/// Gets the tags the activation target must satisfy.
	/// </summary>
	/// <remarks>
	/// An activation without a living target fails these requirements
	/// whenever they are not empty.
	/// </remarks>
	public GameplayTagRequirements TargetRequirements { get; }

	/// <summary>
	/// Gets the instant effect applied to the owner when the ability
	/// activates, or <see langword="null"/> for a free ability.
	/// </summary>
	public GameplayEffectDefinition? CostEffect { get; }

	/// <summary>
	/// Gets the effect applied to the owner when the ability activates,
	/// or <see langword="null"/> for an ability without a cooldown.
	/// </summary>
	public GameplayEffectDefinition? CooldownEffect { get; }

	/// <summary>
	/// Gets the tags granted by the cooldown effect.
	/// </summary>
	/// <remarks>
	/// An ability is on cooldown while the owner has any of these
	/// tags, so an external effect that grants the same tag blocks the
	/// ability too. This is Unreal's rule, where
	/// <c>GetCooldownTags()</c> is read from the cooldown effect's
	/// granted tags.
	/// </remarks>
	public GameplayTagSet CooldownTags =>
		CooldownEffect?.GrantedTags ?? GameplayTagSet.Empty;

	/// <summary>
	/// Gets the asset tags of abilities this ability cancels when it
	/// activates.
	/// </summary>
	public GameplayTagSet CancelAbilitiesWithTags { get; }

	/// <summary>
	/// Gets the asset tags this ability blocks while it is active.
	/// </summary>
	public GameplayTagSet BlockAbilitiesWithTags { get; }

	/// <summary>
	/// Gets the tags that cancel an active instance of this ability.
	/// </summary>
	/// <remarks>
	/// Cancellation is evaluated when the owner's tags change. A tag
	/// that was already present before activation does not cancel the
	/// ability; use <see cref="ActivationRequirements"/> to forbid
	/// that case. Unreal has no equivalent on the ability itself: game
	/// code or a gameplay effect component calls
	/// <c>CancelAbilities</c> explicitly.
	/// </remarks>
	public GameplayTagSet CancelOnTags { get; }

	/// <summary>
	/// Gets whether activating an active instance ends it and starts a
	/// new one instead of being rejected.
	/// </summary>
	public bool CanRetrigger { get; }

	/// <summary>
	/// Gets whether activation ends as soon as it has been committed.
	/// </summary>
	/// <remarks>
	/// A definition defaults to ending on activation, because a data
	/// description has no tasks or input of its own to keep it alive.
	/// Call <see cref="GameplayAbilityDefinitionBuilder.EndOnActivate"/>
	/// with <see langword="false"/> for an ability that remains active
	/// until it is ended or cancelled.
	/// </remarks>
	public bool EndsOnActivate { get; }

	/// <summary>
	/// Gets the authored task sequence that runs while the ability is
	/// active, or <see langword="null"/> for an ability without a plan.
	/// </summary>
	/// <remarks>
	/// The plan shares the ability task runtime. It is one immutable
	/// array per definition; each activation stores only its cursor.
	/// An ability with a plan does not end on activation, because the
	/// plan is what decides when it ends.
	/// </remarks>
	public GameplayAbilityPlan? Plan { get; }

	/// <inheritdoc />
	public override string ToString()
	{
		return $"GameplayAbility({Name})";
	}
}
