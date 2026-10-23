using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Tests.Attributes;
using Volt.Gameplay.Tests.Effects;
using Volt.Gameplay.Tests.Tags;

namespace Volt.Gameplay.Tests.Abilities;

/// <summary>
/// Covers conditional plans: branch selection, nested blocks, live
/// state, destroyed targets, and malformed authoring.
/// </summary>
public sealed class GameplayAbilityPlanBranchTests
{
	private static GameplayAbilityDefinition Conditional(
		string name,
		float waitSeconds = 0f)
	{
		GameplayAbilityPlanBuilder plan = GameplayAbilityPlan.Create();

		if (waitSeconds > 0f)
		{
			plan.Wait(waitSeconds);
		}

		return GameplayAbilityDefinition
			.Create(name)
			.Plan(
				plan
					.If(GameplayCondition.TargetHasTag(
						TagSchema.StatusStunned))
					.ApplyEffect(EffectSchema.Stun)
					.Else()
					.ApplyEffect(EffectSchema.BuffFiveSeconds)
					.EndIf()
					.End()
					.Build())
			.Build();
	}

	[Fact]
	public void ConditionalPlan_FollowsTheTrueBranch()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		Entity target = runtime.CreateActor();
		AbilityRecorder recorder = new(runtime.Abilities);

		Assert.True(runtime.Tags.AddTag(target, TagSchema.StatusStunned));

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			Conditional("Test.Branch.True"));

		Assert.True(runtime.Abilities.TryActivate(handle, target).Succeeded);

		Assert.True(runtime.Tags.HasTag(actor, TagSchema.StatusStunned));
		Assert.False(runtime.Tags.HasTag(actor, TagSchema.EffectBuff));
		Assert.Equal(1, recorder.Count(GameplayAbilityChangeKind.Ended));
		Assert.Equal(0, runtime.Abilities.ActiveAbilityCount);
	}

	[Fact]
	public void ConditionalPlan_FollowsTheFalseBranch()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		Entity target = runtime.CreateActor();

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			Conditional("Test.Branch.False"));

		Assert.True(runtime.Abilities.TryActivate(handle, target).Succeeded);

		Assert.True(runtime.Tags.HasTag(actor, TagSchema.EffectBuff));
		Assert.False(runtime.Tags.HasTag(actor, TagSchema.StatusStunned));
	}

	[Fact]
	public void ConditionalPlan_WithoutElse_FallsThrough()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityDefinition definition = GameplayAbilityDefinition
			.Create("Test.Branch.NoElse")
			.Plan(
				GameplayAbilityPlan
					.Create()
					.If(GameplayCondition.OwnerHasTag(
						TagSchema.StatusBurning))
					.ApplyEffect(EffectSchema.InstantMinusTen)
					.EndIf()
					.ApplyEffect(EffectSchema.BuffFiveSeconds)
					.End()
					.Build())
			.Build();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, definition);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);

		// The If was false, so only the step after EndIf ran.
		Assert.Equal(
			120f,
			runtime.Attributes.GetCurrentValue(
				actor,
				AttributeSchema.Unbounded));
		Assert.True(runtime.Tags.HasTag(actor, TagSchema.EffectBuff));
	}

	[Fact]
	public void NestedBranches_ChooseTheInnermostPath()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		Entity target = runtime.CreateActor();

		GameplayAbilityDefinition definition = GameplayAbilityDefinition
			.Create("Test.Branch.Nested")
			.Plan(
				GameplayAbilityPlan
					.Create()
					.Wait(1f)
					.If(GameplayCondition.OwnerHasTag(
						TagSchema.StatusBurning))
					.If(GameplayCondition.TargetAlive())
					.ApplyEffect(EffectSchema.InstantMinusTen)
					.Else()
					.ApplyEffect(EffectSchema.InstantScaleThenAdd)
					.EndIf()
					.Else()
					.ApplyEffect(EffectSchema.BuffFiveSeconds)
					.EndIf()
					.End()
					.Build())
			.Build();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, definition);

		Assert.True(runtime.Abilities.TryActivate(handle, target).Succeeded);

		// Nothing applies before the wait completes.
		Assert.Equal(
			100f,
			runtime.Attributes.GetCurrentValue(
				actor,
				AttributeSchema.Unbounded));

		runtime.World.DestroyEntity(target);

		Assert.True(runtime.Tags.AddTag(actor, TagSchema.StatusBurning));

		runtime.Tasks.Advance(1f);

		// Outer true, inner false.
		Assert.Equal(
			205f,
			runtime.Attributes.GetCurrentValue(
				actor,
				AttributeSchema.Unbounded));
		Assert.False(runtime.Abilities.IsActive(handle));
	}

	[Fact]
	public void BranchSeesStateChangedAfterThePreviousStep()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		Entity target = runtime.CreateActor();

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			Conditional("Test.Branch.LiveState", waitSeconds: 1f));

		Assert.True(runtime.Abilities.TryActivate(handle, target).Succeeded);

		// The branch has not run yet, so the target's later stun is
		// what the condition observes.
		Assert.True(runtime.Tags.AddTag(target, TagSchema.StatusStunned));

		runtime.Tasks.Advance(1f);

		Assert.True(runtime.Tags.HasTag(actor, TagSchema.StatusStunned));
	}

	[Fact]
	public void BranchOnDestroyedTarget_FollowsTheFalseBranch()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		Entity target = runtime.CreateActor();

		GameplayAbilityDefinition definition = GameplayAbilityDefinition
			.Create("Test.Branch.DestroyedTarget")
			.Plan(
				GameplayAbilityPlan
					.Create()
					.Wait(1f)
					.If(GameplayCondition.TargetAlive())
					.ApplyEffect(EffectSchema.Stun)
					.Else()
					.ApplyEffect(EffectSchema.BuffFiveSeconds)
					.EndIf()
					.End()
					.Build())
			.Build();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, definition);

		Assert.True(runtime.Abilities.TryActivate(handle, target).Succeeded);

		runtime.World.DestroyEntity(target);
		runtime.Tasks.Advance(1f);

		Assert.True(runtime.Tags.HasTag(actor, TagSchema.EffectBuff));
		Assert.False(runtime.Tags.HasTag(actor, TagSchema.StatusStunned));
	}

	[Fact]
	public void Authoring_RejectsMalformedBlocks()
	{
		GameplayAbilityPlanBuilder open = GameplayAbilityPlan
			.Create()
			.If(GameplayCondition.TargetAlive());

		Assert.Throws<InvalidOperationException>(() => open.Build());

		Assert.Throws<InvalidOperationException>(
			() => GameplayAbilityPlan.Create().Else());
		Assert.Throws<InvalidOperationException>(
			() => GameplayAbilityPlan.Create().EndIf());
		Assert.Throws<InvalidOperationException>(
			() => GameplayAbilityPlan
				.Create()
				.If(GameplayCondition.TargetAlive())
				.Else()
				.Else());
		Assert.Throws<ArgumentNullException>(
			() => GameplayAbilityPlan.Create().If(null!));
	}

	[Fact]
	public void BranchingPlan_RejectsTooManySteps()
	{
		GameplayAbilityPlanBuilder plan = GameplayAbilityPlan.Create();

		for (int index = 0; index < 20; index++)
		{
			plan
				.If(GameplayCondition.TargetAlive())
				.ApplyEffect(EffectSchema.InstantMinusTen)
				.EndIf();
		}

		Assert.Throws<InvalidOperationException>(() => plan.Build());
	}

	[Fact]
	public void ConditionalPlan_AllocatesNothingPerActivation()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		Entity target = runtime.CreateActor();
		GameplayAbilityDefinition definition = GameplayAbilityDefinition
			.Create("Test.Branch.Allocation")
			.Plan(
				GameplayAbilityPlan
					.Create()
					.If(GameplayCondition.OwnerHasTag(
						TagSchema.StatusBurning))
					.End()
					.Else()
					.End()
					.EndIf()
					.End()
					.Build())
			.Build();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, definition);

		// Warm the definition and branch paths.
		for (int index = 0; index < 128; index++)
		{
			runtime.Abilities.TryActivate(handle, target);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < 1_000; index++)
		{
			runtime.Abilities.TryActivate(handle, target);
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
	}
}
