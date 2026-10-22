# Volt.Rendering.Transforms

`Volt.Rendering.Transforms` turns fixed-step ECS transforms into
per-frame presentation transforms. It owns the interpolation
mathematics, the previous/current history, and the paused-render
policy. It never writes to the ECS.

## Why interpolation exists

A simulation advances in whole fixed steps, but the application draws
whenever it can. With a 60 Hz simulation and a 144 Hz display, some
frames complete no step and some complete one. Drawing the raw
component transform would therefore hold still for a frame and then
jump, even though the simulation is correct.

`GameLoop.InterpolationAlpha` reports the unspent fraction of the next
step in `[0, 1)`. Interpolating between the last two committed poses at
that fraction produces a smooth image with one step of latency, which
is the standard, deterministic trade.

## API

| Type | Responsibility |
| --- | --- |
| `TransformPose` | One translation, rotation, and scale value; lerp and model-matrix construction |
| `TransformHistory` | Previous and current pose per renderable ECS entity |
| `InterpolatedRenderExtractor` | Render instances whose model matrices come from history |
| `InterpolationWeight` | Chooses alpha or the committed pose for a displayed frame |
| `TransformMatrices` | Shared model-matrix convention (`Scale * Rotation * Translation`) |

```csharp
using Volt.Core.Timing;
using Volt.Rendering.Extraction;
using Volt.Rendering.Transforms;

TransformHistory history = new(world, initialCapacity: 4096);
InterpolatedRenderExtractor extractor = new(world);
InterpolationWeight weight = default;
RenderInstance[] instances = new RenderInstance[4096];

loop.RegisterPostFixedUpdate(
    (in PostFixedUpdateContext context) => history.Capture());

// Once per rendered frame, after Advance:
float displayed = weight.Resolve(
    loop.InterpolationAlpha,
    loop.LastFrame.FixedSteps > 0,
    loop.IsPaused || loop.EffectiveTimeScale.IsZero);

extractor.Extract(instances, history, displayed);
```

`TransformHistory` reads the same component set and renderable filter
as `EcsRenderExtractor`: `PositionComponent`, `RotationComponent`,
`ScaleComponent`, and a `MeshRendererComponent` with valid mesh and
material handles. `CountRequiredInstances`, resource handles, and
output order are unchanged, so swapping the interpolated extractor in
does not change draw-command construction.

## Pose interpolation

* Position and scale are interpolated component-wise with
  `Vector3.Lerp`.
* Rotation uses shortest-path spherical interpolation. A quaternion and
  its negation describe the same orientation; the interpolator flips
  one endpoint onto the short arc instead of spinning the long way.
* Very small angles use a normalized lerp, which is numerically stable
  where the trigonometric form is not.
* An endpoint whose quaternion is non-finite or has squared length at
  or below `1e-12` is treated as invalid: the other endpoint's
  normalized rotation is used, or identity if both are invalid. The
  repaired value exists only in the returned pose; the component is
  never modified.
* Alpha at or below `0` returns the previous pose exactly, and alpha at
  or above `1` returns the current pose exactly. A non-finite alpha is
  treated as `0`.

The model matrix uses `TransformMatrices.CreateModelMatrix`, the same
`Scale * Rotation * Translation` row-vector convention as the
un-interpolated path, so matrix and Vulkan upload conventions cannot
drift between the two extractors.

Scale is interpolated as authored. A path that moves uniformly from a
positive scale through zero to a negative scale produces a degenerate
matrix exactly at zero; that is a consequence of the authored values,
not a reason to substitute an epsilon scale.

## History alignment

`TransformHistory.Capture()` must run once per completed fixed step,
after all of that step's mutations. `GameLoop.RegisterPostFixedUpdate`
provides exactly that point: it runs after fixed updates and after
every scheduled callback due on the step, once per executed step,
including a `StepOnce` step, and never for a step that did not run.

The recommended order inside one fixed step is therefore:

1. `FixedUpdate` callbacks mutate components.
2. Scheduled simulation callbacks mutate components.
3. Post-fixed callbacks capture the committed pose.

Capturing earlier would store a pose that scheduled work had not yet
finished mutating. Capturing later, at render time, would blur two
steps together.

Each capture shifts current to previous and reads the new current
pose, so:

* A frame with several steps retains only the last two completed
  states.
* A frame with no steps changes nothing.
* Pausing after a step leaves previous and current as the two latest
  committed states.

History is keyed by the complete `Entity` value (allocator id, slot,
and generation). A recycled slot cannot inherit the previous
occupant's pose, and a handle from another world never matches. A
secondary slot index removes the old entry when a slot is reused so
the dictionary does not grow without bound.

## Entity lifecycle

| Situation | Behavior |
| --- | --- |
| Newly renderable entity | Previous equals current, so it does not sweep in from another pose |
| Becomes non-renderable | Its entry is pruned by the next `Capture()`; extraction stops producing it immediately |
| Reactivated entity | Its first valid capture starts a fresh identical pair, so it holds instead of sweeping from stale history |
| Destroyed entity | Pruned by the next `Capture()`; `Remove` releases the entry eagerly |
| Recycled slot | The previous occupant's entry is dropped on the next capture or snap |
| Archetype migration without loss of renderability | Capture reads the component columns by query, so the pair is preserved in the new archetype |
| Archetype migration that loses renderability | Same as any other non-renderable entity: pruned, then snapped on return |
| Teleport | Call `Snap(entity)` or `SnapAll()` after the move |
| Scene rebuild before the next capture | `SnapAll()` collapses current poses and also prunes stale entries and recycled-slot occupants |

A capture stamps every entry it observes. When it observed fewer
renderable entities than active entries, it walks the active history
and removes entries whose entity was destroyed, lost a required
component, had its renderer component removed, or carries an invalid
mesh or material handle. In steady state every entry was observed, so
this walk is skipped entirely. Cleanup happens whether or not the
application calls `Remove`, so a reactivated entity cannot interpolate
from a pose recorded before it disappeared: its first valid capture
sets previous and current to the same pose, and only a second captured
state starts a real pair. Unrelated entities keep their pairs, and the
prune never scans the whole world.

A change that happens and is reverted between two captures is not
observable: both captures were valid states, so the pair describes that
one step.

`Snap` collapses history onto the current component pose. Without it,
the next capture interpolates from the pre-teleport pose and sweeps the
entity across the world for one step. Use it for respawns, door
transitions, and level loads. `SnapAll` performs the same collapse for
every renderable entity and applies the full lifecycle cleanup in the
same linear pass, so a rebuild followed by `SnapAll` cannot leave a
destroyed or recycled occupant's history behind.

## Paused rendering

Ordinary interpolation at alpha `0` shows the previous pose. That is
correct while simulation advances, but wrong when it stops: the image
would jump back one step on the frame the pause is observed.

`InterpolationWeight.Resolve` implements the policy:

* While paused or at time scale zero, return `1`, displaying the
  committed pose.
* After resume, keep returning `1` until a fixed step completes.
* Otherwise return alpha clamped to `[0, 1]`.

The first new step moves the held pose into the previous slot, so the
transition from held to interpolated is continuous and never snaps
backward. The type is a mutable value used by the render loop; call
`Reset` after rebuilding the scene and its history.

## Threading and structural rules

Transform history, interpolation, and extraction are single-threaded,
like the world and the loop. Capture from the game thread. Do not
create, destroy, or migrate entities during capture or extraction: the
underlying query throws if the world's structural version changes
mid-iteration.

The ECS components remain authoritative. Extraction copies values into
caller-owned `RenderInstance` storage; it never writes components,
handles, or archetype membership. A render extraction performed before
and after interpolation sees identical component values.

## Allocation and complexity

With history sized once and storage supplied by the caller, steady
state allocates nothing: no per-frame arrays, delegates, LINQ, boxing,
or per-entity objects. The dictionaries allocate only when the
renderable entity count or their capacity grows, and `initialCapacity`
avoids that during startup. The prune uses a reusable key list, so it
allocates only while churn first grows that list to its high-water
size.

`Count` is the number of active entries. `Capacity` is the
`Dictionary` high-water capacity: it can stay above `Count` after a
peak and does not shrink during ordinary capture. `Clear()` removes
every entry and replaces both dictionaries and the prune scratch
list, releasing their storage, so `Capacity` is zero afterwards and
the next capture repopulates. That replacement allocation happens
only inside `Clear`, never in a frame.
Long-running churn therefore keeps memory proportional to the peak
simultaneous renderable count, not to the number of entities ever
seen.

* `Capture`: `O(n)` in steady state, where `n` is renderable
  entities; one dictionary probe per entity. After a disappearance it
  is `O(n + h)`, where `h` is active history, for the one capture that
  prunes.
* History lookup: average `O(1)` per entity.
* Pose interpolation: `O(1)` per entity, no trig for small angles.
* Interpolated extraction: one un-interpolated extraction plus one
  lookup and matrix per entity with history, `O(n)` overall.

Measured on a warmed-up 4096-entity world (BenchmarkDotNet, short run,
0 B allocated): capture ≈ 39 µs, pose interpolation ≈ 89 µs, and
interpolated extraction ≈ 296 µs per call. Small scenes (64 entities)
are ≈ 0.6 µs, 1.3 µs, and 4.4 µs respectively.

Churning 64 entities per capture adds about 29–33 µs to the whole
cycle, including the world destroy/create work, at either scene size.
Invalidating and revalidating them costs two captures plus about 3 µs
of pruning at 512 entities and about 7 µs at 4096. Every case
allocates 0 B after warmup.
