using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Events;
using Volt.Gameplay.Tasks;
using Volt.Gameplay.Tests.Abilities;
using Volt.Gameplay.Tests.Events;
using Volt.Gameplay.Tests.Tags;

namespace Volt.Gameplay.Tests.Tasks;

/// <summary>
/// Guards the zero-allocation claim for warmed-up task paths.
/// </summary>
/// <remarks>
/// The guards measure managed allocations on the calling thread, so
/// they assert the implementation does not allocate, not that the
/// benchmark machine was quiet.
/// </remarks>
public sealed class GameplayAbilityTaskAllocationTests
{
	[Fact]
	public void StartAndCancel_AfterWarmup_AllocatesNothing()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			AbilitySchema.Channel);

		for (int index = 0; index < 128; index++)
		{
			GameplayAbilityActivation activation =
				runtime.Abilities.TryActivate(handle).Activation;

			GameplayAbilityTaskHandle task =
				runtime.Tasks.WaitDuration(activation, 10f);

			runtime.Tasks.Cancel(task);
			runtime.Abilities.End(handle);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < 1_000; index++)
		{
			GameplayAbilityActivation activation =
				runtime.Abilities.TryActivate(handle).Activation;

			GameplayAbilityTaskHandle task =
				runtime.Tasks.WaitDuration(activation, 10f);

			runtime.Tasks.Cancel(task);
			runtime.Abilities.End(handle);
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
	}

	[Fact]
	public void DurationCompletion_AfterWarmup_AllocatesNothing()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			AbilitySchema.Channel);

		for (int index = 0; index < 128; index++)
		{
			GameplayAbilityActivation activation =
				runtime.Abilities.TryActivate(handle).Activation;

			runtime.Tasks.WaitDuration(activation, 0.5f);
			runtime.Tasks.Advance(0.6f);
			runtime.Abilities.End(handle);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < 1_000; index++)
		{
			GameplayAbilityActivation activation =
				runtime.Abilities.TryActivate(handle).Activation;

			runtime.Tasks.WaitDuration(activation, 0.5f);
			runtime.Tasks.Advance(0.6f);
			runtime.Abilities.End(handle);
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
	}

	[Fact]
	public void EventCompletion_AfterWarmup_AllocatesNothing()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			AbilitySchema.Channel);

		GameplayEvent gameplayEvent =
			new(EventSchema.AttackHit);

		for (int index = 0; index < 128; index++)
		{
			GameplayAbilityActivation activation =
				runtime.Abilities.TryActivate(handle).Activation;

			runtime.Tasks.WaitEvent(activation, EventSchema.AttackHit);
			runtime.Events.Publish(gameplayEvent);
			runtime.Abilities.End(handle);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < 1_000; index++)
		{
			GameplayAbilityActivation activation =
				runtime.Abilities.TryActivate(handle).Activation;

			runtime.Tasks.WaitEvent(activation, EventSchema.AttackHit);
			runtime.Events.Publish(gameplayEvent);
			runtime.Abilities.End(handle);
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
	}

	[Fact]
	public void TagConditionCompletion_AfterWarmup_AllocatesNothing()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			AbilitySchema.Channel);

		for (int index = 0; index < 128; index++)
		{
			GameplayAbilityActivation activation =
				runtime.Abilities.TryActivate(handle).Activation;

			runtime.Tasks.WaitTag(
				activation,
				TagSchema.StatusStunned,
				GameplayTagWaitMode.Present);

			runtime.Tags.AddTag(actor, TagSchema.StatusStunned);
			runtime.Tasks.Advance(0.001f);
			runtime.Tags.RemoveTag(actor, TagSchema.StatusStunned);
			runtime.Abilities.End(handle);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < 1_000; index++)
		{
			GameplayAbilityActivation activation =
				runtime.Abilities.TryActivate(handle).Activation;

			runtime.Tasks.WaitTag(
				activation,
				TagSchema.StatusStunned,
				GameplayTagWaitMode.Present);

			runtime.Tags.AddTag(actor, TagSchema.StatusStunned);
			runtime.Tasks.Advance(0.001f);
			runtime.Tags.RemoveTag(actor, TagSchema.StatusStunned);
			runtime.Abilities.End(handle);
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
	}

	[Fact]
	public void IdleTaskPopulation_AfterWarmup_AllocatesNothing()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			AbilitySchema.Channel);

		GameplayAbilityActivation activation =
			runtime.Abilities.TryActivate(handle).Activation;

		const int TaskCount = 1_000;

		for (int index = 0; index < TaskCount; index++)
		{
			runtime.Tasks.WaitDuration(
				activation,
				1_000f + index);
		}

		for (int index = 0; index < 128; index++)
		{
			runtime.Tasks.Advance(0.001f);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < 1_000; index++)
		{
			runtime.Tasks.Advance(0.001f);
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
		Assert.Equal(TaskCount, runtime.Tasks.PendingTaskCount);
	}

	[Fact]
	public void BulkExpiry_AfterWarmup_AllocatesNothing()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			AbilitySchema.Channel);

		const int TaskCount = 10_000;

		for (int pass = 0; pass < 2; pass++)
		{
			GameplayAbilityActivation activation =
				runtime.Abilities.TryActivate(handle).Activation;

			for (int index = 0; index < TaskCount; index++)
			{
				runtime.Tasks.WaitDuration(activation, 0f);
			}

			runtime.Tasks.Advance(0.001f);
			runtime.Abilities.End(handle);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		GameplayAbilityActivation measured =
			runtime.Abilities.TryActivate(handle).Activation;

		for (int index = 0; index < TaskCount; index++)
		{
			runtime.Tasks.WaitDuration(measured, 0f);
		}

		runtime.Tasks.Advance(0.001f);
		runtime.Abilities.End(handle);

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
	}

	[Fact]
	public void EndingWithManyTasks_AfterWarmup_AllocatesNothing()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			AbilitySchema.Channel);

		const int TaskCount = 256;

		for (int pass = 0; pass < 8; pass++)
		{
			GameplayAbilityActivation activation =
				runtime.Abilities.TryActivate(handle).Activation;

			for (int index = 0; index < TaskCount; index++)
			{
				runtime.Tasks.WaitDuration(
					activation,
					1_000f + index);
			}

			runtime.Abilities.End(handle);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int pass = 0; pass < 8; pass++)
		{
			GameplayAbilityActivation activation =
				runtime.Abilities.TryActivate(handle).Activation;

			for (int index = 0; index < TaskCount; index++)
			{
				runtime.Tasks.WaitDuration(
					activation,
					1_000f + index);
			}

			runtime.Abilities.End(handle);
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
	}
}
