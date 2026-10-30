# Editor control inventory

Every visible control in the editor is listed with its state. The
inventory exists so a later change cannot quietly leave a control that
looks usable but edits nothing.

## Contents

- [How each entry was checked](#how-each-entry-was-checked)
- [Menus](#menus)
- [Toolbar](#toolbar)
- [Hierarchy](#hierarchy)
- [Inspector](#inspector)
- [Scene viewport](#scene-viewport)
- [Game viewport](#game-viewport)
- [Project browser](#project-browser)
- [Console and Profiler](#console-and-profiler)
- [ECS inspector](#ecs-inspector)
- [Docking](#docking)
- [Known intentional limitations](#known-intentional-limitations)

## How each entry was checked

- The action is wired in `EditorShell` (menus, toolbar) or in the
  owning panel, and it changes authored state through `EditorSession`,
  never only through widget state.
- The behaviour is covered by a headless test in `tests/Volt.Editor.Tests`
  or `tests/Volt.Editor.Core.Tests`.
- The control appears in the headless snapshots produced by
  `dotnet run --project src/Volt.Editor -- --snapshot <file>
  --snapshot-state <workspace|authoring|start>`.

A command that cannot run is disabled rather than silently inert: the
menu refreshes its enabled state from session state on every frame
(`RefreshCommandStates`), which
`EditorCommandAuditTests.MenuCommands_WithNoProject_AreDisabledRatherThanInert`
pins.

## Menus

| Menu | Controls | State |
| --- | --- | --- |
| File | New Project, Open Project, New Scene, Save Scene, Close Project, Exit | Working; scene commands disabled without a project or scene |
| Edit | Undo, Redo, Cut, Copy, Paste, Duplicate, Delete, Select All, Select None, Invert Selection, Preferences | Working; each follows selection, history, and clipboard state |
| Assets | Refresh Asset Catalog, New Scene | Working; disabled without a project |
| GameObject | Create Empty, Cube, Sphere, Plane, Camera, Light | Working; creates real entities and selects them |
| Component | Add <component> for every catalog entry | Working; disabled when the selection already has that component |
| Window | Every panel toggle, Reset Layout, interface scales, Start Screen | Working; toggles dock panels and persists the workspace |
| Help | About, Show Documentation, Keyboard Shortcuts | Working; writes to the Console |

## Toolbar

| Control | State |
| --- | --- |
| Select, Translate, Rotate, Scale | Working; sets the active tool used by both the viewport toolbar and the gizmo |
| Local / World | Working; changes gizmo orientation and manipulation mathematics (`GizmoDragTests.LocalMode_*`, `WorldMode_*`) |
| Snapping toggle and step field | Working; steps the world delta, covered by `Snapping_StepsTheWorldDelta` |
| Play, Pause, Step, Stop | Working; drives `PlaySession`, including pause, single step, and stop restoring the authored scene |
| Interface scale | Working; rebuilds the interface at the chosen scale and saves it |

## Hierarchy

Creation, deletion, duplication, rename (F2 and context menu),
reparenting by dragging, multi-selection with modifiers, search, and
context menus are implemented and covered by `SelectionCommandsTests`,
`SelectionDeleteTests`, `SelectionDuplicateTests`,
`HierarchyReparentTests`, and `EditorChromeTests`. Selection is one
authoritative set shared with the viewport and the Inspector.

## Inspector

Transform editing (position, rotation, scale), mesh and material
references, visibility, and component properties are editable, with text
fields that commit on Enter and revert on Escape. Add Component offers
only components the selection does not already have, and removing,
enabling, and disabling components works. Inspector construction and
menu wiring are covered by `EditorChromeTests`, the light and material
tests, and the scene-document tests in `Volt.Editor.Core.Tests`.

## Scene viewport

| Control | State |
| --- | --- |
| RMB free-look | Working; native relative mouse mode with cursor hiding, relative motion consumed once per frame, and a fallback when the native request is refused |
| WASD / QE / Shift fly | Working while the look is captured |
| MMB pan, wheel dolly | Working |
| F focus selection | Working |
| LMB select, box select | Working; gizmo handles take priority over entity picking |
| Translation, rotation, scale handles | Working; ray-based axis and plane solves with a refusal for near-parallel views |
| Snap toggle and step | Working |
| Orientation compass | Working; snaps the camera to an axis view at the current distance |
| Viewport toolbar | Working; mirrors the tool and transform-space controls for the active viewport |
| Overlay hint line | Working; describes the current bindings |

## Game viewport

Game tab, runtime camera, Play Mode input routing, pause, and step are
implemented. The Game view renders the play session's camera rather than
the editor camera.

## Project browser

Folders, list and tile views, search, multi-selection with Ctrl/Shift
and Shift+arrow, keyboard navigation, rename, delete (single and bulk),
duplicate (single and bulk), folder creation, folder rename, context
menus, and revealing the current scene are implemented. The browser keeps
every selected asset across grid/list switches and interface-scale
changes and reports the count in the details line.

Renaming a folder moves its contents and rewrites asset references in
scenes, including unknown component payloads, and repoints the startup
scene path, so a rename cannot orphan authored data. That path is covered
by `EditorProjectTests.RenameFolder_*` and
`EditorChromeTests.ProjectBrowser_RenamesAFolderFromItsTileMenu`.

## Console and Profiler

The console records editor log entries with severity and can be cleared.
The profiler draws real frame-time and managed-memory timelines from
`FrameTimeline` and `MemoryTimeline`, and reports live managed memory.

## ECS inspector

The ECS panel reports the runtime world's entity and archetype counts,
the selected object's real ECS handle and component values, measured
chunk storage per archetype, and a component query that runs through
`World.Query`. It refreshes on a timer while playing or paused.
Position, scale, velocity, and angular velocity can be edited in place.
Each edit writes only the live ECS world, is discarded on Stop, and
never reaches the authored scene or its undo history. The
**Apply to Scene** button beside a position or scale row copies one
live value into the authored transform as one undo step, and
**Apply Transform to Scene** copies both together as one step. These
are the only runtime-to-scene writes, and they are explicit. Each
editable row shows the authored value and flags it when it differs from
the runtime value, and the section header lists the diverged channels.
Queries list the matched entities eight at a time with their handles,
authored names, and archetypes; **Prev** and **Next** page through the
list and a counter reports `Page N of M`. Clicking a row selects its
authored object, and runtime-only rows are disabled because they have
no authored object. A name filter narrows the list, and **Scene
order**, **Name**, and **Archetype** buttons reorder it.
The pager row is pinned below the scrolling document instead of
scrolling with it. **Copy Results** writes the filtered list to the
clipboard as one line per entity. The arrow keys move focus and
selection between rows and step onto the next or previous page from the
edge of one, `Page Up` and `Page Down` jump a whole page at a time, and
`Home` and `End` reach the ends of the whole list, and **Reset**, or
`Ctrl+R` on a focused row, clears the filter, the sort, and the checked
components. A hint line under the rows lists those keys.
The filter,
the sort, and the checked components are part of the panel's captured
state, so they come back after an interface-scale rebuild or reopening
the panel, and the remembered query runs again when Play Mode builds a
world.

## Docking

Panels can be switched, closed, reopened from the Window menu, and
resized. The layout, the open/closed state, and the interface scale are
saved per project and restored on launch. Reset Layout restores the
default workspace.

## Known intentional limitations

These are documented in `docs/editor-guide.md` and are not hidden
controls; no visible control promises them:

- No floating dock windows, asset thumbnails, or asset import pipeline
  beyond discovery.
- No custom C# gameplay assembly loading at runtime; gameplay is
  composed from built-in behaviours and authored component data.
- The ECS panel edits only components the runtime entity already has,
  and cannot add or remove them. It cannot edit rotation. Query results can be
  paged, filtered, sorted, selected, and copied as text, but cannot be exported
  as a file. Only position and scale can be copied back to the scene;
  velocity and angular velocity are runtime-only.
- No IME candidate window or complex text shaping.
- Gameplay and scene rendering use a single pass with no shadows or
  post-processing.
