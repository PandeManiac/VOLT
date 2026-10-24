using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Cues;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Events;
using Volt.Gameplay.Tags;

namespace Volt.GameplayDemo;

internal static class GameplayCueDemo
{
	public static void Run()
	{
		using World world = new();
		using GameplayAttributeStore attributes = new(world);
		using GameplayTagStore tags = new(world);
		using GameplayEffectSystem effects = new(world, attributes, tags);
		using GameplayCueSystem cues = new(world);
		using GameplayCueBuffer buffer = new(8, 64);
		cues.PresentationBuffer = buffer;
		cues.BindEffects(effects);
		Entity hero = world.CreateEntity();
		Entity enemy = world.CreateEntity();
		GameplayTag burnCue = GameplayTagRegistry.Shared.Register("Demo.Cue.Burning");
		GameplayTag igniteCue = GameplayTagRegistry.Shared.Register("Demo.Cue.Ignite");
		GameplayTag chargeCue = GameplayTagRegistry.Shared.Register("Demo.Cue.Charging");
		GameplayTag releaseCue = GameplayTagRegistry.Shared.Register("Demo.Cue.Release");
		GameplayTag releaseEvent = GameplayTagRegistry.Shared.Register("Demo.Event.Release");
		GameplayTag collapseCue = GameplayTagRegistry.Shared.Register("Demo.Cue.Collapse");
		MockPresentation presentation = new();

		GameplayEffectDefinition burning = GameplayEffectDefinition.Create("Demo.Burning")
			.HasDuration(2f).Period(1f).Stacking(GameplayEffectStackingType.AggregateByTarget)
			.GameplayCue(burnCue).Build();
		GameplayEffectHandle burn = effects.Apply(enemy, burning, hero).Handle;
		cues.Execute(igniteCue, hero, enemy);
		effects.Apply(enemy, burning, hero);
		effects.Advance(1f);
		effects.Advance(1f);
		Assert(!effects.TryGetEffect(burn, out _), "Burning effect expires after two seconds.");

		GameplayEventSystem events = new(world);
		GameplayAbilitySystem abilities = new(world, tags, effects, attributes, events);
		GameplayAbilityPlan chargePlan = GameplayAbilityPlan.Create()
			.AddCue(chargeCue)
			.WaitEvent(releaseEvent)
			.ExecuteCue(releaseCue)
			.End()
			.Build();
		GameplayAbilityDefinition charge = GameplayAbilityDefinition.Create("Demo.ChargedAttack")
			.EndOnActivate(false)
			.Plan(chargePlan)
			.Build();
		GameplayAbilityHandle chargeHandle = abilities.GiveAbility(hero, charge);
		GameplayAbilityActivationResult chargeResult = abilities.TryActivate(chargeHandle, enemy);
		Assert(chargeResult.Succeeded, "The charged ability activates.");
		Assert(abilities.Cues.ActiveCueCount == 1, "Charge cue is active while the ability waits.");
		events.Publish(new GameplayEvent(releaseEvent, hero, enemy));
		Assert(abilities.Cues.ActiveCueCount == 0, "Ending the activation releases its charge cue.");

		GameplayTag stun = GameplayTagRegistry.Shared.Register("Demo.Status.Stunned");
		GameplayAbilityPlan interruptedPlan = GameplayAbilityPlan.Create()
			.AddCue(chargeCue)
			.Wait(10f)
			.Build();
		GameplayAbilityDefinition interruptedCharge = GameplayAbilityDefinition.Create("Demo.InterruptedCharge")
			.EndOnActivate(false)
			.CancelOnTags(stun)
			.Plan(interruptedPlan)
			.Build();
		GameplayAbilityHandle interruptedHandle = abilities.GiveAbility(hero, interruptedCharge);
		Assert(abilities.TryActivate(interruptedHandle, enemy).Succeeded, "A second charged ability activates.");
		Assert(abilities.Cues.ActiveCueCount == 1, "The second charge cue is active before stun.");
		Assert(tags.AddTag(hero, stun), "Stun tag is added and cancels the charge.");
		Assert(abilities.Cues.ActiveCueCount == 0, "Stun cleanup removes the charge cue.");
		tags.RemoveTag(hero, stun);

		GameplayCueHandle destructionCue = cues.Add(collapseCue, target: enemy);
		Assert(world.DestroyEntity(enemy), "Destroying the target is accepted.");
		Assert(!cues.TryGet(destructionCue, out _), "Destroying a cue owner retires its persistent cue.");

		abilities.Dispose();
		events.Dispose();

		Span<GameplayCueEvent> frame = stackalloc GameplayCueEvent[32];
		int count = buffer.Drain(frame);
		for (int index = 0; index < count; index++)
		{
			presentation.Consume(frame[index]);
		}

		Assert(presentation.ActivePersistent == 0, "Presentation mock has no leaked persistent cue instances.");
		Assert(presentation.Executed >= 2, "Ignition and charged release execute one-shot cues.");
		Assert(presentation.Added == presentation.Removed, "Every added presentation cue has one terminal removal.");
		Console.WriteLine($"Gameplay cues: {count} buffered transitions; {presentation.Executed} executes; {presentation.ActivePersistent} persistent instances remain.");
	}

	private static void Assert(bool condition, string message)
	{
		if (!condition)
		{
			throw new InvalidOperationException(message);
		}
	}

	private sealed class MockPresentation
	{
		public int Executed { get; private set; }
		public int Added { get; private set; }
		public int Removed { get; private set; }
		public int ActivePersistent => Added - Removed;

		public void Consume(GameplayCueEvent cue)
		{
			switch (cue.Kind)
			{
				case GameplayCueKind.Execute:
					Executed++;
					break;
				case GameplayCueKind.Added:
					Added++;
					break;
				case GameplayCueKind.Removed:
					Removed++;
					break;
			}
		}
	}
}
