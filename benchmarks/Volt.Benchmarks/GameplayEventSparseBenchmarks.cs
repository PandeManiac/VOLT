using BenchmarkDotNet.Attributes;
using Volt.Ecs;
using Volt.Gameplay.Events;
using Volt.Gameplay.Tags;

namespace Volt.Benchmarks;

/// <summary>
/// Measures routing and registration at a high, sparsely used tag
/// identifier after registering many unrelated tags.
/// </summary>
/// <remarks>
/// The event router stores bucket heads in 1,024-entry segments
/// allocated on demand. A listener on tag id 100,000 therefore pays
/// for one 4 KiB segment and the segment pointer table, not a dense
/// <c>int[100,001]</c> array. Publishing still resolves the bucket with
/// one segment load and one index.
/// </remarks>
[MemoryDiagnoser]
[InProcess]
public class GameplayEventSparseBenchmarks
{
	private static readonly Action<GameplayEvent> Handler = OnEvent;
	private static int s_sink;

	private World _world = null!;
	private GameplayEventSystem _events = null!;
	private GameplayEvent _event;

	[Params(1_000, 100_000)]
	public int RegisteredTagCount { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		_world = new World();
		_events = new GameplayEventSystem(_world);

		GameplayTag eventTag = GameplayTag.None;

		for (int index = 0; index < RegisteredTagCount; index++)
		{
			eventTag = GameplayTagRegistry.Shared.Register(
				$"VoltBenchmarks.Sparse.Tag{index}");
		}

		_event = new GameplayEvent(eventTag);

		_events.Subscribe(eventTag, Handler);
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_events.Dispose();
		_world.Dispose();
	}

	[Benchmark]
	public int Publish_SingleListenerAtHighId()
	{
		return _events.Publish(_event);
	}

	[Benchmark]
	public int SubscribeAndUnsubscribeAtHighId()
	{
		GameplayEventSubscription subscription =
			_events.Subscribe(_event.Tag, Handler);

		return _events.Unsubscribe(subscription) ? 1 : 0;
	}

	[Benchmark]
	public int CreateRouterAndSubscribeAtHighId()
	{
		using GameplayEventSystem events = new(_world);

		GameplayEventSubscription subscription =
			events.Subscribe(_event.Tag, Handler);

		return events.Unsubscribe(subscription) ? 1 : 0;
	}

	private static void OnEvent(GameplayEvent gameplayEvent)
	{
		s_sink += (int)gameplayEvent.Tag.Id;
	}
}
