using Volt.Ecs;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tests.Attributes;

namespace Volt.Gameplay.Tests.Effects;

/// <summary>
/// Guards the zero-allocation claim for warmed-up effect paths.
/// </summary>
/// <remarks>
/// The guards measure managed allocations on the calling thread, so
/// they assert the implementation does not allocate — not that the
/// benchmark machine happened to be quiet.
/// </remarks>
public sealed class GameplayEffectAllocationTests
{
	[Fact]
	public void ApplyAndRemove_AfterWarmup_AllocatesNothing()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		for (int index = 0; index < 128; index++)
		{
			GameplayEffectHandle warm =
				runtime.Effects.Apply(entity, EffectSchema.BuffFiveSeconds).Handle;

			runtime.Effects.Remove(warm);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < 1_000; index++)
		{
			GameplayEffectHandle handle =
				runtime.Effects.Apply(entity, EffectSchema.BuffFiveSeconds).Handle;

			runtime.Effects.Remove(handle);
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
	}

	[Fact]
	public void InstantApplication_AfterWarmup_AllocatesNothing()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		for (int index = 0; index < 16; index++)
		{
			runtime.Effects.Apply(entity, EffectSchema.InstantMinusTen);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < 1_000; index++)
		{
			runtime.Effects.Apply(entity, EffectSchema.InstantMinusTen);
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
	}

	[Fact]
	public void PeriodicTick_AfterWarmup_AllocatesNothing()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		GameplayEffectDefinition longBurn = GameplayEffectDefinition
			.Create("Test.LongBurn")
			.HasDuration(10_000_000f)
			.Period(1f)
			.Modifier(
				AttributeSchema.Unbounded,
				GameplayModifierOperation.AddBase,
				-0.000_001f)
			.Build();

		runtime.Effects.Apply(entity, longBurn);

		for (int index = 0; index < 128; index++)
		{
			runtime.Effects.Advance(1f);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < 1_000; index++)
		{
			runtime.Effects.Advance(1f);
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
		Assert.Equal(
			1,
			runtime.Effects.ActiveEffectCount);
	}

	[Fact]
	public void IdleAdvance_AfterWarmup_AllocatesNothing()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(entity, EffectSchema.BuffFiveSeconds);
		runtime.Effects.Advance(0.1f);

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < 1_000; index++)
		{
			runtime.Effects.Advance(0.001f);
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
	}

	[Fact]
	public void Stacking_AfterWarmup_AllocatesNothing()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		for (int index = 0; index < 64; index++)
		{
			runtime.Effects.Apply(entity, EffectSchema.BleedPerSource);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < 1_000; index++)
		{
			runtime.Effects.Apply(entity, EffectSchema.BleedPerSource);
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
	}
}
