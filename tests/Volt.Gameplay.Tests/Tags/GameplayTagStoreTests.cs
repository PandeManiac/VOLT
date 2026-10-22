using Volt.Ecs;
using Volt.Gameplay.Tags;

namespace Volt.Gameplay.Tests.Tags;

public sealed class GameplayTagStoreTests
{
	[Fact]
	public void UntaggedEntity_HasNothing()
	{
		using World world = new();
		using GameplayTagStore store = new(world);

		Entity entity = world.CreateEntity();

		Assert.False(store.HasTag(entity, TagSchema.Status));
		Assert.False(store.HasTagExact(entity, TagSchema.Status));
		Assert.Equal(0, store.GetTagCount(entity, TagSchema.Status));
		Assert.Equal(0, store.TaggedEntityCount);
		Assert.True(store.GetTagSet(entity).IsEmpty);
	}

	[Fact]
	public void AddTag_MakesAncestorsMatch_ButNotExact()
	{
		using World world = new();
		using GameplayTagStore store = new(world);

		Entity entity = world.CreateEntity();

		Assert.True(store.AddTag(entity, TagSchema.EffectDamageType1));

		Assert.True(store.HasTag(entity, TagSchema.EffectDamageType1));
		Assert.True(store.HasTag(entity, TagSchema.EffectDamage));
		Assert.True(store.HasTag(entity, TagSchema.Effect));
		Assert.True(store.HasTag(entity, TagSchema.EffectDamageType1));
		Assert.False(store.HasTagExact(entity, TagSchema.EffectDamage));

		Assert.Equal(1, store.TaggedEntityCount);
	}

	[Fact]
	public void TwoGrants_RequireTwoRemovals()
	{
		using World world = new();
		using GameplayTagStore store = new(world);

		Entity entity = world.CreateEntity();

		store.AddTag(entity, TagSchema.StatusStunned);
		store.AddTag(entity, TagSchema.StatusStunned);

		Assert.Equal(2, store.GetTagCount(entity, TagSchema.StatusStunned));

		Assert.True(store.RemoveTag(entity, TagSchema.StatusStunned));
		Assert.True(store.HasTag(entity, TagSchema.StatusStunned));
		Assert.Equal(1, store.GetTagCount(entity, TagSchema.StatusStunned));

		Assert.True(store.RemoveTag(entity, TagSchema.StatusStunned));
		Assert.False(store.HasTag(entity, TagSchema.StatusStunned));
		Assert.Equal(0, store.TaggedEntityCount);

		Assert.False(store.RemoveTag(entity, TagSchema.StatusStunned));
	}

	[Fact]
	public void ParentStopsMatching_WhenLastDescendantGoes()
	{
		using World world = new();
		using GameplayTagStore store = new(world);

		Entity entity = world.CreateEntity();

		store.AddTag(entity, TagSchema.EffectDamageType1);
		store.AddTag(entity, TagSchema.EffectDamageType2);

		Assert.True(store.RemoveTag(entity, TagSchema.EffectDamageType1));
		Assert.True(store.HasTag(entity, TagSchema.EffectDamage));

		Assert.True(store.RemoveTag(entity, TagSchema.EffectDamageType2));
		Assert.False(store.HasTag(entity, TagSchema.EffectDamage));
		Assert.False(store.HasTag(entity, TagSchema.Effect));
	}

	[Fact]
	public void ExplicitParentGrant_SurvivesChildRemoval()
	{
		using World world = new();
		using GameplayTagStore store = new(world);

		Entity entity = world.CreateEntity();

		store.AddTag(entity, TagSchema.EffectDamageType1);
		store.AddTag(entity, TagSchema.EffectDamage);

		store.RemoveTag(entity, TagSchema.EffectDamageType1);

		Assert.True(store.HasTagExact(entity, TagSchema.EffectDamage));
		Assert.True(store.HasTag(entity, TagSchema.Effect));

		store.RemoveTag(entity, TagSchema.EffectDamage);

		Assert.False(store.HasTag(entity, TagSchema.Effect));
	}

	[Fact]
	public void AddTag_InvalidTag_IsIgnored()
	{
		using World world = new();
		using GameplayTagStore store = new(world);

		Entity entity = world.CreateEntity();

		Assert.False(store.AddTag(entity, GameplayTag.None));
		Assert.Equal(0, store.TaggedEntityCount);
	}

	[Fact]
	public void AddTag_DeadEntity_Throws()
	{
		using World world = new();
		using GameplayTagStore store = new(world);

		Entity entity = world.CreateEntity();

		world.DestroyEntity(entity);

		Assert.Throws<ArgumentException>(
			() => store.AddTag(entity, TagSchema.Status));
	}

	[Fact]
	public void DestroyingEntity_ReleasesAllTags()
	{
		using World world = new();
		using GameplayTagStore store = new(world);

		Entity entity = world.CreateEntity();

		store.AddTag(entity, TagSchema.StatusStunned);
		store.AddTag(entity, TagSchema.StatusBurning);
		store.AddTag(entity, TagSchema.StatusBurning);

		Assert.Equal(1, store.TaggedEntityCount);

		world.DestroyEntity(entity);

		Assert.Equal(0, store.TaggedEntityCount);
		Assert.False(store.HasTag(entity, TagSchema.Status));
		Assert.False(store.HasTagExact(entity, TagSchema.StatusBurning));
		Assert.True(store.GetTagSet(entity).IsEmpty);
	}

	[Fact]
	public void ReusedEntityIndex_DoesNotExposeOldOrNewTagsThroughStaleHandle()
	{
		using World world = new();
		using GameplayTagStore store = new(world);

		Entity first = world.CreateEntity();
		store.AddTag(first, TagSchema.StatusBurning);

		world.DestroyEntity(first);

		Entity second = world.CreateEntity();

		Assert.Equal(first.Index, second.Index);
		Assert.NotEqual(first.Generation, second.Generation);

		Assert.False(store.HasTag(first, TagSchema.StatusBurning));
		Assert.False(store.HasTag(first, TagSchema.Status));

		store.AddTag(second, TagSchema.StatusStunned);

		Assert.True(store.HasTag(second, TagSchema.Status));
		Assert.False(store.HasTag(first, TagSchema.Status));
		Assert.False(store.HasTagExact(first, TagSchema.StatusStunned));
		Assert.Equal(0, store.GetTagCount(first, TagSchema.StatusStunned));
	}

	[Fact]
	public void StaleHandle_CannotRemoveOrClearNewEntityTags()
	{
		using World world = new();
		using GameplayTagStore store = new(world);

		Entity first = world.CreateEntity();
		world.DestroyEntity(first);

		Entity second = world.CreateEntity();
		store.AddTag(second, TagSchema.StatusStunned);

		Assert.False(store.RemoveTag(first, TagSchema.StatusStunned));
		Assert.False(store.Clear(first));
		Assert.True(store.HasTagExact(second, TagSchema.StatusStunned));
	}

	[Fact]
	public void EntitiesFromAnotherWorld_AreRejectedOrUnseen()
	{
		using World first = new();
		using World second = new();

		using GameplayTagStore store = new(first);

		Entity foreign = second.CreateEntity();

		Assert.False(store.HasTag(foreign, TagSchema.Status));
		Assert.Throws<ArgumentException>(
			() => store.AddTag(foreign, TagSchema.Status));
	}

	[Fact]
	public void HasAnyAndHasAll_FollowSetSemantics()
	{
		using World world = new();
		using GameplayTagStore store = new(world);

		Entity entity = world.CreateEntity();

		store.AddTag(entity, TagSchema.EffectDamageType1);
		store.AddTag(entity, TagSchema.CueBurning);

		Assert.True(store.HasAny(
			entity,
			GameplayTagSet.Create(TagSchema.Cue)));

		Assert.True(store.HasAll(
			entity,
			GameplayTagSet.Create(TagSchema.EffectDamage, TagSchema.Cue)));

		Assert.False(store.HasAllExact(
			entity,
			GameplayTagSet.Create(TagSchema.EffectDamage)));

		Assert.True(store.HasAnyExact(
			entity,
			GameplayTagSet.Create(TagSchema.CueBurning)));

		Assert.True(store.HasAll(entity, GameplayTagSet.Empty));
		Assert.False(store.HasAny(entity, GameplayTagSet.Empty));
	}

	[Fact]
	public void Matches_EvaluatesQueriesAgainstEntityTags()
	{
		using World world = new();
		using GameplayTagStore store = new(world);

		Entity entity = world.CreateEntity();

		store.AddTag(entity, TagSchema.StatusStunned);

		Assert.True(store.Matches(
			entity,
			GameplayTagQuery.AllTags(TagSchema.Status)));

		Assert.True(store.Matches(
			entity,
			GameplayTagQuery.AnyTags(
				TagSchema.StatusBurning,
				TagSchema.StatusStunned)));

		Assert.False(store.Matches(
			entity,
			GameplayTagQuery.NoTags(TagSchema.StatusStunned)));

		Assert.True(store.Matches(
			entity,
			GameplayTagQuery.AllOf()));

		Assert.False(store.Matches(
			entity,
			GameplayTagQuery.Empty));
	}

	[Fact]
	public void Matches_UntaggedEntity_BehavesLikeEmptyContainer()
	{
		using World world = new();
		using GameplayTagStore store = new(world);

		Entity entity = world.CreateEntity();

		Assert.True(store.Matches(entity, GameplayTagQuery.AllTags()));
		Assert.False(store.Matches(entity, GameplayTagQuery.AnyTags()));
		Assert.True(store.Matches(entity, GameplayTagQuery.NoTags()));
	}

	[Fact]
	public void GetTagSet_ReturnsExplicitGrantsOnly()
	{
		using World world = new();
		using GameplayTagStore store = new(world);

		Entity entity = world.CreateEntity();

		store.AddTag(entity, TagSchema.EffectDamageType1);
		store.AddTag(entity, TagSchema.StatusBurning);
		store.AddTag(entity, TagSchema.StatusBurning);

		GameplayTagSet tags = store.GetTagSet(entity);

		Assert.Equal(2, tags.Count);
		Assert.True(tags.HasTagExact(TagSchema.EffectDamageType1));
		Assert.True(tags.HasTagExact(TagSchema.StatusBurning));
		Assert.False(tags.HasTagExact(TagSchema.EffectDamage));
	}

	[Fact]
	public void Clear_RemovesEveryGrantAndReturnsWhetherAnyExisted()
	{
		using World world = new();
		using GameplayTagStore store = new(world);

		Entity entity = world.CreateEntity();

		Assert.False(store.Clear(entity));

		store.AddTag(entity, TagSchema.Status);
		store.AddTag(entity, TagSchema.Effect);

		Assert.True(store.Clear(entity));
		Assert.False(store.HasTag(entity, TagSchema.Status));
		Assert.Equal(0, store.TaggedEntityCount);
	}

	[Fact]
	public void CopyTaggedEntities_EnumeratesOnlyTaggedEntities()
	{
		using World world = new();
		using GameplayTagStore store = new(world);

		List<Entity> entities = [];
		List<Entity> expected = [];

		for (int index = 0; index < 100; index++)
		{
			Entity entity = world.CreateEntity();
			entities.Add(entity);

			if (index % 3 == 0)
			{
				store.AddTag(entity, TagSchema.StatusStunned);
				expected.Add(entity);
			}
		}

		Assert.Equal(expected.Count, store.TaggedEntityCount);

		Entity[] buffer = new Entity[store.TaggedEntityCount];

		Assert.Equal(buffer.Length, store.CopyTaggedEntities(buffer));

		Assert.Equal(
			expected.OrderBy(entity => entity.Index),
			buffer.OrderBy(entity => entity.Index));
	}

	[Fact]
	public void CopyTaggedEntities_StaysConsistentAfterRemovals()
	{
		using World world = new();
		using GameplayTagStore store = new(world);

		Entity[] entities = new Entity[16];

		for (int index = 0; index < entities.Length; index++)
		{
			entities[index] = world.CreateEntity();
			store.AddTag(entities[index], TagSchema.Status);
		}

		store.RemoveTag(entities[0], TagSchema.Status);
		store.RemoveTag(entities[7], TagSchema.Status);
		store.RemoveTag(entities[15], TagSchema.Status);

		Entity[] buffer = new Entity[store.TaggedEntityCount];

		store.CopyTaggedEntities(buffer);

		HashSet<Entity> copied = [.. buffer];

		Assert.Equal(13, copied.Count);
		Assert.DoesNotContain(entities[0], copied);
		Assert.DoesNotContain(entities[7], copied);
		Assert.DoesNotContain(entities[15], copied);
		Assert.Contains(entities[14], copied);
	}

	[Fact]
	public void CopyTaggedEntities_TruncatesWhenBufferIsTooSmall()
	{
		using World world = new();
		using GameplayTagStore store = new(world);

		Entity first = world.CreateEntity();
		Entity second = world.CreateEntity();

		store.AddTag(first, TagSchema.Status);
		store.AddTag(second, TagSchema.Status);

		Entity[] buffer = new Entity[1];

		Assert.Equal(1, store.CopyTaggedEntities(buffer));
	}

	[Fact]
	public void WarmedRepeatedGrantAndRemoval_AllocatesNothing()
	{
		using World world = new();
		using GameplayTagStore store = new(world);

		Entity entity = world.CreateEntity();

		for (int index = 0; index < 16; index++)
		{
			store.AddTag(entity, TagSchema.Effect);
			store.RemoveTag(entity, TagSchema.Effect);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < 1_000; index++)
		{
			store.AddTag(entity, TagSchema.Effect);
			_ = store.HasTag(entity, TagSchema.Effect);
			store.RemoveTag(entity, TagSchema.Effect);
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
	}

	[Fact]
	public void Dispose_UnsubscribesFromWorldDestruction()
	{
		using World world = new();

		GameplayTagStore store = new(world);
		Entity entity = world.CreateEntity();

		store.AddTag(entity, TagSchema.Status);
		store.Dispose();

		// The store no longer owns per-entity state; destroying the
		// entity must not throw through a dead subscription.
		world.DestroyEntity(entity);

		Assert.Throws<ObjectDisposedException>(
			() => store.HasTag(entity, TagSchema.Status));
	}
}
