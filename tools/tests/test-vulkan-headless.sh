#!/usr/bin/env bash
#
# Self-tests for tools/test-vulkan-headless.sh.
#
# The tests replace weston, dotnet, and weston-screenshooter with small
# mocks on PATH. The harness under test is unmodified: it still performs
# its real process management, screenshot capture sequence, and teardown.
# No GPU, compositor, or built sample is required.
set -uo pipefail

SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
HARNESS=$(cd -- "$SCRIPT_DIR/.." && pwd -P)/test-vulkan-headless.sh
BASE_PATH=$PATH

if [ ! -x "$HARNESS" ]; then
    printf 'harness is not executable: %s\n' "$HARNESS" >&2
    exit 1
fi

WORK_ROOT=$(mktemp -d "${TMPDIR:-/tmp}/vhl-selftest.XXXXXX") || exit 1
MOCKS="$WORK_ROOT/mocks"
REAL_RM=$(command -v rm) || exit 1
PASSED=0
FAILED=0

CURRENT_NAME=""
CASE_DIR=""
CASE_FAILED=0
RC=0
APP_MODE=ok
SHOOTER_MODE=ok
WESTON_MODE=ok
RM_MODE=ok

cleanup() {
    local status=$?
    local pidfile pid

    trap - EXIT INT TERM

    for pidfile in "$WORK_ROOT"/*/*.pid; do
        [ -f "$pidfile" ] || continue
        pid=$(cat "$pidfile" 2>/dev/null) || continue
        [ -n "$pid" ] || continue

        if kill -0 "$pid" 2>/dev/null; then
            kill -TERM "$pid" 2>/dev/null || true
        fi
    done

    if [ -n "$WORK_ROOT" ] && [ -d "$WORK_ROOT" ]; then
        case "$WORK_ROOT" in
            "${TMPDIR:-/tmp}"/vhl-selftest.*)
                rm -rf -- "$WORK_ROOT"
                ;;
        esac
    fi

    exit "$status"
}

trap cleanup EXIT

fail() {
    CASE_FAILED=1
    printf '    # %s\n' "$*" >&2
}

assert_rc() {
    [ "$RC" -eq "$1" ] || fail "expected exit $1, got $RC"
}

assert_contains() {
    grep -qF -- "$2" "$1" 2>/dev/null || fail "expected '$2' in $1"
}

assert_not_contains() {
    if grep -qF -- "$2" "$1" 2>/dev/null; then
        fail "did not expect '$2' in $1"
    fi
}

assert_file() {
    [ -f "$1" ] || fail "missing file $1"
}

assert_no_file() {
    [ ! -e "$1" ] || fail "unexpected file $1"
}

assert_process_dead() {
    local pidfile=$1
    local pid

    [ -f "$pidfile" ] || return 0

    pid=$(cat "$pidfile" 2>/dev/null)

    if [ -n "$pid" ] && kill -0 "$pid" 2>/dev/null; then
        fail "process $pid from $pidfile is still running"
    fi
}

assert_runtime_clean() {
    local leftovers

    leftovers=$(find "$CASE_DIR/tmp" -maxdepth 1 -name 'volt-headless-runtime.*' -print 2>/dev/null)

    if [ -n "$leftovers" ]; then
        fail "private runtime directories were not removed: $leftovers"
    fi
}

assert_run_dir() {
    RUN_DIR=$(find "$CASE_DIR/out" -maxdepth 1 -type d -name 'run-*' -print 2>/dev/null | sort | tail -n 1)

    if [ -z "$RUN_DIR" ]; then
        fail "no run directory was created"
    fi
}

begin_case() {
    CURRENT_NAME=$1
    CASE_DIR="$WORK_ROOT/$CURRENT_NAME"
    CASE_FAILED=0
    RC=0
    APP_MODE=ok
    SHOOTER_MODE=ok
    WESTON_MODE=ok
    RM_MODE=ok
    RUN_DIR=""

    rm -rf -- "$CASE_DIR"
    mkdir -p -- "$CASE_DIR/bin" "$CASE_DIR/tmp" \
        "$CASE_DIR/sample/bin/Release/net10.0"

    cat >"$CASE_DIR/sample/Mock.Sample.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
EOF

    : >"$CASE_DIR/sample/bin/Release/net10.0/Mock.Sample.dll"
}

end_case() {
    if [ "$CASE_FAILED" -eq 0 ]; then
        PASSED=$((PASSED + 1))
        printf 'ok %d - %s\n' "$((PASSED + FAILED))" "$CURRENT_NAME"
    else
        FAILED=$((FAILED + 1))
        printf 'not ok %d - %s\n' "$((PASSED + FAILED))" "$CURRENT_NAME"

        if [ -f "$CASE_DIR/console.err" ]; then
            sed 's/^/    # stderr: /' "$CASE_DIR/console.err" | tail -n 20
        fi

        if [ -f "$CASE_DIR/console.out" ]; then
            sed 's/^/    # stdout: /' "$CASE_DIR/console.out" | tail -n 20
        fi
    fi
}

run_harness() {
    PATH="$MOCKS:$BASE_PATH" \
    TMPDIR="$CASE_DIR/tmp" \
    MOCK_APP_MODE="$APP_MODE" \
    MOCK_SHOOTER_MODE="$SHOOTER_MODE" \
    MOCK_WESTON_MODE="$WESTON_MODE" \
    MOCK_RM_MODE="$RM_MODE" \
    MOCK_APP_PIDFILE="$CASE_DIR/app.pid" \
MOCK_APP_CHILD_PIDFILE="$CASE_DIR/app-child.pid" \
MOCK_WESTON_PIDFILE="$CASE_DIR/weston.pid" \
MOCK_WESTON_SOCKET_LOG="$CASE_DIR/socket.log" \
MOCK_WESTON_ARGS_LOG="$CASE_DIR/weston.args" \
MOCK_APP_ARGS_LOG="$CASE_DIR/app.args" \
    "$HARNESS" "$@" >"$CASE_DIR/console.out" 2>"$CASE_DIR/console.err"
    RC=$?
}

run_default() {
    run_harness \
        --project "$CASE_DIR/sample" \
        --output-dir "$CASE_DIR/out" \
        "$@"
}

install_mocks() {
    mkdir -p -- "$MOCKS"

    cat >"$MOCKS/weston" <<'MOCK'
#!/usr/bin/env bash
set -uo pipefail

mode=${MOCK_WESTON_MODE:-ok}
socket=""
log=""

if [ -n "${MOCK_WESTON_ARGS_LOG:-}" ]; then
    : >"$MOCK_WESTON_ARGS_LOG"

    for argument in "$@"; do
        printf '%s\n' "$argument" >>"$MOCK_WESTON_ARGS_LOG"
    done
fi

for argument in "$@"; do
    case "$argument" in
        --socket=*) socket=${argument#*=} ;;
        --log=*) log=${argument#*=} ;;
    esac
done

if [ -n "${MOCK_WESTON_PIDFILE:-}" ]; then
    printf '%s\n' "$$" >"$MOCK_WESTON_PIDFILE"
fi

if [ -n "$log" ]; then
    printf 'mock weston: mode=%s socket=%s\n' "$mode" "$socket" >>"$log"
fi

if [ "$mode" = "fail" ]; then
    printf 'mock weston: cannot initialize headless backend\n' >&2
    exit 1
fi

if [ "$mode" = "nosocket" ]; then
    trap 'exit 0' TERM INT
    while :; do sleep 0.1; done
fi

path="${XDG_RUNTIME_DIR:?}/$socket"

if [ -n "${MOCK_WESTON_SOCKET_LOG:-}" ]; then
    printf 'SOCKET=%s\n' "$path" >>"$MOCK_WESTON_SOCKET_LOG"
fi

exec python3 - "$path" <<'PY'
import signal
import socket
import sys
import time

path = sys.argv[1]
server = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
server.bind(path)
server.listen(8)
signal.signal(signal.SIGTERM, lambda *_: sys.exit(0))
signal.signal(signal.SIGINT, lambda *_: sys.exit(0))

while True:
    time.sleep(0.1)
PY
MOCK

    cat >"$MOCKS/dotnet" <<'MOCK'
#!/usr/bin/env bash
set -uo pipefail

mode=${MOCK_APP_MODE:-ok}
frames=""
validation=0
arguments=("$@")
index=0

while [ "$index" -lt "${#arguments[@]}" ]; do
    case "${arguments[$index]}" in
        --frames)
            index=$((index + 1))
            frames=${arguments[$index]}
            ;;
        --frames=*) frames=${arguments[$index]#*=} ;;
        --validation) validation=1 ;;
    esac

    index=$((index + 1))
done

if [ -n "${MOCK_APP_PIDFILE:-}" ]; then
    printf '%s\n' "$$" >"$MOCK_APP_PIDFILE"
fi

# The harness launches "dotnet <assembly> <args...>". Record only the
# arguments after the assembly so forwarding can be compared exactly.
if [ -n "${MOCK_APP_ARGS_LOG:-}" ]; then
    : >"$MOCK_APP_ARGS_LOG"

    for argument in "${arguments[@]:1}"; do
        printf '%s\n' "$argument" >>"$MOCK_APP_ARGS_LOG"
    done
fi

printf 'VOLT mock sample\n'
printf 'GPU: Mock Radeon RX 9070 XT\n'

if [ "$mode" != "novalidation" ]; then
    if [ "$validation" -eq 1 ]; then
        printf 'Validation: True\n'
    else
        printf 'Validation: False\n'
    fi
fi

case "$mode" in
    fail)
        printf 'mock application: device creation failed\n' >&2
        exit 3
        ;;
    timeout)
        trap 'exit 0' TERM INT
        while :; do sleep 0.1; done
        ;;
    validation)
        printf 'Validation Error: [ VUID-VkDeviceCreateInfo-pNext-02829 ] mock\n' >&2
        printf 'Presented %s accepted frames.\n' "${frames:-0}"
        exit 0
        ;;
    wrongframes)
        printf 'Presented 2 accepted frames.\n'
        exit 0
        ;;
    ok)
        printf 'Swapchain: 1280x720\n'
        sleep 0.3
        printf 'Frames presented: 1, FPS: 60.0\n'
        sleep 0.9
        printf 'Presented %s accepted frames.\n' "${frames:-0}"
        exit 0
        ;;
    orphan)
        sleep 100 &
        printf '%s\n' "$!" >"${MOCK_APP_CHILD_PIDFILE:?MOCK_APP_CHILD_PIDFILE is required}"
        printf 'Frames presented: 1, FPS: 60.0\n'
        printf 'Presented %s accepted frames.\n' "${frames:-0}"
        exit 0
        ;;
    novalidation)
        printf 'Presented %s accepted frames.\n' "${frames:-0}"
        exit 0
        ;;
    *)
        printf 'unknown mock application mode\n' >&2
        exit 1
        ;;
esac
MOCK

    cat >"$MOCKS/weston-screenshooter" <<'MOCK'
#!/usr/bin/env bash
set -uo pipefail

mode=${MOCK_SHOOTER_MODE:-ok}

case "$mode" in
    fail)
        printf 'mock screenshooter: capture failed\n' >&2
        exit 1
        ;;
    nothing)
        exit 0
        ;;
esac

exec python3 - "$PWD/mock-screenshot.png" "$mode" <<'PY'
import struct
import sys
import zlib

path = sys.argv[1]
mode = sys.argv[2]
width, height = 1280, 720
background = (16, 16, 20)
foreground = (220, 90, 60)
runs = ((300, 490), (560, 720), (770, 960))

background_row = bytes(background) * width
content_rows = {}

for y in range(200, 481):
    row = bytearray(background_row)

    for start, end in runs:
        row[start * 3:(end + 1) * 3] = bytes(foreground) * (end - start + 1)

    content_rows[y] = bytes(row)

raw = bytearray()

for y in range(height):
    raw.append(0)
    raw += content_rows.get(y, background_row)


def chunk(tag, payload):
    return (struct.pack(">I", len(payload)) + tag + payload +
            struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF))


png = b"\x89PNG\r\n\x1a\n"
png += chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
png += chunk(b"IDAT", zlib.compress(bytes(raw), 6))
png += chunk(b"IEND", b"")

if mode == "crc":
    corrupted = bytearray(png)
    offset = corrupted.find(b"IDAT") + 4
    corrupted[offset] ^= 0xFF
    png = bytes(corrupted)
elif mode == "no-iend":
    png = png[:-12]
elif mode == "truncated":
    png = png[:-(12 + 8)]

with open(path, "wb") as handle:
    handle.write(png)
PY
MOCK

    cat >"$MOCKS/rm" <<'MOCK'
#!/usr/bin/env bash
set -uo pipefail

recursive=0

for argument in "$@"; do
    case "$argument" in
        -*r*) recursive=1 ;;
    esac
done

if [ "${MOCK_RM_MODE:-ok}" = "fail-runtime" ] && [ "$recursive" -eq 1 ]; then
    for argument in "$@"; do
        case "$argument" in
            */volt-headless-runtime.*)
                printf 'mock rm: cannot remove %s: Permission denied\n' "$argument" >&2
                exit 1
                ;;
        esac
    done
fi

exec "@REAL_RM@" "$@"
MOCK

    sed -i "s|@REAL_RM@|$REAL_RM|" "$MOCKS/rm"

    chmod 755 "$MOCKS/weston" "$MOCKS/dotnet" "$MOCKS/weston-screenshooter" \
        "$MOCKS/rm"
}

install_mocks

# --- usage and argument validation -----------------------------------------

begin_case "help"
run_harness --help
assert_rc 0
assert_contains "$CASE_DIR/console.out" "Usage:"
assert_contains "$CASE_DIR/console.out" "--screenshots"
end_case

begin_case "unknown-argument"
run_default --frobnicate
assert_rc 2
assert_contains "$CASE_DIR/console.err" "unknown argument: --frobnicate"
end_case

begin_case "invalid-frames"
run_default --frames 0
assert_rc 2
assert_contains "$CASE_DIR/console.err" "--frames requires a positive integer"
end_case

begin_case "missing-project-value"
run_harness --project
assert_rc 2
assert_contains "$CASE_DIR/console.err" "option --project requires a value"
end_case

begin_case "project-without-csproj"
mkdir -p "$CASE_DIR/empty"
run_harness --project "$CASE_DIR/empty" --output-dir "$CASE_DIR/out"
assert_rc 2
assert_contains "$CASE_DIR/console.err" "expected exactly one .csproj"
end_case

begin_case "clusters-without-screenshots"
run_default --expect-clusters 3
assert_rc 2
assert_contains "$CASE_DIR/console.err" "--expect-clusters requires --screenshots"
end_case

begin_case "missing-dependency"
LIMITED="$CASE_DIR/bin"
mkdir -p "$LIMITED"

# Only ordinary utilities are available. dotnet comes from the mock above,
# so this test still reaches the Weston check on a host without .NET.
for dependency in bash dirname basename find sort mktemp timeout grep sed cat head; do
    source=$(command -v "$dependency" 2>/dev/null) || continue
    ln -s "$source" "$LIMITED/$dependency"
done

ln -s "$MOCKS/dotnet" "$LIMITED/dotnet"
[ -x "$LIMITED/dotnet" ] || fail "mock dotnet is not on the restricted PATH"

PATH="$LIMITED" TMPDIR="$CASE_DIR/tmp" \
    "$HARNESS" --project "$CASE_DIR/sample" \
    --output-dir "$CASE_DIR/out" \
    >"$CASE_DIR/console.out" 2>"$CASE_DIR/console.err"
RC=$?
assert_rc 3
assert_contains "$CASE_DIR/console.err" "missing required dependencies: weston"
end_case

begin_case "missing-build-output"
rm -f "$CASE_DIR/sample/bin/Release/net10.0/Mock.Sample.dll"
run_default
assert_rc 3
assert_contains "$CASE_DIR/console.err" "no Release build found for Mock.Sample"
end_case

# --- lifecycle ---------------------------------------------------------------

begin_case "success-with-screenshot"
run_default --frames 5 --validation --screenshots --expect-clusters 3
assert_rc 0
assert_run_dir
assert_contains "$CASE_DIR/console.out" "result:     PASS"
assert_contains "$RUN_DIR/summary.txt" "result=PASS"
assert_contains "$RUN_DIR/summary.txt" "frames_reported=Presented 5 accepted frames"
assert_contains "$RUN_DIR/summary.txt" "validation_line=True"
assert_contains "$RUN_DIR/summary.txt" "validation_diagnostics=0"
assert_contains "$RUN_DIR/image-analysis.log" "IMAGE_OK"
assert_contains "$RUN_DIR/image-analysis.log" "IMAGE_CONTENT_PERCENT"
assert_file "$RUN_DIR/app.stdout.log"
assert_file "$RUN_DIR/app.stderr.log"
assert_file "$RUN_DIR/weston.log"
assert_file "$RUN_DIR/invocation.log"
assert_contains "$CASE_DIR/socket.log" "$CASE_DIR/tmp/volt-headless-runtime."
assert_process_dead "$CASE_DIR/app.pid"
assert_process_dead "$CASE_DIR/weston.pid"
assert_runtime_clean
end_case

begin_case "success-without-screenshot"
run_default --frames 3
assert_rc 0
assert_run_dir
assert_contains "$RUN_DIR/summary.txt" "result=PASS"
assert_no_file "$RUN_DIR/image-analysis.log"
assert_runtime_clean
end_case

begin_case "surviving-child-is-terminated"
APP_MODE=orphan
run_default --frames 3
assert_rc 0
assert_run_dir
assert_contains "$RUN_DIR/summary.txt" "application_status=0"
assert_file "$CASE_DIR/app-child.pid"
assert_process_dead "$CASE_DIR/app-child.pid"
assert_process_dead "$CASE_DIR/app.pid"
assert_process_dead "$CASE_DIR/weston.pid"
assert_runtime_clean
end_case

begin_case "application-failure"
APP_MODE=fail
run_default --frames 3
assert_rc 7
assert_run_dir
assert_contains "$RUN_DIR/summary.txt" "result=FAIL"
assert_contains "$RUN_DIR/app.stderr.log" "mock application: device creation failed"
assert_process_dead "$CASE_DIR/app.pid"
assert_runtime_clean
end_case

begin_case "application-timeout"
APP_MODE=timeout
run_default --frames 3 --timeout-seconds 2
assert_rc 8
assert_run_dir
assert_contains "$RUN_DIR/summary.txt" "result=FAIL"
assert_process_dead "$CASE_DIR/app.pid"
assert_process_dead "$CASE_DIR/weston.pid"
assert_runtime_clean
end_case

begin_case "validation-diagnostics"
APP_MODE=validation
run_default --frames 3 --validation
assert_rc 9
assert_run_dir
assert_contains "$RUN_DIR/summary.txt" "result=FAIL"
assert_contains "$RUN_DIR/summary.txt" "VUID"
end_case

begin_case "validation-not-confirmed"
APP_MODE=novalidation
run_default --frames 3 --validation
assert_rc 9
assert_run_dir
assert_contains "$RUN_DIR/summary.txt" "result=FAIL"
assert_contains "$RUN_DIR/summary.txt" "did not report Validation: True"
assert_contains "$RUN_DIR/summary.txt" "reported: no Validation line"
assert_runtime_clean
end_case

begin_case "frame-count-mismatch"
APP_MODE=wrongframes
run_default --frames 5
assert_rc 11
assert_run_dir
assert_contains "$RUN_DIR/summary.txt" "expected 5 accepted frames"
assert_contains "$RUN_DIR/summary.txt" "Presented 2 accepted frames"
end_case

begin_case "screenshot-tool-failure"
SHOOTER_MODE=fail
run_default --frames 5 --screenshots
assert_rc 10
assert_run_dir
assert_contains "$RUN_DIR/summary.txt" "screenshot tool exited with status 1"
assert_contains "$RUN_DIR/summary.txt" "application_status=0"
assert_process_dead "$CASE_DIR/app.pid"
assert_runtime_clean
end_case

begin_case "screenshot-missing"
SHOOTER_MODE=nothing
run_default --frames 5 --screenshots
assert_rc 10
assert_contains "$CASE_DIR/console.err" "screenshot tool produced no PNG"
end_case

begin_case "screenshot-cluster-mismatch"
run_default --frames 5 --screenshots --expect-clusters 2
assert_rc 10
assert_contains "$CASE_DIR/console.err" "expected 2 content clusters"
end_case

begin_case "screenshot-corrupt-crc"
SHOOTER_MODE=crc
run_default --frames 5 --screenshots
assert_rc 10
assert_run_dir
assert_contains "$CASE_DIR/console.err" "has a bad CRC"
assert_file "$RUN_DIR/image-analysis.log"
assert_process_dead "$CASE_DIR/app.pid"
assert_runtime_clean
end_case

begin_case "screenshot-missing-iend"
SHOOTER_MODE=no-iend
run_default --frames 5 --screenshots
assert_rc 10
assert_contains "$CASE_DIR/console.err" "missing IEND chunk"
assert_runtime_clean
end_case

begin_case "screenshot-truncated-chunk"
SHOOTER_MODE=truncated
run_default --frames 5 --screenshots
assert_rc 10
assert_contains "$CASE_DIR/console.err" "truncated PNG chunk"
assert_runtime_clean
end_case

begin_case "compositor-failure"
WESTON_MODE=fail
run_default --frames 3
assert_rc 4
assert_contains "$CASE_DIR/console.err" "compositor exited before creating its socket"
assert_no_file "$CASE_DIR/app.pid"
assert_process_dead "$CASE_DIR/weston.pid"
assert_runtime_clean
end_case

begin_case "compositor-socket-timeout"
WESTON_MODE=nosocket
run_default --frames 3 --timeout-seconds 2
assert_rc 5
assert_contains "$CASE_DIR/console.err" "compositor did not create"
assert_no_file "$CASE_DIR/app.pid"
assert_process_dead "$CASE_DIR/weston.pid"
assert_runtime_clean
end_case

# --- teardown, preservation, isolation --------------------------------------

begin_case "terminate-cleans-up"
APP_MODE=timeout
mkdir -p "$CASE_DIR/out"

PATH="$MOCKS:$BASE_PATH" \
TMPDIR="$CASE_DIR/tmp" \
MOCK_APP_MODE="$APP_MODE" \
MOCK_WESTON_MODE=ok \
MOCK_APP_PIDFILE="$CASE_DIR/app.pid" \
MOCK_WESTON_PIDFILE="$CASE_DIR/weston.pid" \
MOCK_WESTON_SOCKET_LOG="$CASE_DIR/socket.log" \
    "$HARNESS" --project "$CASE_DIR/sample" \
    --output-dir "$CASE_DIR/out" --frames 3 --timeout-seconds 60 \
    >"$CASE_DIR/console.out" 2>"$CASE_DIR/console.err" &
HARNESS_PID=$!

APP_STDOUT=""

for _ in $(seq 1 200); do
    APP_STDOUT=$(find "$CASE_DIR/out" -name app.stdout.log -print -quit 2>/dev/null)

    if [ -n "$APP_STDOUT" ] && grep -q "Frames presented" "$APP_STDOUT" 2>/dev/null; then
        break
    fi

    sleep 0.05
done

kill -TERM "$HARNESS_PID" 2>/dev/null
wait "$HARNESS_PID"
RC=$?

assert_rc 143
assert_process_dead "$CASE_DIR/app.pid"
assert_process_dead "$CASE_DIR/weston.pid"
assert_runtime_clean
end_case

begin_case "cleanup-fail"
RM_MODE=fail-runtime
run_default --frames 3
assert_rc 12
assert_run_dir
assert_contains "$RUN_DIR/summary.txt" "result=FAIL"
assert_contains "$RUN_DIR/summary.txt" "exit_code=12"
assert_contains "$RUN_DIR/summary.txt" "application_status=0"
assert_contains "$RUN_DIR/summary.txt" \
    "cleanup: could not remove private runtime directory:"
assert_contains "$CASE_DIR/console.err" \
    "cleanup: could not remove private runtime directory:"
assert_process_dead "$CASE_DIR/app.pid"
assert_process_dead "$CASE_DIR/weston.pid"

CLEANUP_RECORDS=$(grep -c '^failure=cleanup:' "$RUN_DIR/summary.txt" 2>/dev/null)
[ "$CLEANUP_RECORDS" -eq 1 ] ||
    fail "expected one cleanup failure record, found ${CLEANUP_RECORDS:-0}"

LEFTOVER=$(find "$CASE_DIR/tmp" -maxdepth 1 -name 'volt-headless-runtime.*' -print -quit)
[ -n "$LEFTOVER" ] || fail "controlled cleanup failure left no runtime directory"
rm -rf -- "$LEFTOVER"
end_case

begin_case "cleanup-fail-code"
APP_MODE=fail
RM_MODE=fail-runtime
run_default --frames 3
assert_rc 7
assert_run_dir
assert_contains "$RUN_DIR/summary.txt" "exit_code=7"
assert_contains "$RUN_DIR/summary.txt" "application exited with status 3"
assert_contains "$RUN_DIR/summary.txt" \
    "cleanup: could not remove private runtime directory:"
assert_contains "$CASE_DIR/console.err" "application exited with status 3"
assert_contains "$CASE_DIR/console.err" \
    "cleanup: could not remove private runtime directory:"

CLEANUP_RECORDS=$(grep -c '^failure=cleanup:' "$RUN_DIR/summary.txt" 2>/dev/null)
[ "$CLEANUP_RECORDS" -eq 1 ] ||
    fail "expected one cleanup failure record, found ${CLEANUP_RECORDS:-0}"

APP_RECORDS=$(grep -c '^failure=application exited with status 3' \
    "$RUN_DIR/summary.txt" 2>/dev/null)
[ "$APP_RECORDS" -eq 1 ] ||
    fail "expected one application failure record, found ${APP_RECORDS:-0}"

LEFTOVER=$(find "$CASE_DIR/tmp" -maxdepth 1 -name 'volt-headless-runtime.*' -print -quit)
[ -n "$LEFTOVER" ] || fail "controlled cleanup failure left no runtime directory"
rm -rf -- "$LEFTOVER"
end_case

begin_case "preserves-unrelated-paths"
mkdir -p "$CASE_DIR/tmp/precious" "$CASE_DIR/out/precious"
printf 'keep\n' >"$CASE_DIR/tmp/precious/sentinel"
printf 'keep\n' >"$CASE_DIR/out/precious/sentinel"

run_default --frames 3
assert_rc 0
assert_file "$CASE_DIR/tmp/precious/sentinel"
assert_file "$CASE_DIR/out/precious/sentinel"
assert_runtime_clean
end_case

begin_case "consecutive-isolation"
run_default --frames 3
assert_rc 0

FIRST_SOCKET=$(grep '^SOCKET=' "$CASE_DIR/socket.log" | tail -n 1)
FIRST_RUN=$(find "$CASE_DIR/out" -maxdepth 1 -type d -name 'run-*' -print | sort | tail -n 1)

run_default --frames 3
assert_rc 0

SECOND_SOCKET=$(grep '^SOCKET=' "$CASE_DIR/socket.log" | tail -n 1)
SECOND_RUN=$(find "$CASE_DIR/out" -maxdepth 1 -type d -name 'run-*' -print | sort | tail -n 1)
RUN_DIRS=$(find "$CASE_DIR/out" -maxdepth 1 -type d -name 'run-*' -print | wc -l)

[ "$FIRST_SOCKET" != "$SECOND_SOCKET" ] || fail "both runs reused the same socket"
[ "$FIRST_RUN" != "$SECOND_RUN" ] || fail "both runs reused the same artifact directory"
[ "$RUN_DIRS" -eq 2 ] || fail "expected 2 run directories, found $RUN_DIRS"
assert_runtime_clean
end_case

begin_case "concurrent-isolation"
CONCURRENT_TMP="$CASE_DIR/shared-tmp"
mkdir -p "$CONCURRENT_TMP" "$CASE_DIR/project-a/bin/Release/net10.0" \
    "$CASE_DIR/project-b/bin/Release/net10.0"
cp "$CASE_DIR/sample/Mock.Sample.csproj" "$CASE_DIR/project-a/Concurrent.A.csproj"
cp "$CASE_DIR/sample/Mock.Sample.csproj" "$CASE_DIR/project-b/Concurrent.B.csproj"
: >"$CASE_DIR/project-a/bin/Release/net10.0/Concurrent.A.dll"
: >"$CASE_DIR/project-b/bin/Release/net10.0/Concurrent.B.dll"

start_concurrent() {
    local project=$1
    local output=$2

    PATH="$MOCKS:$BASE_PATH" \
    TMPDIR="$CONCURRENT_TMP" \
    MOCK_APP_MODE=ok \
    MOCK_WESTON_MODE=ok \
    MOCK_APP_PIDFILE="$project/app.pid" \
    MOCK_WESTON_PIDFILE="$project/weston.pid" \
    MOCK_WESTON_SOCKET_LOG="$project/socket.log" \
        "$HARNESS" --project "$project" --output-dir "$output" \
        --frames 3 >"$project/console.out" 2>"$project/console.err" &
}

start_concurrent "$CASE_DIR/project-a" "$CASE_DIR/out-a"
PID_A=$!
start_concurrent "$CASE_DIR/project-b" "$CASE_DIR/out-b"
PID_B=$!

wait "$PID_A"
RC_A=$?
wait "$PID_B"
RC_B=$?

[ "$RC_A" -eq 0 ] || fail "first concurrent run exited with $RC_A"
[ "$RC_B" -eq 0 ] || fail "second concurrent run exited with $RC_B"

SOCKET_A=$(grep '^SOCKET=' "$CASE_DIR/project-a/socket.log" | tail -n 1)
SOCKET_B=$(grep '^SOCKET=' "$CASE_DIR/project-b/socket.log" | tail -n 1)

[ "$SOCKET_A" != "$SOCKET_B" ] || fail "concurrent runs shared a socket"
[ -z "$(find "$CONCURRENT_TMP" -maxdepth 1 -name 'volt-headless-runtime.*' -print)" ] ||
    fail "concurrent runs left private runtime directories behind"
end_case

# --- compositor shell and sample argument forwarding -------------------------

begin_case "default-kiosk-shell"
run_default --frames 3
assert_rc 0
assert_run_dir
assert_contains "$CASE_DIR/weston.args" "--shell=kiosk"
assert_contains "$CASE_DIR/weston.args" "--no-config"
assert_not_contains "$CASE_DIR/weston.args" "weston-desktop.ini"
assert_contains "$RUN_DIR/summary.txt" "compositor=weston-headless-1280x720"
assert_contains "$RUN_DIR/summary.txt" "weston_shell=kiosk"
assert_not_contains "$RUN_DIR/summary.txt" "weston-headless-kiosk"
assert_contains "$RUN_DIR/invocation.log" "weston_shell=kiosk"
assert_no_file "$RUN_DIR/weston-desktop.ini"
assert_runtime_clean
end_case

begin_case "desktop-shell"
run_default --frames 3 --weston-shell desktop
assert_rc 0
assert_run_dir
assert_contains "$CASE_DIR/weston.args" "--shell=desktop"
assert_contains "$CASE_DIR/weston.args" "weston-desktop.ini"
assert_not_contains "$CASE_DIR/weston.args" "--no-config"
assert_file "$RUN_DIR/weston-desktop.ini"
assert_contains "$RUN_DIR/weston-desktop.ini" "panel-position=none"
assert_contains "$RUN_DIR/summary.txt" "compositor=weston-headless-1280x720"
assert_contains "$RUN_DIR/summary.txt" "weston_shell=desktop"
assert_contains "$RUN_DIR/invocation.log" "weston_shell=desktop"
assert_contains "$RUN_DIR/invocation.log" "weston_config=$RUN_DIR/weston-desktop.ini"
assert_contains "$CASE_DIR/console.out" "shell desktop"
assert_process_dead "$CASE_DIR/app.pid"
assert_process_dead "$CASE_DIR/weston.pid"
assert_runtime_clean
end_case

begin_case "unsupported-shell"
run_default --frames 3 --weston-shell ivi
assert_rc 2
assert_contains "$CASE_DIR/console.err" "--weston-shell must be kiosk or desktop"
assert_no_file "$CASE_DIR/weston.args"
end_case

begin_case "sample-arg-missing-value"
run_harness --project "$CASE_DIR/sample" --output-dir "$CASE_DIR/out" \
    --sample-arg
assert_rc 2
assert_contains "$CASE_DIR/console.err" "option --sample-arg requires a value"
end_case

begin_case "sample-arg-empty-value"
run_default --frames 3 --sample-arg=
assert_rc 2
assert_contains "$CASE_DIR/console.err" "--sample-arg requires a value"
end_case

begin_case "sample-arg-forwarding"
run_default --frames 7 --validation \
    --sample-arg=--resize-at-frame --sample-arg 200 \
    --sample-arg=--resize-to --sample-arg=960x540 \
    --sample-arg="two words"
assert_rc 0
assert_run_dir
assert_contains "$RUN_DIR/summary.txt" \
    "frames_reported=Presented 7 accepted frames"
assert_contains "$RUN_DIR/summary.txt" "validation_line=True"
assert_contains "$RUN_DIR/invocation.log" "sample_arg=--resize-at-frame"
assert_contains "$RUN_DIR/invocation.log" "sample_arg=two words"

printf '%s\n' \
    "--frames" "7" "--validation" \
    "--resize-at-frame" "200" "--resize-to" "960x540" "two words" \
    >"$CASE_DIR/expected.args"

if ! cmp -s "$CASE_DIR/expected.args" "$CASE_DIR/app.args"; then
    fail "forwarded arguments differ from the requested array"
fi

assert_contains "$CASE_DIR/console.out" "sample arguments: --resize-at-frame 200"
assert_process_dead "$CASE_DIR/app.pid"
assert_runtime_clean
end_case

begin_case "sample-arg-forwarding-on-failure"
APP_MODE=fail
run_default --frames 3 \
    --sample-arg=--resize-at-frame --sample-arg=100 \
    --sample-arg=--resize-to --sample-arg=960x540
assert_rc 7
assert_run_dir
assert_contains "$RUN_DIR/summary.txt" "application exited with status 3"
assert_contains "$RUN_DIR/app.stderr.log" \
    "mock application: device creation failed"
assert_contains "$CASE_DIR/app.args" "--resize-at-frame"
assert_contains "$CASE_DIR/app.args" "960x540"
assert_process_dead "$CASE_DIR/app.pid"
assert_runtime_clean
end_case

begin_case "desktop-shell-cleanup"
APP_MODE=timeout
run_default --frames 3 --timeout-seconds 2 --weston-shell desktop
assert_rc 8
assert_run_dir
assert_contains "$RUN_DIR/summary.txt" "weston_shell=desktop"
assert_process_dead "$CASE_DIR/app.pid"
assert_process_dead "$CASE_DIR/weston.pid"
assert_runtime_clean
end_case

# --- summary -----------------------------------------------------------------

echo
printf '%d passed, %d failed\n' "$PASSED" "$FAILED"

if [ "$FAILED" -ne 0 ]; then
    exit 1
fi
