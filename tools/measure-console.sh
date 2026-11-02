#!/usr/bin/env bash
#
# Measures what one console log line costs the editor, so a change to the
# console panel can be compared across commits instead of trusted.
#
# The benchmark is a test-shaped file: this script copies it into the test
# project, runs only it, prints the numbers, and removes it again. Nothing
# is committed to the suite and no timing value is ever asserted, because a
# wall-clock budget would fail on a slower machine for no real defect.
#
# Usage:
#   tools/measure-console.sh            # measure the working tree
#   tools/measure-console.sh --keep     # leave the copied file for editing

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
template="$repo_root/tools/console-benchmark.cs.txt"
target="$repo_root/tests/Volt.Editor.Tests/ConsoleBenchmarkScratch.cs"
report="$(mktemp "${TMPDIR:-/tmp}/volt-console-bench.XXXXXX")"
log="$(mktemp "${TMPDIR:-/tmp}/volt-console-bench-log.XXXXXX")"
keep=0

if [ "${1:-}" = "--keep" ]; then
	keep=1
fi

if [ ! -f "$template" ]; then
	printf 'error: %s is missing\n' "$template" >&2
	exit 1
fi

if [ -e "$target" ]; then
	printf 'error: %s exists; remove it before measuring\n' "$target" >&2
	exit 1
fi

cleanup() {
	if [ "$keep" -eq 0 ]; then
		rm -f "$target"
	fi

	rm -f "$report" "$log"
}

trap cleanup EXIT

cp "$template" "$target"

if ! command -v dotnet >/dev/null 2>&1; then
	if [ -x "$HOME/.dotnet/dotnet" ]; then
		export PATH="$HOME/.dotnet:$PATH"
	else
		printf 'error: dotnet was not found on PATH\n' >&2
		exit 1
	fi
fi

if ! VOLT_BENCH_OUT="$report" \
	dotnet test "$repo_root/tests/Volt.Editor.Tests/Volt.Editor.Tests.csproj" \
	--nologo \
	--filter "FullyQualifiedName~ConsoleBenchmarkScratch" >"$log" 2>&1; then
	printf 'error: the measurement did not build or run\n' >&2
	tail -n 20 "$log" >&2
	exit 1
fi

if [ ! -s "$report" ]; then
	printf 'error: the measurement produced no numbers\n' >&2
	tail -n 20 "$log" >&2
	exit 1
fi

cat "$report"

if [ "$keep" -eq 1 ]; then
	printf 'kept %s for editing\n' "$target"
fi
