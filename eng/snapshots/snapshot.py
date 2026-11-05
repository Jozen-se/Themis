#!/usr/bin/env python3
"""Writes a design snapshot under docs/design from a read of its live planning document (RR-141).

The live documents are the source of truth; docs/design holds revision-stamped copies (CLAUDE.md, "Live planning
documents"). This script is the converter that produces those copies, so that every refresh is reproducible and
reviewable. Python standard library only.

Input: the JSON of one Claude Docs read of the document at the released revision ({"data": {"rev", "xml", "complete"}},
or a JSON array whose text element holds that object, as the read tool saves it). The read must be complete, with
no gap left; a paged read is merged before it is converted.

Usage:
  eng/snapshots/snapshot.py <read.json> <docs/design/snapshot.md> --url <live document URL> --revision <n> --date <YYYY-MM-DD>

The snapshot keeps its first seven lines (the header the repository owns: the SNAPSHOT marker, the revision line and
the notes under it). The revision line is rewritten to name the URL, revision and date; the body is the converted
document. Proved by eng/snapshots/test_snapshot.py, which CI runs.
"""

import argparse
import json
import re
import sys
import xml.etree.ElementTree as ET

HEADER_LINES = 7
REVISION_LINE = re.compile(r"\(https://claude\.ai/[^)]*\), revision \d+, taken [0-9-]+\.")
# Enum labels for dropdowns whose label text the read leaves empty (the decision status column).
DROPDOWN_LABELS = {"43671c2e-fff4": ["Decided", "Proposed", "Open"]}


def _escape(text, code):
    # Prose: escape "<" so a Markdown renderer does not take <sha> for an HTML tag; code spans stay literal.
    return text if code else text.replace("<", "\\<")


def _inline(el, code=False):
    out = _escape(el.text or "", code)
    previous_was_code = False
    for child in el:
        tag = child.tag
        if (tag == "text" and not code and len(child) == 1 and child[0].tag == "code"
                and not (child.text or "") and not (child[0].tail or "")):
            # A text run holding only a code span: adjacent runs join into one span, as the live document shows it.
            piece = _inline(child, code)
            if previous_was_code and out.endswith("`"):
                out = out[:-1] + piece[1:]
            else:
                out += piece
            out += _escape(child.tail or "", code)
            previous_was_code = not (child.tail or "")
            continue
        previous_was_code = False
        if tag == "text":
            out += _inline(child, code)
        elif tag == "bold":
            out += "**" + _inline(child, code) + "**"
        elif tag == "italic":
            out += "*" + _inline(child, code) + "*"
        elif tag == "code":
            out += "`" + _inline(child, True) + "`"
        elif tag == "link":
            out += "[" + _inline(child, code) + "](" + child.get("href", "") + ")"
        elif tag == "mention":
            out += (child.get("label") or child.get("name") or child.get("ref") or "").lstrip("@")
        elif tag == "date":
            out += child.get("value", "")
        elif tag == "dropdown":
            label = "".join(child.itertext()).strip()
            index, enum = child.get("index"), child.get("enum")
            if not label and index is not None and enum in DROPDOWN_LABELS:
                label = DROPDOWN_LABELS[enum][int(index)]
            out += "[" + (label or f"dropdown enum={enum} index={index}") + "]"
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
        kind = el.get("kind", "bullet")
        for i, item in enumerate(el):
            prefix = ("  " * depth) + (f"{i + 1}. " if kind in ("ordered", "number") else "- ")
            if kind == "check":
                prefix += "[x] " if item.get("checked") == "true" else "[ ] "
            first = True
            for child in item:
                if child.tag == "paragraph":
                    out.append((prefix if first else "  " * depth + "  ") + _inline(child).strip())
                    first = False
                else:
                    _block(child, out, depth + 1)
        if depth == 0:
            out.append("")
    elif tag == "table":
        rows = [[" ".join(_inline(p).strip() for p in cell.iter("paragraph")).replace("|", "\\|") for cell in row]
                for row in el.iter("row")]
        if rows:
            out.append("| " + " | ".join(rows[0]) + " |")
            out.append("|" + "---|" * len(rows[0]))
            out.extend("| " + " | ".join(row) + " |" for row in rows[1:])
            out.append("")
    elif tag == "codeBlock":
        out.append("```" + (el.get("language") or ""))
        out.append("".join(el.itertext()))
        out.append("```")
        out.append("")
    elif tag == "blockquote":
        for child in el:
            start = len(out)
            _block(child, out, depth)
            out[start:] = ["> " + line for line in out[start:]]
    elif tag == "embed":
        out.append(f"*[Diagram in the live document: {el.get('caption', '')}]*")
        out.append("")
    elif tag == "gap":
        raise ValueError(f"the read has a gap of {el.get('blocks')} blocks; merge the paged reads first")
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
    out = []
    for child in ET.fromstring(xml):
        _block(child, out)
    text = "\n".join(out).replace("****", "")
    return "\n".join(line.rstrip() for line in text.split("\n"))


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


def write_snapshot(snapshot_text, body, url, revision, date):
    """Keeps the snapshot's header, names url, revision and date in its revision line, and replaces the body."""
    lines = snapshot_text.split("\n")
    header = lines[:HEADER_LINES]
    if len(lines) <= HEADER_LINES or not lines[HEADER_LINES].startswith("# "):
        raise ValueError(f"line {HEADER_LINES + 1} of the snapshot is not the document title")
    new_line, count = REVISION_LINE.subn(f"({url}), revision {revision}, taken {date}.", header[2])
    if count != 1:
        raise ValueError("line 3 of the snapshot holds no revision line to rewrite")
    header[2] = new_line
    return "\n".join(header) + "\n" + body.rstrip("\n") + "\n"


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("read")
    parser.add_argument("snapshot")
    parser.add_argument("--url", required=True)
    parser.add_argument("--revision", required=True, type=int)
    parser.add_argument("--date", required=True)
    args = parser.parse_args(argv)
    data = load_read(args.read)
    if data.get("rev") != args.revision:
        raise ValueError(f"the read is of revision {data.get('rev')}, not {args.revision}")
    with open(args.snapshot, encoding="utf-8") as f:
        current = f.read()
    result = write_snapshot(current, convert(data["xml"]), args.url, args.revision, args.date)
    with open(args.snapshot, "w", encoding="utf-8", newline="\n") as f:
        f.write(result)
    return 0


if __name__ == "__main__":
    sys.exit(main())
