#!/usr/bin/env python3
"""Ground-truth spacing comparison: Lily# vs real LilyPond.

For each case in audit/lilypond-ref/cases/<name>/ (case.lys + case.ly +
case.json), renders BOTH engines to PNG, extracts notehead X centers from a
chosen staff by pixel analysis, and compares the NORMALIZED GAP SEQUENCE
(each gap divided by the first gap — scale-invariant, so resolution and
margins don't matter). A case passes when every normalized gap agrees within
the case's tolerance.

This is the arbiter for layout reports: a deviation from LilyPond is a bug;
a match means the (possibly surprising-looking) spacing is correct engraving.

Usage:
    py -3 audit/scripts/Compare-WithLilypond.py [--case NAME] [--keep]

Requirements:
    - pillow + numpy (py -3 -m pip install pillow numpy)
    - LilyPond binary: env LILYPOND_EXE, else C:/bin/lilypond-2.24.4/bin/lilypond.exe
    - dotnet (renders via LilySharp.Cli)

Limitations (v1):
    - Notehead detection keys on FILLED heads (longest vertical dark run);
      compare staves whose measured notes are quarters or shorter.
    - The expected head count must match what the detector finds after
      dropping clef/time-signature blobs (it keeps the LAST `heads` blobs).

case.json:
    { "description": str, "staff": 0-based-from-top, "heads": int,
      "tolerance": float }
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

import numpy as np
from PIL import Image

REPO = Path(__file__).resolve().parents[2]
CASES_DIR = REPO / "audit" / "lilypond-ref" / "cases"
LILYPOND = Path(os.environ.get("LILYPOND_EXE", r"C:\bin\lilypond-2.24.4\bin\lilypond.exe"))


def run(cmd: list[str], cwd: Path) -> None:
    result = subprocess.run(cmd, cwd=cwd, capture_output=True, text=True,
                            encoding="utf-8", errors="replace")
    if result.returncode != 0:
        raise RuntimeError(f"command failed: {' '.join(map(str, cmd))}\n{result.stdout}\n{result.stderr}")


def render_lilysharp(lys: Path, out_png: Path) -> None:
    run(["dotnet", "run", "--project", str(REPO / "LilySharp.Cli"), "--",
         "png", str(lys), str(out_png), "--scale", "4"], cwd=REPO)


def render_lilypond(ly: Path, out_png: Path) -> None:
    # lilypond's Guile init DEADLOCKS when it inherits certain consoles
    # (observed: spawned under a PowerShell.MCP console, directly or via
    # python — ~5s of CPU, then a kernel wait forever, before the version
    # banner). The proven-safe launch is detached through cmd.exe with
    # stdin from NUL and no inherited console; same recipe regardless of
    # who invokes this script.
    if os.name == "nt":
        log = out_png.with_suffix(".lilypond.log")
        cmdline = (
            f'"{LILYPOND}" --png -dresolution=160 '
            f'-o "{out_png.with_suffix("")}" "{ly}" '
            f'< NUL > "{log}" 2>&1'
        )
        # Pass ONE string so Python's list2cmdline cannot mangle the inner
        # quotes into \" (cmd does not understand backslash escapes); /s
        # makes cmd strip exactly the outer quote pair.
        result = subprocess.run(
            f'cmd.exe /d /s /c "{cmdline}"', cwd=out_png.parent,
            stdin=subprocess.DEVNULL,
            creationflags=subprocess.CREATE_NO_WINDOW)
        if result.returncode != 0:
            tail = log.read_text(encoding="utf-8", errors="replace")[-2000:] if log.exists() else "(no log)"
            print(f"FAILED: lilypond {ly}", file=sys.stderr)
            print(tail, file=sys.stderr)
            raise SystemExit(1)
        return
    run([str(LILYPOND), "--png", "-dresolution=160",
         "-o", str(out_png.with_suffix("")), str(ly)], cwd=out_png.parent)


def content_region(dark: np.ndarray) -> np.ndarray:
    """Masks out footer/header text: keeps only the row range that contains
    long horizontal runs (staff lines); text rows have only short runs."""
    h, w = dark.shape
    keep = np.zeros(h, bool)
    for y in range(h):
        row = dark[y]
        best = cur = 0
        for v in row:
            cur = cur + 1 if v else 0
            if cur > best:
                best = cur
        if best >= 120:  # staff lines are long continuous runs; text is not
            keep[y] = True
    idx = np.where(keep)[0]
    masked = np.zeros_like(dark)
    if len(idx) == 0:
        return masked
    lo = max(0, idx.min() - 60)
    hi = min(h, idx.max() + 60)
    masked[lo:hi] = dark[lo:hi]
    return masked


def find_staves(dark: np.ndarray) -> list[tuple[int, int, float]]:
    """Returns (top_row, bottom_row, line_spacing) per staff, top to bottom."""
    h, w = dark.shape
    ys, xs = np.where(dark)
    if len(xs) == 0:
        raise RuntimeError("blank image")
    x0, x1 = xs.min(), xs.max()
    span = max(1, x1 - x0)
    rowsum = dark[:, x0:x1].sum(axis=1)
    line_rows = [y for y in range(h) if rowsum[y] > 0.6 * span]

    # Cluster adjacent rows into physical lines.
    lines: list[int] = []
    i = 0
    while i < len(line_rows):
        j = i
        while j + 1 < len(line_rows) and line_rows[j + 1] - line_rows[j] <= 2:
            j += 1
        lines.append((line_rows[i] + line_rows[j]) // 2)
        i = j + 1

    if len(lines) % 5 != 0:
        raise RuntimeError(f"expected a multiple of 5 staff lines, found {len(lines)}: {lines}")

    staves = []
    for s in range(len(lines) // 5):
        group = lines[s * 5:(s + 1) * 5]
        spacing = (group[-1] - group[0]) / 4.0
        staves.append((group[0], group[-1], spacing))
    return staves


def find_head_centers(dark: np.ndarray, staff: tuple[int, int, float]) -> list[float]:
    """Filled-notehead X centers (mass centroids) within a band around the staff."""
    top, bottom, spacing = staff
    band_top = max(0, int(top - 1.6 * spacing))
    band_bottom = min(dark.shape[0], int(bottom + 1.6 * spacing))
    band = dark[band_top:band_bottom, :].copy()

    # Longest vertical dark run per column on the RAW band. Each element has
    # a distinctive run height: staff lines ~0.13sp, beams ~0.5-0.7sp,
    # filled heads ~1.0-1.3sp, stems ~3.5sp, barlines 4sp+.
    w = band.shape[1]
    longest = np.zeros(w, int)
    for x in range(w):
        col = band[:, x]
        best = cur = 0
        for v in col:
            cur = cur + 1 if v else 0
            best = max(best, cur)
        longest[x] = best

    # Mask stem/barline columns first (also splits heads joined by a stem),
    # then select head columns by the run-height window. The 0.80 lower
    # bound keeps beams and staff lines out.
    band[:, longest >= 2.4 * spacing] = False
    is_head = (longest >= 0.80 * spacing) & (longest < 2.4 * spacing)
    centers: list[float] = []
    x = 0
    while x < w:
        if is_head[x]:
            x2 = x
            while x2 < w and is_head[x2]:
                x2 += 1
            if x2 - x >= 0.5 * spacing:
                # Mass centroid is steadier than the x-extent midpoint: the
                # stem adds a thin tail on one side that barely shifts mass.
                blob = band[:, x:x2]
                xs = np.where(blob.any(axis=0))[0]
                weights = blob.sum(axis=0)[xs]
                centers.append(x + float((xs * weights).sum() / weights.sum()))
            x = x2
        x += 1
    return centers


def normalized_gaps(centers: list[int]) -> list[float]:
    gaps = [b - a for a, b in zip(centers, centers[1:])]
    return [g / gaps[0] for g in gaps]


def measure(png: Path, staff_index: int, heads: int) -> list[float]:
    img = np.array(Image.open(png).convert("L"))
    dark = content_region(img < 100)
    staves = find_staves(dark)
    if staff_index >= len(staves):
        raise RuntimeError(f"{png.name}: staff {staff_index} not found ({len(staves)} staves)")
    centers = find_head_centers(dark, staves[staff_index])
    if len(centers) < heads:
        raise RuntimeError(f"{png.name}: found {len(centers)} head blobs, need {heads}: {centers}")
    return normalized_gaps(centers[-heads:])


def run_case(case_dir: Path, keep: bool) -> bool:
    cfg = json.loads((case_dir / "case.json").read_text(encoding="utf-8"))
    staff, heads, tol = cfg["staff"], cfg["heads"], cfg["tolerance"]

    work = Path(tempfile.mkdtemp(prefix=f"lyref-{case_dir.name}-"))
    try:
        ours_png = work / "ours.png"
        ref_png = work / "ref.png"
        render_lilysharp(case_dir / "case.lys", ours_png)
        render_lilypond(case_dir / "case.ly", ref_png)

        ours = measure(ours_png, staff, heads)
        ref = measure(ref_png, staff, heads)

        deltas = [abs(a - b) for a, b in zip(ours, ref)]
        ok = len(ours) == len(ref) and all(d <= tol for d in deltas)

        known = cfg.get("knownDivergence")
        if ok:
            verdict = "PASS"
        elif known:
            verdict, ok = "XFAIL", True  # tracked divergence, not a regression
        else:
            verdict = "FAIL"
        print(f"[{verdict}] {case_dir.name}: {cfg.get('description', '')}")
        print(f"        lilypond gaps : {['%.3f' % g for g in ref]}")
        print(f"        lily#    gaps : {['%.3f' % g for g in ours]}")
        print(f"        max delta {max(deltas):.3f} (tolerance {tol})")
        if known and verdict == "XFAIL":
            print(f"        known divergence: {known}")
        return ok
    finally:
        if keep:
            print(f"        artifacts kept in {work}")
        else:
            shutil.rmtree(work, ignore_errors=True)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--case", help="run a single case by directory name")
    parser.add_argument("--keep", action="store_true", help="keep rendered PNGs for inspection")
    args = parser.parse_args()

    if not LILYPOND.exists():
        print(f"lilypond not found at {LILYPOND}; set LILYPOND_EXE", file=sys.stderr)
        return 2

    cases = sorted(d for d in CASES_DIR.iterdir() if (d / "case.json").exists())
    if args.case:
        cases = [c for c in cases if c.name == args.case]
        if not cases:
            print(f"no such case: {args.case}", file=sys.stderr)
            return 2

    failures = 0
    for case in cases:
        try:
            if not run_case(case, args.keep):
                failures += 1
        except Exception as exc:
            failures += 1
            print(f"[ERROR] {case.name}: {exc}")

    print(f"\n{len(cases) - failures}/{len(cases)} cases match LilyPond")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
