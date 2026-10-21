# VOLT Vulkan Mesh Demo

This sample renders three distinct meshes side by side in one Vulkan
frame using VOLT's uploaded mesh buffers and mesh draw-command helper.

All three meshes are uploaded once and drawn from one graphics
pipeline in one render pass, with four draw calls in total:

| Mesh | Geometry | Draw path | Draw calls |
| --- | --- | --- | --- |
| Left | Triangle | Non-indexed `vkCmdDraw` for the complete mesh | 1 |
| Center | Quad | Indexed `vkCmdDrawIndexed` over 16-bit indices, split into two index ranges | 2 |
| Right | Hexagon | Indexed `vkCmdDrawIndexed` for the complete mesh over 32-bit indices | 1 |

The center quad is the range-drawing demonstration. Its two
constituent triangles come from one uploaded 16-bit indexed mesh;
`VulkanMeshDrawCommands.RecordRange` records them as two partial
ranges of the same shared index buffer (indices `0..2` and then
`3..5`). No additional mesh and no per-frame buffer upload is created
for the split. The complete triangle and hexagon keep using the
whole-mesh `VulkanMeshDrawCommands.Record` path, so the sample covers
both complete-mesh and range draws of the same helper.

The pipeline uses conventional position and color vertex input at
binding zero, not shader-based vertex pulling. Each draw pushes one
64-byte model-view-projection matrix, so the same pipeline renders
clip-space shapes for the gallery and perspective geometry for the
camera scene. The shader transforms local-space positions with
`mvp * vec4(position, 1.0)`.

## Scenes

`--scene gallery` is the default and renders the three separated meshes
described above. `--scene ecs-scene` is the opt-in ECS-driven scene
described in [ECS scene](#ecs-scene). `--scene target-range` is the
playable first-person round described in
[Target range](#target-range). `--scene depth-occlusion` uploads an
overlapping scene instead, once per run, through the same mesh buffers:

| Mesh | Geometry | Depth | Index format | Draws |
| --- | --- | --- | --- | --- |
| Near | Red triangle | `0.2` | none | 1 `vkCmdDraw` |
| Far | Blue quad | `0.8` | `uint16` | 2 partial ranges |
| Reference | Green hexagon | `0.5` | `uint32` | 1 complete mesh |

The draw order is near, far, then reference. The far blue quad is
recorded after the near red triangle even though it is farther away,
so ordering alone would make the quad cover the overlap. With depth
testing and depth writes enabled, the earlier red fragments remain
visible because `0.8 < 0.2` is false with the `Less` comparison.

Both overlapping meshes use one depth value for every vertex, so the
result never depends on interpolated depth or floating-point equality.
The quad extends past the triangle's right edge, leaving blue pixels
that are visible in both modes.

The vertex shader forwards the authored Z coordinate as clip-space
depth with `W = 1.0`, so authored values are already Vulkan's 0..1
depth range. The gallery authors `0.0` for every vertex, which keeps
its image unchanged.

### Negative control

`--disable-depth-test` is available only with
`--scene depth-occlusion`. It disables depth testing and depth writes
for the sample pipeline without changing the draw order, geometry,
colours, mesh uploads, or depth attachment. The overlap pixel turns
blue because the later quad is no longer rejected. It is rejected for
the gallery and camera scenes.

## Camera transforms

`--scene camera-transforms` uploads local-space meshes and draws them
through model, view, and projection matrices. Three uploaded meshes
produce five draw calls:

| Draw | Mesh | Model transform | Draw path |
| --- | --- | --- | --- |
| Triangle A | Local triangle | Scale `0.45`, translate `(-0.75, 0.35, 0)` | 1 `vkCmdDraw` |
| Triangle B | Same triangle buffer | Scale `0.40`, rotate Z 25°, rotate Y 35°, translate `(1.05, 0.50, -0.35)` | 1 `vkCmdDraw` |
| Quad triangle A | Local quad | Scale `(1.35, 0.75, 1)`, translate `(0, 0, -1.1)` | First index range |
| Quad triangle B | Same quad buffer | Same model matrix | Second index range |
| Hexagon | Local hexagon | Scale `0.30`, translate `(-1.60, -0.85, -0.30)` | Complete mesh |

Both triangle draws reference `MeshIndex 0`, so the sample uploads one
triangle buffer and reuses it with two different model matrices. The
quad's two index ranges share one model matrix, so the split draw still
covers a single transformed quad. No vertices are pre-transformed and
no vertex buffer is uploaded per frame.

The model matrices are built by the engine's
`TransformMatrices.CreateModelMatrix` from ordinary
`PositionComponent`, `RotationComponent`, and `ScaleComponent` values,
in `scale * rotation * translation` order. The compound Z/Y rotation of
the second triangle is expressed as one normalized quaternion; it is
equivalent to the previous `CreateRotationZ * CreateRotationY` product
up to floating-point rounding.

The camera starts at `(0, 0, 3)` looking at the world origin with up
vector `(0, 1, 0)`, which is the pose the scene had before it became
movable. The projection uses a 60° vertical field of view, the current
framebuffer aspect ratio, and near/far planes `0.1` and `100`. The near
plane maps to depth `0` and the far plane to depth `1`, which matches
Vulkan's 0..1 clip-space depth range. The projected Y row is negated so
world-space +Y appears upwards in Vulkan's downward viewport.

### Interactive camera

`--scene camera-transforms` without `--camera-script` is an interactive
free-fly camera. Movement follows the view direction, so forward
includes pitch; strafing stays horizontal, and Space/Left Ctrl move
along world +Y and -Y.

| Input | Action |
| --- | --- |
| `W` / `S` | Forward / backward |
| `A` / `D` | Strafe left / right |
| `Space` / `Left Ctrl` | Move up / down |
| `Left Shift` | Sprint (3x speed) |
| Mouse X / Y | Yaw / pitch |
| Left click | Enable relative mouse mode |
| `Escape` | Release relative mouse mode |

Diagonal movement is normalized, so it is never faster than a single
axis. Movement integrates elapsed `Stopwatch` time and clamps a single
step to `0.1` seconds, so returning from a long pause cannot teleport
the camera. Pitch is clamped just short of vertical; yaw is continuous
and not wrapped.

Relative mouse mode is never enabled at startup: a left click in the
focused window enables it once SDL also reports that window as owning
the pointer, and the first delta after capture is discarded. A click
SDL cannot attribute to the sample window never captures; a null
mouse focus (no window owns the pointer) is treated that way too.
Escape, focus loss, another window taking the pointer, or shutdown
disables it again. A release is only recorded after SDL confirms the
native state: if `SDL_SetWindowRelativeMouseMode` fails, the camera
stops looking, the release stays pending, and it is retried on the next
update and at shutdown instead of silently reporting success. Input is
read from the engine's per-window snapshot: `Window.PollEvents` starts
an input frame for every VOLT window, routes each native event to its
own window, and completes the frames on the main thread, so the
existing event pump, resize handling, and close handling are unchanged
and no global hooks or synthetic events are used. A headless run never
clicks, so it never captures the mouse.

The camera controller has application lifetime. `MeshRenderer` queries
it for the view matrix on every recorded frame and composes
`model * view * projection` through the shared transform utility, so
movement is visible without recreating the renderer, and a swapchain
recreation leaves position, yaw, and pitch untouched. The view matrix
is produced from the controller's position and quaternion through
`CameraComponent.CreateViewMatrix`, so the free camera and an ECS
camera with the same pose agree. The projection still comes from the
current extent, so the new aspect ratio applies immediately after a
resize.

### Scripted camera verification

`--camera-script` replaces interactive input with a deterministic flight
and is accepted only with `--scene camera-transforms` and a bounded
`--frames` run of at least 122 frames:

```bash
dotnet run \
  --project samples/Volt.VulkanMeshDemo \
  -c Release -- \
  --validation \
  --frames 600 \
  --scene camera-transforms \
  --camera-script
```

The script advances exactly once per accepted presented frame, using a
fixed `1/60` second step and the same controller update methods as
interactive input. It holds the initial pose for 100 accepted frames,
flies forward 0.5 world units while turning 8° right over 12 frames,
then holds the final pose for the rest of the run. Because the camera
state is simulated, the result is identical at any rendering speed, and
presentation retries or swapchain recreation cannot add extra movement.
It never enables mouse capture or injects events.

Checkpoints are written only from the accepted-presentation path, with
invariant numbers:

```
Camera initial pose: position=(0.000, 0.000, 3.000) yaw=0.00deg pitch=0.00deg at accepted frame 1.
Camera script started: position=(0.000, 0.000, 2.958) yaw=-0.67deg pitch=0.00deg at accepted frame 101.
Camera final pose reached: position=(0.038, 0.000, 2.502) yaw=-8.00deg pitch=0.00deg at accepted frame 122; holding.
Camera final pose held for 479 accepted frames.
```

The final-pose checkpoint is emitted after the pose has already been
presented for 10 frames. A resize run also reports
`Camera final pose stable after recreation: ... after 30 accepted frames
in generation 2.`, which lets a screenshot wait for a stable post-resize
frame.

### Matrix convention

`System.Numerics.Matrix4x4` uses row-vector transforms, so the sample
composes `model * view * projection` and multiplies positions on the
left. The sixteen floats are pushed in their natural memory order and
GLSL reads them as a column-major `mat4`, which transposes the matrix
implicitly. The shader therefore multiplies `mvp * vec4(position, 1.0)`
and the CPU does not transpose. `CameraTransformsTests` checks this by
reproducing the shader's column-major multiply from the packed bytes
and comparing it to the CPU row-vector product for an asymmetric
transform.

The gallery and depth-occlusion scenes keep their previous behavior
with identity model and view matrices and a projection that reproduces
the old shader exactly: horizontal scale `1 / max(aspect, 1)`, vertical
scale `min(aspect, 1)`, and Y inversion. Their geometry, draw order,
depth values, and screenshots are unchanged.

The projection is recomputed from the renderer's current extent on
every frame, so a recreated swapchain cannot reuse a stale aspect
ratio. The uploaded meshes are not re-uploaded when the window is
resized.

## ECS scene

`--scene ecs-scene` is the opt-in ECS-driven scene. Instead of a static
draw plan it builds a real `Volt.Ecs.World` whose entities describe the
same perspective arrangement as the fixed camera scene, and derives
every draw from extracted render records:

| Entity | Components | Mesh handle | Draws |
| --- | --- | --- | --- |
| Triangle A | position, rotation, scale, mesh renderer | `(0, 1)`, shared | 1 `vkCmdDraw` |
| Triangle B | position, rotation, scale, mesh renderer | `(0, 1)`, shared | 1 `vkCmdDraw` |
| Quad | position, rotation, scale, mesh renderer | `(1, 1)` | 2 partial index ranges |
| Hexagon | position, rotation, scale, mesh renderer | `(2, 1)` | 1 complete mesh |
| Camera | camera, position, rotation | none | view and projection |

Both triangle entities carry the same complete `MeshHandle`, so one
triangle buffer is uploaded and drawn twice. Four renderable entities
still produce five draw commands, because the 16-bit indexed quad keeps
its two partial index ranges. `RenderInstance` records contain only the
source `Entity`, the mesh and material handles, and a world-space model
matrix; they never contain Vulkan objects.

### Extraction

`EcsRenderExtractor` caches one `ComponentQuery` for position, rotation,
scale, and mesh renderer, then walks the matching archetype chunks
through `WorldQuery`. For each row it reads the chunk's entity span and
the parallel component spans directly, builds the model matrix with
`TransformMatrices.CreateModelMatrix`, and writes one record. Entities
missing any of the four components never appear. The world is not
modified by extraction, and extraction performs no heap allocation in
steady state.

The caller owns the output storage. `Extract(Span<RenderInstance>)`
returns a `RenderExtractionResult` with the written and required
counts; when the span is too small it writes nothing at all and reports
the capacity a complete extraction needs, and
`CountRequiredInstances()` is the preflight form. The scene treats a
short count as a defect and throws rather than rendering a truncated
frame. Extraction is single-threaded: a structural change invalidates
the active query enumeration, so the sample performs its scripted
component writes only after a frame was accepted, when no extraction or
command recording is in progress.

The output order is the world's chunk and row order, which is an ECS
implementation detail rather than a public contract. The scene never
assumes it: tests look records up by `Entity`, and the four-object scene
needs no sorting because depth testing decides visibility. A
production renderer would still want explicit keys before relying on
submission order.

### Handles and materials

The records preserve complete generational handles. The extractor omits
an entity whose mesh or material handle has a zero generation, and
leaves the entity itself untouched; it never treats an index alone as
proof the resource exists, and it never resolves or allocates GPU
resources. Resolution happens in `EcsMeshResolver`, a sample-local
table that matches the whole handle (index and generation) to one
uploaded buffer and its geometry. An unknown index or a stale
generation fails at record time instead of drawing an unbound mesh.
`EcsMaterialPalette` does the same for the single `MaterialHandle
(0, 1)` used by every entity.

The shader still colours vertices directly, so the material handle only
proves that a complete handle survives extraction and validation. It
does not select a pipeline, shader, or texture, and no material or GPU
resource registry was added.

### Camera entity

The camera entity carries a `CameraComponent` with a 60° vertical field
of view, near plane `0.1`, and far plane `100` - the sample's far plane,
not `CameraComponent.Default`'s `1000` - plus `PositionComponent
(0, 0, 3)` and an identity `RotationComponent`. It deliberately has no
`ScaleComponent`, because camera scale is not part of a view matrix.

`CameraEntityMatrices.CreateMatrices` reads those components on every
recorded frame and produces the same view and projection the fixed
camera scene uses, with `ViewportYOrientation.YDown`. The projection
takes the current extent's aspect ratio, so a recreated swapchain
updates it without touching the ECS world, and a changed position or
rotation is visible on the next frame with no GPU resource recreated.

### Scripted ECS verification

`--ecs-script` mutates the world deterministically and is accepted only
with `--scene ecs-scene` and a bounded `--frames` run of at least 210
frames:

```bash
dotnet run \
  --project samples/Volt.VulkanMeshDemo \
  -c Release -- \
  --validation \
  --frames 600 \
  --scene ecs-scene \
  --ecs-script
```

The script advances once per accepted presented frame, so presentation
retries and swapchain recreation cannot mutate the world extra times.
It holds the initial scene for 200 accepted frames, then moves triangle
A from `x = -0.75` to `x = 0.15` and the camera entity from `x = 0` to
`x = 0.3` in place, and holds that state for the rest of the run. The
mutation happens only after the frame was accepted, never while
commands are being recorded, and it never adds or removes entities.
Repeating the mutation sets the same values, so the script stays
deterministic.

```
Ecs initial state: camera=(0.000, 0.000, 3.000) triangleA=(-0.750, 0.350, 0.000) at accepted frame 1.
Ecs change applied: camera=(0.300, 0.000, 3.000) triangleA=(0.150, 0.350, 0.000) at accepted frame 200.
Ecs changed state reached: camera=(0.300, 0.000, 3.000) triangleA=(0.150, 0.350, 0.000) at accepted frame 210; holding.
Ecs changed state held for 391 accepted frames.
```

Every mesh entity keeps its identity and handles across the mutation, so
no buffer is uploaded again; only the extracted model matrices change.
Unrelated entities keep their own component values.

### Headless ECS verification

The static scene is verified on the GPU with analytically derived
probes:

```bash
./tools/test-vulkan-headless.sh \
  --project samples/Volt.VulkanMeshDemo \
  --frames 600 --validation --screenshots \
  --sample-arg=--scene --sample-arg ecs-scene \
  --expect-pixel-class 484,296,red \
  --expect-pixel-class 640,360,blue \
  --expect-pixel-class 835,267,red
```

At 1280x720 the ECS camera reproduces the fixed scene's image: triangle
A spans `x = 432..536, y = 236..328`, the quad `x = 435..845, y =
246..474`, triangle B `x = 820..864, y = 228..312`, and the hexagon is
centred near `(338, 520)`. The red probes prove both triangle entities
draw the one shared mesh, and the blue probe proves the quad is behind
them.

The scripted scene waits for the changed-state checkpoint, then checks
that the moved triangle and the moved camera affected the right pixels:

```bash
./tools/test-vulkan-headless.sh \
  --project samples/Volt.VulkanMeshDemo \
  --frames 600 --validation --screenshots \
  --sample-arg=--scene --sample-arg ecs-scene \
  --sample-arg=--ecs-script \
  --screenshot-after-line "Ecs changed state reached" \
  --expect-pixel-class 609,287,red \
  --expect-pixel-class 500,310,blue \
  --expect-pixel-class 420,360,blue
```

Triangle A's centre moves from `(484, 287)` to `(609, 287)`, so
`609,287` becomes red while `500,310`, which triangle A used to cover,
turns blue. The camera component change moves every other object `0.3`
world units left: the quad's left edge moves from `x = 435` to
`x = 389` and its right edge from `x = 845` to `x = 800`, so `420,360`
turns from background to blue, and triangle B's previous right-hand
area becomes background again. `640,360` and `594,360` are both inside
the quad's centre region before and after, which is why `420,360` is
the sharper camera probe. Comparing the two screenshots leaves about
45,000 differing pixels, and the red content moves from
`x = 430..866` to `x = 554..814`: triangle A moved right, triangle B
moved left.

## Target range

`--scene target-range` is a small, complete first-person round. It
reuses the ECS extraction path, the uploaded-mesh pipeline, the depth
attachment, and `CameraController`, and adds gameplay around them. It
is opt-in; the default scene remains `gallery`.

```bash
dotnet run \
  --project samples/Volt.VulkanMeshDemo \
  -c Release -- \
  --validation \
  --scene target-range
```

Without `--frames` the window stays open until it is closed. Closing
the window releases relative mouse mode and exits with status zero.

### Controls

| Input | Action |
| --- | --- |
| `W` / `S` | Fly forward / backward along the view direction |
| `A` / `D` | Strafe left / right |
| Mouse X / Y | Yaw / pitch |
| Left click | Capture the mouse; every later click fires |
| `Space` / `Left Ctrl` | Fly up / down along world Y |
| `Left Shift` | Sprint (3x speed) |
| `R` | Restart the round |
| `Escape` | Release the mouse |

Capture rules come from the existing `CameraCaptureController`:

- The click that captures the mouse never fires. It is consumed by the
  capture transition, and the captured state only exists from the next
  sampled frame.
- A fresh left press while captured fires exactly once. Holding the
  button does not repeat, and releasing then pressing again fires again.
- The press edge, not the held state, is what the sample consumes, so a
  short click that begins and ends between two polls still captures or
  fires exactly once. The platform reports one edge per physical press,
  so a single click cannot fire twice and a held button cannot fire
  again on a later frame.
- Escape is level-triggered: the first held sample releases the
  pointer, and for as long as the key stays down no click can recapture
  it or fire. Losing keyboard focus, another window taking the pointer,
  or a failed native release also suppress firing for that frame. A
  release that SDL did not confirm stays pending: the view stays
  frozen, the pending state is retried, and no shot is accepted while
  it lasts.
- `R` is a one-shot edge taken from the engine snapshot. The platform
  clears held state and suppresses edges for an unfocused window, so
  holding a key or button in another application and then focusing this
  one cannot fabricate a press.
- A frame in which keyboard focus was lost is cleared by the platform
  and flagged with `FocusLostDuringFrame`. Capture is released on that
  flag even when focus returns before the frame is read, and nothing is
  captured, fired, restarted, or rotated in that frame; a new click is
  required to capture again.

The window title reports score and progress, for example
`VOLT Target Range - 100/500 - 4 targets remaining`, and changes to
`VOLT Target Range - COMPLETE 500/500 - press R to restart` when the
last target is hit. The title is only rebuilt when the score or round
state changes, never per frame. Console messages report each hit, each
miss, round completion, and restart.

### Arena and ECS layout

The world contains seven entities: five targets, one floor, and one
camera. The six renderable entities go through the same
`EcsRenderExtractor` as the ECS scene, so the draw list is derived from
the world every frame rather than from a static plan.

| Target | World position | Radius | Distance from the initial pose |
| --- | --- | --- | --- |
| 1 | `(-3.0, 1.7, 0.0)` | `0.55` | 8.0 |
| 2 | `(3.2, 1.7, -1.0)` | `0.55` | 9.0 |
| 3 | `(0.0, 1.6, -6.0)` | `0.60` | 14.0 |
| 4 | `(-2.0, 3.2, -8.0)` | `0.65` | 16.1 |
| 5 | `(2.4, 0.6, -11.0)` | `0.70` | 19.1 |

Each target is an entity with `PositionComponent`,
`RotationComponent`, `ScaleComponent`, `MeshRendererComponent`, and one
small unmanaged gameplay component, `TargetStateComponent`, whose byte
is `Live` or `Completed`. There is no separate transform cache: hit
detection reads the position and scale components, and rendering reads
the extracted model matrices built from the same components.

The camera is an entity with `CameraComponent`, `PositionComponent`,
and `RotationComponent`. It starts at `(0, 1.7, 8)` looking down `-Z`
with a 60-degree vertical field of view and a `0.1..100` clip range. All
five targets are inside that first view; the nearest is centred about
`35` pixels left of the crosshair, so a target can be acquired
immediately after capturing the mouse. `TargetRangeController` copies
the active `CameraController` pose into these components before every
decision, and the renderer builds the view and projection from the
entity with `CameraEntityMatrices`, so the shot and the image always
use the same pose and the same live extent.

The floor is a plane at `y = 0` covering `x = -20..20, z = -30..12`. It
is the only other geometry; nothing in the range blocks a shot.

### Hit detection

A shot casts the camera's forward ray against the targets' world-space
spheres:

- The origin is the camera entity's position and the direction is its
  normalized forward vector, `rotation * (0, 0, -1)`.
- The target mesh is a faceted unit sphere whose vertices reach radius
  `1`, so the collision radius is the target's current
  `ScaleComponent` value. The sphere tightly bounds the low-poly mesh
  rather than coinciding with its polygon surface: the flat facets sit
  a few percent of the radius inside it. With 12 longitudes and 6
  latitude bands the facet corners are 15 degrees apart, so the
  silhouette chord sags `1 - cos(15 deg) = 0.034` of the radius and a
  facet centre is up to twice that. A ray crossing the silhouette can
  therefore be accepted slightly outside the drawn polygon edge, but
  there is no aiming assist beyond that bounding radius.
- The quadratic is solved against each live sphere. A positive entry
  distance is a hit from outside; when the camera is inside a sphere
  the positive exit distance is the hit instead. Non-positive
  distances are rejected, which excludes targets behind the camera.
- The nearest valid intersection wins, so overlapping targets resolve
  deterministically. Distances beyond `40` world units are rejected.
- Completed targets are skipped, so they cannot score again even
  though their geometry is still present.
- A non-finite origin or direction, a zero-length direction, and a
  non-positive or non-finite maximum distance all report an explicit
  miss instead of a NaN distance or an exception.

Walls do not exist in this range, so no occlusion test is performed.
The floor cannot block a shot because every target sits above it and
the camera cannot go below it without leaving the arena.

### Hit reaction, score, and restart

A hit mutates the target's components in place:

- `MeshRendererComponent.Mesh` switches to a second unit sphere that was
  uploaded once at startup and is coloured blue.
- `ScaleComponent` shrinks to 55% of the authored radius, so the target
  visibly contracts about its own centre.
- `TargetStateComponent` becomes `Completed`.

No vertex buffer is uploaded, no pipeline is created, and no entity is
added or removed, so the next extraction and the next recorded frame
show the change. The hit volume then follows the shrunken scale,
although completed targets are ignored anyway.

Each target awards `100` points once; the maximum is `500`. A miss
changes nothing. Hitting the last target enters the completed state:
all five targets are blue and small, the title reports completion, and
the console prints the final score. Restarting restores every target's
position, rotation, scale, mesh handle, and live state, resets score,
shots, and round state, and returns the camera to `(0, 1.7, 8)` with
yaw and pitch zero via `CameraController.SetPose`. Entity identities
and archetypes survive, so no GPU or ECS structure is rebuilt.

### Reticle

The reticle is a white plus made of two rectangles in one pre-uploaded
mesh. It is drawn inside the same render pass, with the same pipeline
and vertex layout as every other mesh, after the extracted instances.
Its transform is a pure clip-space scale with no view or projection:

- The camera's forward vector always projects to the clip-space origin,
  so a mesh centred there is centred on the shot ray at every camera
  pose and every framebuffer size. No viewport uniform is involved.
- The scale is derived from the current extent each frame, keeping the
  plus about 32 pixels across at 1280x720 and 24 pixels at 960x540, so
  a resize cannot leave it off centre or stretched.
- The authored clip-space depth is zero. The sample's depth test is
  less-than against a cleared value of one, so the reticle always
  passes and is visible over targets without disabling depth testing or
  changing any other draw's depth state. It writes depth only over its
  own few pixels, after every scene draw.
- It is not an ECS entity, so extraction and the aiming candidates
  cannot include it.

### Scripted verification

`--range-script` drives the same controller and camera a player drives,
from value-type commands, and is accepted only with
`--scene target-range` and a bounded run of at least `190` frames:

```bash
dotnet run \
  --project samples/Volt.VulkanMeshDemo \
  -c Release -- \
  --validation \
  --frames 600 \
  --scene target-range \
  --range-script
```

The script advances once per accepted presented frame. It never sends
SDL events, never writes the score, and never marks a target complete:
all of those effects come from the gameplay path. It throws if an
expected outcome does not hold, so a regression fails the run instead
of only printing different numbers.

| Accepted frames | Step |
| --- | --- |
| 1 | Present the initial state. |
| 2-9 | Idle frames. |
| 10-11 | Aim 45 degrees up and fire; the shot must miss, and the score must stay zero. |
| 12 | Confirm the score is still zero. |
| 13-14 | Aim at target 1 and fire; exactly one hit is scored. |
| 15 | Confirm the target's component and mesh handle changed. |
| 16 | Fire again at the completed target; nothing is scored. |
| 17 | Confirm the round still has one completed target. |
| 18-35 | Hold `W` for 16 fixed one-sixtieth-second steps; the ECS camera must move. |
| 37 | Restart; the round had a positive score. |
| 38 | Confirm score, round state, target components, and the camera pose are reset. |
| 39-48 | Aim and fire at all five live targets. |
| 49 | Confirm a completed round with a score of 500. |
| 50-167 | Hold the completed round for 120 accepted frames. |

Console checkpoints from a passing run:

```
Target range script: initial state presented at accepted frame 1: score=0, completed=0, remaining=5, roundComplete=False, camera=(0.000, 1.700, 8.000) forward=(0.000, 0.000, -1.000).
Target range script: miss verified after accepted frame 11: score unchanged at 0, remaining=5; state presented from frame 12.
Target range script: hit verified after accepted frame 14: score=100, completed=1, remaining=4, roundComplete=False, camera=(0.000, 1.700, 8.000) forward=(-0.351, 0.000, -0.936); target visuals updated in ECS and presented from frame 15.
Target range: miss; score 100/500, 4 targets remaining.
Target range script: duplicate shot verified after accepted frame 16: score unchanged at 100.
Target range script: movement verified after accepted frame 35: camera entity moved to (-0.234, 1.700, 7.376) forward=(-0.351, 0.000, -0.936).
Target range script: restart requested after accepted frame 37: score before restart=100.
Target range script: restart verified after accepted frame 38: score=0, completed=0, remaining=5, roundComplete=False, camera=(0.000, 1.700, 8.000) forward=(0.000, 0.000, -1.000); every target restored to its authored position, size, and unhit mesh.
Target range script: round complete after accepted frame 49: score=500, completed=5, remaining=0, roundComplete=True, camera=(0.000, 1.700, 8.000) forward=(0.125, -0.057, -0.990); completed state presented from frame 50.
Target range script: final state proven presented after accepted frame 167: score=500, completed=5, remaining=0, roundComplete=True, camera=(0.000, 1.700, 8.000) forward=(0.125, -0.057, -0.990); held for 120 accepted frames.
```

### Headless GPU verification

The idle range is verified on the GPU with analytically derived probes:

```bash
./tools/test-vulkan-headless.sh \
  --project samples/Volt.VulkanMeshDemo \
  --frames 600 --validation --screenshots \
  --sample-arg=--scene --sample-arg target-range \
  --expect-pixel-class 405,360,red \
  --expect-pixel-class 648,372,red \
  --expect-pixel-class 647,360,white
```

At 1280x720 target 1 projects to `(404.5, 359.5)` with a 44-pixel radius,
and target 3 to `(639.5, 364.0)` with a 27-pixel radius. `405,360` is the
first target's centre; `648,372` is inside the third target but outside
the reticle's arms; `647,360` is seven pixels right of the crosshair and
still on its horizontal arm, which proves the white plus renders on top
of the target behind it.

The scripted round is captured only after the completed state has been
held long enough to be proven presented, so the image cannot race the
last hit:

```bash
./tools/test-vulkan-headless.sh \
  --project samples/Volt.VulkanMeshDemo \
  --frames 600 --validation --screenshots \
  --sample-arg=--scene --sample-arg target-range \
  --sample-arg=--range-script \
  --screenshot-after-line "final state proven presented" \
  --expect-pixel-class 310,323,blue \
  --expect-pixel-class 479,263,blue \
  --expect-pixel-class 647,360,white
```

The scripted camera ends aimed at target 5, so the completed targets
project elsewhere than at the initial pose. Target 1's centre is at
`(310, 323.5)` and target 4's at `(479, 263)`; both probes are inside
their shrunken blue silhouettes. The reticle probe is unchanged, which
shows the overlay follows the view rather than the world.

Resizing proves the completed round survives swapchain recreation and
that the reticle stays centred in the new viewport:

```bash
./tools/test-vulkan-headless.sh \
  --project samples/Volt.VulkanMeshDemo \
  --frames 600 --validation --screenshots \
  --weston-shell desktop \
  --sample-arg=--scene --sample-arg target-range \
  --sample-arg=--range-script \
  --sample-arg=--resize-at-frame --sample-arg 200 \
  --sample-arg=--resize-to --sample-arg 960x540 \
  --screenshot-after-line "Target range state stable after recreation" \
  --expect-pixel-class 232,242,blue \
  --expect-pixel-class 359,197,blue \
  --expect-pixel-class 485,270,white
```

The two blue probes are the same target centres scaled by `0.75`, and
`485,270` is five pixels right of the centre of the `960x540` viewport.
The run reports `Resize observed: ... generation 1 -> 2` and
`Target range state stable after recreation: score=500, completed=5,
remaining=0, roundComplete=True, camera=(0.000, 1.700, 8.000)
forward=(0.125, -0.057, -0.990) after 30 accepted frames in generation
2.`, so the world, score, and completed visuals were not rebuilt.

Probe placement is guarded by unit tests, not just by a screenshot:
`TargetRangeProbeTests` projects the target centres with the same view,
projection, and extent, requires each probe to sit within 60% of its
target's pixel radius, and requires the reticle probes to lie on the
horizontal arm at both extents.

## Engine input

`Volt.Platform.Input` owns the keyboard and mouse state of every
window. The sample reads it once per polled frame instead of calling
any SDL input function:

```csharp
window.PollEvents();

InputSnapshot input = window.Input.Snapshot;

if (input.WasKeyPressed(KeyCode.R))
{
    Restart();
}

if (input.IsKeyDown(KeyCode.W))
{
    MoveForward(deltaSeconds);
}
```

`PollEvents` starts an input frame for every VOLT window, dispatches
each native event to the window named by its ID, and then freezes one
snapshot per window. Polling from any window therefore updates all of
them, and input from one window never becomes input for another. A
snapshot is a value type: a copy taken before the next poll keeps its
own state, and querying it allocates nothing. Call `PollEvents` once
per application frame and read the snapshot afterwards; a second poll
starts a new frame, so an edge belongs to exactly one completed frame.

Keys are physical positions (`KeyCode.W`, `KeyCode.LeftShift`,
`KeyCode.Escape`), not characters produced by the layout.
`IsKeyDown` is held state; `WasKeyPressed` and `WasKeyReleased` are
one-frame edges. A key pressed and released within one frame reports
both edges and ends up, and native key repeats never add a press.
Mouse buttons have the same three queries. Motion and wheel deltas
accumulate within a frame and reset at the next poll. `MousePosition`
is in logical window units and must not be compared with framebuffer
pixels on a high-density display; `MouseMotion` is the unclamped
frame movement.

`KeyboardFocused` and `MouseFocus` describe focus at the end of the
frame. Losing focus clears held keys and buttons without reporting
release edges, so a focus change cannot fabricate a press, and
regaining focus does not invent one. A frame that contains a focus
loss is cleared as a whole and reports `FocusLostDuringFrame` even if
focus returned before the snapshot was read, so an action can rely on
that flag instead of guessing where in the poll the loss happened.
Actions consume edges (`WasKeyPressed`, `WasMouseButtonPressed`)
rather than held state; that is what keeps a click that both starts
and ends inside one poll, and what keeps a held button from repeating.
Relative mouse mode is available
through `TrySetRelativeMouseMode`, `IsRelativeMouseModeEnabled`, and
`DiscardRelativeMotion`, but the platform never captures the pointer
on its own: the application decides when capture is appropriate.
While the window is captured, `RelativeMotion` carries the
pointer-locked delta an FPS camera should consume. It is zero in the
frame that starts capture, so the first captured frame cannot jump the
view. See [src/Volt.Platform/Input/README.md](../../src/Volt.Platform/Input/README.md).

## Depth attachment

The render pass has a color attachment and a depth attachment. Each
swapchain generation creates one `VulkanDepthAttachment` per swapchain
image, so concurrently active frames never write the same depth image.
All attachments in a generation share one format chosen by
`VulkanDepthAttachment.SelectSupportedFormat`: `D32Sfloat` when the
physical device reports optimal-tiling depth-stencil attachment
support, otherwise `D16Unorm`. Device support is queried per physical
device instead of being assumed.

The graphics pipeline enables depth testing and depth writes with the
conventional `Less` comparison. Every frame clears depth to `1.0`
when the render pass begins, and the depth attachment is used only as
`DepthStencilAttachmentOptimal`; no stencil aspect is created or used.

Depth resources belong to the swapchain generation. After a resize, the
old generation is retired after device idle, which destroys its
framebuffers before their depth views, then the depth images and their
memory. The replacement generation builds fresh attachments for the
new extent. Mesh buffers are not re-uploaded.

The default gallery's three shapes do not overlap, so its image is
visually unchanged by depth testing. The opt-in depth-occlusion scene
uses the same render pass, depth attachments, and pipeline with
overlapping meshes to show that the near red triangle survives the
later far blue quad. That is the depth-test proof; see
[Scenes](#scenes).

## Rendering structure

- `DemoMeshes` uploads the three meshes once for the lifetime of the
  logical device. Swapchain recreation does not re-upload them.
- `MeshRenderer` owns the render pass, pipeline, depth attachments,
  and one framebuffer per swapchain image. It records one render pass
  containing the draw plan: three meshes and four draw calls for the
  gallery and depth-occlusion scenes, or five draw calls for the
  camera scene. Complete meshes use `VulkanMeshDrawCommands.Record`;
  the quad's two triangles use `VulkanMeshDrawCommands.RecordRange`.
  `MeshRenderer.CreateDrawPlan` derives that sequence, including each
  draw instance's model matrix, so the plan can be inspected and
  tested without a Vulkan device. For the ECS scene there is no static
  plan: the renderer resolves each extracted record, pushes its
  `model * view * projection`, and splits the quad into the same two
  ranges, so four entities still produce five draws.
- `EcsDemoScene` owns the ECS world, the camera entity, the extractor,
  and a fixed-capacity `RenderInstance` buffer reused for every frame.
  It outlives swapchain generations, so a resize never rebuilds the
  world or resets a component value. `EcsScript` drives the opt-in
  deterministic mutation. `EcsMeshResolver` and `EcsMaterialPalette`
  are small sample-local tables that validate complete generational
  handles before any GPU buffer is used.
- The reusable extraction lives in `Volt.Rendering.Extraction`:
  `RenderInstance` is a backend-independent snapshot record and
  `EcsRenderExtractor` reads chunk component spans without modifying
  the world. `Volt.Rendering.Cameras.CameraEntityMatrices` turns a
  camera entity into view and projection matrices for any aspect ratio
  and viewport orientation. Neither type references Vulkan or SDL.
- `Volt.Rendering.Cameras.CameraController` is the shared engine
  camera: it holds position, yaw, pitch, speed, sprint multiplier,
  and mouse sensitivity, and produces the view matrix and
  allocation-free movement/look updates. Its initial position is
  supplied by the sample. `CameraScript` drives it deterministically
  for headless runs. `CameraInput` maps the engine's per-window input
  snapshot onto `CameraMovementInput` and the gameplay command, owns
  relative mouse capture policy, and calls no SDL function; it is
  created and disposed by `Program`. `CameraCaptureController` holds
  the click and release
  state machine behind a small injectable seam, so capture, release,
  failed native transitions, and shutdown are tested without a
  desktop. The controller has no SDL, window, or Vulkan dependency.
- `CameraTransforms` is sample-local: it selects the legacy
  clip-space projection, keeps the camera scene's documented initial
  pose and model transforms, and documents the
  row-vector/column-major upload convention. The model matrices come
  from `Volt.Rendering.Transforms.TransformMatrices`, the projection
  from `Volt.Rendering.Cameras.CameraProjection` with an explicit
  downward viewport orientation, and the view from the controller.
- The reusable engine math lives in `Volt.Rendering`, which depends
  on `Volt.Ecs`. `TransformMatrices` composes model matrices from
  `PositionComponent`, `RotationComponent`, and `ScaleComponent` and
  composes `model * view * projection`. `CameraProjection` reuses
  `CameraComponent.CreateProjectionMatrix` and adds the viewport Y
  choice. `Volt.Ecs` has no rendering, SDL, or Vulkan dependency.
- `MeshSwapchainResources` owns one swapchain generation, including
  image views and the frame scheduler. It is recreated after resize or
  presentation-driven swapchain changes. Each creation is reported with
  its generation number, extent, depth format, and attachment count.
- `Program` handles windowing, device selection, command buffers,
  bounded execution, the opt-in resize scenario, and resource lifetime.

The frame scheduler provides the render-finished semaphore used by
presentation and preserves the distinction between graphics completion
and presentation completion. The sample waits for device idle before
disposing a swapchain generation or the uploaded meshes.

This sample requires a supported swapchain maintenance1 implementation
with presentation fences. Device setup negotiates the KHR variant first
and then the EXT variant through VOLT's existing extension requirements.
If neither variant is available, the sample fails before creating the
logical device with a `NotSupportedException`. It intentionally does not
use a legacy presentation path: device idle does not establish
presentation retirement.

## Requirements

- .NET 10
- A Vulkan 1.2-capable graphics and presentation device
- Either `VK_KHR_swapchain_maintenance1` or
  `VK_EXT_swapchain_maintenance1`, including the matching surface
  maintenance extension and `VK_KHR_get_surface_capabilities2`
- `glslangValidator` on `PATH` when building, to compile the shaders
- Build the solution or the sample project; the project compiles
  `Shaders/mesh.vert` and `Shaders/mesh.frag` into tracked SPIR-V files

## Running

Interactive:

```bash
dotnet run --project samples/Volt.VulkanMeshDemo -c Release
```

The playable first-person round (see [Target range](#target-range) for
the controls):

```bash
dotnet run \
  --project samples/Volt.VulkanMeshDemo \
  -c Release -- \
  --validation \
  --scene target-range
```

Bounded run with validation layers:

```bash
dotnet run \
  --project samples/Volt.VulkanMeshDemo \
  -c Release -- \
  --validation \
  --frames 120
```

The bounded run counts only frames whose presentation request was
accepted. It exits with a nonzero status on initialization, rendering,
presentation, or cleanup failure. If presentation makes no progress for
ten seconds, the bounded run reports the stall and exits instead of
waiting forever. This covers a minimized window or an unavailable
presentation surface.

### Headless depth-occlusion verification

The headless harness supports targeted pixel assertions, so the scene
is verified on the GPU instead of only by pipeline configuration:

```bash
# Depth testing enabled: the overlap stays red.
./tools/test-vulkan-headless.sh \
  --project samples/Volt.VulkanMeshDemo \
  --frames 600 --validation --screenshots --expect-clusters 2 \
  --sample-arg=--scene --sample-arg depth-occlusion \
  --expect-pixel-class 580,400,red \
  --expect-pixel-class 720,400,blue

# Negative control: the same overlap becomes blue.
./tools/test-vulkan-headless.sh \
  --project samples/Volt.VulkanMeshDemo \
  --frames 600 --validation --screenshots --expect-clusters 2 \
  --sample-arg=--scene --sample-arg depth-occlusion \
  --sample-arg=--disable-depth-test \
  --expect-pixel-class 580,400,blue \
  --expect-pixel-class 720,400,blue
```

The probes are derived from the geometry and the shader projection for
the fixed 1280x720 viewport: at row 400 the near triangle spans
`x = 447..616` and the far quad spans `x = 539..780`. `580,400` is
inside both meshes, while `720,400` is inside the quad only. Both
points are more than 30 pixels from every silhouette. The unit tests
recompute this mapping and fail if geometry changes move either probe
onto an edge.

### Headless camera-transform verification

The camera scene is verified on the GPU with the same harness. The red
probe is the projected local point `(0, -0.1, 0)` inside triangle A,
which also falls inside the farther blue quad. The blue probe is inside
the quad only:

```bash
./tools/test-vulkan-headless.sh \
  --project samples/Volt.VulkanMeshDemo \
  --frames 600 --validation --screenshots --expect-clusters 2 \
  --sample-arg=--scene --sample-arg camera-transforms \
  --expect-pixel-class 484,296,red \
  --expect-pixel-class 700,430,blue
```

At 1280x720 the camera projection places triangle A over the quad at
about `(484, 296)`, triangle B around `(839, 275)`, the quad at
`x = 435..845, y = 246..474`, and the hexagon below and left. The
triangles are drawn before the quad, so the red probe proves depth
testing still decides the overlap. Unit tests project the meshes with
the same matrices and fail if either probe moves onto a silhouette.

### Headless scripted-camera verification

`--screenshot-after-line` makes the harness wait for the final-pose
checkpoint before capturing, so the screenshot shows the moved camera
and not the initial frame:

```bash
./tools/test-vulkan-headless.sh \
  --project samples/Volt.VulkanMeshDemo \
  --frames 600 --validation --screenshots --expect-clusters 2 \
  --sample-arg=--scene --sample-arg camera-transforms \
  --sample-arg=--camera-script \
  --screenshot-after-line "Camera final pose reached" \
  --expect-pixel-class 343,280,red \
  --expect-pixel-class 546,360,blue
```

Both probes are computed from the final pose rather than picked from a
screenshot. The red probe is the projection of triangle A's local point
`(0, -0.1, 0)`, which lies inside triangle A and inside the farther
quad, so it still proves depth testing decides the overlap. The blue
probe is the projection of the quad's local origin, which is outside
every other silhouette. At 1280x720 they project to `(342.9, 279.7)` and
`(545.7, 360.0)`. The unit tests recompute both from the same camera
state and require at least 25 pixels of clearance from every edge.

Moving the camera changes the rendered viewpoint, so the scripted
screenshot differs from the fixed-camera baseline by far more than
antialiasing noise. The comparison is made between the two captured
frames, not by requiring byte equality with a driver-specific reference.

### Resize scenario

An opt-in resize scenario exercises swapchain recreation without user
input:

```bash
dotnet run \
  --project samples/Volt.VulkanMeshDemo \
  -c Release -- \
  --validation \
  --frames 600 \
  --resize-at-frame 200 \
  --resize-to 960x540
```

`--resize-at-frame N` and `--resize-to WIDTHxHEIGHT` must be supplied
together. The trigger frame must be positive and less than `--frames`,
and each dimension must be between 1 and 16384. The sample calls
`SDL_SetWindowSize` on the main thread after presenting frame `N` and
keeps rendering while the compositor applies the new size. It reports:

- `Swapchain: generation 1, ...` for the initial generation.
- `Resize requested at frame N: ...` with the requested size and the
  current window and framebuffer sizes.
- `Retiring swapchain generation 1, 1280x720.` before the old generation
  is destroyed.
- `Resize observed: framebuffer WxH, window WxH, generation 1 -> 2,
  after N presented frames.` once the framebuffer extent actually
  changed and the replacement generation was created.
- `Swapchain: generation 2, ...` with the new extent, depth format, and
  per-image depth attachment count.
- `Frames presented after recreation: N.`

The request triggers at most once. A bounded run fails with a nonzero
status if the compositor never applies the resize, so a logged request
alone never counts as success. Without both arguments the sample behaves
exactly as before.

Fullscreen compositor shells ignore client resize requests. The
committed headless harness therefore offers an opt-in desktop shell for
this scenario; see `tools/README.md`.

The scripted camera and the resize can be combined. The camera state
survives recreation, and the sample reports a second, stable checkpoint
30 accepted frames into the new generation, so a screenshot can prove
the final pose is still rendered at `960x540`:

```bash
./tools/test-vulkan-headless.sh \
  --project samples/Volt.VulkanMeshDemo \
  --frames 600 --validation --screenshots \
  --weston-shell desktop \
  --sample-arg=--scene --sample-arg camera-transforms \
  --sample-arg=--camera-script \
  --sample-arg=--resize-at-frame --sample-arg 200 \
  --sample-arg=--resize-to --sample-arg 960x540 \
  --screenshot-after-line "Camera final pose stable after recreation" \
  --expect-pixel-class 257,210,red \
  --expect-pixel-class 409,270,blue
```

The two probes are the same local points as the 1280x720 run scaled by
`0.75`; both resize extents share the 16:9 aspect ratio. These resize
runs deliberately omit `--expect-clusters`: once the client shrinks to
`960x540` the window no longer covers the compositor output, so the
horizontal content clusters include compositor chrome and the desktop
behind the window. Pixel-class probes at points derived from the new
extent are the verification; the cluster count is only meaningful for a
fullscreen window, as in the runs above.

The ECS scene uses the same scenario. The world, the entity identities,
and the camera component values survive recreation, and the projection
is rebuilt from the new extent, so the changed state is still rendered
at `960x540`:

```bash
./tools/test-vulkan-headless.sh \
  --project samples/Volt.VulkanMeshDemo \
  --frames 600 --validation --screenshots \
  --weston-shell desktop \
  --sample-arg=--scene --sample-arg ecs-scene \
  --sample-arg=--ecs-script \
  --sample-arg=--resize-at-frame --sample-arg 200 \
  --sample-arg=--resize-to --sample-arg 960x540 \
  --screenshot-after-line "Ecs state stable after recreation" \
  --expect-pixel-class 457,215,red \
  --expect-pixel-class 446,270,blue
```

Triangle A's changed centre projects to `(456.6, 215.3)` and the quad's
centre to `(445.8, 270.0)` at the new extent. The run also reports
`Ecs state stable after recreation: camera=(0.300, 0.000, 3.000)
triangleA=(0.150, 0.350, 0.000) after 30 accepted frames in generation
2.`, which proves the world was not rebuilt.

## Tests

```bash
dotnet test tests/Volt.VulkanMeshDemo.Tests -c Release
```

The focused tests cover mesh counts, index validity, the draw plans
including the quad's two partial index ranges and the camera scene's
two triangle instances sharing one uploaded mesh, scene selection and
the depth-negative-control rules, occlusion depths and colours,
local-space camera geometry, analytic probe placement for the initial
and scripted final poses, matrix convention and packing, legacy
projection equivalence, perspective near/far depth mapping, Y
orientation, aspect-dependent projection for both resize extents, the
camera controller (initial pose, view equivalence, basis, translation,
strafing, vertical movement, diagonal normalization, sprint, elapsed
time independence, clamping, non-finite handling, look signs, pitch
clamping, continuous yaw, and cancellation), the deterministic script
(phase boundaries, final-pose hold, path length, determinism, and timing
independence), command-line parsing for validation, bounded frames, the
camera script, the ECS script, the ECS scene, and the resize scenario
(paired arguments, bounds, malformed sizes, triggers at or past the
frame limit, duplicates, and one-time triggering), plus the required
swapchain maintenance1 negotiation. The ECS scene tests check the four
renderable entities, the shared triangle handle, the five draw
commands, the camera entity's pose and far plane, the scripted
mutation's isolation, and the projected pixel positions of the
scripted probes. `EcsMeshResolverTests` covers valid, invalid, stale,
and unknown mesh and material handles plus an unexpected upload order.
`EcsScriptTests` covers the initial hold, the change frame, the settled
checkpoint, the held changed state, and determinism.

The target range has its own focused coverage. `TargetRangeRaycastTests`
covers direct hits, misses, nearest overlap, targets behind the origin,
the distance limit, a ray starting inside a sphere, an arbitrary camera
orientation, completed targets, direction normalization, degenerate
spheres, and invalid origins, directions, and distances.
`TargetRangeControllerTests` covers the initial state, a hit from the
initial pose, an aimed miss, distinct hits, duplicate hits, round
completion, restart, ECS camera movement and rotation, extraction of
the hit visuals, aiming accuracy, pitch clamping, and zero allocation
for updates without gameplay actions. `TargetRangeSceneTests` covers
the entity layout, extraction, the collision sphere bounding the
authored scale, every target being inside the initial view and above a
two-degree angular radius, idempotent hits, value-only mutation (the
world's query enumerator stays valid), target restoration, camera sync,
and the overlay handle. `TargetRangeScriptTests` covers the checkpoint
order, the miss and first hit, restart verification, movement distance,
repeatability, the held completed state, and the required minimum
frames. `TargetRangeProbeTests` recomputes every documented screenshot
probe from the scene and camera at both extents. `TargetRangeReticleTests`
covers centring, square pixel size on non-square and resized viewports,
the minimum size, and viewport validation. `TargetRangeInputTests` and
new `CameraCaptureControllerTests` cases cover the capture click not
firing, one shot per press, held buttons, Escape releasing once and
blocking capture and fire for as long as it stays down, focus loss,
another window's click, a pending release, failed capture, and restart
precedence. `CameraControllerTests` covers the new `SetPose` reset,
including pitch clamping and rejection of non-finite values.
