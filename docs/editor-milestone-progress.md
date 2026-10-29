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
| F. Core authoring workflow | Done. Create, select, rename, transform, inspect, duplicate, delete, undo/redo, save, reopen. Project folders can be created and browsed while empty (recovered from `fix/editor-interaction-reliability`, commit `20884e6`). |
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

The cloud workspace has no Vulkan device and no display server, so these
must be run locally and are **not** claimed as verified:

1. Launch with validation enabled and exit after a normal authoring
   session; confirm no `VUID-vkDestroyPipeline-pipeline-00765` and no
   other validation output.
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

## Recovered work still on the old branches

`fix/editor-interaction-reliability` also has project-browser
multi-selection, bulk duplication, and folder renaming, written against
its own browser model. They were not carried over: master's browser uses
a single selection, so those commits need a deliberate port rather than
a cherry-pick. Folder creation was the only piece that applied cleanly
and is now on this branch; the stale commit's multi-selection copy and
snapshots were left out, and the golden baselines were regenerated from
the current interface.

## Next actions

- Verify the shutdown fix and input behaviour on hardware (list above).
- Port project-browser multi-selection, bulk duplication, and folder
  renaming from the old branch if that workflow is still wanted.
- Extend the control inventory if new panels or commands are added.
