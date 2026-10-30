#!/usr/bin/env python3
"""Checks the guides' contents lists against their own headings.

Every `- [Title](#anchor)` entry under a `## Contents` heading must point
at a heading the document actually has, and every level-two heading must
be listed exactly once. The anchors use GitHub's slug rule: lower case,
punctuation dropped, spaces turned into hyphens. Inline code and
emphasis markers are stripped first because they do not survive into the
slug.

Run directly (`python3 tools/tests/test-doc-contents.py`) or through
`unittest` discovery. Only the standard library is used.
"""

from __future__ import annotations

import pathlib
import re
import unittest

REPO_ROOT = pathlib.Path(__file__).resolve().parents[2]

# Documents that open with a contents list. The milestone log is a
# date-ordered history and deliberately has none.
DOCUMENTS = (
    "docs/editor-guide.md",
    "docs/ui-framework.md",
    "docs/editor-control-audit.md",
)

HEADING_PATTERN = re.compile(r"^(#{1,6})\s+(.*?)\s*$")
ENTRY_PATTERN = re.compile(r"^-\s+\[(?P<title>.+?)\]\(#(?P<anchor>[^)]+)\)\s*$")
CODE_SPAN_PATTERN = re.compile(r"`([^`]*)`")
EMPHASIS_PATTERN = re.compile(r"[*_]")


def slugify(title: str) -> str:
    """Returns the GitHub anchor for a heading.

    Inline code is unwrapped, emphasis markers are dropped, punctuation
    other than hyphens and underscores goes away, and whitespace becomes
    hyphens. That covers every heading in these guides; a heading that
    needed duplicate-suffix handling would fail the uniqueness check.
    """

    text = CODE_SPAN_PATTERN.sub(r"\1", title)
    text = EMPHASIS_PATTERN.sub("", text)
    text = text.strip().lower()

    slug = []
    for character in text:
        if character.isalnum() or character in "-_":
            slug.append(character)
        elif character.isspace():
            slug.append("-")

    return "".join(slug)


def parse(path: pathlib.Path) -> tuple[list[str], list[tuple[str, str]]]:
    """Returns the level-two heading anchors and the contents entries."""

    headings: list[str] = []
    entries: list[tuple[str, str]] = []
    in_contents = False

    for line in path.read_text(encoding="utf-8").splitlines():
        match = HEADING_PATTERN.match(line)

        if match:
            level = len(match.group(1))
            title = match.group(2)

            if level <= 2:
                in_contents = level == 2 and slugify(title) == "contents"

            if level == 2 and slugify(title) != "contents":
                headings.append(slugify(title))

            continue

        if in_contents:
            entry = ENTRY_PATTERN.match(line.strip())

            if entry:
                entries.append((entry.group("title"), entry.group("anchor")))

    return headings, entries


class DocContentsTests(unittest.TestCase):
    def test_documents_exist(self) -> None:
        for document in DOCUMENTS:
            self.assertTrue((REPO_ROOT / document).is_file(), document)

    def test_every_entry_points_at_a_heading(self) -> None:
        for document in DOCUMENTS:
            headings, entries = parse(REPO_ROOT / document)

            self.assertTrue(entries, f"{document} has no contents entries")

            for title, anchor in entries:
                self.assertIn(
                    anchor,
                    headings,
                    f"{document}: '{title}' links to #{anchor}, "
                    "which is not a heading",
                )

    def test_every_section_is_listed_once(self) -> None:
        for document in DOCUMENTS:
            headings, entries = parse(REPO_ROOT / document)
            listed = [anchor for _, anchor in entries]

            self.assertEqual(
                len(listed),
                len(set(listed)),
                f"{document} lists a section more than once",
            )

            self.assertEqual(
                sorted(listed),
                sorted(headings),
                f"{document} contents do not match its level-two headings",
            )

    def test_slug_rule(self) -> None:
        self.assertEqual(
            slugify("Styling, metrics, and icons"), "styling-metrics-and-icons"
        )
        self.assertEqual(slugify("`Ctrl+R` and keys"), "ctrlr-and-keys")
        self.assertEqual(slugify("**Reset** view"), "reset-view")


if __name__ == "__main__":
    unittest.main()
