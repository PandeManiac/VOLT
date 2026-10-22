using Volt.Ecs;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tests.Attributes;
using Volt.Gameplay.Tests.Tags;

namespace Volt.Gameplay.Tests.Effects;

public sealed class GameplayEffectSchedulingTests
{
	[Fact]
	public void DurationEffect_ExpiresAtItsDurationAndNotBefore()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		GameplayEffectHandle handle =
			runtime.Effects.Apply(entity, EffectSchema.BuffFiveSeconds).Handle;

		runtime.Effects.Advance(4f);

		Assert.True(runtime.Effects.TryGetEffect(handle, out _));
		Assert.Equal(120f, runtime.Attributes.GetCurrentValue(
			entity,
			AttributeSchema.Unbounded));

		runtime.Effects.Advance(1f);

		Assert.False(runtime.Effects.TryGetEffect(handle, out _));
		Assert.Equal(100f, runtime.Attributes.GetCurrentValue(
			entity,
			AttributeSchema.Unbounded));
		Assert.Equal(0, runtime.Effects.ActiveEffectCount);
	}

	[Fact]
	public void AdvancingByZero_ChangesNothing()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(entity, EffectSchema.BuffFiveSeconds);

		for (int index = 0; index < 100; index++)
		{
			runtime.Effects.Advance(0f);
		}

		Assert.Equal(0d, runtime.Effects.Time);
		Assert.Equal(1, runtime.Effects.ActiveEffectCount);
		Assert.Equal(120f, runtime.Attributes.GetCurrentValue(
			entity,
			AttributeSchema.Unbounded));
	}

	[Fact]
	public void PeriodicEffect_TicksOncePerPeriod()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(entity, EffectSchema.BurnTenSeconds);

		// The application executed the first tick.
		Assert.Equal(98f, runtime.Attributes.GetBaseValue(
			entity,
			AttributeSchema.Unbounded));

		runtime.Effects.Advance(2.5f);

		// Two more ticks at one and two seconds; the third is not due.
		Assert.Equal(94f, runtime.Attributes.GetBaseValue(
			entity,
			AttributeSchema.Unbounded));

		runtime.Effects.Advance(0.5f);

		Assert.Equal(92f, runtime.Attributes.GetBaseValue(
			entity,
			AttributeSchema.Unbounded));
	}

	[Fact]
	public void PeriodicEffect_HoldsNoTemporaryModifier()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(entity, EffectSchema.BurnTenSeconds);
		runtime.Effects.Advance(0.5f);

		// Between ticks the base and current values agree: a periodic
		// effect executes and forgets instead of holding a modifier.
		Assert.Equal(98f, runtime.Attributes.GetBaseValue(
			entity,
			AttributeSchema.Unbounded));
		Assert.Equal(98f, runtime.Attributes.GetCurrentValue(
			entity,
			AttributeSchema.Unbounded));
	}

	[Fact]
	public void PeriodicEffect_CanWaitForItsFirstPeriod()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(entity, EffectSchema.BurnWaitingFirstTick);

		Assert.Equal(100f, runtime.Attributes.GetBaseValue(
			entity,
			AttributeSchema.Unbounded));

		runtime.Effects.Advance(0.9f);

		Assert.Equal(100f, runtime.Attributes.GetBaseValue(
			entity,
			AttributeSchema.Unbounded));

		runtime.Effects.Advance(0.2f);

		Assert.Equal(98f, runtime.Attributes.GetBaseValue(
			entity,
			AttributeSchema.Unbounded));
	}

	[Fact]
	public void PeriodicEffect_ExecutesAFinalTickOnExpiry()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(entity, EffectSchema.BurnTenSeconds);
		runtime.Effects.Advance(20f);

		// One execution on application plus one per second for the ten
		// second duration, including the tick that lands on expiry.
		Assert.Equal(78f, runtime.Attributes.GetBaseValue(
			entity,
			AttributeSchema.Unbounded));
		Assert.Equal(0, runtime.Effects.ActiveEffectCount);
	}

	[Fact]
	public void PeriodicEffect_StopsWhenRemoved()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		GameplayEffectHandle handle =
			runtime.Effects.Apply(entity, EffectSchema.BurnTenSeconds).Handle;

		runtime.Effects.Advance(2.5f);
		Assert.Equal(94f, runtime.Attributes.GetBaseValue(
			entity,
			AttributeSchema.Unbounded));

		runtime.Effects.Remove(handle);
		runtime.Effects.Advance(10f);

		Assert.Equal(94f, runtime.Attributes.GetBaseValue(
			entity,
			AttributeSchema.Unbounded));
		Assert.False(runtime.Tags.HasTag(entity, TagSchema.StatusBurning));
	}

	[Fact]
	public void OneLargeStep_ProducesTheSameEventsAsManySmallOnes()
	{
		using EffectRuntime large = new();
		using EffectRuntime small = new();

		EffectRecorder largeRecorder = new(large.Effects);
		EffectRecorder smallRecorder = new(small.Effects);

		Entity largeEntity = large.CreateAffectedEntity();
		Entity smallEntity = small.CreateAffectedEntity();

		large.Effects.Apply(largeEntity, EffectSchema.BurnTenSeconds);
		small.Effects.Apply(smallEntity, EffectSchema.BurnTenSeconds);

		large.Effects.Advance(10f);

		for (int index = 0; index < 100; index++)
		{
			small.Effects.Advance(0.1f);
		}

		Assert.Equal(
			small.Attributes.GetBaseValue(
				smallEntity,
				AttributeSchema.Unbounded),
			large.Attributes.GetBaseValue(
				largeEntity,
				AttributeSchema.Unbounded));

		Assert.Equal(0, large.Effects.ActiveEffectCount);
		Assert.Equal(0, small.Effects.ActiveEffectCount);
		Assert.Equal(smallRecorder.Changes.Count, largeRecorder.Changes.Count);

		for (int index = 0; index < smallRecorder.Changes.Count; index++)
		{
			Assert.Equal(
				smallRecorder.Changes[index].Kind,
				largeRecorder.Changes[index].Kind);
		}

		Assert.Equal(
			smallRecorder.Count(GameplayEffectChangeKind.PeriodExecuted),
			largeRecorder.Count(GameplayEffectChangeKind.PeriodExecuted));
		Assert.Equal(
			11,
			largeRecorder.Count(GameplayEffectChangeKind.PeriodExecuted));
	}

	[Fact]
	public void SimultaneousExpirations_ResolveInApplicationOrder()
	{
		using EffectRuntime runtime = new();
		EffectRecorder recorder = new(runtime.Effects);
		Entity entity = runtime.CreateAffectedEntity();

		GameplayEffectHandle first =
			runtime.Effects.Apply(entity, EffectSchema.BuffFiveSeconds).Handle;

		GameplayEffectHandle second =
			runtime.Effects.Apply(entity, EffectSchema.BuffFiveSeconds).Handle;

		GameplayEffectHandle third =
			runtime.Effects.Apply(entity, EffectSchema.BuffFiveSeconds).Handle;

		runtime.Effects.Advance(5f);

		List<GameplayEffectHandle> expired = [];
		int expiredCount = 0;

		foreach (GameplayEffectChange change in recorder.Changes)
		{
			if (change.Kind != GameplayEffectChangeKind.Expired)
			{
				continue;
			}

			expired.Add(change.Handle);
			expiredCount++;
		}

		Assert.Equal(3, expiredCount);
		Assert.Equal(first, expired[0]);
		Assert.Equal(second, expired[1]);
		Assert.Equal(third, expired[2]);
	}

	[Fact]
	public void StackRefresh_ExtendsTheDurationFromNow()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		GameplayEffectHandle handle = runtime.Effects.Apply(
			entity,
			EffectSchema.BleedPerSource).Handle;

		runtime.Effects.Advance(4f);

		runtime.Effects.Apply(entity, EffectSchema.BleedPerSource);

		Assert.True(runtime.Effects.TryGetEffect(
			handle,
			out ActiveGameplayEffect refreshed));

		Assert.Equal(5d, refreshed.RemainingDuration, 3);

		runtime.Effects.Advance(4f);

		Assert.True(runtime.Effects.TryGetEffect(handle, out _));

		runtime.Effects.Advance(1f);

		Assert.False(runtime.Effects.TryGetEffect(handle, out _));
	}

	[Fact]
	public void StackRefresh_NeverRefreshKeepsTheOriginalExpiry()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		GameplayEffectHandle handle = runtime.Effects.Apply(
			entity,
			EffectSchema.BleedNeverRefresh).Handle;

		runtime.Effects.Advance(4f);

		runtime.Effects.Apply(entity, EffectSchema.BleedNeverRefresh);

		Assert.True(runtime.Effects.TryGetEffect(
			handle,
			out ActiveGameplayEffect effect));

		Assert.Equal(1d, effect.RemainingDuration, 3);
		Assert.Equal(2, effect.StackCount);

		runtime.Effects.Advance(1f);

		Assert.False(runtime.Effects.TryGetEffect(handle, out _));
	}

	[Fact]
	public void StackRefresh_ExtendDurationAddsAFullDuration()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		GameplayEffectHandle handle =
			runtime.Effects.Apply(entity, EffectSchema.BleedExtend).Handle;

		runtime.Effects.Advance(4f);

		runtime.Effects.Apply(entity, EffectSchema.BleedExtend);

		Assert.True(runtime.Effects.TryGetEffect(
			handle,
			out ActiveGameplayEffect effect));

		// One second remained, and the new application adds five.
		Assert.Equal(6d, effect.RemainingDuration, 3);
	}

	[Fact]
	public void ExpirationPolicy_RemoveSingleStackDecrementsThenRemoves()
	{
		using EffectRuntime runtime = new();
		EffectRecorder recorder = new(runtime.Effects);
		Entity entity = runtime.CreateAffectedEntity();

		GameplayEffectHandle handle = runtime.Effects.Apply(
			entity,
			EffectSchema.BleedPerStackExpiry).Handle;

		runtime.Effects.Apply(entity, EffectSchema.BleedPerStackExpiry);
		runtime.Effects.Apply(entity, EffectSchema.BleedPerStackExpiry);

		Assert.True(runtime.Effects.TryGetEffect(
			handle,
			out ActiveGameplayEffect effect));

		Assert.Equal(3, effect.StackCount);

		runtime.Effects.Advance(1f);

		Assert.True(runtime.Effects.TryGetEffect(
			handle,
			out ActiveGameplayEffect twoStacks));

		Assert.Equal(2, twoStacks.StackCount);
		Assert.Equal(98f, runtime.Attributes.GetCurrentValue(
			entity,
			AttributeSchema.Unbounded));

		runtime.Effects.Advance(1f);

		Assert.True(runtime.Effects.TryGetEffect(
			handle,
			out ActiveGameplayEffect oneStack));

		Assert.Equal(1, oneStack.StackCount);

		runtime.Effects.Advance(1f);

		Assert.False(runtime.Effects.TryGetEffect(handle, out _));
		Assert.Equal(100f, runtime.Attributes.GetCurrentValue(
			entity,
			AttributeSchema.Unbounded));

		// Two applications gained a stack and two expiries lost one.
		Assert.Equal(4, recorder.Count(GameplayEffectChangeKind.StackChanged));
		Assert.Equal(1, recorder.Count(GameplayEffectChangeKind.Expired));
	}

	[Fact]
	public void ExpirationPolicy_RefreshDurationKeepsTheStackAlive()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		GameplayEffectHandle handle =
			runtime.Effects.Apply(entity, EffectSchema.BleedKeepOnExpiry).Handle;

		runtime.Effects.Advance(5f);

		Assert.True(runtime.Effects.TryGetEffect(handle, out _));
		Assert.Equal(1, runtime.Effects.ActiveEffectCount);
	}

	[Fact]
	public void PeriodicStacking_ResetsThePeriodOnApplication()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(entity, EffectSchema.BurnStacking);

		Assert.Equal(99f, runtime.Attributes.GetBaseValue(
			entity,
			AttributeSchema.Unbounded));

		runtime.Effects.Advance(0.6f);
		runtime.Effects.Apply(entity, EffectSchema.BurnStacking);

		runtime.Effects.Advance(0.4f);

		// The stack reset the period, so the tick that would have
		// happened at one second moved to 1.6 seconds.
		Assert.Equal(99f, runtime.Attributes.GetBaseValue(
			entity,
			AttributeSchema.Unbounded));

		runtime.Effects.Advance(0.6f);

		// Two stacks now, so the tick is worth two.
		Assert.Equal(97f, runtime.Attributes.GetBaseValue(
			entity,
			AttributeSchema.Unbounded));
	}

	[Fact]
	public void PeriodicEffect_InhibitedTicksAreSkipped()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		GameplayEffectHandle burn =
			runtime.Effects.Apply(entity, EffectSchema.BurnWhileStunned).Handle;

		runtime.Effects.Advance(2f);

		Assert.Equal(100f, runtime.Attributes.GetBaseValue(
			entity,
			AttributeSchema.Unbounded));

		GameplayEffectHandle stun =
			runtime.Effects.Apply(entity, EffectSchema.Stun).Handle;

		// ResetPeriod: the next tick is one full period after the
		// effect became active again.
		runtime.Effects.Advance(0.9f);

		Assert.Equal(100f, runtime.Attributes.GetBaseValue(
			entity,
			AttributeSchema.Unbounded));

		runtime.Effects.Advance(0.2f);

		Assert.Equal(99f, runtime.Attributes.GetBaseValue(
			entity,
			AttributeSchema.Unbounded));

		runtime.Effects.Remove(stun);
		runtime.Effects.Advance(3f);

		Assert.Equal(99f, runtime.Attributes.GetBaseValue(
			entity,
			AttributeSchema.Unbounded));
		Assert.True(runtime.Effects.TryGetEffect(burn, out _));
	}

	[Fact]
	public void PeriodicEffect_ExecuteAndResetPeriodRunsOnResume()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(entity, EffectSchema.BurnExecuteOnResume);

		runtime.Effects.Advance(1.5f);

		Assert.Equal(100f, runtime.Attributes.GetBaseValue(
			entity,
			AttributeSchema.Unbounded));

		runtime.Effects.Apply(entity, EffectSchema.Stun);

		Assert.Equal(99f, runtime.Attributes.GetBaseValue(
			entity,
			AttributeSchema.Unbounded));

		runtime.Effects.Advance(1f);

		Assert.Equal(98f, runtime.Attributes.GetBaseValue(
			entity,
			AttributeSchema.Unbounded));
	}

	[Fact]
	public void PausingAndTimeScaling_UseTheSuppliedDelta()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		GameplayEffectHandle handle =
			runtime.Effects.Apply(entity, EffectSchema.BuffFiveSeconds).Handle;

		runtime.Effects.Advance(2.5f);
		runtime.Effects.Advance(2.4f);

		Assert.True(runtime.Effects.TryGetEffect(handle, out _));

		// Four tenths of simulation time remain; a half-speed game
		// reaches them after eight tenths of real time, which is
		// simply a half-size delta each step.
		for (int index = 0; index < 4; index++)
		{
			runtime.Effects.Advance(0.1f);
		}

		Assert.False(runtime.Effects.TryGetEffect(handle, out _));
	}

	[Fact]
	public void Advance_RejectsInvalidSteps()
	{
		using EffectRuntime runtime = new();

		Assert.Throws<ArgumentOutOfRangeException>(() =>
			runtime.Effects.Advance(-1f));

		Assert.Throws<ArgumentOutOfRangeException>(() =>
			runtime.Effects.Advance(float.NaN));

		Assert.Throws<ArgumentOutOfRangeException>(() =>
			runtime.Effects.Advance(float.PositiveInfinity));
	}

	[Fact]
	public void Advance_CannotReenterFromANotification()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(entity, EffectSchema.BuffFiveSeconds);

		runtime.Effects.Changed += _ => runtime.Effects.Advance(1f);

		Assert.Throws<InvalidOperationException>(() =>
			runtime.Effects.Advance(5f));
	}

	[Fact]
	public void DestroyingTheEntityMidTick_StopsTheEffectCleanly()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(entity, EffectSchema.BurnTenSeconds);

		runtime.Effects.Changed += change =>
		{
			if (change.Kind == GameplayEffectChangeKind.PeriodExecuted
				&& runtime.World.IsAlive(change.Target))
			{
				runtime.World.DestroyEntity(change.Target);
			}
		};

		runtime.Effects.Advance(10f);

		Assert.Equal(0, runtime.Effects.ActiveEffectCount);
		Assert.Equal(0, runtime.Effects.GetEffectCount(entity));
	}

	[Fact]
	public void ManyIdleEffects_CostOneComparisonPerStep()
	{
		using EffectRuntime runtime = new();

		Entity[] entities = new Entity[64];

		for (int index = 0; index < entities.Length; index++)
		{
			entities[index] = runtime.CreateAffectedEntity();
			runtime.Effects.Apply(entities[index], EffectSchema.BuffFiveSeconds);
		}

		Assert.Equal(64, runtime.Effects.ActiveEffectCount);

		// Nothing is due for four seconds; the clock advances without
		// touching any instance.
		runtime.Effects.Advance(4f);

		Assert.Equal(64, runtime.Effects.ActiveEffectCount);
		Assert.Equal(120f, runtime.Attributes.GetCurrentValue(
			entities[0],
			AttributeSchema.Unbounded));

		runtime.Effects.Advance(1f);

		Assert.Equal(0, runtime.Effects.ActiveEffectCount);
	}
}
