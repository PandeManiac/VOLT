# Gameplay Foundation Final Verification

**Verification date:** 2026-10-25
**Cold benchmark artifact timestamp:** 2026-10-25 (`BenchmarkRun.txt`)
**Baseline:** `7f229f4e084bb5929d849aa5c282fb45b4fd8702`
**Scope:** Final Gameplay foundation verification; this does not change VOLT's project-wide production-readiness declaration.

## Cold-path benchmark method

`GameplayCueColdPathBenchmarks` and `GameplayCueColdPopulationBenchmarks` use BenchmarkDotNet 0.15.8 with `MemoryDiagnoser` and in-process .NET 10. Each one-shot cold workload gets fresh world/system/buffer state in `IterationSetup`; the operation runs once per iteration and cleanup runs outside the timed method. Runtime data structures are cold; OS process startup, JIT compilation, static initialization, and shared registry initialization are excluded. Shared tags and definitions are established in `GlobalSetup`, unless noted. Managed allocation is per timed invocation and excludes setup/cleanup.

Coverage: first cue system construction; execute with no listener, one listener and hierarchical listener; persistent cue add/update/remove; subscription creation; effect-backed cue creation, expiration and explicit removal; activation-owned cue creation and cleanup; execute-to-presentation-buffer enqueue/drain; first dictionary and cue-slot expansion; first buffer expansion; repeated initialization of 16 independent instances; and persistent population growth at 1,000/10,000/100,000 cues. The detail list below states exactly what initialization is timed. These workloads measure state-cold first use within a warmed .NET process and are not substitutes for process-startup timing.

## Measurements

Executed on the environment below with Release BenchmarkDotNet default statistical job, in-process toolchain, `InvocationCount=1`, `UnrollFactor=1`. BDN ran 18 cold-path methods plus three population-size rows successfully (21 total measurement rows). Raw reproducibility artifacts are committed under [`benchmark-results/gameplay-cues-cold-2026-10-25`](benchmark-results/gameplay-cues-cold-2026-10-25/): full BDN logs, CSV, JSON, Markdown reports, and warmed control outputs. The cold log is `BenchmarkRun.txt`; warm control is `BenchmarkWarmControl.txt`. Reproduce:

```bash
dotnet run --project benchmarks/Volt.Benchmarks -c Release -- \
  --filter '*GameplayCueColdPathBenchmarks*' '*GameplayCueColdPopulationBenchmarks*' \
  --memory --exporters JSON --artifacts ./BenchmarkDotNet.Artifacts
```

| Cold operation | Mean | Managed allocation |
| --- | ---: | ---: |
| World + first cue system construction | 11.45 μs | 14,928 B |
| First no-listener cue execute | 0.97 μs | 0 B |
| First execute, one listener already subscribed | 3.31 μs | 0 B |
| First hierarchical dispatch | 3.59 μs | 0 B |
| First persistent cue add | 1.90 μs | 0 B |
| First persistent cue update (fresh cue prepared) | 1.00 μs | 0 B |
| First persistent cue remove (fresh cue prepared) | 1.52 μs | 0 B |
| First subscription creation | 3.78 μs | 112 B |
| Fourth distinct subscription key (dictionary growth) | 4.39 μs | 192 B |
| 17th cue (slot-array growth) | 4.87 μs | 4,888 B |
| First execute + presentation enqueue + drain | 1.90 μs | 0 B |
| First presentation-buffer expansion | 2.71 μs | 312 B |
| First effect apply -> cue creation | 11.37 μs | 28,984 B |
| First effect expiration -> cue removal (effect/cue prepared) | 5.66 μs | 288 B |
| Explicit effect removal -> cue retirement (prepared) | 5.22 μs | 336 B |
| First activation-owned cue creation (includes activation and task) | 17.76 μs | 70,160 B |
| Ability End + activation-owned cue cleanup (prepared active ability) | 8.85 μs | 2,384 B |
| 16 independent world/cue-system initializations, normalized per runtime | 1.79 μs | 15,093 B |

Population growth includes all N `Add` calls and backing growth, excludes tag registration and world/cue-system setup:

| Persistent cues | Mean | Managed allocation |
| ---: | ---: | ---: |
| 1,000 | 235.2 μs | 306.89 KiB |
| 10,000 | 4.34 ms | 4,866.98 KiB |
| 100,000 | 20.11 ms | 38,922.53 KiB |

The cold method includes world and cue-system construction but not disposal. Execute workloads exclude tag registration; one-listener subscription is setup. Add includes slot allocation and Added dispatch; update/remove receive a newly added cue in setup. Subscription excludes tag registration. Effect creation includes `Apply` and cue transition; expiry and explicit removal begin with effect and cue already active. Activation creation includes activation, lazy cue runtime, cue, and a long-duration task; cleanup begins from that active state. Presentation delivery includes execute + enqueue + drain, with system/buffer constructed in setup; buffer expansion starts with capacity one full. The dictionary case sets up three distinct keys; cue-slot expansion sets up sixteen cues. Repeated init creates sixteen separate `World`/cue-system pairs and reports mean/allocation per instance.

Warm-path control, run separately on the same host and runtime with BDN defaults and `ListenerCount=1`: no-listener execute **8.11 ns / 0 B**; one-listener execute **48.91 ns / 0 B**; add/remove **88.99 ns / 0 B**; buffer enqueue/drain **18.96 ns / 0 B**; subscribe/unsubscribe **25.70 ns / 0 B**. These are deliberately shown side by side, not divided into cold/warm ratios: the cold operations involve fresh structures and sometimes different operation boundaries. Existing allocation tests guard the warmed execute and buffered path at zero allocations.

Environment: Ubuntu 24.04.5 LTS; .NET SDK 10.0.402; runtime 10.0.13; AMD EPYC 19/01 KVM virtual CPU (2.45 GHz reported, 8 logical CPUs; BDN affinity reported 1 CPU); x64 RyuJIT AVX2; concurrent workstation GC. BDN reported high-priority scheduling denied by container permissions. The full statistical population job warned that iteration times are below its 100 ms recommendation and reported outliers. Some first-use per-method rows were recorded on the second in-process invocation after setup and therefore reflect warmed JIT/code paths with fresh data structures, not process-level first call. Treat the cold labeling as runtime-state cold, and measurements as indicative VM comparisons rather than portable latency guarantees. No benchmark operation failed or timed out. Single-sample `Dry` values were not used above. This final artifact set was captured after the benchmark harness source edits and build; no subsequent benchmark source changes have been made.

## Regression verification

Fresh Release validation on this branch: `dotnet restore Volt.slnx` passed; `dotnet build Volt.slnx -c Release --no-restore` passed with **0 warnings, 0 errors**; `dotnet test Volt.slnx -c Release --no-restore` passed with **2,831 passed, 33 skipped, 0 failed (2,864 total)**. All 33 skipped cases are native Vulkan integration/device-dependent tests and are not counted as passes. The `Volt.Gameplay.Examples` project passed **9/9** documented examples; focused cue allocation, integrated lifecycle, cross-store and stress filter passed **269/269**. `dotnet format Volt.slnx --verify-no-changes --severity warn` passed. `Volt.GameplayDemo` passed **125 assertions**. Headless Vulkan harness self-tests passed **52/52** and `bash -n tools/test-vulkan-headless.sh tools/tests/test-vulkan-headless.sh` passed. YAML parser checks confirmed the GPU dispatch input, event condition, runner labels, dependency and harness arguments. No native Vulkan hardware tests ran; their 33 skip status remains explicit.

## Vulkan hardware status and procedure

**Vulkan hardware validation: NOT RUN — HARDWARE UNAVAILABLE.** The Ubuntu VM exposes no `/dev/dri` or `/dev/nvidia*`, no Vulkan ICD or validation-layer directories, and no `vulkaninfo`, Weston, shader compiler, or compositor tools. `libvulkan.so.1` is present but is only the loader. GitHub Actions reports zero registered runners. No runner has been installed or registered.

On an authorized Linux host with a real Vulkan GPU, working driver/loader, `VK_LAYER_KHRONOS_validation`, Weston 15 headless GL, `weston-screenshooter`, Python 3, .NET 10 and `glslangValidator`, run:

```bash
dotnet restore Volt.slnx
dotnet build samples/Volt.VulkanMeshDemo -c Release --no-restore
VOLT_VULKAN_INTEGRATION=1 dotnet test tests/Volt.Graphics.Vulkan.Tests -c Release --no-build
./tools/test-vulkan-headless.sh \
  --project samples/Volt.VulkanMeshDemo \
  --frames 600 --validation --screenshots --expect-clusters 3
```

Retain harness logs/screenshots, native-test output, GPU model, driver and Vulkan API/layer versions, frame count, screenshot and cluster assertions, diagnostics, and exit statuses. Require zero unexpected Vulkan validation diagnostics. The harness's mock self-tests do not validate GPU output. Full checklist and GitHub runner procedure: [`README.md`](README.md).

## CI policy

The hosted `build-test` job remains automatic on pushes and pull requests. The real GPU job remains executable only when a maintainer manually dispatches the workflow and sets `gpu_validation=true`; it runs only after hosted checks pass. It cannot be triggered by a push or PR, so absent hardware cannot leave standard CI indefinitely queued and PR code is not run on self-hosted infrastructure. The manually requested job still needs `[self-hosted, linux, vulkan-gpu]` and can remain queued when no matching runner exists. It is not a required branch-protection context; keep the hosted check as the applicable PR requirement.

## Tracked hardware follow-up

**Pending:** a repository owner must register or provide an authorized Linux Vulkan GPU runner with labels `[self-hosted, linux, vulkan-gpu]`, .NET 10, `glslangValidator`, Weston 15 headless GL, `weston-screenshooter`, Python 3, a working Vulkan driver/loader, and `VK_LAYER_KHRONOS_validation`. Then dispatch **Actions → CI → Run workflow** with `gpu_validation=true`; run the native Vulkan integration tests and the 600-frame harness command above; preserve device/version information, test output, screenshots and harness logs. Close this follow-up only after the real device, validation layer, 600 frames, screenshot integrity, three-cluster assertion, and zero unexpected Vulkan validation diagnostics are confirmed. Do not register a personal runner without owner approval.

## Intentional scope boundaries

Gameplay adds no replication, animation, audio, scripting, rendering, or project-specific ability systems. Hardware absence is separately tracked engine validation and does not by itself invalidate Gameplay feature closure when all applicable software checks pass and the limitation is clear.
