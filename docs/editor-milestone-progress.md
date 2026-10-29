# Editor milestone progress record

This file is the durable hand-off for the editor interaction milestone.
It records what is done, what was verified, and what to do next, so a
later session does not have to reconstruct progress from a conversation.

## Recovered state

- Repository: `PandeManiac/VOLT`.
- The recovered workspace was clean, on commit `0c6acbb`, and that
  commit is exactly `origin/master`; 204 commits of earlier work were
  already pushed. Nothing was left uncommitted or unpushed.
- Work for this continuation is on branch
  `fix/editor-milestone-continuation`, which is based on `0c6acbb`.
- Two older branches, `fix/editor-interaction-reliability` and
  `fix/editor-vulkan-stability`, hold commits that are not ancestors of
  master. They are preserved on the remote; review them before merging
  rather than assuming their work is missing from master.

## Build and test

The .NET 10 SDK is not preinstalled in the cloud workspace. Install it
with the official script, then build and test:

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
chmod +x /tmp/dotnet-install.sh
/tmp/dotnet-install.sh --channel 10.0 --install-dir "$HOME/.dotnet" --quality GA
export PATH="$HOME/.dotnet:$PATH"

dotnet build Volt.slnx -c Release
dotnet test Volt.slnx -c Release
```

Last verified: build succeeded with 0 warnings and 0 errors; the full
suite reported 3903 passed, 0 failed, 33 skipped (the skipped tests are
the opt-in native Vulkan tests that need a real device).

## Stage status

| Stage | State |
| --- | --- |
| A. Relative mouse capture | Done. `Volt.Editor/Input/WindowPointerCapture` is a reconciled state machine over the platform's native relative mode; `SceneViewport` owns the look lifecycle. Covered by `ViewportLookCaptureTests` (capture, release, focus loss, viewport removal, refusal, revocation, escape, single consumption, cross-panel hover). |
| B. Translation gizmos | Done. Ray-plane-constrained axis solve and a real ray-plane solve for plane handles, with near-parallel refusal and a stable press reference. Covered by `GizmoDragTests`. |
| C. Rotation and scale gizmos | Done. Rotation is solved in world space and converted back through the authored rotation, so parent rotations do not tilt the object. Scale clamps at a positive minimum. |
| D. Selection and manipulation consistency | Done. One selection set shared by hierarchy, viewport, and inspector; drag cancel restores transforms and adds no undo step; parent-and-child selections transform the parent once. |
| E. Functional UI audit | Done, and recorded in `docs/editor-control-audit.md`. Every menu action is wired and disabled rather than inert when it cannot run (`EditorCommandAuditTests`). |
| F. Core authoring workflow | Done. Create, select, rename, transform, inspect, duplicate, delete, undo/redo, save, reopen. Project folders can be created, browsed while empty, and renamed with scene and component references rewritten; the browser supports multi-selection, bulk deletion, and bulk duplication (recovered from `fix/editor-interaction-reliability`). |
| G. Vulkan shutdown cleanup | Fixed, awaiting hardware confirmation. `EditorSceneRenderer.Dispose` idles the device before destroying the pipeline, targets, and meshes, because the host's own idle runs later. |
| H. Final verification | Partial. Build, full test suite, and headless snapshots pass in the cloud; the GPU session, native window input, and actual gizmo feel need a machine with a display. |

## What the pipeline fix was

`EditorSceneRenderer` and `VulkanUiHost` are `using` declarations, so the
renderer is disposed first while the host's `WaitIdle()` runs last. The
scene pipeline, its offscreen targets, and its meshes were therefore
destroyed while a submitted command buffer could still reference them,
which is what `VUID-vkDestroyPipeline-pipeline-00765` reports at exit.
The renderer now waits for device idle before releasing anything, and
still releases the remaining resources when the device is lost.

## Remaining verification on real hardware

The cloud workspace has no Vulkan ICD, no compositor, and no root access
to install either, so it cannot present a real frame. What the cloud can
check does pass: the solution builds cleanly, the full suite is green, and
CPU snapshots of the start screen and an authoring session
(`EditorSnapshot`) show readable text and correct layout after the
recovered font and frame-slot fixes. That path paints without a GPU by
design, so it cannot prove the Vulkan atlas mapping, resource lifetime,
or presentation.

These checks need a machine with a display and are **not** claimed as
verified:

1. Launch with validation enabled and exit after a normal authoring
   session; confirm no `VUID-vkDestroyPipeline-pipeline-00765` and no
   other validation output:
   `dotnet run --project src/Volt.Editor -c Release -- --validation --frames 300`
2. Repeat launch and exit several times, including after opening and
   closing a project.
3. Hold RMB in the Scene view; confirm the cursor hides, rotation never
   stops at a desktop edge, and releasing or alt-tabbing restores the
   pointer with no camera jump. Check both Wayland and X11.
4. Drag each translation axis and plane from grazing, overhead, and
   close-up angles; confirm the object stays attached to the handle.
5. Confirm rotation and scale feel consistent on rotated objects and
   under a rotated parent, and that one drag is one undo step.
6. Click through the menus and panels named in the control inventory.

## Project-browser recovery

`fix/editor-interaction-reliability` carried four sequential, tested
commits on the same base as this branch: multi-selection with bulk
deletion, bulk asset duplication, empty-folder creation, and folder
renaming that rewrites scene and component asset references.

Folder creation was first carried forward by hand (commit `b93cef7`) so
the browser would not silently lose an empty folder; the remaining three
were folded in by merging the branch (commit `cb7836e`). Merging kept the
branch's own tests and history instead of retyping about 2,300 lines
against a moved base. The only conflicts were the hunks where the
hand-carried folder code met the branch's version of the same code (the
branch side was taken) and the guide, where the two descriptions of asset
selection were combined into one paragraph.

A folder rename now rewrites asset paths inside scenes, including
unknown component payloads, and repoints the startup scene, so autorings
survive moving assets between folders. The browser keeps several selected
assets across grid/list switches and interface-scale changes.

## Vulkan-branch recovery

`fix/editor-vulkan-stability` was six commits on a much older base, but it
held real fixes master lacked. All six are now here, one commit each, with
their tests:

- An offscreen render target creates the render pass before the
  framebuffer that references it (`37f310f`).
- An abandoned frame keeps the original failure instead of the error
  produced while cleaning up after it (`55a7ab9`).
- Startup reports the physical and logical window size plus the display
  scale from `UiSurfaceMetrics`, because the layout surface is still zero
  before the first frame (`b85e7cf`).
- Every painted font belongs to one atlas-mapped font set: the shell
  publishes its theme to the context, panels take captions on attach, and
  the renderer fails with the font name instead of painting white blocks
  (`7a583bc`).
- The frame slot is published before the UI pass records, so a sampled
  target is in the layout the UI expects (`90147a3`).
- The start screen uses larger, higher-contrast text (`ca9cdbf`).

Only two of the six conflicted. The window-geometry commit disagreed with
the saved window size and position, so the diagnostic sink was kept and
the saved geometry preserved. The font commit's project panel had moved
since its base; master's block was kept and the deferred-caption change
re-applied on top. Because the cherry-pick had already applied the other
hunks of that commit, a first pass inserted a duplicate `ApplyDpiScale`,
which the build caught and the duplicate was removed.

## Next actions

- Run the hardware checks above, including one launch that paints the
  start screen and one with a project open, to confirm no text paints as
  white blocks.
- Extend the control inventory if new panels or commands are added.
- Both stale branches are now folded in; there is no unmerged project
  work left on them.
