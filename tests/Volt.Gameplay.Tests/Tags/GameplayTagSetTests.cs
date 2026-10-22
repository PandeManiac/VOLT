using Volt.Gameplay.Tags;

namespace Volt.Gameplay.Tests.Tags;

public sealed class GameplayTagSetTests
{
	[Fact]
	public void Empty_ContainsNothing_ButSatisfiesEmptyRequirements()
	{
		GameplayTagSet empty = GameplayTagSet.Empty;

		Assert.True(empty.IsEmpty);
		Assert.Equal(0, empty.Count);
		Assert.True(empty.HasAll(GameplayTagSet.Empty));
		Assert.True(empty.HasAllExact(GameplayTagSet.Empty));
		Assert.False(empty.HasAny(GameplayTagSet.Empty));
		Assert.False(empty.HasAnyExact(GameplayTagSet.Empty));
		Assert.False(empty.HasTag(TagSchema.Effect));
	}

	[Fact]
	public void Create_DeduplicatesAndSortsByTagIdentity()
	{
		GameplayTagSet set = GameplayTagSet.Create(
			TagSchema.EffectDamageType2,
			TagSchema.EffectDamageType1,
			TagSchema.EffectDamageType2);

		Assert.Equal(2, set.Count);
		Assert.Equal(TagSchema.EffectDamageType1, set[0]);
		Assert.Equal(TagSchema.EffectDamageType2, set[1]);
	}

	[Fact]
	public void Create_RejectsInvalidTag()
	{
		Assert.Throws<ArgumentException>(
			() => GameplayTagSet.Create(TagSchema.Effect, GameplayTag.None));
	}

	[Fact]
	public void Create_DoesNotAliasTheSourceArray()
	{
		GameplayTag[] source = [TagSchema.EffectDamageType1];

		GameplayTagSet set = GameplayTagSet.Create(source);

		source[0] = TagSchema.OtherLeaf;

		Assert.Equal(1, set.Count);
		Assert.True(set.HasTagExact(TagSchema.EffectDamageType1));
	}

	[Fact]
	public void Create_CachesAncestorsWithoutMakingThemExplicit()
	{
		GameplayTagSet set = GameplayTagSet.Create(TagSchema.EffectDamageType1);

		Assert.Equal(1, set.Count);
		Assert.True(set.HasTag(TagSchema.EffectDamage));
		Assert.True(set.HasTag(TagSchema.Effect));
		Assert.False(set.HasTagExact(TagSchema.EffectDamage));
		Assert.False(set.HasTagExact(TagSchema.Effect));
	}

	[Fact]
	public void HasTag_InvalidTag_IsFalse()
	{
		GameplayTagSet set = GameplayTagSet.Create(TagSchema.Effect);

		Assert.False(set.HasTag(GameplayTag.None));
		Assert.False(set.HasTagExact(GameplayTag.None));
	}

	[Fact]
	public void HasAny_ExactVariant_DoesNotFollowAncestry()
	{
		GameplayTagSet set = GameplayTagSet.Create(TagSchema.EffectDamageType1);
		GameplayTagSet requirement = GameplayTagSet.Create(TagSchema.EffectDamage);

		Assert.True(set.HasAny(requirement));
		Assert.False(set.HasAnyExact(requirement));
	}

	[Fact]
	public void HasAll_WithUnrelatedTag_IsFalse()
	{
		GameplayTagSet set = GameplayTagSet.Create(TagSchema.EffectDamageType1);

		Assert.False(set.HasAll(GameplayTagSet.Create(
			TagSchema.EffectDamage,
			TagSchema.Other)));

		Assert.True(set.HasAll(GameplayTagSet.Create(
			TagSchema.EffectDamage)));
	}

	[Fact]
	public void Equality_IsOrderIndependent_AndHashStable()
	{
		GameplayTagSet first = GameplayTagSet.Create(
			TagSchema.EffectDamageType1,
			TagSchema.CueBurning);

		GameplayTagSet second = GameplayTagSet.Create(
			TagSchema.CueBurning,
			TagSchema.EffectDamageType1);

		Assert.Equal(first, second);
		Assert.True(first == second);
		Assert.Equal(first.GetHashCode(), second.GetHashCode());

		GameplayTagSet third = GameplayTagSet.Create(
			TagSchema.EffectDamageType1,
			TagSchema.Cue);

		Assert.NotEqual(first, third);
	}

	[Fact]
	public void Equality_DistinguishesExplicitParentFromInheritedParent()
	{
		GameplayTagSet inherited = GameplayTagSet.Create(
			TagSchema.EffectDamageType1);

		GameplayTagSet explicitBoth = GameplayTagSet.Create(
			TagSchema.EffectDamageType1,
			TagSchema.EffectDamage);

		Assert.NotEqual(inherited, explicitBoth);

		Assert.True(inherited.HasTag(TagSchema.EffectDamage));
		Assert.True(explicitBoth.HasTag(TagSchema.EffectDamage));
	}

	[Fact]
	public void Filter_KeepsDescendantsOfTheFilter()
	{
		GameplayTagSet set = GameplayTagSet.Create(
			TagSchema.EffectDamageType1,
			TagSchema.CueBurning);

		GameplayTagSet filtered = set.Filter(
			GameplayTagSet.Create(TagSchema.EffectDamage));

		Assert.Equal(1, filtered.Count);
		Assert.True(filtered.HasTagExact(TagSchema.EffectDamageType1));
		Assert.False(filtered.HasTagExact(TagSchema.CueBurning));
	}

	[Fact]
	public void Filter_DoesNotKeepAncestorsOfTheFilter()
	{
		GameplayTagSet set = GameplayTagSet.Create(TagSchema.EffectDamage);

		GameplayTagSet filtered = set.Filter(
			GameplayTagSet.Create(TagSchema.EffectDamageType1));

		Assert.True(filtered.IsEmpty);
	}

	[Fact]
	public void FilterExact_RequiresTheSameTag()
	{
		GameplayTagSet set = GameplayTagSet.Create(
			TagSchema.EffectDamageType1,
			TagSchema.CueBurning);

		GameplayTagSet filtered = set.FilterExact(
			GameplayTagSet.Create(TagSchema.CueBurning));

		Assert.Equal(1, filtered.Count);
		Assert.True(filtered.HasTagExact(TagSchema.CueBurning));
	}

	[Fact]
	public void GetGameplayTagParents_IsAFixedPoint()
	{
		GameplayTagSet set = GameplayTagSet.Create(TagSchema.EffectDamageType1);

		GameplayTagSet parents = set.GetGameplayTagParents();

		Assert.Equal(4, parents.Count);
		Assert.True(parents.HasTagExact(TagSchema.EffectDamageType1));
		Assert.True(parents.HasTagExact(TagSchema.EffectDamage));
		Assert.True(parents.HasTagExact(TagSchema.Effect));

		Assert.Equal(parents, parents.GetGameplayTagParents());

		// The original set is unchanged.
		Assert.Equal(1, set.Count);
	}

	[Fact]
	public void Serialize_RoundTripsThroughRegisteredNames()
	{
		GameplayTagSet set = GameplayTagSet.Create(
			TagSchema.EffectDamageType1,
			TagSchema.CueBurning,
			TagSchema.StatusStunnedDeep);

		string text = set.Serialize();

		GameplayTagSet parsed = GameplayTagSet.Parse(text);

		Assert.Equal(set, parsed);
		Assert.True(parsed.HasTag(TagSchema.EffectDamage));
	}

	[Fact]
	public void Serialize_IsSortedByName_NotByIdentity()
	{
		GameplayTagSet set = GameplayTagSet.Create(
			TagSchema.StatusStunned,
			TagSchema.AbilityDash);

		string text = set.Serialize();

		Assert.Equal(
			"VoltGameplayTests.Ability.Dash, " +
			"VoltGameplayTests.Status.Stunned",
			text);
	}

	[Fact]
	public void Parse_EmptyText_ReturnsEmptySet()
	{
		Assert.True(GameplayTagSet.Parse("").IsEmpty);
		Assert.True(GameplayTagSet.Parse("   ").IsEmpty);
		Assert.True(GameplayTagSet.Parse(" , , ").IsEmpty);
	}

	[Fact]
	public void Parse_UnknownName_Throws()
	{
		Assert.Throws<ArgumentException>(
			() => GameplayTagSet.Parse("VoltGameplayTests.Unknown.Tag"));
	}

	[Fact]
	public void Matches_EmptyQuery_IsFalse()
	{
		Assert.False(
			GameplayTagSet.Create(TagSchema.Effect)
				.Matches(GameplayTagQuery.Empty));
	}

	[Fact]
	public void ToString_IsStableForDiagnostics()
	{
		GameplayTagSet set = GameplayTagSet.Create(TagSchema.AbilityDash);

		Assert.Equal(
			"GameplayTagSet { VoltGameplayTests.Ability.Dash }",
			set.ToString());
	}
}
