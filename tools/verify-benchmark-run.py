#!/usr/bin/env python3
"""Checks that a recorded benchmark run holds the measurements it must.

BenchmarkDotNet writes a row of NA values for a benchmark it could not
build or run and still exits 0, so a recording script cannot trust the
process status alone. This reads the recorded summary table instead and
fails when a required method and job is absent, ambiguous, or unmeasured,
naming each row that is missing.

A run is read like ``tools/compare-benchmark-runs.py`` reads one: a
recorded text file, a directory holding it, or a downloaded artifact
archive.
"""

from __future__ import annotations

import argparse
import importlib.util
import sys
import zipfile
from pathlib import Path

TOOLS = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location(
    "compare_benchmark_runs",
    TOOLS / "compare-benchmark-runs.py",
)
comparator = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(comparator)

# The message BenchmarkDotNet writes when its project search is ambiguous.
DUPLICATE_PROJECT = "Found more than one matching project file"


def parse_requirement(spec: str) -> tuple[str, str]:
    """Splits a ``Method:Job`` requirement."""
    method, _, job = spec.partition(":")

    return method.strip(), job.strip()


def issues(text: str) -> list[str]:
    """Returns the run's own list of benchmarks with issues, if it wrote one."""
    lines = text.splitlines()

    for index, line in enumerate(lines):
        if line.strip() != "Benchmarks with issues:":
            continue

        found: list[str] = []

        for following in lines[index + 1:]:
            if not following.strip():
                continue

            if following.startswith("//") or not following.startswith(" "):
                break

            found.append(following.strip())

        return found

    return []


def check(
    text: str,
    required: list[tuple[str, str]],
) -> tuple[list[tuple[str, str, str]], list[str]]:
    """Returns the required measurements found and the ones that are missing."""
    rows = comparator.parse_rows(text)
    measured: list[tuple[str, str, str]] = []
    problems: list[str] = []

    for method, job in required:
        matches = [
            (row_method, row_job, mean)
            for row_method, row_job, mean in rows
            if row_method == method and row_job == job
        ]

        if not matches:
            problems.append(f"{method} ({job}) has no row")
            continue

        if len(matches) > 1:
            problems.append(
                f"{method} ({job}) is quoted {len(matches)} times, so it is "
                "ambiguous"
            )
            continue

        mean = matches[0][2]

        if comparator.VALUE.match(mean) is None:
            problems.append(
                f"{method} ({job}) produced {mean or 'nothing'} instead of a "
                "measurement"
            )
            continue

        measured.append((method, job, mean))

    return measured, problems


def explain(text: str) -> None:
    """Names the cause of a failed run when the log shows a known one."""
    if DUPLICATE_PROJECT not in text:
        return

    print(
        "\nBenchmarkDotNet found more than one copy of the benchmark project "
        "and would not build the launched jobs. Run the benchmark through "
        "tools/benchmark-ecs-refresh.sh, which builds from a scratch copy of "
        "the sources so copies elsewhere in the checkout cannot make the "
        "search ambiguous.",
        file=sys.stderr,
    )


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--run",
        type=Path,
        required=True,
        help="a recorded BenchmarkRun.txt, a directory holding one, or an "
        "artifact archive",
    )
    parser.add_argument(
        "--require",
        action="append",
        default=[],
        metavar="METHOD:JOB",
        help="a measurement the run must hold, for example "
        "SteadyRefresh:ShortRun; repeatable",
    )
    args = parser.parse_args(argv)

    if not args.require:
        print("--require METHOD:JOB is needed.", file=sys.stderr)
        return 2

    required: list[tuple[str, str]] = []

    for spec in args.require:
        method, job = parse_requirement(spec)

        if not method or not job:
            print(
                f"--require expects method:job, not {spec!r}.",
                file=sys.stderr,
            )
            return 2

        required.append((method, job))

    try:
        text = comparator.read_run(args.run)
    except (FileNotFoundError, OSError, zipfile.BadZipFile) as error:
        print(error, file=sys.stderr)
        return 2

    if not comparator.parse_rows(text):
        print(
            f"{args.run}: no benchmark summary table; the run did not finish.",
            file=sys.stderr,
        )

        for issue in issues(text):
            print(f"  {issue}", file=sys.stderr)

        explain(text)

        return 1

    measured, problems = check(text, required)

    if problems:
        print(f"{args.run}: the run is incomplete.", file=sys.stderr)

        for problem in problems:
            print(f"  {problem}", file=sys.stderr)

        for issue in issues(text):
            print(f"  benchmark with issues: {issue}", file=sys.stderr)

        explain(text)

        return 1

    for method, job, mean in measured:
        print(f"{method} ({job}): {mean}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
