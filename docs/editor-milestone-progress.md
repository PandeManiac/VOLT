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

## Theme correction and runtime ECS inspection (October 2026)

Branch `freebuff/212b0f79ee2a8862f016a79a`, based on master `31dc5f5`.

### What changed

- **Colour-space fix.** The swapchain selector prefers
  `B8G8R8A8_SRGB`, and the UI shader wrote palette bytes into it
  unchanged, so the framebuffer applied the sRGB transfer function a
  second time. That is the measured cause of the interface rendering
  much brighter than its palette. `ui.frag` now decodes authored sRGB
to linear when the target format carries the transfer function, and
  the new `UiSurfaceColorSpace` decides that from the chosen format so
  a UNORM surface still passes bytes through. The third push constant
  carries the flag; `ui.vert`/`ui.frag` were rebuilt with glslang
  16.6.0.
- **Darker theme and larger type.** The palette is now the requested
  near-black charcoal set (`#11151C` window, `#1A212B`-range panels,
  `#10151D` wells, `#38B6E8` accent). Body text rose from 14 to 15 and
  the panel header from 13 to 14 logical units.
- **Read-only ECS introspection API.**
  `World.GetArchetypes`, `World.TryGetSignature`,
  `World.TryReadComponent<T>`, `World.TryGetArchetypeStorage`, and
  `ComponentRegistry.GetRegisteredComponents` / `TryGetInfoForType`.
  Reads copy their value; measured storage comes from the chunks
  themselves.
- **`RuntimeWorldSnapshot`.** Captures the live world, formats the
  component values it can (Position, Rotation, Scale, Velocity,
  AngularVelocity) and labels the rest as unformatted with their size,
  and runs real `World.Query` calls under a structural freeze.
- **ECS panel.** Runtime entity handle, component values, archetype
  storage table, and a component query builder. Refreshes four times a
  second and is read-only.

### Verified in the cloud

- Release build: 0 warnings, 0 errors.
- `WorldIntrospectionTests` 8 passed, `RuntimeWorldSnapshotTests` 6
  passed, `UiSurfaceColorSpaceTests` 7 cases passed.
- Full solution suite passes (see the session report for the exact
  count).
- Golden snapshot baselines and the guide images were regenerated and
  reviewed at 1440x900; the darker surfaces, separations, and larger
  text render correctly, and the ECS tab is present in the bottom tab
  group.

### Not verified here

- No Vulkan device exists in this workspace, so the sRGB decode has not
  been seen on hardware. A windowed run should show a `#1B212B` panel
  as that byte value rather than a brighter one, on both an sRGB
  swapchain and (if it is ever selected) the UNORM fallback.
- The ECS panel has not been driven interactively. Its data path is
  covered by tests; it is not the active tab in any snapshot preset, so
  its rendered layout has not been captured.

### Next

- Per-entity query results with paging.
- Project-owned gameplay assemblies, which is Milestone E.

## Runtime component editing (October 2026)

Same branch, on top of `dd0367b`.

### What changed

- **`RuntimeWorldEditor`.** `Apply` writes Position, Scale, Velocity, or
  AngularVelocity into the live Play Mode world. The session overload
  refuses work unless Play Mode is running or paused; the runtime
  overload additionally rejects non-finite values, an authored id with
  no runtime entity, a dead handle, and a component the entity does not
  have. It never adds or removes components, which would migrate the
  entity between archetypes from the UI thread.
- **Write-back rule.** An edit changes the live ECS component only. It
  never mutates the authored scene, marks it dirty, or creates an undo
  entry, and stopping Play discards it with the world. A running system
  can overwrite the value on the next tick; the panel reports that.
- **`RuntimeComponentSnapshot`.** Now carries the raw `Vector3` and an
  optional `RuntimeEditChannel`, so the panel edits the exact stored
  value instead of re-parsing a three-decimal display string. Rotation
  remains read-only and explains why: the runtime holds a world-space
  quaternion, not authored Euler angles.
- **ECS panel.** Position and scale (and motion values when present)
  render as axis fields. A field that has focus pauses the panel's
  four-per-second rebuild so typing is not interrupted; disposing the
  editors before a rebuild releases their handlers.

### Verified in the cloud

- Release build and the full solution suite (see the session report for
  the exact counts).
- `RuntimeWorldEditorTests` covers live-world writes, authored-data
  isolation, missing components, unknown ids, non-finite input, the
  stopped/running/stopped session boundary, and the snapshot's editable
  marker.

### Not verified here

- The panel has not been driven interactively, and no GPU exists in the
  workspace. The editing path is covered at the snapshot and world
  levels; the focused-field refresh hold and the row layout have not
  been captured in a window.

## Runtime-to-scene apply (October 2026)

Same branch, on top of `0cacea5`.

### What changed

- **`RuntimeWorldEditor.ApplyToScene`.** Copies one runtime transform
  value into the authored scene and records a single
  `SetTransformCommand`, so `Ctrl+Z` restores the previous transform and
  redo reapplies it. The session overload refuses stopped Play Mode.
  The runtime overload refuses velocity and angular velocity (no
  authored counterpart), a missing runtime entity, an authored object
  that has left the scene, and a value that already matches, which
  would otherwise add an empty undo step.
- **ECS panel.** Position and scale rows pair their axis field with an
  **Apply to Scene** button. The button reads the value stored in the
  live component, not uncommitted text in the field, and the panel's
  status line reports the outcome. The write-back rule is otherwise
  unchanged: an edit still touches the live world only.
- **Combined copy.** **Apply Transform to Scene** reads position and
  scale in one pass and writes them with a single
  `SetTransformCommand`, so a simulated pose is restored by one undo
  step. It refuses a stopped session, an object that left either the
  runtime or the scene, and an unchanged transform; if only one of the
  two components is present, the other is left alone and the message
  says which value was copied.
- **Divergence hints.** `RuntimeWorldSnapshot.CompareTransform`
  compares the runtime position and scale with the authored transform
  using a tolerance smaller than the displayed precision. Each editable
  row shows the authored value and marks it when it differs, and the
  section header lists the diverged channels, so it is obvious which
  values were never copied back. The hints refresh on the panel's timer
  and after a copy or undo.
- **Docs.** The guide and control audit describe the copy as the one
  explicit runtime-to-scene action and note that saving is still a
  separate step.

### Verified in the cloud

- Release build: 0 warnings, 0 errors.
- `Volt.Editor.Core.Tests` 65 passed, `Volt.Editor.Tests` 275 passed.
- New `RuntimeWorldEditorTests` cover the copied position with undo and
  redo, scale preserving rotation and position, the motion-channel
  refusal, the unchanged-value no-op, and the stopped-session boundary.
- New `EcsInspectorEditingTests` case types into the runtime X field,
  commits, clicks the real laid-out button, checks the authored
  transform and undo count, and undoes it. A second headless case
  clicks **Apply Transform to Scene** and proves position and scale
  arrive together and undo restores both.
- New `RuntimeWorldEditorTests` cover the combined copy with undo and
  redo, the unchanged-transform no-op, and stopped Play Mode, plus the
  divergence comparison: a diverged position beside a matching scale,
  a sub-display-precision difference treated as a match, and an
  entity that left the scene.
- A headless `EcsInspectorEditingTests` case asserts the authored value
  is shown, turns to a match after **Apply Transform to Scene**, and
  returns to diverged after undo.

### Not verified here

- No GPU or interactive session, so the button's rendered placement in
  the row has not been seen in a window. Its laid-out bounds and click
  path are covered by the headless test.

### Next

- Per-entity query results with paging.
- Project-owned gameplay assemblies (Milestone E).

## Paged query results (October 2026)

Same branch, on top of `b9d1963`.

### What changed

- **`RuntimeQueryResult`.** `RunQuery` now returns one
  `RuntimeQueryEntity` per match, with the ECS handle, the authored id
  and name when one exists, and the archetype signature. Matches follow
  authored scene order, with runtime-only entities after them, so paging
  stays stable between queries.
- **ECS panel.** Results render eight rows per page with **Prev** and
  **Next** buttons and a `Page N of M (X entities)` counter, below the
  query's existing counts and signature summary. The pager hides until
  a query matches; a new query, a failed query, and stopping Play all
  clear the list.

### Verified in the cloud

- Release build: 0 warnings, 0 errors.
- `Volt.Editor.Core.Tests` 66 passed, `Volt.Editor.Tests` 276 passed.
- New query assertions cover an authored entity's handle, name, and
  archetype, the stable authored ordering, and a runtime-only entity
  reporting a null authored id and an empty name.
- A headless panel test runs a real Position query over more than one
  page of entities, checks the eight-row first page and page counter,
  pages forward and back with laid-out buttons, and confirms Prev is
  disabled on the first page.

### Not verified here

- No GPU or interactive session, so the pager and result rows have not
  been seen in a window. The test lays the panel out on a tall surface,
  so scrolling the panel down to the query section was not exercised.

### Next

- Project-owned gameplay assemblies (Milestone E).

## Query row selection, filtering, and sorting (October 2026)

Same branch, on top of `a19cba3`.

### What changed

- **Rows select authored objects.** A query row calls
  `EditorSession.Select` with the entity's authored id, so the
  Hierarchy, Inspector, and viewport follow the click. Runtime-only rows
  have no authored object, so they are rendered disabled and muted
  instead of silently doing nothing.
- **Filtering.** A name field filters the matched list
  case-insensitively. Runtime-only rows have no name, so any filter hides
  them.
- **Sorting.** **Scene order**, **Name**, and **Archetype** buttons
  reorder the view. Ties fall back to name and then handle index, so
  paging stays deterministic. Changing the filter or sort returns to
  page one.
- **Counter.** The page label reports `X of Y entities` while a filter is
  active and plain `X entities` otherwise, and an empty view says that no
  entity matches the filter.

### Verified in the cloud

- Release build: 0 warnings, 0 errors.
- `Volt.Editor.Core.Tests` 66 passed, `Volt.Editor.Tests` 279 passed.
- Headless tests click a row and check the session's active entity, click
  a disabled runtime-only row and check the active entity is unchanged,
  and sort by name and then type a name filter to check the narrowed row
  and the `1 of N entities` counter.

### Not verified here

- No GPU or interactive session, so none of the controls have been seen
  in a window. The tests lay the panel out on a tall surface, so
  scrolling down to the query section is not exercised. Disabled rows
  rely on the framework's input gating rather than a distinct disabled
  treatment.

### Next

- Project-owned gameplay assemblies (Milestone E).

## Query result copying, keyboard navigation, and view state (October 2026)

Same branch, on top of `096c4fa`.

### What changed

- **Copy Results.** A button in the query pager writes the filtered list
  to the clipboard, one line per entity in the same format the rows
  show. It copies the whole filtered list, not only the page on screen,
  and reports the count on the status bar. An empty or unrun query
  reports instead of clearing the clipboard.
- **Arrow keys.** Up and Down move focus and selection between query
  rows, skipping runtime-only rows because they have no authored object
  to select. Navigation stays inside the current page.
- **View state.** The filter and sort are captured in
  `EcsInspectorPanelState`, the same way `ProjectPanelState` keeps the
  project browser's view, so an interface-scale rebuild or reopening the
  panel restores them. They already survived Play start and stop,
  because the panel is not recreated then.

### Verified in the cloud

- Release build: 0 warnings, 0 errors.
- `Volt.Editor.Tests` 282 passed, 0 failed.
- Headless tests click **Copy Results** and compare the clipboard line by
  line with the visible rows, focus a row and press Down then Up to check
  focus and the session's active entity, and rebuild the panel from a
  captured state to check the filter text and sort highlight come back.

### Not verified here

- No GPU or interactive session, so none of the controls have been seen
  in a window, and the real platform clipboard was not exercised: the
  tests use the in-process clipboard. Arrow navigation across page
  boundaries is deliberately not implemented, so the last row of a page
  does not move to the next page.

## Cross-page rows and remembered queries (October 2026)

Same branch, on top of `6aeb8c1`.

### What changed

- **Cross-page rows.** An arrow key at the edge of a page now carries
  focus and selection onto the adjacent page: Down lands on its first
  selectable row, Up on the last one. A page holding only runtime
  entities is not entered, rather than flipping the pager without a
  selection.
- **Remembered query.** The checked components are captured with the
  filter and sort. A non-empty remembered set means the query is
  expected back, so the panel runs it again as soon as a world exists:
  when Play Mode starts, and when the panel is rebuilt during Play.
  Stopping Play still clears results that describe a world that is gone;
  the intent survives so the next Play run answers the query on its own.

### Verified in the cloud

- Release build: 0 warnings, 0 errors.
- `Volt.Editor.Tests` 284 passed, 0 failed.
- Headless tests press Down once per row until the page ends and check
  the first row of page two takes focus and selection, then press Up to
  come back to the last row of page one. Another builds a panel from a
  remembered component and checks that Play start answers the query
  with no click, then stops and starts again to see it answered twice.

### Not verified here

- No GPU or interactive session, so the pager and the arrow keys have
  not been seen in a window. Focus and selection are driven through the
  headless input context, not a real window or a real clipboard.

## A pinned pager and page keys (October 2026)

Same branch, on top of `181e338`.

### What changed

- **Pinned pager.** The pager row, including **Copy Results**, was the
  last item inside the panel's scrolling document, so a long archetype
  or query section pushed it out of view. It now sits below the scroll
  area in the panel's own layout, which keeps page changes and copying
  in reach at any scroll offset.
- **Page Up and Page Down.** On a focused query row they jump a whole
  page and land on the row an arrow would have reached: Page Down on
  the first selectable row of the next page, Page Up on the last
  selectable row of the previous one. Both reuse the arrow's page-edge
  path, so a page holding only runtime entities is still not entered.

### Verified in the cloud

- Release build: 0 warnings, 0 errors.
- `Volt.Editor.Tests` 286 passed, 0 failed.
- Headless tests press Page Down and Page Up and check the pager label,
  focus, and selection on the entered pages, and assert the pager is
  laid out outside the `ScrollView` rather than inside its document.

### Not verified here

- No GPU or interactive session. The pager is proven to be outside the
  scroll view in the widget tree and laid out in a short panel, but it
  has not been seen at a real interface scale, and a wheel or drag
  scroll of the document was not replayed against a window.

## Row ends with Home and End (October 2026)

Same branch, on top of `dd29eb1`.

### What changed

- **Home and End.** On a focused query row they move focus and selection
  to the first and last selectable row of the whole result list, turning
  to the page that row lives on rather than stopping at the page edge.
  Both reuse the same row-selection helper the page keys and the arrow's
  edge step now share, so a page holding only runtime entities is never
  entered silently.

### Verified in the cloud

- Release build: 0 warnings, 0 errors.
- `Volt.Editor.Tests` 287 passed, 0 failed.
- A headless test starts in the middle of page one, presses End and
  checks the pager reads the last page with its last row focused and a
  selection made, then presses Home and checks page one with its first
  row focused.

### Not verified here

- No GPU or interactive session, so the keys have only been driven
  through the headless input context, not a real window.

## Reset view and a manual checklist (October 2026)

Same branch, on top of `851845f`.

### What changed

- **Reset.** A **Reset** button beside the sort buttons returns the
  query section to its initial state in one click: the filter clears, the
  sort returns to scene order, every component unchecks, and both the
  results and the remembered query go with them.
- **Checklist.** The editor guide ends with the manual pass over the
  panel that headless tests cannot stand in for. It went in as a
  section because `/docs/*` is ignored, so a new file there would not
  have been tracked.

### Verified in the cloud

- Release build: 0 warnings, 0 errors.
- `Volt.Editor.Tests` 288 passed, 0 failed.
- A headless test runs a filtered, name-sorted query, clicks **Reset**,
  and checks the rows are gone, the filter is empty, nothing is checked,
  scene order is selected, and the captured state holds no remembered
  components.

### Not verified here

- No GPU or interactive session, so the button has not been seen in a
  window. The checklist is the manual pass that covers the rest.

## A reset shortcut and a linked checklist (October 2026)

Same branch, on top of `fde7206`.

### What changed

- **Ctrl+R.** A focused query row resets the query view with `Ctrl+R`,
  the same action as the **Reset** button, which now names the shortcut
  in its tooltip. No editor shortcut binds that combination, so it cannot
  shadow a global command.
- **Contents.** The guide opens with a contents list linking every
  section, including the manual checklist at its end.

### Verified in the cloud

- Release build: 0 warnings, 0 errors.
- `Volt.Editor.Tests` 289 passed, 0 failed.
- A headless test focuses a query row, presses `Ctrl+R`, and checks the
  rows, the filter, and the checks are back to their initial state.
- `tools/tests/test-compare-editor-docs.py` and the guide's screenshot
  freshness check both pass, so the guide's contents list and new section
  left the captured images intact.

### Not verified here

- No GPU or interactive session, so the shortcut was only driven through
  the headless input context, not a real keyboard.

## Key hints and document contents (October 2026)

Same branch, on top of `6715ba8`.

### What changed

- **Key hints.** A muted line under the query rows says what the keys do:
  Up and Down move, Page Up and Page Down change the page, Home and End
  jump to the ends, and Ctrl+R resets the view. It appears with the rows
  and leaves with them, so it costs no space before a query runs.
- **Contents lists.** The UI framework guide and the editor control audit
  had no table of contents either; both now open with one, matching the
  editor guide. The milestone log keeps its date-ordered sections without
  one, because it is read as a history rather than navigated.

### Verified in the cloud

- Release build: 0 warnings, 0 errors.
- `Volt.Editor.Tests` 290 passed, 0 failed.
- A headless test checks the hint is hidden before a query, appears with
  the rows with the expected keys in its text, and goes away when the
  view is reset.

### Not verified here

- No GPU or interactive session, so the hint line has not been seen in a
  window; its presence, visibility, and text are asserted instead.

### Next

- Project-owned gameplay assemblies (Milestone E).

## Query rows, edit safety, and a refresh measurement (October 2026)

Same branch, on top of `12f3dfd`.

### What changed

- **The query list follows the same rule as the rows.** Its matches are
  rebuilt only when the list it shows changes, so a repeated apply with
  the same filter, sort, and matches leaves the rows in place. A query is
  re-run when the world's structure changes, which is when an entity
  appearing or leaving can change what is already listed, rather than on
  every refresh.
- **An edit survives a moving world.** A new test types into a runtime
  position field, then writes a different value straight into the live
  world's column and refreshes the panel: the focused field and its typed
  text survive, and the row is rebuilt with the committed value once the
  edit ends.
- **Renames reach a running query.** The names a query lists were read
  from the runtime world, which is built when Play starts, so renaming an
  object left the listed name stale. The query now resolves each authored
  id through the open scene, and a scene edit marks the query for a
  re-run, so a rename refreshes the list without touching the structure.
- **A tracked benchmark.** `EcsInspectorRefreshBenchmarks` measures the
  two refresh paths, and `tools/benchmark-ecs-refresh.sh` records a run
  under `tools/benchmark-results/ecs-refresh-<date>/` so two builds can be
  compared. The recorded run measured a steady refresh at 8.89 us and a
  rebuilding refresh at 16.06 us, roughly 1.8 times the steady path, so
  the gating removes about 7 us from each of the four refreshes a second.
- **The refresh benchmark is tracked across runs.** A second recording of
  the same build measured a steady refresh at 8.93 us in-process and 8.50 us
  launched, against 8.89 us and 9.05 us in the first run; the rebuilding
  path measured 16.22 us and 15.13 us against 16.06 us and 16.07 us. The
  absolute microseconds moved by up to six percent between runs, while the
  rebuilding-to-steady ratio stayed between 1.78 and 1.82, so the ratio is
  the figure worth tracking. CI records a run on a hosted runner and fails
  only when a mean exceeds twice the committed baseline: a gate that catches
  a collapsed path rather than several percent of drift. The same
  comparison also quotes the two paths against each other with
  `--overhead RebuildingRefresh:SteadyRefresh` and fails past 2.2, which
  leans on the steadier figure: the quotient stayed at 1.78 launched and
  1.81 to 1.82 in-process, so a rebuilding slowdown cannot hide behind a
  machine that moved both means. The comparison is
  `tools/compare-benchmark-runs.py`, which reads the summary table of a
  recorded run, so a saved `BenchmarkRun.txt` is enough to check one.
- **A hosted run is judged against another hosted run.** The benchmark
  job takes the last successful master run's artifact as its baseline
  and falls back to the committed run when there is none, so the means
  are compared within one machine class instead of across two. `--json`
  writes the same comparison for a script to follow over many artifacts,
  `tools/benchmark-trend.py` lines those documents up in the order given,
  optionally narrowed to one method pair, so a sequence of artifacts reads
  as a trend, and the comparator reads a
  downloaded archive or extracted directory directly. The job summary
  names the baseline that was chosen and repeats the table, so a reviewer
  does not have to open the log.
- **The selected entity's header follows a rename.** The header read the
  name captured when Play started, and the rebuild key was built from the
  same text, so a rename changed neither. Both now resolve the authored
  name through the open scene, which is the lookup the query rows use.
- **The guide says when a query re-runs.** The query bullet now states
  that a running query re-runs when entities appear or leave and when an
  authored name changes, and keeps its rows otherwise.

### Verified in the cloud

- Release build: 0 warnings, 0 errors.
- `Volt.Editor.Tests` 300 passed, 0 failed.
- `python3 tools/tests/test-compare-benchmark-runs.py` passes its seventeen
  cases, and the comparator reports the two recorded runs at a 0.94 ratio
  with an unchanged 1.78 rebuilding-to-steady quotient.
- `dotnet format Volt.slnx --verify-no-changes` now passes. The whitespace
  drift in `EditorShell.cs`, `VulkanUiHost.cs` and `TextField.cs` predated
  this work and had been failing the format gate; it is fixed in place.
- `./tools/capture-editor-docs.sh --check` compares all five images.
- The documentation comparator and contents checkers still pass.

### Not verified here

- No GPU or display: no windowed timing, and the query-gating behaviour is
  checked through widget identity rather than a live window session.
- The new CI job has now run on GitHub. On the push that added it, the
  benchmark job passed, recording a run in 106 seconds and comparing it
  with the committed baseline. The build-and-test job passed its build,
  test, both tooling suites, the contents check and the screenshot check,
  then failed only at the formatting step on the drift above. The run on
  the commit that cleared it was fully green: the format check passed, the
  demo ran, and the benchmark job passed with the quotient gate in place,
  so both gates are proven on a hosted runner. The hosted means are not
  recorded here, since only the ratio means the same thing across machines.
- **The gates run before a push.** `tools/pre-push-check.sh` runs the
  formatting check, both tooling suites, the contents check, shell syntax,
  and whitespace in the commits about to leave and the working tree;
  `.githooks/pre-push` calls it once a clone sets `core.hooksPath`. The
  drift that failed the hosted format step would then have failed in the
  terminal instead. CI also keeps its recorded run as an artifact, so a
  hosted machine's means can be compared after the fact rather than only
  its ratio, and the comparator reads an artifact archive or an
  extracted directory directly, so two hosted runs can be quoted against
  each other. `--screenshots` adds the image comparison, the slowest gate
  and the last one that only a hosted runner was checking.

### Next

- Project-owned gameplay assemblies (Milestone E).

## Compact rows and a frame-order test (October 2026)

Same branch, on top of `27adb36`.

### What changed

- **One line for a component with no editor.** A component that has
  neither an editable channel nor readable fields is now a single muted
  line, `MeshRenderer - No inspector formatter; 16 bytes per entity.`,
  instead of an accent heading plus a note that reserved two rows for one
  fact. The note itself no longer repeats the type the row already names,
  and the guide states the rule.
- **Rows rebuild only when the snapshot changes.** The panel refreshes
  four times a second, and rebuilt its rows every time. It now builds a
  key from the text the rows display, so an unchanged world leaves them
  in place; `EcsInspectorRefreshTests` proves a steady tick keeps the same
  row widgets while selecting another entity replaces them.
- **The frame ordering has a test.** `EcsInspectorFrameTests` captures the
  Play Mode panel and asserts that the band holding the archetype rows is
  painted, which only holds while a capture takes one further layout and
  draw after its last update. Dropping that frame leaves the band blank
  and fails this test as well as the image comparison.

### Verified in the cloud

- Release build: 0 warnings, 0 errors.
- `Volt.Editor.Tests` 295 passed, 0 failed, including the refresh and
  capture-frame tests.
- `./tools/capture-editor-docs.sh --check` compares all five images.
- The documentation comparator and contents checkers still pass.

### Not verified here

- No GPU or display: the compact line was reviewed in the headless
  capture, not in a window.

### Next

- Project-owned gameplay assemblies (Milestone E).

## Two ECS figures and a windowed comparison check (October 2026)

Same branch, on top of `337d6ae`.

### What changed

- **The blank rows were a frame-ordering fault.** The ECS panel rebuilds
  its rows from a live snapshot during `Update`, after the frame has
  measured its children, so a frame drawn straight after a rebuild
  painted none of them while still reserving their space. A probe of the
  widget tree showed the rows existed with bounds; a probe rendering the
  panel mid-frame and one frame later showed the difference. A capture
  now takes one further layout and draw after its last update, and the
  component editors and archetype rows paint.
- **A second ECS figure.** `--snapshot-state ecs-query` leaves the
  selection alone and scrolls the panel to its query rows, key hints, and
  pinned pager, so the guide shows both the selection path and the query
  path. `tools/capture-editor-docs.sh` writes `editor-ecs-query.png`, and
  both ECS figures have golden baselines.
- **Windowed captures can be compared.** `tools/compare-editor-docs.py`
  accepts two PNG files as well as two directories, so a capture taken
  from a real window under the Vulkan harness can be checked against a
  committed image with the same per-channel and pixel tolerances.

### Verified in the cloud

- Release build: 0 warnings, 0 errors.
- `Volt.Editor.Tests` 292 passed, 0 failed, including both ECS baselines.
- `./tools/capture-editor-docs.sh --check` compares all five images.
- `python3 tools/tests/test-compare-editor-docs.py` covers the file mode,
  and `python3 tools/tests/test-doc-contents.py` still passes.

### Not verified here

- No GPU or display, so no windowed capture was taken: the file mode is
  covered by synthetic images, not by a real window.

### Next

- Project-owned gameplay assemblies (Milestone E).

## The ECS panel in the guide's screenshots (October 2026)

Same branch, on top of `07e13c5`.

### What changed

- **A fourth documentation capture.** `--snapshot-state ecs` selects the
  starter scene's cube, starts Play Mode, and opens the ECS tab, so the
  panel paints the running world and the runtime entity it resolves that
  selection to, rather than its idle state. The bottom dock is given a
  fifth of the window, which leaves room for the session summary, the
  selected entity, and the pager in one frame.
  `tools/capture-editor-docs.sh` writes it as `editor-ecs.png`, and the
  guide embeds it in the Runtime ECS inspector section.
- **A golden baseline for the preset.** `GoldenSnapshotTests` builds the
  same layout, selection, and remembered query, and compares its capture
  with `tests/Volt.Editor.Tests/Baselines/editor-ecs.png`. A panel that
  stops resolving the selection, loses the session summary, or paints the
  wrong dock proportions now fails the build.
- **Preset support in the snapshot path.** `EditorSnapshot.Capture` and
  the shell's internal constructor accept an `EcsInspectorPanelState`, so
  a headless capture can show a panel that normally needs a click to
  reach its populated state.
- **Contents checks.** `tools/tests/test-doc-contents.py` verifies that
  every contents entry resolves to a heading and every level-two heading
  is listed once; CI runs it. It immediately found two sections missing
  from the editor guide's list, which are now included.

### Verified in the cloud

- Release build: 0 warnings, 0 errors.
- `Volt.Editor.Tests` 291 passed, 0 failed, including the new
  `PlayModeEcsPanel_MatchesItsBaseline`.
- `./tools/capture-editor-docs.sh --check` compares all four committed
  images with fresh captures.
- `python3 tools/tests/test-doc-contents.py` and
  `python3 tools/tests/test-compare-editor-docs.py` pass.

### Not verified here

- No GPU or interactive session: the new image comes from the headless
  snapshot renderer, which draws the same widget tree the window paints
  but not through Vulkan.

### Next

- Project-owned gameplay assemblies (Milestone E).

## Project-owned gameplay (November 2026)

Branch `feat/volt-game-authoring-next`, based on the verified head of
`fix/benchmark-project-discovery` (`c87f155`), which carried the benchmark
isolation, inspector and profiler refresh, and swapchain-recreation work.
Nothing from that branch was redone; this work follows it.

### What changed

- **`Volt.Editor.Core.Gameplay`.** `IGameplayModule` declares what a game
  assembly contributes, `IGameplaySystem` runs once per fixed step,
  `GameplayComponentBinding` maps an authored component onto unmanaged ECS
  storage, and `GameplayRegistrar` collects components, bindings, and
  systems.
- **Loading.** `GameplayAssembly.Load` loads a built assembly into its own
  collectible context while resolving every host-provided assembly to the
  host's copy, so `AuthoredComponent` and `World` keep one identity across
  the boundary. Modules are discovered by reflection; a module that throws
  is reported and skipped; a missing, corrupt, or module-less assembly is
  an ordinary failed result that leaves the caller usable.
- **Runtime.** `RuntimeScene` applies bindings while it builds entities and
  runs systems in registration order after the built-in behaviours, with
  per-system fault isolation: the first failure is reported and that
  system is skipped for the rest of the session.
- **Project workflow.** `GameplayProjectScaffold` writes `Scripts/` with a
  buildable SDK project and a working starter module.
  `GameplayProjectBuilder` compiles with the real SDK, parses located
  diagnostics, and de-duplicates the per-target repeats. `project.volt.json`
  records the optional `gameplay` section.
- **Editor.** Assets → Build Game Code compiles and loads; Play Mode loads
  the built assembly and passes its bindings and systems into the session;
  diagnostics reach the Console. An unchanged scene is reloaded after a
  load so project components become editable types. A dirty scene is never
  reloaded for it.
- **Catalog.** `RegisterOrUpdate` replaces an entry when a rebuild changes
  the CLR type behind a stable id, keeps retired types resolvable so older
  instances still serialize, serializes its own access, and returns a
  snapshot from `All` because a load can change metadata while a panel
  enumerates it.
- **Sample.** `samples/Volt.PlayDemo/SampleGame` now owns game code
  (`playdemo.playerControl` and the system that moves the player) that
  `Volt.PlayDemo` loads the way the editor does, before it reads the scene.
  `Volt.PlayDemo.csproj` excludes `SampleGame/**` so the host's compile
  glob does not absorb the nested project's sources or generated files.

### Verified here

- Release build of the whole solution: 0 warnings, 0 errors.
- `Volt.Editor.Core.Tests`: 83 passed. New cases cover assembly loading,
  registration idempotency, a throwing module, a binding with no
  component, a missing assembly, a corrupt assembly, an assembly with no
  module, load-context collection after unload, component round-tripping
  through a scene, two real SDK builds (success and genuine compiler
  error), scaffold idempotency, manifest round-tripping, diagnostic
  parsing, and the shipped sample running its own system.
- `Volt.Editor.Tests`: 307 passed, including three editor workflow tests
  that build a project, add its component through the same session call
  the Inspector uses, play, and see the project's system move the entity
  while the authored transform stays untouched.
- Full solution run: 3351 passed, 33 skipped, 1 failed.
  `Volt.Gameplay.Tests.Lifecycle.GameplayLifecycleAllocationTests.RecycleWithTags_AfterWarmup_AllocatesNothing`
  failed only in the parallel solution run and passes in isolation; it is
  an allocation-measurement assertion sensitive to other assemblies running
  at the same time, and this work does not touch that code.

### Not verified here

- No GPU and no display server, so no window has shown project game code
  running. The whole workflow is exercised headlessly.
- Windows was not available. The build runs `dotnet build` as a child
  process, which is platform-neutral, but has only been run on Linux.
- Hot reload is not implemented and is not claimed: a rebuild loads a new
  assembly and keeps existing scene data, but nothing watches source files.

### Next

- Show the project's scripts and built assembly in the Project Browser.
- Format a project's bound component values in the ECS panel instead of a
  size-only line, so runtime inspection reaches custom gameplay.
- Asset import (PNG texture or glTF mesh) as the next vertical slice.

## Runtime inspection of project gameplay (November 2026)

Branch `feat/volt-game-authoring-next`, on top of `1241323`.

### What changed

- **Component descriptions.** `GameplayRegistrar.DescribeComponent<T>`
  registers a display name and a projection over a project's unmanaged
  component, backed by the `RuntimeComponentFormatters` registry. The
  runtime inspector has no field names for native storage, so without a
  description the best it could say was the component's name and size.
- **Snapshot.** `RuntimeWorldSnapshot` consults the registry for components
  with no built-in shape. A description that throws becomes a note on that
  one component and leaves the rest of the entity readable; a component
  with no description keeps the size line it always had. Descriptions read
  through the world's copying accessor, so nothing holds a reference into
  chunk storage.
- **Systems view.** `RuntimeWorldSnapshot.Systems` carries each system's
  name and whether it still ticks, and the ECS panel reports the count,
  the names, and a `(skipped)` marker for a system the runtime faulted.
  The line is rebuilt only when the set changes. A world with no game code
  says so rather than showing an empty heading.
- **Unload honesty.** Unloading an assembly now detaches its runtime
  descriptions, which a formatter's closure over a CLR type would
  otherwise keep reachable. The claim that the load context is collected
  was withdrawn: the process-wide component registry records every
  component's CLR type for the life of the process, so an assembly that
  registers components cannot be proven collected. No hot reload is
  claimed.
- **Scaffold and sample** describe their component, so a new project's
  runtime state is readable in Play Mode from the start.

### Verified here

- Release build: 0 warnings, 0 errors.
- `Volt.Editor.Core.Tests`: 88 passed. New cases cover live values through
  a description, a throwing description, a component with no description,
  culture-independent number formatting, and a snapshot that marks the
  faulted fixture system while leaving the healthy one active.
- `Volt.Editor.Tests`: 307 passed, including the editor path that renders
  the snapshot after building a project's game code.
- The `editor-ecs.png` golden baseline was regenerated for the new line and
  reviewed: the panel shows `Systems: none (built-in behaviours only)` for
  a scene without game code. The baseline update mode rewrites every
  baseline, so the four with no visual change were restored from HEAD and
  the suite re-run to prove only one capture genuinely moved.

### Not verified here

- No GPU and no display server; the panel is exercised through the
  headless CPU capture, not a window.
- Windows was not available.

### Next

- List the project's game code in the Project Browser.
- Asset import (PNG texture or glTF mesh) as the next vertical slice.

## Game code in the Project Browser (November 2026)

The previous slice made project-owned systems run and report themselves,
but the files they are written in could not be seen in the editor: the
catalog only scanned `Assets` and `Scenes`, and activating anything but a
scene answered "is not openable yet". A developer could therefore write a
system, build it, watch it run, and still have no way to reach its source
from the browser.

- `ProjectAssetCatalog` also scans `Scripts`, which is where the game
  project lives. `bin` and `obj` are skipped there: they hold what the
  build produced, not what the user wrote, and listing them would bury
  the source tree under generated files.
- Activating a script hands its absolute path to the OS-registered
  application (`EditorSession.OpenProjectFile`), so game code opens in the
  editor the developer already configured instead of in a minimal text
  box. The opener is an injectable delegate, so tests launch no process.
- Failure is ordinary here: a machine with no `.cs` association logs a
  warning and says so in the status line rather than throwing, and a path
  that resolves outside the project is refused with an error. A file
  written since the last catalog refresh is found by refreshing once.
- The asset context menu offers "Open in Editor" for scripts, while
  scenes keep "Open".
- Verified: release build with 0 warnings and 0 errors;
  `Volt.Editor.Core.Tests` 89 passed, including a new catalog test that
  finds `Scripts/Runtime/PlayerMotor.cs` as a script and confirms no
  `Scripts/bin` or `Scripts/obj` entry reaches the assets or folders;
  `Volt.Editor.Tests` 311 passed with four new tests: listing plus
  external open, refusing a path outside the project, reporting a failed
  launch without throwing, and opening a file written after the last
  refresh.
- Three baselines were regenerated and reviewed: `editor-default`,
  `editor-authoring`, and `editor-rotate-world-snap` now show the fixture
  project's `Scripts` folder as a tree row, a third tile, and "3 items".
  The update mode also rewrote `editor-ecs-query.png` with 33 antialias
  pixels of noise, so that one was restored from HEAD.

### Not verified here

- No GPU and no display server; the browser is exercised through the
  headless CPU capture, not a window.
- Windows and macOS file associations were not exercised.

### Next

- Asset import (PNG texture or glTF mesh) as the next vertical slice.

## OBJ mesh import (November 2026)

The Project Browser could show a mesh file, but nothing could draw it: both
renderers resolved identifiers only through `EditorMeshLibrary`, whose
three primitives were compiled in. A scene referencing an exported mesh
therefore drew nothing in the authoring view and fell back to a cube in
Play Mode.

- `ObjMeshLoader` reads Wavefront OBJ: positions and faces, with polygons
  triangulated as a fan, negative indices resolved, and the
  `v/vt/vn` form accepted. Normals, texture coordinates, and materials
  are ignored rather than misread. Malformed input reports the line it
  came from.
- `EditorProject.TryLoadMesh` loads a referenced asset, caching geometry
  against the file's size and write time, so painting does not re-parse
  while an edited file is picked up on the next lookup. A missing,
  unsupported, or broken file returns a diagnostic: a scene naming one
  still opens.
- `ProjectMeshResolver` is the single place that turns an authored
  reference into drawable geometry for both viewports, picking bounds, and
  focus, so an object cannot look one way and pick another. Imported
  vertices carry a baked face shade, because the scene shader has no
  normals and an unshaded mesh reads as a flat silhouette.
- `ViewportMeshInstance` may now carry its own vertices. The CPU
  rasterizer draws them directly; the GPU renderer uploads them on first
  use and re-uploads when the file behind them changes.
- Verified: release build with 0 warnings and 0 errors;
  `Volt.Editor.Core.Tests` 101 passed, including the OBJ parser (quad
  triangulation, negative indices, ignored directives, unreferenced
  vertices excluded from bounds, malformed faces reported) and project
  loading (triangles, cache replacement after an edit, unsupported
  format, broken file, missing asset);
  `Volt.Editor.Tests` 314 passed, including an end-to-end test that writes
  an OBJ into a project, resolves it, and rasterizes it into a viewport
  image with visible coverage. All golden baselines still match, which
  confirms the built-in mesh path is pixel-unchanged.

### Not verified here

- No GPU and no display server: imported geometry is verified through the
  CPU rasterizer; the Vulkan upload path was written and compiled but not
  executed.
- Textures and glTF/GLB are not imported yet, and an imported mesh is not
  listed in the primitive menu (it is assigned through the Mesh Asset
  field, which accepts a project asset).

### Next

- Texture import, which needs a texture pipeline before it can render.
- A viewport check that a picked imported mesh matches its rendered
  silhouette.

## Authored material colour (November 2026)

`MeshRendererData` had a Material field from the first version of the
scene model, but nothing read it: every object drew in its own baked
shading, and an imported mesh could not be coloured at all. A material
reference was therefore a setting that promised an effect and delivered
none.

- `MaterialLoader` reads a small JSON asset (`{ "albedo": "#RRGGBB" }`, or
  three 0..1 numbers). Only the property that changes what is rendered is
  read, so a file cannot promise an effect this build lacks. Malformed
  JSON, a missing albedo, and a bad colour each report the file.
- `EditorProject.TryLoadMaterial` caches the colour against the file's
  size and write time, and `CreateMaterial` writes a default-coloured
  file, naming it against the existing assets. The Assets menu gained
  **New Material** so the workflow starts in the editor.
- `ProjectMeshResolver` applies the material by tinting the baked vertex
  colours, for built-in primitives and imported meshes alike. The scene
  shader already carries one albedo per vertex and no material binding,
  so the authored colour reaches the CPU capture and the GPU viewport
  through the same data, with no second pipeline. An empty,
  `builtin:default`, or unresolvable reference leaves the mesh untinted
  rather than hiding it.
- Verified: release build with 0 warnings and 0 errors;
  `Volt.Editor.Core.Tests` 112 passed, including hex and numeric albedo
  parsing, alpha trimming, malformed input, material creation naming, the
  cached colour after an edit, and refusing a scene as a material;
  `Volt.Editor.Tests` 319 passed, including tinting a built-in mesh,
  tinting an imported mesh while keeping its positions, the fallbacks,
  and a rasterized frame where a red material makes the centre pixel
  red. All golden baselines still match, because the default material
  changes no colour.

### Not verified here

- No GPU and no display server: the tint is verified through the CPU
  rasterizer; the GPU path consumes the same tinted vertices but was not
  executed.
- Textures are still absent: a material sets a flat colour, not an image.

### Next

- Texture import, starting with a PNG decoder and a UV path.
- Listing project materials in the material field's picker, filtered to
  that asset kind.

## Asset actions in the Project Browser (November 2026)

Importing a mesh and colouring it were possible, but only by hand: place
an object, add a Mesh Renderer, type the mesh path, then type the
material path. The browser could see the assets and do nothing with
them, which is the part of an asset pipeline a developer feels every
minute.

- **Place in Scene** on a mesh asset creates one object at the origin,
  named after the file, carrying that mesh and the default material, and
  selects it. The entity and its renderer are a single undo step, and a
  mesh that cannot be read is reported instead of placing an invisible
  object.
- **Assign to Selected** on a material asset applies it to every selected
  object that renders a mesh, leaving objects without a renderer alone.
  A missing or broken material is logged rather than applied.
- Both actions go through `EditorSession`, so they are available to the
  menus and testable without the window; the browser only enables them
  when the asset kind and the scene state make them meaningful.
- Verified: release build with 0 warnings and 0 errors;
  `Volt.Editor.Tests` 324 passed, including placing a mesh as one undo
  step, refusing a broken mesh without changing the scene, refusing a
  scene as a mesh, applying a material to exactly the selected renderers,
  and reporting a missing material.

### Not verified here

- No display server: the menu items are exercised through the session
  calls they invoke, not by opening the window and clicking them.

### Next

- Texture import, starting with a PNG decoder and a UV path.
- Filtering the mesh and material fields' pickers to their own asset
  kinds.

## Game code in the Console (November 2026)

A project's systems could move objects, throw, and be reported when they
faulted, but there was no way for them to say anything. Debugging game
code meant inferring behaviour from a moving cube, and the Console only
ever carried editor messages.

- `RuntimeScene` owns a `RuntimeLog`, so a system writes
  `scene.Log.Info("…")` through the world it already receives. There is
  no static logger, no editor reference, and nothing to configure before
  a world exists, so the same code works in a standalone run.
- The buffer is bounded at 500 messages per world and counts what it
  dropped. A system logging every tick would otherwise grow the editor's
  memory for the length of a session and bury every other message.
- The editor drains what it has not seen into the Console, prefixing game
  messages with `Game:`, keeping warnings as warnings, and reporting a
  dropped-message count once. `OnStart` messages appear when Play begins
  rather than after the first tick, and starting Play again begins empty.
- The scaffolded project and the sample game both announce themselves
  in `OnStart`, so a new project exercises the contract from its first
  Play.
- Verified: release build with 0 warnings and 0 errors;
  `Volt.Editor.Core.Tests` 117 passed, including levels and order, the
  bounded buffer with its dropped count, a system writing in `OnStart`
  and on each tick, and one log per world; `Volt.Editor.Tests` 325
  passed, including a session test that builds a new project's scaffolded
  game code, starts Play, pumps two frames, and finds exactly one
  `Game: Player motor ready` entry.

### Not verified here

- No display server: the Console is exercised through the session log it
  reads, not by opening the panel.

### Next

- Texture import, starting with a PNG decoder and a UV path.
- Filtering the Console by source so game messages can be isolated.

## Project validation (November 2026)

Scenes now reference imported meshes, materials, a startup scene, and an
optional gameplay assembly. A renamed or deleted file leaves the scene
opening and part of it silently missing, and nothing reported that a
project was unsound until someone noticed a hole in the viewport.

- `ProjectValidator` walks the project the way the editor would: it opens
  the manifest, loads the startup scene and every other scene, resolves
  each visible mesh and material reference through the same resolver the
  viewports use, and checks that recorded game code exists and has been
  built. Every problem is collected, so one broken scene does not hide
  the rest.
- Severity distinguishes what breaks rendering from what merely needs
  attention: an unresolvable mesh, unloadable scene, or missing game-code
  project is an error; an unassigned mesh, unreadable material, or
  unbuilt assembly is a warning.
- `Volt.Editor --validate-project <folder>` prints the report and uses the
  exit code as the contract: 0 valid, 1 errors, 2 unusable command. It
  never creates a window or a GPU device, so it runs on a build machine.
- **Assets → Validate Project** runs the same check in the editor and
  writes each issue to the Console at its severity.
- Verified: release build with 0 warnings and 0 errors;
  `Volt.Editor.Tests` 331 passed, including a sound project with an
  imported mesh and material, a missing mesh as an error naming the
  entity, an unreadable material as a warning, a malformed scene reported
  without hiding the startup scene, a folder without a manifest, and
  recorded game code whose project file is gone; the headless command was
  also run against a folder without a project and returned exit code 1
  with the manifest message.

### Not verified here

- No display server: the menu action is exercised through the validator it
  calls, not by opening the window.

### Next

- Texture import, starting with a PNG decoder and a UV path.
- Running validation as part of the editor's own startup check.

## Play checks what it is about to run (November 2026)

Validation could be asked for, but Play still started a scene whose mesh
reference did not resolve: the game ran with a hole in it and nothing said
so unless someone ran the report first.

- `StartPlay` validates the scene it is about to run through
  `ProjectValidator.ValidateScene`, which uses the same resolver the
  viewports use. Errors are written to the Console at error level and the
  status line reads `Play refused: 1 broken reference`; Play Mode never
  starts.
- Warnings alone do not block: an unreadable material still leaves Play
  available, because the object is drawn with its default shading and a
  developer may be part-way through authoring.
- The check is scoped to the open scene, not the whole project, so a
  broken reference elsewhere does not stop a sound scene from running.
- Authoring is untouched by a refusal: the scene, its selection, and the
  broken reference remain so it can be fixed and played again.
- Verified: release build with 0 warnings and 0 errors;
  `Volt.Editor.Tests` 335 passed, including refusing a scene with a
  missing mesh (no runtime, error logged, status set, scene intact), still
  starting when only a material warning applies, starting with built-in
  meshes, and fixing the file then playing successfully.

### Not verified here

- No display server: the toolbar's Play button calls the same session
  method these tests exercise.

### Next

- Texture import, starting with a PNG decoder and a UV path.
- Running the whole-project validation at editor startup.

## Validation when a project opens (November 2026)

Validation existed on demand and before Play, but a developer who opened a
project and started editing could still work for a while before
discovering that a mesh file had been moved.

- `ProjectValidator.Validate(EditorProject)` checks a project that is
  already open, so opening does not re-read the manifest it just loaded.
- `OpenProjectCore` runs it after the startup scene loads, writes every
  issue to the Console at its severity, and adds one summary line. When
  there are errors the status line says so and points at the Console;
  warnings alone leave the status untouched.
- Creating a project skips the check: a project that was just written
  cannot have broken references, and reporting its not-yet-built game code
  would be noise at the one moment everything is known to be new.
- Verified: release build with 0 warnings and 0 errors;
  `Volt.Editor.Tests` 338 passed, including reopening a project with a
  moved mesh (error line naming the file, `Project validation: 1 error`
  summary, error status), reopening a sound project with no error entries
  and no error status, and confirming that creating a project logs no
  validation summary.

### Not verified here

- No display server: the summary is exercised through the session log and
  status the start screen's open path uses.

### Next

- Texture import, starting with a PNG decoder and a UV path.
- Filtering the Console by source so validation and game messages can be
  read apart.

## Console sources (November 2026)

The Console filtered by severity only. Validation warnings, messages from
a running game, and the editor's own actions all arrived at the same
levels, so seeing a game's output meant scrolling past everything else.

- `EditorLogEntry` carries an `EditorLogSource`: editor, validation, or
  game. The source is set where the message is written rather than guessed
  from its text later, so a game that prints something starting with
  "Game:" cannot be confused with the editor's own prefix.
- `EditorSession.Log` gained an optional source, defaulting to editor, so
  existing call sites are unchanged. Runtime log draining tags its entries
  as game (including the dropped-message notice), and both validation
  paths - opening a project and refusing to play - tag theirs as
  validation. The Assets menu's Validate Project action does the same.
- The Console header gained a source dropdown next to the severity
  toggles: All sources, Editor, Validation, or Game. The two filters are
  independent, and the empty state now says a severity or source filter
  may be hiding messages.
- Verified: release build with 0 warnings and 0 errors;
  `Volt.Editor.Tests` 342 passed, including game messages tagged as game,
  a refused Play tagging its reference error as validation, editor
  messages defaulting to editor, and reopening a project with an unbuilt
  assembly producing a validation warning and no validation error.

### Not verified here

- No display server: the dropdown's filtering is exercised through the
  session entries it reads, not by opening the panel and choosing an item.
- The golden baselines were not regenerated; the Console tab is not the
  visible tab in any captured workspace.

### Next

- Texture import, starting with a PNG decoder and a UV path.
- Remembering the chosen Console filters between sessions.

## The Console filter is a remembered setting (November 2026)

The Console could filter by severity and source, but the choice lived in
the panel: it reset when the interface scale rebuilt the chrome, and it
did not survive a restart. A developer who always silences info messages
had to silence them every session.

- `EditorConsoleFilter` is a value - which severities, which source - and
  it lives on `EditorSession` beside the target frame rate, announcing a
  change through the existing settings event. The panel's controls now
  show the session's filter instead of owning it, so a rebuild or a
  project switch cannot leave the chrome and the rows disagreeing.
- `EditorSettings` persists the three severity toggles and the source
  spelling (`all`, `editor`, `validation`, `game`). An unknown or missing
  spelling normalizes to `all`, and a file written before this change
  loads with every message visible - the behaviour those files were
  written against.
- Startup and project adoption both apply the saved filter, so the choice
  follows the project the way the rest of the workspace settings do.
- Verified: release build with 0 warnings and 0 errors;
  `Volt.Editor.Tests` 349 passed, including the severity/source matrix,
  the default showing everything, the session announcing exactly one
  change per distinct value, a save/load round trip, an unknown source
  falling back to `all`, an older file defaulting to showing everything,
  and a round trip through every source spelling.

### Not verified here

- No display server: the panel is exercised through the session value it
  reads, not by toggling the controls in a window.

### Next

- Texture import, starting with a PNG decoder and a UV path.
- Remembering the selected dock tab and scroll position the same way.

## A project runs without the editor (November 2026)

Until now a project could only be run by opening the editor and pressing
Play, which meant a game could not be exercised on a build machine, on a
server, or on a headless box - and the engine's end-to-end path could not
be checked at all without a GPU. `Volt.Editor --run-project <folder>`
closes that gap.

- The command takes the same path Play Mode does: validate the project,
  load the startup scene, use the built gameplay assembly when one
exists, then build a separate runtime world through `PlaySession`.
- It simulates a fixed number of ticks (`--frames`, default 180) instead
  of rendering frames, so the result is reproducible: the same tick
  count, entity count, messages and diagnostics every run.
- It prints the scene and entity count, the game-code assembly, each
  runtime diagnostic and each game-log message, then exits. Zero means
  the scene ran to completion; one means the project could not open, had
  broken references, or its game code did not load; two means the
  command line itself was unusable.
- A project with no built game code is not an error: the run reports it
  as a warning and simulates the scene without project systems, which is
  what pressing Play does with an unbuilt project.
- Stopping Play disposes the runtime world, so the entity count, log and
  diagnostics are captured while it is still alive. The first test run
  caught this as an `ObjectDisposedException` on `World`.
- Verified: release build with 0 warnings and 0 errors; `Volt.Editor.Tests`
  353 passed twice; `Volt.Editor.Core.Tests` 117; full solution 3432
  passed, 33 skipped. The four new tests cover a scaffolded project
  running 30 ticks without game code, a deleted startup scene stopping
  the run with problems and no ticks, a folder without a manifest, and
  the guard that rejects a non-positive tick count. The CLI itself was
  checked here: a folder without a manifest exits 1 with the manifest
  error, and `--frames 0` exits 2.

### Not verified here

- The run uses the CPU only; the windowed renderer is not involved.
- The assembly-loading branch is exercised by the existing gameplay
  loader and Play Mode tests, not by a CLI run against a built project,
  because building project code inside the test suite is slow. Run
  `--run-project` on a real project after **Build Game Code** to cover it
  on your machine.
- No display server, so no windowed player was attempted.

### Next

- Texture import, starting with a PNG decoder and a UV path.
- A windowed player that reuses this runner with the scene renderer.

## PNG decoding (November 2026)

The engine could not read any image format, so every surface was a flat
material colour. `PngDecoder` is the first layer of texture support: it
turns a PNG file into 8-bit RGBA pixels, which is the hard, error-prone
part of importing a texture.

- Supported: 8-bit greyscale, greyscale with alpha, RGB and RGBA, no
  interlacing. Palette, 16-bit and interlaced files are rejected with a
  message that says why, rather than decoded approximately.
- All five PNG scanline filters are implemented, including Paeth. The
  test fixture encodes the same pixels with each filter, so the decoder
  has to reconstruct exactly what was written.
- The compressed data is ordinary zlib, so decoding uses the runtime's
  `ZLibStream` and adds no package dependency.
- Chunk CRCs are deliberately not verified: a corrupt file is caught by
  the zlib stream or by a short scanline, and checking every CRC costs
  more than the failure it detects.
- `PngImage.Sample` clamps coordinates to the edge and uses nearest
  sampling, which is what a material preview needs at any zoom.
- Verified: release build with 0 warnings and 0 errors; 13 decoder tests
  pass inside `Volt.Editor.Core.Tests` (130 total).

### Not verified here

- Nothing renders a decoded image yet: the scene vertex format has no
  texture coordinate, so sampling and the material binding are the next
  slice. Importing a texture does not change any picture today.
- No GPU work was attempted, so the Vulkan sampler and descriptor path
  are untouched.

### Next

- A material `texture` reference and CPU sampling in the software
  viewport.
- A material preview that shows the decoded image in the Inspector.

## OBJ texture coordinates (November 2026)

A decoded PNG could not be applied to anything, because imported
geometry had no texture coordinates. `ObjMeshLoader` now reads `vt`
lines and the `v/vt/vn` corner form.

- Coordinates are stored **per corner**, alongside the position
  indices. A modelling tool routinely gives one shared position a
different coordinate on each side of a seam, and a per-position array
  cannot represent that; a per-corner one can without expanding the
  vertex list.
- Negative indices resolve relative to the coordinates seen so far,
  exactly as vertex and normal indices do.
- A one-component `vt` is legal in OBJ and reads as a zero second
  coordinate. A face with no texture index fills zeros for its own
  corners, so the per-corner list always aligns with the index list.
- A file with no `vt` at all reports no coordinates (`Uvs` empty,
  `HasTextureCoordinates` false) instead of a list of zeros that would
  sample one texel across the whole mesh. The first test run caught that
  distinction: the loader was reporting a mapping where the file had
  none.
- Verified: release build with 0 warnings and 0 errors; the OBJ tests
  are 13 passed, including a quad with four corner coordinates, two
  triangles sharing a position with different seam coordinates,
  negative texture indices, a mixed file where one face omits the
  coordinates another uses, an out-of-range texture index, and the
  no-coordinates case.

### Not verified here

- Nothing samples a texture yet: the scene vertex format and both
  renderers are untouched, so an imported mesh with coordinates still
  draws in its material colour only.

### Next

- A material `texture` reference, validation of the image, and CPU
  sampling in the software viewport.

## Textured materials in the CPU renderer (November 2026)

The pipeline is now closed on the CPU path: a material names a PNG, the
imported mesh carries corner coordinates, and the software viewport
samples the image as it fills each triangle.

- `MaterialAsset` gained an optional `texture` path. It is read as a
  non-empty string and nothing else: an empty string or a number is
  reported instead of silently ignored.
- `EditorProject.TryLoadTexture` decodes the PNG and caches the pixels
  against the file's size and write time, exactly like imported meshes,
  so painting a viewport does not decode the image every frame.
- `ProjectMeshResolver` pairs the image with the mesh's per-corner
  coordinates and hands both to the viewport as one value, so a draw
  cannot sample one mesh's image with another mesh's mapping. The
  material colour still multiplies the texture.
- Textured triangles take a separate path through clipping and fill:
  coordinates are interpolated through the same six clip planes, then
  barycentric weights interpolate them across the triangle. The mapped
  triangles never touch the existing scanline path, so every existing
  golden image is unchanged.
- The interpolation is perspective-correct: each corner's weight is
  scaled by its reciprocal clip W, so a floor's texture converges toward
  the horizon instead of swimming. The first version used plain screen
  weights, which are affine; `InterpolateUv` now owns that math and has
  a test for the classic case where a screen-space midpoint is a quarter
  of the way along the coordinate in 3D.
- OBJ texture coordinates are bottom-up, so the sampler flips V once at
  the sampling site and the decoded image stays a faithful copy.
- Validation now reports a material whose texture cannot be loaded as a
  warning, next to the existing material warning.
- A texture without coordinates (a built-in primitive, or an OBJ with no
  `vt`), a material without a texture, and a missing image all draw
  exactly as before in the material colour instead of disappearing.

### Verified

Release build with 0 warnings and 0 errors. The material tests cover a
kept texture path, an absent one, an empty path, and a non-string value.
`TexturedMaterialTests` check that the resolver pairs a 2x2 red image
with the quad's six corner coordinates, that the rendered centre pixel
is red-dominant, and that a material without an image or with a missing
image resolves untextured but still drawable.

### Not verified here

- Only the software renderer samples textures. The Vulkan scene pipeline
  has one albedo colour per vertex and no sampler or descriptor set, so
  the windowed viewport still draws textured meshes flat. That is the
  next GPU-side slice and it needs a machine with a Vulkan device.
- Sampling is nearest-neighbour with edge clamping and no mipmaps, which
  is right for a review image and not a general texture pipeline.
- The validator's missing-texture warning is covered by a test that sets
  a material on the scaffolded cube; the warning names the image and the
  project stays valid, because the object still draws in its colour.

### Next

- A GPU sampler for the scene pipeline, behind the same material value.
- A material preview in the Inspector.

## Creating a material from a texture (November 2026)

A textured material was only reachable by writing its JSON by hand: the
editor could create a flat material, and it could assign one, but it
could not connect an imported image to a material. The Project Browser's
context menu on a `.png` now offers **Create Material From Texture**.

- The new material is named after the image (`wood.png` becomes
  `Assets/Materials/wood Material.material`) and numbered if that name
  is taken, so creating one never overwrites an existing material.
- Its albedo is white, so the image appears unchanged; a tint applied
  here would silently alter every texture the editor creates.
- The action refuses anything that is not a texture in the project and
  reports it as a warning, rather than writing a material that points at
  a mesh or a scene.
- Creating a material does not assign it: the developer still chooses
  which object shows it, which is the same two-step flow as a flat
  material.

### Verified

Release build with 0 warnings and 0 errors. The core tests create a
material from a texture and read the texture path and white albedo back
out of the file, assert a second creation is numbered rather than
overwriting the first, and assert a non-texture asset is refused. The
session test covers the editor action itself.

### Not verified here

- No display server, so the context menu item was not clicked in a
  window; the session action it calls is covered directly.

### Next

- Creating a texture-less material from the menu could reuse the same
  naming rules.

## Material preview in the Inspector (November 2026)

Selecting a material in the Project Browser now describes it in the
Inspector instead of showing "Nothing selected": the name, the albedo as
both a swatch value and its hex spelling, and one line about the image.

- The hex is the spelling a developer would edit in the file, so a
  mismatch between what the file says and what the preview shows is
  visible rather than plausible.
- A texture line reports the decoded size (`wood.png · 512 × 512`), says
  the material is a flat colour when it has no image, and says why when
  the image cannot be read. An unreadable material still describes
  itself, because the preview is exactly where a broken file should
  announce itself.
- The selection reaches the panel through the session rather than
  through a reference to the Browser, so any panel can react without
  knowing where the selection came from. A multi-selection reports no
  single asset instead of guessing which one to preview.
- A selected object still wins: selecting an entity leaves the component
  inspector unchanged, and the material preview only appears while
  nothing is selected.

### Verified

Release build with 0 warnings and 0 errors. `MaterialPreviewTests` cover
a textured material (name, `#FFFFFF`, `2 × 2` from a decoded image), a
flat material reporting no texture, a missing image starting with
`Missing:`, an unreadable material still describing itself, the null and
non-material cases, and the session announcing one change per distinct
selection.

### Not verified here

- No display server: the empty-state text is driven by `MaterialPreview`
  and asserted directly, but the panel itself was not drawn.
- The preview is text and a hex value; it does not yet render the image
  itself as a thumbnail, which needs the UI's image path.

### Next

- A texture thumbnail in the preview, once the UI can draw decoded
  pixels.

## Renaming an image keeps textured materials working (November 2026)

Scenes store mesh and material references, and the editor already rewrote
them when an asset was renamed. Materials were the gap: a material stores
its image as a plain path inside its own file, so renaming a PNG left
every textured material pointing at a file that no longer existed - with
no indication of why the texture disappeared.

- Renaming a `.png` now repoints every material whose `texture` names
  it, in the same operation that moves the file.
- The file is patched in place rather than re-serialized from the values
  this build understands, so a field added by hand or by a later version
  survives the rewrite. There is a test for exactly that.
- The material cache entry is dropped outright after the patch. A
  rewritten file can keep the same length and even the same write time
  on a coarse filesystem, so relying on the cache key would have shown
  the old path.
- A material that uses a different image is untouched, and renaming a
  mesh does not rewrite materials at all.

### Verified

Release build with 0 warnings and 0 errors. The new core tests rename a
texture and read the updated path back through the material cache, assert
the albedo is preserved, assert a hand-added `note` field survives the
patch, assert an unrelated material keeps its own image, and assert
renaming a mesh leaves materials alone.

### Not verified here

- Renaming a folder that contains images does not yet repoint material
  `texture` paths; scene references already follow folder renames, so
  this is the remaining half of the same rule.
- No display server: the rename dialog itself was not exercised, only
  the project operation it calls.
