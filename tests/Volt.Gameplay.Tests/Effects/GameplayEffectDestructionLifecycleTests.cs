using Volt.Ecs;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tests.Attributes;
using Volt.Gameplay.Tests.Tags;

namespace Volt.Gameplay.Tests.Effects;

public sealed class GameplayEffectDestructionLifecycleTests
{
	[Fact]
	public void ThrowingDestructionListener_DoesNotLeaveOtherEffectsActive()
	{
		using EffectRuntime runtime = new();
		Entity target = runtime.CreateAffectedEntity();
		runtime.Effects.Apply(target, EffectSchema.Stun);
		runtime.Effects.Apply(target, EffectSchema.BurnTenSeconds);
		int notifications = 0;

		runtime.Effects.Changed += _ =>
		{
			notifications++;
			throw new InvalidOperationException("destruction cue consumer failed");
		};

		AggregateException error = Assert.Throws<AggregateException>(
			() => runtime.World.DestroyEntity(target));

		Assert.Equal(2, error.Flatten().InnerExceptions.Count);
		Assert.All(
			error.Flatten().InnerExceptions,
			exception => Assert.Equal(
				"destruction cue consumer failed",
				exception.Message));
		Assert.Equal(2, notifications);
		Assert.Equal(0, runtime.Effects.ActiveEffectCount);
		Assert.Equal(0, runtime.Effects.GetEffectCount(target));
	}

	[Fact]
	public void DestroyingTarget_ReportsEveryEffectAsRemovedAfterCleanup()
	{
		using EffectRuntime runtime = new();
		Entity target = runtime.CreateAffectedEntity();
		GameplayEffectApplyResult stun = runtime.Effects.Apply(
			target,
			EffectSchema.Stun);
		GameplayEffectApplyResult burning = runtime.Effects.Apply(
			target,
			EffectSchema.BurnTenSeconds);
		List<GameplayEffectChange> changes = [];
		runtime.Effects.Changed += changes.Add;

		Assert.True(runtime.World.DestroyEntity(target));

		Assert.Equal(0, runtime.Effects.ActiveEffectCount);
		Assert.False(runtime.Effects.TryGetEffect(stun.Handle, out _));
		Assert.False(runtime.Effects.TryGetEffect(burning.Handle, out _));
		Assert.Equal(2, changes.Count);
		Assert.All(changes, change =>
		{
			Assert.Equal(GameplayEffectChangeKind.TargetDestroyed, change.Kind);
			Assert.Equal(target, change.Target);
			Assert.False(runtime.Effects.TryGetEffect(change.Handle, out _));
		});
		Assert.Contains(changes, change =>
			change.Handle == stun.Handle
			&& change.Definition == EffectSchema.Stun
			&& change.StackCount == 1);
		Assert.Contains(changes, change =>
			change.Handle == burning.Handle
			&& change.Definition == EffectSchema.BurnTenSeconds
			&& change.StackCount == 1);
		Assert.False(runtime.Tags.HasTag(target, TagSchema.StatusStunned));
		Assert.False(runtime.Tags.HasTag(target, TagSchema.StatusBurning));
	}
}
