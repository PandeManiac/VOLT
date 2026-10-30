#!/usr/bin/env bash
# Runs the gates hosted CI checks first, so a failure shows up before a push.
# The hook in .githooks/pre-push calls this; enable it with
#   git config core.hooksPath .githooks
set -euo pipefail

usage() {
    printf '%s\n' \
        "Usage: tools/pre-push-check.sh [options]" \
        "" \
        "Runs the formatting, tooling, contents, syntax and whitespace" \
        "gates, so a failure shows up before a push." \
        "" \
        "Options:" \
        "  --screenshots   Also compare the documentation screenshots," \
        "                  which builds the editor first." \
        "  --help          Show this help."
}

screenshots=0

while [ "$#" -gt 0 ]; do
    case "$1" in
        --screenshots)
            screenshots=1
            shift
            ;;
        --help|-h)
            usage
            exit 0
            ;;
        *)
            printf 'error: unknown option %s\n' "$1" >&2
            usage >&2
            exit 2
            ;;
    esac
done

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

status=0
passed=0

run() {
    local name="$1"
    shift

    printf '\n== %s\n' "$name"

    if "$@"; then
        passed=$((passed + 1))
        return 0
    fi

    printf 'FAILED: %s\n' "$name" >&2
    status=1

    return 0
}

# dotnet format restores unless told not to, so it works on a cold tree.
run "formatting" \
    dotnet format Volt.slnx --verify-no-changes --severity warn
run "document tooling" python3 tools/tests/test-compare-editor-docs.py
run "benchmark tooling" python3 tools/tests/test-compare-benchmark-runs.py
run "document contents" python3 tools/tests/test-doc-contents.py

shell_sources=()
while IFS= read -r script; do
    shell_sources+=("$script")
done < <(find tools -name '*.sh' -type f | sort)

run "shell syntax" bash -n "${shell_sources[@]}"

# Whitespace errors in the commits about to leave the machine, which the
# working-tree check below cannot see. Skipped when no remote base resolves,
# for example on a branch that has never been pushed.
base="$(git merge-base HEAD origin/master 2>/dev/null || true)"

# A clone without a fetched origin falls back to its own upstream.
if [ -z "$base" ]; then
    base="$(git merge-base HEAD "@{upstream}" 2>/dev/null || true)"
fi

if [ -n "$base" ]; then
    run "whitespace in the pushed commits" git diff --check "$base"..HEAD
else
    printf '\n== whitespace in the pushed commits\n'
    printf 'skipped: no origin/master or upstream to compare with\n'
fi

run "whitespace in the working tree" git diff --check

# The image comparison builds the editor and captures five images, so it
# is the slowest gate and is asked for explicitly.
if [ "$screenshots" -eq 1 ]; then
    run "documentation screenshots" ./tools/capture-editor-docs.sh --check
else
    printf '\n== documentation screenshots\n'
    printf 'skipped: pass --screenshots to compare the images\n'
fi

printf '\n%d gate(s) passed\n' "$passed"

if [ "$status" -ne 0 ]; then
    printf 'At least one gate failed; fix it before pushing.\n' >&2

    exit 1
fi

printf 'All gates passed.\n'
