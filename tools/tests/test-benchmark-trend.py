#!/usr/bin/env python3
"""Self-tests for tools/benchmark-trend.py."""

from __future__ import annotations

import contextlib
import importlib.util
import io
import json
import tempfile
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location(
    "benchmark_trend",
    TOOLS / "benchmark-trend.py",
)
trend = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(trend)


class TrendTestCase(unittest.TestCase):
    def setUp(self) -> None:
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)

    def write(
        self,
        name: str,
        quotients: list[tuple[str, float]],
        *,
        numerator: str = "RebuildingRefresh",
        denominator: str = "SteadyRefresh",
        limit: float | None = 2.2,
        over: bool = False,
    ) -> Path:
        path = self.root / name
        path.write_text(
            json.dumps(
                {
                    "baseline": "base",
                    "current": "now",
                    "job": None,
                    "max_ratio": None,
                    "methods": [],
                    "overhead": {
                        "numerator": numerator,
                        "denominator": denominator,
                        "max": limit,
                        "rows": [
                            {
                                "job": job,
                                "baseline_numerator_us": 16.074,
                                "baseline_denominator_us": 9.053,
                                "current_numerator_us": 15.126,
                                "current_denominator_us": 8.495,
                                "baseline_quotient": 1.78,
                                "current_quotient": quotient,
                                "over_limit": over,
                            }
                            for job, quotient in quotients
                        ],
                    },
                    "exceeded": over,
                }
            ),
            encoding="utf-8",
        )

        return path

    def plain(self, name: str, document: dict) -> Path:
        path = self.root / name
        path.write_text(json.dumps(document), encoding="utf-8")

        return path

    def run_trend(self, *paths: Path) -> tuple[int, str, str]:
        out, err = io.StringIO(), io.StringIO()

        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            status = trend.main([str(path) for path in paths])

        return status, out.getvalue(), err.getvalue()


class TrendTests(TrendTestCase):
    def test_prints_each_quotient_in_the_given_order(self) -> None:
        first = self.write("01.json", [("ShortRun", 1.78)])
        second = self.write("02.json", [("ShortRun", 1.82)])

        status, output, _ = self.run_trend(first, second)

        self.assertEqual(0, status)
        self.assertIn("1.78x", output)
        self.assertIn("1.82x", output)
        self.assertLess(output.index("01.json"), output.index("02.json"))

    def test_shows_the_change_from_the_previous_run(self) -> None:
        first = self.write("01.json", [("ShortRun", 1.78)])
        second = self.write("02.json", [("ShortRun", 1.82)])

        _, output, _ = self.run_trend(first, second)
        rows = [
            line
            for line in output.splitlines()
            if line.startswith(str(self.root))
        ]

        self.assertEqual(2, len(rows))
        self.assertEqual(
            ["ShortRun", "1.78x", "-", "2.20x"], rows[0].split()[1:]
        )
        self.assertEqual(
            ["ShortRun", "1.82x", "+0.04", "2.20x"],
            rows[1].split()[1:],
        )

    def test_names_the_methods_it_quotes(self) -> None:
        document = self.write("01.json", [("ShortRun", 1.78)])

        _, output, _ = self.run_trend(document)

        self.assertIn("RebuildingRefresh over SteadyRefresh:", output)

    def test_marks_a_quotient_over_the_limit(self) -> None:
        document = self.write("01.json", [("ShortRun", 2.5)], over=True)

        _, output, _ = self.run_trend(document)
        over = [
            line
            for line in output.splitlines()
            if line.startswith(str(self.root))
        ]

        self.assertEqual(1, len(over))
        self.assertTrue(over[0].endswith("over"), over[0])

    def test_document_without_a_quotient_is_an_input_error(self) -> None:
        document = self.plain(
            "plain.json",
            {
                "baseline": "base",
                "current": "now",
                "methods": [],
                "overhead": None,
                "exceeded": False,
            },
        )

        status, output, error = self.run_trend(document)

        self.assertEqual(2, status)
        self.assertEqual("", output)
        self.assertIn("no overhead quotient", error)

    def test_missing_document_is_an_input_error(self) -> None:
        status, output, error = self.run_trend(self.root / "absent.json")

        self.assertEqual(2, status)
        self.assertEqual("", output)
        self.assertIn("no such document", error)

    def test_mixing_methods_is_an_input_error(self) -> None:
        first = self.write("01.json", [("ShortRun", 1.78)])
        second = self.write("02.json", [("ShortRun", 1.80)], denominator="Other")

        status, output, error = self.run_trend(first, second)

        self.assertEqual(2, status)
        self.assertEqual("", output)
        self.assertIn("different methods", error)


if __name__ == "__main__":
    unittest.main()
