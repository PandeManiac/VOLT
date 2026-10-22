# Volt.Core.Timing

`Volt.Core.Timing` is the backend-independent game-loop foundation:
variable updates, fixed simulation steps, simulation-time scheduling,
simulation time scaling, pause, single-step, and interpolation alpha.

It has no dependency on SDL, Vulkan, `Volt.Ecs`, `Volt.Rendering`, or
`Volt.Jobs`. A console application can use it without opening a window.

## Update versus FixedUpdate

| Callback | Runs | Time source | Typical use |
| --- | --- | --- | --- |
| `UpdateCallback` | Once per `GameLoop.Advance`, including while paused | Real elapsed time | Input, camera, menus, presentation smoothing |
| `FixedUpdateCallback` | Zero or more times per `Advance` | Constant `1 / FixedUpdatesPerSecond` | Gameplay simulation, ECS systems |
| `ScheduledUpdateCallback` | At absolute simulation-step deadlines | Fixed steps | AI ticks, regeneration, waves |
| `PostFixedUpdateCallback` | Once per completed fixed step, after all of its work | Fixed steps | Capturing previous/current state for interpolation |

`Update` is not tied to a successful GPU presentation. A rejected,
skipped, or failed present does not suppress an update and does not
reset simulation time; the loop has no graphics dependency at all.

## Execution order

Each `GameLoop.Advance` performs exactly this sequence:

1. Sample the monotonic clock and compute the elapsed interval.
2. Clamp the interval to `GameLoopSettings.MaximumFrameDelta`, scale
   the accepted time by the effective `TimeScale`, and add the scaled
   interval to the fixed-step accumulator.
3. Execute zero or more fixed steps, up to
   `GameLoopSettings.MaxFixedStepsPerFrame`. For every step:
   fixed-update callbacks run in registration order, then the
   scheduled callbacks due on that step, then the post-fixed-update
   callbacks. The scheduler marks the step as current before the
   fixed updates run, so work they register cannot execute on that
   same step. The post-fixed phase runs after all of the step's
   mutations, which makes it the correct place to capture transform
   history for interpolation.
4. Execute the update callbacks once, in registration order.
5. Return a `FrameTiming` value and store it in `LastFrame`.

The application polls input before `Advance` and renders afterwards
using `InterpolationAlpha`. Input edges are therefore consumed once per
application frame; a frame that executes five fixed steps does not
replay a press, a click, or a restart.

```csharp
using Volt.Core.Timing;

GameLoop loop = new();

loop.RegisterUpdate(static (in UpdateContext context) =>
{
    // Read input and animate presentation with context.DeltaSeconds.
});

loop.RegisterFixedUpdate(static (in FixedUpdateContext context) =>
{
    // Advance gameplay with context.FixedDeltaSeconds.
});

loop.Schedule.EveryHz(10, static (in ScheduledUpdateContext context) =>
{
    // Run ten times per simulation second.
});

for (int frame = 0; frame < 600; frame++)
{
    // Poll input here, before Advance, in a real application.
    loop.Advance();

    Render(loop.InterpolationAlpha);
}

static void Render(float interpolationAlpha)
{
    // Draw using a value between the previous and current fixed state.
}
```

A real application replaces the bounded `for` above with its main
loop: `Advance` runs once per iteration, after polling input and before
recording a frame. The sample in `samples/Volt.VulkanMeshDemo` shows
the full integration.

Registration allocates; steady-state frames do not. Register during
initialization. `RegisterUpdate`, `UnregisterUpdate`,
`RegisterFixedUpdate`, and `UnregisterFixedUpdate` throw from inside a
callback, because a changing callback list would make frame order
depend on when the change happened.

## Fixed rate and exact time

The default fixed rate is 60 Hz. Supported rates are 1 to 1000
updates per second. One fixed step is exactly `1 / rate` simulation
seconds.

The accumulator stores elapsed time in integer units scaled by the
rate. A step is consumed only when the exact rational amount has
accumulated, so there is no repeated rounding of 16.666667 ms and no
long-run phase drift. `FixedUpdateContext.SimulationTimeSeconds` is
computed from the absolute step count, not by adding deltas.

`FixedUpdateContext.FixedDelta` is a `TimeSpan` and therefore an
approximation of 1/60 second (166,666 ticks). Integrate with
`FixedDeltaSeconds` when exactness matters.

## Simulation time scale

`GameLoop.TimeScale` scales the rate at which accepted real time
becomes simulation time. The configured fixed rate and the time scale
are independent: at `0.5x`, a 60 Hz simulation still executes whole
`1/60` simulation-second steps, it simply executes half as many of
them per real second. Changing the fixed rate would change gameplay
semantics; changing the scale does not.

```csharp
loop.TimeScale = 0.5f; // Half speed.
loop.TimeScale = 1.0f; // Normal speed (default).
loop.TimeScale = 2.0f; // Double speed, still bounded by the step cap.
loop.TimeScale = 0.0f; // Freeze simulation time; Update keeps running.
```

`TimeScale` is a value type created from a validated `float`:

* `0` is supported and freezes simulation time.
* Every positive value from `2^-103` to `TimeScale.Maximum` (`4.0x`) is
  supported. The lower bound guarantees that the exact reduced
  denominator fits in a positive signed 128-bit integer (at most
  `2^126`); a float just below it can reduce to `1 / 2^127` and
  overflow. The domain is contiguous, so smaller values are rejected
  even when one would individually fit.
* Negative, NaN, and infinite values throw `ArgumentOutOfRangeException`.
  Reverse simulation is not implemented.
* Values below the lower bound are rejected instead of being rounded or
  stored with a wrapped denominator.

`default(TimeScale)` is deliberately not a zero scale: it has a zero
numerator and a zero denominator, so it cannot participate in the
loop's rational arithmetic. Assigning it to `GameLoop.TimeScale` throws
`ArgumentException`; assign `TimeScale.Zero` to freeze time. An
accidental zero-initialized struct therefore fails at the assignment
instead of silently stopping the simulation.

### Precision

The scale is stored as an exact rational, not as a floating-point
multiplier. `0.5f` becomes exactly `1/2`, `0.25f` exactly `1/4`, and
`1.5f` exactly `3/2`. Each elapsed interval is multiplied by the
numerator in 128-bit integer arithmetic, divided by the denominator,
and the sub-nanosecond remainder is carried into the next interval.
Scaling therefore cannot drift by truncating a fraction on every
frame, and long runs keep the exact rational phase.

Changing the scale clears that carry, discarding at most one
nanosecond of scaled time, because a remainder denominated in the old
scale cannot be reinterpreted in the new one. Time already accepted
by the accumulator is never recomputed, dropped, or replayed by a
scale change.

### When a scale change takes effect

`Advance` captures the requested scale once, at the start of the
outer frame. A change made between frames applies to the next
`Advance`. A change made by an `Update`, `FixedUpdate`, scheduled, or
post-fixed callback applies to the next frame: the batch of fixed
steps already planned for the current frame keeps using the scale that
frame started with. No callback can observe two different rules for
the same frame's elapsed time.

### Interaction with catch-up limits

`MaximumFrameDelta` is applied to real time before scaling, so it
bounds the real interval a frame may accept. At `4.0x`, one accepted
250 ms interval represents one simulation second and is still limited
to `MaxFixedStepsPerFrame` whole steps; the excess is dropped, not
queued. `FrameTiming.DroppedTime` is real time; `FrameTiming.DroppedSimulationTime`
is the same discarded debt expressed in simulation time. At `2.0x`,
one dropped real second reports two dropped simulation seconds.

A high scale can never overflow the accumulator or produce a negative
simulation interval: the products are computed in `Int128`, the
accumulator saturates only at its documented integer limit, and the
fixed-step cap is checked after scaling.

## Timing source

`MonotonicClock.Shared` uses `Stopwatch.GetTimestamp` and
`Stopwatch.Frequency`. It is monotonic and unaffected by wall-clock,
time-zone, or daylight-saving changes.

The loop samples the clock when it is created, so the first frame sees
only the time since construction. `ResetTiming` discards accumulated
time and resamples the clock after a long operation such as loading a
level. A negative interval is treated as zero. Time beyond the
elapsed-time clamp is reported as `FrameTiming.DroppedTime`, not
simulated.

Each interval is converted to nanoseconds as one exact rational, and
the sub-nanosecond remainder is carried into the next conversion.
Truncating every interval independently would lose up to one
nanosecond per frame; a clock with 3 ticks per second, advanced by one
tick three times, would deliver 999,999,999 ns and run 59 steps
instead of 60. The carry makes many short intervals and one equal
longer interval produce the same result. It survives `ResetTiming`,
pause, resume, and a backward clock adjustment. Only saturating an
interval longer than `long.MaxValue` nanoseconds discards it, because
the frame-delta clamp would discard that time anyway.

Implement `IMonotonicClock` to inject controlled time in tests or in a
deterministic scripted mode.

## Scheduler

`GameLoop.Schedule` is a `SimulationScheduler`:

```csharp
ScheduledHandle ai = loop.Schedule.EveryHz(10, game.UpdateAI);
ScheduledHandle health = loop.Schedule.Every(
    TimeSpan.FromSeconds(2),
    game.RegenerateHealth);
ScheduledHandle wave = loop.Schedule.After(
    TimeSpan.FromSeconds(5),
    game.SpawnWave);

loop.Schedule.Cancel(ai);
```

* `EveryHz(rate)` requires `1 <= rate <= FixedUpdatesPerSecond`.
* `Every(interval)` requires a positive interval of at least one fixed
  step.
* `After(delay)` runs once; a zero delay means the next fixed step.

Every deadline is measured from the fixed step that was current at
registration. Registering `After(TimeSpan.FromSeconds(5))` after step
400 runs at step 700, not at step 401. `EveryHz(10)` registered after
step 120 first runs at step 126, and `Every(TimeSpan.FromSeconds(2))`
registered after step 120 first runs at step 240. A recurring schedule
stays on the rational cadence of its registration origin even when an
invocation is late, so a 7 Hz callback registered at step 100 runs on
steps 109, 118, 126, ... and still averages exactly 7 Hz. A
registration made from inside a fixed step is treated the same way:
the earliest possible deadline is the next step, even for
`After(TimeSpan.Zero)`.

Callbacks run synchronously on the thread that called `Advance`.
The scheduler never creates a task, timer, thread, or per-invocation
closure. `Cancel` is safe to call twice and safe from inside a
callback. A cancelled slot is reused with a new generation, so a stale
handle cannot cancel unrelated work.

### Frequency quantization

Rates that divide the fixed rate are uniform: 10 Hz on 60 Hz runs every
6 steps. A non-divisor such as 7 Hz uses absolute rational deadlines
from its registration origin, so registered before the first step it
runs on steps 9, 18, 26, 35, 43, 52, 60, ... and averages exactly
7 invocations per simulation second. It is never truncated to 8 steps
(7.5 Hz). Each invocation still runs on a whole fixed step, so the
observed cadence has bounded jitter of less than one step.

A requested rate above the fixed rate is rejected. A 60 Hz simulation
cannot produce 144 distinct invocations per second, and silently
rounding the request would misrepresent it.

Intervals are exact rationals of the fixed step. `Every` with 750 ms on
60 Hz is exactly 45 steps. An interval shorter than one fixed step is
rejected.

## Missed deadlines and overload

`GameLoopSettings` bounds catch-up in two independent ways:

* `MaximumFrameDelta` (default 250 ms) limits how much elapsed time a
  single frame may accept.
* `MaxFixedStepsPerFrame` (default 5) limits how many steps one frame
  may execute.

When more steps are due than the cap allows, the excess whole steps are
dropped. The sub-step remainder is kept. Under sustained overload the
game intentionally runs slower than wall time to stay responsive; the
engine does not claim real-time execution when the application cannot
keep up. `FrameTiming.DroppedTime`, `FrameTiming.FixedSteps`, and
`GameLoop.TotalDroppedTime` report this without allocating.

If simulation time jumps past one or more scheduled deadlines, a
recurring callback runs at most once on the step that observes the
jump. Its next deadline is computed from the original schedule, not
from `now + interval`, so the cadence does not drift. Skipped
occurrences are counted in `ScheduledUpdateContext.SkippedInvocations`
and `SimulationScheduler.SkippedInvocations`; they are never replayed
in a burst.

Work registered during dispatch is eligible on the next step. There is
no unbounded loop over overdue entries, and no callback runs twice on
one fixed step.

## Pause, zero scale, and single-step

`Pause()` and `TimeScale = 0` both stop simulation progression, but
they are different states:

| | `TimeScale = 0` | `Pause()` |
| --- | --- | --- |
| Fixed steps | Stop, because scaled elapsed time is zero | Stop |
| Scheduled simulation work | Stops with the steps | Stops |
| `Update` | Runs every frame | Runs every frame |
| `FrameTiming` real values | Continue to report real elapsed time | Report zero elapsed time |
| Fractional accumulator debt | Kept | Discarded |
| `IsPaused` | `false` | `true` |
| `StepOnce` | Accepted while paused only | Accepted |

Zero scale is a simulation-speed choice; pause is an explicit
application state. Changing the scale back from zero resumes from the
accumulated fraction without a catch-up burst, because no time was
accumulated while the scale was zero.

* `Pause` stops fixed steps and scheduled simulation work. `Update`
  continues, so input and menus stay responsive.
* The partial fixed interval is discarded, and elapsed time is not
  accumulated while paused.
* `Resume` resamples the clock and discards accumulated time, so a long
  pause cannot become a catch-up burst. It preserves the configured
  time scale: a paused half-speed loop resumes at `0.5x`.
* `StepOnce` requests exactly one fixed step. It returns `false` while
  running or when a request is already pending. The step runs on the
  next `Advance`, advances simulation time by one fixed interval, and
  also runs the scheduled work due on that step. The time scale does
  not apply: a single step is always one configured fixed interval,
  even at `0.5x`, `2.0x`, or `0.0x`.
* `Pause` called from inside a callback takes effect immediately: the
  fixed step that is executing completes, including its scheduled
  work, but no later step planned for that frame runs and `Update`
  still runs once. The unexecuted steps are reported in
  `FrameTiming.DroppedTime`. `FrameTiming.IsPaused` and
  `UpdateContext.IsPaused` describe the state at the start of the
  frame, so the pause appears there from the next frame.

## Simulation-time scheduling under scaling

Scheduled callbacks follow simulation time, never wall time. At
`0.5x`, a `EveryHz(10)` task averages five invocations per real
second; at `2.0x` it averages twenty. `After(TimeSpan.FromSeconds(5))`
takes ten real seconds to fire at half speed. Deadlines are still
expressed in absolute fixed steps from the registration origin, so a
scale change cannot move an existing deadline relative to the
simulation timeline, and skipped occurrences are still reported rather
than replayed.

## Interpolation

`InterpolationAlpha` is the unspent fraction of the next fixed step, in
`[0, 1)`, computed from the exact integer remainder after scaling. It
is `0` while paused. It describes simulation progress, not real time,
so it is unaffected by the time scale.

```csharp
TransformHistory history = new(world);
loop.RegisterPostFixedUpdate(
    (in PostFixedUpdateContext context) => history.Capture());

// While rendering:
float alpha = loop.InterpolationAlpha;
Vector3 displayed = Vector3.Lerp(previousPosition, currentPosition, alpha);
```

The simulation owns the authoritative current state. An application
keeps the previous fixed-step value in presentation storage and copies
the current value into it before each fixed step. `Volt.Rendering`
implements that storage as `TransformHistory`, captured in the
post-fixed phase so scheduled transform mutations are included, and
`InterpolatedRenderExtractor` applies the interpolated pose at render
time. Interpolation never writes back into authoritative ECS
components: the renderer reads a derived value.

Because ordinary interpolation at alpha `0` selects the previous pose,
a paused or zero-scaled frame must display the committed pose instead.
`Volt.Rendering.Transforms.InterpolationWeight` holds that policy: it
returns `1` while simulation is frozen and until the frame after
resume that completes a fixed step, so pausing and resuming never
snap the image backward.

See [Volt.Rendering.Transforms](../../Volt.Rendering/Transforms/README.md)
for the interpolation API, entity lifecycle rules, and allocation
contract.

## Threading

`Update`, `FixedUpdate`, and scheduled callbacks run synchronously on
the game thread. The loop and scheduler are not thread-safe. No
background worker implicitly reads input or touches graphics state.

`Advance` is not reentrant. Calling it from an update, fixed-update, or
scheduled callback throws `InvalidOperationException` before any clock
or frame state is touched, so the rejected attempt cannot corrupt the
outer frame. The loop remains usable after the caller handles the
exception.

A future game may use `Volt.Jobs.JobScheduler` from inside a fixed
update for CPU parallelism, but this milestone does not integrate the
two automatically. `JobScheduler` decides *how* work is parallelized;
`SimulationScheduler` decides *when* simulation work runs.

## Why not Volt.Jobs?

`Volt.Jobs.JobScheduler` runs work on the .NET ThreadPool and manages
CPU parallelism. It has no notion of frames, simulation ticks,
deadlines, pause, or interpolation. The timing loop is the clock the
game runs on; the job scheduler is a tool that clock can use.

## Precision and limits

* Time is exact in integer rational units, but `TimeSpan` reporting is
  truncated to 100 ns.
* Callbacks execute on fixed-step boundaries, so their wall-clock
  execution instant is not exact.
* A rate above the fixed rate and an interval below one fixed step are
  rejected rather than approximated.
* A time scale is exact in rational form, but scaled nanoseconds are
  truncated per interval with the remainder carried; the carry limits
  the error to less than one nanosecond of scaled time at any instant.
* Changing the time scale discards the pending sub-nanosecond carry,
  at most one nanosecond.
* `MaximumFrameDelta` drops time; that is intentional and is not a
  drift bug. Dropped time is reported in both real and simulation
  units when scaling is active.
* A pause requested mid-frame reports its unexecuted fixed steps as
  dropped time; the sub-step fraction is discarded silently, just as
  at an ordinary pause.

## Zero-allocation scope

After registration and warmup, `Advance`, fixed stepping, idle
scheduling, due callbacks, post-fixed callbacks, time scaling,
pause/resume, and single-step allocate nothing. Tests assert
`GC.GetAllocatedBytesForCurrentThread` is unchanged over 10,000 frames
for each case, including every supported scale and the scaled clock
conversion. User callback bodies can of course allocate; that cost
belongs to the application, not to the loop.
