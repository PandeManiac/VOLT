namespace Volt.Core.Timing;

/// <summary>
/// Runs registered callbacks on fixed simulation steps at requested
/// frequencies, intervals, or after a delay.
/// </summary>
/// <remarks>
/// <para>
/// Deadlines are absolute fixed-step indices derived from the step
/// that was current at registration and from rational periods, never
/// from the time a callback actually ran. A 7 Hz callback registered
/// before the first step on a 60 Hz simulation therefore fires on
/// steps 9, 18, 26, 35, 43, 52, 60, ... and averages exactly seven
/// invocations per simulation second instead of being truncated to
/// 8 steps (7.5 Hz). Registered at step 100 it fires on steps 109,
/// 118, 126, ... and still averages exactly 7 Hz. Callbacks always
/// run on a step boundary, so a requested rate above the fixed rate
/// cannot be honored and is rejected rather than silently rounded.
/// </para>
/// <para>
/// Periods and delays are relative to the fixed step at registration.
/// <c>After(5 s)</c> registered after step 400 runs at step 700, not
/// on the next step, and a recurring registration keeps its rational
/// cadence relative to that origin even when an invocation is late.
/// Work registered during a fixed step, from a fixed-update or a
/// scheduled callback, can never run on that same step: its earliest
/// deadline is the following step.
/// </para>
/// <para>
/// The next due entry is found through a binary min-heap ordered by
/// deadline and then by registration order. Idle polling is O(1)
/// (the root deadline is compared once), processing a due callback is
/// O(log n) to remove and, for recurring work, reinsert it, and
/// registration or cancellation is O(log n). Nothing is scanned per
/// frame, and a recurring entry is never allocated again.
/// </para>
/// <para>
/// Every callback executes synchronously on the thread that calls
/// <see cref="GameLoop.Advance"/>. The scheduler never uses a timer, a
/// task, a background thread, or a per-invocation allocation.
/// </para>
/// <para>
/// The scheduler is not thread-safe and must be used from the game
/// thread, like the loop that owns it.
/// </para>
/// </remarks>
public sealed class SimulationScheduler
{
	private const int InitialCapacity = 8;

	private Entry[] _entries = new Entry[InitialCapacity];
	private int[] _heap = new int[InitialCapacity];
	private int[] _heapPositions = new int[InitialCapacity];

	// Pre-sized so the first release of a slot does not grow the
	// stack during a frame. Registration pays for it, not steady
	// state.
	private readonly Stack<int> _freeSlots = new(InitialCapacity);
	private readonly int _fixedUpdatesPerSecond;

	private int _heapCount;
	private int _slotCount;
	private int _activeCount;
	private long _nextOrder;
	private long _currentStep;
	private long _totalInvocations;
	private long _skippedInvocations;

	internal SimulationScheduler(int fixedUpdatesPerSecond)
	{
		if (fixedUpdatesPerSecond <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(fixedUpdatesPerSecond),
				fixedUpdatesPerSecond,
				"The fixed simulation rate must be positive.");
		}

		_fixedUpdatesPerSecond = fixedUpdatesPerSecond;

		Array.Fill(_heapPositions, -1);
	}

	/// <summary>
	/// Gets the fixed simulation rate the scheduler advances at.
	/// </summary>
	public int FixedUpdatesPerSecond => _fixedUpdatesPerSecond;

	/// <summary>
	/// Gets how many registrations are still active.
	/// </summary>
	public int ActiveCount => _activeCount;

	/// <summary>
	/// Gets the total callback invocations since construction.
	/// </summary>
	public long TotalInvocations => _totalInvocations;

	/// <summary>
	/// Gets the total intended occurrences skipped because simulation
	/// time had already passed their deadlines.
	/// </summary>
	/// <remarks>
	/// A long pause with single steps, or a frame that jumped several
	/// seconds, produces a bounded number of invocations and reports
	/// the rest here instead of replaying them.
	/// </remarks>
	public long SkippedInvocations => _skippedInvocations;

	/// <summary>
	/// Gets the fixed step of the next due callback, or <c>-1</c>
	/// when no registration is pending.
	/// </summary>
	public long NextDeadlineStep =>
		_heapCount == 0 ? -1 : _entries[_heap[0]].NextStep;

	/// <summary>
	/// Runs a callback at a requested simulation frequency.
	/// </summary>
	/// <param name="updatesPerSecond">
	/// Desired invocations per simulation second. It must be positive
	/// and no greater than the loop's fixed rate.
	/// </param>
	/// <param name="callback">The callback to invoke.</param>
	/// <returns>A handle that can cancel the registration.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The rate is not positive or exceeds the fixed rate.
	/// </exception>
	/// <remarks>
	/// Rates that divide the fixed rate have a uniform period. Other
	/// rates use alternating whole-step periods whose long-run average
	/// is exact; each invocation runs on the first eligible step and
	/// therefore has bounded quantization jitter of less than one
	/// fixed step. The first invocation is one period after the step
	/// that was current at registration, so registering late does not
	/// shift the cadence.
	/// </remarks>
	public ScheduledHandle EveryHz(
		int updatesPerSecond,
		ScheduledUpdateCallback callback)
	{
		ArgumentNullException.ThrowIfNull(callback);

		if (updatesPerSecond <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(updatesPerSecond),
				updatesPerSecond,
				"A scheduled frequency must be positive.");
		}

		if (updatesPerSecond > _fixedUpdatesPerSecond)
		{
			throw new ArgumentOutOfRangeException(
				nameof(updatesPerSecond),
				updatesPerSecond,
				$"A scheduled frequency cannot exceed the fixed " +
				$"simulation rate of {_fixedUpdatesPerSecond} Hz. " +
				"A callback cannot run more often than the simulation " +
				"advances, and silently rounding the rate would " +
				"misrepresent it.");
		}

		return Register(
			callback,
			_fixedUpdatesPerSecond,
			updatesPerSecond,
			repeating: true);
	}

	/// <summary>
	/// Runs a callback once per simulation interval.
	/// </summary>
	/// <param name="interval">The simulation-time interval.</param>
	/// <param name="callback">The callback to invoke.</param>
	/// <returns>A handle that can cancel the registration.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The interval is not positive or is shorter than one fixed step.
	/// </exception>
	/// <remarks>
	/// The first invocation happens one full interval after the
	/// simulation step that was current at registration. The
	/// interval is converted to an exact rational number of fixed
	/// steps, so 750 ms at 60 Hz is exactly 45 steps and does not
	/// drift. An interval below one fixed step cannot be represented
	/// and is rejected.
	/// </remarks>
	public ScheduledHandle Every(
		TimeSpan interval,
		ScheduledUpdateCallback callback)
	{
		ArgumentNullException.ThrowIfNull(callback);

		long numerator = IntervalNumerator(interval, nameof(interval));
		long denominator = TimeSpan.TicksPerSecond;

		if (numerator < denominator)
		{
			throw new ArgumentOutOfRangeException(
				nameof(interval),
				interval,
				$"The interval is shorter than one fixed step of " +
				$"1/{_fixedUpdatesPerSecond} second. A callback cannot " +
				"run more often than the simulation advances.");
		}

		return Register(callback, numerator, denominator, repeating: true);
	}

	/// <summary>
	/// Runs a callback once, after a simulation-time delay.
	/// </summary>
	/// <param name="delay">The delay before the single invocation.</param>
	/// <param name="callback">The callback to invoke.</param>
	/// <returns>A handle that can cancel the registration.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The delay is negative.
	/// </exception>
	/// <remarks>
	/// The delay is measured from the step that was current at
	/// registration. The registration is removed after its invocation.
	/// A delay of zero runs on the next fixed step, never
	/// synchronously inside the registration call and never on the
	/// step that is already executing.
	/// </remarks>
	public ScheduledHandle After(
		TimeSpan delay,
		ScheduledUpdateCallback callback)
	{
		ArgumentNullException.ThrowIfNull(callback);

		if (delay < TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(
				nameof(delay),
				delay,
				"A scheduled delay cannot be negative.");
		}

		// A zero delay is legal and means the next fixed step. Only a
		// positive delay needs the interval conversion.
		long numerator = delay == TimeSpan.Zero
			? 0
			: IntervalNumerator(delay, nameof(delay));

		return Register(
			callback,
			numerator,
			TimeSpan.TicksPerSecond,
			repeating: false);
	}

	/// <summary>
	/// Cancels a registration.
	/// </summary>
	/// <param name="handle">The handle returned at registration.</param>
	/// <returns>
	/// <c>true</c> when this call cancelled the registration;
	/// <c>false</c> when the handle was already cancelled, already
	/// completed, or belongs to no registration.
	/// </returns>
	/// <remarks>
	/// Cancelling twice is safe: the second call reports
	/// <c>false</c>. Cancelling from inside any callback is
	/// supported; a callback that cancels the registration that is
	/// currently executing simply stops it from running again.
	/// </remarks>
	public bool Cancel(ScheduledHandle handle)
	{
		if (handle.IsNone)
		{
			return false;
		}

		int slot = handle.Slot;

		if ((uint)slot >= (uint)_slotCount)
		{
			return false;
		}

		if (!_entries[slot].Active ||
			_entries[slot].Generation != handle.Generation)
		{
			return false;
		}

		int heapIndex = _heapPositions[slot];

		if (heapIndex >= 0)
		{
			RemoveAt(heapIndex);
		}

		ReleaseSlot(slot);

		return true;
	}

	/// <summary>
	/// Records that a fixed step has begun, before its fixed-update
	/// callbacks run.
	/// </summary>
	/// <param name="stepIndex">The one-based fixed step index.</param>
	/// <remarks>
	/// Registration uses this value as the simulation step at
	/// registration, so a fixed-update callback that registers
	/// <see cref="After(TimeSpan, ScheduledUpdateCallback)">work with no
	/// delay</see> cannot make it run on the step that is currently
	/// executing.
	/// </remarks>
	internal void BeginStep(long stepIndex)
	{
		_currentStep = stepIndex;
	}

	/// <summary>
	/// Runs every callback due on the given fixed step.
	/// </summary>
	/// <param name="stepIndex">The one-based fixed step index.</param>
	/// <remarks>
	/// Each entry runs at most once per step. A recurring entry is
	/// advanced to the next deadline after the current step, so a
	/// large jump in simulation time cannot produce an unbounded
	/// burst. Work registered during dispatch is eligible on the next
	/// step, never in the pass that is already running.
	/// </remarks>
	internal void Advance(long stepIndex)
	{
		BeginStep(stepIndex);

		while (_heapCount > 0)
		{
			int slot = _heap[0];

			if (_entries[slot].NextStep > stepIndex)
			{
				break;
			}

			// Remove the entry before invoking user code: the
			// callback may register or cancel work, which can
			// resize the entry array and invalidate references.
			Entry due = _entries[slot];

			RemoveAt(0);

			long nextOccurrence = due.NextOccurrence + 1;
			long skipped = 0;

			if (due.Repeating)
			{
				long computed = NextOccurrenceAfterStep(
					stepIndex,
					due.Origin,
					due.Numerator,
					due.Denominator);

				if (computed > due.NextOccurrence)
				{
					nextOccurrence = computed;
					skipped = computed - due.NextOccurrence - 1;
				}
			}

			ScheduledUpdateContext context = new(
				stepIndex,
				stepIndex / (double)_fixedUpdatesPerSecond,
				due.NextOccurrence,
				skipped);

			due.Callback!(in context);

			_totalInvocations++;
			_skippedInvocations += skipped;

			if (!_entries[slot].Active ||
				_entries[slot].Generation != due.Generation)
			{
				// Cancelled, or cancelled and reused, by the
				// callback (or another callback that ran before
				// this check).
				continue;
			}

			if (due.Repeating)
			{
				ref Entry entry = ref _entries[slot];

				entry.NextOccurrence = nextOccurrence;
				entry.NextStep = DeadlineStep(
					entry.Origin,
					nextOccurrence,
					entry.Numerator,
					entry.Denominator);

				Push(slot);
			}
			else
			{
				ReleaseSlot(slot);
			}
		}
	}

	private ScheduledHandle Register(
		ScheduledUpdateCallback callback,
		long numerator,
		long denominator,
		bool repeating)
	{
		int slot = AllocateSlot();
		long origin = _currentStep;

		ref Entry entry = ref _entries[slot];

		entry.Callback = callback;
		entry.Numerator = numerator;
		entry.Denominator = denominator;
		entry.Origin = origin;
		entry.NextOccurrence = 1;
		entry.Order = _nextOrder++;
		entry.Repeating = repeating;
		entry.Active = true;

		long deadline = DeadlineStep(origin, 1, numerator, denominator);

		// A registration made between steps, or from inside a
		// callback, must never run in the pass that is already
		// executing or the step that is currently executing.
		if (deadline <= origin)
		{
			deadline = origin == long.MaxValue
				? long.MaxValue
				: origin + 1;
		}

		entry.NextStep = deadline;

		ScheduledHandle handle = new(slot, entry.Generation);

		Push(slot);

		return handle;
	}

	private int AllocateSlot()
	{
		int slot;

		if (_freeSlots.Count > 0)
		{
			slot = _freeSlots.Pop();
		}
		else
		{
			if (_slotCount == _entries.Length)
			{
				Grow();
			}

			slot = _slotCount++;

			_entries[slot].Generation = 1;
			_heapPositions[slot] = -1;
		}

		_activeCount++;

		return slot;
	}

	private void ReleaseSlot(int slot)
	{
		ref Entry entry = ref _entries[slot];

		entry.Active = false;
		entry.Callback = null;
		entry.Repeating = false;

		entry.Generation++;

		if (entry.Generation == 0)
		{
			entry.Generation = 1;
		}

		_activeCount--;
		_freeSlots.Push(slot);
	}

	private void Grow()
	{
		int capacity = _entries.Length * 2;

		int previousCapacity = _entries.Length;

		Array.Resize(ref _entries, capacity);
		Array.Resize(ref _heapPositions, capacity);
		Array.Resize(ref _heap, capacity);

		for (int index = previousCapacity; index < capacity; index++)
		{
			_heapPositions[index] = -1;
		}
	}

	private void Push(int slot)
	{
		_heap[_heapCount] = slot;
		_heapPositions[slot] = _heapCount;
		_heapCount++;

		SiftUp(_heapCount - 1);
	}

	private void RemoveAt(int heapIndex)
	{
		int removed = _heap[heapIndex];

		_heapCount--;

		_heapPositions[removed] = -1;

		if (heapIndex == _heapCount)
		{
			return;
		}

		_heap[heapIndex] = _heap[_heapCount];
		_heapPositions[_heap[heapIndex]] = heapIndex;

		SiftDown(heapIndex);
		SiftUp(heapIndex);
	}

	private void SiftUp(int index)
	{
		while (index > 0)
		{
			int parent = (index - 1) / 2;

			if (!Less(_heap[index], _heap[parent]))
			{
				return;
			}

			Swap(index, parent);
			index = parent;
		}
	}

	private void SiftDown(int index)
	{
		while (true)
		{
			int left = (index * 2) + 1;

			if (left >= _heapCount)
			{
				return;
			}

			int right = left + 1;
			int smallest = right < _heapCount &&
				Less(_heap[right], _heap[left])
				? right
				: left;

			if (!Less(_heap[smallest], _heap[index]))
			{
				return;
			}

			Swap(index, smallest);
			index = smallest;
		}
	}

	private void Swap(int left, int right)
	{
		int slot = _heap[left];

		_heap[left] = _heap[right];
		_heap[right] = slot;

		_heapPositions[_heap[left]] = left;
		_heapPositions[_heap[right]] = right;
	}

	private bool Less(int left, int right)
	{
		ref Entry leftEntry = ref _entries[left];
		ref Entry rightEntry = ref _entries[right];

		if (leftEntry.NextStep != rightEntry.NextStep)
		{
			return leftEntry.NextStep < rightEntry.NextStep;
		}

		return leftEntry.Order < rightEntry.Order;
	}

	private long IntervalNumerator(TimeSpan interval, string parameterName)
	{
		if (interval <= TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(
				parameterName,
				interval,
				"A scheduled interval must be positive.");
		}

		if (interval.Ticks >
			long.MaxValue / _fixedUpdatesPerSecond)
		{
			throw new ArgumentOutOfRangeException(
				parameterName,
				interval,
				"The interval is too large to be represented in " +
				"fixed steps.");
		}

		return interval.Ticks * _fixedUpdatesPerSecond;
	}

	private static long NextOccurrenceAfterStep(
		long stepIndex,
		long origin,
		long numerator,
		long denominator)
	{
		Int128 elapsed = (Int128)stepIndex - origin;

		if (elapsed <= 0)
		{
			return 1;
		}

		Int128 occurrences = elapsed * denominator / numerator;

		// The next deadline must be strictly after this step. The
		// +1 below is what skips missed occurrences instead of
		// replaying them; the deadline remains on the original
		// schedule.
		Int128 next = occurrences + 1;

		return next > long.MaxValue
			? long.MaxValue
			: (long)next;
	}

	private static long DeadlineStep(
		long origin,
		long occurrence,
		long numerator,
		long denominator)
	{
		if (numerator == 0)
		{
			return origin;
		}

		Int128 product = (Int128)occurrence * numerator;
		Int128 steps = (product + denominator - 1) / denominator;
		Int128 deadline = (Int128)origin + steps;

		return deadline >= long.MaxValue
			? long.MaxValue
			: (long)deadline;
	}

	private struct Entry
	{
		public ScheduledUpdateCallback? Callback;
		public long Numerator;
		public long Denominator;
		public long Origin;
		public long NextStep;
		public long NextOccurrence;
		public long Order;
		public uint Generation;
		public bool Repeating;
		public bool Active;
	}
}
