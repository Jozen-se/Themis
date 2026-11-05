"""Fixture tests for the snapshot converter (RR-141). Run by CI: python3 -m unittest discover -s eng/snapshots."""

import json
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import snapshot  # noqa: E402

FIXTURE_XML = (
    "<doc node='n' rev='9'>"
    "<paragraph id='a' heading='1'><text id='b'>Themis Fixture</text></paragraph>"
    "<paragraph id='c'><mention id='d' label='@Sebastian Hansson'/><text id='e'> wrote </text>"
    "<bold id='f'>bold</bold><text id='g'>, </text><italic id='h'>italic</italic><text id='i'> and </text>"
    "<link id='j' href='https://example.com'>a link</link><text id='k'> on </text><date id='l' value='2026-11-05'/>"
    "<text id='m'>.</text></paragraph>"
    "<paragraph id='n'><text id='o'>A summary named \"Themis review of &lt;sha&gt;\" and </text>"
    "<text id='p'><code>Item</code></text><text id='q'><code>Company</code></text>"
    "<text id='r'><code>.item_posting_group</code></text><text id='s'>; a span keeps </text>"
    "<text id='t'><code>a &lt;b&gt;</code></text><text id='u'>.</text></paragraph>"
    "<list id='v' kind='check'><listItem id='w' checked='true'><paragraph id='x'><text id='y'>Done</text></paragraph>"
    "</listItem><listItem id='z'><paragraph id='aa'><text id='ab'>Open</text></paragraph></listItem></list>"
    "<list id='ac' kind='ordered'><listItem id='ad'><paragraph id='ae'><text id='af'>First</text></paragraph>"
    "<list id='ag' kind='bullet'><listItem id='ah'><paragraph id='ai'><text id='aj'>Nested</text></paragraph>"
    "</listItem></list></listItem></list>"
    "<table id='ak'><row id='al'><cell id='am'><paragraph id='an'><text id='ao'>ID</text></paragraph></cell>"
    "<cell id='ap'><paragraph id='aq'><text id='ar'>Status</text></paragraph></cell></row>"
    "<row id='as'><cell id='at'><paragraph id='au'><text id='av'>D-1 a|b</text></paragraph></cell>"
    "<cell id='aw'><paragraph id='ax'><dropdown id='ay' enum='43671c2e-fff4' index='0'/></paragraph></cell></row>"
    "</table>"
    "<codeBlock id='az' language='markdown'>Keep &lt;sha&gt; as it is</codeBlock>"
    "<paragraph id='ba'><bold id='bb'></bold><text id='bc'>Trailing space   </text></paragraph>"
    "</doc>"
)

FIXTURE_MARKDOWN = "\n".join([
    "# Themis Fixture",
    "",
    "Sebastian Hansson wrote **bold**, *italic* and [a link](https://example.com) on 2026-11-05.",
    "",
    "A summary named \"Themis review of \\<sha>\" and `ItemCompany.item_posting_group`; a span keeps `a <b>`.",
    "",
    "- [x] Done",
    "- [ ] Open",
    "",
    "1. First",
    "  - Nested",
    "",
    "| ID | Status |",
    "|---|---|",
    "| D-1 a\\|b | [Decided] |",
    "",
    "```markdown",
    "Keep <sha> as it is",
    "```",
    "",
    "Trailing space",
    "",
])

HEADER = "\n".join([
    "<!-- SNAPSHOT. Do not edit by hand. Regenerate from the live document. -->",
    "",
    "> **Snapshot** of the live document *Themis Fixture* (https://claude.ai/code/artifact/old), revision 1, taken 2026-01-01.",
    "> The live document is authoritative.",
    "> Where this copy and the live document differ, the live document wins.",
    "> Diagrams are not reproduced; their captions are kept in place.",
    "",
])


class ConvertTests(unittest.TestCase):
    def test_RR_141_the_converter_writes_the_fixture_document_as_its_expected_markdown(self):
        self.assertEqual(FIXTURE_MARKDOWN, snapshot.convert(FIXTURE_XML))

    def test_RR_141_a_gap_left_in_the_read_is_refused(self):
        with self.assertRaises(ValueError):
            snapshot.convert("<doc node='n' rev='9'><gap blocks='3'/></doc>")


class SnapshotTests(unittest.TestCase):
    def test_RR_141_the_header_is_kept_and_its_revision_line_names_the_new_url_revision_and_date(self):
        current = HEADER + "\n# Themis Fixture\n\nOld body\n"
        result = snapshot.write_snapshot(current, FIXTURE_MARKDOWN, "https://claude.ai/code/artifact/new", 9, "2026-11-05")
        lines = result.split("\n")
        self.assertEqual(HEADER.split("\n")[:2], lines[:2])
        self.assertEqual("> **Snapshot** of the live document *Themis Fixture* (https://claude.ai/code/artifact/new), revision 9, taken 2026-11-05.", lines[2])
        self.assertEqual(HEADER.split("\n")[3:7], lines[3:7])
        self.assertEqual(FIXTURE_MARKDOWN.rstrip("\n") + "\n", "\n".join(lines[7:]))

    def test_RR_141_a_snapshot_without_its_header_is_refused(self):
        with self.assertRaises(ValueError):
            snapshot.write_snapshot("# Themis Fixture\n\nBody\n", FIXTURE_MARKDOWN, "u", 9, "2026-11-05")

    def test_RR_141_the_command_line_refuses_an_incomplete_read_and_a_read_of_another_revision(self):
        with tempfile.TemporaryDirectory() as directory:
            target = os.path.join(directory, "snapshot.md")
            with open(target, "w", encoding="utf-8") as f:
                f.write(HEADER + "\n# Themis Fixture\n\nOld body\n")
            incomplete = os.path.join(directory, "incomplete.json")
            with open(incomplete, "w", encoding="utf-8") as f:
                json.dump({"data": {"rev": 9, "complete": False, "xml": FIXTURE_XML}}, f)
            other = os.path.join(directory, "other.json")
            with open(other, "w", encoding="utf-8") as f:
                json.dump({"data": {"rev": 8, "complete": True, "xml": FIXTURE_XML}}, f)
            args = [target, "--url", "u", "--revision", "9", "--date", "2026-11-05"]
            with self.assertRaises(ValueError):
                snapshot.main([incomplete] + args)
            with self.assertRaises(ValueError):
                snapshot.main([other] + args)

    def test_RR_141_the_command_line_writes_the_snapshot_from_a_read_saved_as_the_read_tool_saves_it(self):
        with tempfile.TemporaryDirectory() as directory:
            target = os.path.join(directory, "snapshot.md")
            with open(target, "w", encoding="utf-8") as f:
                f.write(HEADER + "\n# Themis Fixture\n\nOld body\n")
            saved = os.path.join(directory, "read.json")
            payload = {"verdict": "allow", "data": {"rev": 9, "complete": True, "xml": FIXTURE_XML}}
            with open(saved, "w", encoding="utf-8") as f:
                json.dump([{"type": "text", "text": "notice"}, {"type": "text", "text": json.dumps(payload)}], f)
            snapshot.main([saved, target, "--url", "https://claude.ai/code/artifact/new", "--revision", "9", "--date", "2026-11-05"])
            with open(target, encoding="utf-8") as f:
                written = f.read()
            self.assertIn("revision 9, taken 2026-11-05.", written)
            self.assertTrue(written.endswith(FIXTURE_MARKDOWN.rstrip("\n") + "\n"))


if __name__ == "__main__":
    unittest.main()
