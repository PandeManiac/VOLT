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
described above. `--scene depth-occlusion` uploads an overlapping scene
instead, once per run, through the same mesh buffers:

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
sampled on the main thread through SDL's state queries, so the existing
event pump, resize handling, and close handling are unchanged and no
global hooks or synthetic events are used. A headless run never clicks,
so it never captures the mouse.

The camera controller has application lifetime. `MeshRenderer` queries
it for the view matrix on every recorded frame and composes
`model * view * projection`, so movement is visible without recreating
the renderer, and a swapchain recreation leaves position, yaw, and pitch
untouched. The projection still comes from the current extent, so the
new aspect ratio applies immediately after a resize.

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
  tested without a Vulkan device.
- `CameraController` holds position, yaw, pitch, speed, sprint
  multiplier, and mouse sensitivity, and produces the view matrix and
  allocation-free movement/look updates. `CameraScript` drives it
  deterministically for headless runs. `CameraInput` samples SDL
  keyboard and mouse state and owns relative mouse capture; it is
  created and disposed by `Program`. `CameraCaptureController` holds
  the click and release state machine behind a small injectable seam,
  so capture, release, failed native transitions, and shutdown are
  tested without a desktop.
- `CameraTransforms` builds the legacy and perspective projections,
  the default look-at view matrix, and the camera scene's model
  transforms. It documents the row-vector/column-major convention.
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
  --frames 600 --validation --screenshots --expect-clusters 2 \
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
`0.75`; both resize extents share the 16:9 aspect ratio.

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
camera script, and the resize scenario (paired arguments, bounds,
malformed sizes, triggers at or past the frame limit, duplicates, and
one-time triggering), plus the required swapchain maintenance1
negotiation.
