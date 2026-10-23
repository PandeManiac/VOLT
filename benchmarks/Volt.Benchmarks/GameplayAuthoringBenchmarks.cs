using BenchmarkDotNet.Attributes;
using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Events;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tasks;

namespace Volt.Benchmarks;

/// <summary>
/// Measures authored ability paths: linear and conditional plans,
/// custom task start and completion, cancellation, and composite waits.
/// </summary>
/// <remarks>
/// Every operation starts and ends an activation, so the numbers
/// include retrieval of the activation token, plan execution, task
/// bookkeeping, and the terminal notification. The harness allocates
/// nothing per operation after its one-time setup.
/// </remarks>
[MemoryDiagnoser]
[InProcess]
public class GameplayAuthoringBenchmarks
{
	private static readonly GameplayTag ConditionalTag =
		GameplayTagRegistry.Shared.Register(
			"VoltBenchmarks.Authoring.ConditionalTag");

	private static readonly GameplayTag BenchEvent =
		GameplayTagRegistry.Shared.Register(
			"VoltBenchmarks.Authoring.Event");

	private static readonly GameplayTag BenchEventOther =
		GameplayTagRegistry.Shared.Register(
			"VoltBenchmarks.Authoring.Event.Other");

	private static readonly GameplayAttribute BenchAttribute =
		GameplayAttributeRegistry.Shared.Register(
			"VoltBenchmarks.Authoring.Attribute",
			defaultBaseValue: 100f);

	private AuthoringRuntime _runtime = null!;
	private GameplayAbilityTaskDefinition _definition = null!;

	private GameplayAbilityHandle _linear;
	private GameplayAbilityHandle _conditional;
	private GameplayAbilityHandle _nested;
	private GameplayAbilityHandle _customWait;
	private GameplayAbilityHandle _anyEvent;
	private GameplayAbilityHandle _anyTimeout;
	private GameplayAbilityHandle _anyTag;

	private readonly GameplayAbilityTaskHandle[] _task = [default];

	[GlobalSetup]
	public void Setup()
	{
		_runtime = new AuthoringRuntime();

		_definition = _runtime.Abilities.RegisterTaskDefinition(
			"Bench.Authoring.CustomTask");

		GameplayEffectDefinition noop = GameplayEffectDefinition
			.Create("Bench.Authoring.Noop")
			.Instant()
			.Modifier(
				BenchAttribute,
				GameplayModifierOperation.AddBase,
				0f)
			.Build();

		_linear = _runtime.Abilities.GiveAbility(
			_runtime.Actor,
			GameplayAbilityDefinition
				.Create("Bench.Authoring.Linear")
				.Plan(
					GameplayAbilityPlan
						.Create()
						.ApplyEffect(noop)
						.PublishEvent(BenchEvent)
						.End()
						.Build())
				.Build());

		_conditional = _runtime.Abilities.GiveAbility(
			_runtime.Actor,
			GameplayAbilityDefinition
				.Create("Bench.Authoring.Conditional")
				.Plan(
					GameplayAbilityPlan
						.Create()
						.If(GameplayCondition.OwnerHasTag(ConditionalTag))
						.ApplyEffect(noop)
						.Else()
						.ApplyEffect(noop)
						.EndIf()
						.End()
						.Build())
				.Build());

		_nested = _runtime.Abilities.GiveAbility(
			_runtime.Actor,
			GameplayAbilityDefinition
				.Create("Bench.Authoring.Nested")
				.Plan(
					GameplayAbilityPlan
						.Create()
						.If(GameplayCondition.TargetAlive())
						.If(GameplayCondition.All(
							GameplayCondition.OwnerAttribute(
								BenchAttribute,
								GameplayAttributeComparison.GreaterThan,
								50f),
							GameplayCondition.Not(
								GameplayCondition.OwnerHasTag(
									ConditionalTag))))
						.ApplyEffect(noop)
						.Else()
						.ApplyEffect(noop)
						.EndIf()
						.Else()
						.ApplyEffect(noop)
						.EndIf()
						.End()
						.Build())
				.Build());

		_customWait = _runtime.Abilities.GiveAbility(
			_runtime.Actor,
			GameplayAbilityDefinition
				.Create("Bench.Authoring.CustomWait")
				.Plan(
					GameplayAbilityPlan
						.Create()
						.WaitCustom(_definition)
						.End()
						.Build())
				.Build());

		_anyEvent = _runtime.Abilities.GiveAbility(
			_runtime.Actor,
			GameplayAbilityDefinition
				.Create("Bench.Authoring.AnyEvent")
				.Plan(
					GameplayAbilityPlan
						.Create()
						.WaitAny(
							GameplayWaitAny
								.Create()
								.OnEvent(BenchEvent)
								.OnEvent(BenchEventOther))
						.End()
						.Build())
				.Build());

		_anyTimeout = _runtime.Abilities.GiveAbility(
			_runtime.Actor,
			GameplayAbilityDefinition
				.Create("Bench.Authoring.AnyTimeout")
				.Plan(
					GameplayAbilityPlan
						.Create()
						.WaitAny(
							GameplayWaitAny
								.Create()
								.WithTimeout(2f))
						.End()
						.Build())
				.Build());

		_anyTag = _runtime.Abilities.GiveAbility(
			_runtime.Actor,
			GameplayAbilityDefinition
				.Create("Bench.Authoring.AnyTag")
				.Plan(
					GameplayAbilityPlan
						.Create()
						.WaitAny(
							GameplayWaitAny
								.Create()
								.WithTimeout(5f)
								.OnTag(ConditionalTag))
						.End()
						.Build())
				.Build());
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_runtime.Dispose();
	}

	[Benchmark(Baseline = true)]
	public int Activate_LinearPlan()
	{
		return _runtime.Abilities.TryActivate(_linear).Succeeded ? 1 : 0;
	}

	[Benchmark]
	public int Activate_ConditionalPlan()
	{
		return _runtime.Abilities.TryActivate(_conditional).Succeeded
			? 1
			: 0;
	}

	[Benchmark]
	public int Activate_NestedConditionalPlan()
	{
		return _runtime.Abilities.TryActivate(_nested).Succeeded ? 1 : 0;
	}

	[Benchmark]
	public int CustomTask_StartAndComplete()
	{
		if (!_runtime.Abilities.TryActivate(_customWait).Succeeded)
		{
			return 0;
		}

		if (_runtime.Tasks.CopyTasks(_runtime.Actor, _task) != 1)
		{
			return 0;
		}

		return _runtime.Tasks.Complete(_task[0]) ? 1 : 0;
	}

	[Benchmark]
	public int CustomTask_StartAndCancel()
	{
		if (!_runtime.Abilities.TryActivate(_customWait).Succeeded)
		{
			return 0;
		}

		return _runtime.Abilities.Cancel(_customWait) ? 1 : 0;
	}

	[Benchmark]
	public int WaitAny_EventCompletes()
	{
		if (!_runtime.Abilities.TryActivate(_anyEvent).Succeeded)
		{
			return 0;
		}

		_runtime.Events.Publish(new GameplayEvent(
			BenchEventOther,
			_runtime.Actor,
			_runtime.Actor));

		return _runtime.Abilities.IsActive(_anyEvent) ? 0 : 1;
	}

	[Benchmark]
	public int WaitAny_TimeoutCompletes()
	{
		if (!_runtime.Abilities.TryActivate(_anyTimeout).Succeeded)
		{
			return 0;
		}

		_runtime.Tasks.Advance(2f);

		return _runtime.Abilities.IsActive(_anyTimeout) ? 0 : 1;
	}

	[Benchmark]
	public int WaitAny_TagConditionCompletes()
	{
		if (!_runtime.Abilities.TryActivate(_anyTag).Succeeded)
		{
			return 0;
		}

		_runtime.Tags.AddTag(_runtime.Actor, ConditionalTag);
		_runtime.Tasks.Advance(0.01f);
		_runtime.Tags.RemoveTag(_runtime.Actor, ConditionalTag);

		return _runtime.Abilities.IsActive(_anyTag) ? 0 : 1;
	}

	private sealed class AuthoringRuntime : IDisposable
	{
		public AuthoringRuntime()
		{
			World = new World();
			Attributes = new GameplayAttributeStore(World);
			Tags = new GameplayTagStore(World);
			Effects = new GameplayEffectSystem(World, Attributes, Tags);
			Events = new GameplayEventSystem(World);
			Abilities = new GameplayAbilitySystem(
				World,
				Tags,
				Effects,
				Attributes,
				Events);
			Actor = World.CreateEntity();
		}

		public World World { get; }

		public GameplayAttributeStore Attributes { get; }

		public GameplayTagStore Tags { get; }

		public GameplayEffectSystem Effects { get; }

		public GameplayEventSystem Events { get; }

		public GameplayAbilitySystem Abilities { get; }

		public GameplayAbilityTaskSystem Tasks => Abilities.Tasks;

		public Entity Actor { get; }

		public void Dispose()
		{
			Abilities.Dispose();
			Events.Dispose();
			Effects.Dispose();
			Tags.Dispose();
			Attributes.Dispose();
			World.Dispose();
		}
	}
}
