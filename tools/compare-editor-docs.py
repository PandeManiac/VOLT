#!/usr/bin/env python3
"""Compare documentation screenshots with a fresh headless capture.

The editor's snapshot rasterizer is deterministic on one machine, but a
different platform can move antialiased coverage by a few channel
levels. This checker treats those differences the same way the golden
snapshot tests do: a pixel only counts as different when a channel
differs by more than the channel tolerance, and a capture passes when
the fraction of such pixels stays within the pixel tolerance.
"""

from __future__ import annotations

import argparse
import struct
import sys
import zlib
from pathlib import Path

SIGNATURE = b"\x89PNG\r\n\x1a\n"
DEFAULT_CHANNEL_TOLERANCE = 8
DEFAULT_PIXEL_TOLERANCE = 0.002


class PngError(Exception):
    """The file is not a PNG this checker can read."""


def paeth(left: int, above: int, corner: int) -> int:
    estimate = left + above - corner
    left_distance = abs(estimate - left)
    above_distance = abs(estimate - above)
    corner_distance = abs(estimate - corner)

    if left_distance <= above_distance and left_distance <= corner_distance:
        return left

    return above if above_distance <= corner_distance else corner


def read_png(path: Path) -> tuple[int, int, list[bytes]]:
    """Read an eight-bit, non-interlaced RGBA PNG into decoded rows."""

    data = path.read_bytes()

    if not data.startswith(SIGNATURE):
        raise PngError("not a PNG file")

    offset = len(SIGNATURE)
    width = 0
    height = 0
    bit_depth = -1
    color_type = -1
    interlace = -1
    compressed = bytearray()
    complete = False

    while offset + 12 <= len(data):
        length = struct.unpack(">I", data[offset : offset + 4])[0]
        chunk_type = data[offset + 4 : offset + 8]
        offset += 8

        if length > len(data) - offset - 4:
            raise PngError("truncated PNG chunk")

        chunk = data[offset : offset + length]
        offset += length

        crc = struct.unpack(">I", data[offset : offset + 4])[0]
        offset += 4

        if (zlib.crc32(chunk_type + chunk) & 0xFFFFFFFF) != crc:
            raise PngError(
                f"bad CRC for {chunk_type.decode('latin-1')} chunk"
            )

        if chunk_type == b"IHDR":
            if length != 13:
                raise PngError("malformed PNG header")

            (
                width,
                height,
                bit_depth,
                color_type,
                compression,
                filter_method,
                interlace,
            ) = struct.unpack(">IIBBBBB", chunk)

            if compression != 0 or filter_method != 0:
                raise PngError("unsupported PNG compression or filter method")
        elif chunk_type == b"IDAT":
            compressed.extend(chunk)
        elif chunk_type == b"IEND":
            complete = True
            break

    if width <= 0 or height <= 0 or not complete:
        raise PngError("PNG is missing its header or end chunk")

    if bit_depth != 8 or color_type != 6 or interlace != 0:
        raise PngError(
            "only eight-bit RGBA, non-interlaced PNGs are supported"
        )

    try:
        raw = zlib.decompress(bytes(compressed))
    except zlib.error as error:
        raise PngError(f"cannot decompress image data: {error}") from error

    stride = width * 4
    expected = (stride + 1) * height

    if len(raw) != expected:
        raise PngError("PNG image data has the wrong length")

    rows: list[bytes] = []
    previous = bytearray(stride)
    cursor = 0

    for _ in range(height):
        filter_type = raw[cursor]
        cursor += 1

        row = bytearray(raw[cursor : cursor + stride])
        cursor += stride

        if filter_type == 0:
            pass
        elif filter_type == 1:
            for index in range(4, stride):
                row[index] = (row[index] + row[index - 4]) & 0xFF
        elif filter_type == 2:
            for index in range(stride):
                row[index] = (row[index] + previous[index]) & 0xFF
        elif filter_type == 3:
            for index in range(stride):
                left = row[index - 4] if index >= 4 else 0
                row[index] = (
                    row[index] + ((left + previous[index]) // 2)
                ) & 0xFF
        elif filter_type == 4:
            for index in range(stride):
                left = row[index - 4] if index >= 4 else 0
                above = previous[index]
                corner = previous[index - 4] if index >= 4 else 0
                row[index] = (
                    row[index] + paeth(left, above, corner)
                ) & 0xFF
        else:
            raise PngError(f"unsupported PNG filter type {filter_type}")

        rows.append(bytes(row))
        previous = row

    return width, height, rows


def compare(
    reference: Path,
    candidate: Path,
    channel_tolerance: int,
) -> tuple[int, float, int]:
    """Return the differing-pixel count, ratio, and worst channel delta."""

    reference_width, reference_height, reference_rows = read_png(reference)
    candidate_width, candidate_height, candidate_rows = read_png(candidate)

    if (reference_width, reference_height) != (
        candidate_width,
        candidate_height,
    ):
        raise PngError(
            "image size differs: "
            f"{reference_width}x{reference_height} versus "
            f"{candidate_width}x{candidate_height}"
        )

    differing = 0
    worst = 0

    for reference_row, candidate_row in zip(
        reference_rows, candidate_rows
    ):
        for index in range(0, len(reference_row), 4):
            red = abs(reference_row[index] - candidate_row[index])
            green = abs(
                reference_row[index + 1] - candidate_row[index + 1]
            )
            blue = abs(
                reference_row[index + 2] - candidate_row[index + 2]
            )
            alpha = abs(
                reference_row[index + 3] - candidate_row[index + 3]
            )

            delta = max(red, green, blue, alpha)

            if delta > worst:
                worst = delta

            if delta > channel_tolerance:
                differing += 1

    total = reference_width * reference_height

    return differing, differing / total, worst


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Compare committed editor documentation screenshots with a "
            "fresh capture using a per-channel and pixel tolerance."
        )
    )
    parser.add_argument(
        "reference",
        type=Path,
        help="directory containing the committed PNG images",
    )
    parser.add_argument(
        "candidate",
        type=Path,
        help="directory containing the fresh capture",
    )
    parser.add_argument(
        "--channel-tolerance",
        type=int,
        default=DEFAULT_CHANNEL_TOLERANCE,
        help=(
            "largest per-channel difference that still counts as equal "
            f"(default: {DEFAULT_CHANNEL_TOLERANCE})"
        ),
    )
    parser.add_argument(
        "--pixel-tolerance",
        type=float,
        default=DEFAULT_PIXEL_TOLERANCE,
        help=(
            "largest fraction of differing pixels that still passes "
            f"(default: {DEFAULT_PIXEL_TOLERANCE})"
        ),
    )

    arguments = parser.parse_args(argv)

    if arguments.channel_tolerance < 0:
        parser.error("--channel-tolerance must be zero or greater")

    if not 0.0 <= arguments.pixel_tolerance <= 1.0:
        parser.error("--pixel-tolerance must be between zero and one")

    references = sorted(arguments.reference.glob("*.png"))

    if not references:
        print(
            f"error: no PNG files in {arguments.reference}",
            file=sys.stderr,
        )
        return 2

    failures: list[str] = []

    for reference in references:
        candidate = arguments.candidate / reference.name

        if not candidate.exists():
            print(
                f"{reference.name}: MISSING from {arguments.candidate}",
                file=sys.stderr,
            )
            failures.append(reference.name)
            continue

        try:
            differing, ratio, worst = compare(
                reference,
                candidate,
                arguments.channel_tolerance,
            )
        except (PngError, OSError) as error:
            print(f"{reference.name}: ERROR: {error}", file=sys.stderr)
            failures.append(reference.name)
            continue

        allowed = arguments.pixel_tolerance * 100.0
        passed = ratio <= arguments.pixel_tolerance

        print(
            f"{reference.name}: {'OK' if passed else 'FAIL'} "
            f"(worst channel delta {worst}, "
            f"{differing} differing pixels / {ratio * 100.0:.3f}%; "
            f"allowed {arguments.channel_tolerance} per channel and "
            f"{allowed:.3f}% of pixels)"
        )

        if not passed:
            failures.append(reference.name)

    if failures:
        print(
            f"{len(failures)} documentation screenshot(s) do not match.",
            file=sys.stderr,
        )
        return 1

    print("Documentation screenshots are in sync.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
