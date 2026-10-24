using Volt.Core.Timing;
using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Cues;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Events;
using Volt.Gameplay.Simulation;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tasks;

namespace Volt.Gameplay.Examples;

/// <summary>
/// Compiles and runs the examples published in the gameplay
/// documentation, so a signature change breaks the build instead of
/// leaving a snippet that no longer works.
/// </summary>
/// <remarks>
/// Every example owns and disposes its own world and stores, exactly as
/// the documentation tells an application to. Names are documentation
/// scoped; the tag and attribute registries are process-wide, so they
/// must not collide with another test project's names.
/// </remarks>
public static class DocumentedExamples
{
	private const string Root = "Volt.DocExamples";

	public static void CreateServices()
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
			attributes);
		using GameplayCueSystem cues = new(world);
		using GameplayCueBuffer buffer = new(64, 4096);

		cues.BindEffects(effects);
		cues.PresentationBuffer = buffer;

		Entity player = world.CreateEntity();

		Assert(world.IsAlive(player), "the world owns a live entity");
		Assert(effects.ActiveEffectCount == 0, "no effects are active yet");
		Assert(cues.ActiveCueCount == 0, "no cues are active yet");
	}

	public static void RegisterAndApplyEffects()
	{
		using World world = new();
		using GameplayAttributeStore attributes = new(world);
		using GameplayTagStore tags = new(world);
		using GameplayEffectSystem effects = new(world, attributes, tags);

		GameplayAttribute health = GameplayAttributeRegistry.Shared.Register(
			$"{Root}.Health",
			100f);
		GameplayTag burning = GameplayTagRegistry.Shared.Register(
			$"{Root}.Status.Burning");

		GameplayEffectDefinition burn = GameplayEffectDefinition
			.Create("Doc.Burn")
			.HasDuration(3f)
			.Period(1f)
			.Modifier(health, GameplayModifierOperation.AddBase, -2f)
			.GrantTags(burning)
			.Build();

		Entity target = world.CreateEntity();
		GameplayEffectApplyResult applied = effects.Apply(target, burn);

		Assert(applied.Status == GameplayEffectApplyStatus.Applied, "the effect applied");
		Assert(tags.HasTagExact(target, burning), "the effect granted its tag");
		Assert(attributes.GetCurrentValue(target, health) == 98f, "the first tick dealt 2");

		effects.Advance(3f);

		Assert(!tags.HasTagExact(target, burning), "expiry withdrew the grant");
		Assert(effects.ActiveEffectCount == 0, "the instance expired");
	}

	public static void DefineAnAbility()
	{
		using World world = new();
		using GameplayAttributeStore attributes = new(world);
		using GameplayTagStore tags = new(world);
		using GameplayEffectSystem effects = new(world, attributes, tags);
		using GameplayAbilitySystem abilities = new(
			world,
			tags,
			effects,
			attributes);

		GameplayAttribute mana = GameplayAttributeRegistry.Shared.Register(
			$"{Root}.Mana",
			50f);
		GameplayTag stunned = GameplayTagRegistry.Shared.Register(
			$"{Root}.Status.Stunned");
		GameplayTag cooldownDash = GameplayTagRegistry.Shared.Register(
			$"{Root}.Cooldown.Dash");

		GameplayEffectDefinition manaCost = GameplayEffectDefinition
			.Create("Doc.Dash.Cost")
			.Instant()
			.Modifier(mana, GameplayModifierOperation.AddBase, -10f)
			.Build();

		GameplayEffectDefinition dashCooldown = GameplayEffectDefinition
			.Create("Doc.Dash.Cooldown")
			.HasDuration(2f)
			.GrantTags(cooldownDash)
			.Build();

		GameplayAbilityDefinition dash = GameplayAbilityDefinition
			.Create("Doc.Ability.Dash")
			.AssetTags(cooldownDash)
			.Cost(manaCost)
			.Cooldown(dashCooldown)
			.RequireToActivate(GameplayTagRequirements.Ignore(
				GameplayTagSet.Create(stunned)))
			.Build();

		Entity player = world.CreateEntity();
		GameplayAbilityHandle handle = abilities.GiveAbility(player, dash);
		GameplayAbilityActivationResult result = abilities.TryActivate(handle);

		Assert(
			result.Status == GameplayAbilityActivationStatus.ActivatedAndEnded,
			"the instant ability activated and ended");
		Assert(abilities.IsOnCooldown(player, dash), "the cooldown is running");
		Assert(!abilities.TryActivate(handle).Succeeded, "the cooldown blocks a retry");
	}

	public static void AuthorAConditionalPlan()
	{
		using World world = new();
		using GameplayAttributeStore attributes = new(world);
		using GameplayTagStore tags = new(world);
		using GameplayEffectSystem effects = new(world, attributes, tags);
		using GameplayAbilitySystem abilities = new(
			world,
			tags,
			effects,
			attributes);

		GameplayAttribute health = GameplayAttributeRegistry.Shared.Register(
			$"{Root}.Conditional.Health",
			100f);
		GameplayTag stunned = GameplayTagRegistry.Shared.Register(
			$"{Root}.Conditional.Stunned");

		GameplayEffectDefinition executeEffect = GameplayEffectDefinition
			.Create("Doc.Finisher.Execute")
			.Instant()
			.Modifier(health, GameplayModifierOperation.AddBase, -50f)
			.Build();

		GameplayEffectDefinition standardEffect = GameplayEffectDefinition
			.Create("Doc.Finisher.Standard")
			.Instant()
			.Modifier(health, GameplayModifierOperation.AddBase, -5f)
			.Build();

		GameplayCondition wounded = GameplayCondition.OwnerAttribute(
			health,
			GameplayAttributeComparison.LessThan,
			30f);
		GameplayCondition vulnerable = GameplayCondition.All(
			wounded,
			GameplayCondition.TargetHasTag(stunned));

		GameplayAbilityDefinition finisher = GameplayAbilityDefinition
			.Create("Doc.Ability.Finisher")
			.Plan(
				GameplayAbilityPlan
					.Create()
					.If(vulnerable)
					.ApplyEffect(executeEffect, GameplayAbilityEffectTarget.Target)
					.Else()
					.ApplyEffect(standardEffect, GameplayAbilityEffectTarget.Target)
					.EndIf()
					.End()
					.Build())
			.Build();

		Entity player = world.CreateEntity();
		Entity target = world.CreateEntity();

		attributes.SetBaseValue(player, health, 20f);

		GameplayAbilityHandle handle = abilities.GiveAbility(player, finisher);

		Assert(
			abilities.TryActivate(handle, target).Status
				== GameplayAbilityActivationStatus.ActivatedAndEnded,
			"the plan ran");

		// The owner was wounded but the target was not stunned, so the
		// standard branch applied.
		Assert(
			attributes.GetCurrentValue(target, health) == 95f,
			"the standard branch applied");
	}

	public static void RegisterACustomTask()
	{
		using World world = new();
		using GameplayAttributeStore attributes = new(world);
		using GameplayTagStore tags = new(world);
		using GameplayEffectSystem effects = new(world, attributes, tags);
		using GameplayAbilitySystem abilities = new(
			world,
			tags,
			effects,
			attributes);

		GameplayAttribute health = GameplayAttributeRegistry.Shared.Register(
			$"{Root}.Custom.Health",
			100f);

		GameplayEffectDefinition hitEffect = GameplayEffectDefinition
			.Create("Doc.Attack.Hit")
			.Instant()
			.Modifier(health, GameplayModifierOperation.AddBase, -10f)
			.Build();

		GameplayAbilityTaskDefinition swing = abilities
			.RegisterTaskDefinition("Swing");

		GameplayAbilityDefinition attack = GameplayAbilityDefinition
			.Create("Doc.Ability.Attack")
			.Plan(
				GameplayAbilityPlan
					.Create()
					.WaitCustom(swing)
					.ApplyEffect(hitEffect, GameplayAbilityEffectTarget.Target)
					.End()
					.Build())
			.Build();

		Entity player = world.CreateEntity();
		Entity target = world.CreateEntity();
		GameplayAbilityHandle handle = abilities.GiveAbility(player, attack);
		GameplayAbilityActivationResult result = abilities.TryActivate(handle, target);

		Assert(result.Succeeded, "the plan started and is waiting for the swing");

		// The plan owns the task it started; application code signals
		// completion through that handle rather than creating a second
		// task for the same definition.
		Span<GameplayAbilityTaskHandle> pending =
			stackalloc GameplayAbilityTaskHandle[1];

		Assert(
			abilities.Tasks.CopyTasks(player, pending) == 1,
			"the plan started exactly one task");
		Assert(abilities.Tasks.Complete(pending[0]), "the application completed it");
		Assert(
			abilities.Tasks.PendingTaskCount == 0,
			"the plan resumed and ended");
		Assert(attributes.GetCurrentValue(target, health) == 90f, "the hit landed");
	}

	public static void AuthorACompositeWait()
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

		GameplayAttribute health = GameplayAttributeRegistry.Shared.Register(
			$"{Root}.Composite.Health",
			100f);
		GameplayTag incomingHit = GameplayTagRegistry.Shared.Register(
			$"{Root}.Event.Hit");
		GameplayTag stunned = GameplayTagRegistry.Shared.Register(
			$"{Root}.Composite.Stunned");

		GameplayEffectDefinition counterEffect = GameplayEffectDefinition
			.Create("Doc.Counter.Hit")
			.Instant()
			.Modifier(health, GameplayModifierOperation.AddBase, -7f)
			.Build();

		GameplayWaitAny guard = GameplayWaitAny
			.Create()
			.WithTimeout(0.4f)
			.OnEvent(incomingHit)
			.OnTag(stunned, GameplayTagWaitMode.Present);

		GameplayAbilityPlan plan = GameplayAbilityPlan
			.Create()
			.WaitAny(guard)
			.ApplyEffect(counterEffect, GameplayAbilityEffectTarget.Target)
			.End()
			.Build();

		GameplayAbilityDefinition counter = GameplayAbilityDefinition
			.Create("Doc.Ability.Counter")
			.Plan(plan)
			.Build();

		Entity player = world.CreateEntity();
		Entity target = world.CreateEntity();
		GameplayAbilityHandle handle = abilities.GiveAbility(player, counter);

		Assert(abilities.TryActivate(handle, target).Succeeded, "the wait armed");

		events.Publish(new GameplayEvent(incomingHit, source: target, target: player));
		abilities.Tasks.Advance(0.001f);

		Assert(
			attributes.GetCurrentValue(target, health) == 93f,
			"the event won the race and the counter landed");
	}

	public static void IntegrateWithTheFixedStep()
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

		GameplayAttribute health = GameplayAttributeRegistry.Shared.Register(
			$"{Root}.Simulation.Health",
			100f);

		GameplayEffectDefinition burn = GameplayEffectDefinition
			.Create("Doc.Simulation.Burn")
			.HasDuration(1f)
			.Period(0.5f)
			.Modifier(health, GameplayModifierOperation.AddBase, -1f)
			.Build();

		GameLoop loop = new();
		GameplaySimulation simulation = new(loop, abilities, effects);

		try
		{
			Entity target = world.CreateEntity();
			effects.Apply(target, burn);

			// The integration owns the clock while it is attached.
			Assert(effects.ActiveEffectCount == 1, "the burn is active");

			try
			{
				effects.Advance(0.1f);
				throw new InvalidOperationException(
					"manual advancement must be rejected while attached");
			}
			catch (InvalidOperationException)
			{
			}
		}
		finally
		{
			simulation.Dispose();
		}

		Assert(effects.ActiveEffectCount == 1, "manual control returned");
		effects.Advance(2f);
		Assert(effects.ActiveEffectCount == 0, "the burn expired");
	}

	public static void SubscribeAndBufferCues()
	{
		using World world = new();
		using GameplayCueSystem cues = new(world);
		using GameplayCueBuffer buffer = new(16, 256);

		GameplayTag hit = GameplayTagRegistry.Shared.Register($"{Root}.Cue.Hit");
		List<GameplayCueKind> seen = [];

		GameplayCueSubscription listener = cues.Subscribe(
			hit,
			cue => seen.Add(cue.Kind));

		cues.PresentationBuffer = buffer;
		cues.Execute(hit, magnitude: 12f);

		GameplayCueHandle looping = cues.Add(hit, stackCount: 1);
		cues.Update(looping, magnitude: 3f, stackCount: 2);
		cues.Remove(looping);
		cues.Unsubscribe(listener);

		Assert(
			seen.SequenceEqual(
			[
				GameplayCueKind.Execute,
				GameplayCueKind.Added,
				GameplayCueKind.Updated,
				GameplayCueKind.Removed
			]),
			"the listener saw every transition in order");

		Span<GameplayCueEvent> drained = stackalloc GameplayCueEvent[16];
		int count = buffer.Drain(drained);

		Assert(count == 4, "the buffer drained every transition");
		Assert(
			drained[0].Kind == GameplayCueKind.Execute
				&& drained[0].Magnitude == 12f,
			"the one-shot survived the handoff");
	}

	public static void DriveCuesFromEffectsAndActivations()
	{
		using World world = new();
		using GameplayAttributeStore attributes = new(world);
		using GameplayTagStore tags = new(world);
		using GameplayEffectSystem effects = new(world, attributes, tags);
		using GameplayAbilitySystem abilities = new(
			world,
			tags,
			effects,
			attributes);
		// The ability system owns its cue router; use that instance so
		// activation-owned cues and effect-owned cues meet in one place.
		GameplayCueSystem cues = abilities.Cues;

		cues.BindEffects(effects);

		GameplayTag burning = GameplayTagRegistry.Shared.Register(
			$"{Root}.Cue.Burning");
		GameplayTag slow = GameplayTagRegistry.Shared.Register($"{Root}.Cue.Slow");
		GameplayTag cast = GameplayTagRegistry.Shared.Register($"{Root}.Cue.Cast");

		GameplayEffectDefinition burn = GameplayEffectDefinition
			.Create("Doc.Cue.Burn")
			.Infinite()
			.GameplayCue(burning)
			.Build();

		GameplayAbilityDefinition channel = GameplayAbilityDefinition
			.Create("Doc.Ability.Channel")
			.EndOnActivate(false)
			.Plan(
				GameplayAbilityPlan
					.Create()
					.ExecuteCue(slow)
					.AddCue(cast)
					.Wait(0.5f)
					.End()
					.Build())
			.Build();

		Entity target = world.CreateEntity();
		effects.Apply(target, burn);

		Assert(cues.ActiveCueCount == 1, "the effect owns one persistent cue");

		Entity caster = world.CreateEntity();
		GameplayAbilityHandle handle = abilities.GiveAbility(caster, channel);
		GameplayAbilityActivationResult result = abilities.TryActivate(handle);

		Assert(result.Succeeded, "the channel started");
		Assert(cues.ActiveCueCount == 2, "the activation owns one persistent cue");

		abilities.Cancel(handle);

		Assert(cues.ActiveCueCount == 1, "cancellation released the activation cue");

		effects.RemoveAll(target);

		Assert(cues.ActiveCueCount == 0, "removing the effect released its cue");
	}

	private static void Assert(bool condition, string message)
	{
		if (!condition)
		{
			throw new InvalidOperationException(
				$"Documented example failed: {message}.");
		}
	}
}
