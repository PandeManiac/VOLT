using Volt.Ecs;
using Volt.Gameplay.Abilities;

namespace Volt.Gameplay.Tests.Abilities;

public sealed class GameplayAbilityGrantTests
{
	[Fact]
	public void GiveAbility_ExposesTheGrant()
	{
		using AbilityRuntime runtime = new();
		AbilityRecorder recorder = new(runtime.Abilities);
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			AbilitySchema.Dash,
			level: 2,
			inputId: 3);

		Assert.False(handle.IsNone);
		Assert.Equal(actor, handle.Owner);
		Assert.Equal(1, runtime.Abilities.GetAbilityCount(actor));
		Assert.Equal(1, runtime.Abilities.GrantedAbilityCount);

		Assert.True(runtime.Abilities.TryGetAbility(
			handle,
			out GrantedGameplayAbility granted));

		Assert.Same(AbilitySchema.Dash, granted.Definition);
		Assert.Equal(actor, granted.Owner);
		Assert.Equal(2, granted.Level);
		Assert.Equal(3, granted.InputId);
		Assert.False(granted.IsActive);

		Assert.Equal("Granted", recorder.Sequence());
		Assert.Equal(handle, recorder.Last(GameplayAbilityChangeKind.Granted).Handle);
	}

	[Fact]
	public void GiveAbility_RejectsInvalidArguments()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		Assert.Throws<ArgumentNullException>(() =>
			runtime.Abilities.GiveAbility(actor, null!));

		Assert.Throws<ArgumentException>(() =>
			runtime.Abilities.GiveAbility(default, AbilitySchema.Dash));

		Assert.Throws<ArgumentOutOfRangeException>(() =>
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Dash, level: 0));

		Assert.Equal(0, runtime.Abilities.GetAbilityCount(actor));
	}

	[Fact]
	public void GiveAbility_AllowsTwoGrantsOfOneDefinition()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle first =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Dash);

		GameplayAbilityHandle second =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Dash);

		Assert.NotEqual(first, second);
		Assert.Equal(2, runtime.Abilities.GetAbilityCount(actor));

		Assert.True(runtime.Abilities.RevokeAbility(first));

		Assert.Equal(1, runtime.Abilities.GetAbilityCount(actor));
		Assert.True(runtime.Abilities.IsActive(second) == false);
	}

	[Fact]
	public void RevokeAbility_RemovesTheGrant()
	{
		using AbilityRuntime runtime = new();
		AbilityRecorder recorder = new(runtime.Abilities);
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Dash);

		Assert.True(runtime.Abilities.RevokeAbility(handle));

		Assert.False(runtime.Abilities.TryGetAbility(handle, out _));
		Assert.Equal(0, runtime.Abilities.GetAbilityCount(actor));
		Assert.Equal(0, runtime.Abilities.GrantedAbilityCount);
		Assert.False(runtime.Abilities.RevokeAbility(handle));

		Assert.Equal(
			GameplayAbilityActivationStatus.NotGranted,
			runtime.Abilities.TryActivate(handle).Status);

		Assert.Equal("Granted,Revoked", recorder.Sequence());
	}

	[Fact]
	public void RevokeAbility_CancelsAnActiveInstanceFirst()
	{
		using AbilityRuntime runtime = new();
		AbilityRecorder recorder = new(runtime.Abilities);
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
		Assert.True(runtime.Abilities.IsActive(handle));

		Assert.True(runtime.Abilities.RevokeAbility(handle));

		Assert.Equal(0, runtime.Abilities.ActiveAbilityCount);
		Assert.Equal(
			"Granted,Activated,Cancelled,Revoked",
			recorder.Sequence());
	}

	[Fact]
	public void RevokeAll_RemovesEveryGrant()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);
		runtime.Abilities.GiveAbility(actor, AbilitySchema.Dash);
		runtime.Abilities.GiveAbility(actor, AbilitySchema.Jump);

		Assert.Equal(3, runtime.Abilities.RevokeAll(actor));
		Assert.Equal(0, runtime.Abilities.GetAbilityCount(actor));
		Assert.Equal(0, runtime.Abilities.GrantedAbilityCount);
		Assert.Equal(0, runtime.Abilities.RevokeAll(actor));
	}

	[Fact]
	public void CopyAbilities_AndInputLookup_FindEveryGrant()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle dash = runtime.Abilities.GiveAbility(
			actor,
			AbilitySchema.Dash,
			inputId: 1);

		GameplayAbilityHandle jump = runtime.Abilities.GiveAbility(
			actor,
			AbilitySchema.Jump,
			inputId: 2);

		GameplayAbilityHandle channel = runtime.Abilities.GiveAbility(
			actor,
			AbilitySchema.Channel,
			inputId: 1);

		Span<GameplayAbilityHandle> handles = stackalloc GameplayAbilityHandle[4];

		Assert.Equal(3, runtime.Abilities.CopyAbilities(actor, handles));
		Assert.Contains(dash, handles[..3].ToArray());
		Assert.Contains(jump, handles[..3].ToArray());
		Assert.Contains(channel, handles[..3].ToArray());

		Span<GameplayAbilityHandle> small = stackalloc GameplayAbilityHandle[1];

		Assert.Equal(1, runtime.Abilities.CopyAbilities(actor, small));

		Assert.True(runtime.Abilities.TryFindAbilityByInput(
			actor,
			2,
			out GameplayAbilityHandle found));

		Assert.Equal(jump, found);

		Assert.False(runtime.Abilities.TryFindAbilityByInput(actor, 9, out _));

		Assert.True(runtime.Abilities.TryFindAbility(
			actor,
			AbilitySchema.Channel,
			out GrantedGameplayAbility ability));

		Assert.Equal(channel, ability.Handle);
		Assert.False(runtime.Abilities.TryFindAbility(
			actor,
			AbilitySchema.Guard,
			out _));
	}

	[Fact]
	public void DestroyEntity_ReleasesGrantsWithoutNotification()
	{
		using AbilityRuntime runtime = new();
		AbilityRecorder recorder = new(runtime.Abilities);
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);

		runtime.World.DestroyEntity(actor);

		Assert.Equal(0, runtime.Abilities.GrantedAbilityCount);
		Assert.Equal(0, runtime.Abilities.ActiveAbilityCount);
		Assert.False(runtime.Abilities.TryGetAbility(handle, out _));
		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.Equal("Granted,Activated", recorder.Sequence());
	}

	[Fact]
	public void RecycledEntityIndex_HasNoGrants()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);

		runtime.World.DestroyEntity(actor);

		Entity replacement = runtime.CreateActor();

		Assert.Equal(actor.Index, replacement.Index);
		Assert.NotEqual(actor, replacement);
		Assert.Equal(0, runtime.Abilities.GetAbilityCount(replacement));
		Assert.False(runtime.Abilities.TryGetAbility(handle, out _));
	}

	[Fact]
	public void HandleFromAnotherSystem_IsNotGranted()
	{
		using AbilityRuntime runtime = new();
		using AbilityRuntime other = new();

		Entity foreignActor = other.CreateActor();

		GameplayAbilityHandle foreignHandle =
			other.Abilities.GiveAbility(foreignActor, AbilitySchema.Dash);

		Assert.Equal(
			GameplayAbilityActivationStatus.NotGranted,
			runtime.Abilities.TryActivate(foreignHandle).Status);
	}
}
