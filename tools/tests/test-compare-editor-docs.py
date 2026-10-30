#!/usr/bin/env python3
"""Self-tests for tools/compare-editor-docs.py."""

from __future__ import annotations

import contextlib
import importlib.util
import io
import struct
import tempfile
import unittest
import zlib
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location(
    "compare_editor_docs",
    TOOLS / "compare-editor-docs.py",
)
comparator = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(comparator)


def chunk(kind: bytes, payload: bytes) -> bytes:
    return (
        struct.pack(">I", len(payload))
        + kind
        + payload
        + struct.pack(">I", zlib.crc32(kind + payload) & 0xFFFFFFFF)
    )


def write_png(
    path: Path,
    width: int,
    height: int,
    rows: list[bytearray],
) -> None:
    raw = b"".join(b"\x00" + bytes(row) for row in rows)
    png = (
        b"\x89PNG\r\n\x1a\n"
        + chunk(
            b"IHDR",
            struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0),
        )
        + chunk(b"IDAT", zlib.compress(raw, 6))
        + chunk(b"IEND", b"")
    )
    path.write_bytes(png)


def make_rows(
    width: int,
    height: int,
    color: tuple[int, int, int, int] = (40, 50, 60, 255),
) -> list[bytearray]:
    return [bytearray(color * width) for _ in range(height)]


class CompareEditorDocsTests(unittest.TestCase):
    def setUp(self) -> None:
        self._temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self._temporary.cleanup)
        self.root = Path(self._temporary.name)
        self.reference = self.root / "reference"
        self.candidate = self.root / "candidate"
        self.reference.mkdir()
        self.candidate.mkdir()

    def run_comparator(self, *extra: str) -> tuple[int, str, str]:
        output = io.StringIO()
        errors = io.StringIO()

        with contextlib.redirect_stdout(output), contextlib.redirect_stderr(
            errors
        ):
            code = comparator.main(
                [
                    str(self.reference),
                    str(self.candidate),
                    *extra,
                ]
            )

        return code, output.getvalue(), errors.getvalue()

    def write_pair(
        self,
        width: int = 64,
        height: int = 64,
    ) -> tuple[list[bytearray], list[bytearray]]:
        reference = make_rows(width, height)
        candidate = [bytearray(row) for row in reference]

        write_png(
            self.reference / "editor.png",
            width,
            height,
            reference,
        )
        write_png(
            self.candidate / "editor.png",
            width,
            height,
            candidate,
        )

        return reference, candidate

    def rewrite_candidate(
        self,
        width: int,
        height: int,
        rows: list[bytearray],
    ) -> None:
        write_png(
            self.candidate / "editor.png",
            width,
            height,
            rows,
        )

    def compare_files(
        self,
        reference: Path,
        candidate: Path,
    ) -> tuple[int, str, str]:
        output = io.StringIO()
        errors = io.StringIO()

        with contextlib.redirect_stdout(output), contextlib.redirect_stderr(
            errors
        ):
            code = comparator.main([str(reference), str(candidate)])

        return code, output.getvalue(), errors.getvalue()

    def test_single_file_pair_passes(self) -> None:
        reference = self.root / "baseline.png"
        candidate = self.root / "window.png"
        rows = make_rows(32, 32)

        write_png(reference, 32, 32, rows)
        write_png(candidate, 32, 32, [bytearray(row) for row in rows])

        code, output, _ = self.compare_files(reference, candidate)

        self.assertEqual(code, 0)
        self.assertIn("window.png: OK", output)

    def test_single_file_pair_reports_a_different_window(self) -> None:
        reference = self.root / "baseline.png"
        candidate = self.root / "window.png"
        rows = make_rows(32, 32)
        changed = [bytearray(row) for row in rows]

        # A single pixel stays inside the tolerance; a band of them, as a
        # moved panel produces, must fail.
        for y in range(4, 8):
            for x in range(32):
                changed[y][x * 4] = 250

        write_png(reference, 32, 32, rows)
        write_png(candidate, 32, 32, changed)

        code, output, errors = self.compare_files(reference, candidate)

        self.assertEqual(code, 1)
        self.assertIn("window.png: FAIL", output)
        self.assertIn("does not match", errors)

    def test_mixing_a_file_and_a_directory_is_rejected(self) -> None:
        reference = self.root / "baseline.png"

        write_png(reference, 16, 16, make_rows(16, 16))

        with self.assertRaises(SystemExit) as caught:
            with contextlib.redirect_stderr(io.StringIO()):
                comparator.main([str(reference), str(self.candidate)])

        self.assertEqual(caught.exception.code, 2)

    def test_identical_images_pass(self) -> None:
        self.write_pair()

        code, output, errors = self.run_comparator()

        self.assertEqual(0, code, errors)
        self.assertIn("editor.png: OK", output)
        self.assertIn("worst channel delta 0", output)

    def test_channel_difference_within_tolerance_passes(self) -> None:
        width = 16
        height = 16
        _, candidate = self.write_pair(width, height)
        candidate[5][(5 * 4)] += 8
        self.rewrite_candidate(width, height, candidate)

        code, output, errors = self.run_comparator()

        self.assertEqual(0, code, errors)
        self.assertIn("worst channel delta 8", output)

    def test_channel_difference_over_tolerance_fails(self) -> None:
        width = 16
        height = 16
        _, candidate = self.write_pair(width, height)
        candidate[5][(5 * 4)] += 9
        self.rewrite_candidate(width, height, candidate)

        code, output, errors = self.run_comparator()

        self.assertEqual(1, code, errors)
        self.assertIn("editor.png: FAIL", output)

    def test_large_changed_region_fails(self) -> None:
        width = 64
        height = 64
        _, candidate = self.write_pair(width, height)

        for y in range(4, 20):
            for x in range(4, 20):
                candidate[y][x * 4] += 80

        self.rewrite_candidate(width, height, candidate)

        code, output, errors = self.run_comparator()

        self.assertEqual(1, code, errors)
        self.assertIn("editor.png: FAIL", output)

    def test_alpha_difference_is_compared(self) -> None:
        width = 16
        height = 16
        _, candidate = self.write_pair(width, height)
        candidate[5][(5 * 4) + 3] -= 20
        self.rewrite_candidate(width, height, candidate)

        code, _, _ = self.run_comparator()

        self.assertEqual(1, code)

    def test_custom_channel_tolerance_is_honoured(self) -> None:
        width = 16
        height = 16
        _, candidate = self.write_pair(width, height)
        candidate[5][(5 * 4)] += 20
        self.rewrite_candidate(width, height, candidate)

        code, _, errors = self.run_comparator(
            "--channel-tolerance",
            "30",
        )

        self.assertEqual(0, code, errors)

    def test_missing_candidate_fails(self) -> None:
        write_png(
            self.reference / "editor.png",
            8,
            8,
            make_rows(8, 8),
        )

        code, _, errors = self.run_comparator()

        self.assertEqual(1, code)
        self.assertIn("MISSING", errors)

    def test_size_mismatch_fails(self) -> None:
        write_png(
            self.reference / "editor.png",
            8,
            8,
            make_rows(8, 8),
        )
        write_png(
            self.candidate / "editor.png",
            4,
            4,
            make_rows(4, 4),
        )

        code, _, errors = self.run_comparator()

        self.assertEqual(1, code)
        self.assertIn("image size differs", errors)

    def test_malformed_candidate_fails(self) -> None:
        write_png(
            self.reference / "editor.png",
            8,
            8,
            make_rows(8, 8),
        )
        (self.candidate / "editor.png").write_bytes(b"not a png")

        code, _, errors = self.run_comparator()

        self.assertEqual(1, code)
        self.assertIn("not a PNG file", errors)

    def test_no_reference_images_is_a_usage_error(self) -> None:
        code, _, errors = self.run_comparator()

        self.assertEqual(2, code)
        self.assertIn("no PNG files", errors)


if __name__ == "__main__":
    unittest.main()
