#!/usr/bin/env python3
"""Self-tests for tools/verify-benchmark-run.py."""

from __future__ import annotations

import contextlib
import importlib.util
import io
import tempfile
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location(
    "verify_benchmark_run",
    TOOLS / "verify-benchmark-run.py",
)
verifier = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(verifier)

REQUIRED = [
    "--require",
    "SteadyRefresh:ShortRun",
    "--require",
    "RebuildingRefresh:ShortRun",
]


def run_summary(rows: list[tuple[str, str, str]]) -> str:
    """Builds the part of a run's output that the verifier reads."""
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


def pair(steady: str, rebuilding: str) -> list[tuple[str, str, str]]:
    return [
        ("SteadyRefresh", "ShortRun", steady),
        ("RebuildingRefresh", "ShortRun", rebuilding),
    ]


class VerifierTestCase(unittest.TestCase):
    def setUp(self) -> None:
        self._root = tempfile.TemporaryDirectory()
        self.addCleanup(self._root.cleanup)
        self.directory = Path(self._root.name)

    def write(self, name: str, text: str) -> Path:
        path = self.directory / name
        path.write_text(text, encoding="utf-8")

        return path

    def run_verifier(self, *args: object) -> tuple[int, str, str]:
        out, err = io.StringIO(), io.StringIO()
        argv = [str(arg) for arg in args]

        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            status = verifier.main(argv)

        return status, out.getvalue(), err.getvalue()


class CompleteRunTests(VerifierTestCase):
    def test_accepts_a_run_with_every_required_measurement(self) -> None:
        run = self.write("run.txt", run_summary(pair("8.5 us", "15.1 us")))

        status, output, error = self.run_verifier("--run", run, *REQUIRED)

        self.assertEqual(0, status)
        self.assertEqual("", error)
        self.assertIn("SteadyRefresh (ShortRun): 8.5 us", output)
        self.assertIn("RebuildingRefresh (ShortRun): 15.1 us", output)

    def test_reads_a_run_from_a_directory(self) -> None:
        directory = self.directory / "artifact"
        directory.mkdir()
        (directory / "BenchmarkRun.txt").write_text(
            run_summary(pair("8.5 us", "15.1 us")),
            encoding="utf-8",
        )

        status, _, _ = self.run_verifier("--run", directory, *REQUIRED)

        self.assertEqual(0, status)


class IncompleteRunTests(VerifierTestCase):
    """A launched benchmark that failed is a row of NA, not a missing exit."""

    def test_fails_when_a_required_row_is_unmeasured(self) -> None:
        run = self.write("run.txt", run_summary(pair("NA", "NA")))

        status, output, error = self.run_verifier("--run", run, *REQUIRED)

        self.assertEqual(1, status)
        self.assertEqual("", output)
        self.assertIn("SteadyRefresh (ShortRun) produced NA", error)
        self.assertIn("RebuildingRefresh (ShortRun) produced NA", error)

    def test_fails_when_only_one_row_is_unmeasured(self) -> None:
        run = self.write("run.txt", run_summary(pair("8.5 us", "NA")))

        status, _, error = self.run_verifier("--run", run, *REQUIRED)

        self.assertEqual(1, status)
        self.assertIn("RebuildingRefresh (ShortRun) produced NA", error)
        self.assertNotIn("SteadyRefresh (ShortRun)", error)

    def test_fails_when_a_required_row_is_absent(self) -> None:
        run = self.write(
            "run.txt",
            run_summary([("SteadyRefresh", "ShortRun", "8.5 us")]),
        )

        status, _, error = self.run_verifier("--run", run, *REQUIRED)

        self.assertEqual(1, status)
        self.assertIn("RebuildingRefresh (ShortRun) has no row", error)

    def test_fails_when_a_required_row_is_quoted_twice(self) -> None:
        run = self.write(
            "run.txt",
            run_summary(
                pair("8.5 us", "15.1 us")
                + [("SteadyRefresh", "ShortRun", "9.1 us")]
            ),
        )

        status, _, error = self.run_verifier("--run", run, *REQUIRED)

        self.assertEqual(1, status)
        self.assertIn("SteadyRefresh (ShortRun) is quoted 2 times", error)

    def test_a_run_without_a_table_did_not_finish(self) -> None:
        run = self.write("run.txt", "// no summary here\n")

        status, output, error = self.run_verifier("--run", run, *REQUIRED)

        self.assertEqual(1, status)
        self.assertEqual("", output)
        self.assertIn("no benchmark summary table", error)

    def test_lists_the_benchmarks_that_reported_issues(self) -> None:
        text = run_summary(pair("NA", "NA")).replace(
            "// * Summary *",
            "Benchmarks with issues:\n"
            "  EcsInspectorRefreshBenchmarks.SteadyRefresh: ShortRun\n"
            "\n// * Summary *",
        )
        run = self.write("run.txt", text)

        _, _, error = self.run_verifier("--run", run, *REQUIRED)

        self.assertIn(
            "benchmark with issues: "
            "EcsInspectorRefreshBenchmarks.SteadyRefresh: ShortRun",
            error,
        )

    def test_explains_an_ambiguous_project_search(self) -> None:
        run = self.write(
            "run.txt",
            "// Generate Exception: System.NotSupportedException: "
            "Found more than one matching project file for Volt.Benchmarks\n",
        )

        _, _, error = self.run_verifier("--run", run, *REQUIRED)

        self.assertIn("more than one copy of the benchmark project", error)
        self.assertIn("scratch copy of the sources", error)


class UsageTests(VerifierTestCase):
    def test_rejects_a_requirement_without_a_job(self) -> None:
        run = self.write("run.txt", run_summary(pair("8.5 us", "15.1 us")))

        status, _, error = self.run_verifier("--run", run, "--require", "Steady")

        self.assertEqual(2, status)
        self.assertIn("expects method:job", error)

    def test_requires_at_least_one_measurement(self) -> None:
        run = self.write("run.txt", run_summary(pair("8.5 us", "15.1 us")))

        status, _, error = self.run_verifier("--run", run)

        self.assertEqual(2, status)
        self.assertIn("--require METHOD:JOB is needed", error)

    def test_reports_a_missing_run(self) -> None:
        status, _, error = self.run_verifier(
            "--run", self.directory / "absent.txt", *REQUIRED
        )

        self.assertEqual(2, status)
        self.assertIn("absent.txt", error)


if __name__ == "__main__":
    unittest.main()
