using Volt.Gameplay.Tags;

namespace Volt.Gameplay.Tests.Tags;

/// <summary>
/// Reproduces the expectations of Unreal's own gameplay tag tests.
/// </summary>
/// <remarks>
/// <para>
/// Every assertion below corresponds to a check in Unreal 5.8.4's
/// <c>Engine/Source/Runtime/GameplayTags/Private/Tests/GameplayTagTests.cpp</c>
/// (<c>GameplayTagTest_TagComparisonTest</c> and
/// <c>GameplayTagTest_TagContainerTest</c>) or
/// <c>GameplayTagQueryTests.cpp</c>. They are the differential
/// reference for VOLT's tag semantics: if one of these fails, VOLT has
/// diverged from confirmed engine behavior.
/// </para>
/// <para>
/// The tag names are prefixed with the test root, which adds one
/// ancestor. Assertions about exact counts therefore use the
/// relationship rather than Unreal's raw number, but every matching
/// predicate is identical.
/// </para>
/// </remarks>
public sealed class UnrealGameplayTagBehaviorTests
{
	[Fact]
	public void TagComparison_MatchesUnreal()
	{
		GameplayTag effectDamage = TagSchema.EffectDamage;
		GameplayTag type1 = TagSchema.EffectDamageType1;
		GameplayTag type2 = TagSchema.EffectDamageType2;
		GameplayTag empty = GameplayTag.None;

		GameplayTag type1Copy = type1;

		Assert.True(type1 == type1Copy);
		Assert.True(type1 != type2);
		Assert.True(type1 != effectDamage);

		Assert.True(type1.MatchesTag(effectDamage));
		Assert.False(type1.MatchesTagExact(effectDamage));
		Assert.False(type1.MatchesTag(empty));
		Assert.False(type1.MatchesTagExact(empty));
		Assert.False(empty.MatchesTag(empty));
		Assert.False(empty.MatchesTagExact(empty));

		Assert.True(type1.DirectParent == effectDamage);
	}

	[Fact]
	public void TagContainer_SetOperations_MatchUnreal()
	{
		GameplayTag effectDamage = TagSchema.EffectDamage;
		GameplayTag type1 = TagSchema.EffectDamageType1;
		GameplayTag type2 = TagSchema.EffectDamageType2;
		GameplayTag cue = TagSchema.CueBurning;
		GameplayTag empty = GameplayTag.None;

		GameplayTagSet emptyContainer = GameplayTagSet.Empty;

		GameplayTagSet container = GameplayTagSet.Create(type1, cue);
		GameplayTagSet reverse = GameplayTagSet.Create(cue, type1);
		GameplayTagSet other = GameplayTagSet.Create(type2, cue);

		GameplayTagSet containerCopy = container;

		Assert.True(container == containerCopy);
		Assert.True(container == reverse);
		Assert.True(container != other);

		GameplayTagSet copy = GameplayTagSet.Create(type1, cue);

		Assert.True(copy == container);
		Assert.True(copy != other);

		Assert.True(container.HasAny(other));
		Assert.True(container.HasAnyExact(other));
		Assert.False(container.HasAll(other));
		Assert.False(container.HasAllExact(other));
		Assert.True(container.HasAll(copy));
		Assert.True(container.HasAllExact(copy));

		Assert.True(container.HasAll(emptyContainer));
		Assert.True(container.HasAllExact(emptyContainer));
		Assert.False(container.HasAny(emptyContainer));
		Assert.False(container.HasAnyExact(emptyContainer));

		Assert.True(emptyContainer.HasAll(emptyContainer));
		Assert.True(emptyContainer.HasAllExact(emptyContainer));
		Assert.False(emptyContainer.HasAny(emptyContainer));
		Assert.False(emptyContainer.HasAnyExact(emptyContainer));

		Assert.False(emptyContainer.HasAll(container));
		Assert.False(emptyContainer.HasAllExact(container));
		Assert.False(emptyContainer.HasAny(container));
		Assert.False(emptyContainer.HasAnyExact(container));

		Assert.True(container.HasTag(effectDamage));
		Assert.False(container.HasTagExact(effectDamage));
		Assert.False(container.HasTag(empty));
		Assert.False(container.HasTagExact(empty));

		Assert.True(type1.MatchesAny(GameplayTagSet.Create(effectDamage)));
		Assert.False(type1.MatchesAnyExact(GameplayTagSet.Create(effectDamage)));
		Assert.True(type1.MatchesAny(container));

		GameplayTagSet filteredExact = container.FilterExact(other);

		Assert.True(filteredExact.HasTagExact(cue));
		Assert.False(filteredExact.HasTagExact(type1));

		GameplayTagSet filtered = container.Filter(
			GameplayTagSet.Create(effectDamage));

		Assert.False(filtered.HasTagExact(cue));
		Assert.True(filtered.HasTagExact(type1));

		// Unreal calls this the single-tag container: the tag is
		// explicit, its ancestors are only cached.
		GameplayTagSet single = GameplayTagSet.Create(type1);

		Assert.True(single.HasTagExact(type1));
		Assert.True(single.HasTag(effectDamage));
		Assert.False(single.HasTagExact(effectDamage));

		GameplayTagSet parentContainer = type1.Parents;

		Assert.True(parentContainer.HasTagExact(type1));
		Assert.True(parentContainer.HasTag(effectDamage));
		Assert.True(parentContainer.HasTagExact(effectDamage));
	}

	[Fact]
	public void TagQuery_EmptyExpressionMatrix_MatchesUnreal()
	{
		GameplayTagSet empty = GameplayTagSet.Empty;
		GameplayTagSet nonEmpty = GameplayTagSet.Create(TagSchema.Effect);

		Assert.False(GameplayTagQuery.Empty.Matches(empty));
		Assert.False(GameplayTagQuery.Empty.Matches(nonEmpty));

		Assert.True(GameplayTagQuery.AllOf().Matches(empty));
		Assert.True(GameplayTagQuery.AllOf().Matches(nonEmpty));

		Assert.True(GameplayTagQuery.AllTags().Matches(empty));
		Assert.True(GameplayTagQuery.AllTags().Matches(nonEmpty));

		Assert.False(GameplayTagQuery.AnyOf().Matches(empty));
		Assert.False(GameplayTagQuery.AnyOf().Matches(nonEmpty));

		Assert.False(GameplayTagQuery.AnyTags().Matches(empty));
		Assert.False(GameplayTagQuery.AnyTags().Matches(nonEmpty));

		Assert.True(GameplayTagQuery.NoneOf().Matches(empty));
		Assert.True(GameplayTagQuery.NoneOf().Matches(nonEmpty));

		Assert.True(GameplayTagQuery.NoTags().Matches(empty));
		Assert.True(GameplayTagQuery.NoTags().Matches(nonEmpty));
	}

	[Fact]
	public void TagQuery_PopulatedExpressionMatrix_MatchesUnreal()
	{
		GameplayTag tag = TagSchema.Effect;
		GameplayTagSet empty = GameplayTagSet.Empty;
		GameplayTagSet nonEmpty = GameplayTagSet.Create(tag);

		// Unreal's expression nodes also carry an otherwise unused
		// tag set, so its "AllExprMatch with tags but no children"
		// case matches everything. VOLT's type cannot represent a tag
		// set on an expression node, which removes that trap rather
		// than reproducing it; AllOf with no children is tested above.
		Assert.False(GameplayTagQuery.AllTags(tag).Matches(empty));
		Assert.True(GameplayTagQuery.AllTags(tag).Matches(nonEmpty));

		Assert.False(GameplayTagQuery.AnyOf(
			GameplayTagQuery.AllTags(tag)).Matches(empty));

		Assert.True(GameplayTagQuery.AnyOf(
			GameplayTagQuery.AllTags(tag)).Matches(nonEmpty));

		Assert.False(GameplayTagQuery.AnyTags(tag).Matches(empty));
		Assert.True(GameplayTagQuery.AnyTags(tag).Matches(nonEmpty));

		Assert.True(GameplayTagQuery.NoneOf(
			GameplayTagQuery.AllTags(tag)).Matches(empty));

		Assert.True(GameplayTagQuery.NoTags(tag).Matches(empty));
		Assert.False(GameplayTagQuery.NoTags(tag).Matches(nonEmpty));
	}
}
