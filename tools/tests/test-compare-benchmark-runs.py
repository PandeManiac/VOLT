#!/usr/bin/env python3
"""Self-tests for tools/compare-benchmark-runs.py."""

from __future__ import annotations

import contextlib
import importlib.util
import io
import json
import tempfile
import unittest
import zipfile
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


class ArtifactTests(ComparatorTestCase):
    """Reading a run from a directory or a downloaded artifact."""

    def archive(
        self, name: str, rows: list[tuple[str, str, str]]
    ) -> Path:
        path = self.directory / name

        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr(
                f"{path.stem}/BenchmarkRun.txt", run_summary(rows)
            )

        return path

    def test_reads_a_run_from_an_artifact_archive(self) -> None:
        baseline = self.archive("base.zip", [("Steady", "ShortRun", "9 us")])
        current = self.archive("now.zip", [("Steady", "ShortRun", "10 us")])

        status, output = self.run_comparator(
            "--baseline", str(baseline),
            "--current", str(current),
        )

        self.assertEqual(0, status)
        self.assertIn("1.11", output)

    def test_quotes_the_quotient_between_two_hosted_runs(self) -> None:
        baseline = self.archive(
            "base.zip",
            [
                ("SteadyRefresh", "ShortRun", "9 us"),
                ("RebuildingRefresh", "ShortRun", "16.2 us"),
            ],
        )
        current = self.archive(
            "now.zip",
            [
                ("SteadyRefresh", "ShortRun", "8.5 us"),
                ("RebuildingRefresh", "ShortRun", "15.1 us"),
            ],
        )

        status, output = self.run_comparator(
            "--baseline", str(baseline),
            "--current", str(current),
            "--job", "ShortRun",
            "--overhead", "RebuildingRefresh:SteadyRefresh",
            "--max-overhead", "2.2",
        )

        self.assertEqual(0, status)
        self.assertIn("1.80x -> 1.78x", output)

    def test_reads_a_run_from_an_extracted_directory(self) -> None:
        extracted = self.directory / "ecs-refresh-benchmark"
        extracted.mkdir()
        (extracted / "BenchmarkRun.txt").write_text(
            run_summary([("Steady", "ShortRun", "9 us")]),
            encoding="utf-8",
        )
        current = self.write("now.txt", [("Steady", "ShortRun", "9 us")])

        status, _ = self.run_comparator(
            "--baseline", str(extracted),
            "--current", str(current),
        )

        self.assertEqual(0, status)

    def test_reports_an_archive_without_a_run(self) -> None:
        path = self.directory / "empty.zip"

        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr("notes.txt", "nothing here")

        current = self.write("now.txt", [("Steady", "ShortRun", "9 us")])

        status, _ = self.run_comparator(
            "--baseline", str(path),
            "--current", str(current),
        )

        self.assertEqual(2, status)

    def test_reports_a_directory_without_a_run(self) -> None:
        empty = self.directory / "empty"
        empty.mkdir()
        current = self.write("now.txt", [("Steady", "ShortRun", "9 us")])

        status, _ = self.run_comparator(
            "--baseline", str(empty),
            "--current", str(current),
        )

        self.assertEqual(2, status)


class JsonTests(ComparatorTestCase):
    """The machine-readable form of the same comparison."""

    def pair(self, steady: str, rebuilding: str) -> list[tuple[str, str, str]]:
        return [
            ("SteadyRefresh", "ShortRun", steady),
            ("RebuildingRefresh", "ShortRun", rebuilding),
        ]

    def test_writes_each_method_and_its_ratio(self) -> None:
        baseline = self.write("base.txt", [("Steady", "ShortRun", "8 us")])
        current = self.write("now.txt", [("Steady", "ShortRun", "10 us")])

        status, output = self.run_comparator(
            "--baseline", str(baseline),
            "--current", str(current),
            "--json",
        )

        payload = json.loads(output)

        self.assertEqual(0, status)
        self.assertEqual("Steady", payload["methods"][0]["method"])
        self.assertAlmostEqual(1.25, payload["methods"][0]["ratio"])
        self.assertIsNone(payload["overhead"])
        self.assertFalse(payload["exceeded"])

    def test_includes_the_quotient_and_its_limit(self) -> None:
        baseline = self.write("base.txt", self.pair("9 us", "16.2 us"))
        current = self.write("now.txt", self.pair("8.5 us", "15.1 us"))

        status, output = self.run_comparator(
            "--baseline", str(baseline),
            "--current", str(current),
            "--job", "ShortRun",
            "--overhead", "RebuildingRefresh:SteadyRefresh",
            "--max-overhead", "2.2",
            "--json",
        )

        payload = json.loads(output)
        quoted = payload["overhead"]["rows"][0]

        self.assertEqual(0, status)
        self.assertEqual("RebuildingRefresh", payload["overhead"]["numerator"])
        self.assertAlmostEqual(1.8, quoted["baseline_quotient"])
        self.assertAlmostEqual(
            1.776, quoted["current_quotient"], places=3
        )
        self.assertFalse(quoted["over_limit"])

    def test_marks_a_quotient_over_the_limit_and_exits_one(self) -> None:
        baseline = self.write("base.txt", self.pair("8 us", "16 us"))
        current = self.write("now.txt", self.pair("8 us", "20 us"))

        status, output = self.run_comparator(
            "--baseline", str(baseline),
            "--current", str(current),
            "--job", "ShortRun",
            "--overhead", "RebuildingRefresh:SteadyRefresh",
            "--max-overhead", "2.2",
            "--json",
        )

        payload = json.loads(output)

        self.assertEqual(1, status)
        self.assertTrue(payload["exceeded"])
        self.assertTrue(payload["overhead"]["rows"][0]["over_limit"])

    def test_writes_the_report_to_a_file(self) -> None:
        baseline = self.write("base.txt", [("Steady", "ShortRun", "8 us")])
        current = self.write("now.txt", [("Steady", "ShortRun", "10 us")])
        report = self.directory / "comparison.json"

        status, output = self.run_comparator(
            "--baseline", str(baseline),
            "--current", str(current),
            "--json-output", str(report),
        )

        payload = json.loads(report.read_text(encoding="utf-8"))

        self.assertEqual(0, status)
        self.assertIn("Steady", output)
        self.assertAlmostEqual(1.25, payload["methods"][0]["ratio"])

    def test_writes_no_file_when_an_input_is_incomplete(self) -> None:
        baseline = self.write("base.txt", self.pair("8 us", "16 us"))
        current = self.write(
            "now.txt",
            [("SteadyRefresh", "ShortRun", "8 us")],
        )
        report = self.directory / "comparison.json"

        status, output = self.run_comparator(
            "--baseline", str(baseline),
            "--current", str(current),
            "--json-output", str(report),
        )

        self.assertEqual(2, status)
        self.assertEqual("", output)
        self.assertFalse(report.exists())

    def test_writes_the_file_when_a_limit_is_exceeded(self) -> None:
        baseline = self.write("base.txt", self.pair("8 us", "16 us"))
        current = self.write("now.txt", self.pair("8 us", "20 us"))
        report = self.directory / "comparison.json"

        status, _ = self.run_comparator(
            "--baseline", str(baseline),
            "--current", str(current),
            "--job", "ShortRun",
            "--overhead", "RebuildingRefresh:SteadyRefresh",
            "--max-overhead", "2.2",
            "--json-output", str(report),
        )

        payload = json.loads(report.read_text(encoding="utf-8"))

        self.assertEqual(1, status)
        self.assertTrue(payload["exceeded"])

    def test_rejects_both_json_destinations(self) -> None:
        baseline = self.write("base.txt", [("Steady", "ShortRun", "8 us")])
        current = self.write("now.txt", [("Steady", "ShortRun", "8 us")])

        status, _ = self.run_comparator(
            "--baseline", str(baseline),
            "--current", str(current),
            "--json",
            "--json-output", str(self.directory / "comparison.json"),
        )

        self.assertEqual(2, status)

    def test_writes_nothing_when_an_input_is_missing(self) -> None:
        baseline = self.write("base.txt", self.pair("8 us", "16 us"))
        current = self.write("now.txt", [("SteadyRefresh", "ShortRun", "8 us")])

        status, output = self.run_comparator(
            "--baseline", str(baseline),
            "--current", str(current),
            "--json",
        )

        self.assertEqual(2, status)
        self.assertEqual("", output)


class AmbiguousRunTests(ComparatorTestCase):
    """Rows that cannot be paired, or a table that moved its columns."""

    def typed_summary(
        self, rows: list[tuple[str, str, str, str]]
    ) -> str:
        lines = [
            "| Type | Method | Job | Mean |",
            "|----- |------- | --- |-----:|",
        ]

        for type_name, method, job, mean in rows:
            lines.append(f"| {type_name} | {method} | {job} | {mean} |")

        return "\n".join(lines) + "\n"

    def test_finds_the_mean_column_by_name(self) -> None:
        text = self.typed_summary(
            [
                ("First", "Steady", "ShortRun", "8 us"),
                ("Second", "Rebuilding", "ShortRun", "16 us"),
            ]
        )

        self.assertEqual(
            {("Steady", "ShortRun"): 8.0, ("Rebuilding", "ShortRun"): 16.0},
            comparator.parse_means(text),
        )

    def test_rejects_a_run_that_quotes_one_row_twice(self) -> None:
        baseline = self.write("base.txt", [("Steady", "ShortRun", "8 us")])
        current = self.write(
            "now.txt",
            [
                ("Steady", "ShortRun", "8 us"),
                ("Steady", "ShortRun", "9 us"),
            ],
        )

        status, output = self.run_comparator(
            "--baseline", str(baseline),
            "--current", str(current),
        )

        self.assertEqual(2, status)
        self.assertEqual("", output)

    def test_names_a_method_two_types_share(self) -> None:
        text = self.typed_summary(
            [
                ("First", "Steady", "ShortRun", "8 us"),
                ("Second", "Steady", "ShortRun", "9 us"),
            ]
        )

        with self.assertRaises(comparator.AmbiguousRows):
            comparator.parse_means(text)

    def test_fails_when_the_current_run_has_no_rows(self) -> None:
        baseline = self.write("base.txt", [("Steady", "ShortRun", "8 us")])
        current = self.directory / "now.txt"
        current.write_text("nothing here\n", encoding="utf-8")

        status, _ = self.run_comparator(
            "--baseline", str(baseline),
            "--current", str(current),
        )

        self.assertEqual(2, status)


if __name__ == "__main__":
    unittest.main()
