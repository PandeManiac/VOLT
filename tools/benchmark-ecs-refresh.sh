#!/usr/bin/env bash
# Records the ECS inspector's refresh paths as a tracked benchmark run.
#
# The steady path is a refresh that finds the snapshot unchanged and keeps
# its rows; the rebuilding path rewrites the selected entity's rows, as a
# selection change or a moved world does. Runs are kept under
# tools/benchmark-results so two builds can be compared directly.
#
# BenchmarkDotNet wraps the benchmark project to build its out-of-process
# jobs. It finds that project by walking up from the working directory to
# the nearest folder holding a solution or project file, then searching that
# folder and every subfolder for Volt.Benchmarks.csproj; more than one match
# is an error. Volt.slnx makes the repository root that folder for a run
# started anywhere in the checkout, so neither --project nor the working
# directory narrows the search, and a performance lab or an exported
# experiment beside the root makes it ambiguous. The run therefore builds
# from a scratch copy of the sources it needs: the copy holds exactly one
# benchmark project, and it is made from the working tree, so an edit in
# progress is still what gets measured.
#
# A benchmark that fails to build is still reported by BenchmarkDotNet with
# exit status 0: the run ends with a table row of NA values, and the summary
# lists it under "Benchmarks with issues". The process status alone would
# record such a run as a success, so the recorded table is checked
# afterwards and the script fails when a required measurement is absent.
set -euo pipefail

SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
REPO_ROOT=$(cd -- "$SCRIPT_DIR/.." && pwd -P)
PROJECT_DIR="$REPO_ROOT/benchmarks/Volt.Benchmarks"
DOTNET=${DOTNET:-dotnet}
PYTHON=${PYTHON:-python3}
DATE=${DATE:-$(date +%F)}
OUTPUT_DIR=${OUTPUT_DIR:-"$REPO_ROOT/tools/benchmark-results/ecs-refresh-$DATE"}

FILTER='*EcsInspectorRefreshBenchmarks*'
REQUIRED=(SteadyRefresh:ShortRun RebuildingRefresh:ShortRun)

# What a benchmark build needs from the checkout. Copying these without
# their bin and obj directories gives the run a clean tree and keeps every
# other copy of the project out of it.
SOURCES=(
    benchmarks
    src
    assets
    Directory.Build.props
    Volt.slnx
    .editorconfig
)

mkdir -p "$OUTPUT_DIR"
RUN_FILE=$(cd -- "$OUTPUT_DIR" && pwd -P)/BenchmarkRun.txt

# The project itself must still be the only one under its directory. A
# second copy inside it would be a mistake worth naming rather than leaving
# as a build error in the log.
count=$(find "$PROJECT_DIR" \
    -name 'Volt.Benchmarks.csproj' \
    -not -path '*/obj/*' | wc -l)

if [ "$count" -ne 1 ]; then
    printf 'error: %d copies of Volt.Benchmarks.csproj under %s; keep one\n' \
        "$count" "$PROJECT_DIR" >&2
    find "$PROJECT_DIR" \
        -name 'Volt.Benchmarks.csproj' \
        -not -path '*/obj/*' >&2
    exit 2
fi

TREE=$(mktemp -d "${TMPDIR:-/tmp}/volt-benchmark-XXXXXXXX")
succeeded=0

cleanup() {
    if [ "$succeeded" -eq 1 ]; then
        rm -rf "$TREE"

        return
    fi

    printf 'Benchmark build tree kept at %s\n' "$TREE" >&2
}

trap cleanup EXIT

for entry in "${SOURCES[@]}"; do
    if [ ! -e "$REPO_ROOT/$entry" ]; then
        continue
    fi

    tar -C "$REPO_ROOT" \
        --exclude='bin' \
        --exclude='obj' \
        --exclude='BenchmarkDotNet.Artifacts' \
        -cf - "$entry" | tar -C "$TREE" -xf -
done

cd -- "$TREE"

set +e
"$DOTNET" run -c Release \
    --project benchmarks/Volt.Benchmarks \
    -- \
    --filter "$FILTER" \
    --job short 2>&1 | tee "$RUN_FILE"
status=${PIPESTATUS[0]}
set -e

if [ "$status" -ne 0 ]; then
    printf 'error: BenchmarkDotNet exited with status %d\n' "$status" >&2
    printf 'the log is in %s\n' "$RUN_FILE" >&2
    exit "$status"
fi

requirements=()
for pair in "${REQUIRED[@]}"; do
    requirements+=(--require "$pair")
done

"$PYTHON" "$SCRIPT_DIR/verify-benchmark-run.py" \
    --run "$RUN_FILE" \
    "${requirements[@]}"

succeeded=1

printf 'Benchmark run written to %s\n' "$RUN_FILE"
