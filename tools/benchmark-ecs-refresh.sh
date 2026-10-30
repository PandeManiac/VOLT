#!/usr/bin/env bash
# Records the ECS inspector's refresh paths as a tracked benchmark run.
#
# The steady path is a refresh that finds the snapshot unchanged and keeps
# its rows; the rebuilding path rewrites the selected entity's rows, as a
# selection change or a moved world does. Runs are kept under
# tools/benchmark-results so two builds can be compared directly.
set -euo pipefail

SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
REPO_ROOT=$(cd -- "$SCRIPT_DIR/.." && pwd -P)
DOTNET=${DOTNET:-dotnet}
DATE=${DATE:-$(date +%F)}
OUTPUT_DIR="$REPO_ROOT/tools/benchmark-results/ecs-refresh-$DATE"

mkdir -p "$OUTPUT_DIR"

"$DOTNET" run -c Release \
	--project "$REPO_ROOT/benchmarks/Volt.Benchmarks" \
	-- \
	--filter '*EcsInspectorRefreshBenchmarks*' \
	--job short 2>&1 | tee "$OUTPUT_DIR/BenchmarkRun.txt"

printf 'Benchmark run written to %s\n' "$OUTPUT_DIR/BenchmarkRun.txt"
