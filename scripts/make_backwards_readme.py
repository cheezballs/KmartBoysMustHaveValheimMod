#!/usr/bin/env python3
"""Builds thunderstore/README.md by writing every line of
docs/thunderstore-readme.forward.md right to left.

Markdown line prefixes (#, -, numbered lists) stay at the start so
headings and bullets still render; table rows are reversed whole.

Usage (from the repo root):  python scripts/make_backwards_readme.py
"""
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parent.parent
SRC = ROOT / "docs" / "thunderstore-readme.forward.md"
DST = ROOT / "thunderstore" / "README.md"

PREFIX = re.compile(r"^(#{1,6} |- |\d+\. )?(.*)$")


def reverse_line(line: str) -> str:
    if line.startswith("|"):
        return line[::-1]
    m = PREFIX.match(line)
    prefix, body = m.group(1) or "", m.group(2)
    return prefix + body[::-1]


def main() -> None:
    lines = SRC.read_text(encoding="utf-8").splitlines()
    DST.write_text("\n".join(reverse_line(l) for l in lines) + "\n", encoding="utf-8")
    print(f"Wrote {DST.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
