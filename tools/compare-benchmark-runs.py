#!/usr/bin/env python3
"""Compares two recorded BenchmarkDotNet runs of the same benchmarks.

Reads the summary table each run writes, reports every method's mean and
its ratio to the baseline, and can fail when a mean grows past a multiple
of the baseline. The table is the only part of a run this needs, so a
recorded BenchmarkRun.txt is enough.
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

# Microseconds per unit, so a mean is comparable across rows.
UNITS = {"ns": 0.001, "us": 1.0, "µs": 1.0, "ms": 1000.0, "s": 1_000_000.0}

VALUE = re.compile(r"^(?P<number>[0-9]+(?:\.[0-9]+)?)\s*(?P<unit>ns|us|µs|ms|s)$")

# Column order of the summary table: Method, Job, Toolchain,
# IterationCount, LaunchCount, WarmupCount, Mean, ...
MEAN_COLUMN = 6


def parse_means(text: str) -> dict[tuple[str, str], float]:
    """Returns {(method, job): mean in microseconds} from a run's summary."""
    means: dict[tuple[str, str], float] = {}
    in_table = False

    for line in text.splitlines():
        stripped = line.strip()

        if not stripped.startswith("|"):
            in_table = False
            continue

        cells = [cell.strip() for cell in stripped.strip("|").split("|")]

        if cells and cells[0] == "Method":
            in_table = True
            continue

        if not in_table or len(cells) <= MEAN_COLUMN:
            continue

        method, job, mean = cells[0], cells[1], cells[MEAN_COLUMN]
        match = VALUE.match(mean)

        if not method or match is None:
            continue

        means[(method, job)] = (
            float(match.group("number")) * UNITS[match.group("unit")]
        )

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
    args = parser.parse_args(argv)

    baseline = parse_means(args.baseline.read_text(encoding="utf-8"))
    current = parse_means(args.current.read_text(encoding="utf-8"))

    if not baseline:
        print(
            f"{args.baseline}: no summary table found",
            file=sys.stderr,
        )
        return 2

    rows, missing = compare(baseline, current, args.job)

    if missing:
        print(
            "The current run is missing: " + ", ".join(missing),
            file=sys.stderr,
        )
        return 2

    if not rows:
        print("No rows matched the requested job.", file=sys.stderr)
        return 2

    print(f"{'method':<24}{'job':<12}{'baseline':>12}{'current':>12}{'ratio':>8}")

    over: list[str] = []

    for method, job, base_mean, current_mean, ratio in rows:
        flag = ""

        if args.max_ratio is not None and ratio > args.max_ratio:
            flag = "  over"
            over.append(method)

        print(
            f"{method:<24}{job:<12}"
            f"{base_mean:10.3f}us{current_mean:10.3f}us{ratio:8.2f}{flag}"
        )

    if over and args.max_ratio is not None:
        print(
            f"\n{', '.join(over)} grew past {args.max_ratio:g}x the baseline.",
            file=sys.stderr,
        )
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
