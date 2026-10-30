#!/usr/bin/env python3
"""Self-tests for tools/compare-benchmark-runs.py."""

from __future__ import annotations

import contextlib
import importlib.util
import io
import tempfile
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location(
    "compare_benchmark_runs",
    TOOLS / "compare-benchmark-runs.py",
)
comparator = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(comparator)


def run_summary(rows: list[tuple[str, str, str]]) -> str:
    """Builds the part of a run's output that the comparator reads."""
    lines = [
        "// * Summary *",
        "",
        "| Method | Job | Toolchain | IterationCount | LaunchCount | "
        "WarmupCount | Mean | Error | StdDev |",
        "|------- | --- | --------- | -------------- | ----------- | "
        "----------- |-----:|------:|-------:|",
    ]

    for method, job, mean in rows:
        lines.append(
            f"| {method} | {job} | Default | 3 | 1 | 3 | {mean} "
            "| 0.1 us | 0.1 us |"
        )

    return "\n".join(lines) + "\n"


class ParseMeansTests(unittest.TestCase):
    def test_reads_a_mean_for_each_method_and_job(self) -> None:
        means = comparator.parse_means(
            run_summary(
                [
                    ("SteadyRefresh", "ShortRun", "8.495 us"),
                    ("RebuildingRefresh", "ShortRun", "15.126 us"),
                ]
            )
        )

        self.assertEqual(8.495, means[("SteadyRefresh", "ShortRun")])
        self.assertEqual(15.126, means[("RebuildingRefresh", "ShortRun")])

    def test_normalizes_every_unit_to_microseconds(self) -> None:
        means = comparator.parse_means(
            run_summary(
                [
                    ("Fast", "ShortRun", "500 ns"),
                    ("Steady", "ShortRun", "8.5 us"),
                    ("Slow", "ShortRun", "2 ms"),
                ]
            )
        )

        self.assertEqual(0.5, means[("Fast", "ShortRun")])
        self.assertEqual(8.5, means[("Steady", "ShortRun")])
        self.assertEqual(2000.0, means[("Slow", "ShortRun")])

    def test_ignores_the_separator_and_blank_rows(self) -> None:
        text = run_summary([("Steady", "ShortRun", "9 us")]).replace(
            "| Steady | ShortRun | Default",
            "| --- | --- | --- | --- | --- | --- | --- | --- | --- |\n"
            "|  |  |  |  |  |  |  |  |  |\n"
            "| Steady | ShortRun | Default",
        )

        self.assertEqual(
            {("Steady", "ShortRun"): 9.0},
            comparator.parse_means(text),
        )

    def test_reports_nothing_for_text_without_a_table(self) -> None:
        self.assertEqual({}, comparator.parse_means("no table here\n"))


class ComparatorTestCase(unittest.TestCase):

    def setUp(self) -> None:
        self._root = tempfile.TemporaryDirectory()
        self.addCleanup(self._root.cleanup)
        self.directory = Path(self._root.name)

    def write(self, name: str, rows: list[tuple[str, str, str]]) -> Path:
        path = self.directory / name
        path.write_text(run_summary(rows), encoding="utf-8")

        return path

    def run_comparator(self, *args: str) -> tuple[int, str]:
        out = io.StringIO()

        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(
            io.StringIO()
        ):
            status = comparator.main(list(args))

        return status, out.getvalue()

class CompareTests(ComparatorTestCase):

    def test_reports_the_ratio_against_the_baseline(self) -> None:
        baseline = self.write("base.txt", [("Steady", "ShortRun", "8 us")])
        current = self.write("now.txt", [("Steady", "ShortRun", "10 us")])

        status, output = self.run_comparator(
            "--baseline", str(baseline),
            "--current", str(current),
        )

        self.assertEqual(0, status)
        self.assertIn("Steady", output)
        self.assertIn("1.25", output)

    def test_passes_when_a_mean_stays_inside_the_limit(self) -> None:
        baseline = self.write("base.txt", [("Steady", "ShortRun", "8 us")])
        current = self.write("now.txt", [("Steady", "ShortRun", "9 us")])

        status, _ = self.run_comparator(
            "--baseline", str(baseline),
            "--current", str(current),
            "--max-ratio", "1.5",
        )

        self.assertEqual(0, status)

    def test_fails_when_a_mean_grows_past_the_limit(self) -> None:
        baseline = self.write("base.txt", [("Steady", "ShortRun", "8 us")])
        current = self.write("now.txt", [("Steady", "ShortRun", "20 us")])

        status, output = self.run_comparator(
            "--baseline", str(baseline),
            "--current", str(current),
            "--max-ratio", "2.0",
        )

        self.assertEqual(1, status)
        self.assertIn("Steady", output)

    def test_compares_only_the_requested_job_row(self) -> None:
        baseline = self.write(
            "base.txt",
            [
                ("Steady", "InProcess", "8 us"),
                ("Steady", "ShortRun", "8 us"),
            ],
        )
        current = self.write(
            "now.txt",
            [
                ("Steady", "InProcess", "8 us"),
                ("Steady", "ShortRun", "40 us"),
            ],
        )

        status, _ = self.run_comparator(
            "--baseline", str(baseline),
            "--current", str(current),
            "--job", "ShortRun",
            "--max-ratio", "2.0",
        )

        self.assertEqual(1, status)

    def test_fails_when_the_current_run_lacks_a_baseline_method(self) -> None:
        baseline = self.write("base.txt", [("Steady", "ShortRun", "8 us")])
        current = self.write("now.txt", [("Other", "ShortRun", "8 us")])

        status, _ = self.run_comparator(
            "--baseline", str(baseline),
            "--current", str(current),
        )

        self.assertEqual(2, status)

    def test_fails_when_the_baseline_has_no_table(self) -> None:
        baseline = self.directory / "empty.txt"
        baseline.write_text("nothing here\n", encoding="utf-8")
        current = self.write("now.txt", [("Steady", "ShortRun", "8 us")])

        status, _ = self.run_comparator(
            "--baseline", str(baseline),
            "--current", str(current),
        )

        self.assertEqual(2, status)


class OverheadTests(ComparatorTestCase):
    """The quotient of one method's mean over another's."""

    def pair(self, steady: str, rebuilding: str) -> list[tuple[str, str, str]]:
        return [
            ("SteadyRefresh", "ShortRun", steady),
            ("RebuildingRefresh", "ShortRun", rebuilding),
        ]

    def test_quotes_one_method_over_another(self) -> None:
        baseline = self.write("base.txt", self.pair("8 us", "16 us"))
        current = self.write("now.txt", self.pair("9 us", "18 us"))

        status, output = self.run_comparator(
            "--baseline", str(baseline),
            "--current", str(current),
            "--job", "ShortRun",
            "--overhead", "RebuildingRefresh:SteadyRefresh",
        )

        self.assertEqual(0, status)
        self.assertIn("2.00x -> 2.00x", output)

    def test_passes_when_the_quotient_stays_inside_the_limit(self) -> None:
        baseline = self.write("base.txt", self.pair("8 us", "16 us"))
        current = self.write("now.txt", self.pair("8.4 us", "17.64 us"))

        status, _ = self.run_comparator(
            "--baseline", str(baseline),
            "--current", str(current),
            "--job", "ShortRun",
            "--overhead", "RebuildingRefresh:SteadyRefresh",
            "--max-overhead", "2.2",
        )

        self.assertEqual(0, status)

    def test_fails_when_the_quotient_grows_past_the_limit(self) -> None:
        baseline = self.write("base.txt", self.pair("8 us", "16 us"))
        current = self.write("now.txt", self.pair("8 us", "20 us"))

        status, output = self.run_comparator(
            "--baseline", str(baseline),
            "--current", str(current),
            "--job", "ShortRun",
            "--overhead", "RebuildingRefresh:SteadyRefresh",
            "--max-overhead", "2.2",
        )

        self.assertEqual(1, status)
        self.assertIn("2.50x", output)

    def test_ignores_another_job_when_one_is_requested(self) -> None:
        baseline = self.write(
            "base.txt",
            [
                ("SteadyRefresh", "InProcess", "8 us"),
                ("RebuildingRefresh", "InProcess", "16 us"),
                ("SteadyRefresh", "ShortRun", "8 us"),
                ("RebuildingRefresh", "ShortRun", "16 us"),
            ],
        )
        current = self.write(
            "now.txt",
            [
                ("SteadyRefresh", "InProcess", "8 us"),
                ("RebuildingRefresh", "InProcess", "40 us"),
                ("SteadyRefresh", "ShortRun", "8 us"),
                ("RebuildingRefresh", "ShortRun", "17 us"),
            ],
        )

        status, _ = self.run_comparator(
            "--baseline", str(baseline),
            "--current", str(current),
            "--job", "ShortRun",
            "--overhead", "RebuildingRefresh:SteadyRefresh",
            "--max-overhead", "2.2",
        )

        self.assertEqual(0, status)

    def test_fails_when_a_quoted_method_is_missing(self) -> None:
        baseline = self.write("base.txt", self.pair("8 us", "16 us"))
        current = self.write(
            "now.txt",
            [("SteadyRefresh", "ShortRun", "8 us")],
        )

        status, _ = self.run_comparator(
            "--baseline", str(baseline),
            "--current", str(current),
            "--job", "ShortRun",
            "--overhead", "RebuildingRefresh:SteadyRefresh",
            "--max-overhead", "2.2",
        )

        self.assertEqual(2, status)

    def test_rejects_a_maximum_without_a_pair(self) -> None:
        baseline = self.write("base.txt", self.pair("8 us", "16 us"))
        current = self.write("now.txt", self.pair("8 us", "16 us"))

        status, _ = self.run_comparator(
            "--baseline", str(baseline),
            "--current", str(current),
            "--max-overhead", "2.2",
        )

        self.assertEqual(2, status)

    def test_rejects_a_pair_without_a_denominator(self) -> None:
        baseline = self.write("base.txt", self.pair("8 us", "16 us"))
        current = self.write("now.txt", self.pair("8 us", "16 us"))

        status, _ = self.run_comparator(
            "--baseline", str(baseline),
            "--current", str(current),
            "--job", "ShortRun",
            "--overhead", "RebuildingRefresh:",
            "--max-overhead", "2.2",
        )

        self.assertEqual(2, status)


if __name__ == "__main__":
    unittest.main()
