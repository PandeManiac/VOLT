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
binding zero, not shader-based vertex pulling. A single push constant
supplies the current aspect ratio, so the three shapes stay separated
and keep their proportions when the window is resized.

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

The three shapes do not overlap, so adding the depth buffer leaves the
image visually unchanged. That confirms correct attachment
configuration but does not demonstrate depth occlusion for overlapping
geometry.

## Rendering structure

- `DemoMeshes` uploads the three meshes once for the lifetime of the
  logical device. Swapchain recreation does not re-upload them.
- `MeshRenderer` owns the render pass, pipeline, depth attachments,
  and one framebuffer per swapchain image. It records one render pass
  containing the draw plan: three meshes and four draw calls. Complete
  meshes use `VulkanMeshDrawCommands.Record`; the quad's two triangles
  use `VulkanMeshDrawCommands.RecordRange`. `MeshRenderer.CreateDrawPlan`
  derives that sequence from the mesh geometries, so the plan can be
  inspected and tested without a Vulkan device.
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

## Tests

```bash
dotnet test tests/Volt.VulkanMeshDemo.Tests -c Release
```

The focused tests cover mesh counts, index validity, the four-draw
plan including the quad's two partial index ranges, command-line
parsing for validation, bounded frames, and the resize scenario
(paired arguments, bounds, malformed sizes, triggers at or past the
frame limit, duplicates, and one-time triggering), plus the required
swapchain maintenance1 negotiation.
