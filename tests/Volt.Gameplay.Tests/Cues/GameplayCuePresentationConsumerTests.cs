using Volt.Ecs;
using Volt.Gameplay.Cues;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tests.Abilities;
using Volt.Gameplay.Tests.Tags;

namespace Volt.Gameplay.Tests.Cues;

/// <summary>
/// Exercises the buffered cue handoff the way a presentation layer would
/// consume it, without a renderer or an audio engine.
/// </summary>
/// <remarks>
/// The consumer keeps only what an application needs: a set of live
/// persistent cues and a count of one-shots. It drains the bounded
/// buffer at frame boundaries and must reconstruct the same state
/// whether it sees one transition or the whole history. The tests pin
/// the ordering the buffer promises and prove that a cue which is added
/// and removed between frames still delivers both transitions.
/// </remarks>
public sealed class GameplayCuePresentationConsumerTests
{
	private static readonly GameplayEffectDefinition TimedCue =
		GameplayEffectDefinition
			.Create("Test.Consumer.Timed")
			.HasDuration(4f)
			.Stacking(GameplayEffectStackingType.AggregateByTarget, 5)
			.GrantTags(TagSchema.StatusStunned)
			.GameplayCue(TagSchema.CueBurning)
			.Build();

	private static readonly GameplayEffectDefinition GuardedCue =
		GameplayEffectDefinition
			.Create("Test.Consumer.Guarded")
			.Infinite()
			.GrantTags(TagSchema.StatusStunned)
			.GameplayCue(TagSchema.CueBurning)
			.RequireWhileActive(
				GameplayTagRequirements.Require(
					GameplayTagSet.Create(TagSchema.StatusBurning)))
			.Build();

	[Fact]
	public void Consumer_RebuildsLiveStateAcrossSeveralSimulationSteps()
	{
		using AbilityRuntime runtime = new();
		using GameplayCueSystem cues = new(runtime.World);
		using GameplayCueBuffer buffer = new(8, 64);
		using PresentationConsumer consumer = new(buffer);

		cues.BindEffects(runtime.Effects);
		cues.PresentationBuffer = buffer;

		Entity actor = runtime.CreateActor();

		// Two simulation steps before the frame boundary: the cue is
		// created in the first and stacked in the second.
		runtime.Effects.Apply(actor, TimedCue);
		runtime.Effects.Advance(1f);
		runtime.Effects.Apply(actor, TimedCue);
		runtime.Effects.Advance(1f);

		Assert.Equal(0, consumer.FrameCount);
		Assert.Equal(1, buffer.Count > 0 ? 1 : 0);

		consumer.Present();

		Assert.Equal(1, consumer.PersistentCueCount);
		Assert.Equal(2, consumer.StackCountOf(TagSchema.CueBurning));
		Assert.Equal(0, consumer.OneShotCount);

		// A third step expires nothing yet; the frame has no events.
		runtime.Effects.Advance(1f);
		consumer.Present();

		Assert.Equal(1, consumer.PersistentCueCount);

		// The last step expires the instance, which must remove the
		// presentation state rather than leaving it behind.
		runtime.Effects.Advance(2f);
		consumer.Present();

		Assert.Equal(0, consumer.PersistentCueCount);
		Assert.Equal(1, consumer.MaxObservedPersistentCueCount);
		Assert.True(consumer.RemovedCount >= 1);
	}

	[Fact]
	public void Consumer_ObservesBothTransitionsWhenACueLivesBetweenFrames()
	{
		using World world = new();
		using GameplayCueSystem cues = new(world);
		using GameplayCueBuffer buffer = new(4, 8);
		using PresentationConsumer consumer = new(buffer);

		cues.PresentationBuffer = buffer;

		Entity target = world.CreateEntity();
		GameplayCueHandle handle = cues.Add(TagSchema.CueBurning, target, target);

		Assert.True(cues.Remove(handle));

		consumer.Present();

		Assert.Equal(0, consumer.PersistentCueCount);
		Assert.Equal(
			[GameplayCueKind.Added, GameplayCueKind.Removed],
			consumer.Kinds);
	}

	[Fact]
	public void Consumer_SeesExecuteAddUpdateRemoveInOrder()
	{
		using World world = new();
		using GameplayCueSystem cues = new(world);
		using GameplayCueBuffer buffer = new(8, 16);
		using PresentationConsumer consumer = new(buffer);

		cues.PresentationBuffer = buffer;

		Entity target = world.CreateEntity();
		GameplayCueHandle handle = cues.Add(TagSchema.CueBurning, target, target);

		cues.Execute(TagSchema.CueBurning, target, target);
		Assert.True(cues.Update(handle, 3f, 2));
		Assert.True(cues.Remove(handle));

		consumer.Present();

		Assert.Equal(
			[
				GameplayCueKind.Added,
				GameplayCueKind.Execute,
				GameplayCueKind.Updated,
				GameplayCueKind.Removed
			],
			consumer.Kinds);
		Assert.Equal(1, consumer.OneShotCount);
		Assert.Equal(0, consumer.PersistentCueCount);
	}

	[Fact]
	public void Consumer_HandlesInhibitionAndResumptionAsRemoveAndAdd()
	{
		using AbilityRuntime runtime = new();
		using GameplayCueSystem cues = new(runtime.World);
		using GameplayCueBuffer buffer = new(8, 32);
		using PresentationConsumer consumer = new(buffer);

		cues.BindEffects(runtime.Effects);
		cues.PresentationBuffer = buffer;

		Entity actor = runtime.CreateActor();

		runtime.Effects.Apply(actor, GuardedCue);

		consumer.Present();
		Assert.Equal(0, consumer.PersistentCueCount);

		Assert.True(runtime.Tags.AddTag(actor, TagSchema.StatusBurning));
		consumer.Present();

		Assert.Equal(1, consumer.PersistentCueCount);

		Assert.True(runtime.Tags.RemoveTag(actor, TagSchema.StatusBurning));
		consumer.Present();

		Assert.Equal(0, consumer.PersistentCueCount);
		Assert.Equal(
			[GameplayCueKind.Added, GameplayCueKind.Removed],
			consumer.Kinds);
	}

	[Fact]
	public void Consumer_RemovesEveryCueWhenTheEntityDisappears()
	{
		using AbilityRuntime runtime = new();
		using GameplayCueSystem cues = new(runtime.World);
		using GameplayCueBuffer buffer = new(8, 32);
		using PresentationConsumer consumer = new(buffer);

		cues.BindEffects(runtime.Effects);
		cues.PresentationBuffer = buffer;

		Entity actor = runtime.CreateActor();

		runtime.Effects.Apply(actor, TimedCue);
		runtime.Effects.Apply(actor, TimedCue);

		consumer.Present();

		Assert.Equal(1, consumer.PersistentCueCount);

		Assert.True(runtime.World.DestroyEntity(actor));

		consumer.Present();

		Assert.Equal(0, consumer.PersistentCueCount);
		Assert.Equal(0, runtime.Effects.ActiveEffectCount);
	}

	[Fact]
	public void Buffer_OverflowThrowsWithoutLosingOrReorderingTransitions()
	{
		using World world = new();
		using GameplayCueSystem cues = new(world);
		using GameplayCueBuffer buffer = new(2, 4);
		using PresentationConsumer consumer = new(buffer);

		cues.PresentationBuffer = buffer;

		Entity target = world.CreateEntity();
		GameplayCueHandle handle = cues.Add(TagSchema.CueBurning, target, target);
		cues.Execute(TagSchema.CueBurning, target, target);
		cues.Execute(TagSchema.CueBurning, target, target);
		cues.Execute(TagSchema.CueBurning, target, target);

		Assert.Equal(4, buffer.Count);
		Assert.Throws<InvalidOperationException>(
			() => cues.Execute(TagSchema.CueBurning, target, target));
		Assert.Equal(4, buffer.Count);

		consumer.Present();

		Assert.Equal(
			[
				GameplayCueKind.Added,
				GameplayCueKind.Execute,
				GameplayCueKind.Execute,
				GameplayCueKind.Execute
			],
			consumer.Kinds);
		Assert.Equal(1, consumer.PersistentCueCount);
		Assert.True(cues.Remove(handle));
	}

	[Fact]
	public void DisposingTheBufferBeforeTheSystem_StopsTheHandoffWithoutThrowing()
	{
		using World world = new();
		using GameplayCueSystem cues = new(world);
		GameplayCueBuffer buffer = new(8, 16);
		List<GameplayCueKind> kinds = [];

		cues.PresentationBuffer = buffer;
		cues.Subscribe(TagSchema.CueBurning, change => kinds.Add(change.Kind));

		Entity target = world.CreateEntity();
		GameplayCueHandle handle = cues.Add(TagSchema.CueBurning, target, target);

		buffer.Dispose();

		Assert.True(buffer.IsDisposed);
		Assert.True(cues.Remove(handle));

		// The synchronous listener still sees the removal, and the
		// disposed handoff is skipped rather than throwing.
		Assert.Equal(
			[GameplayCueKind.Added, GameplayCueKind.Removed],
			kinds);
		Assert.Equal(0, cues.ActiveCueCount);
	}

	[Fact]
	public void DisposingTheSystemFlushesRemainingCuesToTheBuffer()
	{
		using World world = new();
		using GameplayCueBuffer buffer = new(8, 16);
		GameplayCueSystem cues = new(world);

		cues.PresentationBuffer = buffer;

		Entity target = world.CreateEntity();
		cues.Add(TagSchema.CueBurning, target, target);

		cues.Dispose();

		Span<GameplayCueEvent> events = stackalloc GameplayCueEvent[8];
		int drained = buffer.Drain(events);
		List<GameplayCueKind> kinds = [];

		for (int index = 0; index < drained; index++)
		{
			kinds.Add(events[index].Kind);
		}

		Assert.Equal(
			[GameplayCueKind.Added, GameplayCueKind.Removed],
			kinds);
	}

	/// <summary>
	/// The presentation-side replica of the cue stream.
	/// </summary>
	private sealed class PresentationConsumer : IDisposable
	{
		private readonly GameplayCueBuffer _buffer;
		private readonly Dictionary<GameplayCueHandle, GameplayCueEvent> _live = [];
		private readonly List<GameplayCueKind> _kinds = [];
		private GameplayCueEvent[] _events = new GameplayCueEvent[16];

		public PresentationConsumer(GameplayCueBuffer buffer)
		{
			_buffer = buffer;
		}

		public int PersistentCueCount => _live.Count;

		public int OneShotCount { get; private set; }

		public int RemovedCount { get; private set; }

		public int FrameCount { get; private set; }

		public int MaxObservedPersistentCueCount { get; private set; }

		public IReadOnlyList<GameplayCueKind> Kinds => _kinds;

		public int StackCountOf(GameplayTag tag)
		{
			int stacks = 0;

			foreach (GameplayCueEvent cueEvent in _live.Values)
			{
				if (cueEvent.Tag == tag)
				{
					stacks += cueEvent.StackCount;
				}
			}

			return stacks;
		}

		/// <summary>
		/// Drains one presentation frame. Every transition is handled in
		/// buffer order, exactly as the application's update loop would.
		/// </summary>
		public void Present()
		{
			FrameCount++;

			while (true)
			{
				int drained = _buffer.Drain(_events);

				if (drained == 0)
				{
					break;
				}

				for (int index = 0; index < drained; index++)
				{
					Handle(_events[index]);
					_events[index] = default;
				}

				if (drained < _events.Length)
				{
					break;
				}
			}

			MaxObservedPersistentCueCount = Math.Max(
				MaxObservedPersistentCueCount,
				_live.Count);
		}

		public void Dispose()
		{
			_live.Clear();
			_kinds.Clear();
			_events = [];
		}

		private void Handle(GameplayCueEvent cueEvent)
		{
			_kinds.Add(cueEvent.Kind);

			switch (cueEvent.Kind)
			{
				case GameplayCueKind.Execute:
					OneShotCount++;
					break;

				case GameplayCueKind.Added:
					_live[cueEvent.Handle] = cueEvent;
					break;

				case GameplayCueKind.Updated:
					Assert.True(_live.ContainsKey(cueEvent.Handle));
					_live[cueEvent.Handle] = cueEvent;
					break;

				case GameplayCueKind.Removed:
					Assert.True(_live.Remove(cueEvent.Handle));
					RemovedCount++;
					break;

				default:
					throw new ArgumentOutOfRangeException(
						nameof(cueEvent),
						cueEvent.Kind,
						"The consumer does not understand this cue kind.");
			}
		}
	}
}
