#!/usr/bin/env python3
"""Writes a design snapshot under docs/design from a read of its live planning document (RR-141).

The live documents are the source of truth; docs/design holds revision-stamped copies (CLAUDE.md, "Live planning
documents"). This script is the converter that produces those copies, so that every refresh is reproducible and
reviewable. Python standard library only.

Input: the JSON of one Claude Docs read of the document at the released revision ({"data": {"rev", "xml", "complete"}},
or a JSON array whose text element holds that object, as the read tool saves it). The read must be complete, with
no gap left; a paged read is merged before it is converted.

Usage:
  eng/snapshots/snapshot.py <read.json> <docs/design/snapshot.md> --revision <n> --date <YYYY-MM-DD>

The snapshot keeps its first seven lines (the header the repository owns: the SNAPSHOT marker, the revision line with
the live document's URL, and the notes under it). The revision line is rewritten to name the revision and date; the
body is the converted document, whose title must equal the snapshot's. Proved by eng/snapshots/test_snapshot.py, which CI runs.
"""

import argparse
import json
import re
import sys
import xml.etree.ElementTree as ET

HEADER_LINES = 7
REVISION_LINE = re.compile(r"(\(https://claude\.ai/[^)]*\)), revision \d+, taken [0-9-]+\.")


def _escape(text, code):
    # Prose: escape "<" so a Markdown renderer does not take <sha> for an HTML tag; code spans stay literal.
    return text if code else text.replace("<", "\\<")


MARKS = {"bold": "**", "italic": "*", "code": "`"}


def _mark_run(child):
    """The mark of a run that is one bold, italic or code element, bare or wrapped in a text run with no other text."""
    if child.tag in MARKS:
        return child.tag, child
    if (child.tag == "text" and len(child) == 1 and child[0].tag in MARKS
            and not (child.text or "") and not (child[0].tail or "")):
        return child[0].tag, child[0]
    return None, None


def _inline(el, code=False):
    out = _escape(el.text or "", code)
    open_mark = None  # the mark of the run that ends out, when nothing has followed it
    for child in el:
        tag = child.tag
        mark_tag, mark_el = (None, None) if code else _mark_run(child)
        if mark_tag:
            inner = _inline(mark_el, mark_tag == "code")
            mark = MARKS[mark_tag]
            if inner:
                # Adjacent runs of the same mark join into one span, as the live document shows them; an empty run
                # writes nothing, so no stray "****" or lone "*" changes the text around it.
                if open_mark == mark:
                    out = out[:-len(mark)] + inner + mark
                else:
                    out += mark + inner + mark
                open_mark = mark
            tail = _escape(child.tail or "", code)
            out += tail
            if tail:
                open_mark = None
            continue
        open_mark = None
        if tag == "text":
            out += _inline(child, code)
        elif tag in MARKS:
            out += _inline(child, True)
        elif tag == "link":
            out += "[" + _inline(child, code) + "](" + child.get("href", "") + ")"
        elif tag == "mention":
            out += (child.get("label") or child.get("name") or child.get("ref") or "").lstrip("@")
        elif tag == "date":
            out += child.get("value", "")
        elif tag == "dropdown":
            # The read carries the selected option's name; a status is never guessed from its index.
            label = "".join(child.itertext()).strip()
            if not label:
                raise ValueError(f"a dropdown (enum {child.get('enum')}, index {child.get('index')}) has no label in the read")
            out += "[" + label + "]"
        elif tag == "embed":
            out += "<embed " + " ".join(f"{k}={v}" for k, v in child.attrib.items() if k not in ("id", "h", "rev")) + ">"
        elif tag == "hardBreak":
            out += "\n"
        else:
            out += _inline(child, code)
        out += _escape(child.tail or "", code)
    return out


def _block(el, out, depth=0):
    tag = el.tag
    if tag == "paragraph":
        text = _inline(el).strip()
        heading = el.get("heading")
        out.append("#" * int(heading) + " " + text if heading else text)
        out.append("")
    elif tag == "list":
        # depth is the indent, in columns, of this list's markers. Content under an item is indented by the width of
        # the item's marker (two columns for "- ", three for "1. "), as CommonMark needs for it to stay in the item.
        kind = el.get("kind", "bullet")
        for i, item in enumerate(el):
            marker = f"{i + 1}. " if kind in ("ordered", "number") else "- "
            box = ("[x] " if item.get("checked") == "true" else "[ ] ") if kind == "check" else ""
            inner = depth + len(marker)
            first = True
            for child in item:
                if child.tag == "paragraph":
                    out.append(" " * depth + (marker + box if first else " " * len(marker)) + _inline(child).strip())
                    first = False
                elif child.tag == "list":
                    _block(child, out, inner)
                else:
                    start = len(out)
                    _block(child, out, inner)
                    out[start:] = [(" " * inner + line) if line else line for line in out[start:]]
        if depth == 0:
            out.append("")
    elif tag == "table":
        # A cell is one line: its hard breaks become <br>, and "|" is escaped.
        rows = [[" ".join(_inline(p).strip() for p in cell.iter("paragraph")).replace("|", "\\|").replace("\n", "<br>")
                 for cell in row]
                for row in el.iter("row")]
        if rows:
            out.append("| " + " | ".join(rows[0]) + " |")
            out.append("|" + "---|" * len(rows[0]))
            out.extend("| " + " | ".join(row) + " |" for row in rows[1:])
            out.append("")
    elif tag == "codeBlock":
        # The fence is one backtick longer than the longest backtick run in the content, so the content cannot close it.
        content = "".join(el.itertext())
        fence = "`" * max(3, max((len(run) for run in re.findall(r"`+", content)), default=0) + 1)
        out.append(fence + (el.get("language") or ""))
        out.append(content)
        out.append(fence)
        out.append("")
    elif tag == "blockquote":
        for child in el:
            start = len(out)
            _block(child, out, depth)
            out[start:] = ["> " + line for line in out[start:]]
    elif tag == "embed":
        out.append(f"*[Diagram in the live document: {el.get('caption', '')}]*")
        out.append("")
    elif tag == "horizontalRule":
        out.append("---")
        out.append("")
    elif list(el):
        out.append(f"<!-- {tag} -->")
        for child in el:
            _block(child, out, depth)
    else:
        text = _inline(el).strip()
        if text:
            out.append(f"<!-- {tag} --> " + text)
            out.append("")


def convert(xml):
    """Converts the XML of a document read to the Markdown body of its snapshot."""
    root = ET.fromstring(xml)
    # A gap anywhere (a list item, table rows, a paragraph) means the read is not whole.
    gap = root.find(".//gap")
    if gap is not None:
        raise ValueError(f"the read has a gap of {gap.get('blocks')} blocks; merge the paged reads first")
    out = []
    for child in root:
        _block(child, out)
    return "\n".join(line.rstrip() for line in "\n".join(out).split("\n"))


def load_read(path):
    """Returns the data object of a saved read: a JSON object, or a JSON array whose text element holds one."""
    with open(path, encoding="utf-8") as f:
        raw = json.load(f)
    if isinstance(raw, list):
        raw = next(json.loads(x["text"]) for x in raw if x.get("text", "").lstrip().startswith("{"))
    data = raw["data"]
    if not data.get("complete"):
        raise ValueError("the read is not complete; read the remaining pages and merge them first")
    return data


def write_snapshot(snapshot_text, body, revision, date):
    """Keeps the snapshot's header and its URL, names revision and date in its revision line, and replaces the body.

    The body must be the same document: its title line must equal the snapshot's, so a read written into the wrong
    snapshot is refused.
    """
    lines = snapshot_text.split("\n")
    header = lines[:HEADER_LINES]
    if len(lines) <= HEADER_LINES or not lines[HEADER_LINES].startswith("# "):
        raise ValueError(f"line {HEADER_LINES + 1} of the snapshot is not the document title")
    title = body.split("\n", 1)[0]
    if title != lines[HEADER_LINES]:
        raise ValueError(f"the read's title {title!r} is not the snapshot's {lines[HEADER_LINES]!r}")
    new_line, count = REVISION_LINE.subn(lambda m: f"{m.group(1)}, revision {revision}, taken {date}.", header[2])
    if count != 1:
        raise ValueError("line 3 of the snapshot holds no revision line to rewrite")
    header[2] = new_line
    return "\n".join(header) + "\n" + body.rstrip("\n") + "\n"


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("read")
    parser.add_argument("snapshot")
    parser.add_argument("--revision", required=True, type=int)
    parser.add_argument("--date", required=True)
    args = parser.parse_args(argv)
    data = load_read(args.read)
    if data.get("rev") != args.revision:
        raise ValueError(f"the read is of revision {data.get('rev')}, not {args.revision}")
    with open(args.snapshot, encoding="utf-8") as f:
        current = f.read()
    result = write_snapshot(current, convert(data["xml"]), args.revision, args.date)
    with open(args.snapshot, "w", encoding="utf-8", newline="\n") as f:
        f.write(result)
    return 0


if __name__ == "__main__":
    sys.exit(main())
