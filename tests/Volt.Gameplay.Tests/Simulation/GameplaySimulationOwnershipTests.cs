using Volt.Core.Timing;
using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Events;
using Volt.Gameplay.Simulation;
using Volt.Gameplay.Tags;

namespace Volt.Gameplay.Tests.Simulation;

/// <summary>
/// Covers clock-claim ownership: an integration releases only the
/// claims it acquired, and it advances exactly the effect system its
/// abilities use.
/// </summary>
public sealed class GameplaySimulationOwnershipTests
{
	[Fact]
	public void RejectedSecondIntegration_DoesNotDetachTheFirstIntegration()
	{
		using GameplaySimulationRuntime first = new();
		using GameplayEventSystem otherEvents = new(first.World);
		using GameplayAbilitySystem otherAbilities = new(
			first.World,
			first.Tags,
			first.Effects,
			first.Attributes,
			otherEvents);
		GameLoop otherLoop = new(
			GameLoopSettings.Default,
			new GameplaySimulationRuntime.FakeMonotonicClock());

		Assert.NotNull(first.Tasks);

		// The effect system is already claimed by the first
		// integration, so this construction fails before it acquires
		// anything.
		Assert.Throws<InvalidOperationException>(
			() => new GameplaySimulation(
				otherLoop,
				otherAbilities,
				first.Effects));

		// The first integration keeps both of its claims, so manual
		// advancement cannot double-advance the clock.
		Assert.True(first.Simulation.IsAttached);
		Assert.Throws<InvalidOperationException>(
			() => first.Effects.Advance(1f));
		Assert.Throws<InvalidOperationException>(
			() => first.Tasks.Advance(1f));

		first.Step();

		Assert.Equal(
			first.Loop.FixedDeltaSeconds,
			first.Effects.Time,
			9);
		Assert.Equal(
			first.Loop.FixedDeltaSeconds,
			first.Tasks.Time,
			9);
	}

	[Fact]
	public void ForeignClaim_IsNotReleasedByAFailedAcquisition()
	{
		using GameplaySimulationRuntime runtime = new();
		SimulationClockClaim foreign = new();

		Assert.NotNull(runtime.Tasks);

		Assert.Throws<InvalidOperationException>(
			() => runtime.Effects.BeginSimulation(foreign));
		Assert.Throws<InvalidOperationException>(
			() => runtime.Abilities.BeginSimulation(foreign));
		Assert.Throws<InvalidOperationException>(
			() => runtime.Tasks.BeginSimulation(foreign));

		// Cleanup after a failed acquisition releases only the claims
		// that acquisition actually took.
		runtime.Effects.EndSimulation(foreign);
		runtime.Abilities.EndSimulation(foreign);
		runtime.Tasks.EndSimulation(foreign);

		Assert.Throws<InvalidOperationException>(
			() => runtime.Effects.Advance(1f));
		Assert.Throws<InvalidOperationException>(
			() => runtime.Tasks.Advance(1f));

		runtime.Step();

		Assert.Equal(
			runtime.Loop.FixedDeltaSeconds,
			runtime.Effects.Time,
			9);
		Assert.Equal(
			runtime.Loop.FixedDeltaSeconds,
			runtime.Tasks.Time,
			9);
	}

	[Fact]
	public void Integration_RejectsAnEffectSystemTheAbilitiesDoNotUse()
	{
		using World world = new();
		using GameplayAttributeStore attributes = new(world);
		using GameplayTagStore tags = new(world);
		using GameplayEffectSystem effects = new(world, attributes, tags);
		using GameplayEffectSystem otherEffects = new(
			world,
			attributes,
			tags);
		using GameplayEventSystem events = new(world);
		using GameplayAbilitySystem abilities = new(
			world,
			tags,
			effects,
			attributes,
			events);
		GameLoop loop = new();

		ArgumentException error = Assert.Throws<ArgumentException>(
			() => new GameplaySimulation(loop, abilities, otherEffects));

		Assert.Equal("effects", error.ParamName);

		// Both clocks remain under manual control, and a correct
		// integration can still be created afterwards.
		otherEffects.Advance(1f);
		effects.Advance(1f);
		abilities.Tasks.Advance(1f);

		using GameplaySimulation simulation = new(loop, abilities, effects);

		Assert.True(simulation.IsAttached);
		Assert.Equal(effects, simulation.Effects);
		Assert.Throws<InvalidOperationException>(
			() => effects.Advance(1f));
		Assert.Throws<InvalidOperationException>(
			() => abilities.Tasks.Advance(1f));
	}

	[Fact]
	public void FailedCallbackRegistration_ReleasesEveryClaimItAcquired()
	{
		using World world = new();
		using GameplayAttributeStore attributes = new(world);
		using GameplayTagStore tags = new(world);
		using GameplayEffectSystem effects = new(world, attributes, tags);
		using GameplayEventSystem events = new(world);
		using GameplayAbilitySystem abilities = new(
			world,
			tags,
			effects,
			attributes,
			events);
		GameplaySimulationRuntime.FakeMonotonicClock clock = new();
		GameLoop loop = new(GameLoopSettings.Default, clock);

		Assert.NotNull(abilities.Tasks);

		bool attempted = false;

		loop.RegisterFixedUpdate((in FixedUpdateContext context) =>
		{
			if (attempted)
			{
				return;
			}

			attempted = true;

			// The loop rejects registrations while a frame runs, so
			// this construction claims both clocks and then fails.
			using GameplaySimulation rejected = new(
				loop,
				abilities,
				effects);
		});

		clock.AdvanceNanoseconds(GameplaySimulationRuntime.FrameNanoseconds);

		Assert.Throws<InvalidOperationException>(() => loop.Advance());
		Assert.True(attempted);

		// No claim survived the failure.
		effects.Advance(0.25f);
		abilities.Tasks.Advance(0.25f);

		double effectsTime = effects.Time;
		double tasksTime = abilities.Tasks.Time;

		clock.AdvanceNanoseconds(GameplaySimulationRuntime.FrameNanoseconds);
		loop.Advance();

		// The rejected callback was never registered, so a later
		// frame advances nothing.
		Assert.Equal(effectsTime, effects.Time, 9);
		Assert.Equal(tasksTime, abilities.Tasks.Time, 9);
	}

	[Fact]
	public void IndependentWorlds_OwnTheirClocksSeparately()
	{
		using GameplaySimulationRuntime first = new();
		using GameplaySimulationRuntime second = new();

		Assert.True(first.Simulation.IsAttached);
		Assert.True(second.Simulation.IsAttached);

		first.Step();

		Assert.Equal(
			first.Loop.FixedDeltaSeconds,
			first.Effects.Time,
			9);
		Assert.Equal(0d, second.Effects.Time, 9);
		Assert.Equal(0d, second.Tasks.Time, 9);

		first.Simulation.Dispose();

		first.Effects.Advance(0.5f);

		Assert.Throws<InvalidOperationException>(
			() => second.Effects.Advance(0.5f));
		Assert.Throws<InvalidOperationException>(
			() => second.Tasks.Advance(0.5f));
	}
}
