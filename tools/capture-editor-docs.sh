#!/usr/bin/env bash
# Regenerate the editor guide's screenshots with the headless snapshot
# command. No display server, GPU, or project checkout is needed.
set -euo pipefail

SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
REPO_ROOT=$(cd -- "$SCRIPT_DIR/.." && pwd -P)
PROJECT="$REPO_ROOT/src/Volt.Editor/Volt.Editor.csproj"
OUTPUT_DIR="$REPO_ROOT/docs/images"
DOTNET=${DOTNET:-dotnet}
PYTHON=${PYTHON:-python3}

WIDTH=1440
HEIGHT=900
CHECK=0
BUILD=1

usage() {
	printf '%s\n' \
		"Usage: tools/capture-editor-docs.sh [options]" \
		"" \
		"Writes docs/images/editor-workspace.png, editor-authoring.png," \
		"editor-ecs.png, and editor-start.png from the editor's --snapshot" \
		"command." \
		"" \
		"Options:" \
		"  --check              Capture to a temporary directory and compare" \
		"                       committed images with a per-channel and pixel" \
		"                       tolerance (requires Python 3)." \
		"  --no-build           Skip the Release build of Volt.Editor." \
		"  --output-dir PATH    Write images somewhere other than docs/images." \
		"  --help               Show this help."
}

while [[ $# -gt 0 ]]; do
	case "$1" in
		--check)
			CHECK=1
			shift
			;;
		--no-build)
			BUILD=0
			shift
			;;
		--output-dir)
			if [[ $# -lt 2 ]]; then
				printf 'error: --output-dir requires a path\n' >&2
				exit 2
			fi

			OUTPUT_DIR=$2
			shift 2
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

if ! command -v "$DOTNET" >/dev/null 2>&1; then
	printf 'error: %s was not found on PATH\n' "$DOTNET" >&2
	exit 1
fi

if [[ $BUILD -eq 1 ]]; then
	"$DOTNET" build "$PROJECT" -c Release --nologo
fi

TARGET_DIR=$OUTPUT_DIR
TEMP_DIR=""

if [[ $CHECK -eq 1 ]]; then
	TEMP_DIR=$(mktemp -d "${TMPDIR:-/tmp}/volt-editor-docs.XXXXXX")
	trap 'rm -rf "$TEMP_DIR"' EXIT
	TARGET_DIR=$TEMP_DIR
fi

mkdir -p "$TARGET_DIR"

capture() {
	local name=$1
	local state=$2
	local arguments=(
		--snapshot "$TARGET_DIR/$name"
		--width "$WIDTH"
		--height "$HEIGHT"
	)

	if [[ -n "$state" ]]; then
		arguments+=(--snapshot-state "$state")
	fi

	"$DOTNET" run \
		--project "$PROJECT" \
		-c Release \
		--no-build \
		-- \
		"${arguments[@]}"
}

capture editor-workspace.png workspace
capture editor-authoring.png authoring
capture editor-ecs.png ecs
capture editor-start.png start

if [[ $CHECK -eq 1 ]]; then
	if ! command -v "$PYTHON" >/dev/null 2>&1; then
		printf 'error: %s was not found on PATH (required by --check)\n' \
			"$PYTHON" >&2
		exit 1
	fi

	if ! "$PYTHON" "$SCRIPT_DIR/compare-editor-docs.py" \
		"$OUTPUT_DIR" "$TEMP_DIR"; then
		printf '%s\n' \
			"Documentation screenshots are stale." \
			"Run ./tools/capture-editor-docs.sh and review the images." >&2
		exit 1
	fi

	exit 0
fi

printf 'Documentation screenshots written to %s\n' "$OUTPUT_DIR"
