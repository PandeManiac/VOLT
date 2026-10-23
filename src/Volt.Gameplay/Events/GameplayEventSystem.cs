using Volt.Ecs;
using Volt.Gameplay.Tags;

namespace Volt.Gameplay.Events;

/// <summary>
/// Routes gameplay events to the subscriptions that match them.
/// </summary>
/// <remarks>
/// <para>
/// Subscriptions are indexed by tag. Dispatch walks the event tag and
/// its ancestors, so it touches only the buckets that can match and
/// never scans unrelated listeners. With no subscriptions the whole
/// publish is one counter check.
/// </para>
/// <para>
/// A subscription can be scoped to an entity. Destroying that entity
/// removes its subscriptions immediately, and a recycled entity index
/// cannot revalidate an old subscription because the scoped owner keeps
/// its generation.
/// </para>
/// <para>
/// Dispatch captures the matching subscriptions before invoking any
/// handler, exactly like the ability system's bulk operations capture
/// their grants. A handler may subscribe, unsubscribe, publish another
/// event, cancel an ability, or destroy an entity: the captured set is
/// revalidated per delivery, so a removed subscription does not run and
/// a new one does not receive the event that was already in flight.
/// Recursive publishing is bounded by <see cref="MaxDispatchDepth"/>.
/// </para>
/// <para>
/// Notifications are delivered after the subscription state is
/// committed. An exception from a handler propagates to the publisher;
/// subscriptions added or removed before the exception stay committed,
/// and the dispatch depth guard is released in <c>finally</c>.
/// </para>
/// <para>
/// The system is single-threaded and uses no locks, like the rest of
/// the gameplay runtime.
/// </para>
/// </remarks>
public sealed class GameplayEventSystem : IDisposable
{
	private const int SegmentShift = 10;
	private const int SegmentSize = 1 << SegmentShift;
	private const int SegmentMask = SegmentSize - 1;
	private const int InitialSubscriptionCapacity = 16;
	private const int InitialCaptureCapacity = 8;
	private const int MaxDispatchDepth = 16;

	private static int s_nextSystemId;

	private readonly World _world;
	private readonly uint _allocatorId;
	private readonly uint _systemId;

	private SubscriptionSlot[] _subscriptions =
		new SubscriptionSlot[InitialSubscriptionCapacity];
	private int _subscriptionSlotCount;
	private int _freeSubscriptionSlot = -1;
	private int _subscriptionCount;

	private int[] _buckets = [];
	private EntitySubscriptions[]?[] _owners = [];

	private readonly List<CaptureBuffer> _captureBuffers = [];
	private int _dispatchDepth;

	private bool _disposed;

	/// <summary>
	/// Creates an event system over a world. Subscriptions scoped to an
	/// entity are released when that entity is destroyed.
	/// </summary>
	/// <param name="world">The world whose entities can scope a subscription.</param>
	/// <exception cref="ArgumentNullException">
	/// The world is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// Dispose the system before disposing the world.
	/// </remarks>
	public GameplayEventSystem(World world)
	{
		ArgumentNullException.ThrowIfNull(world);

		_world = world;
		_allocatorId = world.AllocatorId;
		_systemId = AllocateSystemId();

		_world.EntityDestroyed += HandleEntityDestroyed;
	}

	/// <summary>
	/// Gets the number of live subscriptions.
	/// </summary>
	public int SubscriptionCount => _subscriptionCount;

	/// <summary>
	/// Subscribes a delegate to an event tag.
	/// </summary>
	/// <param name="tag">
	/// The event tag to listen for, or <see cref="GameplayTag.None"/> to
	/// receive every event.
	/// </param>
	/// <param name="handler">The handler invoked for a matching event.</param>
	/// <param name="match">
	/// Whether the tag matches exactly or hierarchically. Exact is the
	/// default, mirroring Unreal's generic event callback map.
	/// </param>
	/// <param name="owner">
	/// The entity that scopes the subscription, or
	/// <c>default(Entity)</c> for an unscoped subscription that lives
	/// until it is removed explicitly.
	/// </param>
	/// <param name="sourceFilter">
	/// An optional source entity; when set, events from another source
	/// are ignored before the handler is reached.
	/// </param>
	/// <param name="targetFilter">
	/// An optional target entity; when set, events to another target are
	/// ignored before the handler is reached.
	/// </param>
	/// <returns>The subscription handle.</returns>
	/// <exception cref="ArgumentNullException">
	/// The handler is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// The scoped owner is not alive in this system's world.
	/// </exception>
	/// <remarks>
	/// A wildcard subscription is delivered after every tagged
	/// subscription for the event. Within a bucket, the most recent
	/// subscription is delivered first.
	/// </remarks>
	public GameplayEventSubscription Subscribe(
		GameplayTag tag,
		Action<GameplayEvent> handler,
		GameplayEventMatch match = GameplayEventMatch.Exact,
		Entity owner = default,
		Entity sourceFilter = default,
		Entity targetFilter = default)
	{
		ArgumentNullException.ThrowIfNull(handler);

		return CreateSubscription(
			tag,
			match,
			owner,
			sourceFilter,
			targetFilter,
			handler,
			null,
			0);
	}

	/// <summary>
	/// Removes one subscription.
	/// </summary>
	/// <param name="subscription">The subscription handle.</param>
	/// <returns>
	/// <see langword="true"/> when this call removed the subscription;
	/// <see langword="false"/> when it had already been removed or
	/// belongs to another system.
	/// </returns>
	public bool Unsubscribe(GameplayEventSubscription subscription)
	{
		ThrowIfDisposed();

		if (!TryResolve(subscription, out int slot))
		{
			return false;
		}

		Unlink(slot);
		ReleaseSlot(slot);
		_subscriptionCount--;

		return true;
	}

	/// <summary>
	/// Removes every subscription scoped to an entity.
	/// </summary>
	/// <param name="owner">The scoping entity.</param>
	/// <returns>The number of subscriptions this call removed.</returns>
	/// <remarks>
	/// The call walks only the owner's list, not the whole subscription
	/// table. Destroying an entity performs the same release silently.
	/// </remarks>
	public int UnsubscribeAll(Entity owner)
	{
		ThrowIfDisposed();

		if (!TryGetOwnerSlot(owner, out int segmentIndex, out int offset))
		{
			return 0;
		}

		ref EntitySubscriptions state = ref _owners[segmentIndex]![offset];

		return ReleaseOwnerSubscriptions(ref state);
	}

	/// <summary>
	/// Publishes an event immediately to the subscriptions that exist
	/// when the call starts.
	/// </summary>
	/// <param name="gameplayEvent">The event.</param>
	/// <returns>The number of subscriptions actually invoked.</returns>
	/// <exception cref="ArgumentException">
	/// The event does not carry a registered tag.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Event delivery is nested more than
	/// <see cref="MaxDispatchDepth"/> levels deep, which means a handler
	/// publishes without a terminating condition.
	/// </exception>
	/// <remarks>
	/// <para>
	/// The event is delivered synchronously, in deterministic order:
	/// the exact tag bucket first, then each ancestor from nearest to
	/// farthest, then wildcard subscriptions. Each bucket is newest
	/// subscription first.
	/// </para>
	/// <para>
	/// A handler may publish another event. The nested dispatch uses a
	/// separate reusable capture buffer; the outer dispatch continues
	/// with its own captured set afterwards.
	/// </para>
	/// </remarks>
	public int Publish(in GameplayEvent gameplayEvent)
	{
		ThrowIfDisposed();

		if (!gameplayEvent.IsValid)
		{
			throw new ArgumentException(
				"A gameplay event must carry a registered tag.",
				nameof(gameplayEvent));
		}

		if (_subscriptionCount == 0)
		{
			return 0;
		}

		if (_dispatchDepth >= MaxDispatchDepth)
		{
			throw new InvalidOperationException(
				$"Gameplay event delivery is nested more than " +
				$"{MaxDispatchDepth} levels deep. A handler is publishing " +
				$"events recursively without a terminating condition.");
		}

		CaptureBuffer buffer = _captureBuffers.Count == _dispatchDepth
			? AddCaptureBuffer()
			: _captureBuffers[_dispatchDepth];

		_dispatchDepth++;

		try
		{
			int captured = Capture(in gameplayEvent, buffer);
			int delivered = 0;

			for (int index = 0; index < captured; index++)
			{
				CapturedSubscription candidate = buffer.Items[index];

				if (!TryResolve(candidate, out int slot))
				{
					continue;
				}

				ref SubscriptionSlot subscription = ref _subscriptions[slot];

				if (!OwnerIsAlive(ref subscription))
				{
					continue;
				}

				Action<GameplayEvent>? handler = subscription.Handler;

				if (handler is not null)
				{
					handler(gameplayEvent);
				}
				else
				{
					subscription.Sink!.OnGameplayEvent(
						in gameplayEvent,
						CreateHandle(slot),
						subscription.Token);
				}

				delivered++;
			}

			return delivered;
		}
		finally
		{
			_dispatchDepth--;
		}
	}

	/// <summary>
	/// Subscribes an internal sink without allocating a delegate.
	/// </summary>
	internal GameplayEventSubscription SubscribeSink(
		GameplayTag tag,
		GameplayEventMatch match,
		Entity owner,
		Entity sourceFilter,
		Entity targetFilter,
		IGameplayEventSink sink,
		int token)
	{
		ArgumentNullException.ThrowIfNull(sink);

		return CreateSubscription(
			tag,
			match,
			owner,
			sourceFilter,
			targetFilter,
			null,
			sink,
			token);
	}

	/// <inheritdoc />
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_world.EntityDestroyed -= HandleEntityDestroyed;

		for (int index = 0; index < _subscriptionSlotCount; index++)
		{
			_subscriptions[index].Handler = null;
			_subscriptions[index].Sink = null;
		}

		_subscriptions = [];
		_buckets = [];
		_owners = [];
		_captureBuffers.Clear();
		_subscriptionSlotCount = 0;
		_freeSubscriptionSlot = -1;
		_subscriptionCount = 0;
		_dispatchDepth = 0;
		_disposed = true;

		GC.SuppressFinalize(this);
	}

	private GameplayEventSubscription CreateSubscription(
		GameplayTag tag,
		GameplayEventMatch match,
		Entity owner,
		Entity sourceFilter,
		Entity targetFilter,
		Action<GameplayEvent>? handler,
		IGameplayEventSink? sink,
		int token)
	{
		ThrowIfDisposed();

		if (match is not (
			GameplayEventMatch.Exact or GameplayEventMatch.Hierarchical))
		{
			throw new ArgumentOutOfRangeException(
				nameof(match),
				match,
				"Unknown gameplay event match mode.");
		}

		if (!owner.Equals(default) && !_world.IsAlive(owner))
		{
			throw new ArgumentException(
				"The entity is not alive in this system's world. A " +
				"subscription cannot be scoped to a destroyed or recycled " +
				"handle.",
				nameof(owner));
		}

		int slot = AcquireSubscriptionSlot();
		int bucket = GetBucketIndex(tag);

		EnsureBucketCapacity(bucket);

		ref SubscriptionSlot subscription = ref _subscriptions[slot];

		subscription.Tag = tag;
		subscription.Match = match;
		subscription.Owner = owner;
		subscription.SourceFilter = sourceFilter;
		subscription.TargetFilter = targetFilter;
		subscription.Handler = handler;
		subscription.Sink = sink;
		subscription.Token = token;

		LinkBucket(slot, bucket);

		if (!owner.Equals(default))
		{
			LinkOwner(owner, slot);
		}

		_subscriptionCount++;

		return CreateHandle(slot);
	}

	private int Capture(in GameplayEvent gameplayEvent, CaptureBuffer buffer)
	{
		GameplayTag tag = gameplayEvent.Tag;
		int count = CaptureBucket(
			GetBucketIndex(tag),
			true,
			false,
			in gameplayEvent,
			buffer,
			0);

		ReadOnlySpan<GameplayTag> ancestors =
			GameplayTagRegistry.Shared.GetAncestors(tag);

		foreach (GameplayTag ancestor in ancestors)
		{
			count = CaptureBucket(
				GetBucketIndex(ancestor),
				false,
				false,
				in gameplayEvent,
				buffer,
				count);
		}

		// Wildcard subscriptions live in bucket zero and match every
		// event. They run after the tagged subscriptions.
		return CaptureBucket(
			0,
			false,
			true,
			in gameplayEvent,
			buffer,
			count);
	}

	private int CaptureBucket(
		int bucket,
		bool allowExact,
		bool wildcard,
		in GameplayEvent gameplayEvent,
		CaptureBuffer buffer,
		int count)
	{
		if ((uint)bucket >= (uint)_buckets.Length)
		{
			return count;
		}

		int slot = _buckets[bucket];

		while (slot >= 0)
		{
			ref SubscriptionSlot subscription = ref _subscriptions[slot];
			int next = subscription.NextInBucket;

			// A subscription in an ancestor bucket with exact matching
			// is waiting for that ancestor, not for this descendant.
			if ((wildcard
					|| allowExact
					|| subscription.Match == GameplayEventMatch.Hierarchical)
				&& OwnerIsAlive(ref subscription)
				&& SourceAndTargetMatch(ref subscription, in gameplayEvent))
			{
				buffer.EnsureCapacity(count + 1);

				buffer.Items[count++] = new CapturedSubscription(
					slot,
					subscription.Generation);
			}

			slot = next;
		}

		return count;
	}

	private bool SourceAndTargetMatch(
		ref SubscriptionSlot subscription,
		in GameplayEvent gameplayEvent)
	{
		if (!subscription.SourceFilter.Equals(default)
			&& gameplayEvent.Source != subscription.SourceFilter)
		{
			return false;
		}

		return subscription.TargetFilter.Equals(default)
			|| gameplayEvent.Target == subscription.TargetFilter;
	}

	private bool OwnerIsAlive(ref SubscriptionSlot subscription)
	{
		return subscription.Owner.Equals(default)
			|| _world.IsAlive(subscription.Owner);
	}

	private void LinkBucket(int slot, int bucket)
	{
		ref SubscriptionSlot subscription = ref _subscriptions[slot];

		subscription.NextInBucket = _buckets[bucket];
		subscription.PreviousInBucket = -1;

		if (_buckets[bucket] >= 0)
		{
			_subscriptions[_buckets[bucket]].PreviousInBucket = slot;
		}

		_buckets[bucket] = slot;
	}

	private void UnlinkBucket(int slot)
	{
		ref SubscriptionSlot subscription = ref _subscriptions[slot];
		int bucket = GetBucketIndex(subscription.Tag);

		if (subscription.PreviousInBucket >= 0)
		{
			_subscriptions[subscription.PreviousInBucket].NextInBucket =
				subscription.NextInBucket;
		}
		else if ((uint)bucket < (uint)_buckets.Length)
		{
			_buckets[bucket] = subscription.NextInBucket;
		}

		if (subscription.NextInBucket >= 0)
		{
			_subscriptions[subscription.NextInBucket].PreviousInBucket =
				subscription.PreviousInBucket;
		}

		subscription.NextInBucket = -1;
		subscription.PreviousInBucket = -1;
	}

	private void LinkOwner(Entity owner, int slot)
	{
		ref EntitySubscriptions state = ref GetOrCreateOwnerSlot(owner);
		int head = state.Head;

		ref SubscriptionSlot subscription = ref _subscriptions[slot];

		subscription.NextOnOwner = head;
		subscription.PreviousOnOwner = -1;

		if (head >= 0)
		{
			_subscriptions[head].PreviousOnOwner = slot;
		}

		state.Head = slot;
		state.Count++;
	}

	private void UnlinkOwner(int slot)
	{
		ref SubscriptionSlot subscription = ref _subscriptions[slot];

		if (!TryGetOwnerSlot(
			subscription.Owner,
			out int segmentIndex,
			out int offset))
		{
			return;
		}

		ref EntitySubscriptions state = ref _owners[segmentIndex]![offset];
		int previous = subscription.PreviousOnOwner;
		int next = subscription.NextOnOwner;

		if (previous >= 0)
		{
			_subscriptions[previous].NextOnOwner = next;
		}
		else
		{
			state.Head = next;
		}

		if (next >= 0)
		{
			_subscriptions[next].PreviousOnOwner = previous;
		}

		if (state.Count > 0)
		{
			state.Count--;
		}

		subscription.NextOnOwner = -1;
		subscription.PreviousOnOwner = -1;
	}

	private int ReleaseOwnerSubscriptions(ref EntitySubscriptions state)
	{
		int count = 0;
		int slot = state.Head;

		while (slot >= 0)
		{
			int next = _subscriptions[slot].NextOnOwner;

			UnlinkBucket(slot);
			ReleaseSlot(slot);
			_subscriptionCount--;
			count++;

			slot = next;
		}

		state.Head = -1;
		state.Count = 0;

		return count;
	}

	private void HandleEntityDestroyed(Entity entity)
	{
		if (entity.AllocatorId != _allocatorId)
		{
			return;
		}

		if (!TryGetOwnerSlot(entity, out int segmentIndex, out int offset))
		{
			return;
		}

		ReleaseOwnerSubscriptions(ref _owners[segmentIndex]![offset]);
	}

	private EntitySubscriptions[] GetOwnerSegment(int segmentIndex)
	{
		if (segmentIndex >= _owners.Length)
		{
			Array.Resize(
				ref _owners,
				Math.Max(segmentIndex + 1, _owners.Length * 2));
		}

		EntitySubscriptions[]? segment = _owners[segmentIndex];

		if (segment is null)
		{
			segment = new EntitySubscriptions[SegmentSize];
			_owners[segmentIndex] = segment;
		}

		return segment;
	}

	private ref EntitySubscriptions GetOrCreateOwnerSlot(Entity owner)
	{
		int entityIndex = (int)owner.Index;
		int segmentIndex = entityIndex >> SegmentShift;
		EntitySubscriptions[] segment = GetOwnerSegment(segmentIndex);

		ref EntitySubscriptions state = ref segment[entityIndex & SegmentMask];

		if (state.Generation != owner.Generation)
		{
			// An earlier destruction handler may have recycled the index
			// and scoped a subscription to the replacement before this
			// system observed the old destruction. Retire the old
			// subscriptions before adopting the new generation.
			if (state.Count > 0)
			{
				ReleaseOwnerSubscriptions(ref state);
			}

			state.Generation = owner.Generation;
			state.Head = -1;
			state.Count = 0;
		}

		return ref state;
	}

	private bool TryGetOwnerSlot(
		Entity owner,
		out int segmentIndex,
		out int offset)
	{
		segmentIndex = 0;
		offset = 0;

		if (owner.AllocatorId != _allocatorId)
		{
			return false;
		}

		int entityIndex = (int)owner.Index;
		segmentIndex = entityIndex >> SegmentShift;

		if (segmentIndex >= _owners.Length)
		{
			return false;
		}

		EntitySubscriptions[]? segment = _owners[segmentIndex];

		if (segment is null)
		{
			return false;
		}

		offset = entityIndex & SegmentMask;

		return segment[offset].Generation == owner.Generation;
	}

	private int GetBucketIndex(GameplayTag tag)
	{
		return tag.IsValid ? checked((int)tag.Id) : 0;
	}

	private void EnsureBucketCapacity(int bucket)
	{
		if (bucket < _buckets.Length)
		{
			return;
		}

		int previous = _buckets.Length;
		int capacity = Math.Max(bucket + 1, Math.Max(4, _buckets.Length * 2));

		Array.Resize(ref _buckets, capacity);
		_buckets.AsSpan(previous).Fill(-1);
	}

	private GameplayEventSubscription CreateHandle(int slot)
	{
		ref SubscriptionSlot subscription = ref _subscriptions[slot];

		return new GameplayEventSubscription(
			_systemId,
			slot,
			subscription.Generation);
	}

	private bool TryResolve(
		GameplayEventSubscription subscription,
		out int slot)
	{
		slot = subscription.Slot;

		if (subscription.IsNone
			|| (uint)subscription.Slot >= (uint)_subscriptionSlotCount)
		{
			return false;
		}

		ref SubscriptionSlot candidate = ref _subscriptions[subscription.Slot];

		return subscription.SystemId == _systemId
			&& candidate.Generation == subscription.Generation;
	}

	private bool TryResolve(CapturedSubscription captured, out int slot)
	{
		slot = captured.Slot;

		if ((uint)slot >= (uint)_subscriptionSlotCount)
		{
			return false;
		}

		return _subscriptions[slot].Generation == captured.Generation;
	}

	private int AcquireSubscriptionSlot()
	{
		int slot;

		if (_freeSubscriptionSlot >= 0)
		{
			slot = _freeSubscriptionSlot;
			_freeSubscriptionSlot = _subscriptions[slot].NextInBucket;
		}
		else
		{
			if (_subscriptionSlotCount == _subscriptions.Length)
			{
				Array.Resize(
					ref _subscriptions,
					Math.Max(
						InitialSubscriptionCapacity,
						_subscriptions.Length * 2));
			}

			slot = _subscriptionSlotCount++;
		}

		ref SubscriptionSlot subscription = ref _subscriptions[slot];

		subscription.Generation = subscription.Generation == 0
			? 1
			: subscription.Generation;

		return slot;
	}

	private void ReleaseSlot(int slot)
	{
		ref SubscriptionSlot subscription = ref _subscriptions[slot];
		uint generation = NextGeneration(subscription.Generation);

		_subscriptions[slot] = new SubscriptionSlot
		{
			Generation = generation,
			NextInBucket = _freeSubscriptionSlot,
			PreviousInBucket = -1,
			NextOnOwner = -1,
			PreviousOnOwner = -1
		};

		_freeSubscriptionSlot = slot;
	}

	private void Unlink(int slot)
	{
		UnlinkBucket(slot);
		UnlinkOwner(slot);
	}

	private CaptureBuffer AddCaptureBuffer()
	{
		CaptureBuffer buffer = new();

		_captureBuffers.Add(buffer);

		return buffer;
	}

	private static uint NextGeneration(uint generation)
	{
		generation++;

		return generation == 0 ? 1 : generation;
	}

	private static uint AllocateSystemId()
	{
		// System identity is process-global and never reused, so a
		// subscription handle cannot be mistaken for one from a later
		// system. Zero is the default handle's id and is skipped.
		uint id = unchecked((uint)Interlocked.Increment(ref s_nextSystemId));

		return id == 0
			? unchecked((uint)Interlocked.Increment(ref s_nextSystemId))
			: id;
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}

	private readonly struct CapturedSubscription
	{
		public CapturedSubscription(int slot, uint generation)
		{
			Slot = slot;
			Generation = generation;
		}

		public int Slot { get; }
		public uint Generation { get; }
	}

	private sealed class CaptureBuffer
	{
		public CapturedSubscription[] Items =
			new CapturedSubscription[InitialCaptureCapacity];

		public void EnsureCapacity(int required)
		{
			if (required <= Items.Length)
			{
				return;
			}

			Array.Resize(
				ref Items,
				Math.Max(required, Items.Length * 2));
		}
	}

	private struct SubscriptionSlot
	{
		public uint Generation;
		public GameplayTag Tag;
		public GameplayEventMatch Match;
		public Entity Owner;
		public Entity SourceFilter;
		public Entity TargetFilter;
		public Action<GameplayEvent>? Handler;
		public IGameplayEventSink? Sink;
		public int Token;
		public int NextInBucket;
		public int PreviousInBucket;
		public int NextOnOwner;
		public int PreviousOnOwner;
	}

	private struct EntitySubscriptions
	{
		public uint Generation;
		public int Head;
		public int Count;
	}
}
