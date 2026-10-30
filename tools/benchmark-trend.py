#!/usr/bin/env python3
"""Prints the refresh quotient of several benchmark comparisons over time.

Each argument is a JSON document written by
``tools/compare-benchmark-runs.py --json``. Documents are shown in the order
given, so a shell glob or a list sorted by download time reads as a trend.
``--methods A:B`` keeps one pair out of a mixed series; without it every
document must quote the same pair, because quotients of different pairs
cannot be lined up.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path


def load(path: Path) -> dict:
    """Reads one ``--json`` document, explaining anything else."""

    try:
        text = path.read_text(encoding="utf-8")
    except FileNotFoundError:
        raise ValueError(f"{path}: no such document") from None
    except OSError as error:
        raise ValueError(f"{path}: {error.strerror}") from None

    try:
        document = json.loads(text)
    except json.JSONDecodeError as error:
        raise ValueError(
            f"{path}: not a --json document ({error.msg} at line {error.lineno})"
        ) from None

    if not isinstance(document, dict) or not isinstance(
        document.get("overhead"), dict
    ):
        raise ValueError(
            f"{path}: no overhead quotient; compare with --overhead first"
        )

    return document


def points(
    documents: list[tuple[Path, dict]],
    only: tuple[str, str] | None = None,
) -> list[dict]:
    """Flattens the documents into one row per job, in the given order."""

    rows: list[dict] = []

    for path, document in documents:
        block = document["overhead"]

        for row in block.get("rows", []):
            if only is not None and (
                block["numerator"],
                block["denominator"],
            ) != only:
                continue

            rows.append(
                {
                    "document": str(path),
                    "job": row["job"],
                    "numerator": block["numerator"],
                    "denominator": block["denominator"],
                    "quotient": float(row["current_quotient"]),
                    "limit": block.get("max"),
                    "over": bool(row["over_limit"]),
                }
            )

    if not rows:
        if only is not None:
            raise ValueError(f"no rows quote {only[0]}:{only[1]}")

        raise ValueError("no quotient rows in the given documents")

    pairs = {(row["numerator"], row["denominator"]) for row in rows}

    if len(pairs) > 1:
        listed = ", ".join(f"{a}:{b}" for a, b in sorted(pairs))

        raise ValueError(f"the documents quote different methods: {listed}")

    return rows


def render(rows: list[dict]) -> str:
    """Lays the rows out as one table, with the change from the last run."""

    numerator = rows[0]["numerator"]
    denominator = rows[0]["denominator"]
    limit = next((row["limit"] for row in rows if row["limit"] is not None), None)
    width = max(len(row["document"]) for row in rows)
    width = max(width, len("run"))

    header = f"{'run':<{width}}  {'job':<12}{'quotient':>9}{'change':>9}"

    if limit is not None:
        header += f"{'limit':>8}"

    lines = [f"{numerator} over {denominator}:", "", header]
    previous: dict[str, float] = {}

    for row in rows:
        last = previous.get(row["job"])
        previous[row["job"]] = row["quotient"]
        change = "-" if last is None else f"{row['quotient'] - last:+.2f}"

        line = (
            f"{row['document']:<{width}}  {row['job']:<12}"
            f"{row['quotient']:8.2f}x{change:>9}"
        )

        if limit is not None:
            line += f"{limit:7.2f}x"

        if row["over"]:
            line += "  over"

        lines.append(line)

    quotients = [row["quotient"] for row in rows]
    lines.append("")
    lines.append(
        f"{len(quotients)} run(s), {min(quotients):.2f}x to {max(quotients):.2f}x"
    )

    return "\n".join(lines) + "\n"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "documents",
        nargs="+",
        type=Path,
        help="a JSON document written by --json, oldest first",
    )
    parser.add_argument(
        "--methods",
        default=None,
        metavar="A:B",
        help="keep only this method pair, for example "
        "RebuildingRefresh:SteadyRefresh",
    )
    args = parser.parse_args(argv)

    only = None

    if args.methods is not None:
        numerator, _, denominator = args.methods.partition(":")

        if not numerator.strip() or not denominator.strip():
            print(
                f"--methods expects numerator:denominator, "
                f"not {args.methods!r}.",
                file=sys.stderr,
            )
            return 2

        only = (numerator.strip(), denominator.strip())

    try:
        rows = points([(path, load(path)) for path in args.documents], only)
    except ValueError as error:
        print(error, file=sys.stderr)
        return 2

    sys.stdout.write(render(rows))

    return 0


if __name__ == "__main__":
    sys.exit(main())
