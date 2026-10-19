# VOLT test tooling

## Headless Vulkan harness

`tools/test-vulkan-headless.sh` runs a VOLT Vulkan sample against a
private, GPU-backed headless Weston compositor. It is the supported way
to verify rendering on a machine without a usable display and to capture
real rendered frames in automation.

The harness is self-contained: it creates and destroys its own graphical
environment, so it can run from a terminal, a CI job, or an agent session
without touching the desktop that launched it.

### Usage

```bash
./tools/test-vulkan-headless.sh \
    --project samples/Volt.VulkanMeshDemo \
    --frames 600 \
    --validation \
    --screenshots
```

| Option | Default | Meaning |
| --- | --- | --- |
| `--project PATH` | required | Sample project directory containing one `.csproj` |
| `--frames N` | `600` | Accepted frames the sample must present |
| `--validation` | off | Passes `--validation` to the sample |
| `--screenshots` | off | Captures and verifies a screenshot |
| `--expect-clusters N` | off | Requires `N` horizontal content clusters; needs `--screenshots` |
| `--output-dir PATH` | `<repo>/artifacts/headless` | Root for run artifacts |
| `--timeout-seconds N` | `180` | Bound for the whole run |
| `--help` | | Usage information |

Relative paths are resolved against the caller's working directory. The
repository itself is located relative to the script, so the harness works
from any directory.

The harness never builds the sample. Build first:

```bash
dotnet build Volt.slnx -c Release
```

It then runs `dotnet <assembly>.dll --frames N [--validation]`. Samples
used with this harness must:

- Accept `--frames N` and terminate after presenting that many frames.
- Print `Presented N accepted frames.` when the run is complete. The
  summary and exit code depend on this line.
- Accept `--validation`; when it is passed, the sample must print
  `Validation: True`. A missing, `False`, or unrecognized confirmation is
  treated as a validation failure, so the harness fails closed instead of
  assuming the layers were enabled.
- Stay alive long enough to be captured when `--screenshots` is used.
  Short runs such as `--frames 5` can finish before the capture window;
  use a few hundred frames for screenshot runs.

An optional `--expect-clusters N` compares the screenshot against a known
scene. Use `3` for `Volt.VulkanMeshDemo`, which draws three separated
meshes. Other samples should omit it until their expected layout is known.

### Isolation

Each invocation creates:

- A private `XDG_RUNTIME_DIR` (`mktemp -d`, mode `0700`) under `TMPDIR`.
- A uniquely named Wayland socket inside that directory.
- A private headless Weston compositor using the `headless` backend, the
  `gl` renderer, and a `1280x720` kiosk output.
- A unique run directory under the output root.

The sample receives only the private `XDG_RUNTIME_DIR` and
`WAYLAND_DISPLAY`, plus `SDL_VIDEODRIVER=wayland` and an empty `DISPLAY`,
so an accidental X11 or desktop Wayland fallback cannot happen. The
harness never edits compositor configuration, systemd units, KDE
settings, or global environment variables, and never moves the pointer,
requests focus, or injects input.

Concurrent and repeated runs are isolated from each other: runtime
directories, sockets, and artifact directories are unique per invocation.

### Lifecycle and teardown

The compositor and the sample are background jobs in their own process
groups (Bash job control). Cleanup signals only those process groups, so
unrelated Weston instances or desktop applications cannot be matched by
name and killed.

The sample runs behind a small supervisor that leads the sample's process
group. The supervisor records the sample's exit status and then stays
alive until teardown, so the group ID remains attached to a process the
harness owns even after the sample itself exits. Descendants left behind
by the sample are still owned and terminated, and a recycled group ID can
never be signalled by mistake. The sample's exit status is read from the
supervisor, so teardown does not affect status reporting.

Teardown runs on success, application failure, screenshot failure,
timeout, and `INT`/`TERM`/`HUP`. Each owned group receives `SIGTERM` and a
bounded grace period, then `SIGKILL`, and is reaped. The private runtime
directory is removed only after its processes are gone, and only if its
path matches the temporary prefix the harness created. The output
directory is never removed.

Cleanup failure never hides an earlier failure. The exit status is the
first failure the run encountered, so a cleanup failure is reported as
code `12` only when the run would otherwise have succeeded. When an
earlier failure exists, its code is preserved and the cleanup failure is
listed in `summary.txt` and on stderr.

### Artifacts

Every run creates `<output-dir>/run-<UTC timestamp>-<pid>-<random>/`:

| File | Contents |
| --- | --- |
| `invocation.log` | Resolved paths, arguments, tool locations |
| `summary.txt` | Machine-readable result, frame count, validation state |
| `app.stdout.log` | Sample output, including frame progress |
| `app.stderr.log` | Sample errors and Vulkan validation output |
| `app.status` | Sample exit status recorded by its supervisor |
| `weston.log`, `weston.stdout.log` | Compositor diagnostics |
| `screenshooter.log` | Screenshot tool output |
| `image-analysis.log` | PNG verification results |
| `*.png` | Captured screenshot, when requested |

Artifacts from failed runs are preserved for diagnosis. Only the private
runtime directory is removed.

### Screenshot verification

When `--screenshots` is used, the harness waits for the sample to report a
presented frame, runs `weston-screenshooter` against the private
compositor, and then verifies the newest PNG:

- Valid PNG signature, exact chunk boundaries, and no truncated chunks.
- A matching CRC for every chunk and a complete, empty `IEND` chunk with
  no data after it.
- A complete zlib stream that decompresses to exactly the expected image
  data.
- 8-bit, non-interlaced grayscale, RGB, palette, and alpha image data.
- The expected `1280x720` output size.
- At least 0.5% non-background pixels, so a blank compositor surface is a
  failure rather than a pass.
- The requested number of horizontal content clusters, if
  `--expect-clusters` was given.

Screenshot capture or verification failure exits with code `10`; it is
never silently ignored.

### Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Rendering verified |
| `1` | Internal setup failure |
| `2` | Usage or argument error |
| `3` | Missing dependency or missing Release build |
| `4` | Compositor failed to start |
| `5` | Compositor did not create its socket |
| `6` | Application could not be started |
| `7` | Application failed |
| `8` | Timeout |
| `9` | Vulkan validation diagnostics |
| `10` | Screenshot capture or verification failed |
| `11` | Presented frame count mismatch |
| `12` | Cleanup failure |
| `129`/`130`/`143` | Terminated by `HUP`/`INT`/`TERM` |

### Dependencies

Required: Bash, `weston` 15 (headless backend), `weston-screenshooter`,
`dotnet`, GNU coreutils (`mktemp`, `timeout`), and Python 3 (screenshot
verification). The harness fails with exit code `3` listing anything
missing. It does not install packages.

Linux only: the harness targets the Wayland/Weston stack used by VOLT.

### Self-tests

```bash
./tools/tests/test-vulkan-headless.sh
```

The self-tests put mock `weston`, `dotnet`, and `weston-screenshooter`
executables on `PATH`. The harness under test is unmodified and still
performs real process-group teardown, screenshot sequencing, artifact
handling, and signal handling; only the graphics tools are simulated.
Coverage includes argument validation, missing dependencies, successful
runs, application failure, timeout, validation diagnostics (including
missing confirmation), surviving sample descendants, frame-count
mismatch, screenshot failures (including CRC-corrupted and incomplete
PNGs), compositor failures, termination cleanup, controlled cleanup
failures and their summary records, preservation of unrelated files, and
consecutive/concurrent isolation. The missing-dependency test uses only
ordinary utilities plus the mock `dotnet`, so it does not require a .NET
installation on the test host.

`bash -n` and `shellcheck -S style` should stay clean for both scripts.
