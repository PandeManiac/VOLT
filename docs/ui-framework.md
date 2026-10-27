# Volt.Ui

Volt.Ui is VOLT's native, retained-mode user interface framework. It
does not depend on Volt.Ecs, a graphics API, SDL, or the editor. The
editor is one consumer of it; games are expected to be another.

## Design in one page

- **Retained widget tree.** Each widget owns its children, its layout
  parameters, and its lifetime. There is no per-frame rebuild of the
  tree and no widget is an ECS entity.
- **Measure and arrange.** Containers measure children against
  available space and then arrange them into rectangles. Layout is
  invalidated rather than recomputed blindly; a clean subtree is
  measured from its cached desired size.
- **Draw list.** Painting appends immutable commands (rectangles,
  rounded rectangles, borders, lines, images, text) with an explicit
  clip rectangle. Widgets never call a graphics API.
- **Batching.** `UiGeometry` converts a completed draw list into
  vertices, indices, and batches that break only on scissor or texture
  changes. It is pure CPU code and is unit tested headlessly.
- **Theming.** Colors, metrics, and font instances come from
  `UiTheme`. Widgets never hardcode a palette.

Input routing is documented on `UiContext`: pointer events bubble from
the hit widget to the root; key events tunnel from the root to the
focused widget and then bubble back. Returning `true` stops
propagation. Handlers may remove widgets or change focus safely.

## Minimal headless example

```csharp
using Volt.Ui;
using Volt.Ui.Rendering;
using Volt.Ui.Widgets;

using UiContext ui = new();

StackPanel panel = new()
{
    Spacing = 8f,
    Padding = new UiThickness(16f)
};

panel.Add(new Label("Hello, VOLT")
{
    Font = ui.Theme.Fonts.Heading
});

Button button = new("Click me");
button.Clicked += clicked => clicked.Text = "Clicked";
panel.Add(button);

ui.Add(panel);

ui.BeginFrame(
    timeSeconds: 0,
    surface: new UiSize(800f, 600f),
    pointerPosition: new UiPoint(0f, 0f));

UiGeometry geometry = new();
geometry.Build(ui.Draw(), scale: 1f);

// geometry.Vertices, geometry.Indices, and geometry.Batches are ready
// for any backend that can draw textured triangles with scissor tests.
```

The same code runs in a window: `VulkanUiHost` owns the SDL window,
Vulkan device, swapchain, input translation, and presentation, and the
application supplies a per-frame callback. See
`samples/Volt.UiDemo` for a complete standalone program that uses no
editor code.

## Widget lifetime

- `Widget.Add` attaches a child; `Remove` detaches and disposes it.
- Detaching a widget immediately clears focus, pointer capture,
  hover, and popup references that point into the removed subtree.
  Removing a widget from inside its own event handler is supported.
- A widget cannot be attached to two parents. Reparenting is remove
  followed by add.
- `Dispose` releases the widget subtree; a `UiContext` disposes its
  root when it is disposed.
- Widget identity is a per-context integer assigned on attach, not an
  ECS entity id.

## Layout

| Property | Meaning |
| --- | --- |
| `Width`, `Height` | `UiLength.Auto`, points, or a percentage |
| `MinWidth`, `MinHeight`, `MaxWidth`, `MaxHeight` | Hard constraints |
| `Margin`, `Padding` | Outer and inner `UiThickness` |
| `FlexGrow`, `FlexShrink` | Distribution of leftover or deficit space in a stack |
| `HorizontalAlignment`, `VerticalAlignment` | Placement inside the arranged slot |
| `ClipToBounds` | Nested clipping, reflected in the draw list |

Containers: `StackPanel` (horizontal or vertical), `Panel`
(single-child host), `CanvasPanel` (explicit slots), `ScrollView`,
`Splitter`, and the docking composites described below.

Invalidation entry points are `InvalidateMeasure`, `InvalidateArrange`,
`InvalidateVisual`, and `InvalidateLayoutTree`. Changing a property
that affects layout already invalidates the affected widget.

High-DPI support is a theme concern: `UiContext.SetDpiScale` rebuilds
the theme at a new scale and rasterizes glyphs at physical resolution,
while layout stays in logical units. `UiGeometry.Build(drawList,
scale)` converts the completed frame to physical pixels.

## Text and fonts

`UiFont` rasterizes TrueType glyphs with an R8 coverage atlas and
supports full Unicode scalar values, kerning, measurement, wrapping,
caret positions, and hit testing. Two embedded faces (IBM Plex Sans
and JetBrains Mono, both SIL OFL 1.1) are used by the default theme.

Implemented limitations, stated honestly:

- No complex shaping, ligatures, or glyph substitution.
- No bidirectional reordering.
- Combining marks receive zero advance but are not repositioned.
- IME composition text can be supplied with `UiContext.SetComposition`
  and committed text arrives through `TextInput`, but no candidate
  window is drawn by VOLT.

## Controls

Labels, buttons, toggle buttons, check boxes, radio buttons, text
fields, numeric fields, sliders, drop-downs, progress bars, separators,
spacers, scroll views, panels, splitters, tabs, tree views, menu bars,
popups, tooltips, color and vector fields, and a data-driven
`PropertyGrid` are implemented. `PropertyGrid` consumes cached
`UiProperty` metadata, not per-frame reflection.

`Volt.Ui` does not reference Volt.Ecs. An editor adapter can inspect
ECS components through ordinary public APIs and expose them as
properties without making the UI framework depend on the ECS.

## Docking

Docking is layered on top of the widget system, not built into every
widget.

```csharp
using Volt.Ui.Docking;

DockLayout layout = DockLayout.CreateEditorDefault();
DockHost dock = new(layout, panelId => CreateContentFor(panelId));
```

`DockLayout` owns panel identity and the split/tab tree, supports
open, close, activate, and move operations, and serializes to versioned
JSON with `Serialize` and `Deserialize`. `DockLayout.CreateEditorDefault`
returns the layout used by the editor, whose panel ids are `hierarchy`,
`scene`, `game`, `inspector`, `project`, `console`, and `profiler`.
The editor persists this JSON in `<project>/.volt/workspace.json` and
restores it on the next launch.

Floating panel windows are not implemented; panels are rearranged
inside the dock host.

## Rendering integration

`Volt.Ui.Vulkan` contains two layers:

1. `VulkanUiRenderer` owns the pipeline, sampler, font atlas texture
   array, per-frame host-visible vertex and index buffers, and render
   pass for a swapchain generation. It issues one draw per batch.
   Font atlases are uploaded only when their revision changes.
2. `VulkanUiHost` owns the window, instance, surface, device, swapchain
   recreation, input translation, and the frame loop. It exposes
   `Ui`, `DeltaSeconds`, `LogicalSize`, `DpiScale`, and
   `RequestExit`, and the application supplies one update callback.

The host requires only the window-system extensions and
`VK_KHR_swapchain`, plus a presentable surface. Presentation fences
from `VK_KHR_swapchain_maintenance1` are enabled when the platform
exposes them, and each swapchain generation is otherwise retired
through an explicit device idle, so a GPU without that extension
still runs the UI. `PresentationFencesEnabled` reports which path is
in use.

A different backend only needs to consume `UiDrawList` or
`UiGeometry`; neither contains API-specific types. The UI framework
itself contains no Vulkan reference.

## Testing

`tests/Volt.Ui.Tests` drives the real widget tree without a window:
layout, focus, pointer capture, safe removal, text editing, scrolling,
dock serialization, and geometry batching. Hardware-dependent tests
are deliberately separate from these.

## Measured performance

`benchmarks/Volt.Benchmarks/UiBenchmarks.cs` measures the CPU half of
the UI against headless contexts. There is no window and no GPU in
these numbers; renderer cost is measured separately.

```
dotnet run --project benchmarks/Volt.Benchmarks -c Release -- \
  --filter '*UiBenchmarks*' --job short
```

A recorded run lives in
`tools/benchmark-results/ui-headless-2026-10-27/`. Its results on a
shared cloud VM, from a short job, were:

| Operation | Mean | Allocated |
| --- | --- | --- |
| Widget create, attach, remove, dispose (64 per invocation) | 8.9 µs | 29.7 KB |
| Layout, 24-level nested tree | 0.55 µs | 0 |
| Layout, 256 rows of a label and a button | 7.4 µs | 0 |
| Steady-state frame, same tree | 39 µs | 0 |
| Frame after a surface resize, same tree | 264 µs | 339 KB |
| Hit test in that tree | 7.3 µs | 0 |
| Tree view with 5000 visible rows, steady frame | 1.9 µs | 0 |
| Tree view, structural change plus the frame that flattens it | 122 µs | 0 |
| Inspector with 256 properties, values changing | 65 µs | 15 KB |
| Inspector with 256 properties, values unchanged | 38 µs | 0 |
| Dock layout serialize / deserialize | 7.2 µs / 10.9 µs | 6.8 KB |
| Dock rebuild plus frame after activating a panel | 13 µs | 30 KB |

What these numbers support, and what they do not:

- A steady-state frame on an unchanged 512-widget tree allocates
  nothing. The tree view paints 5000 rows in under 2 µs because it
  paints only the rows in the viewport.
- An inspector refresh that observes unchanged values allocates
  nothing; numeric and vector editors leave text they already display
  alone. Values that do change still reformat, and a drag-frames
  inspector costs about 15 KB a frame in short-lived strings.
- A resize frame reflows text for the whole tree, which is the reason
  it allocates. It happens once per resize, not per frame, and the
  frames that follow it are the steady-state case above. Reusing
  wrapped text across width changes is a known optimization.
- A structural change to the tree view costs about 23 ns per visible
  row to flatten. This is linear work on a deliberate structural
  edit, not per-frame work.
- These are short-job measurements on shared hardware without a GPU.
  They are a baseline for detecting regressions, not a promise about
  any particular machine, and they say nothing about rendering.

## Current limitations

- The editor's scene and game viewports draw grid, axes, entity
  markers, and transform gizmos into Volt.Ui drawing commands. They do
  not yet composite an offscreen 3D mesh render target.
- Text shaping, bidirectional text, and IME candidate windows are
  documented above.
- Icons are drawn procedurally (glyphs and rectangles); no icon font
  or image asset pipeline is wired into the theme yet.
- `UiContext` consumes one pointer. Multi-touch and pen input are not
  part of the current input contract.
