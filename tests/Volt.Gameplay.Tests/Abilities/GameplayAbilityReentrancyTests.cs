using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tests.Attributes;
using Volt.Gameplay.Tests.Effects;
using Volt.Gameplay.Tests.Tags;

namespace Volt.Gameplay.Tests.Abilities;

/// <summary>
/// Covers the lifecycle hazards that arise when notifications re-enter
/// the ability system, and the ownership rules of a grant handle.
/// </summary>
public sealed class GameplayAbilityReentrancyTests
{
	/// <summary>
	/// A cooldown whose application requirements can be invalidated by
	/// a cancellation callback.
	/// </summary>
	private static readonly GameplayEffectDefinition GuardedCooldown =
		GameplayEffectDefinition
			.Create("Test.Ability.GuardedCooldown")
			.HasDuration(2f)
			.GrantTags(AbilitySchema.CooldownDash)
			.RequireToApply(
				GameplayTagRequirements.Ignore(
					GameplayTagSet.Create(TagSchema.StatusStunned)))
			.Build();

	/// <summary>
	/// An external cooldown that outlasts the dash's own cooldown.
	/// </summary>
	private static readonly GameplayEffectDefinition LongExternalCooldown =
		GameplayEffectDefinition
			.Create("Test.Ability.LongExternalCooldown")
			.HasDuration(10f)
			.GrantTags(AbilitySchema.CooldownDash)
			.Build();

	/// <summary>
	/// Cancels the channel, whose cancellation then stuns the owner, and
	/// applies a cost that a stun forbids.
	/// </summary>
	private static readonly GameplayAbilityDefinition CostAfterCancel =
		GameplayAbilityDefinition
			.Create("Test.Ability.CostAfterCancel")
			.AssetTags(AbilitySchema.AbilityFree)
			.CancelAbilitiesWithTags(AbilitySchema.AbilityChannel)
			.Cost(AbilitySchema.CostIgnoringStun)
			.Cooldown(AbilitySchema.DashCooldown)
			.Build();

	/// <summary>
	/// Cancels the channel, whose cancellation then stuns the owner, and
	/// applies a cooldown that a stun forbids.
	/// </summary>
	private static readonly GameplayAbilityDefinition CooldownAfterCancel =
		GameplayAbilityDefinition
			.Create("Test.Ability.CooldownAfterCancel")
			.AssetTags(AbilitySchema.AbilityFree)
			.CancelAbilitiesWithTags(AbilitySchema.AbilityChannel)
			.Cost(AbilitySchema.CostTen)
			.Cooldown(GuardedCooldown)
			.Build();

	/// <summary>
	/// A costed ability that cancels the channel and starts a cooldown.
	/// </summary>
	private static readonly GameplayAbilityDefinition AffordableAfterCancel =
		GameplayAbilityDefinition
			.Create("Test.Ability.AffordableAfterCancel")
			.AssetTags(AbilitySchema.AbilityFree)
			.CancelAbilitiesWithTags(AbilitySchema.AbilityChannel)
			.Cost(AbilitySchema.CostTen)
			.Cooldown(AbilitySchema.DashCooldown)
			.Build();

	/// <summary>
	/// A costed ability whose cooldown can be removed by an effect
	/// notification before activation returns.
	/// </summary>
	private static readonly GameplayAbilityDefinition CostedCooldown =
		GameplayAbilityDefinition
			.Create("Test.Ability.CostedCooldown")
			.AssetTags(AbilitySchema.AbilityFree)
			.Cost(AbilitySchema.CostTen)
			.Cooldown(GuardedCooldown)
			.Build();

	/// <summary>
	/// A lasting ability whose cooldown can be cancelled by an effect
	/// notification while it is being applied.
	/// </summary>
	private static readonly GameplayAbilityDefinition InterruptedCooldown =
		GameplayAbilityDefinition
			.Create("Test.Ability.InterruptedCooldown")
			.AssetTags(AbilitySchema.AbilityFree)
			.Cooldown(GuardedCooldown)
			.EndOnActivate(false)
			.Build();

	[Fact]
	public void CostRejectedDuringCommit_DoesNotActivateOrSpend()
	{
		using AbilityRuntime runtime = new();
		AbilityRecorder recorder = new(runtime.Abilities);
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle victim =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);

		Assert.True(runtime.Abilities.TryActivate(victim).Succeeded);

		bool stunned = false;

		runtime.Abilities.Changed += change =>
		{
			if (change.Kind == GameplayAbilityChangeKind.Cancelled
				&& change.Handle == victim
				&& !stunned)
			{
				stunned = true;

				Assert.True(runtime.Effects.Apply(
					actor,
					EffectSchema.Stun).Applied);
			}
		};

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, CostAfterCancel);

		GameplayAbilityActivationResult result =
			runtime.Abilities.TryActivate(handle);

		Assert.True(stunned);
		Assert.Equal(GameplayAbilityActivationStatus.RejectedByCost, result.Status);
		Assert.False(result.Succeeded);
		Assert.Equal(
			50f,
			runtime.Attributes.GetCurrentValue(
				actor,
				AttributeSchema.Stamina));
		Assert.False(runtime.Abilities.IsOnCooldown(actor, CostAfterCancel));
		Assert.False(runtime.Tags.HasTag(actor, AbilitySchema.CooldownDash));
		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.Equal(0, runtime.Abilities.ActiveAbilityCount);
		Assert.Equal(
			"Granted,Activated,Granted,Cancelled",
			recorder.Sequence());
	}

	[Fact]
	public void CooldownRejectedDuringCommit_DoesNotPayTheCost()
	{
		using AbilityRuntime runtime = new();
		AbilityRecorder recorder = new(runtime.Abilities);
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle victim =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);

		Assert.True(runtime.Abilities.TryActivate(victim).Succeeded);

		bool stunned = false;

		runtime.Abilities.Changed += change =>
		{
			if (change.Kind == GameplayAbilityChangeKind.Cancelled
				&& change.Handle == victim
				&& !stunned)
			{
				stunned = true;

				Assert.True(runtime.Effects.Apply(
					actor,
					EffectSchema.Stun).Applied);
			}
		};

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, CooldownAfterCancel);

		GameplayAbilityActivationResult result =
			runtime.Abilities.TryActivate(handle);

		Assert.True(stunned);
		Assert.Equal(
			GameplayAbilityActivationStatus.RejectedByCooldown,
			result.Status);
		Assert.False(result.Succeeded);
		Assert.Equal(
			50f,
			runtime.Attributes.GetCurrentValue(
				actor,
				AttributeSchema.Stamina));
		Assert.False(runtime.Tags.HasTag(actor, AbilitySchema.CooldownDash));
		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.Equal(0, runtime.Abilities.ActiveAbilityCount);
		Assert.Equal(
			"Granted,Activated,Granted,Cancelled",
			recorder.Sequence());
	}

	[Fact]
	public void CostSpentByCancellation_RejectsBeforePayingOrCoolingDown()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle victim =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);

		Assert.True(runtime.Abilities.TryActivate(victim).Succeeded);

		runtime.Abilities.Changed += change =>
		{
			if (change.Kind == GameplayAbilityChangeKind.Cancelled
				&& change.Handle == victim)
			{
				runtime.Attributes.SetBaseValue(
					actor,
					AttributeSchema.Stamina,
					5f);
			}
		};

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, AffordableAfterCancel);

		GameplayAbilityActivationResult result =
			runtime.Abilities.TryActivate(handle);

		Assert.Equal(GameplayAbilityActivationStatus.RejectedByCost, result.Status);
		Assert.False(result.Succeeded);
		Assert.Equal(
			5f,
			runtime.Attributes.GetCurrentValue(
				actor,
				AttributeSchema.Stamina));
		Assert.False(runtime.Tags.HasTag(actor, AbilitySchema.CooldownDash));
		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.Equal(0, runtime.Abilities.ActiveAbilityCount);
	}

	[Fact]
	public void CooldownRemovedDuringApplication_RejectsTheActivation()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		runtime.Effects.Changed += change =>
		{
			if (change.Kind == GameplayEffectChangeKind.Applied
				&& ReferenceEquals(change.Definition, GuardedCooldown))
			{
				runtime.Effects.Remove(change.Handle);
			}
		};

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, CostedCooldown);

		GameplayAbilityActivationResult result =
			runtime.Abilities.TryActivate(handle);

		Assert.Equal(
			GameplayAbilityActivationStatus.RejectedByCooldown,
			result.Status);
		Assert.False(result.Succeeded);
		Assert.Equal(
			50f,
			runtime.Attributes.GetCurrentValue(
				actor,
				AttributeSchema.Stamina));
		Assert.False(runtime.Abilities.IsOnCooldown(actor, CostedCooldown));
		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.Equal(0, runtime.Abilities.ActiveAbilityCount);
	}

	[Fact]
	public void CancelledDuringCooldownApplication_RollsBackTheCooldown()
	{
		using AbilityRuntime runtime = new();
		AbilityRecorder recorder = new(runtime.Abilities);
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, InterruptedCooldown);

		bool cancelled = false;

		runtime.Effects.Changed += change =>
		{
			if (!cancelled
				&& change.Kind == GameplayEffectChangeKind.Applied
				&& ReferenceEquals(change.Definition, GuardedCooldown))
			{
				cancelled = true;

				Assert.True(runtime.Abilities.Cancel(handle));
			}
		};

		GameplayAbilityActivationResult result =
			runtime.Abilities.TryActivate(handle);

		Assert.True(cancelled);
		Assert.Equal(
			GameplayAbilityActivationStatus.EndedDuringActivation,
			result.Status);
		Assert.False(result.Succeeded);
		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.True(runtime.Abilities.TryGetAbility(handle, out _));
		Assert.Equal(0, runtime.Abilities.ActiveAbilityCount);
		Assert.Equal(0, runtime.Abilities.GetActiveAbilityCount(actor));
		Assert.False(runtime.Abilities.IsOnCooldown(actor, InterruptedCooldown));
		Assert.False(runtime.Tags.HasTag(actor, AbilitySchema.CooldownDash));
		Assert.Equal("Granted,Cancelled", recorder.Sequence());
	}

	[Fact]
	public void OwnerRecycledDuringDestruction_ReleasesTheOldGrants()
	{
		using World world = new();
		using GameplayAttributeStore attributes = new(world);
		using GameplayTagStore tags = new(world);
		using GameplayEffectSystem effects = new(world, attributes, tags);

		Entity doomed = world.CreateEntity();
		Entity replacement = default;
		GameplayAbilityHandle replacementHandle = default;
		GameplayAbilitySystem abilities = null!;

		world.EntityDestroyed += destroyed =>
		{
			if (destroyed != doomed)
			{
				return;
			}

			replacement = world.CreateEntity();
			replacementHandle = abilities.GiveAbility(
				replacement,
				AbilitySchema.Channel);

			Assert.True(abilities.TryActivate(replacementHandle).Succeeded);
		};

		using GameplayAbilitySystem system =
			new(world, tags, effects, attributes);

		abilities = system;

		GameplayAbilityHandle stale =
			system.GiveAbility(doomed, AbilitySchema.Channel);

		Assert.True(system.TryActivate(stale).Succeeded);
		Assert.True(world.DestroyEntity(doomed));

		Assert.Equal(doomed.Index, replacement.Index);
		Assert.NotEqual(doomed.Generation, replacement.Generation);
		Assert.False(system.TryGetAbility(stale, out _));
		Assert.True(system.TryGetAbility(replacementHandle, out _));
		Assert.Equal(1, system.GrantedAbilityCount);
		Assert.Equal(1, system.ActiveAbilityCount);
		Assert.Equal(0, system.GetAbilityCount(doomed));
		Assert.Equal(1, system.GetAbilityCount(replacement));
		Assert.Equal(1, system.GetActiveAbilityCount(replacement));
	}

	[Fact]
	public void HandlesFromAnotherSystemOnTheSameWorld_AreNotInterchangeable()
	{
		using AbilityRuntime runtime = new();
		using GameplayAbilitySystem other = new(
			runtime.World,
			runtime.Tags,
			runtime.Effects,
			runtime.Attributes);

		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle mine =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);

		GameplayAbilityHandle theirs =
			other.GiveAbility(actor, AbilitySchema.Channel);

		// Both systems allocate the same first slot for the same owner,
		// so only the owning system can tell the handles apart.
		Assert.Equal(mine.Slot, theirs.Slot);
		Assert.Equal(mine.Generation, theirs.Generation);
		Assert.NotEqual(mine, theirs);

		Assert.Equal(
			GameplayAbilityActivationStatus.NotGranted,
			other.TryActivate(mine).Status);
		Assert.False(other.IsActive(mine));
		Assert.False(other.TryGetAbility(mine, out _));
		Assert.False(other.RevokeAbility(mine));
		Assert.True(runtime.Abilities.TryGetAbility(mine, out _));
		Assert.Equal(1, other.GetAbilityCount(actor));
	}

	[Fact]
	public void RevokedGrant_CannotReactivateFromItsCancellation()
	{
		using AbilityRuntime runtime = new();
		AbilityRecorder recorder = new(runtime.Abilities);
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);

		bool cancelled = false;
		GameplayAbilityActivationResult reactivation = default;

		runtime.Abilities.Changed += change =>
		{
			if (change.Kind == GameplayAbilityChangeKind.Cancelled
				&& change.Handle == handle
				&& !cancelled)
			{
				cancelled = true;
				reactivation = runtime.Abilities.TryActivate(change.Handle);
			}
		};

		Assert.True(runtime.Abilities.RevokeAbility(handle));

		Assert.True(cancelled);
		Assert.Equal(
			GameplayAbilityActivationStatus.NotGranted,
			reactivation.Status);
		Assert.False(runtime.Abilities.TryGetAbility(handle, out _));
		Assert.Equal(0, runtime.Abilities.GrantedAbilityCount);
		Assert.Equal(0, runtime.Abilities.ActiveAbilityCount);
		Assert.Equal(0, runtime.Abilities.GetActiveAbilityCount(actor));
		Assert.Equal("Granted,Activated,Cancelled,Revoked", recorder.Sequence());
	}

	[Fact]
	public void RevokeFromCancellation_DoesNotRevokeTwice()
	{
		using AbilityRuntime runtime = new();
		AbilityRecorder recorder = new(runtime.Abilities);
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);

		bool nested = false;
		bool nestedResult = true;

		runtime.Abilities.Changed += change =>
		{
			if (change.Kind == GameplayAbilityChangeKind.Cancelled
				&& change.Handle == handle
				&& !nested)
			{
				nested = true;
				nestedResult = runtime.Abilities.RevokeAbility(change.Handle);
			}
		};

		Assert.True(runtime.Abilities.RevokeAbility(handle));

		Assert.True(nested);
		Assert.False(nestedResult);
		Assert.False(runtime.Abilities.TryGetAbility(handle, out _));
		Assert.Equal(0, runtime.Abilities.GrantedAbilityCount);
		Assert.Equal(0, runtime.Abilities.ActiveAbilityCount);
		Assert.Equal("Granted,Activated,Cancelled,Revoked", recorder.Sequence());
	}

	[Fact]
	public void RevokeAll_LeavesGrantsAddedByNotificationsAlone()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle first =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);

		GameplayAbilityHandle second =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);

		Assert.True(runtime.Abilities.TryActivate(first).Succeeded);
		Assert.True(runtime.Abilities.TryActivate(second).Succeeded);

		GameplayAbilityHandle replacement = GameplayAbilityHandle.None;

		runtime.Abilities.Changed += change =>
		{
			if (change.Kind == GameplayAbilityChangeKind.Revoked
				&& change.Handle == second
				&& replacement.IsNone)
			{
				replacement =
					runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);
			}
		};

		Assert.Equal(2, runtime.Abilities.RevokeAll(actor));

		Assert.False(runtime.Abilities.TryGetAbility(first, out _));
		Assert.False(runtime.Abilities.TryGetAbility(second, out _));
		Assert.True(runtime.Abilities.TryGetAbility(replacement, out _));
		Assert.Equal(1, runtime.Abilities.GetAbilityCount(actor));
		Assert.Equal(1, runtime.Abilities.GrantedAbilityCount);
		Assert.Equal(0, runtime.Abilities.ActiveAbilityCount);
	}

	[Fact]
	public void CancelAll_LeavesAbilitiesActivatedByNotificationsAlone()
	{
		using AbilityRuntime runtime = new();
		AbilityRecorder recorder = new(runtime.Abilities);
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle first =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);

		GameplayAbilityHandle second =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);

		Assert.True(runtime.Abilities.TryActivate(first).Succeeded);
		Assert.True(runtime.Abilities.TryActivate(second).Succeeded);

		bool reactivated = false;

		runtime.Abilities.Changed += change =>
		{
			if (change.Kind == GameplayAbilityChangeKind.Cancelled
				&& change.Handle == second
				&& !reactivated)
			{
				reactivated = true;

				Assert.True(runtime.Abilities.TryActivate(second).Succeeded);
			}
		};

		Assert.Equal(2, runtime.Abilities.CancelAll(actor));

		Assert.True(reactivated);
		Assert.False(runtime.Abilities.IsActive(first));
		Assert.True(runtime.Abilities.IsActive(second));
		Assert.Equal(1, runtime.Abilities.ActiveAbilityCount);
		Assert.Equal(2, recorder.Count(GameplayAbilityChangeKind.Cancelled));
	}

	[Fact]
	public void CancelByTags_LeavesAbilitiesActivatedByNotificationsAlone()
	{
		using AbilityRuntime runtime = new();
		AbilityRecorder recorder = new(runtime.Abilities);
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle first =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);

		GameplayAbilityHandle second =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);

		Assert.True(runtime.Abilities.TryActivate(first).Succeeded);
		Assert.True(runtime.Abilities.TryActivate(second).Succeeded);

		bool reactivated = false;

		runtime.Abilities.Changed += change =>
		{
			if (change.Kind == GameplayAbilityChangeKind.Cancelled
				&& change.Handle == second
				&& !reactivated)
			{
				reactivated = true;

				Assert.True(runtime.Abilities.TryActivate(second).Succeeded);
			}
		};

		Assert.Equal(
			2,
			runtime.Abilities.CancelByTags(
				actor,
				GameplayTagSet.Create(TagSchema.Ability)));

		Assert.True(reactivated);
		Assert.False(runtime.Abilities.IsActive(first));
		Assert.True(runtime.Abilities.IsActive(second));
		Assert.Equal(1, runtime.Abilities.ActiveAbilityCount);
		Assert.Equal(2, recorder.Count(GameplayAbilityChangeKind.Cancelled));
	}

	[Fact]
	public void CancelOnTags_LeavesAbilitiesActivatedByNotificationsAlone()
	{
		using AbilityRuntime runtime = new();
		AbilityRecorder recorder = new(runtime.Abilities);
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle first = runtime.Abilities.GiveAbility(
			actor,
			AbilitySchema.ChannelCancelOnStun);

		GameplayAbilityHandle second = runtime.Abilities.GiveAbility(
			actor,
			AbilitySchema.ChannelCancelOnStun);

		Assert.True(runtime.Abilities.TryActivate(first).Succeeded);
		Assert.True(runtime.Abilities.TryActivate(second).Succeeded);

		bool reactivated = false;

		runtime.Abilities.Changed += change =>
		{
			if (change.Kind == GameplayAbilityChangeKind.Cancelled
				&& change.Handle == second
				&& !reactivated)
			{
				reactivated = true;

				Assert.True(runtime.Abilities.TryActivate(second).Succeeded);
			}
		};

		Assert.True(runtime.Effects.Apply(actor, EffectSchema.Stun).Applied);

		Assert.True(reactivated);
		Assert.False(runtime.Abilities.IsActive(first));
		Assert.True(runtime.Abilities.IsActive(second));
		Assert.Equal(1, runtime.Abilities.ActiveAbilityCount);
		Assert.Equal(2, recorder.Count(GameplayAbilityChangeKind.Cancelled));
	}

	[Fact]
	public void TryActivateByInput_LeavesGrantsAddedByNotificationsAlone()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle first = runtime.Abilities.GiveAbility(
			actor,
			AbilitySchema.Channel,
			inputId: 1);

		GameplayAbilityHandle second = runtime.Abilities.GiveAbility(
			actor,
			AbilitySchema.Channel,
			inputId: 1);

		GameplayAbilityHandle added = GameplayAbilityHandle.None;

		runtime.Abilities.Changed += change =>
		{
			if (change.Kind == GameplayAbilityChangeKind.Activated
				&& change.Handle == second
				&& added.IsNone)
			{
				added = runtime.Abilities.GiveAbility(
					actor,
					AbilitySchema.Channel,
					inputId: 1);
			}
		};

		Assert.Equal(2, runtime.Abilities.TryActivateByInput(actor, 1));

		Assert.False(added.IsNone);
		Assert.True(runtime.Abilities.IsActive(first));
		Assert.True(runtime.Abilities.IsActive(second));
		Assert.False(runtime.Abilities.IsActive(added));
		Assert.Equal(2, runtime.Abilities.GetActiveAbilityCount(actor));
	}

	[Fact]
	public void Retrigger_RejectsANestedActivationFromItsEndedNotification()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			AbilitySchema.Retriggerable);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);

		bool nested = false;
		GameplayAbilityActivationResult nestedResult = default;

		runtime.Abilities.Changed += change =>
		{
			if (change.Kind == GameplayAbilityChangeKind.Ended
				&& change.Handle == handle
				&& !nested)
			{
				nested = true;
				nestedResult = runtime.Abilities.TryActivate(change.Handle);
			}
		};

		GameplayAbilityActivationResult result =
			runtime.Abilities.TryActivate(handle);

		Assert.True(nested);
		Assert.True(result.Succeeded);
		Assert.Equal(
			GameplayAbilityActivationStatus.AlreadyActive,
			nestedResult.Status);
		Assert.True(runtime.Abilities.IsActive(handle));
		Assert.Equal(1, runtime.Abilities.ActiveAbilityCount);
	}

	[Fact]
	public void CooldownTime_IncludesExternalEffectsThatGrantTheTags()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Dash);

		Assert.True(runtime.Effects.Apply(
			actor,
			AbilitySchema.ExternalDashCooldown).Applied);

		Assert.True(runtime.Abilities.IsOnCooldown(actor, AbilitySchema.Dash));
		Assert.True(double.IsPositiveInfinity(
			runtime.Abilities.GetCooldownTimeRemaining(
				actor,
				AbilitySchema.Dash)));

		// The external tag alone never blocks the ability from having
		// its own cooldown; once the external effect ends, the ability
		// activates and starts its own two-second cooldown.
		Assert.True(runtime.Effects.RemoveAll(actor) == 1);
		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
		Assert.True(runtime.Effects.Apply(actor, LongExternalCooldown).Applied);

		double remaining = runtime.Abilities.GetCooldownTimeRemaining(
			actor,
			AbilitySchema.Dash);

		Assert.InRange(remaining, 9.9, 10.0);
	}

	[Fact]
	public void StaleHandle_DoesNotResolveAfterSlotReuse()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle stale =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);

		Assert.True(runtime.Abilities.RevokeAbility(stale));

		GameplayAbilityHandle replacement =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);

		Assert.Equal(stale.Slot, replacement.Slot);
		Assert.NotEqual(stale, replacement);
		Assert.False(runtime.Abilities.TryGetAbility(stale, out _));
		Assert.False(runtime.Abilities.IsActive(stale));
		Assert.False(runtime.Abilities.RevokeAbility(stale));
		Assert.Equal(
			GameplayAbilityActivationStatus.NotGranted,
			runtime.Abilities.TryActivate(stale).Status);
		Assert.True(runtime.Abilities.TryGetAbility(replacement, out _));
	}
}
