using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Events;
using Volt.Gameplay.Tags;

namespace Volt.Gameplay.Cues;

/// <summary>
/// Routes one-shot and persistent gameplay cues for one world.
/// </summary>
/// <remarks>
/// Cue tags are shared identities: definitions using the same tag are interpreted as the same presentation action, while each persistent instance has a distinct generational handle. Hierarchical listeners receive exact and descendant tags. The system is single-threaded; callbacks run synchronously after cue state commits. Listener exceptions propagate and do not roll state back.
/// </remarks>
public sealed class GameplayCueSystem : IDisposable
{
	private const int InitialCapacity = 16;
	private const int MaxDispatchDepth = 64;
	private const int MaxDestructionDepth = 64;
	private static int s_nextSystemId;
	private readonly uint _systemId;
	private readonly World _world;
	private readonly uint _allocatorId;
	private DestructionBuffer[] _destructionBuffers = [new(), new(), new(), new()];
	private int _destructionDepth;
	private CueSlot[] _cues = new CueSlot[InitialCapacity];
	private int _cueCount;
	private int _freeCue = -1;
	private int _activeCount;
	private SubscriptionSlot[] _subscriptions = new SubscriptionSlot[InitialCapacity];
	private int _subscriptionCount;
	private int _freeSubscription = -1;
	private int _liveSubscriptions;
	private Dictionary<GameplayTag, int> _exact = [];
	private Dictionary<GameplayTag, int> _hierarchical = [];
	private Dictionary<GameplayEffectHandle, GameplayCueHandle> _effectCues = [];
	private Dictionary<GameplayAbilityActivation, List<GameplayCueHandle>> _activationCues = [];
	private OwnerSlot[]?[] _owners = [];
	private CaptureBuffer[] _buffers = new CaptureBuffer[4];
	private int _dispatchDepth;
	private bool _disposed;
	private bool _disposing;
	private GameplayCueBuffer? _buffer;
	private GameplayEffectSystem? _effects;
	private bool _applyingEffectChange;
	private List<GameplayEffectChange>? _deferredEffectChanges;

	/// <summary>Creates a cue system for a world.</summary>
	public GameplayCueSystem(World world)
	{
		ArgumentNullException.ThrowIfNull(world);
		_world = world;
		_allocatorId = world.AllocatorId;
		_systemId = NextSystemId();
		for (int index = 0; index < _buffers.Length; index++)
		{
			_buffers[index] = new CaptureBuffer();
		}

		_world.EntityDestroyed += HandleEntityDestroyed;
	}

	/// <summary>Number of active persistent cue instances.</summary>
	public int ActiveCueCount => _activeCount;
	/// <summary>Number of listener subscriptions.</summary>
	public int SubscriptionCount => _liveSubscriptions;
	/// <summary>Connects effect lifecycle changes to effect-associated cue transitions.</summary>
	public void BindEffects(GameplayEffectSystem effects)
	{
		ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(effects);
		if (!ReferenceEquals(effects.World, _world))
		{
			throw new ArgumentException("Effects must belong to the same world.", nameof(effects));
		}

		if (_effects is not null)
		{
			throw new InvalidOperationException("An effect system is already bound.");
		}

		_effects = effects;
		_effects.CueChanged += HandleEffectChange;
	}
	/// <summary>Raised synchronously after cue state commits.</summary>
	public event Action<GameplayCueEvent>? Changed;

	/// <summary>Routes cue events to this optional presentation buffer as well as synchronous listeners.</summary>
	/// <remarks>Passing null disables the handoff. The buffer is not owned by the cue system.</remarks>
	public GameplayCueBuffer? PresentationBuffer
	{
		get => _buffer;
		set { ThrowIfDisposed(); _buffer = value; }
	}

	/// <summary>Emits a one-shot cue.</summary>
	public int Execute(GameplayTag tag, Entity source = default, Entity target = default, float magnitude = 1f, GameplayEventPayload parameters = default)
	{
		ValidateTag(tag);
		ValidateMagnitude(magnitude);
		return Dispatch(new GameplayCueEvent(GameplayCueKind.Execute, tag, default, source, target, parameters, magnitude, 0, default, default));
	}

	/// <summary>Creates an independently owned persistent cue.</summary>
	public GameplayCueHandle Add(GameplayTag tag, Entity source = default, Entity target = default, float magnitude = 1f, int stackCount = 1, GameplayEventPayload parameters = default, GameplayAbilityActivation activation = default, GameplayEffectHandle effect = default)
	{
		ValidateTag(tag);
		ValidateMagnitude(magnitude);
		if (stackCount <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(stackCount));
		}

		if (!target.Equals(default) && !_world.IsAlive(target))
		{
			throw new ArgumentException("The cue target must be alive in this world's allocator.", nameof(target));
		}

		if (!activation.IsNone && activation.Owner.AllocatorId != _allocatorId)
		{
			throw new ArgumentException("The activation must belong to this world's allocator.", nameof(activation));
		}

		if (!effect.IsNone)
		{
			if (effect.Target.AllocatorId != _allocatorId)
			{
				throw new ArgumentException("The effect must belong to this world's allocator.", nameof(effect));
			}

			if (_effects is null || !_effects.TryGetEffect(effect, out _))
			{
				throw new ArgumentException("An effect-owned cue must reference an active effect in the bound effect system.", nameof(effect));
			}
		}
		int slot = AcquireCue();
		ref CueSlot cue = ref _cues[slot];
		cue.Active = true; cue.Tag = tag; cue.Source = source; cue.Target = target;
		cue.Magnitude = magnitude; cue.StackCount = stackCount; cue.Parameters = parameters; cue.Activation = activation; cue.Effect = effect;
		cue.Owner = !target.Equals(default) ? target : _world.IsAlive(source) ? source : default;
		if (!cue.Owner.Equals(default))
		{
			LinkOwner(cue.Owner, slot);
		}

		_activeCount++;
		GameplayCueHandle handle = Handle(slot);
		if (!effect.IsNone)
		{
			_effectCues[effect] = handle;
		}

		if (!activation.IsNone)
		{
			if (!_activationCues.TryGetValue(activation, out List<GameplayCueHandle>? owned))
			{
				owned = [];
				_activationCues.Add(activation, owned);
			}
			owned.Add(handle);
		}
		try
		{
			Dispatch(Event(GameplayCueKind.Added, slot, handle));
		}
		catch (Exception dispatchFailure)
		{
			GameplayCueEvent removed = Event(GameplayCueKind.Removed, slot, handle);
			RetireCue(slot);
			try
			{
				Dispatch(removed);
			}
			catch (Exception cleanupFailure)
			{
				throw new AggregateException(
					"Gameplay cue creation notification failed and its " +
					"presentation state also failed to clean up.",
					dispatchFailure,
					cleanupFailure);
			}
			System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(dispatchFailure).Throw();
		}
		return handle;
	}

	/// <summary>Updates magnitude, stack count and typed parameters of a persistent cue.</summary>
	public bool Update(GameplayCueHandle handle, float magnitude, int stackCount, GameplayEventPayload parameters = default)
	{
		ThrowIfDisposed(); ValidateMagnitude(magnitude);
		if (stackCount <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(stackCount));
		}

		if (!TryResolve(handle, out int slot))
		{
			return false;
		}

		_cues[slot].Magnitude = magnitude; _cues[slot].StackCount = stackCount; _cues[slot].Parameters = parameters;
		Dispatch(Event(GameplayCueKind.Updated, slot, handle));
		return true;
	}

	/// <summary>Removes a persistent cue. The removed event contains a dead identity, never a live record reference.</summary>
	public bool Remove(GameplayCueHandle handle)
	{
		ThrowIfDisposed();
		if (!TryResolve(handle, out int slot))
		{
			return false;
		}

		GameplayCueEvent removed = Event(GameplayCueKind.Removed, slot, handle);
		RetireCue(slot);
		Dispatch(removed);
		return true;
	}

	/// <summary>Subscribes to exact or hierarchical tag transitions, optionally scoped to an entity.</summary>
	public GameplayCueSubscription Subscribe(GameplayTag tag, Action<GameplayCueEvent> handler, GameplayCueMatch match = GameplayCueMatch.Exact, Entity owner = default)
	{
		ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(handler); ValidateTag(tag);
		if (match is not (GameplayCueMatch.Exact or GameplayCueMatch.Hierarchical))
		{
			throw new ArgumentOutOfRangeException(nameof(match));
		}

		if (!owner.Equals(default) && !_world.IsAlive(owner))
		{
			throw new ArgumentException("The subscription owner must be alive in this world.", nameof(owner));
		}

		int slot = AcquireSubscription(); ref SubscriptionSlot subscription = ref _subscriptions[slot];
		subscription.Tag = tag; subscription.Match = match; subscription.Owner = owner; subscription.Handler = handler;
		Dictionary<GameplayTag, int> buckets = match == GameplayCueMatch.Exact ? _exact : _hierarchical;
		LinkBucket(buckets, slot, tag);
		if (!owner.Equals(default))
		{
			LinkOwnerSubscription(owner, slot);
		}

		_liveSubscriptions++;
		return new GameplayCueSubscription(_systemId, slot, subscription.Generation);
	}

	/// <summary>Removes one subscription; stale and foreign handles are ignored.</summary>
	public bool Unsubscribe(GameplayCueSubscription handle)
	{
		ThrowIfDisposed(); if (!TryResolveSubscription(handle, out int slot))
		{
			return false;
		}

		UnlinkSubscription(slot); ReleaseSubscription(slot); _liveSubscriptions--; return true;
	}

	/// <summary>Removes every listener scoped to an entity.</summary>
	public int UnsubscribeAll(Entity owner)
	{
		ThrowIfDisposed(); if (!TryGetOwner(owner, out int segment, out int offset))
		{
			return 0;
		}

		ref OwnerSlot state = ref _owners[segment]![offset]; int count = 0; int slot = state.SubscriptionHead;
		while (slot >= 0) { int next = _subscriptions[slot].NextOnOwner; ReleaseSubscriptionByIndex(slot); count++; slot = next; }
		return count;
	}

	/// <summary>Copies one active cue's stable state.</summary>
	public bool TryGet(GameplayCueHandle handle, out GameplayCueEvent cue)
	{
		ThrowIfDisposed();
		if (TryResolve(handle, out int slot)) { cue = Event(GameplayCueKind.Added, slot, handle); return true; }
		cue = default; return false;
	}

	/// <summary>Disposes the system, emitting Removed for all remaining cues.</summary>
	public void Dispose()
	{
		if (_disposed || _disposing)
		{
			return;
		}

		_world.EntityDestroyed -= HandleEntityDestroyed;
		if (_effects is not null) { _effects.CueChanged -= HandleEffectChange; _effects = null; }
		_disposing = true;
		Exception? failure = null;
		for (int slot = 0; slot < _cueCount; slot++)
		{
			if (_cues[slot].Active)
			{
				GameplayCueEvent removed = Event(GameplayCueKind.Removed, slot, Handle(slot)); RetireCue(slot);
				try { Dispatch(removed); } catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
			}
		}

		for (int slot = 0; slot < _subscriptionCount; slot++)
		{
			if (_subscriptions[slot].Active)
			{
				ReleaseSubscriptionByIndex(slot);
			}
		}
		_subscriptions = []; _cues = []; _owners = []; _exact.Clear(); _hierarchical.Clear(); _effectCues.Clear(); _activationCues.Clear(); Changed = null; _disposed = true; _disposing = false;
		if (failure is not null)
		{
			System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
		}

		GC.SuppressFinalize(this);
	}

	private int Dispatch(in GameplayCueEvent cueEvent, bool bufferAlreadyQueued = false)
	{
		Exception? failure = null;
		if (!bufferAlreadyQueued)
		{
			try { _buffer?.Enqueue(cueEvent); }
			catch (Exception exception) { failure = exception; }
		}

		if (_liveSubscriptions == 0)
		{
			try { Changed?.Invoke(cueEvent); }
			catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
			if (failure is not null)
			{
				System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
			}

			return 0;
		}

		if (_dispatchDepth >= MaxDispatchDepth)
		{
			InvalidOperationException depthFailure = new($"Gameplay cue dispatch nested beyond {MaxDispatchDepth} levels.");
			failure = failure is null ? depthFailure : new AggregateException(failure, depthFailure);
			System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
		}

		if (_buffers.Length == _dispatchDepth)
		{
			Array.Resize(ref _buffers, _buffers.Length * 2);
		}

		CaptureBuffer buffer = _buffers[_dispatchDepth] ??= new CaptureBuffer();
		_dispatchDepth++;
		try
		{
			int captured = Capture(cueEvent.Tag, buffer);
			int delivered = 0;
			for (int index = 0; index < captured; index++)
			{
				CapturedSubscription candidate = buffer.Items[index];
				if (!TryResolveSubscription(candidate.Handle, out int slot))
				{
					continue;
				}

				ref SubscriptionSlot current = ref _subscriptions[slot];
				if (!current.Owner.Equals(default) && !_world.IsAlive(current.Owner))
				{
					continue;
				}

				try
				{
					current.Handler?.Invoke(cueEvent);
					delivered++;
				}
				catch (Exception exception)
				{
					failure = failure is null ? exception : new AggregateException(failure, exception);
				}
			}
			try { Changed?.Invoke(cueEvent); }
			catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
			if (failure is not null)
			{
				System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
			}

			return delivered;
		}
		finally { buffer.Count = 0; _dispatchDepth--; }
	}

	private int Capture(GameplayTag tag, CaptureBuffer buffer)
	{
		int count = CaptureBucket(_exact, tag, buffer, 0);
		count = CaptureBucket(_hierarchical, tag, buffer, count);
		ReadOnlySpan<GameplayTag> ancestors = GameplayTagRegistry.Shared.GetAncestors(tag);
		foreach (GameplayTag ancestor in ancestors)
		{
			count = CaptureBucket(_hierarchical, ancestor, buffer, count);
		}

		return count;
	}

	private int CaptureBucket(Dictionary<GameplayTag, int> buckets, GameplayTag tag, CaptureBuffer buffer, int count)
	{
		if (!buckets.TryGetValue(tag, out int slot))
		{
			return count;
		}

		while (slot >= 0)
		{
			ref SubscriptionSlot candidate = ref _subscriptions[slot];
			int next = candidate.NextOnTag;
			if (candidate.Active && count == buffer.Items.Length)
			{
				Array.Resize(ref buffer.Items, buffer.Items.Length * 2);
			}

			if (candidate.Active)
			{
				buffer.Items[count++] = new CapturedSubscription(new GameplayCueSubscription(_systemId, slot, candidate.Generation));
			}

			slot = next;
		}
		return count;
	}

	private int AcquireCue()
	{
		int slot;
		if (_freeCue >= 0) { slot = _freeCue; _freeCue = _cues[slot].NextFree; }
		else { if (_cueCount == _cues.Length) { Array.Resize(ref _cues, _cues.Length * 2); } slot = _cueCount++; }
		if (_cues[slot].Generation == 0)
		{
			_cues[slot].Generation = 1;
		}

		return slot;
	}
	private GameplayCueHandle Handle(int slot) => new(_systemId, slot, _cues[slot].Generation);
	private bool TryResolve(GameplayCueHandle handle, out int slot)
	{
		slot = handle.Slot; return !handle.IsNone && handle.SystemId == _systemId && (uint)slot < (uint)_cueCount && _cues[slot].Active && _cues[slot].Generation == handle.Generation;
	}
	internal void HandleEffectChange(Volt.Gameplay.Effects.GameplayEffectChange change)
	{
		ThrowIfDisposed();
		if (_applyingEffectChange)
		{
			(_deferredEffectChanges ??= []).Add(change);
			return;
		}

		_applyingEffectChange = true;
		try
		{
			HandleEffectChangeCore(change);
			while (_deferredEffectChanges is { Count: > 0 } deferred)
			{
				GameplayEffectChange next = deferred[0];
				deferred.RemoveAt(0);
				HandleEffectChangeCore(next);
			}
		}
		finally
		{
			_applyingEffectChange = false;
			_deferredEffectChanges?.Clear();
		}
	}

	private void HandleEffectChangeCore(Volt.Gameplay.Effects.GameplayEffectChange change)
	{
		if (!change.Definition.CueTag.IsValid)
		{
			return;
		}

		if (change.Definition.IsInstant)
		{
			if (change.Kind == GameplayEffectChangeKind.Applied)
			{
				Execute(change.Definition.CueTag, change.Source, change.Target, change.StackCount);
			}
			return;
		}
		if (change.Kind == GameplayEffectChangeKind.PeriodExecuted)
		{
			Execute(change.Definition.CueTag, change.Source, change.Target, change.StackCount);
			return;
		}

		switch (change.Kind)
		{
			case GameplayEffectChangeKind.Applied:
			case GameplayEffectChangeKind.Uninhibited:
				if (change.Definition.IsInstant)
				{
					if (change.Kind == GameplayEffectChangeKind.Applied)
					{
						Execute(change.Definition.CueTag, change.Source, change.Target, change.StackCount);
					}
					break;
				}
				if (!change.IsInhibited && !TryFindEffectCue(change.Handle, out _)
					&& _effects is not null && _effects.TryGetEffect(change.Handle, out _))
				{
					Add(change.Definition.CueTag, change.Source, change.Target, change.StackCount, change.StackCount, effect: change.Handle);
				}
				break;
			case GameplayEffectChangeKind.StackChanged:
				if (change.Definition.IsInstant)
				{
					break;
				}
				if (change.IsInhibited)
				{
					RemoveEffect(change.Handle);
				}
				else if (TryFindEffectCue(change.Handle, out GameplayCueHandle cue))
				{
					Update(cue, change.StackCount, change.StackCount);
				}
				else if (_effects is not null && _effects.TryGetEffect(change.Handle, out _))
				{
					Add(change.Definition.CueTag, change.Source, change.Target, change.StackCount, change.StackCount, effect: change.Handle);
				}
				break;
			case GameplayEffectChangeKind.Inhibited:
				RemoveEffect(change.Handle);
				break;
			case GameplayEffectChangeKind.Removed:
			case GameplayEffectChangeKind.Expired:
			case GameplayEffectChangeKind.TargetDestroyed:
				RemoveEffect(change.Handle);
				break;
		}
	}

	internal void RemoveActivation(GameplayAbilityActivation activation)
	{
		ThrowIfDisposed();
		if (activation.IsNone || !_activationCues.Remove(activation, out List<GameplayCueHandle>? owned))
		{
			return;
		}

		Exception? failure = null;
		foreach (GameplayCueHandle cue in owned)
		{
			try { Remove(cue); }
			catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
		}
		if (failure is not null)
		{
			System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
		}
	}

	internal bool IsDisposed => _disposed;

	internal bool TryFindEffectForTesting(GameplayEffectHandle effect, out GameplayCueHandle handle) => TryFindEffectCue(effect, out handle);

	private bool TryFindEffectCue(GameplayEffectHandle effect, out GameplayCueHandle handle)
	{
		if (!effect.IsNone && _effectCues.TryGetValue(effect, out handle) && TryResolve(handle, out _))
		{
			return true;
		}

		handle = GameplayCueHandle.None;
		return false;
	}

	private void RemoveEffect(Volt.Gameplay.Effects.GameplayEffectHandle effect)
	{
		if (TryFindEffectCue(effect, out GameplayCueHandle cue))
		{
			Remove(cue);
		}
	}

	private void RetireCue(int slot)
	{
		CueSlot old = _cues[slot];
		if (!old.Effect.IsNone)
		{
			_effectCues.Remove(old.Effect);
		}

		if (!old.Activation.IsNone && _activationCues.TryGetValue(old.Activation, out List<GameplayCueHandle>? owned))
		{
			owned.Remove(Handle(slot));
			if (owned.Count == 0)
			{
				_activationCues.Remove(old.Activation);
			}
		}
		if (!old.Owner.Equals(default) && TryGetOwner(old.Owner, out int segment, out int offset))
		{
			UnlinkOwner(ref _owners[segment]![offset], slot);
		}

		_activeCount--; _cues[slot] = new CueSlot { Generation = NextGeneration(old.Generation), NextOnOwner = -1, PreviousOnOwner = -1, NextFree = _freeCue }; _freeCue = slot;
	}
	private GameplayCueEvent Event(GameplayCueKind kind, int slot, GameplayCueHandle handle)
	{
		ref CueSlot cue = ref _cues[slot];
		return new GameplayCueEvent(kind, cue.Tag, handle, cue.Source, cue.Target, cue.Parameters, cue.Magnitude, cue.StackCount, cue.Activation, cue.Effect);
	}

	private int AcquireSubscription()
	{
		int slot;
		if (_freeSubscription >= 0) { slot = _freeSubscription; _freeSubscription = _subscriptions[slot].NextFree; }
		else { if (_subscriptionCount == _subscriptions.Length) { Array.Resize(ref _subscriptions, _subscriptions.Length * 2); } slot = _subscriptionCount++; }
		if (_subscriptions[slot].Generation == 0)
		{
			_subscriptions[slot].Generation = 1;
		}

		_subscriptions[slot].Active = true; _subscriptions[slot].PreviousOnTag = -1; _subscriptions[slot].NextOnTag = -1; _subscriptions[slot].PreviousOnOwner = -1; _subscriptions[slot].NextOnOwner = -1;
		return slot;
	}
	private void LinkBucket(Dictionary<GameplayTag, int> buckets, int slot, GameplayTag tag)
	{
		int head = buckets.TryGetValue(tag, out int existing) ? existing : -1;
		_subscriptions[slot].NextOnTag = head;
		if (head >= 0)
		{
			_subscriptions[head].PreviousOnTag = slot;
		}

		buckets[tag] = slot;
	}
	private void LinkOwner(Entity owner, int slot)
	{
		ref OwnerSlot state = ref GetOwner(owner); int head = state.CueHead;
		_cues[slot].NextOnOwner = head; _cues[slot].PreviousOnOwner = -1;
		if (head >= 0)
		{
			_cues[head].PreviousOnOwner = slot;
		}

		state.CueHead = slot;
	}
	private void LinkOwnerSubscription(Entity owner, int slot)
	{
		ref OwnerSlot state = ref GetOwner(owner); int head = state.SubscriptionHead;
		_subscriptions[slot].NextOnOwner = head; _subscriptions[slot].PreviousOnOwner = -1;
		if (head >= 0)
		{
			_subscriptions[head].PreviousOnOwner = slot;
		}

		state.SubscriptionHead = slot;
	}
	private void UnlinkOwner(ref OwnerSlot state, int slot)
	{
		int previous = _cues[slot].PreviousOnOwner, next = _cues[slot].NextOnOwner;
		if (previous >= 0)
		{
			_cues[previous].NextOnOwner = next;
		}
		else
		{
			state.CueHead = next;
		}

		if (next >= 0)
		{
			_cues[next].PreviousOnOwner = previous;
		}
	}
	private void UnlinkSubscription(int slot)
	{
		ref SubscriptionSlot subscription = ref _subscriptions[slot];
		Dictionary<GameplayTag, int> buckets = subscription.Match == GameplayCueMatch.Exact ? _exact : _hierarchical;
		if (subscription.PreviousOnTag >= 0)
		{
			_subscriptions[subscription.PreviousOnTag].NextOnTag = subscription.NextOnTag;
		}
		else if (subscription.NextOnTag < 0)
		{
			buckets.Remove(subscription.Tag);
		}
		else
		{
			buckets[subscription.Tag] = subscription.NextOnTag;
		}
		if (subscription.NextOnTag >= 0)
		{
			_subscriptions[subscription.NextOnTag].PreviousOnTag = subscription.PreviousOnTag;
		}

		if (!subscription.Owner.Equals(default) && TryGetOwner(subscription.Owner, out int segment, out int offset))
		{
			ref OwnerSlot state = ref _owners[segment]![offset]; int previous = subscription.PreviousOnOwner, next = subscription.NextOnOwner;
			if (previous >= 0)
			{
				_subscriptions[previous].NextOnOwner = next;
			}
			else
			{
				state.SubscriptionHead = next;
			}

			if (next >= 0)
			{
				_subscriptions[next].PreviousOnOwner = previous;
			}
		}
	}
	private void ReleaseSubscriptionByIndex(int slot) { UnlinkSubscription(slot); ReleaseSubscription(slot); _liveSubscriptions--; }
	private void ReleaseSubscription(int slot)
	{
		uint generation = NextGeneration(_subscriptions[slot].Generation);
		_subscriptions[slot] = new SubscriptionSlot { Generation = generation, NextFree = _freeSubscription };
		_freeSubscription = slot;
	}
	private bool TryResolveSubscription(GameplayCueSubscription handle, out int slot)
	{
		slot = handle.Slot; return !handle.IsNone && handle.SystemId == _systemId && (uint)slot < (uint)_subscriptionCount && _subscriptions[slot].Active && _subscriptions[slot].Generation == handle.Generation;
	}
	private ref OwnerSlot GetOwner(Entity owner)
	{
		int index = (int)owner.Index, segment = index >> 10;
		if (segment >= _owners.Length)
		{
			Array.Resize(ref _owners, Math.Max(segment + 1, _owners.Length * 2));
		}

		OwnerSlot[]? slots = _owners[segment]; if (slots is null)
		{
			_owners[segment] = slots = new OwnerSlot[1024];
		}

		ref OwnerSlot state = ref slots[index & 1023];
		if (state.Generation != owner.Generation)
		{
			if (state.Generation != 0 && (state.CueHead >= 0 || state.SubscriptionHead >= 0))
			{
				RetireStaleOwnerState(ref state);
			}

			state = new OwnerSlot { Generation = owner.Generation, CueHead = -1, SubscriptionHead = -1 };
		}
		return ref state;
	}
	private bool TryGetOwner(Entity owner, out int segment, out int offset)
	{
		segment = (int)owner.Index >> 10; offset = (int)owner.Index & 1023;
		return owner.AllocatorId == _allocatorId && (uint)segment < (uint)_owners.Length && _owners[segment] is not null && _owners[segment]![offset].Generation == owner.Generation;
	}
	private void UnlinkOwnerSubscription(ref OwnerSlot state, int slot)
	{
		int previous = _subscriptions[slot].PreviousOnOwner, next = _subscriptions[slot].NextOnOwner; if (previous >= 0)
		{
			_subscriptions[previous].NextOnOwner = next;
		}
		else
		{
			state.SubscriptionHead = next;
		}

		if (next >= 0)
		{
			_subscriptions[next].PreviousOnOwner = previous;
		}
	}
	private void RetireStaleOwnerState(ref OwnerSlot state)
	{
		int cue = state.CueHead;
		while (cue >= 0)
		{
			int next = _cues[cue].NextOnOwner;
			if (_cues[cue].Active)
			{
				RetireCue(cue);
			}

			cue = next;
		}
		int subscription = state.SubscriptionHead;
		while (subscription >= 0)
		{
			int next = _subscriptions[subscription].NextOnOwner;
			uint nextGeneration = next >= 0 ? _subscriptions[next].Generation : 0;
			if (_subscriptions[subscription].Active)
			{
				ReleaseSubscriptionByIndex(subscription);
			}

			subscription = next;
			if (subscription >= 0 && _subscriptions[subscription].Generation != nextGeneration)
			{
				break;
			}
		}
	}
	private void HandleEntityDestroyed(Entity entity)
	{
		if (!TryGetOwner(entity, out int segment, out int offset))
		{
			return;
		}

		if (_destructionDepth >= MaxDestructionDepth)
		{
			throw new InvalidOperationException($"Gameplay cue entity-destruction callbacks nested beyond {MaxDestructionDepth} levels.");
		}

		if (_destructionBuffers.Length == _destructionDepth)
		{
			Array.Resize(ref _destructionBuffers, _destructionBuffers.Length * 2);
		}

		DestructionBuffer buffer = _destructionBuffers[_destructionDepth] ??= new DestructionBuffer();
		_destructionDepth++;
		try
		{
			buffer.Count = 0;
			ref OwnerSlot state = ref _owners[segment]![offset];
			int cue = state.CueHead;
			while (cue >= 0)
			{
				int next = _cues[cue].NextOnOwner;
				uint nextGeneration = next >= 0 ? _cues[next].Generation : 0;
				if (_cues[cue].Active && _cues[cue].Owner == entity)
				{
					if (buffer.Count == buffer.Events.Length)
					{
						Array.Resize(ref buffer.Events, buffer.Events.Length * 2);
					}

					buffer.Events[buffer.Count++] = Event(GameplayCueKind.Removed, cue, Handle(cue));
					RetireCue(cue);
				}
				cue = next;
				if (cue >= 0 && _cues[cue].Generation != nextGeneration)
				{
					break;
				}
			}
			int subscription = state.SubscriptionHead;
			while (subscription >= 0)
			{
				int next = _subscriptions[subscription].NextOnOwner;
				if (_subscriptions[subscription].Active && _subscriptions[subscription].Owner == entity)
				{
					ReleaseSubscriptionByIndex(subscription);
				}

				subscription = next;
			}
			if (TryGetOwner(entity, out segment, out offset))
			{
				_owners[segment]![offset].CueHead = -1;
				_owners[segment]![offset].SubscriptionHead = -1;
			}
			Exception? failure = null;
			for (int index = 0; index < buffer.Count; index++)
			{
				try { Dispatch(buffer.Events[index]); }
				catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
				buffer.Events[index] = default;
			}
			buffer.Count = 0;
			if (failure is not null)
			{
				System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
			}
		}
		finally { _destructionDepth--; }
	}
	private static uint NextGeneration(uint value) { value++; return value == 0 ? 1 : value; }
	private static uint NextSystemId() { uint id = unchecked((uint)Interlocked.Increment(ref s_nextSystemId)); return id == 0 ? unchecked((uint)Interlocked.Increment(ref s_nextSystemId)) : id; }
	private static void ValidateTag(GameplayTag tag)
	{
		if (!tag.IsValid)
		{
			throw new ArgumentException("A gameplay cue requires a registered tag.", nameof(tag));
		}
	}
	private static void ValidateMagnitude(float magnitude)
	{
		if (!float.IsFinite(magnitude))
		{
			throw new ArgumentOutOfRangeException(nameof(magnitude));
		}
	}
	private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed || _disposing, this);

	private struct CueSlot
	{
		public uint Generation; public bool Active; public GameplayTag Tag; public Entity Source; public Entity Target; public Entity Owner; public float Magnitude; public int StackCount; public GameplayEventPayload Parameters; public GameplayAbilityActivation Activation; public Volt.Gameplay.Effects.GameplayEffectHandle Effect; public int PreviousOnOwner; public int NextOnOwner; public int NextFree;
	}
	private struct SubscriptionSlot
	{
		public uint Generation; public bool Active; public GameplayTag Tag; public GameplayCueMatch Match; public Entity Owner; public Action<GameplayCueEvent>? Handler; public int PreviousOnTag; public int NextOnTag; public int PreviousOnOwner; public int NextOnOwner; public int NextFree;
	}
	private struct OwnerSlot { public uint Generation; public int CueHead; public int SubscriptionHead; }
	private sealed class CaptureBuffer { public CapturedSubscription[] Items = new CapturedSubscription[8]; public int Count; }
	private readonly struct CapturedSubscription { public CapturedSubscription(GameplayCueSubscription handle) => Handle = handle; public GameplayCueSubscription Handle { get; } }
	private sealed class DestructionBuffer { public GameplayCueEvent[] Events = new GameplayCueEvent[InitialCapacity]; public int Count; }
}
