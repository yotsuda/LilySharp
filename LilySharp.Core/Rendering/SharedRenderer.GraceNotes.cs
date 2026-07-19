// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Collections.Immutable;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;

namespace LilySharp.Core.Rendering;

internal static partial class SharedRenderer
{
    // ---------- Grace notes ----------

    /// <summary>
    /// Draws grace-note groups: small noteheads (with optional accidentals)
    /// scaled to GraceNoteLayout.Scale (typically 0.65), placed before the
    /// main note's column.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/grace-engraver.cc:36-80 Grace_engraver
    /// LILYPOND-REF: scm/define-grobs.scm:1721 GraceSpacing grob
    /// </remarks>
    private static void DrawGraceNotes(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc, double pageHeight)
    {
        if (layout.GraceNoteLayouts.IsDefaultOrEmpty) return;
        foreach (var g in layout.GraceNoteLayouts)
        {
            if (!sysY.TryGetValue(g.MeasureIndex, out var sy)) continue; // other page
            double syUp = pageHeight - sy;

            // Tab staff: grace notes are small fret numbers on the string lines,
            // not noteheads. No stems/beam/slur/ledger — tab grace is just the
            // shrunken digit before the main fret.
            if (g.Tuning is { } graceTuning)
            {
                DrawTabGraceNotes(g, syUp, graceTuning, g.TabClef, g.TabTransposition, gc);
                continue;
            }

            // StaffYOffset places the grace over its OWN staff in a multi-staff
            // score (0 for the first staff / single-staff). Y-up: below the system
            // top is smaller Y-up.
            double staffMiddleY = syUp - g.StaffYOffset - StaffHeight / 2;
            // On an ossia staff the whole group shrinks again: head Ys go
            // through the staff-top affine and the grace's own scale compounds
            // with the ossia scale (a grace on a magnified staff is scaled
            // twice in LP too — fontSize composes).
            double eff = os.Size(g.Scale, g.StaffIndex);
            double scaledFontSize = FontSize * eff;
            double currentX = g.X;
            double lastNoteX = g.X, lastNoteY = staffMiddleY;
            int lastGraceStaffPos = 0;
            // Per-head geometry, collected so the stems/beam can be drawn once
            // the whole group's positions are known.
            var headX = new List<double>(g.Notes.Length);
            var headY = new List<double>(g.Notes.Length);
            var beamCounts = new List<int>(g.Notes.Length);
            using (gc.Source(g.SourcePosition))
            {
                foreach (var note in g.Notes)
                {
                    double y = os.YUp(staffMiddleY + note.StaffPosition / 2.0,
                        g.StaffIndex, g.MeasureIndex);
                    // Ledgers under the head — layer 0 with the staff lines.
                    // LILYPOND-REF: scm/define-grobs.scm LedgerLineSpanner (layer . 0)
                    // On an ossia the anchor is the affined middle and the
                    // per-step offsets shrink via `unit`, matching the heads.
                    if (note.NeedsLedger)
                        DrawLedgerLines(note.StaffPosition, currentX,
                            os.YUp(staffMiddleY, g.StaffIndex, g.MeasureIndex), gc,
                            EngravingDefaults.NoteheadBlackWidth * eff,
                            unit: os.Size(1.0, g.StaffIndex));
                    if (note.Accidental is { } acc)
                        DrawAccidental(acc, isCourtesy: false, currentX, y,
                            g.SourcePosition, gc, eff);
                    gc.DrawNotehead(EmmentalerGlyphs.NoteheadBlack, currentX, y, scaledFontSize, null,
                        GlyphMetrics.NoteheadBlackAdvance * eff,
                        GlyphMetrics.NoteheadBlack.Height * eff);
                    headX.Add(currentX);
                    headY.Add(y);
                    beamCounts.Add(BeamCountForDuration(note.BaseDuration.Denominator));
                    lastNoteX = currentX;
                    lastNoteY = y;
                    lastGraceStaffPos = note.StaffPosition;
                    // Advance one grace column = head width + inter-grace space, the
                    // SAME per-note step the layout reserves; the old width-only step
                    // left the heads short of their reserved room and read as cramped.
                    // LILYPOND-REF: GraceNoteEngraver GraceNoteWidth (1.2) +
                    //   GraceNoteSpacing (0.3); lily/grace-spacing-engraver.cc.
                    currentX += (1.2 + 0.3) * eff;
                }

                // Stems (forced UP) plus the connecting beam, or a flag for a lone
                // grace note. Without this the small heads float free of any stem.
                // LILYPOND-REF: scm/music-functions.scm:633-637 score-grace-settings —
                //   ((Voice Stem direction ,UP) (Voice Slur direction ,DOWN)): grace
                //   stems are forced up regardless of pitch, and the auto-slur bows down.
                DrawGraceStemsAndBeam(headX, headY, beamCounts, eff,
                    g.Type == GraceNoteType.Acciaccatura, gc);

                // Grace slur from the last grace notehead to the main notehead.
                // LILYPOND-REF: ly/grace-init.ly startGraceSlur/stopGraceSlur —
                // acciaccatura and appoggiatura are auto-slurred to the main note.
                if (g.Notes.Length > 0 &&
                    g.Type is GraceNoteType.Acciaccatura or GraceNoteType.Appoggiatura)
                {
                    double mainY = os.YUp(
                        staffMiddleY + g.MainNoteStaffPosition / 2.0,
                        g.StaffIndex, g.MeasureIndex);
                    // A main note below the middle line is stem-up (stem on the head's
                    // RIGHT); the slur can then run to the head centre. A stem-down note
                    // keeps its stem on the LEFT, so the slur tucks short of it.
                    bool mainStemUp = g.MainNoteStaffPosition < 0;
                    DrawGraceSlur(lastNoteX, lastNoteY, lastGraceStaffPos,
                        g.MainNoteX, mainY, g.MainNoteStaffPosition, mainStemUp,
                        g.MeasureIndex, eff, gc, pageHeight, staffMiddleY);
                }
            }
        }
    }

    /// <summary>
    /// Draws a grace group on a TAB staff: each grace note becomes a small fret
    /// number on its string line (resolved from the note's MIDI pitch + tuning),
    /// scaled by the grace scale. No stems, beams, slurs, or ledger lines — tab
    /// grace notes are just the shrunken digits ahead of the main fret.
    /// </summary>
    private static void DrawTabGraceNotes(GraceNoteLayout g, double syUp, TuningType tuning,
        ClefType clef, int transposition, IDrawingContext gc)
    {
        double tabTopY = syUp - g.StaffYOffset;
        int[] tuningArray = Tunings.GetTuning(tuning);
        int octaveShift = Tunings.SoundingShift(clef, transposition);
        double stringSpace = EngravingDefaults.TabStringSpace(Tunings.GetStringCount(tuning));
        // Tab grace digits sit only slightly below the main fret size (NOT the
        // 0.65 notehead grace scale): on a tab staff the fret number IS the note,
        // so the size contrast that reads as "grace" in notation would here just
        // make the digit illegibly tiny.
        double fontSize = TabFretFontSize * TabGraceFretScale;
        double currentX = g.X;

        using (gc.Source(g.SourcePosition))
        {
            foreach (var note in g.Notes)
            {
                var (stringNum, fret) = Tunings.CalculateFret(note.Midi + octaveShift, tuningArray, 0);
                double noteY = tabTopY - (stringNum - 1) * stringSpace;
                string fretText = fret.ToString();
                double bgWidth = (fretText.Length == 1 ? 0.625 : 1.0) * fontSize;
                double bgHeight = 0.6875 * fontSize;
                // White background occludes the string line behind the digit. The
                // rect's visual-top edge is above the string line (larger Y-up).
                gc.DrawRectangle(currentX - bgWidth / 2, noteY + bgHeight / 2, bgWidth, bgHeight,
                    fill: Color.White);
                gc.DrawText(fretText, currentX, noteY - fontSize * 0.32, fontSize, "serif",
                    FontStyle.Bold, TextAnchor.Middle, Color.Black);
                currentX += 1.2 * g.Scale;
            }
        }
    }

    /// <summary>Number of beams/flag-hooks for a duration denominator
    /// (8th=1, 16th=2, 32nd=3, …); 0 for quarter and longer.</summary>
    private static int BeamCountForDuration(int denominator)
    {
        int beams = 0;
        for (int d = denominator; d >= 8; d /= 2) beams++;
        return beams;
    }

    /// <summary>
    /// Draws the up-pointing stems for a grace group, then either a connecting
    /// beam (≥2 beamable heads) or a single flag (lone grace note). Everything is
    /// scaled by the grace scale; stems are forced UP per score-grace-settings.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/music-functions.scm:633-637 score-grace-settings — grace
    ///   stems are forced UP (so a stem-up beam stacks its secondary beams toward
    ///   the heads, i.e. downward on the page).
    /// LILYPOND-REF: lily/beam.cc secondary beams translated by beam-thickness +
    ///   gap; here BeamTranslation, scaled.
    /// Simplification: equal-length stems, so the beam runs parallel to the head
    /// contour (LilyPond solves a quanted slope). A grace group mixing
    /// beamed (≥8th) and unbeamed (≤quarter) durations falls back to per-head
    /// flags rather than a partial beam.
    /// </remarks>
    private static void DrawGraceStemsAndBeam(
        List<double> xs, List<double> ys, List<int> beamCounts, double scale,
        bool acciaccatura, IDrawingContext gc)
    {
        int n = xs.Count;
        if (n == 0) return;

        double stemThick = EngravingDefaults.StemThickness * scale;
        double stemLen = EngravingDefaults.DefaultStemLength * scale;
        // Stem-up attaches at the right edge of the (scaled) notehead.
        double upAttach = EngravingDefaults.NoteheadBlackWidth * scale - stemThick / 2;
        double StemX(int i) => xs[i] + upAttach;
        // Stem end: the up-stem runs from the head to stemLen above it — up is larger
        // Y-up, so add in the native page Y-up frame.
        double StemEndY(int i) => ys[i] + stemLen;

        int maxBeams = 0;
        foreach (var b in beamCounts) maxBeams = Math.Max(maxBeams, b);
        bool allBeamable = n > 1 && beamCounts.All(b => b >= 1);

        if (maxBeams == 0 || !allBeamable)
        {
            // Bare stems (fixed length) plus a flag per beamable head. A lone grace,
            // a quarter-or-longer grace, or a non-uniform group takes this path.
            for (int i = 0; i < n; i++)
                gc.DrawLine(StemX(i), ys[i], StemX(i), StemEndY(i), Color.Black, stemThick);
            if (maxBeams == 0) return;
            for (int i = 0; i < n; i++)
            {
                if (beamCounts[i] == 0) continue;
                int denom = 1 << (beamCounts[i] + 2);   // beams→denominator (1→8, 2→16, …)
                var flag = EmmentalerGlyphs.GetFlag(denom, stemUp: true);
                if (flag.HasValue)
                    gc.DrawGlyph(flag.Value, StemX(i), StemEndY(i), FontSize * scale, Color.Black);
                // Acciaccatura: diagonal slash through the stem just below the flag.
                if (acciaccatura)
                    DrawGraceSlash(StemX(i), StemEndY(i), scale, gc);
            }
            return;
        }

        // Beamed group. The beam is ONE straight line at a fixed stem length above
        // the heads; each stem then runs from its head to that SLOPED line at its own
        // x (a fixed per-stem length would leave the inner stems short of the beam and
        // read as jagged). The beam ends extend half a stem past the outer stems so
        // the corner squares off against the vertical stem edge.
        // LILYPOND-REF: lily/beam.cc — stems terminate on the beam; beam.cc:631
        //   horizontal_[dir] += dir * stem_width / 2 (flush end).
        double beamLeftY = StemEndY(0), beamRightY = StemEndY(n - 1);
        double span = StemX(n - 1) - StemX(0);
        double beamSlope = span > 0.001 ? (beamRightY - beamLeftY) / span : 0.0;
        double BeamY(double x) => beamLeftY + beamSlope * (x - StemX(0));

        for (int i = 0; i < n; i++)
            gc.DrawLine(StemX(i), ys[i], StemX(i), BeamY(StemX(i)), Color.Black, stemThick);

        double beamThick = EngravingDefaults.BeamThickness * scale;
        double beamTrans = EngravingDefaults.BeamTranslation * scale;
        double halfStem = stemThick / 2;

        // LILYPOND-REF: lily/lookup.cc Lookup::beam — the beam quad is a parallelogram
        // (sloped by `slope` over `width`, thickness `thick`) whose corners are offset
        // by the blot so the ends stay vertical.
        // A beam is a PARALLELOGRAM with vertical ends (LP Lookup::beam): a plain
        // sloped thick line caps its ends perpendicular to the slope and leaves a
        // triangle poking past the vertical stem. Corners are the left/right ends
        // (extended half a stem) at ±half the (vertical) beam thickness.
        double beamHalf = beamThick / 2;
        void Beam(int a, int b, double off)
        {
            double xL = StemX(a) - halfStem, xR = StemX(b) + halfStem;
            double yL = BeamY(xL) + off, yR = BeamY(xR) + off;
            // Quad corner offsets flip with the Y-up frame so each vertex keeps its
            // original device Y (and emit slot).
            gc.DrawFilledQuad(
                (xL, yL + beamHalf), (xR, yR + beamHalf),
                (xR, yR - beamHalf), (xL, yL - beamHalf), Color.Black);
        }

        Beam(0, n - 1, 0);                    // primary across the whole group
        for (int level = 1; level < maxBeams; level++)
        {
            // Secondaries stack toward the heads (below the up-stem beam), which is
            // the negative direction in the page Y-up frame.
            double off = -(level * beamTrans);
            int i = 0;
            while (i < n - 1)
            {
                if (beamCounts[i] > level && beamCounts[i + 1] > level)
                {
                    int j = i;
                    while (j < n - 1 && beamCounts[j] > level && beamCounts[j + 1] > level) j++;
                    Beam(i, j, off);
                    i = j;
                }
                else i++;
            }
        }
        // Beamed acciaccatura would carry the slash on the beam itself
        // (Beam.stencil = slashed-stencil); not yet ported — only the lone-note
        // flag dash above is. acciaccatura groups are almost always a single note.
    }

    /// <summary>
    /// Draws the acciaccatura slash: a diagonal stroke through the (up) stem just
    /// below the flag, lower-left to upper-right, with the stem top as origin.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: mf/feta-flags.mf:1228-1260 "grace dash (up)" (flags.ugrace) —
    ///   stroke from z1=(-hip_width·0.72, -foot_depth·0.72) to z2=(hip_width, -flare),
    ///   pen = 1.5·stemthickness; with flare = 1 ss, foot_depth = 3 ss,
    ///   hip_width = upflag_width − hip_thickness/2,
    ///   upflag_width = .65·notehead_width + stemthickness/2,
    ///   hip_thickness = linethickness + .069 ss. All scaled by the grace scale.
    /// </remarks>
    private static void DrawGraceSlash(double stemX, double stemTopY, double scale, IDrawingContext gc)
    {
        const double hipDepthRatio = 0.72;
        const double footDepth = 3.0;   // staff spaces
        const double flare = 1.0;       // staff spaces
        double upflagWidth = 0.65 * EngravingDefaults.NoteheadBlackWidth
                           + EngravingDefaults.StemThickness / 2;
        double hipThickness = EngravingDefaults.LineThickness + 0.069;
        double hipWidth = upflagWidth - hipThickness / 2;

        // feta y is up; the page frame is now Y-up too, so a feta y of -k is k
        // staff-spaces below the stem top (= −k in Y-up).
        double x1 = stemX - hipWidth * hipDepthRatio * scale;
        double y1 = stemTopY - footDepth * hipDepthRatio * scale;   // lower-left
        double x2 = stemX + hipWidth * scale;
        double y2 = stemTopY - flare * scale;                       // upper-right
        gc.DrawLine(x1, y1, x2, y2, Color.Black, 1.5 * EngravingDefaults.StemThickness * scale);
    }

    /// <summary>
    /// Draws a small slur arcing below from the last grace note to the main
    /// note (grace stems point up, so the slur bows underneath).
    /// </summary>
    /// <remarks>LILYPOND-REF: ly/grace-init.ly — grace auto-slur.</remarks>
    // Drops of the slur's ends below the noteheads' bottom edges. LilyPond keeps
    // free-head-distance (0.3) from a head; the main-note end drops further because
    // that note is stem-down (its stem sits on the slur's side) and the slur clears
    // it — measured ≈0.65 ss below the head in LP's output. The grace end keeps the
    // plain free-head-distance (its stem points the other way).
    // LILYPOND-REF: scm/layout-slur.scm default-slur-details free-head-distance 0.3,
    //   stem-encompass-penalty 30.
    private const double GraceSlurStartClearance = 0.5;
    private const double GraceSlurEndClearance = 0.65;
    // Tuck the main-note end a little further left of the stem (stem-down mains only).
    private const double GraceSlurEndLeftShift = 0.15;

    private static void DrawGraceSlur(double graceX, double graceY, int graceStaffPos,
        double mainX, double mainY, int mainStaffPos, bool mainStemUp,
        int measureIndex, double scale, IDrawingContext gc, double pageHeight,
        double staffMiddleY)
    {
        // The slur scorer reasons in device coordinates (its result is layout Y-up =
        // -device); convert the Y-up head anchors to device, solve, then flip the
        // final curve back to page Y-up for the flipping context.
        graceY = pageHeight - graceY;
        mainY = pageHeight - mainY;
        // staffMiddleY arrives page-Y-up (same as the head anchors did); the scorer
        // needs it in the device frame so its staff-line avoidance lands correctly.
        double staffMiddleYDevice = pageHeight - staffMiddleY;
        double startX = graceX + GlyphMetrics.NoteheadBlack.CenterX * scale;
        double startY = graceY + 0.5 + GraceSlurStartClearance;
        // A stem-DOWN main note carries its stem on the head's LEFT, so end the slur
        // short of it (tuck beside the stem). A stem-UP main note has a clear left side,
        // so run the slur out to the head CENTRE. Dropped below the head either way so
        // the bow does not hug the notehead.
        double endX = mainStemUp
            ? mainX + GlyphMetrics.NoteheadBlack.CenterX
            : mainX - GraceSlurEndLeftShift;
        double endY = mainY + 0.5 + GraceSlurEndClearance;

        if (endX - startX < 0.5) return; // degenerate

        // Optimise the endpoints through the SAME slur scorer the regular slurs use
        // (LilyPond's Slur_score): enumerate attachment Ys around the base and pick the
        // configuration that best encompasses the heads and flattens the slope. This is
        // what pulls the grace-side start down when the main note is far below it,
        // instead of a fixed clearance. The heads are fed as obstacles to encompass.
        // LILYPOND-REF: lily/slur-scoring.cc:436 Slur_score_state::get_best_curve.
        var slurItem = new SlurItem(graceStaffPos, mainStaffPos, curveUp: false,
            measureIndex, measureIndex, startItemIndex: 0, endItemIndex: 0);
        double graceHalf = 0.5 * scale, mainHalf = 0.5;
        var obstacles = new List<SlurObstacle>
        {
            new(startX, graceY - graceHalf, graceY + graceHalf, SlurObstacleType.NoteHead),
            new(endX, mainY - mainHalf, mainY + mainHalf, SlurObstacleType.NoteHead),
        };
        var solved = new SlurScoringProblem(
            slurItem, startX, startY, endX, endY, staffMiddleYDevice,
            obstacles: obstacles).Solve();

        // For a short grace→main span (a beamed grace run sits right against the main
        // note) the scorer's free-head inset can drop every candidate, collapsing the
        // width; keep the base attachments then so the bow stays visible.
        bool degenerate = solved.EndX - solved.StartX < 1.0;
        double sx = degenerate ? startX : solved.StartX;
        // solved's Y is page Y-up (= -device); reflect back to the device startY/endY.
        double sy = degenerate ? startY : -solved.StartYUp;
        double ex = degenerate ? endX : solved.EndX;
        double ey = degenerate ? endY : -solved.EndYUp;

        // Draw the optimised endpoints with LilyPond's slur_shape base curve: both
        // inner control points sit `height` off the chord PERPENDICULAR, indented
        // `indent` along it — a rounder, symmetric arc. Slur grob defaults height-limit
        // 2.0, ratio 0.25. LILYPOND-REF: lily/bezier-bow.cc slur_height /
        //   get_slur_indent_height (height = F0_1(width*r0/h_inf)*h_inf,
        //   F0_1(x)=2/pi*atan(pi*x/2); indent = 2*h_inf - q^2/3.1/(width+q),
        //   q = 2*h_inf*3.1, cap width/3.1); slur-configuration.cc generate_curve.
        const double hInf = 2.0, r0 = 0.25, maxFraction = 1.0 / 3.1;
        double dx = ex - sx, dyc = ey - sy;
        double len = Math.Sqrt(dx * dx + dyc * dyc);
        double height = 2.0 / Math.PI * Math.Atan(Math.PI * (len * r0 / hInf) / 2.0) * hInf;
        double q = 2.0 * hInf / maxFraction;
        double indent = Math.Min(2.0 * hInf - q * q * maxFraction / (len + q), len * maxFraction);
        double ux = dx / len, uy = dyc / len;   // chord unit
        double perpX = -uy, perpY = ux;         // perpendicular, +Y (down) for a bow below
        var c1 = (X: sx + perpX * height + ux * indent, Y: sy + perpY * height + uy * indent);
        var c2 = (X: ex + perpX * height - ux * indent, Y: ey + perpY * height - uy * indent);

        // Flip the device-space curve back to page Y-up for the flipping context.
        DrawCurve(sx, pageHeight - sy, ex, pageHeight - ey,
            (c1.X, pageHeight - c1.Y), (c2.X, pageHeight - c2.Y),
            curveUp: false, EngravingDefaults.SlurMidThickness * scale, gc);
    }

}
