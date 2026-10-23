using Volt.Gameplay.Abilities;
using Volt.Gameplay.Tests.Attributes;

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
	public void CostAndCooldown_AfterWarmup_AllocatesNothing()
	{
		using AbilityRuntime runtime = new();
		var actor = runtime.CreateActor();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Dash);

		for (int index = 0; index < 128; index++)
		{
			runtime.Abilities.TryActivate(handle);
			runtime.Effects.Advance(2f);
			runtime.Attributes.SetBaseValue(
				actor,
				AttributeSchema.Stamina,
				50f);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < 1_000; index++)
		{
			runtime.Abilities.TryActivate(handle);
			runtime.Effects.Advance(2f);
			runtime.Attributes.SetBaseValue(
				actor,
				AttributeSchema.Stamina,
				50f);
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
	}

	[Fact]
	public void ActivateAllAndCancelAll_AfterWarmup_AllocatesNothing()
	{
		using AbilityRuntime runtime = new();
		var actor = runtime.CreateActor();

		const int GrantCount = 256;

		Span<GameplayAbilityHandle> handles =
			stackalloc GameplayAbilityHandle[GrantCount];

		for (int index = 0; index < GrantCount; index++)
		{
			handles[index] = runtime.Abilities.GiveAbility(
				actor,
				AbilitySchema.Channel,
				inputId: 1);
		}

		for (int pass = 0; pass < 8; pass++)
		{
			for (int index = 0; index < GrantCount; index++)
			{
				runtime.Abilities.TryActivate(handles[index]);
			}

			runtime.Abilities.CancelAll(actor);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int pass = 0; pass < 16; pass++)
		{
			for (int index = 0; index < GrantCount; index++)
			{
				runtime.Abilities.TryActivate(handles[index]);
			}

			runtime.Abilities.CancelAll(actor);
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
	}

	[Fact]
	public void RevokeAll_AfterWarmup_AllocatesNothing()
	{
		using AbilityRuntime runtime = new();
		var actor = runtime.CreateActor();

		const int GrantCount = 256;

		for (int pass = 0; pass < 8; pass++)
		{
			for (int index = 0; index < GrantCount; index++)
			{
				runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);
			}

			runtime.Abilities.RevokeAll(actor);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int pass = 0; pass < 16; pass++)
		{
			for (int index = 0; index < GrantCount; index++)
			{
				runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);
			}

			runtime.Abilities.RevokeAll(actor);
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
	}

	[Fact]
	public void InputActivation_AfterWarmup_AllocatesNothing()
	{
		using AbilityRuntime runtime = new();
		var actor = runtime.CreateActor();

		const int GrantCount = 256;

		for (int index = 0; index < GrantCount; index++)
		{
			runtime.Abilities.GiveAbility(
				actor,
				AbilitySchema.Shove,
				inputId: 1);
		}

		for (int pass = 0; pass < 8; pass++)
		{
			runtime.Abilities.TryActivateByInput(actor, 1);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int pass = 0; pass < 16; pass++)
		{
			runtime.Abilities.TryActivateByInput(actor, 1);
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
