using BenchmarkDotNet.Attributes;
using Volt.Ecs;
using Volt.Gameplay.Events;
using Volt.Gameplay.Tags;

namespace Volt.Benchmarks;

/// <summary>
/// Measures gameplay event dispatch with no listeners, one listener,
/// many relevant listeners, many irrelevant listeners, and exact
/// versus hierarchical matching.
/// </summary>
/// <remarks>
/// Runs in process for the same reason as the other gameplay
/// benchmarks: duplicate benchmark project names in untracked
/// experiment workspaces make the out-of-process toolchain ambiguous.
/// </remarks>
[MemoryDiagnoser]
[InProcess]
public class GameplayEventBenchmarks
{
	private static readonly Action<GameplayEvent> Handler = OnEvent;
	private static int s_sink;

	private World _world = null!;
	private GameplayEventSystem _empty = null!;
	private GameplayEventSystem _single = null!;
	private GameplayEventSystem _relevant = null!;
	private GameplayEventSystem _irrelevant = null!;
	private GameplayEventSystem _hierarchical = null!;
	private GameplayEventSystem _registration = null!;
	private GameplayEvent _matchingEvent;
	private GameplayTag _matchingTag;
	private GameplayTag _otherTag;
	private GameplayTag _ancestorTag;

	[Params(1_000, 10_000)]
	public int ListenerCount { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		_world = new World();

		_empty = new GameplayEventSystem(_world);
		_single = new GameplayEventSystem(_world);
		_relevant = new GameplayEventSystem(_world);
		_irrelevant = new GameplayEventSystem(_world);
		_hierarchical = new GameplayEventSystem(_world);
		_registration = new GameplayEventSystem(_world);

		_matchingTag = GameplayTagRegistry.Shared.Register(
			"VoltBenchmarks.Events.Damage.Received");

		_otherTag = GameplayTagRegistry.Shared.Register(
			"VoltBenchmarks.Events.Unrelated");

		_ancestorTag = GameplayTagRegistry.Shared.Register(
			"VoltBenchmarks.Events.Damage");

		_matchingEvent = new GameplayEvent(_matchingTag);

		_single.Subscribe(_matchingTag, Handler);
		_hierarchical.Subscribe(
			_ancestorTag,
			Handler,
			GameplayEventMatch.Hierarchical);

		for (int index = 0; index < ListenerCount; index++)
		{
			_relevant.Subscribe(_matchingTag, Handler);
			_irrelevant.Subscribe(_otherTag, Handler);
		}
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_empty.Dispose();
		_single.Dispose();
		_relevant.Dispose();
		_irrelevant.Dispose();
		_hierarchical.Dispose();
		_registration.Dispose();
		_world.Dispose();
	}

	[Benchmark]
	public int Publish_NoListeners()
	{
		return _empty.Publish(_matchingEvent);
	}

	[Benchmark]
	public int Publish_OneListener()
	{
		return _single.Publish(_matchingEvent);
	}

	[Benchmark]
	public int Publish_ManyRelevantListeners()
	{
		return _relevant.Publish(_matchingEvent);
	}

	[Benchmark]
	public int Publish_ManyIrrelevantListeners()
	{
		return _irrelevant.Publish(_matchingEvent);
	}

	[Benchmark]
	public int Publish_HierarchicalMatch()
	{
		return _hierarchical.Publish(_matchingEvent);
	}

	[Benchmark]
	public bool SubscribeAndUnsubscribe()
	{
		GameplayEventSubscription subscription =
			_registration.Subscribe(_matchingTag, Handler);

		return _registration.Unsubscribe(subscription);
	}

	private static void OnEvent(GameplayEvent gameplayEvent)
	{
		s_sink += (int)gameplayEvent.Tag.Id;
	}
}
