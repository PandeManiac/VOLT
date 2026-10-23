using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tests.Attributes;
using Volt.Gameplay.Tests.Tags;

namespace Volt.Gameplay.Tests.Abilities;

/// <summary>
/// Covers condition evaluation against live tag and attribute state,
/// and the authoring limits that keep a tree bounded.
/// </summary>
public sealed class GameplayConditionTests
{
	private static GameplayConditionContext Context(
		AbilityRuntime runtime,
		Entity owner,
		Entity target)
	{
		return new GameplayConditionContext(
			runtime.Tags,
			runtime.Attributes,
			owner,
			target,
			runtime.World.IsAlive(target));
	}

	[Fact]
	public void OwnerTagConditions_ReadLiveGrants()
	{
		using AbilityRuntime runtime = new();
		Entity owner = runtime.CreateActor();
		GameplayCondition condition =
			GameplayCondition.OwnerHasTag(TagSchema.StatusBurning);

		GameplayConditionContext context = Context(
			runtime,
			owner,
			default);

		Assert.False(condition.Evaluate(in context));

		Assert.True(runtime.Tags.AddTag(owner, TagSchema.StatusBurning));

		GameplayConditionContext granted = Context(
			runtime,
			owner,
			default);

		Assert.True(condition.Evaluate(in granted));

		Assert.True(runtime.Tags.RemoveTag(owner, TagSchema.StatusBurning));

		GameplayConditionContext revoked = Context(
			runtime,
			owner,
			default);

		Assert.False(condition.Evaluate(in revoked));
	}

	[Fact]
	public void ExactTagCondition_RejectsADescendantGrant()
	{
		using AbilityRuntime runtime = new();
		Entity owner = runtime.CreateActor();

		Assert.True(runtime.Tags.AddTag(owner, TagSchema.StatusStunnedDeep));

		GameplayCondition inherited =
			GameplayCondition.OwnerHasTag(TagSchema.StatusStunned);
		GameplayCondition exact =
			GameplayCondition.OwnerHasTag(
				TagSchema.StatusStunned,
				exact: true);

		GameplayConditionContext context = Context(
			runtime,
			owner,
			default);

		Assert.True(inherited.Evaluate(in context));
		Assert.False(exact.Evaluate(in context));
	}

	[Fact]
	public void TargetConditions_UseTheActivationTarget()
	{
		using AbilityRuntime runtime = new();
		Entity owner = runtime.CreateActor();
		Entity target = runtime.CreateActor();

		Assert.True(runtime.Tags.AddTag(target, TagSchema.StatusStunned));

		GameplayConditionContext context = Context(runtime, owner, target);

		Assert.True(GameplayCondition
			.TargetHasTag(TagSchema.StatusStunned)
			.Evaluate(in context));
		Assert.False(GameplayCondition
			.TargetMissingTag(TagSchema.StatusStunned)
			.Evaluate(in context));
		Assert.True(GameplayCondition
			.TargetAlive()
			.Evaluate(in context));
		Assert.False(GameplayCondition
			.OwnerHasTag(TagSchema.StatusStunned)
			.Evaluate(in context));
	}

	[Fact]
	public void DestroyedTarget_FailsEveryTargetCondition()
	{
		using AbilityRuntime runtime = new();
		Entity owner = runtime.CreateActor();
		Entity target = runtime.CreateActor();

		runtime.World.DestroyEntity(target);

		GameplayConditionContext context = Context(runtime, owner, target);

		Assert.False(GameplayCondition
			.TargetAlive()
			.Evaluate(in context));
		Assert.False(GameplayCondition
			.TargetHasTag(TagSchema.StatusStunned)
			.Evaluate(in context));
		Assert.False(GameplayCondition
			.TargetMissingTag(TagSchema.StatusStunned)
			.Evaluate(in context));
		Assert.False(GameplayCondition
			.TargetAttribute(
				AttributeSchema.Health,
				GameplayAttributeComparison.GreaterThan,
				0f)
			.Evaluate(in context));
	}

	[Fact]
	public void AttributeConditions_CompareTheCurrentValue()
	{
		using AbilityRuntime runtime = new();
		Entity owner = runtime.CreateActor();

		GameplayCondition lowHealth = GameplayCondition.OwnerAttribute(
			AttributeSchema.Health,
			GameplayAttributeComparison.LessThan,
			30f);

		GameplayConditionContext healthy = Context(
			runtime,
			owner,
			default);

		Assert.False(lowHealth.Evaluate(in healthy));

		Assert.True(runtime.Attributes.SetBaseValue(
			owner,
			AttributeSchema.Health,
			20f));

		GameplayConditionContext wounded = Context(
			runtime,
			owner,
			default);

		Assert.True(lowHealth.Evaluate(in wounded));
		Assert.True(GameplayCondition
			.OwnerAttribute(
				AttributeSchema.Health,
				GameplayAttributeComparison.Equal,
				20f)
			.Evaluate(in wounded));
		Assert.True(GameplayCondition
			.OwnerAttribute(
				AttributeSchema.Health,
				GameplayAttributeComparison.NotEqual,
				21f)
			.Evaluate(in wounded));
	}

	[Fact]
	public void CombinedConditions_FollowAllAnyAndNot()
	{
		using AbilityRuntime runtime = new();
		Entity owner = runtime.CreateActor();

		Assert.True(runtime.Tags.AddTag(owner, TagSchema.StatusBurning));

		GameplayCondition all = GameplayCondition.All(
			GameplayCondition.OwnerHasTag(TagSchema.StatusBurning),
			GameplayCondition.OwnerAttribute(
				AttributeSchema.Health,
				GameplayAttributeComparison.GreaterThan,
				50f));
		GameplayCondition any = GameplayCondition.Any(
			GameplayCondition.OwnerHasTag(TagSchema.StatusStunned),
			GameplayCondition.OwnerHasTag(TagSchema.StatusBurning));
		GameplayCondition not = GameplayCondition.Not(
			GameplayCondition.OwnerHasTag(TagSchema.StatusStunned));
		GameplayCondition impossible = GameplayCondition.All(
			GameplayCondition.OwnerHasTag(TagSchema.StatusStunned),
			GameplayCondition.OwnerHasTag(TagSchema.StatusBurning));

		GameplayConditionContext context = Context(
			runtime,
			owner,
			default);

		Assert.True(all.Evaluate(in context));
		Assert.True(any.Evaluate(in context));
		Assert.True(not.Evaluate(in context));
		Assert.False(impossible.Evaluate(in context));
	}

	[Fact]
	public void CombinedConditions_DoNotRetainTheCallerArray()
	{
		using AbilityRuntime runtime = new();
		Entity owner = runtime.CreateActor();
		GameplayConditionContext context = Context(runtime, owner, default);

		GameplayCondition[] children =
		[
			GameplayCondition.OwnerHasTag(TagSchema.StatusBurning)
		];
		GameplayCondition combined = GameplayCondition.All(children);

		children[0] = GameplayCondition.OwnerHasTag(TagSchema.StatusStunned);

		Assert.False(combined.Evaluate(in context));
		Assert.Equal(2, combined.NodeCount);
	}

	[Fact]
	public void CombinedConditions_EnforceTheTreeLimits()
	{
		GameplayCondition leaf =
			GameplayCondition.OwnerHasTag(TagSchema.StatusBurning);

		Assert.Equal(1, leaf.NodeCount);
		Assert.Equal(1, leaf.Depth);

		GameplayCondition deep = leaf;

		for (int index = 1; index < GameplayCondition.MaximumDepth; index++)
		{
			deep = GameplayCondition.Not(deep);
		}

		Assert.Equal(GameplayCondition.MaximumDepth, deep.Depth);

		Assert.Throws<InvalidOperationException>(
			() => GameplayCondition.Not(deep));

		GameplayCondition[] many = new GameplayCondition[
			GameplayCondition.MaximumNodeCount];

		Array.Fill(many, leaf);

		Assert.Throws<InvalidOperationException>(
			() => GameplayCondition.Any(many));
	}

	[Fact]
	public void Authoring_RejectsInvalidConditions()
	{
		Assert.Throws<ArgumentException>(
			() => GameplayCondition.OwnerHasTag(GameplayTag.None));
		Assert.Throws<ArgumentException>(
			() => GameplayCondition.OwnerAttribute(
				default,
				GameplayAttributeComparison.Equal,
				1f));
		Assert.Throws<ArgumentOutOfRangeException>(
			() => GameplayCondition.OwnerAttribute(
				AttributeSchema.Health,
				GameplayAttributeComparison.Equal,
				float.NaN));
		Assert.Throws<ArgumentException>(
			() => GameplayCondition.All());
		Assert.Throws<ArgumentException>(
			() => GameplayCondition.Any());
		Assert.Throws<ArgumentNullException>(
			() => GameplayCondition.Not(null!));
		Assert.Throws<ArgumentNullException>(
			() => GameplayCondition.All(
				GameplayCondition
					.OwnerHasTag(TagSchema.StatusBurning),
				null!));
	}
}
