#!/usr/bin/env python3
"""Compares two recorded BenchmarkDotNet runs of the same benchmarks.

Reads the summary table each run writes, reports every method's mean and
its ratio to the baseline, and can fail when a mean grows past a multiple
of the baseline.

It can also quote one method's mean against another's. That quotient moves
far less between builds than either mean does, because both methods absorb
whatever the machine did differently, so it catches a slowdown the loose
per-mean limit would let through.

The table is the only part of a run this needs, so a recorded
BenchmarkRun.txt is enough. Each input may also be a directory holding
one or a downloaded CI artifact archive, which is how two hosted runs
are quoted against each other instead of against the committed
baseline. ``--json`` writes the same comparison as JSON, so a series of
downloaded artifacts can be tracked by a script.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import zipfile
from pathlib import Path

# Microseconds per unit, so a mean is comparable across rows.
UNITS = {"ns": 0.001, "us": 1.0, "µs": 1.0, "ms": 1000.0, "s": 1_000_000.0}

VALUE = re.compile(r"^(?P<number>[0-9]+(?:\.[0-9]+)?)\s*(?P<unit>ns|us|µs|ms|s)$")

# The summary table names its columns in a header row, which is found by
# name rather than by position: BenchmarkDotNet adds a Type column when a
# run holds more than one benchmark class, which moves the rest right.
METHOD_COLUMN = "Method"
JOB_COLUMN = "Job"
MEAN_COLUMN = "Mean"

# The file a run directory or an artifact archive is expected to hold.
RUN_FILENAME = "BenchmarkRun.txt"


def find_run(path: Path) -> Path:
    """Returns the run inside a directory, at its top or one level down."""
    direct = path / RUN_FILENAME

    if direct.is_file():
        return direct

    for candidate in sorted(path.rglob(RUN_FILENAME)):
        return candidate

    raise FileNotFoundError(f"{path}: no {RUN_FILENAME} here")


def read_run(path: Path) -> str:
    """Reads a run from a file, an extracted directory, or an artifact zip."""
    if path.is_dir():
        return read_run(find_run(path))

    if zipfile.is_zipfile(path):
        with zipfile.ZipFile(path) as archive:
            for name in archive.namelist():
                if Path(name).name == RUN_FILENAME:
                    return archive.read(name).decode("utf-8")

        raise FileNotFoundError(f"{path}: no {RUN_FILENAME} in the archive")

    return path.read_text(encoding="utf-8")


class AmbiguousRows(ValueError):
    """Raised when a summary names the same method and job twice."""


def parse_rows(text: str) -> list[tuple[str, str, str]]:
    """Returns (method, job, mean cell) for each row of a run's summary.

    Only a table with a job column is read, because rows are paired by
    method and job: a table without one cannot be compared against
    another run.
    """
    rows: list[tuple[str, str, str]] = []
    columns: dict[str, int] | None = None

    for line in text.splitlines():
        stripped = line.strip()

        if not stripped.startswith("|"):
            columns = None
            continue

        cells = [cell.strip() for cell in stripped.strip("|").split("|")]

        if all(name in cells for name in (METHOD_COLUMN, JOB_COLUMN, MEAN_COLUMN)):
            columns = {
                name: cells.index(name)
                for name in (METHOD_COLUMN, JOB_COLUMN, MEAN_COLUMN)
            }
            continue

        if columns is None:
            continue

        if len(cells) <= max(columns.values()):
            continue

        method = cells[columns[METHOD_COLUMN]]

        if not method:
            continue

        rows.append(
            (
                method,
                cells[columns[JOB_COLUMN]],
                cells[columns[MEAN_COLUMN]],
            )
        )

    return rows


def parse_means(
    text: str,
    source: str = "the run",
) -> dict[tuple[str, str], float]:
    """Returns {(method, job): mean in microseconds} from a run's summary."""
    means: dict[tuple[str, str], float] = {}

    for method, job, mean in parse_rows(text):
        match = VALUE.match(mean)

        if match is None:
            continue

        key = (method, job)

        if key in means:
            raise AmbiguousRows(
                f"{source} quotes {method} ({job}) more than once, so one "
                "of the two rows cannot be paired with the other run; "
                "record one benchmark class per run"
            )

        means[key] = float(match.group("number")) * UNITS[match.group("unit")]

    return means


def compare(
    baseline: dict[tuple[str, str], float],
    current: dict[tuple[str, str], float],
    job: str | None = None,
) -> tuple[list[tuple[str, str, float, float, float]], list[str]]:
    """Pairs each baseline row with the same row in the current run."""
    rows: list[tuple[str, str, float, float, float]] = []
    missing: list[str] = []

    for (method, base_job), base_mean in sorted(baseline.items()):
        if job is not None and base_job != job:
            continue

        current_mean = current.get((method, base_job))

        if current_mean is None:
            missing.append(f"{method} ({base_job})")
            continue

        rows.append(
            (
                method,
                base_job,
                base_mean,
                current_mean,
                current_mean / base_mean,
            )
        )

    return rows, missing


def write_json(document: dict[str, object], destination: Path) -> None:
    """Writes a comparison beside the file, then moves it into place.

    A comparison that fails returns before this is called, so a bad input
    cannot leave an empty report behind for the trend tool to read.
    """
    temporary = destination.with_name(destination.name + ".tmp")
    temporary.write_text(
        json.dumps(document, indent=2) + "\n",
        encoding="utf-8",
    )
    temporary.replace(destination)


def parse_overhead_spec(spec: str) -> tuple[str, str]:
    """Splits an ``numerator:denominator`` method pair."""
    numerator, _, denominator = spec.partition(":")

    return numerator.strip(), denominator.strip()


def overhead(
    baseline: dict[tuple[str, str], float],
    current: dict[tuple[str, str], float],
    numerator: str,
    denominator: str,
    job: str | None = None,
) -> tuple[list[tuple[str, float, float, float, float]], list[str]]:
    """Forms the quotient of one method's mean over another's, per job.

    Returns the finished rows and, separately, the method the runs do not
    quote, so a renamed method fails loudly instead of quietly leaving the
    gate with nothing to check.
    """
    rows: list[tuple[str, float, float, float, float]] = []
    missing: list[str] = []

    for base_job in sorted({row_job for _, row_job in baseline}):
        if job is not None and base_job != job:
            continue

        base_over = baseline.get((numerator, base_job))
        base_under = baseline.get((denominator, base_job))
        current_over = current.get((numerator, base_job))
        current_under = current.get((denominator, base_job))

        absent = [
            f"{method} ({base_job})"
            for method, quotient in (
                (numerator, (base_over, current_over)),
                (denominator, (base_under, current_under)),
            )
            if any(part is None for part in quotient)
        ]

        if absent:
            missing.extend(absent)
            continue

        rows.append((base_job, base_over, base_under, current_over, current_under))

    return rows, missing


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", type=Path, required=True)
    parser.add_argument("--current", type=Path, required=True)
    parser.add_argument(
        "--job",
        default=None,
        help="compare only this job row, for example ShortRun",
    )
    parser.add_argument(
        "--max-ratio",
        type=float,
        default=None,
        help="exit non-zero when a mean exceeds this multiple of the baseline",
    )
    parser.add_argument(
        "--overhead",
        default=None,
        help="also quote one method's mean over another's, written as "
        "numerator:denominator, for example RebuildingRefresh:SteadyRefresh",
    )
    parser.add_argument(
        "--max-overhead",
        type=float,
        default=None,
        help="exit non-zero when the --overhead quotient exceeds this value",
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="write a JSON summary to stdout instead of the table",
    )
    parser.add_argument(
        "--json-output",
        type=Path,
        default=None,
        metavar="PATH",
        help="write the JSON summary to PATH and the table to stdout; a "
        "failed comparison writes no file",
    )
    args = parser.parse_args(argv)

    if args.max_overhead is not None and args.overhead is None:
        print("--max-overhead needs --overhead.", file=sys.stderr)
        return 2

    if args.json and args.json_output is not None:
        print(
            "--json writes to stdout and --json-output writes a file; "
            "choose one.",
            file=sys.stderr,
        )
        return 2

    try:
        baseline_text = read_run(args.baseline)
        current_text = read_run(args.current)
    except (FileNotFoundError, zipfile.BadZipFile) as error:
        print(error, file=sys.stderr)
        return 2

    try:
        baseline = parse_means(baseline_text, str(args.baseline))
        current = parse_means(current_text, str(args.current))
    except ValueError as error:
        print(error, file=sys.stderr)
        return 2

    if not baseline:
        print(
            f"{args.baseline}: no usable benchmark rows found",
            file=sys.stderr,
        )
        return 2

    if not current:
        print(
            f"{args.current}: no usable benchmark rows found",
            file=sys.stderr,
        )
        return 2

    rows, missing = compare(baseline, current, args.job)

    if missing:
        print(
            "The current run is missing: "
            + ", ".join(missing)
            + ". A row of NA counts as missing; check the recording log.",
            file=sys.stderr,
        )
        return 2

    if not rows:
        print("No rows matched the requested job.", file=sys.stderr)
        return 2

    over: list[str] = []
    methods: list[dict[str, object]] = []

    for method, job, base_mean, current_mean, ratio in rows:
        exceeded = args.max_ratio is not None and ratio > args.max_ratio

        if exceeded:
            over.append(method)

        methods.append(
            {
                "method": method,
                "job": job,
                "baseline_us": round(base_mean, 6),
                "current_us": round(current_mean, 6),
                "ratio": round(ratio, 6),
                "over_limit": exceeded,
            }
        )

    over_overhead: list[str] = []
    overhead_summary: dict[str, object] | None = None

    if args.overhead is not None:
        numerator, denominator = parse_overhead_spec(args.overhead)

        if not numerator or not denominator:
            print(
                f"--overhead expects numerator:denominator, "
                f"not {args.overhead!r}.",
                file=sys.stderr,
            )
            return 2

        overhead_rows, overhead_missing = overhead(
            baseline, current, numerator, denominator, args.job
        )

        if overhead_missing:
            print(
                "The runs are missing: " + ", ".join(overhead_missing),
                file=sys.stderr,
            )
            return 2

        if not overhead_rows:
            print("No rows matched the requested job.", file=sys.stderr)
            return 2

        quoted: list[dict[str, object]] = []

        for job, base_over, base_under, current_over, current_under in overhead_rows:
            base_quotient = base_over / base_under
            current_quotient = current_over / current_under
            exceeded = (
                args.max_overhead is not None
                and current_quotient > args.max_overhead
            )

            if exceeded:
                over_overhead.append(job)

            quoted.append(
                {
                    "job": job,
                    "baseline_numerator_us": round(base_over, 6),
                    "baseline_denominator_us": round(base_under, 6),
                    "current_numerator_us": round(current_over, 6),
                    "current_denominator_us": round(current_under, 6),
                    "baseline_quotient": round(base_quotient, 6),
                    "current_quotient": round(current_quotient, 6),
                    "over_limit": exceeded,
                }
            )

        overhead_summary = {
            "numerator": numerator,
            "denominator": denominator,
            "max": args.max_overhead,
            "rows": quoted,
        }

    document = {
        "baseline": str(args.baseline),
        "current": str(args.current),
        "job": args.job,
        "max_ratio": args.max_ratio,
        "methods": methods,
        "overhead": overhead_summary,
        "exceeded": bool(over or over_overhead),
    }

    if args.json_output is not None:
        try:
            write_json(document, args.json_output)
        except OSError as error:
            print(f"{args.json_output}: {error.strerror}", file=sys.stderr)
            return 2

    if args.json:
        json.dump(document, sys.stdout, indent=2)
        print()
    else:
        print(
            f"{'method':<24}{'job':<12}{'baseline':>12}{'current':>12}{'ratio':>8}"
        )

        for row in methods:
            flag = "  over" if row["over_limit"] else ""

            print(
                f"{row['method']:<24}{row['job']:<12}"
                f"{row['baseline_us']:10.3f}us{row['current_us']:10.3f}us"
                f"{row['ratio']:8.2f}{flag}"
            )

        if overhead_summary is not None:
            print(
                f"\n{overhead_summary['numerator']} over "
                f"{overhead_summary['denominator']}:"
            )

            for row in overhead_summary["rows"]:
                flag = "  over" if row["over_limit"] else ""

                print(
                    f"  {row['job']}: {row['baseline_numerator_us']:.3f}us/"
                    f"{row['baseline_denominator_us']:.3f}us -> "
                    f"{row['current_numerator_us']:.3f}us/"
                    f"{row['current_denominator_us']:.3f}us  "
                    f"{row['baseline_quotient']:.2f}x -> "
                    f"{row['current_quotient']:.2f}x{flag}"
                )

    if over and args.max_ratio is not None:
        print(
            f"\n{', '.join(over)} grew past {args.max_ratio:g}x the baseline.",
            file=sys.stderr,
        )

    if over_overhead and args.max_overhead is not None:
        print(
            f"\n{args.overhead} grew past {args.max_overhead:g}x "
            f"on {', '.join(over_overhead)}.",
            file=sys.stderr,
        )

    if over or over_overhead:
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
