using Volt.Gameplay.Abilities;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tests.Attributes;
using Volt.Gameplay.Tests.Tags;

namespace Volt.Gameplay.Tests.Abilities;

public sealed class GameplayAbilityDefinitionTests
{
	[Fact]
	public void Definition_DefaultsToEndingOnActivation()
	{
		GameplayAbilityDefinition definition =
			GameplayAbilityDefinition.Create("Test.Defaults").Build();

		Assert.Equal("Test.Defaults", definition.Name);
		Assert.True(definition.EndsOnActivate);
		Assert.False(definition.CanRetrigger);
		Assert.Null(definition.CostEffect);
		Assert.Null(definition.CooldownEffect);
		Assert.True(definition.CooldownTags.IsEmpty);
		Assert.True(definition.AssetTags.IsEmpty);
		Assert.True(definition.ActivationRequirements.IsEmpty);
		Assert.True(definition.TargetRequirements.IsEmpty);
		Assert.True(definition.CancelAbilitiesWithTags.IsEmpty);
		Assert.True(definition.BlockAbilitiesWithTags.IsEmpty);
		Assert.True(definition.CancelOnTags.IsEmpty);
		Assert.Equal("GameplayAbility(Test.Defaults)", definition.ToString());
	}

	[Fact]
	public void Name_MustNotBeEmpty()
	{
		Assert.Throws<ArgumentException>(() =>
			GameplayAbilityDefinition.Create("  ").Build());
	}

	[Fact]
	public void Cost_MustBeInstant()
	{
		GameplayEffectDefinition durationCost = GameplayEffectDefinition
			.Create("Test.Ability.DurationCost")
			.HasDuration(1f)
			.Modifier(
				AttributeSchema.Unbounded,
				GameplayModifierOperation.AddBase,
				-1f)
			.Build();

		Assert.Throws<ArgumentException>(() =>
			GameplayAbilityDefinition
				.Create("Test.Ability.DurationCostAbility")
				.Cost(durationCost)
				.Build());
	}

	[Fact]
	public void Cooldown_MustNotBeInstantAndMustGrantTags()
	{
		GameplayEffectDefinition instant = GameplayEffectDefinition
			.Create("Test.Ability.InstantCooldown")
			.Instant()
			.GrantTags(AbilitySchema.CooldownJump)
			.Build();

		Assert.Throws<ArgumentException>(() =>
			GameplayAbilityDefinition
				.Create("Test.Ability.InstantCooldownAbility")
				.Cooldown(instant)
				.Build());

		GameplayEffectDefinition tagless = GameplayEffectDefinition
			.Create("Test.Ability.TaglessCooldown")
			.HasDuration(1f)
			.Build();

		Assert.Throws<InvalidOperationException>(() =>
			GameplayAbilityDefinition
				.Create("Test.Ability.TaglessCooldownAbility")
				.Cooldown(tagless)
				.Build());
	}

	[Fact]
	public void Cooldown_CannotGrantACancelTag()
	{
		// The cooldown applies during activation, so this ability
		// would cancel the instance it just started.
		Assert.Throws<InvalidOperationException>(() =>
			GameplayAbilityDefinition
				.Create("Test.Ability.SelfCancellingCooldown")
				.Cooldown(AbilitySchema.DashCooldown)
				.CancelOnTags(AbilitySchema.CooldownDash)
				.Build());
	}

	[Fact]
	public void Cooldown_CannotGrantAChildOfACancelTag()
	{
		Assert.Throws<InvalidOperationException>(() =>
			GameplayAbilityDefinition
				.Create("Test.Ability.SelfCancellingCooldownChild")
				.Cooldown(AbilitySchema.DashCooldown)
				.CancelOnTags(AbilitySchema.Cooldown)
				.Build());
	}

	[Fact]
	public void Definition_ExposesEveryValue()
	{
		GameplayTagSet assetTags =
			GameplayTagSet.Create(TagSchema.AbilityDash);

		GameplayTagRequirements requirements =
			GameplayTagRequirements.Require(
				GameplayTagSet.Create(TagSchema.EffectBuff));

		GameplayTagSet cancelTags =
			GameplayTagSet.Create(TagSchema.StatusStunned);

		GameplayAbilityDefinition definition = GameplayAbilityDefinition
			.Create("Test.Exposed")
			.AssetTags(assetTags)
			.RequireToActivate(requirements)
			.RequireOnTarget(requirements)
			.Cost(AbilitySchema.CostTen)
			.Cooldown(AbilitySchema.DashCooldown)
			.CancelAbilitiesWithTags(TagSchema.Ability)
			.BlockAbilitiesWithTags(TagSchema.Ability)
			.CancelOnTags(cancelTags)
			.Retrigger(true)
			.EndOnActivate(false)
			.Build();

		Assert.Same(assetTags, definition.AssetTags);
		Assert.Same(requirements, definition.ActivationRequirements);
		Assert.Same(requirements, definition.TargetRequirements);
		Assert.Same(AbilitySchema.CostTen, definition.CostEffect);
		Assert.Same(AbilitySchema.DashCooldown, definition.CooldownEffect);
		Assert.True(definition.CooldownTags.HasTagExact(AbilitySchema.CooldownDash));
		Assert.True(definition.CancelAbilitiesWithTags.HasTagExact(TagSchema.Ability));
		Assert.True(definition.BlockAbilitiesWithTags.HasTagExact(TagSchema.Ability));
		Assert.True(definition.CancelOnTags.HasTagExact(TagSchema.StatusStunned));
		Assert.True(definition.CanRetrigger);
		Assert.False(definition.EndsOnActivate);
	}

	[Fact]
	public void Builder_RejectsNullArguments()
	{
		GameplayAbilityDefinitionBuilder builder =
			GameplayAbilityDefinition.Create("Test.NullArguments");

		Assert.Throws<ArgumentNullException>(() =>
			builder.AssetTags((GameplayTagSet)null!));
		Assert.Throws<ArgumentNullException>(() =>
			builder.RequireToActivate(null!));
		Assert.Throws<ArgumentNullException>(() => builder.RequireOnTarget(null!));
		Assert.Throws<ArgumentNullException>(() => builder.Cost(null!));
		Assert.Throws<ArgumentNullException>(() => builder.Cooldown(null!));
		Assert.Throws<ArgumentNullException>(() =>
			builder.CancelAbilitiesWithTags((GameplayTagSet)null!));
		Assert.Throws<ArgumentNullException>(() =>
			builder.BlockAbilitiesWithTags((GameplayTagSet)null!));
		Assert.Throws<ArgumentNullException>(() =>
			builder.CancelOnTags((GameplayTagSet)null!));
	}
}
