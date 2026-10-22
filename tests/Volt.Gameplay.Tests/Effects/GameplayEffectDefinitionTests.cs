using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tests.Attributes;
using Volt.Gameplay.Tests.Tags;

namespace Volt.Gameplay.Tests.Effects;

public sealed class GameplayEffectDefinitionTests
{
	[Fact]
	public void Definition_DefaultsToInstantWithoutStacking()
	{
		GameplayEffectDefinition definition = GameplayEffectDefinition
			.Create("Test.Defaults")
			.Build();

		Assert.Equal("Test.Defaults", definition.Name);
		Assert.Equal(GameplayEffectDurationType.Instant, definition.DurationType);
		Assert.True(definition.IsInstant);
		Assert.False(definition.IsPeriodic);
		Assert.Equal(0, definition.ModifierCount);
		Assert.True(definition.GrantedTags.IsEmpty);
		Assert.True(definition.ApplicationRequirements.IsEmpty);
		Assert.True(definition.OngoingRequirements.IsEmpty);
		Assert.True(definition.RemovalRequirements.IsEmpty);
		Assert.Equal(GameplayEffectStackingType.None, definition.StackingType);
		Assert.Equal(0, definition.StackLimit);
		Assert.True(definition.FactorInStackCount);
		Assert.True(definition.ExecutePeriodicOnApplication);
		Assert.Equal(
			GameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication,
			definition.StackDurationRefreshPolicy);
		Assert.Equal(
			GameplayEffectStackingExpirationPolicy.ClearEntireStack,
			definition.StackExpirationPolicy);
		Assert.Equal(
			GameplayEffectPeriodInhibitionPolicy.NeverReset,
			definition.PeriodicInhibitionPolicy);
	}

	[Fact]
	public void InstantEffect_CannotHaveAPeriod()
	{
		// Unreal silently ignores the period of an instant effect
		// because such a spec is never tracked. VOLT rejects the
		// combination instead of accepting a definition that cannot
		// do what it says.
		Assert.Throws<ArgumentException>(() =>
			GameplayEffectDefinition
				.Create("Test.InstantPeriod")
				.Instant()
				.Period(1f)
				.Build());
	}

	[Fact]
	public void Duration_MustBeFiniteAndPositive()
	{
		Assert.Throws<ArgumentException>(() =>
			GameplayEffectDefinition
				.Create("Test.ZeroDuration")
				.HasDuration(0f)
				.Build());

		Assert.Throws<ArgumentException>(() =>
			GameplayEffectDefinition
				.Create("Test.NegativeDuration")
				.HasDuration(-1f)
				.Build());

		Assert.Throws<ArgumentException>(() =>
			GameplayEffectDefinition
				.Create("Test.InfiniteDuration")
				.HasDuration(float.PositiveInfinity)
				.Build());
	}

	[Fact]
	public void Period_MustBeFiniteAndNotNegative()
	{
		Assert.Throws<ArgumentException>(() =>
			GameplayEffectDefinition
				.Create("Test.NegativePeriod")
				.HasDuration(1f)
				.Period(-0.5f)
				.Build());

		Assert.Throws<ArgumentException>(() =>
			GameplayEffectDefinition
				.Create("Test.NaNPeriod")
				.HasDuration(1f)
				.Period(float.NaN)
				.Build());
	}

	[Fact]
	public void StackLimit_RequiresAStackingType()
	{
		Assert.Throws<ArgumentException>(() =>
			GameplayEffectDefinition
				.Create("Test.OrphanLimit")
				.HasDuration(1f)
				.Stacking(GameplayEffectStackingType.None, 3)
				.Build());
	}

	[Fact]
	public void StackLimit_CannotBeNegative()
	{
		Assert.Throws<ArgumentException>(() =>
			GameplayEffectDefinition
				.Create("Test.NegativeLimit")
				.HasDuration(1f)
				.Stacking(GameplayEffectStackingType.AggregateByTarget, -1)
				.Build());
	}

	[Fact]
	public void Modifier_RequiresARegisteredAttribute()
	{
		GameplayAttribute unregistered = default;

		Assert.Throws<InvalidOperationException>(() =>
			GameplayEffectDefinition
				.Create("Test.UnregisteredAttribute")
				.Instant()
				.Modifier(
					unregistered,
					GameplayModifierOperation.AddBase,
					1f)
				.Build());
	}

	[Fact]
	public void Modifier_RejectsNonFiniteMagnitudes()
	{
		Assert.Throws<ArgumentException>(() =>
			GameplayEffectDefinition
				.Create("Test.NaNMagnitude")
				.Instant()
				.Modifier(
					AttributeSchema.Unbounded,
					GameplayModifierOperation.AddBase,
					float.NaN)
				.Build());

		Assert.Throws<ArgumentException>(() =>
			GameplayEffectDefinition
				.Create("Test.InfiniteMagnitude")
				.Instant()
				.Modifier(
					AttributeSchema.Unbounded,
					GameplayModifierOperation.AddBase,
					float.PositiveInfinity)
				.Build());
	}

	[Fact]
	public void Name_MustNotBeEmpty()
	{
		Assert.Throws<ArgumentException>(() =>
			GameplayEffectDefinition.Create("  ").Build());
	}

	[Fact]
	public void GrantedTags_CannotContradictTheOngoingRequirements()
	{
		// An effect that forbids a tag it grants would withdraw its
		// own grant forever. The definition is rejected instead of
		// letting the instance oscillate.
		Assert.Throws<InvalidOperationException>(() =>
			GameplayEffectDefinition
				.Create("Test.SelfDefeating")
				.Infinite()
				.GrantTags(TagSchema.StatusStunned)
				.RequireWhileActive(
					GameplayTagRequirements.Ignore(
						GameplayTagSet.Create(TagSchema.StatusStunned)))
				.Build());
	}

	[Fact]
	public void GrantedChildTag_ContradictsAnIgnoredParentRequirement()
	{
		// Granting Status.Stunned violates an ignored Status, so this
		// is the self-defeating direction.
		Assert.Throws<InvalidOperationException>(() =>
			GameplayEffectDefinition
				.Create("Test.SelfDefeatingChild")
				.Infinite()
				.GrantTags(TagSchema.StatusStunned)
				.RequireWhileActive(
					GameplayTagRequirements.Ignore(
						GameplayTagSet.Create(TagSchema.Status)))
				.Build());
	}

	[Fact]
	public void GrantedParentTag_DoesNotContradictAnIgnoredChild()
	{
		// Granting Status leaves Status.Stunned absent, so the ignored
		// requirement still holds and the effect can settle.
		GameplayEffectDefinition definition = GameplayEffectDefinition
			.Create("Test.GrantParentIgnoreChild")
			.Infinite()
			.GrantTags(TagSchema.Status)
			.RequireWhileActive(
				GameplayTagRequirements.Ignore(
					GameplayTagSet.Create(TagSchema.StatusStunned)))
			.Build();

		Assert.True(definition.GrantedTags.HasTagExact(TagSchema.Status));
	}

	[Fact]
	public void Requirements_MatchNonExactTags()
	{
		GameplayTagSet stunned =
			GameplayTagSet.Create(TagSchema.StatusStunned);

		GameplayTagRequirements requireStatus =
			GameplayTagRequirements.Require(
				GameplayTagSet.Create(TagSchema.Status));

		GameplayTagRequirements ignoreStatus =
			GameplayTagRequirements.Ignore(
				GameplayTagSet.Create(TagSchema.Status));

		Assert.True(requireStatus.IsSatisfiedBy(stunned));
		Assert.False(ignoreStatus.IsSatisfiedBy(stunned));
		Assert.True(requireStatus.IsSatisfiedBy(stunned.GetGameplayTagParents()));
		Assert.True(GameplayTagRequirements.Empty.IsSatisfiedBy(stunned));
	}

	[Fact]
	public void GrantedTags_AndModifiers_AreExposedImmutably()
	{
		GameplayEffectDefinition definition = GameplayEffectDefinition
			.Create("Test.Exposed")
			.HasDuration(3f)
			.GrantTags(TagSchema.EffectBuff)
			.Modifier(
				AttributeSchema.Unbounded,
				GameplayModifierOperation.AddBase,
				2f)
			.Build();

		Assert.Equal(1, definition.ModifierCount);
		Assert.Equal(
			AttributeSchema.Unbounded,
			definition.Modifiers[0].Attribute);
		Assert.Equal(2f, definition.Modifiers[0].Magnitude);
		Assert.True(definition.GrantedTags.HasTagExact(TagSchema.EffectBuff));
		Assert.Equal("GameplayEffect(Test.Exposed)", definition.ToString());
	}
}
