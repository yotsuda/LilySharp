#!/usr/bin/env python3
"""Compress each bundled Emmentaler design to WOFF2.

Reads:  LilySharp.Core/Fonts/emmentaler-{11,13,14,16,18,20,23,26}.otf
Writes: LilySharp.Core/Fonts/emmentaler-<design>.woff2 (only when the bytes change)

WHY THERE HAS TO BE ONE PER DESIGN. Emmentaler is optically sized, so a grob at a smaller
font size is drawn from a different FILE, not from this one scaled
(lily/font-select.cc:41-70 best_rounded_design_size). An SVG embeds the face it draws with,
so the drawing side needs the same eight designs the metrics side reads — otherwise the box
a column reserves stops being the box the glyph fills.

WOFF2 rather than the .otf itself: an embedded face is base64 in every SVG, and the .otf is
about twice the size (103KB against 52KB for the 20). Only the designs a score actually uses
are embedded, so a score without graces or ossias carries exactly what it carries today.

Idempotent: a file whose bytes already match is left alone, so re-running does not churn the
tree. Run after the bundled .otf files are updated.
"""
from __future__ import annotations

import sys
from pathlib import Path

try:
    from fontTools.ttLib import TTFont
except ImportError:
    sys.stderr.write("fontTools not installed. Run: pip install fonttools brotli\n")
    sys.exit(2)

# Kept in step with Extract-EmmentalerMetrics.py's DESIGNS — the same eight files.
DESIGNS: list[int] = [11, 13, 14, 16, 18, 20, 23, 26]

# ⚠️ The 20 is NOT regenerated. Its .woff2 has been bundled since long before this script and
# is what every embedded SVG carries today; re-encoding it here produces the same glyphs in
# different bytes (52200 against the shipped 52116), which would change every embedded SVG for
# no reason at all. Pass --all to rebuild it deliberately, e.g. after updating the .otf.
SHIPPED: int = 20


def main() -> int:
    fonts = Path(__file__).resolve().parents[2] / "LilySharp.Core" / "Fonts"
    rebuild_shipped = "--all" in sys.argv[1:]
    written = 0
    skipped = 0
    for design in DESIGNS:
        if design == SHIPPED and not rebuild_shipped:
            print(f"emmentaler-{design}.woff2 left as shipped (pass --all to rebuild)")
            skipped += 1
            continue
        otf = fonts / f"emmentaler-{design}.otf"
        woff2 = fonts / f"emmentaler-{design}.woff2"
        if not otf.exists():
            sys.stderr.write(f"ERROR: {otf} is missing\n")
            return 1
        # ⚠️ recalcTimestamp=False or the output is a DIFFERENT FILE on every run: fontTools
        # stamps head.modified with the current time by default, so two runs an hour apart
        # produced 52916 and 52788 bytes from the same .otf. A font asset that churns on every
        # re-run cannot be checked into a tree, and the compression difference hides any real
        # font change in the noise.
        font = TTFont(str(otf), recalcTimestamp=False)
        font.flavor = "woff2"
        tmp = woff2.with_suffix(".woff2.tmp")
        font.save(str(tmp))
        new = tmp.read_bytes()
        if woff2.exists() and woff2.read_bytes() == new:
            tmp.unlink()
            print(f"emmentaler-{design}.woff2 unchanged ({len(new)} bytes)")
            continue
        tmp.replace(woff2)
        written += 1
        print(f"emmentaler-{design}.woff2 written ({len(new)} bytes)")
    print(f"{written} file(s) written, {len(DESIGNS) - written - skipped} unchanged,"
          f" {skipped} left as shipped")
    return 0


if __name__ == "__main__":
    sys.exit(main())
