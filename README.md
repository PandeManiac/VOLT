# Volt

**Volt** is a free and open-source game engine written in C#, built with an emphasis on performance, data locality, concurrency, and predictable memory behavior.

The engine is developed from the lower levels upward. Rather than treating memory management, entity storage, and job scheduling as implementation details, Volt makes them foundational parts of its architecture.

Performance is measured, not assumed. Optimizations are evaluated through benchmarks, and changes to core systems are backed by tests.

## Architecture

Volt is built around an archetype-based entity-component system (ECS).

Entities are lightweight handles, components are unmanaged value types, and entities sharing the same component signature are stored together in archetypes. Component data resides in contiguous, native-memory chunks, allowing systems to process component columns directly instead of repeatedly looking up individual entities.

The ECS supports component queries, structural migration between archetypes, chunk reuse, and cached migration plans.

Structural changes are coordinated separately from component processing. Parallel chunk execution uses structural freezing to prevent entity creation, destruction, or component-layout changes from invalidating storage while jobs are running.

Structural freezing does not automatically make conflicting component reads and writes thread-safe. Those operations still require appropriate coordination.

### Projects

| Project | Purpose |
|---|---|
| `Volt.Core` | Native memory arenas and buffers |
| `Volt.Ecs` | Entities, components, archetypes, queries, and chunk storage |
| `Volt.Jobs` | Job scheduling and parallel batch execution |
| `Volt.Core.Tests` | Tests for native memory utilities |
| `Volt.Ecs.Tests` | Tests for ECS storage, queries, and structural operations |
| `Volt.Jobs.Tests` | Tests for job scheduling and parallel execution |
| `Volt.Benchmarks` | BenchmarkDotNet performance benchmarks |

The job scheduler currently uses the .NET ThreadPool and Task Parallel Library. Volt does not maintain a separate pool of custom worker threads.

## Building

Volt targets **.NET 10**.

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

### Benchmarks

Volt uses [BenchmarkDotNet](https://github.com/dotnet/BenchmarkDotNet) to measure performance-sensitive operations.

The benchmark suite includes native memory operations, ECS iteration, entity access, structural changes, chunk availability, fragmentation, and archetype migration.

Benchmarks are intended to provide a basis for evaluating implementation changes, not simply to produce the largest possible throughput numbers.

## Engineering Principles

Volt's core development follows a few priorities:

- **Correctness before optimization.** Performance improvements must preserve the expected behavior of the engine.
- **Data-oriented storage.** Contiguous component layouts and predictable memory access are preferred where appropriate.
- **Explicit ownership.** Native allocations, entity lifetimes, and structural changes have defined ownership and validity rules.
- **Measured performance.** Benchmarks and profiling guide optimization decisions.
- **Controlled concurrency.** Parallel execution must account for storage lifetime, synchronization, and conflicting access.
- **Maintainable implementation.** Complexity should have a demonstrable benefit rather than exist for its own sake.

The intention is not to replace every facility provided by .NET. Existing runtime primitives are used when they are appropriate, and lower-level alternatives are considered when measurements justify them.

## Contributing

Contributions are welcome.

Changes to performance-sensitive code should include relevant tests and, where appropriate, benchmark results. Performance improvements should be evaluated against correctness, memory usage, and implementation complexity.

Bug reports, reproducible test cases, and performance investigations are also valuable contributions.

## License

Volt is free and open-source software licensed under the **GNU Lesser General Public License v3.0 only (LGPL-3.0-only)**.

The license is intended to keep distributed modifications to the LGPL-covered engine open-source while allowing developers to create and distribute proprietary or open-source games using Volt, subject to LGPL compliance requirements.

See [LICENSE](LICENSE) and [COPYING.LESSER](COPYING.LESSER) for the complete license terms.
