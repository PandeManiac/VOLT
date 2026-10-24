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
	public void ReplacementTaggedBeforeDestructionCallback_RetiresOldDenseState()
	{
		using World world = new();

		Entity first = world.CreateEntity();
		Entity other = world.CreateEntity();

		GameplayTagStore? store = null;
		Entity? replacement = null;

		// Subscribed before the store, so the replacement exists and
		// is tagged while the old generation's dense entry still does.
		world.EntityDestroyed += destroyed =>
		{
			if (destroyed != first)
			{
				return;
			}

			replacement = world.CreateEntity();
			store!.AddTag(replacement.Value, TagSchema.StatusStunned);
		};

		using GameplayTagStore actual = new(world);
		store = actual;

		store.AddTag(first, TagSchema.StatusBurning);
		store.AddTag(other, TagSchema.StatusBurning);

		Assert.Equal(2, store.TaggedEntityCount);

		Assert.True(world.DestroyEntity(first));

		Assert.NotNull(replacement);
		Assert.Equal(first.Index, replacement.Value.Index);
		Assert.NotEqual(first.Generation, replacement.Value.Generation);
		Assert.Equal(2, store.TaggedEntityCount);

		Assert.False(store.HasTag(replacement.Value, TagSchema.StatusBurning));
		Assert.True(store.HasTag(replacement.Value, TagSchema.StatusStunned));
		Assert.True(store.HasTag(other, TagSchema.StatusBurning));

		Entity[] buffer = new Entity[store.TaggedEntityCount];

		Assert.Equal(2, store.CopyTaggedEntities(buffer));
		Assert.Equal(2, buffer.Distinct().Count());
		Assert.Contains(other, buffer);
		Assert.Contains(replacement.Value, buffer);
		Assert.DoesNotContain(first, buffer);

		Assert.False(store.RemoveTag(first, TagSchema.StatusStunned));
		Assert.True(store.HasTag(replacement.Value, TagSchema.StatusStunned));
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
	public void TagsChanged_ReportsEverySuccessfulMutation()
	{
		using World world = new();
		using GameplayTagStore store = new(world);

		Entity entity = world.CreateEntity();
		List<Entity> changed = [];

		store.TagsChanged += changed.Add;

		store.AddTag(entity, TagSchema.Status);
		store.AddTag(entity, TagSchema.Status);
		store.RemoveTag(entity, TagSchema.Status);
		store.RemoveTag(entity, TagSchema.Status);

		// A removal without a grant is not a change.
		store.RemoveTag(entity, TagSchema.Status);

		Assert.Equal(4, changed.Count);

		foreach (Entity reported in changed)
		{
			Assert.Equal(entity, reported);
		}
	}

	[Fact]
	public void TagsChanged_IsNotRaisedForTheInvalidTag()
	{
		using World world = new();
		using GameplayTagStore store = new(world);

		Entity entity = world.CreateEntity();
		int changes = 0;

		store.TagsChanged += _ => changes++;

		Assert.False(store.AddTag(entity, GameplayTag.None));
		Assert.False(store.RemoveTag(entity, GameplayTag.None));
		Assert.Equal(0, changes);
	}

	[Fact]
	public void TagsChanged_IsRaisedOnceForClearButNotForDestruction()
	{
		using World world = new();
		using GameplayTagStore store = new(world);

		Entity entity = world.CreateEntity();
		int changes = 0;

		store.AddTag(entity, TagSchema.Status);

		store.TagsChanged += _ => changes++;

		// A clear is an observable mutation: dependent systems must
		// recompute their conditions, exactly as for a single removal.
		Assert.True(store.Clear(entity));
		Assert.Equal(1, changes);

		store.AddTag(entity, TagSchema.Status);
		Assert.Equal(2, changes);

		world.DestroyEntity(entity);
		Assert.Equal(2, changes);
	}

	[Fact]
	public void Clear_DoesNotRemoveGrantsOwnedByGameplayEffects()
	{
		using World world = new();
		using GameplayTagStore store = new(world);

		Entity entity = world.CreateEntity();

		store.AddTag(entity, TagSchema.Status);
		store.AddOwnedTag(entity, TagSchema.Status);
		store.AddOwnedTag(entity, TagSchema.EffectDamage);

		Assert.True(store.Clear(entity));

		// Only the unowned grant is cleared. The owned grants stay, so
		// the effect that recorded them still holds exactly what it
		// claims and cannot consume somebody else's grant later.
		Assert.Equal(1, store.GetTagCount(entity, TagSchema.Status));
		Assert.Equal(0, store.GetUnownedTagCount(entity, TagSchema.Status));
		Assert.Equal(1, store.GetTagCount(entity, TagSchema.EffectDamage));
		Assert.True(store.HasTag(entity, TagSchema.EffectDamage));
		Assert.False(store.Clear(entity));
	}

	[Fact]
	public void RemoveTag_NeverReleasesAnEffectOwnedGrant()
	{
		using World world = new();
		using GameplayTagStore store = new(world);

		Entity entity = world.CreateEntity();

		store.AddOwnedTag(entity, TagSchema.Status);

		Assert.False(store.RemoveTag(entity, TagSchema.Status));
		Assert.Equal(1, store.GetTagCount(entity, TagSchema.Status));

		store.AddTag(entity, TagSchema.Status);

		Assert.True(store.RemoveTag(entity, TagSchema.Status));
		Assert.Equal(1, store.GetTagCount(entity, TagSchema.Status));
		Assert.True(store.RemoveOwnedTag(entity, TagSchema.Status));
		Assert.False(store.HasTag(entity, TagSchema.Status));
	}

	[Fact]
	public void OwnedAndUnownedGrants_ShareOneVisibleCount()
	{
		using World world = new();
		using GameplayTagStore store = new(world);

		Entity entity = world.CreateEntity();

		store.AddTag(entity, TagSchema.StatusStunned);
		store.AddOwnedTag(entity, TagSchema.StatusStunned);

		Assert.Equal(2, store.GetTagCount(entity, TagSchema.StatusStunned));
		Assert.Equal(1, store.GetUnownedTagCount(entity, TagSchema.StatusStunned));
		Assert.True(store.HasTag(entity, TagSchema.Status));

		Assert.True(store.RemoveOwnedTag(entity, TagSchema.StatusStunned));
		Assert.False(store.RemoveOwnedTag(entity, TagSchema.StatusStunned));
		Assert.Equal(1, store.GetTagCount(entity, TagSchema.StatusStunned));
		Assert.True(store.HasTag(entity, TagSchema.Status));
	}

	[Fact]
	public void Clear_NotifiesEveryRemovedTagBeforeTheEntityNotification()
	{
		using World world = new();
		using GameplayTagStore store = new(world);

		Entity entity = world.CreateEntity();
		List<string> order = [];

		store.AddTag(entity, TagSchema.Status);
		store.AddTag(entity, TagSchema.Effect);
		store.AddTag(entity, TagSchema.OtherLeaf);

		store.TagChanged += (_, _) => order.Add("tag");
		store.TagsChanged += _ => order.Add("entity");

		Assert.True(store.Clear(entity));

		Assert.Equal(["tag", "tag", "tag", "entity"], order);
		Assert.False(store.HasTag(entity, TagSchema.Status));
		Assert.Equal(0, store.TaggedEntityCount);
	}

	[Fact]
	public void Clear_HandlerMayTagTheEntityAgain()
	{
		using World world = new();
		using GameplayTagStore store = new(world);

		Entity entity = world.CreateEntity();
		bool readded = false;
		int clears = 0;

		store.AddTag(entity, TagSchema.Status);

		store.TagsChanged += changed =>
		{
			if (!readded)
			{
				readded = true;
				store.AddTag(changed, TagSchema.Effect);

				// A nested clear observes the guard and reports that it
				// removed nothing instead of recursing.
				Assert.False(store.Clear(changed));
				clears++;
			}
		};

		Assert.True(store.Clear(entity));
		Assert.Equal(1, clears);
		Assert.True(store.HasTag(entity, TagSchema.Effect));
		Assert.False(store.HasTag(entity, TagSchema.Status));
	}

	[Fact]
	public void Clear_HandlerMayClearAnotherEntityWithoutCorruptingTheOuterDispatch()
	{
		using World world = new();
		using GameplayTagStore store = new(world);

		Entity first = world.CreateEntity();
		Entity second = world.CreateEntity();

		store.AddTag(first, TagSchema.Status);
		store.AddTag(first, TagSchema.Effect);
		store.AddTag(second, TagSchema.OtherLeaf);

		List<(Entity Entity, GameplayTag Tag)> changes = [];
		int entityNotifications = 0;
		bool nested = false;

		store.TagChanged += (entity, tag) =>
		{
			changes.Add((entity, tag));

			if (nested || entity != first)
			{
				return;
			}

			nested = true;

			// The nested clear has its own snapshot, so the outer
			// dispatch still reports the tags it removed.
			Assert.True(store.Clear(second));
		};

		store.TagsChanged += _ => entityNotifications++;

		Assert.True(store.Clear(first));

		GameplayTag[] firstTags = changes
			.Where(change => change.Entity == first)
			.Select(change => change.Tag)
			.OrderBy(tag => tag.Id)
			.ToArray();

		Assert.Equal(
			new[] { TagSchema.Effect.Id, TagSchema.Status.Id }.OrderBy(id => id),
			firstTags.Select(tag => tag.Id));
		Assert.Contains((second, TagSchema.OtherLeaf), changes);
		Assert.Equal(2, entityNotifications);
		Assert.False(store.HasTag(second, TagSchema.OtherLeaf));
		Assert.False(store.HasTag(first, TagSchema.Status));
		Assert.False(store.HasTag(first, TagSchema.Effect));
	}

	[Fact]
	public void TagsChanged_HandlerMayMutateTheSameEntity()
	{
		using World world = new();
		using GameplayTagStore store = new(world);

		Entity entity = world.CreateEntity();
		int changes = 0;

		store.TagsChanged += changed =>
		{
			changes++;

			if (changes < 4)
			{
				store.AddTag(changed, TagSchema.Effect);
			}
		};

		store.AddTag(entity, TagSchema.Status);

		// The first grant triggers the handler, and each of its three
		// grants triggers it again until the guard stops the chain.
		Assert.Equal(4, changes);
		Assert.Equal(3, store.GetTagCount(entity, TagSchema.Effect));
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
