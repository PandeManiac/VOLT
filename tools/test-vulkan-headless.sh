#!/usr/bin/env bash
#
# VOLT headless Vulkan test harness.
#
# Runs a VOLT Vulkan sample against a private headless Weston compositor,
# captures logs and screenshots, tears everything down, and returns an
# exit code that identifies the failing phase.
#
# The caller's desktop session is never used or modified: the harness
# creates its own XDG_RUNTIME_DIR and Wayland socket, points the sample
# at them, and removes them again before exiting.
#
# See tools/README.md for the full contract.
set -uo pipefail

SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
REPO_ROOT=$(cd -- "$SCRIPT_DIR/.." && pwd -P)
PROGRAM=$(basename -- "${BASH_SOURCE[0]}")

readonly EXIT_OK=0
readonly EXIT_FAILURE=1
readonly EXIT_USAGE=2
readonly EXIT_DEPENDENCY=3
readonly EXIT_COMPOSITOR=4
readonly EXIT_SOCKET=5
readonly EXIT_APP_START=6
readonly EXIT_APP_FAILED=7
readonly EXIT_TIMEOUT=8
readonly EXIT_VALIDATION=9
readonly EXIT_SCREENSHOT=10
readonly EXIT_FRAMES=11
readonly EXIT_CLEANUP=12

PROJECT_DIR=""
FRAMES=600
VALIDATION=0
SCREENSHOTS=0
EXPECT_CLUSTERS=""
WESTON_SHELL=kiosk
SAMPLE_ARGS=()
OUTPUT_DIR="$REPO_ROOT/artifacts/headless"
TIMEOUT_SECONDS=180
WIDTH=1280
HEIGHT=720

RUN_DIR=""
RUNTIME_DIR=""
SOCKET=""
SOCKET_PATH=""
WESTON_PID=""
APP_PID=""
DEADLINE=0

RESULT_CODE=0
FAILURES=()
GPU_LINE=""
VALIDATION_LINE=""
APP_STATUS=""
FRAMES_REPORTED=""
VALIDATION_HITS=0
VALIDATION_EXCERPT=""
SCREENSHOT_PATH=""
SCREENSHOT_SUMMARY=""
ORIGINAL_ARGS=("$@")

usage() {
    cat <<EOF
Usage: $PROGRAM --project PATH [options]

Runs a VOLT Vulkan sample against a private, headless Wayland compositor.

The harness never touches the caller's desktop session: it creates a
private XDG_RUNTIME_DIR and Wayland socket, forces the application onto
that socket, and removes everything it created before exiting.

Required:
  --project PATH          Sample project directory containing one .csproj.
                          Its Release build must already exist.

Options:
  --frames N              Frames the sample must present (default: 600).
  --validation            Pass --validation to the sample.
  --screenshots           Capture and verify a screenshot while it runs.
  --expect-clusters N     Require N horizontal content clusters in the
                          screenshot (use 3 for Volt.VulkanMeshDemo).
  --weston-shell NAME     Shell for the private headless Weston: kiosk
                          (default) or desktop. The desktop shell runs
                          without its panel so the sample window covers
                          the whole output and can be resized by the
                          sample. It is opt-in and never changes the
                          caller's desktop.
  --sample-arg VALUE      Extra argument forwarded to the sample unchanged.
                          Repeat it once per argument; --sample-arg=VALUE
                          is also accepted. Values are forwarded as a
                          separate array element and never re-split.
  --output-dir PATH       Artifact root (default: <repo>/artifacts/headless).
  --timeout-seconds N     Bound for the whole run (default: 180).
  -h, --help              Show this help.

Sample contract:
  The sample is launched as:
    dotnet <assembly>.dll --frames N [--validation] [--sample-arg values]
  It must print "Presented N accepted frames." when it finishes and remain
  alive long enough to be captured when --screenshots is used. With
  --validation it must print "Validation: True"; missing or false
  confirmation fails the run. Samples that do not accept these arguments
  cannot be run by this harness.

Exit codes:
   0 success                 7 application failed
   2 usage error             8 timeout
   3 missing dependency      9 Vulkan validation diagnostics
   4 compositor failed      10 screenshot capture/verification failed
   5 compositor socket      11 presented frame count mismatch
   6 application start      12 cleanup failure
EOF
}

log() {
    printf '[headless] %s\n' "$*"
}

log_error() {
    printf '[headless] error: %s\n' "$*" >&2
}

record_failure() {
    local code=$1
    shift

    if [ "$RESULT_CODE" -eq 0 ]; then
        RESULT_CODE=$code
    fi

    FAILURES+=("$*")
    log_error "$*"
}

usage_error() {
    log_error "$*"
    usage >&2
    exit "$EXIT_USAGE"
}

require_value() {
    if [ "$2" -lt 2 ] || [ -z "${3-}" ]; then
        usage_error "option $1 requires a value"
    fi
}

is_positive_integer() {
    [[ "$1" =~ ^[1-9][0-9]*$ ]]
}

now() {
    date +%s
}

remaining_seconds() {
    local left=$((DEADLINE - $(now)))

    if [ "$left" -lt 1 ]; then
        left=1
    fi

    printf '%s' "$left"
}

process_alive() {
    local pid=$1
    local state

    [ -n "$pid" ] || return 1
    [ -r "/proc/$pid/stat" ] || return 1

    state=$(awk '{ print $3 }' "/proc/$pid/stat" 2>/dev/null) || return 1
    [ "$state" != "Z" ]
}

group_alive() {
    local pgid=$1

    [ -n "$pgid" ] || return 1
    kill -0 -- "-$pgid" 2>/dev/null
}

# Stops a process group started by this harness. The background commands
# are launched with job control enabled, so each one leads its own process
# group and no unrelated process can be matched by name.
#
# Only the group is signalled. The leader stays alive until teardown (the
# application runs behind a supervisor that holds the group open), so the
# group ID cannot be recycled for an unrelated process while it is ours to
# clean up. Falling back to a bare PID signal would risk exactly that.
stop_group() {
    local pid=$1
    local name=$2
    local grace=${3:-5}
    local deadline

    [ -n "$pid" ] || return 0

    if process_alive "$pid" || group_alive "$pid"; then
        kill -TERM -- "-$pid" 2>/dev/null || true
    fi

    deadline=$((SECONDS + grace))
    while [ "$SECONDS" -lt "$deadline" ]; do
        if ! process_alive "$pid" && ! group_alive "$pid"; then
            break
        fi

        sleep 0.05
    done

    if process_alive "$pid" || group_alive "$pid"; then
        log "force-terminating $name (pid $pid)"
        kill -KILL -- "-$pid" 2>/dev/null || true
    fi

    wait "$pid" 2>/dev/null || true

    deadline=$((SECONDS + 3))
    while [ "$SECONDS" -lt "$deadline" ] && group_alive "$pid"; do
        sleep 0.05
    done

    if group_alive "$pid"; then
        return 1
    fi

    return 0
}

# Releases every resource owned by this invocation. Safe to call twice;
# it is called explicitly before the summary and again from the EXIT trap.
# Each failure is recorded in FAILURES so it reaches summary.txt as well as
# stderr. The first failure encountered keeps its exit code; record_failure
# only assigns code 12 when nothing else has failed.
teardown() {
    local failures=0
    local pid
    local runtime_dir

    if [ -n "$APP_PID" ]; then
        pid=$APP_PID

        if ! stop_group "$pid" "application" 5; then
            record_failure "$EXIT_CLEANUP" \
                "cleanup: application process group (pid $pid) survived teardown"
            failures=1
        fi

        APP_PID=""
    fi

    if [ -n "$WESTON_PID" ]; then
        pid=$WESTON_PID

        if ! stop_group "$pid" "compositor" 5; then
            record_failure "$EXIT_CLEANUP" \
                "cleanup: compositor process group (pid $pid) survived teardown"
            failures=1
        fi

        WESTON_PID=""
    fi

    if [ -n "$RUNTIME_DIR" ] && [ -d "$RUNTIME_DIR" ]; then
        runtime_dir=$RUNTIME_DIR

        if [ -n "$SOCKET_PATH" ]; then
            rm -f -- "$SOCKET_PATH" "$SOCKET_PATH.lock" 2>/dev/null || true
        fi

        if [ "$failures" -eq 0 ]; then
            case "$runtime_dir" in
                "${TMP_BASE:-/tmp}"/volt-headless-runtime.*)
                    if ! rm -rf -- "$runtime_dir"; then
                        record_failure "$EXIT_CLEANUP" \
                            "cleanup: could not remove private runtime directory: $runtime_dir"
                        failures=1
                    fi
                    ;;
                *)
                    record_failure "$EXIT_CLEANUP" \
                        "cleanup: refusing to remove unexpected path: $runtime_dir"
                    failures=1
                    ;;
            esac
        fi

        RUNTIME_DIR=""
    fi

    return "$failures"
}

write_summary() {
    local status=$1
    local verdict="PASS"
    local failure

    if [ "$status" -ne 0 ]; then
        verdict="FAIL"
    fi

    {
        printf 'result=%s\n' "$verdict"
        printf 'exit_code=%s\n' "$status"
        printf 'project=%s\n' "$PROJECT_DIR"
        printf 'run_dir=%s\n' "$RUN_DIR"
        printf 'frames_requested=%s\n' "$FRAMES"
        printf 'frames_reported=%s\n' "${FRAMES_REPORTED:-}"
        printf 'validation_requested=%s\n' "$VALIDATION"
        printf 'validation_line=%s\n' "${VALIDATION_LINE:-}"
        printf 'validation_diagnostics=%s\n' "$VALIDATION_HITS"
        printf 'validation_excerpt=%s\n' "$VALIDATION_EXCERPT"
        printf 'application_status=%s\n' "${APP_STATUS:-}"
        printf 'screenshot=%s\n' "${SCREENSHOT_PATH:-}"
        printf 'screenshot_summary=%s\n' "${SCREENSHOT_SUMMARY:-}"
        printf 'gpu=%s\n' "${GPU_LINE:-}"
        printf 'timeout_seconds=%s\n' "$TIMEOUT_SECONDS"
        printf 'compositor=weston-headless-%sx%s\n' "$WIDTH" "$HEIGHT"
        printf 'weston_shell=%s\n' "$WESTON_SHELL"

        for failure in "${FAILURES[@]}"; do
            printf 'failure=%s\n' "$failure"
        done
    } >"$RUN_DIR/summary.txt"
}

print_summary() {
    local status=$1
    local verdict="PASS"
    local failure

    if [ "$status" -ne 0 ]; then
        verdict="FAIL"
    fi

    echo
    log "project:    $PROJECT_DIR"
    log "application: status=${APP_STATUS:-not started}"
    log "frames:     ${FRAMES_REPORTED:-none} (requested $FRAMES)"
    log "validation: ${VALIDATION_LINE:-not reported}, diagnostics=$VALIDATION_HITS"
    log "gpu:        ${GPU_LINE:-not reported}"

    if [ -n "$SCREENSHOT_PATH" ]; then
        log "screenshot: $SCREENSHOT_PATH"
        log "image:      ${SCREENSHOT_SUMMARY:-not analyzed}"
    fi

    log "artifacts:  $RUN_DIR"

    for failure in "${FAILURES[@]}"; do
        log_error "$failure"
    done

    log "result:     $verdict"
}

finish() {
    local status

    # teardown records any cleanup failure itself, so RESULT_CODE already
    # preserves an earlier failure code and falls back to 12 only when the
    # run would otherwise have succeeded.
    teardown || true

    status=$RESULT_CODE

    write_summary "$status"
    print_summary "$status"

    exit "$status"
}

# shellcheck disable=SC2329  # invoked through trap
handle_signal() {
    local signal=$1
    local code=$2

    trap - INT TERM HUP
    record_failure "$code" "interrupted by SIG$signal"
    finish
}

# shellcheck disable=SC2329  # invoked through trap
cleanup() {
    local status=$?

    trap - EXIT INT TERM HUP

    if ! teardown; then
        log_error "cleanup did not release every owned process"

        if [ "$status" -eq 0 ]; then
            status=$EXIT_CLEANUP
        fi
    fi

    exit "$status"
}

trap cleanup EXIT
trap 'handle_signal INT 130' INT
trap 'handle_signal TERM 143' TERM
trap 'handle_signal HUP 129' HUP

while [ $# -gt 0 ]; do
    case "$1" in
        --project)
            require_value "$1" "$#" "${2-}"
            PROJECT_DIR=$2
            shift 2
            ;;
        --frames)
            require_value "$1" "$#" "${2-}"

            if ! is_positive_integer "$2"; then
                usage_error "--frames requires a positive integer"
            fi

            FRAMES=$2
            shift 2
            ;;
        --validation)
            VALIDATION=1
            shift
            ;;
        --screenshots)
            SCREENSHOTS=1
            shift
            ;;
        --expect-clusters)
            require_value "$1" "$#" "${2-}"

            if ! is_positive_integer "$2"; then
                usage_error "--expect-clusters requires a positive integer"
            fi

            EXPECT_CLUSTERS=$2
            shift 2
            ;;
        --weston-shell)
            require_value "$1" "$#" "${2-}"

            case "$2" in
                kiosk|desktop)
                    WESTON_SHELL=$2
                    ;;
                *)
                    usage_error \
                        "--weston-shell must be kiosk or desktop (got: $2)"
                    ;;
            esac

            shift 2
            ;;
        --sample-arg)
            require_value "$1" "$#" "${2-}"
            SAMPLE_ARGS+=("$2")
            shift 2
            ;;
        --sample-arg=*)
            sample_arg=${1#*=}

            if [ -z "$sample_arg" ]; then
                usage_error "--sample-arg requires a value"
            fi

            SAMPLE_ARGS+=("$sample_arg")
            shift
            ;;
        --output-dir)
            require_value "$1" "$#" "${2-}"
            OUTPUT_DIR=$2
            shift 2
            ;;
        --timeout-seconds)
            require_value "$1" "$#" "${2-}"

            if ! is_positive_integer "$2"; then
                usage_error "--timeout-seconds requires a positive integer"
            fi

            TIMEOUT_SECONDS=$2
            shift 2
            ;;
        -h|--help)
            usage
            exit "$EXIT_OK"
            ;;
        *)
            usage_error "unknown argument: $1"
            ;;
    esac
done

if [ -z "$PROJECT_DIR" ]; then
    usage_error "--project is required"
fi

if [ "$SCREENSHOTS" -eq 0 ] && [ -n "$EXPECT_CLUSTERS" ]; then
    usage_error "--expect-clusters requires --screenshots"
fi

if [ ! -d "$PROJECT_DIR" ]; then
    usage_error "project directory does not exist: $PROJECT_DIR"
fi

PROJECT_DIR=$(cd -- "$PROJECT_DIR" && pwd -P)

PROJECT_FILES=()

while IFS= read -r project_file; do
    PROJECT_FILES+=("$project_file")
done < <(find "$PROJECT_DIR" -maxdepth 1 -type f -name '*.csproj' -print 2>/dev/null | sort)

if [ "${#PROJECT_FILES[@]}" -ne 1 ]; then
    usage_error "expected exactly one .csproj in $PROJECT_DIR, found ${#PROJECT_FILES[@]}"
fi

MISSING=()

for dependency in mktemp timeout dotnet weston; do
    if ! command -v "$dependency" >/dev/null 2>&1; then
        MISSING+=("$dependency")
    fi
done

if [ "$SCREENSHOTS" -eq 1 ]; then
    for dependency in weston-screenshooter python3; do
        if ! command -v "$dependency" >/dev/null 2>&1; then
            MISSING+=("$dependency")
        fi
    done
fi

if [ "${#MISSING[@]}" -gt 0 ]; then
    log_error "missing required dependencies: ${MISSING[*]}"
    exit "$EXIT_DEPENDENCY"
fi

ASSEMBLY_NAME=$(sed -n \
    's:.*<AssemblyName>[[:space:]]*\([^<]*\)[[:space:]]*</AssemblyName>.*:\1:p' \
    "${PROJECT_FILES[0]}" | head -n 1)

if [ -z "$ASSEMBLY_NAME" ]; then
    ASSEMBLY_NAME=$(basename -- "${PROJECT_FILES[0]}" .csproj)
fi

ASSEMBLIES=()

while IFS= read -r assembly; do
    ASSEMBLIES+=("$assembly")
done < <(find "$PROJECT_DIR/bin/Release" -mindepth 2 -maxdepth 2 \
    -type f -name "$ASSEMBLY_NAME.dll" -print 2>/dev/null | sort)

if [ "${#ASSEMBLIES[@]}" -eq 0 ]; then
    log_error "no Release build found for $ASSEMBLY_NAME in $PROJECT_DIR/bin/Release"
    log_error "build it first, for example: dotnet build Volt.slnx -c Release"
    exit "$EXIT_DEPENDENCY"
fi

if [ "${#ASSEMBLIES[@]}" -gt 1 ]; then
    log_error "multiple Release builds found for $ASSEMBLY_NAME:"
    printf '  %s\n' "${ASSEMBLIES[@]}" >&2
    log_error "remove stale target frameworks or pass a cleanly built project"
    exit "$EXIT_DEPENDENCY"
fi

APP_DLL=${ASSEMBLIES[0]}

umask 077

if ! mkdir -p -- "$OUTPUT_DIR"; then
    log_error "cannot create output directory: $OUTPUT_DIR"
    exit "$EXIT_FAILURE"
fi

OUTPUT_DIR=$(cd -- "$OUTPUT_DIR" && pwd -P)
RUN_ID="run-$(date -u +%Y%m%dT%H%M%SZ)-$$-$RANDOM"
RUN_DIR="$OUTPUT_DIR/$RUN_ID"

if ! mkdir -- "$RUN_DIR"; then
    log_error "cannot create run directory: $RUN_DIR"
    exit "$EXIT_FAILURE"
fi

# The kiosk shell runs every top-level window fullscreen, which makes
# client-side resizing ineffective. The desktop shell is opt-in for
# resize scenarios; its panel is disabled so the sample window covers
# the whole output, keeping screenshot verification identical. The
# configuration is private to this run and never touches the caller's
# desktop configuration.
WESTON_CONFIG_ARGS=(--no-config)
WESTON_CONFIG=""

if [ "$WESTON_SHELL" = "desktop" ]; then
    WESTON_CONFIG="$RUN_DIR/weston-desktop.ini"

    if ! printf '[shell]\npanel-position=none\n' >"$WESTON_CONFIG"; then
        log_error "cannot write the private compositor configuration"
        exit "$EXIT_FAILURE"
    fi

    WESTON_CONFIG_ARGS=(-c "$WESTON_CONFIG")
fi

DEADLINE=$(( $(now) + TIMEOUT_SECONDS ))

{
    printf 'timestamp=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    printf 'program=%s\n' "$PROGRAM"
    printf 'repo_root=%s\n' "$REPO_ROOT"
    printf 'project=%s\n' "$PROJECT_DIR"
    printf 'app_dll=%s\n' "$APP_DLL"
    printf 'frames=%s\n' "$FRAMES"
    printf 'validation=%s\n' "$VALIDATION"
    printf 'screenshots=%s\n' "$SCREENSHOTS"
    printf 'expect_clusters=%s\n' "${EXPECT_CLUSTERS:-}"
    printf 'weston_shell=%s\n' "$WESTON_SHELL"
    printf 'weston_config=%s\n' "$WESTON_CONFIG"
    printf 'output_dir=%s\n' "$OUTPUT_DIR"
    printf 'timeout_seconds=%s\n' "$TIMEOUT_SECONDS"
    printf 'weston=%s\n' "$(command -v weston)"
    printf 'dotnet=%s\n' "$(command -v dotnet)"

    for argument in "${ORIGINAL_ARGS[@]}"; do
        printf 'argument=%s\n' "$argument"
    done

    for argument in "${SAMPLE_ARGS[@]}"; do
        printf 'sample_arg=%s\n' "$argument"
    done
} >"$RUN_DIR/invocation.log"

log "run directory: $RUN_DIR"
log "project: $PROJECT_DIR"
log "assembly: $APP_DLL"

TMP_BASE=${TMPDIR:-/tmp}

if ! RUNTIME_DIR=$(mktemp -d "$TMP_BASE/volt-headless-runtime.XXXXXX"); then
    record_failure "$EXIT_FAILURE" "could not create a private runtime directory"
    finish
fi

if ! chmod 700 "$RUNTIME_DIR"; then
    record_failure "$EXIT_FAILURE" "could not restrict the private runtime directory"
    finish
fi

SOCKET="vh-$RANDOM"
SOCKET_PATH="$RUNTIME_DIR/$SOCKET"

if [ "${#SOCKET_PATH}" -ge 100 ]; then
    record_failure "$EXIT_FAILURE" "Wayland socket path is too long: $SOCKET_PATH"
    finish
fi

log "private runtime: $RUNTIME_DIR"
log "private socket:  $SOCKET"

# Job control gives every background command its own process group, so
# teardown can signal exactly the processes this run created.
set -m

XDG_RUNTIME_DIR="$RUNTIME_DIR" \
WAYLAND_DISPLAY="$SOCKET" \
DISPLAY='' \
    weston \
        --backend=headless \
        --renderer=gl \
        --shell="$WESTON_SHELL" \
        --width="$WIDTH" \
        --height="$HEIGHT" \
        --socket="$SOCKET" \
        --idle-time=0 \
        "${WESTON_CONFIG_ARGS[@]}" \
        --debug \
        --log="$RUN_DIR/weston.log" \
        >"$RUN_DIR/weston.stdout.log" 2>&1 &

WESTON_PID=$!

SOCKET_TIMEOUT=$(remaining_seconds)

if [ "$SOCKET_TIMEOUT" -gt 15 ]; then
    SOCKET_TIMEOUT=15
fi

SOCKET_DEADLINE=$(( $(now) + SOCKET_TIMEOUT ))

while [ ! -S "$SOCKET_PATH" ]; do
    if [ "$(now)" -ge "$SOCKET_DEADLINE" ]; then
        record_failure "$EXIT_SOCKET" \
            "compositor did not create $SOCKET_PATH within ${SOCKET_TIMEOUT}s"
        finish
    fi

    if ! process_alive "$WESTON_PID"; then
        record_failure "$EXIT_COMPOSITOR" "compositor exited before creating its socket"
        finish
    fi

    sleep 0.05
done

log "compositor ready (pid $WESTON_PID, shell $WESTON_SHELL, ${WIDTH}x${HEIGHT})"

APP_TIMEOUT=$(remaining_seconds)

if [ "$APP_TIMEOUT" -gt 10 ]; then
    APP_TIMEOUT=$((APP_TIMEOUT - 5))
fi

APP_ARGS=(--frames "$FRAMES")

if [ "$VALIDATION" -eq 1 ]; then
    APP_ARGS+=(--validation)
fi

if [ "${#SAMPLE_ARGS[@]}" -gt 0 ]; then
    APP_ARGS+=("${SAMPLE_ARGS[@]}")
    log "sample arguments: ${SAMPLE_ARGS[*]}"
fi

# The sample runs behind a supervisor that is the leader of the private
# process group. The supervisor records the sample's exit status and then
# stays alive until teardown. That keeps the group ID pinned to a process
# this harness owns, so signalling it later can never hit an unrelated
# group whose ID was recycled, even when the sample left children behind.
APP_STATUS_FILE="$RUN_DIR/app.status"

(
    set +m

    XDG_RUNTIME_DIR="$RUNTIME_DIR" \
    WAYLAND_DISPLAY="$SOCKET" \
    SDL_VIDEODRIVER=wayland \
    DISPLAY='' \
        dotnet "$APP_DLL" "${APP_ARGS[@]}" \
        >"$RUN_DIR/app.stdout.log" 2>"$RUN_DIR/app.stderr.log" &
    app_child=$!

    wait "$app_child"
    app_status=$?

    if printf '%s\n' "$app_status" >"$APP_STATUS_FILE.partial" 2>/dev/null; then
        mv -f -- "$APP_STATUS_FILE.partial" "$APP_STATUS_FILE" 2>/dev/null || true
    fi

    while :; do
        sleep 1
    done
) >/dev/null 2>&1 &

APP_PID=$!
APP_DEADLINE=$(( $(now) + APP_TIMEOUT ))

log "application started (group leader pid $APP_PID, timeout ${APP_TIMEOUT}s)"

READY=0
READY_TIMEOUT=$(remaining_seconds)

if [ "$READY_TIMEOUT" -gt 60 ]; then
    READY_TIMEOUT=60
fi

READY_DEADLINE=$(( $(now) + READY_TIMEOUT ))

while [ "$(now)" -lt "$READY_DEADLINE" ]; do
    if grep -qE 'Frames presented:|Presented [0-9]+ accepted frames' \
            "$RUN_DIR/app.stdout.log" 2>/dev/null; then
        READY=1
        break
    fi

    if [ -f "$APP_STATUS_FILE" ]; then
        break
    fi

    sleep 0.05
done

if [ "$READY" -eq 1 ]; then
    log "first frame presented"
fi

if [ "$SCREENSHOTS" -eq 1 ]; then
    if [ "$READY" -ne 1 ]; then
        record_failure "$EXIT_SCREENSHOT" \
            "application did not present a frame in time; screenshot not captured"
    else
        SHOOT_TIMEOUT=$(remaining_seconds)

        if [ "$SHOOT_TIMEOUT" -gt 20 ]; then
            SHOOT_TIMEOUT=20
        fi

        SHOOTER_STATUS=0

        (
            cd -- "$RUN_DIR" || exit 1
            XDG_RUNTIME_DIR="$RUNTIME_DIR" \
            WAYLAND_DISPLAY="$SOCKET" \
                timeout "$SHOOT_TIMEOUT" weston-screenshooter
        ) >"$RUN_DIR/screenshooter.log" 2>&1 || SHOOTER_STATUS=$?

        SCREENSHOT_ENTRY=$(find "$RUN_DIR" -maxdepth 1 -type f -name '*.png' \
            -printf '%T@ %p\n' 2>/dev/null | sort -n | tail -n 1)
        SCREENSHOT_PATH=""

        if [ -n "$SCREENSHOT_ENTRY" ]; then
            SCREENSHOT_PATH=${SCREENSHOT_ENTRY#* }
        fi

        if [ "$SHOOTER_STATUS" -ne 0 ]; then
            record_failure "$EXIT_SCREENSHOT" \
                "screenshot tool exited with status $SHOOTER_STATUS"
            SCREENSHOT_PATH=""
        elif [ -z "$SCREENSHOT_PATH" ]; then
            record_failure "$EXIT_SCREENSHOT" "screenshot tool produced no PNG"
        else
            log "screenshot captured: $SCREENSHOT_PATH"
        fi
    fi
fi

# The application PID is deliberately kept for teardown: descendants may
# still be running in its group after the direct process has exited.
APP_STATUS=""
APP_TIMED_OUT=0

while [ ! -f "$APP_STATUS_FILE" ]; do
    if [ "$(now)" -ge "$APP_DEADLINE" ]; then
        APP_TIMED_OUT=1
        break
    fi

    sleep 0.05
done

if [ "$APP_TIMED_OUT" -eq 1 ]; then
    APP_STATUS=124
elif [ -r "$APP_STATUS_FILE" ]; then
    APP_STATUS=$(sed -n '1p' "$APP_STATUS_FILE" 2>/dev/null)

    if ! [[ "$APP_STATUS" =~ ^[0-9]+$ ]]; then
        APP_STATUS=125
        record_failure "$EXIT_APP_FAILED" \
            "application did not report a valid exit status"
    fi
else
    APP_STATUS=125
    record_failure "$EXIT_APP_FAILED" "application exit status was not recorded"
fi

log "application exited with status $APP_STATUS"

case "$APP_STATUS" in
    0)
        ;;
    124|137)
        record_failure "$EXIT_TIMEOUT" \
            "application exceeded its ${TIMEOUT_SECONDS}s run budget"
        ;;
    126|127)
        record_failure "$EXIT_APP_START" \
            "application could not be started (status $APP_STATUS)"
        ;;
    *)
        record_failure "$EXIT_APP_FAILED" \
            "application exited with status $APP_STATUS"
        ;;
esac

GPU_LINE=$(sed -n 's/^GPU:[[:space:]]*//p' "$RUN_DIR/app.stdout.log" | head -n 1)
VALIDATION_LINE=$(sed -n 's/^Validation:[[:space:]]*//p' \
    "$RUN_DIR/app.stdout.log" | head -n 1)

FRAMES_REPORTED=$(grep -oE 'Presented [0-9]+ accepted frames' \
    "$RUN_DIR/app.stdout.log" 2>/dev/null | tail -n 1)

VALIDATION_HITS=$(cat "$RUN_DIR/app.stdout.log" "$RUN_DIR/app.stderr.log" 2>/dev/null |
    grep -Eic 'VUID-|validation error|validation warning' || true)

VALIDATION_EXCERPT=$(cat "$RUN_DIR/app.stdout.log" "$RUN_DIR/app.stderr.log" 2>/dev/null |
    grep -Ei 'VUID-|validation error|validation warning' |
    head -n 1 | cut -c 1-200 || true)

if [ "$APP_STATUS" -eq 0 ]; then
    if ! grep -qF "Presented $FRAMES accepted frames" \
            "$RUN_DIR/app.stdout.log" 2>/dev/null; then
        record_failure "$EXIT_FRAMES" \
            "expected $FRAMES accepted frames, found: ${FRAMES_REPORTED:-none}"
    fi
fi

if [ "$VALIDATION_HITS" -gt 0 ]; then
    record_failure "$EXIT_VALIDATION" \
        "found $VALIDATION_HITS Vulkan validation diagnostic(s): " \
        "${VALIDATION_EXCERPT:-no details available}"
fi

if [ "$VALIDATION" -eq 1 ] && [ "$APP_STATUS" -eq 0 ] &&
        [ "$VALIDATION_LINE" != "True" ]; then
    record_failure "$EXIT_VALIDATION" \
        "validation was requested but the application did not report Validation: True" \
        "(reported: ${VALIDATION_LINE:-no Validation line})"
fi

if [ -n "$SCREENSHOT_PATH" ]; then
    IMAGE_STATUS=0

    IMAGE_OUTPUT=$(python3 - "$SCREENSHOT_PATH" "$WIDTH" "$HEIGHT" \
            "${EXPECT_CLUSTERS:-}" <<'PY' 2>&1
import struct
import sys
import zlib
from collections import Counter

path, expected_width, expected_height, expected_clusters = sys.argv[1:5]
expected_width = int(expected_width)
expected_height = int(expected_height)
expected_clusters = int(expected_clusters) if expected_clusters else None


def fail(message):
    print(f"IMAGE_ERROR={message}")
    sys.exit(1)


with open(path, "rb") as handle:
    data = handle.read()

if not data.startswith(b"\x89PNG\r\n\x1a\n"):
    fail("file is not a PNG")

position = 8
chunks = []

while True:
    if position == len(data):
        fail("missing IEND chunk")

    if position + 8 > len(data):
        fail("truncated PNG chunk")

    (length,) = struct.unpack(">I", data[position:position + 4])
    chunk_type = data[position + 4:position + 8]

    if not all(65 <= value <= 90 or 97 <= value <= 122 for value in chunk_type):
        fail("invalid PNG chunk type")

    payload_start = position + 8
    payload_end = payload_start + length
    crc_end = payload_end + 4

    if crc_end > len(data):
        fail("truncated PNG chunk")

    payload = data[payload_start:payload_end]
    (expected_crc,) = struct.unpack(">I", data[payload_end:crc_end])
    actual_crc = zlib.crc32(chunk_type + payload) & 0xFFFFFFFF

    if actual_crc != expected_crc:
        fail(f"PNG chunk {chunk_type.decode('latin-1')} has a bad CRC")

    chunks.append((chunk_type, payload))
    position = crc_end

    if chunk_type == b"IEND":
        break

if position != len(data):
    fail("unexpected data after the IEND chunk")

if not chunks or chunks[0][0] != b"IHDR" or len(chunks[0][1]) != 13:
    fail("missing or malformed IHDR chunk")

if chunks[-1][0] != b"IEND" or chunks[-1][1] != b"":
    fail("malformed IEND chunk")

(width, height, depth, color_type, compression, filter_method,
    interlace) = struct.unpack(">IIBBBBB", chunks[0][1])

if compression != 0 or filter_method != 0:
    fail("unsupported PNG compression or filter method")

compressed = bytearray()
palette = None

for chunk_type, payload in chunks[1:]:
    if chunk_type == b"IHDR":
        fail("duplicate IHDR chunk")
    elif chunk_type == b"PLTE":
        if not payload or len(payload) % 3 != 0:
            fail("malformed PLTE chunk")

        palette = [tuple(payload[i:i + 3]) for i in range(0, len(payload), 3)]
    elif chunk_type == b"IDAT":
        compressed += payload

if not compressed:
    fail("missing IDAT chunk")

if (width, height) != (expected_width, expected_height):
    fail(f"unexpected image size {width}x{height}")

if depth != 8 or interlace != 0:
    fail(f"unsupported PNG depth={depth} interlace={interlace}")

channels = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}.get(color_type)

if channels is None:
    fail(f"unsupported PNG color type {color_type}")

if color_type == 3 and palette is None:
    fail("palette PNG without a PLTE chunk")

try:
    decompressor = zlib.decompressobj()
    raw = decompressor.decompress(bytes(compressed)) + decompressor.flush()
except zlib.error as error:
    fail(f"cannot decompress PNG data: {error}")

if not decompressor.eof:
    fail("PNG image data is incomplete")

if decompressor.unused_data:
    fail("unexpected trailing data in the PNG image stream")

stride = width * channels

if len(raw) != height * (stride + 1):
    fail("unexpected image data size")

rows = []
previous = bytearray(stride)
offset = 0

for _ in range(height):
    filter_type = raw[offset]
    offset += 1
    line = bytearray(raw[offset:offset + stride])
    offset += stride

    if filter_type == 0:
        pass
    elif filter_type == 1:
        for index in range(stride):
            left = line[index - channels] if index >= channels else 0
            line[index] = (line[index] + left) & 0xFF
    elif filter_type == 2:
        for index in range(stride):
            line[index] = (line[index] + previous[index]) & 0xFF
    elif filter_type == 3:
        for index in range(stride):
            left = line[index - channels] if index >= channels else 0
            line[index] = (line[index] + ((left + previous[index]) >> 1)) & 0xFF
    elif filter_type == 4:
        for index in range(stride):
            left = line[index - channels] if index >= channels else 0
            up = previous[index]
            corner = previous[index - channels] if index >= channels else 0
            estimate = left + up - corner
            distance_left = abs(estimate - left)
            distance_up = abs(estimate - up)
            distance_corner = abs(estimate - corner)

            if distance_left <= distance_up and distance_left <= distance_corner:
                predictor = left
            elif distance_up <= distance_corner:
                predictor = up
            else:
                predictor = corner

            line[index] = (line[index] + predictor) & 0xFF
    else:
        fail(f"unsupported PNG filter type {filter_type}")

    rows.append(line)
    previous = line


def pixel(row, x):
    index = x * channels

    if color_type == 3:
        value = row[index]

        if value >= len(palette):
            fail(f"palette index {value} is out of range")

        return palette[value]

    if color_type in (0, 4):
        value = row[index]
        return (value, value, value)

    return (row[index], row[index + 1], row[index + 2])


sample = Counter()

for y in range(0, height, 4):
    row = rows[y]

    for x in range(0, width, 4):
        sample[pixel(row, x)] += 1

background, _ = sample.most_common(1)[0]
columns = [0] * width
content_pixels = 0

for y in range(height):
    row = rows[y]

    for x in range(width):
        red, green, blue = pixel(row, x)

        if (abs(red - background[0]) + abs(green - background[1]) +
                abs(blue - background[2])) > 40:
            columns[x] += 1
            content_pixels += 1

percent = 100.0 * content_pixels / (width * height)

runs = []
inside = False
start = 0

for x, count in enumerate(columns):
    if count > 2 and not inside:
        start = x
        inside = True
    elif count <= 2 and inside:
        runs.append((start, x - 1))
        inside = False

if inside:
    runs.append((start, width - 1))

runs = [run for run in runs if run[1] - run[0] > 20]

print(f"IMAGE_SIZE={width}x{height}")
print(f"IMAGE_BACKGROUND={background[0]},{background[1]},{background[2]}")
print(f"IMAGE_CONTENT_PERCENT={percent:.2f}")
print(f"IMAGE_CLUSTERS={runs}")

if percent < 0.5:
    fail("screenshot is blank; it contains no rendered content")

if expected_clusters is not None and len(runs) != expected_clusters:
    fail(f"expected {expected_clusters} content clusters, found {len(runs)}")

print("IMAGE_OK")
PY
) || IMAGE_STATUS=$?

    printf '%s\n' "$IMAGE_OUTPUT" >"$RUN_DIR/image-analysis.log"

    if [ "$IMAGE_STATUS" -ne 0 ]; then
        IMAGE_ERROR=$(printf '%s\n' "$IMAGE_OUTPUT" | sed -n 's/^IMAGE_ERROR=//p' | head -n 1)
        record_failure "$EXIT_SCREENSHOT" \
            "screenshot verification failed: ${IMAGE_ERROR:-unknown error}"
    else
        SCREENSHOT_SUMMARY=$(printf '%s\n' "$IMAGE_OUTPUT" |
            grep -E '^IMAGE_(SIZE|CONTENT_PERCENT|CLUSTERS)=' |
            tr '\n' ' ' | sed 's/[[:space:]]*$//')

        log "image verified: $SCREENSHOT_SUMMARY"
    fi
fi

finish
