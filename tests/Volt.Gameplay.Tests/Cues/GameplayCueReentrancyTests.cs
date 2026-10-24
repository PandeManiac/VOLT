using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Cues;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tests.Abilities;
using Volt.Gameplay.Tests.Tags;

namespace Volt.Gameplay.Tests.Cues;

public sealed class GameplayCueReentrancyTests
{
	[Fact]
	public void NestedDispatch_UnsubscribesCapturedListenerAndDefersNewListener()
	{
		using World world = new();
		using GameplayCueSystem cues = new(world);
		List<int> calls = [];
		GameplayCueSubscription victim = default;
		bool nested = false;
		victim = cues.Subscribe(TagSchema.CueBurning, _ => calls.Add(1));
		cues.Subscribe(TagSchema.Cue, _ =>
		{
			if (nested)
			{
				return;
			}

			nested = true;
			cues.Unsubscribe(victim);
			cues.Subscribe(TagSchema.CueBurning, _ => calls.Add(3));
			cues.Execute(TagSchema.CueBurning);
			calls.Add(2);
		}, GameplayCueMatch.Hierarchical);

		cues.Execute(TagSchema.CueBurning);

		Assert.Equal([1, 3, 2], calls);
	}

	[Fact]
	public void ThrowingListeners_DeliverToOthersAndKeepCueStateCommitted()
	{
		using World world = new();
		using GameplayCueSystem cues = new(world);
		int delivered = 0;
		cues.Subscribe(TagSchema.CueBurning, cue =>
		{
			if (cue.Kind == GameplayCueKind.Execute)
			{
				throw new InvalidOperationException("first");
			}
		});
		cues.Subscribe(TagSchema.CueBurning, _ => delivered++);

		InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => cues.Execute(TagSchema.CueBurning));

		Assert.Equal("first", error.Message);
		Assert.Equal(1, delivered);
		GameplayCueHandle handle = cues.Add(TagSchema.CueBurning);
		Assert.Equal(1, cues.ActiveCueCount);
		Assert.True(cues.TryGet(handle, out _));
		Assert.True(cues.Remove(handle));
		Assert.Equal(0, cues.ActiveCueCount);
	}

	[Fact]
	public void ActivationCleanup_RemovesEveryOwnedCueAndDoesNotRemoveReplacementActivation()
	{
		using AbilityRuntime runtime = new();
		Entity owner = runtime.CreateActor();
		GameplayTag eventTag = GameplayTagRegistry.Shared.Register("VoltGameplayTests.Cue.Release");
		GameplayAbilityPlan plan = GameplayAbilityPlan.Create()
			.AddCue(TagSchema.CueBurning)
			.AddCue(TagSchema.CueBurning)
			.WaitEvent(eventTag)
			.End()
			.Build();
		GameplayAbilityDefinition ability = GameplayAbilityDefinition.Create("Cue.TwoOwned")
			.EndOnActivate(false)
			.Plan(plan)
			.Build();
		GameplayAbilityHandle grant = runtime.Abilities.GiveAbility(owner, ability);
		GameplayAbilityActivation first = runtime.Abilities.TryActivate(grant).Activation;
		Assert.Equal(2, runtime.Abilities.Cues.ActiveCueCount);

		runtime.Abilities.Cancel(grant);
		GameplayAbilityActivation second = runtime.Abilities.TryActivate(grant).Activation;
		Assert.NotEqual(first, second);
		Assert.Equal(2, runtime.Abilities.Cues.ActiveCueCount);
		runtime.Abilities.Cues.RemoveActivation(first);
		Assert.Equal(2, runtime.Abilities.Cues.ActiveCueCount);
		runtime.Abilities.Cancel(grant);
		Assert.Equal(0, runtime.Abilities.Cues.ActiveCueCount);
	}

	[Fact]
	public void OwnerScopedSubscriptionsAreRemovedAndCannotReceiveAfterIndexReuse()
	{
		using World world = new();
		Entity original = world.CreateEntity();
		using GameplayCueSystem cues = new(world);
		int calls = 0;
		GameplayCueSubscription owned = cues.Subscribe(TagSchema.CueBurning, _ => calls++, owner: original);
		GameplayCueSubscription global = cues.Subscribe(TagSchema.CueBurning, _ => calls++);

		Assert.True(world.DestroyEntity(original));
		Entity replacement = world.CreateEntity();
		Assert.Equal(original.Index, replacement.Index);
		cues.Execute(TagSchema.CueBurning);

		Assert.Equal(1, calls);
		Assert.False(cues.Unsubscribe(owned));
		Assert.True(cues.Unsubscribe(global));
		Assert.Equal(0, cues.SubscriptionCount);
	}

	[Fact]
	public void OwnerDestruction_DoesNotRetireCueCreatedForRecycledIndex()
	{
		using World world = new();
		Entity original = world.CreateEntity();
		using GameplayCueSystem cues = new(world);
		Entity replacement = default;
		GameplayCueHandle replacementCue = default;
		bool reentered = false;
		cues.Changed += change =>
		{
			if (!reentered && change.Kind == GameplayCueKind.Removed)
			{
				reentered = true;
				replacement = world.CreateEntity();
				replacementCue = cues.Add(TagSchema.CueBurning, target: replacement);
			}
		};
		cues.Add(TagSchema.CueBurning, target: original);

		world.DestroyEntity(original);

		Assert.Equal(original.Index, replacement.Index);
		Assert.NotEqual(original.Generation, replacement.Generation);
		Assert.Equal(1, cues.ActiveCueCount);
		Assert.True(cues.TryGet(replacementCue, out GameplayCueEvent current));
		Assert.Equal(replacement, current.Target);
	}
}
