#!/usr/bin/env bash
# Self-tests for tools/benchmark-ecs-refresh.sh.
#
# A mock dotnet stands in for the benchmark run. It reports the tree it was
# started in, the files the build needs, and how many copies of the
# benchmark project that tree holds, so the recording script's isolation and
# its completeness check are tested without BenchmarkDotNet or a .NET
# installation.
set -euo pipefail

ROOT=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd -P)
SCRIPT="$ROOT/tools/benchmark-ecs-refresh.sh"
PROJECT="$ROOT/benchmarks/Volt.Benchmarks"

work=$(mktemp -d)
stray="$PROJECT/duplicate-check"
trap 'rm -rf "$work" "$stray"' EXIT

fixtures="$work/fixtures"
mkdir -p "$fixtures"

table_header() {
    printf '%s\n' \
        '| Method | Job | Toolchain | IterationCount | LaunchCount | WarmupCount | Mean | Error | StdDev |' \
        '|------- | --- | --------- | -------------- | ----------- | ----------- |-----:|------:|-------:|'
}

in_process_rows() {
    printf '%s\n' \
        '| SteadyRefresh | InProcess | InProcessEmitToolchain | Default | Default | Default | 8.5 us | 0.1 us | 0.1 us |' \
        '| RebuildingRefresh | InProcess | InProcessEmitToolchain | Default | Default | Default | 15.1 us | 0.2 us | 0.2 us |'
}

short_rows() {
    printf '| SteadyRefresh | ShortRun | Default | 3 | 1 | 3 | %s | 0.1 us | 0.1 us |\n' "$1"
    printf '| RebuildingRefresh | ShortRun | Default | 3 | 1 | 3 | %s | 0.2 us | 0.2 us |\n' "$2"
}

{
    table_header
    in_process_rows
    short_rows '8.5 us' '15.1 us'
} > "$fixtures/complete.txt"

{
    table_header
    in_process_rows
    short_rows 'NA' 'NA'
} > "$fixtures/na.txt"

{
    table_header
    in_process_rows
    short_rows '8.5 us' 'NA'
} > "$fixtures/half.txt"

{
    table_header
    in_process_rows
} > "$fixtures/missing.txt"

{
    printf '%s\n' '// Generate Exception: System.NotSupportedException: Found more than one matching project file for Volt.Benchmarks'
    printf '%s\n' '// BenchmarkDotNet has failed to build the auto-generated boilerplate code.'
    table_header
    in_process_rows
    short_rows 'NA' 'NA'
} > "$fixtures/duplicate.txt"

cat > "$work/dotnet" <<'FAKE'
#!/usr/bin/env bash
set -euo pipefail

printf 'PWD=%s\n' "$PWD"
printf 'ARGS=%s\n' "$*"
printf 'SOLUTION=%s\n' "$([ -f Volt.slnx ] && printf yes || printf no)"
printf 'SOURCES=%s\n' "$([ -f src/Volt.Core/Volt.Core.csproj ] && printf yes || printf no)"
printf 'COPIES=%s\n' \
    "$(find . -name Volt.Benchmarks.csproj -not -path '*/obj/*' | wc -l)"

if [ "${FAKE_BDN_SCENARIO:-complete}" = "failed" ]; then
    printf 'the benchmark build failed\n' >&2
    exit 7
fi

cat "$FIXTURES/$FAKE_BDN_SCENARIO.txt"
FAKE
chmod +x "$work/dotnet"

passed=0
failed=0

run_script() {
    rm -rf "$work/out"

    set +e
    FAKE_BDN_SCENARIO="$1" FIXTURES="$fixtures" \
        DOTNET="$work/dotnet" OUTPUT_DIR="$work/out" \
        "$SCRIPT" > "$work/stdout" 2> "$work/stderr"
    status=$?
    set -e
}

check() {
    local name="$1"
    shift

    if "$@"; then
        printf 'ok: %s\n' "$name"
        passed=$((passed + 1))
    else
        printf 'FAILED: %s\n' "$name" >&2
        sed 's/^/    /' "$work/stdout" "$work/stderr" >&2
        failed=$((failed + 1))
    fi
}

expect_status() {
    [ "$status" -eq "$1" ]
}

stdout_has() {
    grep -Fq -- "$1" "$work/stdout"
}

stdout_lacks() {
    ! grep -Fq -- "$1" "$work/stdout"
}

stderr_has() {
    grep -Fq -- "$1" "$work/stderr"
}

run_file_has() {
    grep -Fq -- "$1" "$work/out/BenchmarkRun.txt"
}

run_file_exists() {
    [ -f "$work/out/BenchmarkRun.txt" ]
}

tree_of_run() {
    sed -n 's/^PWD=//p' "$work/out/BenchmarkRun.txt" | head -1
}

tree_is_outside_the_checkout() {
    local tree
    tree=$(tree_of_run)

    [ -n "$tree" ] || return 1

    case "$tree" in
        "$ROOT"|"$ROOT"/*) return 1 ;;
    esac
}

tree_is_removed() {
    local tree
    tree=$(tree_of_run)

    [ -n "$tree" ] && [ ! -d "$tree" ]
}

discard_kept_tree() {
    local tree
    tree=$(sed -n 's/.*Benchmark build tree kept at //p' "$work/stderr" | head -1)

    [ -z "$tree" ] || rm -rf "$tree"
}

run_script complete
check "a complete run succeeds" expect_status 0
check "it reports both measurements" stdout_has "SteadyRefresh (ShortRun): 8.5 us"
check "it names the recorded run" stdout_has "Benchmark run written to"
check "the run log is kept" run_file_exists
check "the build tree is outside the checkout" tree_is_outside_the_checkout
check "the build tree is cleaned after success" tree_is_removed
check "the build tree has a solution marker" run_file_has "SOLUTION=yes"
check "the build tree has the referenced sources" run_file_has "SOURCES=yes"
check "the build tree holds one benchmark project" run_file_has "COPIES=1"
check "it still asks for the launched refresh jobs" run_file_has \
    "ARGS=run -c Release --project benchmarks/Volt.Benchmarks -- --filter *EcsInspectorRefreshBenchmarks* --job short"

run_script na
check "a run without launched measurements fails" expect_status 1
check "it names the unmeasured method" stderr_has "SteadyRefresh (ShortRun) produced NA"
check "it does not claim the run was written" stdout_lacks "Benchmark run written"
check "the log is kept for diagnosis" run_file_exists
check "the build tree is kept for diagnosis" stderr_has "Benchmark build tree kept at"
discard_kept_tree

run_script half
check "a half-measured run fails" expect_status 1
check "it names the unmeasured launched row" stderr_has \
    "RebuildingRefresh (ShortRun) produced NA"
discard_kept_tree

run_script missing
check "a run without the launched rows fails" expect_status 1
check "it names the absent row" stderr_has "RebuildingRefresh (ShortRun) has no row"
discard_kept_tree

run_script duplicate
check "an ambiguous project search fails" expect_status 1
check "it explains the duplicate project" stderr_has \
    "more than one copy of the benchmark project"
discard_kept_tree

run_script failed
check "a failing benchmark process fails the script" expect_status 7
check "it reports the process status" stderr_has "exited with status 7"
check "the log is kept for diagnosis" run_file_exists
discard_kept_tree

# The real project directory must still hold exactly one project file.
mkdir -p "$stray"
printf '%s\n' '<Project />' > "$stray/Volt.Benchmarks.csproj"
set +e
FIXTURES="$fixtures" DOTNET="$work/dotnet" OUTPUT_DIR="$work/out" \
    "$SCRIPT" > "$work/stdout" 2> "$work/stderr"
status=$?
set -e
rm -rf "$stray"

check "a second project in the project directory fails" expect_status 2
check "it names the copies" stderr_has "2 copies of Volt.Benchmarks.csproj"

printf '\n%d test(s) passed\n' "$passed"

if [ "$failed" -ne 0 ]; then
    printf '%d test(s) failed\n' "$failed" >&2

    exit 1
fi
