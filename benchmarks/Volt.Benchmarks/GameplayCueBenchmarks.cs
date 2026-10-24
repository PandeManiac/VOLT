using BenchmarkDotNet.Attributes;
using Volt.Ecs;
using Volt.Gameplay.Cues;
using Volt.Gameplay.Tags;

namespace Volt.Benchmarks;

/// <summary>Measures gameplay cue dispatch, persistent lifecycles, and buffered handoff.</summary>
[MemoryDiagnoser]
[InProcess]
public class GameplayCueBenchmarks
{
	private static readonly Action<GameplayCueEvent> Handler = OnCue;
	private static int s_sink;

	private World _world = null!;
	private GameplayCueSystem _empty = null!;
	private GameplayCueSystem _single = null!;
	private GameplayCueSystem _many = null!;
	private GameplayCueSystem _registration = null!;
	private GameplayCueBuffer _buffer = null!;
	private GameplayTag _tag;
	private GameplayCueEvent _event;

	[Params(1, 1000)]
	public int ListenerCount { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		_world = new World();
		_empty = new GameplayCueSystem(_world);
		_single = new GameplayCueSystem(_world);
		_many = new GameplayCueSystem(_world);
		_registration = new GameplayCueSystem(_world);
		_buffer = new GameplayCueBuffer(1024, 1024);
		_tag = GameplayTagRegistry.Shared.Register("VoltBenchmarks.Cues.Impact");
		_event = new GameplayCueEvent(GameplayCueKind.Execute, _tag, default, default, default, default, 1f, 0, default, default);
		_single.Subscribe(_tag, Handler);
		for (int index = 0; index < ListenerCount; index++)
		{
			_many.Subscribe(_tag, Handler);
		}
		for (int index = 0; index < 1024; index++)
		{
			_buffer.Enqueue(_event);
		}
		_buffer.Clear();
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_empty.Dispose();
		_single.Dispose();
		_many.Dispose();
		_registration.Dispose();
		_buffer.Dispose();
		_world.Dispose();
	}

	[Benchmark]
	public int Execute_NoListeners() => _empty.Execute(_tag);

	[Benchmark]
	public int Execute_OneListener() => _single.Execute(_tag);

	[Benchmark]
	public int Execute_ManyListeners() => _many.Execute(_tag);

	[Benchmark]
	public bool AddAndRemovePersistent()
	{
		GameplayCueHandle cue = _empty.Add(_tag);
		return _empty.Remove(cue);
	}

	[Benchmark]
	public int BufferedEnqueueAndDrain()
	{
		_buffer.Enqueue(_event);
		Span<GameplayCueEvent> destination = stackalloc GameplayCueEvent[1];
		return _buffer.Drain(destination);
	}

	[Benchmark]
	public bool SubscribeAndUnsubscribe()
	{
		GameplayCueSubscription subscription = _registration.Subscribe(_tag, Handler);
		return _registration.Unsubscribe(subscription);
	}

	private static void OnCue(GameplayCueEvent cue) => s_sink += (int)cue.Tag.Id;
}
