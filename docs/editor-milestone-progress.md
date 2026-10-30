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
