using Volt.Gameplay.Abilities;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tests.Effects;
using Volt.Gameplay.Tests.Tags;

namespace Volt.Gameplay.Tests.Abilities;

public sealed class GameplayAbilityLifecycleTests
{
	[Fact]
	public void PersistentAbility_StaysActiveUntilEnded()
	{
		using AbilityRuntime runtime = new();
		AbilityRecorder recorder = new(runtime.Abilities);
		var actor = runtime.CreateActor();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);

		GameplayAbilityActivationResult result =
			runtime.Abilities.TryActivate(handle);

		Assert.Equal(GameplayAbilityActivationStatus.Activated, result.Status);
		Assert.True(runtime.Abilities.IsActive(handle));
		Assert.Equal(1, runtime.Abilities.GetActiveAbilityCount(actor));
		Assert.Equal(1, runtime.Abilities.ActiveAbilityCount);

		Assert.True(runtime.Abilities.End(handle));
		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.Equal(0, runtime.Abilities.ActiveAbilityCount);
		Assert.False(runtime.Abilities.End(handle));
		Assert.False(runtime.Abilities.Cancel(handle));

		Assert.Equal("Granted,Activated,Ended", recorder.Sequence());
	}

	[Fact]
	public void Cancel_ReportsCancellation()
	{
		using AbilityRuntime runtime = new();
		AbilityRecorder recorder = new(runtime.Abilities);
		var actor = runtime.CreateActor();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
		Assert.True(runtime.Abilities.Cancel(handle));

		Assert.Equal(
			GameplayAbilityChangeKind.Cancelled,
			recorder.Last(GameplayAbilityChangeKind.Cancelled).Kind);
		Assert.Equal(0, runtime.Abilities.ActiveAbilityCount);
	}

	[Fact]
	public void CancelByTags_MatchesTheAssetTagHierarchy()
	{
		using AbilityRuntime runtime = new();
		var actor = runtime.CreateActor();

		GameplayAbilityHandle channel =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);

		GameplayAbilityHandle guard =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Guard);

		Assert.True(runtime.Abilities.TryActivate(channel).Succeeded);
		Assert.True(runtime.Abilities.TryActivate(guard).Succeeded);

		Assert.Equal(
			2,
			runtime.Abilities.CancelByTags(
				actor,
				GameplayTagSet.Create(TagSchema.Ability)));

		Assert.False(runtime.Abilities.IsActive(channel));
		Assert.False(runtime.Abilities.IsActive(guard));
		Assert.Equal(0, runtime.Abilities.CancelByTags(
			actor,
			GameplayTagSet.Create(TagSchema.Ability)));
	}

	[Fact]
	public void CancelAll_EndsEveryActiveAbility()
	{
		using AbilityRuntime runtime = new();
		var actor = runtime.CreateActor();

		runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);
		runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);

		Span<GameplayAbilityHandle> handles =
			stackalloc GameplayAbilityHandle[2];

		Assert.Equal(2, runtime.Abilities.CopyAbilities(actor, handles));

		foreach (GameplayAbilityHandle handle in handles)
		{
			Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
		}

		Assert.Equal(2, runtime.Abilities.CancelAll(actor));
		Assert.Equal(0, runtime.Abilities.ActiveAbilityCount);
	}

	[Fact]
	public void CancelOnTags_CancelsWhenTheTagArrives()
	{
		using AbilityRuntime runtime = new();
		AbilityRecorder recorder = new(runtime.Abilities);
		var actor = runtime.CreateActor();

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			AbilitySchema.ChannelCancelOnStun);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);

		Assert.True(runtime.Effects.Apply(actor, EffectSchema.Stun).Applied);

		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.Equal(
			1,
			recorder.Count(GameplayAbilityChangeKind.Cancelled));
	}

	[Fact]
	public void CancelOnTags_IgnoresTagsPresentBeforeActivation()
	{
		// The tag that cancels the ability was already present, so the
		// activation itself raises no tag change. A later, unrelated
		// tag change re-evaluates the cancel tags and cancels then.
		using AbilityRuntime runtime = new();
		var actor = runtime.CreateActor();

		Assert.True(runtime.Effects.Apply(actor, EffectSchema.Stun).Applied);

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			AbilitySchema.ChannelCancelOnStun);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
		Assert.True(runtime.Abilities.IsActive(handle));

		Assert.True(runtime.Effects.Apply(
			actor,
			EffectSchema.BuffFiveSeconds).Applied);

		Assert.False(runtime.Abilities.IsActive(handle));
	}

	[Fact]
	public void CancelAbilitiesWithTags_CancelsOnActivation()
	{
		using AbilityRuntime runtime = new();
		AbilityRecorder recorder = new(runtime.Abilities);
		var actor = runtime.CreateActor();

		GameplayAbilityHandle channel =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);

		GameplayAbilityHandle shove =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Shove);

		Assert.True(runtime.Abilities.TryActivate(channel).Succeeded);
		Assert.True(runtime.Abilities.TryActivate(shove).Succeeded);

		Assert.False(runtime.Abilities.IsActive(channel));
		Assert.False(runtime.Abilities.IsActive(shove));

		// The cancelling ability marks itself active and cancels its
		// targets before it raises its own Activated notification.
		Assert.Equal(
			"Granted,Granted,Activated,Cancelled,Activated,Ended",
			recorder.Sequence());
	}

	[Fact]
	public void RevokingFromActivatedNotification_ReportsEndedDuringActivation()
	{
		using AbilityRuntime runtime = new();
		AbilityRecorder recorder = new(runtime.Abilities);

		runtime.Abilities.Changed += change =>
		{
			if (change.Kind == GameplayAbilityChangeKind.Activated)
			{
				runtime.Abilities.RevokeAbility(change.Handle);
			}
		};

		var actor = runtime.CreateActor();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);

		GameplayAbilityActivationResult result =
			runtime.Abilities.TryActivate(handle);

		Assert.Equal(
			GameplayAbilityActivationStatus.EndedDuringActivation,
			result.Status);

		Assert.False(result.Succeeded);
		Assert.False(runtime.Abilities.TryGetAbility(handle, out _));
		Assert.Equal(0, runtime.Abilities.GrantedAbilityCount);

		Assert.Equal(
			"Granted,Activated,Cancelled,Revoked",
			recorder.Sequence());
	}

	[Fact]
	public void DestroyingOwnerFromActivatedNotification_ReportsEndedDuringActivation()
	{
		using AbilityRuntime runtime = new();

		runtime.Abilities.Changed += change =>
		{
			if (change.Kind == GameplayAbilityChangeKind.Activated)
			{
				runtime.World.DestroyEntity(change.Owner);
			}
		};

		var actor = runtime.CreateActor();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);

		GameplayAbilityActivationResult result =
			runtime.Abilities.TryActivate(handle);

		Assert.Equal(
			GameplayAbilityActivationStatus.EndedDuringActivation,
			result.Status);

		Assert.Equal(0, runtime.Abilities.GrantedAbilityCount);
		Assert.Equal(0, runtime.Abilities.ActiveAbilityCount);
	}

	[Fact]
	public void Dispose_RejectsFurtherUse()
	{
		AbilityRuntime runtime = new();

		try
		{
			var actor = runtime.CreateActor();

			GameplayAbilityHandle handle =
				runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);

			runtime.Abilities.Dispose();

			Assert.Throws<ObjectDisposedException>(() =>
				runtime.Abilities.TryActivate(handle));

			Assert.Throws<ObjectDisposedException>(() =>
				runtime.Abilities.GiveAbility(actor, AbilitySchema.Dash));

			Assert.Throws<ObjectDisposedException>(() =>
				runtime.Abilities.GetAbilityCount(actor));

			// Disposing twice is harmless, which keeps a using block
			// honest after an explicit dispose.
			runtime.Abilities.Dispose();
		}
		finally
		{
			runtime.Dispose();
		}
	}
}
