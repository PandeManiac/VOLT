# VOLT

**VOLT** is a free and open-source game engine written in C#, with an emphasis on performance, data locality, concurrency, explicit resource ownership, and predictable memory behavior.

VOLT is being developed from the lower levels upward. Memory management, entity storage, job scheduling, platform integration, and graphics resource lifetimes are treated as foundational engineering concerns rather than hidden implementation details.

Performance is measured, not assumed. Optimizations are evaluated through benchmarks, and changes to core systems are backed by automated tests.

> **Development status:** VOLT is experimental and under active development. It is not yet a complete, production-ready game engine. Its Vulkan backend can render and present a shader-driven triangle, but a continuous rendering loop, general-purpose 3D scene rendering, and a complete asset pipeline are not yet implemented.

## Architecture

### Entity-component system

VOLT uses an archetype-based entity-component system (ECS).

Entities are lightweight handles, components are unmanaged value types, and entities sharing the same component signature are stored together in archetypes. Component data resides in contiguous, native-memory chunks so systems can process component columns directly rather than repeatedly looking up individual entities.

The ECS supports component queries, structural migration between archetypes, chunk reuse, and cached migration plans.

Structural changes are coordinated separately from component processing. Parallel chunk execution uses structural freezing to prevent entity creation, destruction, or component-layout changes from invalidating storage while jobs are running.

Structural freezing does **not** automatically make conflicting component reads and writes thread-safe. Those operations still require appropriate coordination.

`World` raises `EntityDestroyed` after a successful destruction, outside the structural lock, so optional subsystems can release per-entity state or stop scheduled work without the core ECS depending on them. With no subscribers the event costs one null check per destruction.

### Jobs and memory

The job scheduler supports parallel batch execution using the .NET ThreadPool and Task Parallel Library. VOLT does not currently maintain a separate pool of custom worker threads.

Core utilities provide native-memory arenas and buffers for workloads that benefit from explicit allocation and lifetime management.

### Simulation timing

`Volt.Core.Timing` provides the deterministic clock an application runs on: one `Update` per frame, zero or more 60 Hz `FixedUpdate` steps, and scheduled simulation work at absolute fixed-step deadlines. Fixed time is accumulated in exact integer units, so a 60 Hz simulation does not drift from repeatedly adding an approximate 1/60 second.

The loop clamps long frames, caps catch-up steps, drops excess simulation debt instead of building a backlog, supports pause and single-step, and exposes an interpolation alpha in `[0, 1)`. Simulation time is scalable from `0x` (frozen) to `4x` through an exact rational factor with carried fractional remainders, so slow motion and fast-forward do not drift or change the fixed timestep. Scheduled callbacks run synchronously on the game thread, never through a timer or a hidden task. The subsystem has no SDL, Vulkan, ECS, or job-scheduler dependency, and its steady-state frames allocate nothing.

`Volt.Rendering.Transforms` builds on that loop to interpolate real ECS transform components: `TransformHistory` captures the committed pose after every fixed step, `InterpolatedRenderExtractor` derives interpolated model matrices for rendering, and `InterpolationWeight` keeps a paused game showing its latest committed pose instead of snapping back one step. History drops entities that stop being renderable, so an entity that disappears and returns starts from a held pose instead of sweeping from its stale one. The ECS components remain the only authoritative simulation state.

See [src/Volt.Core/Timing/README.md](src/Volt.Core/Timing/README.md) for the execution order, frequency semantics, missed-deadline policy, and time-scale precision, and [src/Volt.Rendering/Transforms/README.md](src/Volt.Rendering/Transforms/README.md) for the interpolation and history rules.

### Gameplay

`Volt.Gameplay` is an optional assembly for Unreal-style gameplay systems. It currently implements hierarchical gameplay tags: interned four-byte identities, an append-only registry with cached ancestry, immutable tag sets for shared definitions, a mutable authoring container, nested AND/OR/NOT tag queries, and reference-counted per-entity tag storage integrated with entity lifetime. Matching performs no string work and allocates nothing once warmed up; containers of 40 tags resolve exact and hierarchical lookups in a few nanoseconds, and entity tag lookup is flat in world size.

It also implements gameplay attributes with registered definitions, base and current values, Unreal-compatible modifier aggregation (`AddBase`, additive and compound multipliers, additive divisors, final add, and override), definition-driven clamping, cached evaluation, and change notifications. An attribute read is a cached binary search: about 2.3 ns regardless of world size or active modifiers.

Gameplay effects are implemented on top of those stores: instant, duration, infinite, and periodic execution, reference-counted granted tags, per-source and per-target stacking with limits and refresh policies, application, ongoing and removal tag requirements, inhibition, and deterministic expiry and ticking from a single due-time heap. Idle effects cost one comparison per simulated step regardless of how many are active, and applying or removing one allocates nothing once warmed up.

Abilities are implemented on top of effects: definitions with owner and target tag requirements, blocking by active abilities, instant costs checked for affordability, cooldowns that are the tags their effect grants, cancellation by tag or by another ability's asset tags, and grants that are released when their owner is destroyed. Activation is a status, not an exception; a rejected attempt touches nothing. Every ability operation is flat in world size, and a warmed-up activation allocates nothing.

Gameplay events are tag-indexed values with a source, a target, and a typed payload. Dispatch visits the exact tag, its ancestors for hierarchical listeners, and the wildcard bucket, so unrelated subscriptions are never scanned; scoped subscriptions are removed when their owner is destroyed, and reentrant handlers are captured and revalidated rather than allowed to invalidate the iteration. Ability tasks are pooled, generational records owned by an activation token: duration waits use a deadline heap, event waits use the event index, and tag waits use a per-owner index updated from tag changes. Plans compose those waits and actions as data, and an ability's tasks are released before its end notification. One hundred thousand idle tasks cost the same as one, and warmed-up task and event paths allocate nothing. Their behavior, Unreal references, and intentional differences are tracked in [the gameplay specification](src/Volt.Gameplay/GameplaySpec.md).

### Platform and graphics

Platform integration uses SDL3 for native windows, events, and Vulkan-compatible presentation surfaces.

VOLT includes an experimental Vulkan graphics backend built with Silk.NET. The implemented path covers instance and device creation, GPU selection, presentation surfaces, swapchains and image views, command buffers, synchronization, shader modules, graphics pipelines, render passes, framebuffers, and swapchain presentation.

The Vulkan surface demo renders a color-interpolated triangle from vertex and fragment shaders. Its vertices are generated by the vertex shader; the demo does not yet require vertex or index buffers.

This is currently a **single-frame demonstration**, not a continuous renderer. The window remains responsive while the result is displayed briefly. Resize-driven swapchain recreation, general-purpose geometry rendering, and a complete scene-rendering workflow remain future work.

See the [Vulkan surface demo README](samples/Volt.VulkanSurfaceDemo/README.md) for sample-specific setup and limitations.

### Projects

| Project | Purpose |
| --- | --- |
| `Volt.Core` | Native memory arenas and buffers, and the deterministic game-loop timing foundation |
| `Volt.Ecs` | Entities, components, archetypes, queries, and chunk storage |
| `Volt.Gameplay` | Optional gameplay systems: hierarchical tags, attributes, effects, abilities, events, and tasks |
| `Volt.Jobs` | Job scheduling and parallel batch execution |
| `Volt.Graphics` | Graphics device abstractions |
| `Volt.Graphics.Vulkan` | Vulkan graphics backend and native resource management |
| `Volt.Platform` | SDL3 windowing, events, and platform integration |
| `Volt.Rendering` | Camera and extraction abstractions, render components, and interpolated transform history |
| `Volt.*.Tests` | Automated tests for the corresponding engine subsystems |
| `Volt.Benchmarks` | BenchmarkDotNet performance benchmarks |

The `samples/` directory contains focused demonstrations of platform, Vulkan, and gameplay functionality. Samples are integration and diagnostic tools, not yet a public game-creation workflow.

## Building

VOLT targets **.NET 10**. Install the .NET 10 SDK before building.

Clone the repository:

```bash
git clone https://github.com/PandeManiac/VOLT.git
cd VOLT
```

Build the solution:

```bash
dotnet build Volt.slnx -c Release
```

Run the tests:

```bash
dotnet test Volt.slnx -c Release
```

### Vulkan triangle demo

The Vulkan surface demo requires a working Vulkan driver and the native SDL3 dependencies supplied or required by the platform packages. Its GLSL shaders are compiled to SPIR-V using `glslangValidator`.

On Arch Linux, install the shader compiler:

```bash
sudo pacman -S --needed glslang
```

Run the demo on Wayland:

```bash
SDL_VIDEODRIVER=wayland dotnet run \
  --project samples/Volt.VulkanSurfaceDemo \
  -c Release -- --validation
```

Or on X11:

```bash
SDL_VIDEODRIVER=x11 dotnet run \
  --project samples/Volt.VulkanSurfaceDemo \
  -c Release -- --validation
```

The `--validation` option requests the Vulkan validation layer and requires `VK_LAYER_KHRONOS_validation` to be installed. Omit the option to run without validation.

The demo displays a color-interpolated triangle against a dark background for a few seconds, then releases its resources and exits. It does not yet run a continuous rendering loop.

### Benchmarks

VOLT uses [BenchmarkDotNet](https://github.com/dotnet/BenchmarkDotNet) to measure performance-sensitive operations.

The benchmark suite includes native memory operations, ECS iteration, entity access, structural changes, chunk availability, fragmentation, and archetype migration, plus empty, fixed-step, idle-scheduled, and due-scheduled game-loop frames, time-scaled frames at `1.0x`, `0.5x`, and `2.0x`, the separate costs of capturing transform history, interpolating poses, extracting interpolated render instances, and capturing under entity churn, gameplay tag matching, query evaluation, authoring, name resolution, effect application, stacking, idle stepping, periodic ticking, ability granting, activation, blocking, input activation, cancellation, and behavior from 1 to 256 grants on one owner, plus gameplay event dispatch with zero, relevant, irrelevant, and hierarchical listeners, single-task creation, completion, and cancellation, idle task populations up to 100,000, batch expiry, batch cancellation, ability ending with pending tasks, and owner destruction with pending tasks.

Benchmark results are intended to inform implementation decisions, not merely to maximize reported throughput. Results should be evaluated alongside correctness, memory consumption, and workload characteristics.

## Engineering Principles

VOLT development follows several priorities:

- **Correctness before optimization.** Performance improvements must preserve expected behavior.
- **Data-oriented storage.** Prefer contiguous layouts and predictable memory access where appropriate.
- **Explicit ownership.** Native allocations, Vulkan resources, entity lifetimes, and structural changes have defined validity rules.
- **Measured performance.** Use benchmarks and profiling to justify optimizations.
- **Controlled concurrency.** Parallel execution must account for storage lifetimes, synchronization, and conflicting access.
- **Incremental verification.** Test individual subsystems and validate their integration on real hardware.
- **Maintainable implementation.** Added complexity should have a demonstrable benefit.

The goal is not to replace every facility provided by .NET. Existing runtime primitives are used when appropriate, and lower-level alternatives are considered when measurements justify them.

## Contributing

Contributions are welcome.

Changes to performance-sensitive code should include relevant tests and, when appropriate, benchmark results. Improvements should be assessed for correctness, memory usage, implementation complexity, and measured performance.

Bug reports, reproducible test cases, documentation improvements, and performance investigations are also valuable.

VOLT is evolving quickly. Before undertaking a large feature, reviewing the current source and discussing its intended scope is encouraged.

## License

VOLT is free and open-source software licensed under the **GNU Lesser General Public License v3.0 only (`LGPL-3.0-only`)**.

The LGPL is intended to keep distributed modifications to the LGPL-covered engine available under the applicable license terms while permitting developers to create and distribute proprietary or open-source games that use VOLT, subject to LGPL compliance requirements. Those requirements can include license notices, access to covered source code, and the ability to replace or relink the covered library, depending on how software is distributed.

The license permits charging for copies or distribution. Nevertheless, freely accessible redistribution and contributions are encouraged.

The complete license terms are provided in [LICENSE](LICENSE) (GNU GPLv3) and [COPYING.LESSER](COPYING.LESSER) (the LGPLv3 additional permissions). Both documents apply together; the presence of the GPLv3 text does not change VOLT's stated `LGPL-3.0-only` license.
