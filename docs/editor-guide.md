# VOLT Editor

`Volt.Editor` is a standalone graphical editor built entirely from
`Volt.Ui` widgets and presented by the engine's Vulkan backend. It does
not embed ImGui, Avalonia, WPF, WinForms, or Qt.

## Running

```bash
dotnet run --project src/Volt.Editor
```

Open an existing project folder directly; replace `/tmp/MyGame` with any
absolute path. Without `--project` the editor starts on its start screen
instead:

```bash
dotnet run --project src/Volt.Editor -- --project /tmp/MyGame
```

Optional flags:

| Flag | Effect |
| --- | --- |
| `--validation` | Enables Vulkan validation layers |
| `--project <folder>` | Opens a project with a `project.volt.json` |
| `--frames <count>` | Runs a fixed number of frames, then exits |

The editor requires a working SDL3 video backend and a Vulkan device
with presentation support. It compiles and its logic is unit tested on
Linux, but windowed execution has not been exercised: the build
machine this work was done on has no display server, no `/dev/dri`
node, and no Vulkan driver, so a window cannot be created and no UI
frame has been presented on a GPU. Hardware validation is the job of
the repository's own GPU harness (`tools/test-vulkan-headless.sh` and
the manual `vulkan-gpu` CI job), which documents its runner
prerequisites; see `tools/README.md`.

## First project

The start screen asks for a project folder and a project name. It
creates:

```text
MyGame/
  project.volt.json
  Assets/
  Scenes/
    Main.volt.scene.json
```

The starter scene contains a camera and a cube with the built-in spin
behaviour. `project.volt.json` is versioned and points at the startup
scene. Paths are project-relative and may not escape the project root.

## Layout

```text
Menu bar
Toolbar (Select, Move, Rotate, Scale, Play, Pause, Step, Stop)
+-----------+-----------------------------+------------+
| Hierarchy | Scene / Game tabs           | Inspector  |
|           +-----------------------------+            |
|           | Project / Console / Profiler|            |
+-----------+-----------------------------+------------+
Status bar
```

The menu bar, toolbar, and status bar are fixed-height rows of one
vertical stack, and the workspace between them is the row that declares
`FlexGrow`. That is what keeps the status bar on the bottom edge and
ends the dock area where the status bar begins, at any window size:
a row that fills the space it is offered would otherwise claim the
whole surface and push the rows after it out of the window.
`tests/Volt.Editor.Tests/EditorShellLayoutTests.cs` lays the shell out
headlessly at real window sizes and asserts that chrome placement,
panel containment, paint clipping, and hit testing all agree.

Panels are real `DockHost` panels. Drag splitters to resize, drop a
panel onto another tab group to combine them, and use **Window** to
close or reopen panels. The layout is serialized to
`<project>/.volt/workspace.json` when the editor closes and restored
the next time that project is opened.

## Authoring

- **Hierarchy** lists the authored scene. Select one or more objects,
  rename them, and create, duplicate, or delete them from the context
  menu or the menus.
- **Inspector** edits the selected object's transform and every
  supported component. Values are written through undoable commands;
  the vector, color, numeric, enum, and asset properties all come from
  the component catalog rather than hardcoded editor code.
- **Scene view** supports orbit, pan, and zoom, click selection, and
  translate, rotate, and scale gizmos. Transform gestures are committed
  as one undo step instead of one step per drag frame.
- **Project** browses the project folder through the asset catalog and
  can assign asset references in the inspector.
- **Console** records editor actions and errors.
- **Profiler** reports frame timing and runtime counts while the
  editor is running.

Keyboard shortcuts: `Ctrl+S` save, `Ctrl+Z` undo, `Ctrl+Y` or
`Ctrl+Shift+Z` redo, `Delete` delete selection.

Saving writes a temporary file and moves it into place, so a failed
save cannot truncate the previous scene. Unknown components are kept
as raw JSON instead of being dropped.

## Play Mode

The toolbar's Play button serializes the authored scene, loads that
snapshot as a fresh ECS world, and starts a fixed-step simulation
(1/60 s). Game view shows the running world. **Pause**, **Step**, and
**Stop** control it.

Runtime state is separate from authored state: stopping disposes the
runtime world and the authored scene is not written back. This is
covered by `tests/Volt.Editor.Tests` and
`tests/Volt.Editor.Core.Tests/Runtime/PlaySessionTests.cs`.

## Running a game without the editor

Gameplay data is normal project data. `samples/Volt.PlayDemo` is an
independent executable that:

1. Opens `samples/Volt.PlayDemo/SampleGame` through the same project
   API the editor uses.
2. Loads the startup scene.
3. Starts a `PlaySession`.
4. Presents its own window with `VulkanUiHost` and a Volt.Ui HUD.

```bash
dotnet run --project samples/Volt.PlayDemo
```

Add `--frames N` to bound a run so it exits by itself, and `--validation`
to load the Vulkan validation layer. A run prints the selected GPU, whether
validation is enabled, and `Presented N accepted frames.` on exit; a bounded
run that presents the wrong number of frames exits non-zero.

```bash
dotnet run --project samples/Volt.PlayDemo -- --create /tmp/NewGame
```

`--create` writes only into a folder that does not exist yet or is empty,
so it cannot overwrite work in progress. Running it twice reports that the
folder `already contains files`; open the existing project with
`--project <folder>` instead.

The created project contains a player object with the built-in move
behaviour (`Axis`, `Speed`, `Automatic`) and can be opened, edited, and
saved in the editor before running it again.

The demo's viewport projects runtime ECS positions through a fixed
camera into Volt.Ui draw commands. It demonstrates scene ownership,
fixed-step simulation, input sampling, and independent execution; it is
not yet a mesh-rendering game framework.

## Project structure and assemblies

| Assembly | Responsibility |
| --- | --- |
| `Volt.Ui` | Widget tree, layout, styling, text, input, draw lists, docking |
| `Volt.Ui.Vulkan` | Vulkan UI renderer and window host |
| `Volt.Editor.Core` | Authored scenes, serialization, projects, undo, runtime, play sessions |
| `Volt.Editor` | Editor session, shell, panels, viewports, executable |
| `Volt.Ecs` | Archetype ECS used by runtime worlds |

`Volt.Ui` does not reference `Volt.Ecs` or `Volt.Editor.Core`.

## Current limitations

- Scene and Game views draw engine-authored content as projected UI
  primitives (grid, axes, markers, gizmos). They do not yet sample an
  offscreen mesh render target into the panel.
- Gameplay logic is currently composed from the built-in behaviours
  (`Spin`, `Move`) and authored component data. Loading user-authored
  C# assemblies into a separate runtime is not implemented.
- Floating dock windows, asset thumbnails, and an asset import pipeline
  beyond discovery are not implemented.
- Text entry covers Unicode text and clipboard operations. IME
  candidate windows and complex shaping are not implemented.
- Windows was not available to verify; code paths are platform-neutral
  and use SDL3 and Vulkan, but only Linux builds and tests were run.
- The editor has no automated UI test that opens a native window.
  Session, scene, project, undo, play mode, dock layout, rendering
  geometry, and the whole shell's layout, clipping, and hit testing are
  covered headlessly; only a window on a GPU shows pixels.
- In a window shorter than a panel's minimum content, that panel clips
  its own overflow rather than scrolling it. The strict
  no-geometry-outside-the-window check therefore runs at the window the
  editor opens with.

## Where to look

- `docs/ui-framework.md` — UI programming model and rendering contracts.
- `samples/Volt.UiDemo` — standalone UI application.
- `samples/Volt.PlayDemo` — standalone game execution.
- `tests/Volt.Ui.Tests`, `tests/Volt.Editor.Core.Tests`,
  `tests/Volt.Editor.Tests` — headless behavior tests.
- `benchmarks/Volt.Benchmarks/UiBenchmarks.cs` and
  `tools/benchmark-results/ui-headless-2026-10-27` — measured UI cost.
