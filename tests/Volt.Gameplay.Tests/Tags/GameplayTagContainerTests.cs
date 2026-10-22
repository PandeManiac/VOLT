using Volt.Gameplay.Tags;

namespace Volt.Gameplay.Tests.Tags;

public sealed class GameplayTagContainerTests
{
	[Fact]
	public void Add_ReportsWhetherTheTagWasNew()
	{
		GameplayTagContainer container = new();

		Assert.True(container.Add(TagSchema.Effect));
		Assert.False(container.Add(TagSchema.Effect));
		Assert.Equal(1, container.Count);
	}

	[Fact]
	public void Add_IgnoresInvalidTag()
	{
		GameplayTagContainer container = new();

		Assert.False(container.Add(GameplayTag.None));
		Assert.True(container.IsEmpty);
	}

	[Fact]
	public void Add_MakesAncestorsMatch_ButNotExact()
	{
		GameplayTagContainer container = new();

		container.Add(TagSchema.EffectDamageType1);

		Assert.True(container.HasTag(TagSchema.EffectDamage));
		Assert.True(container.HasTag(TagSchema.Effect));
		Assert.False(container.HasTagExact(TagSchema.EffectDamage));
	}

	[Fact]
	public void Remove_RecomputesSharedAncestors()
	{
		GameplayTagContainer container = new();

		container.Add(TagSchema.EffectDamageType1);
		container.Add(TagSchema.EffectDamageType2);

		Assert.True(container.Remove(TagSchema.EffectDamageType1));
		Assert.True(container.HasTag(TagSchema.EffectDamage));

		Assert.True(container.Remove(TagSchema.EffectDamageType2));
		Assert.False(container.HasTag(TagSchema.EffectDamage));
		Assert.False(container.HasTag(TagSchema.Effect));
		Assert.True(container.IsEmpty);
	}

	[Fact]
	public void Remove_MissingTag_ReturnsFalse()
	{
		GameplayTagContainer container = new();

		container.Add(TagSchema.Effect);

		Assert.False(container.Remove(TagSchema.Status));
		Assert.False(container.Remove(GameplayTag.None));
		Assert.Equal(1, container.Count);
	}

	[Fact]
	public void RemoveRange_RemovesOnlyPresentTags()
	{
		GameplayTagContainer container = new();

		container.Add(TagSchema.Effect);
		container.Add(TagSchema.Status);

		int removed = container.RemoveRange(GameplayTagSet.Create(
			TagSchema.Effect,
			TagSchema.Other));

		Assert.Equal(1, removed);
		Assert.True(container.HasTagExact(TagSchema.Status));
		Assert.False(container.HasTagExact(TagSchema.Effect));
	}

	[Fact]
	public void Clear_EmptiesTagsAndParents()
	{
		GameplayTagContainer container = new();

		container.Add(TagSchema.StatusStunnedDeep);
		container.Clear();

		Assert.True(container.IsEmpty);
		Assert.False(container.HasTag(TagSchema.Status));
	}

	[Fact]
	public void Append_MergesExplicitTags()
	{
		GameplayTagContainer first = new(TagSchema.Effect);
		GameplayTagContainer second = new(TagSchema.Status, TagSchema.Effect);

		first.Append(second);

		Assert.Equal(2, first.Count);
		Assert.Equal(
			first.ToSet(),
			GameplayTagSet.Create(TagSchema.Effect, TagSchema.Status));
	}

	[Fact]
	public void Equality_IsOrderIndependent()
	{
		GameplayTagContainer first = new(
			TagSchema.EffectDamageType1,
			TagSchema.CueBurning);

		GameplayTagContainer second = new(
			TagSchema.CueBurning,
			TagSchema.EffectDamageType1);

		Assert.Equal(first, second);
		Assert.True(first == second);
		Assert.Equal(first.GetHashCode(), second.GetHashCode());
	}

	[Fact]
	public void GetGameplayTagParents_MakesAncestorsExplicit()
	{
		GameplayTagContainer container = new(TagSchema.EffectDamageType1);

		GameplayTagContainer parents = container.GetGameplayTagParents();

		Assert.Equal(4, parents.Count);
		Assert.True(parents.HasTagExact(TagSchema.EffectDamage));
		Assert.True(parents.HasTagExact(TagSchema.Effect));
		Assert.True(parents.Equals(parents.GetGameplayTagParents()));

		// The source container is not modified.
		Assert.Equal(1, container.Count);
		Assert.False(container.HasTagExact(TagSchema.EffectDamage));
	}

	[Fact]
	public void ToSet_IsAnIndependentSnapshot()
	{
		GameplayTagContainer container = new(TagSchema.Effect);

		GameplayTagSet snapshot = container.ToSet();

		container.Add(TagSchema.Status);

		Assert.Equal(1, snapshot.Count);
		Assert.False(snapshot.HasTag(TagSchema.Status));
		Assert.Equal(2, container.Count);
	}

	[Fact]
	public void HasAnyAndHasAll_MatchSetSemantics()
	{
		GameplayTagContainer container = new(
			TagSchema.EffectDamageType1,
			TagSchema.CueBurning);

		Assert.True(container.HasAny(
			GameplayTagSet.Create(TagSchema.Cue)));

		Assert.True(container.HasAll(
			GameplayTagSet.Create(TagSchema.EffectDamage, TagSchema.Cue)));

		Assert.False(container.HasAllExact(
			GameplayTagSet.Create(TagSchema.EffectDamage)));

		Assert.True(container.HasAll(GameplayTagSet.Empty));
		Assert.False(container.HasAny(GameplayTagSet.Empty));
	}

	[Fact]
	public void Filter_MatchesSetFilter()
	{
		GameplayTagContainer container = new(
			TagSchema.EffectDamageType1,
			TagSchema.CueBurning);

		GameplayTagSet filtered = container.Filter(
			GameplayTagSet.Create(TagSchema.EffectDamage));

		Assert.Equal(1, filtered.Count);
		Assert.True(filtered.HasTagExact(TagSchema.EffectDamageType1));
	}

	[Fact]
	public void Serialize_RoundTrips()
	{
		GameplayTagContainer container = new(
			TagSchema.StatusBurning,
			TagSchema.AbilityDash);

		string text = container.Serialize();

		GameplayTagContainer parsed = GameplayTagContainer.Parse(text);

		Assert.Equal(container, parsed);
	}

	[Fact]
	public void WarmedContainer_MutationAndQueries_AllocateNothing()
	{
		GameplayTagContainer container = new();

		// Reserve capacity and resolve every lazily initialized
		// registry entry before measuring.
		container.AddRange(TagSchema.Bulk);
		container.Clear();

		GameplayTagSet requirement = GameplayTagSet.Create(
			TagSchema.Bulk[0],
			TagSchema.Bulk[1]);

		for (int index = 0; index < 16; index++)
		{
			container.Add(TagSchema.Bulk[2]);
			_ = container.HasTag(TagSchema.Bulk[0]);
			_ = container.HasAll(requirement);
			container.Remove(TagSchema.Bulk[2]);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < 1_000; index++)
		{
			container.Add(TagSchema.Bulk[2]);
			_ = container.HasTag(TagSchema.Bulk[2]);
			_ = container.HasAny(requirement);
			_ = container.HasAll(requirement);
			container.Remove(TagSchema.Bulk[2]);
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
	}
}
