using Volt.Gameplay.Abilities;

namespace Volt.Gameplay.Tests.Abilities;

/// <summary>
/// Guards the zero-allocation claim for warmed-up ability paths.
/// </summary>
/// <remarks>
/// The guards measure managed allocations on the calling thread, so
/// they assert the implementation does not allocate — not that the
/// benchmark machine happened to be quiet.
/// </remarks>
public sealed class GameplayAbilityAllocationTests
{
	[Fact]
	public void ActivateAndEnd_AfterWarmup_AllocatesNothing()
	{
		using AbilityRuntime runtime = new();
		var actor = runtime.CreateActor();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);

		for (int index = 0; index < 128; index++)
		{
			runtime.Abilities.TryActivate(handle);
			runtime.Abilities.End(handle);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < 1_000; index++)
		{
			runtime.Abilities.TryActivate(handle);
			runtime.Abilities.End(handle);
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
	}

	[Fact]
	public void GiveAndRevoke_AfterWarmup_AllocatesNothing()
	{
		using AbilityRuntime runtime = new();
		var actor = runtime.CreateActor();

		for (int index = 0; index < 128; index++)
		{
			GameplayAbilityHandle warm =
				runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);

			runtime.Abilities.RevokeAbility(warm);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < 1_000; index++)
		{
			GameplayAbilityHandle handle =
				runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);

			runtime.Abilities.RevokeAbility(handle);
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
	}

	[Fact]
	public void RejectedActivation_AfterWarmup_AllocatesNothing()
	{
		using AbilityRuntime runtime = new();
		var actor = runtime.CreateActor();

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			AbilitySchema.InfiniteCooldownAbility);

		runtime.Abilities.TryActivate(handle);

		for (int index = 0; index < 128; index++)
		{
			runtime.Abilities.TryActivate(handle);
		}

		Assert.True(runtime.Abilities.IsOnCooldown(
			actor,
			AbilitySchema.InfiniteCooldownAbility));

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < 1_000; index++)
		{
			runtime.Abilities.TryActivate(handle);
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
	}
}
