namespace Volt.Core.Timing;

/// <summary>
/// Owns the application's frame timing: one <c>Update</c> per frame,
/// zero or more fixed simulation steps per frame, and scheduled
/// simulation work, all driven by a monotonic clock.
/// </summary>
/// <remarks>
/// <para>
/// One call to <see cref="Advance"/> performs the following work in
/// this order:
/// </para>
/// <list type="number">
/// <item>Sample the monotonic clock and compute the elapsed interval.</item>
/// <item>Clamp the interval to
/// <see cref="GameLoopSettings.MaximumFrameDelta"/> and add the
/// accepted time to the fixed-step accumulator.</item>
/// <item>Execute up to
/// <see cref="GameLoopSettings.MaxFixedStepsPerFrame"/> fixed steps,
/// stopping early if a callback pauses the loop. For each step,
/// fixed-update callbacks run in registration order, then the
/// scheduled callbacks due on that step.</item>
/// <item>Execute the registered update callbacks once, in
/// registration order.</item>
/// </list>
/// <para>
/// The application polls input before calling <see cref="Advance"/>
/// and renders afterwards using <see cref="InterpolationAlpha"/>. A
/// failed, skipped, or rejected presentation therefore never suppresses
/// an update and never resets simulation time; the loop has no
/// knowledge of a graphics device at all.
/// </para>
/// <para>
/// Simulation time advances only through fixed steps. It is computed
/// from an integer accumulator, never by repeatedly adding an
/// approximate 1/60 second, so the long-run phase does not drift.
/// </para>
/// <para>
/// The loop is single-threaded: callbacks run synchronously on the
/// thread that calls <see cref="Advance"/>. It does not require a
/// window, SDL, Vulkan, the ECS, or the job scheduler. Steady-state
/// frames allocate nothing; registration grows its storage.
/// </para>
/// </remarks>
public sealed class GameLoop
{
	private const int InitialCallbackCapacity = 4;

	private readonly GameLoopSettings _settings;
	private readonly IMonotonicClock _clock;
	private readonly long _maximumFrameDeltaNanoseconds;
	private readonly float _fixedDeltaSeconds;
	private readonly TimeSpan _fixedDelta;

	private UpdateCallback[] _updates = new UpdateCallback[InitialCallbackCapacity];
	private FixedUpdateCallback[] _fixedUpdates =
		new FixedUpdateCallback[InitialCallbackCapacity];

	private int _updateCount;
	private int _fixedUpdateCount;

	private PostFixedUpdateCallback[] _postFixedUpdates =
		new PostFixedUpdateCallback[InitialCallbackCapacity];

	private int _postFixedUpdateCount;

	private FixedStepAccumulator _accumulator;
	private Int128 _nanosecondCarry;
	private Int128 _scaleCarry;
	private TimeScale _requestedTimeScale = TimeScale.Normal;
	private TimeScale _effectiveTimeScale = TimeScale.Normal;
	private long _lastTimestamp;
	private long _frameIndex;
	private long _totalFixedSteps;
	private long _totalDroppedNanoseconds;
	private long _totalDroppedSimulationNanoseconds;
	private bool _paused;
	private bool _singleStepPending;
	private bool _advancing;
	private FrameTiming _lastFrame;

	/// <summary>
	/// Creates a loop.
	/// </summary>
	/// <param name="settings">
	/// Timing settings, or <see langword="null"/> for
	/// <see cref="GameLoopSettings.Default"/>.
	/// </param>
	/// <param name="clock">
	/// The monotonic clock, or <see langword="null"/> for
	/// <see cref="MonotonicClock.Shared"/>.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The clock reports an invalid rate.
	/// </exception>
	/// <remarks>
	/// The clock is sampled when the loop is created, so the first
	/// <see cref="Advance"/> observes only the time since construction
	/// instead of fabricating a large initial delta.
	/// </remarks>
	public GameLoop(
		GameLoopSettings? settings = null,
		IMonotonicClock? clock = null)
	{
		_settings = settings ?? GameLoopSettings.Default;
		_settings.Validate();

		_clock = clock ?? MonotonicClock.Shared;

		if (_clock.TicksPerSecond <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(clock),
				_clock.TicksPerSecond,
				"The monotonic clock must report a positive tick rate.");
		}

		_maximumFrameDeltaNanoseconds = checked(
			_settings.MaximumFrameDelta.Ticks * 100);

		_fixedDeltaSeconds = 1.0f / _settings.FixedUpdatesPerSecond;
		_fixedDelta = TimeSpan.FromSeconds(
			1.0 / _settings.FixedUpdatesPerSecond);

		_lastTimestamp = _clock.Timestamp;
		_lastFrame = new FrameTiming(
			frameIndex: 0,
			realDelta: TimeSpan.Zero,
			scaledDelta: TimeSpan.Zero,
			droppedTime: TimeSpan.Zero,
			droppedSimulationTime: TimeSpan.Zero,
			fixedSteps: 0,
			totalFixedSteps: 0,
			simulationDeltaSeconds: 0.0,
			accumulatedRemainderSeconds: 0.0,
			interpolationAlpha: 0.0f,
			timeScale: TimeScale.Normal,
			isPaused: false);

		Schedule = new SimulationScheduler(
			_settings.FixedUpdatesPerSecond);
	}

	/// <summary>
	/// Gets the timing settings.
	/// </summary>
	public GameLoopSettings Settings => _settings;

	/// <summary>
	/// Gets the scheduler for simulation-time work.
	/// </summary>
	public SimulationScheduler Schedule { get; }

	/// <summary>
	/// Gets or sets the requested simulation time scale.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The scale multiplies accepted real elapsed time before it
	/// reaches the fixed-step accumulator. <c>1.0x</c> is normal,
	/// <c>0.5x</c> is half speed, and <c>0.0x</c> freezes simulation
	/// time without pausing the loop. Negative and non-finite values
	/// are rejected when the value is constructed.
	/// </para>
	/// <para>
	/// A change made between frames applies to the next
	/// <see cref="Advance"/>. A change made by a callback applies to
	/// the next frame, never to the fixed steps already planned for
	/// the current one. Sub-nanosecond scaled time accumulated under
	/// the previous scale is discarded, which is at most one
	/// nanosecond per change.
	/// </para>
	/// <para>
	/// An uninitialized <c>default(TimeScale)</c> is rejected instead
	/// of being treated as frozen time: it has no denominator and
	/// cannot be used in the loop's rational arithmetic. Assign
	/// <see cref="TimeScale.Zero"/> to freeze simulation time.
	/// </para>
	/// </remarks>
	/// <exception cref="ArgumentException">
	/// The assigned value is <c>default(TimeScale)</c> rather than a
	/// constructed scale.
	/// </exception>
	public TimeScale TimeScale
	{
		get => _requestedTimeScale;
		set
		{
			if (!value.IsInitialized)
			{
				throw new ArgumentException(
					"A time scale must be initialized. " +
					"default(TimeScale) has no denominator and is not " +
					"zero; assign TimeScale.Zero to freeze simulation " +
					"time.",
					nameof(value));
			}

			_requestedTimeScale = value;
		}
	}

	/// <summary>
	/// Gets the time scale that the most recent
	/// <see cref="Advance"/> used.
	/// </summary>
	public TimeScale EffectiveTimeScale => _effectiveTimeScale;

	/// <summary>
	/// Gets whether the simulation is paused.
	/// </summary>
	public bool IsPaused => _paused;

	/// <summary>
	/// Gets whether a <see cref="StepOnce"/> request is waiting for
	/// the next <see cref="Advance"/>.
	/// </summary>
	public bool IsSingleStepPending =>
		_paused && _singleStepPending;

	/// <summary>
	/// Gets the interpolation fraction for the most recently
	/// completed frame, in <c>[0, 1)</c>.
	/// </summary>
	public float InterpolationAlpha => ToAlpha(_accumulator.Alpha);

	/// <summary>
	/// Gets the total fixed steps executed since construction.
	/// </summary>
	public long TotalFixedSteps => _totalFixedSteps;

	/// <summary>
	/// Gets the total real time discarded by the elapsed-time clamp,
	/// the fixed-step cap, or a pause requested during a frame.
	/// </summary>
	/// <remarks>
	/// The value is measured on the real-time clock, so it can be
	/// compared directly with the clock and with
	/// <see cref="FrameTiming.RealDelta"/>. At <c>2.0x</c>, one
	/// dropped simulation second corresponds to half a second here.
	/// </remarks>
	public TimeSpan TotalDroppedTime =>
		TimeSpan.FromTicks(_totalDroppedNanoseconds / 100);

	/// <summary>
	/// Gets the total simulation time discarded by the elapsed-time
	/// clamp, the fixed-step cap, or a pause requested during a frame.
	/// </summary>
	/// <remarks>
	/// The value is measured in simulation seconds. At <c>1.0x</c> it
	/// equals <see cref="TotalDroppedTime"/>; at <c>2.0x</c> it is
	/// twice as large, because a dropped real second represents two
	/// dropped simulation seconds.
	/// </remarks>
	public TimeSpan TotalDroppedSimulationTime =>
		TimeSpan.FromTicks(
			_totalDroppedSimulationNanoseconds / 100);

	/// <summary>
	/// Gets the simulation time, in seconds, at the end of the last
	/// completed frame.
	/// </summary>
	public double SimulationTimeSeconds =>
		_totalFixedSteps / (double)_settings.FixedUpdatesPerSecond;

	/// <summary>
	/// Gets the most recently completed frame.
	/// </summary>
	public FrameTiming LastFrame => _lastFrame;

	/// <summary>
	/// Gets the fixed step duration, in seconds.
	/// </summary>
	public float FixedDeltaSeconds => _fixedDeltaSeconds;

	/// <summary>
	/// Advances the loop by one application frame.
	/// </summary>
	/// <returns>The timing of the completed frame.</returns>
	/// <exception cref="InvalidOperationException">
	/// Called while another <see cref="Advance"/> is still executing,
	/// or an update or fixed-update registration was attempted from
	/// inside a callback.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Call this exactly once per application iteration, after polling
	/// input and before rendering. Update callbacks run even while
	/// paused; fixed and scheduled simulation work does not.
	/// </para>
	/// <para>
	/// <see cref="Pause"/> may be called from inside a callback. The
	/// step that is executing completes, no later step planned for the
	/// frame runs, and update callbacks still run once.
	/// </para>
	/// </remarks>
	public FrameTiming Advance()
	{
		if (_advancing)
		{
			throw new InvalidOperationException(
				"GameLoop.Advance() cannot be called while another " +
				"Advance() is still executing. Update, fixed-update, " +
				"and scheduled callbacks all run inside Advance(); a " +
				"nested advance would sample the clock and mutate frame " +
				"state twice. Let the current frame finish and call " +
				"Advance() from the application loop instead.");
		}


		// The scale is captured once per outer frame. A callback that
		// changes it affects the next frame, never the batch of fixed
		// steps that is already being planned.
		TimeScale timeScale = _requestedTimeScale;

		if (!timeScale.Equals(_effectiveTimeScale))
		{
			_effectiveTimeScale = timeScale;

			// The carry is a fraction of a nanosecond of scaled time
			// denominated in the previous scale, so reinterpreting it
			// would be wrong. At most one nanosecond of scaled time
			// is discarded, once per change.
			_scaleCarry = Int128.Zero;
		}

		long timestamp = _clock.Timestamp;
		long elapsedTicks = timestamp - _lastTimestamp;

		_lastTimestamp = timestamp;

		if (elapsedTicks < 0)
		{
			// A clock that moved backwards cannot represent elapsed
			// time. Treat the frame as having taken no time rather
			// than moving simulation backwards or overflowing.
			elapsedTicks = 0;
		}

		long elapsedNanoseconds = ToNanoseconds(elapsedTicks);
		long acceptedNanoseconds = Math.Min(
			elapsedNanoseconds,
			_maximumFrameDeltaNanoseconds);
		long droppedRealNanoseconds =
			elapsedNanoseconds - acceptedNanoseconds;
		long droppedSimulationNanoseconds =
			ScaleWithoutCarry(droppedRealNanoseconds, timeScale);

		bool wasPaused = _paused;
		int fixedSteps = 0;
		long scaledNanoseconds = 0;
		double simulationDeltaSeconds = 0.0;

		_frameIndex++;
		_advancing = true;

		try
		{
			if (wasPaused)
			{
				if (_singleStepPending)
				{
					_singleStepPending = false;

					RunFixedStep(isPaused: true);
					fixedSteps = 1;
				}
			}
			else
			{
				scaledNanoseconds = ApplyScale(
					acceptedNanoseconds,
					timeScale);

				long dueSteps = _accumulator.Accumulate(
					scaledNanoseconds,
					_settings.FixedUpdatesPerSecond);

				if (dueSteps > _settings.MaxFixedStepsPerFrame)
				{
					long excessSteps =
						dueSteps - _settings.MaxFixedStepsPerFrame;

					// Dropping whole steps keeps the sub-step
					// remainder, which belongs to the next step.
					AccumulateDroppedSteps(
						excessSteps,
						timeScale,
						ref droppedRealNanoseconds,
						ref droppedSimulationNanoseconds);

					dueSteps = _settings.MaxFixedStepsPerFrame;
				}

				long executedSteps = 0;

				while (executedSteps < dueSteps)
				{
					RunFixedStep(isPaused: false);
					executedSteps++;

					if (_paused)
					{
						// The step that paused the loop completes;
						// the rest of this frame's steps do not run.
						// Pause() already reset the accumulator, so
						// the remaining debt cannot burst later.
						AccumulateDroppedSteps(
							dueSteps - executedSteps,
							timeScale,
							ref droppedRealNanoseconds,
							ref droppedSimulationNanoseconds);

						break;
					}
				}

				fixedSteps = (int)executedSteps;
			}

			simulationDeltaSeconds =
				fixedSteps / (double)_settings.FixedUpdatesPerSecond;

			UpdateContext context = new(
				_frameIndex,
				NanosecondsToTimeSpan(acceptedNanoseconds),
				NanosecondsToTimeSpan(scaledNanoseconds),
				timeScale,
				fixedSteps,
				simulationDeltaSeconds,
				ToAlpha(_accumulator.Alpha),
				wasPaused);

			for (int index = 0; index < _updateCount; index++)
			{
				_updates[index](in context);
			}
		}
		finally
		{
			_advancing = false;
		}

		_totalDroppedNanoseconds = SaturatingAdd(
			_totalDroppedNanoseconds,
			droppedRealNanoseconds);

		_totalDroppedSimulationNanoseconds = SaturatingAdd(
			_totalDroppedSimulationNanoseconds,
			droppedSimulationNanoseconds);

		_lastFrame = new FrameTiming(
			_frameIndex,
			NanosecondsToTimeSpan(acceptedNanoseconds),
			NanosecondsToTimeSpan(scaledNanoseconds),
			NanosecondsToTimeSpan(droppedRealNanoseconds),
			NanosecondsToTimeSpan(droppedSimulationNanoseconds),
			fixedSteps,
			_totalFixedSteps,
			simulationDeltaSeconds,
			_accumulator.Alpha / _settings.FixedUpdatesPerSecond,
			ToAlpha(_accumulator.Alpha),
			timeScale,
			wasPaused);

		return _lastFrame;
	}

	/// <summary>
	/// Registers a callback that runs once per application frame.
	/// </summary>
	/// <param name="callback">The callback to run.</param>
	/// <remarks>
	/// Registration belongs to initialization: it may allocate and it
	/// preserves order. Calling it from inside
	/// <see cref="Advance"/> is rejected, because mutable callback
	/// lists are one source of frame-order nondeterminism.
	/// </remarks>
	public void RegisterUpdate(UpdateCallback callback)
	{
		ArgumentNullException.ThrowIfNull(callback);
		ThrowIfAdvancing();

		if (_updateCount == _updates.Length)
		{
			Array.Resize(ref _updates, _updates.Length * 2);
		}

		_updates[_updateCount++] = callback;
	}

	/// <summary>
	/// Removes a previously registered update callback.
	/// </summary>
	/// <param name="callback">
	/// The delegate passed to <see cref="RegisterUpdate"/>.
	/// </param>
	/// <returns>
	/// <c>true</c> when a registration was removed; otherwise
	/// <c>false</c>.
	/// </returns>
	/// <remarks>
	/// The first equal registration is removed and the remaining
	/// callbacks keep their relative order.
	/// </remarks>
	public bool UnregisterUpdate(UpdateCallback callback)
	{
		ArgumentNullException.ThrowIfNull(callback);
		ThrowIfAdvancing();

		for (int index = 0; index < _updateCount; index++)
		{
			if (!_updates[index].Equals(callback))
			{
				continue;
			}

			RemoveAt(_updates, index, _updateCount);
			_updateCount--;

			return true;
		}

		return false;
	}

	/// <summary>
	/// Registers a callback that runs once per fixed simulation step.
	/// </summary>
	/// <param name="callback">The callback to run.</param>
	public void RegisterFixedUpdate(FixedUpdateCallback callback)
	{
		ArgumentNullException.ThrowIfNull(callback);
		ThrowIfAdvancing();

		if (_fixedUpdateCount == _fixedUpdates.Length)
		{
			Array.Resize(
				ref _fixedUpdates,
				_fixedUpdates.Length * 2);
		}

		_fixedUpdates[_fixedUpdateCount++] = callback;
	}

	/// <summary>
	/// Removes a previously registered fixed-update callback.
	/// </summary>
	/// <param name="callback">
	/// The delegate passed to <see cref="RegisterFixedUpdate"/>.
	/// </param>
	/// <returns>
	/// <c>true</c> when a registration was removed; otherwise
	/// <c>false</c>.
	/// </returns>
	public bool UnregisterFixedUpdate(FixedUpdateCallback callback)
	{
		ArgumentNullException.ThrowIfNull(callback);
		ThrowIfAdvancing();

		for (int index = 0; index < _fixedUpdateCount; index++)
		{
			if (!_fixedUpdates[index].Equals(callback))
			{
				continue;
			}

			RemoveAt(_fixedUpdates, index, _fixedUpdateCount);
			_fixedUpdateCount--;

			return true;
		}

		return false;
	}

	/// <summary>
	/// Registers a callback that runs once per completed fixed step,
	/// after all of that step's work.
	/// </summary>
	/// <param name="callback">The callback to run.</param>
	/// <remarks>
	/// <para>
	/// The callback runs after the step's fixed-update callbacks and
	/// after every scheduled callback due on that step, so it sees the
	/// complete state the step committed. It runs once per executed
	/// step, including a step requested by <see cref="StepOnce"/>,
	/// and never for a step that did not run.
	/// </para>
	/// <para>
	/// Registration belongs to initialization and preserves order.
	/// Like the other callback lists it cannot be changed from inside
	/// <see cref="Advance"/>, because a frame whose callback set
	/// changes part-way through would be nondeterministic.
	/// </para>
	/// </remarks>
	public void RegisterPostFixedUpdate(PostFixedUpdateCallback callback)
	{
		ArgumentNullException.ThrowIfNull(callback);
		ThrowIfAdvancing();

		if (_postFixedUpdateCount == _postFixedUpdates.Length)
		{
			Array.Resize(
				ref _postFixedUpdates,
				_postFixedUpdates.Length * 2);
		}

		_postFixedUpdates[_postFixedUpdateCount++] = callback;
	}

	/// <summary>
	/// Removes a previously registered post-fixed-update callback.
	/// </summary>
	/// <param name="callback">
	/// The delegate passed to <see cref="RegisterPostFixedUpdate"/>.
	/// </param>
	/// <returns>
	/// <c>true</c> when a registration was removed; otherwise
	/// <c>false</c>.
	/// </returns>
	public bool UnregisterPostFixedUpdate(
		PostFixedUpdateCallback callback)
	{
		ArgumentNullException.ThrowIfNull(callback);
		ThrowIfAdvancing();

		for (int index = 0; index < _postFixedUpdateCount; index++)
		{
			if (!_postFixedUpdates[index].Equals(callback))
			{
				continue;
			}

			RemoveAt(
				_postFixedUpdates,
				index,
				_postFixedUpdateCount);

			_postFixedUpdateCount--;

			return true;
		}

		return false;
	}

	/// <summary>
	/// Pauses simulation progression.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Update callbacks keep running so input and menus remain
	/// responsive. Fixed updates and scheduled simulation work stop,
	/// and elapsed time is not accumulated. The partial fixed interval
	/// that had accumulated is discarded, and a pending
	/// <see cref="StepOnce"/> request is cleared.
	/// </para>
	/// <para>
	/// Called from inside a callback, the step that is executing
	/// finishes but the rest of the fixed steps planned for the frame
	/// do not run. Their time is reported in
	/// <see cref="FrameTiming.DroppedTime"/>, and the clock is
	/// resampled so a later <see cref="Resume"/> cannot replay it.
	/// </para>
	/// </remarks>
	public void Pause()
	{
		_paused = true;
		_singleStepPending = false;
		_accumulator.Reset();
		_scaleCarry = Int128.Zero;
		_lastTimestamp = _clock.Timestamp;
	}

	/// <summary>
	/// Resumes simulation progression.
	/// </summary>
	/// <remarks>
	/// The clock is resampled and accumulated time is discarded, so a
	/// long pause cannot turn into a catch-up burst and cannot shift
	/// the interpolation phase.
	/// </remarks>
	public void Resume()
	{
		_paused = false;
		_singleStepPending = false;
		_accumulator.Reset();
		_scaleCarry = Int128.Zero;
		_lastTimestamp = _clock.Timestamp;
	}

	/// <summary>
	/// Requests exactly one fixed simulation step while paused.
	/// </summary>
	/// <returns>
	/// <c>true</c> when the request is pending; <c>false</c> when the
	/// loop is running or a request was already pending.
	/// </returns>
	/// <remarks>
	/// The step runs during the next <see cref="Advance"/>, together
	/// with the scheduled work due on that step, and advances
	/// simulation time by exactly one fixed interval. Repeated calls
	/// before that <see cref="Advance"/> do not queue extra steps.
	/// </remarks>
	public bool StepOnce()
	{
		if (!_paused || _singleStepPending)
		{
			return false;
		}

		_singleStepPending = true;

		return true;
	}

	/// <summary>
	/// Discards accumulated time and any pending single-step request,
	/// and resamples the clock without changing the pause state.
	/// </summary>
	/// <remarks>
	/// Use this after an operation that legitimately consumed a long
	/// time outside the loop, such as loading a level, so the next
	/// frame does not observe the load as a hitch.
	/// </remarks>
	public void ResetTiming()
	{
		_accumulator.Reset();
		_scaleCarry = Int128.Zero;
		_singleStepPending = false;
		_lastTimestamp = _clock.Timestamp;
	}

	private void RunFixedStep(bool isPaused)
	{
		_totalFixedSteps++;

		// The scheduler treats this step as the registration time of
		// any work a fixed-update callback adds, so that work cannot
		// run on the step that is currently executing.
		Schedule.BeginStep(_totalFixedSteps);

		FixedUpdateContext context = new(
			_totalFixedSteps,
			_fixedDeltaSeconds,
			_fixedDelta,
			_totalFixedSteps / (double)_settings.FixedUpdatesPerSecond,
			isPaused);

		for (int index = 0; index < _fixedUpdateCount; index++)
		{
			_fixedUpdates[index](in context);
		}

		Schedule.Advance(_totalFixedSteps);

		// Post-fixed work observes the complete committed step, so
		// it runs after the linked scheduler callbacks that can
		// still mutate simulation state.
		if (_postFixedUpdateCount > 0)
		{
			PostFixedUpdateContext postContext = new(
				_totalFixedSteps,
				_totalFixedSteps / (double)_settings.FixedUpdatesPerSecond,
				isPaused);

			for (int index = 0; index < _postFixedUpdateCount; index++)
			{
				_postFixedUpdates[index](in postContext);
			}
		}
	}

	private long ApplyScale(
		long nanoseconds,
		in TimeScale scale)
	{
		if (scale.IsZero)
		{
			_scaleCarry = Int128.Zero;
			return 0;
		}

		// The remainder is carried in denominator units, so a scale
		// such as 1/3 loses no time over any number of frames.
		Int128 scaled =
			((Int128)nanoseconds * scale.Numerator) + _scaleCarry;
		Int128 result = scaled / scale.Denominator;

		_scaleCarry = scaled % scale.Denominator;

		return result > long.MaxValue
			? long.MaxValue
			: (long)result;
	}

	private static long ScaleWithoutCarry(
		long nanoseconds,
		in TimeScale scale)
	{
		if (nanoseconds <= 0 || scale.IsZero)
		{
			return 0;
		}

		Int128 scaled =
			(Int128)nanoseconds * scale.Numerator / scale.Denominator;

		return scaled > long.MaxValue
			? long.MaxValue
			: (long)scaled;
	}

	private static long SimulationNanosecondsToReal(
		long simulationNanoseconds,
		in TimeScale scale)
	{
		if (simulationNanoseconds <= 0 || scale.IsZero)
		{
			return 0;
		}

		Int128 real = (Int128)simulationNanoseconds *
			scale.Denominator / scale.Numerator;

		return real > long.MaxValue
			? long.MaxValue
			: (long)real;
	}

	private void AccumulateDroppedSteps(
		long steps,
		in TimeScale scale,
		ref long realNanoseconds,
		ref long simulationNanoseconds)
	{
		if (steps <= 0)
		{
			return;
		}

		Int128 simulation =
			((Int128)steps * FixedStepAccumulator.UnitsPerStep) /
			_settings.FixedUpdatesPerSecond;

		long dropped = simulation > long.MaxValue
			? long.MaxValue
			: (long)simulation;

		simulationNanoseconds = SaturatingAdd(
			simulationNanoseconds,
			dropped);

		realNanoseconds = SaturatingAdd(
			realNanoseconds,
			SimulationNanosecondsToReal(dropped, scale));
	}

	private void ThrowIfAdvancing()
	{
		if (_advancing)
		{
			throw new InvalidOperationException(
				"Update and fixed-update callbacks must be registered " +
				"or unregistered outside GameLoop.Advance(). " +
				"Registration belongs to initialization; changing the " +
				"callback list while a frame is running would make " +
				"frame order depend on when the change happened.");
		}
	}

	private long ToNanoseconds(long ticks)
	{
		// Each interval is converted as one rational number with the
		// sub-nanosecond remainder carried into the next conversion.
		// Truncating every interval independently would lose up to
		// one nanosecond per frame, which is enough for a coarse clock
		// such as 3 ticks per second to run a step short of a full
		// second after three ticks.
		Int128 scaled =
			((Int128)ticks * FixedStepAccumulator.UnitsPerStep) +
			_nanosecondCarry;
		Int128 nanoseconds = scaled / _clock.TicksPerSecond;

		_nanosecondCarry = scaled % _clock.TicksPerSecond;

		if (nanoseconds > long.MaxValue)
		{
			// Saturation is the only lossy case. The carry belongs to
			// an interval that cannot be represented anyway, and the
			// frame-delta clamp would discard it.
			_nanosecondCarry = 0;

			return long.MaxValue;
		}

		return (long)nanoseconds;
	}

	private static float ToAlpha(double alpha)
	{
		float value = (float)alpha;

		// A remainder such as 0.99999998 rounds to 1.0f, which would
		// break the documented [0, 1) contract. Report the nearest
		// representable value below one instead.
		return value >= 1.0f
			? MathF.BitDecrement(1.0f)
			: value;
	}

	private static TimeSpan NanosecondsToTimeSpan(long nanoseconds)
	{
		return TimeSpan.FromTicks(nanoseconds / 100);
	}

	private static long SaturatingAdd(long left, long right)
	{
		if (right > 0 && left > long.MaxValue - right)
		{
			return long.MaxValue;
		}

		return left + right;
	}

	private static void RemoveAt<T>(T[] callbacks, int index, int count)
	{
		for (int position = index; position < count - 1; position++)
		{
			callbacks[position] = callbacks[position + 1];
		}

		callbacks[count - 1] = default!;
	}
}
