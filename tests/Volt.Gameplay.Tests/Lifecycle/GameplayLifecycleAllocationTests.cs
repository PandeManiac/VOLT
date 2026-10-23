using Volt.Ecs;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tests.Attributes;
using Volt.Gameplay.Tests.Tags;

namespace Volt.Gameplay.Tests.Lifecycle;

/// <summary>
/// Guards the warmed-up allocation claim for the cross-store recycle
/// path, not only for individual store operations.
/// </summary>
public sealed class GameplayLifecycleAllocationTests
{
	private static readonly GameplayEffectDefinition InfiniteEffect =
		GameplayEffectDefinition
			.Create("Test.Lifecycle.Infinite")
			.Infinite()
			.Modifier(
				AttributeSchema.Unbounded,
				GameplayModifierOperation.AddBase,
				5f)
			.GrantTags(TagSchema.StatusStunned)
			.Build();

	[Fact]
	public void RecycleWithTags_AfterWarmup_AllocatesNothing()
	{
		using RecyclingRuntime runtime = new((state, entity) =>
			state.Tags.AddTag(entity, TagSchema.StatusStunned));

		AssertZeroAllocations(runtime);
		Assert.Equal(1, runtime.Tags.TaggedEntityCount);
	}

	[Fact]
	public void RecycleWithAttributes_AfterWarmup_AllocatesNothing()
	{
		using RecyclingRuntime runtime = new((state, entity) =>
			state.Attributes.SetBaseValue(
				entity,
				AttributeSchema.Unbounded,
				42f));

		AssertZeroAllocations(runtime);
		Assert.Equal(1, runtime.Attributes.AttributedEntityCount);
	}

	[Fact]
	public void RecycleWithEffects_AfterWarmup_AllocatesNothing()
	{
		using RecyclingRuntime runtime = new((state, entity) =>
		{
			for (int index = 0; index < 4; index++)
			{
				state.Effects.Apply(entity, InfiniteEffect);
			}
		});

		AssertZeroAllocations(runtime);
		Assert.Equal(4, runtime.Effects.ActiveEffectCount);
	}

	[Fact]
	public void RecycleWithAllStores_AfterWarmup_AllocatesNothing()
	{
		using RecyclingRuntime runtime = new((state, entity) =>
		{
			state.Tags.AddTag(entity, TagSchema.StatusBurning);
			state.Attributes.SetBaseValue(
				entity,
				AttributeSchema.Unbounded,
				42f);

			state.Effects.Apply(entity, InfiniteEffect);
		});

		AssertZeroAllocations(runtime);
		Assert.Equal(1, runtime.Tags.TaggedEntityCount);
		Assert.Equal(1, runtime.Attributes.AttributedEntityCount);
		Assert.Equal(1, runtime.Effects.ActiveEffectCount);
	}

	private static void AssertZeroAllocations(RecyclingRuntime runtime)
	{
		for (int index = 0; index < 64; index++)
		{
			runtime.Recycle();
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < 1_000; index++)
		{
			runtime.Recycle();
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
	}

	private sealed class RecyclingRuntime : IDisposable
	{
		private readonly Action<RecyclingRuntime, Entity> _populate;

		public RecyclingRuntime(Action<RecyclingRuntime, Entity> populate)
		{
			_populate = populate;

			World = new World();
			World.EntityDestroyed += HandleDestroyed;

			Attributes = new GameplayAttributeStore(World);
			Tags = new GameplayTagStore(World);
			Effects = new GameplayEffectSystem(World, Attributes, Tags);

			Current = World.CreateEntity();
			populate(this, Current);
		}

		public World World { get; }

		public GameplayAttributeStore Attributes { get; }

		public GameplayTagStore Tags { get; }

		public GameplayEffectSystem Effects { get; }

		public Entity Current { get; private set; }

		private Entity? Replacement { get; set; }

		public void Recycle()
		{
			World.DestroyEntity(Current);

			Current = Replacement!.Value;
			Replacement = null;
		}

		public void Dispose()
		{
			Effects.Dispose();
			Tags.Dispose();
			Attributes.Dispose();
			World.Dispose();
		}

		private void HandleDestroyed(Entity destroyed)
		{
			if (destroyed != Current)
			{
				return;
			}

			Entity replacement = World.CreateEntity();

			_populate(this, replacement);

			Replacement = replacement;
		}
	}
}
