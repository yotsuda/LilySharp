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
    /// scaled to GraceNoteLayout.Scale (GraceNoteItem.ScaleFactor), placed before the
    /// main note's column.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/grace-engraver.cc:36-80 Grace_engraver
    /// LILYPOND-REF: scm/define-grobs.scm:1721 GraceSpacing grob
    /// </remarks>
    private static void DrawGraceNotes(ScoreLayout layout, Dictionary<int, double> sysTopYUp,
        in OssiaShrink os, IDrawingContext gc, double pageHeight)
    {
        if (layout.GraceNoteLayouts.IsDefaultOrEmpty) return;
        foreach (var g in layout.GraceNoteLayouts)
        {
            if (!sysTopYUp.TryGetValue(g.MeasureIndex, out var syUp)) continue; // other page

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
            // The run's column offsets come from the LAYOUT — the same chain the reservation
            // and the beam quanter read (SpacingRules.GraceColumns). The renderer places, it
            // does not decide: it used to step by its own literal (1.2 + 0.3) * eff, which
            // was neither of the two widths the layout had reserved. The offsets are in
            // grace-scaled staff spaces already, so only the OSSIA factor is applied here.
            double unit = os.Size(1.0, g.StaffIndex);
            // THE FONT this grace's glyphs come out of — the design its font-size selected,
            // magnified again by the ossia the staff carries (fontSize composes in LilyPond
            // too). Everything drawn below reads a dimension out of this and multiplies
            // nothing: it is the same table the LAYOUT reserved with
            // (SpacingRules.GraceColumns via GraceNoteItem.Font), so the box and the ink
            // cannot disagree.
            var graceFont = unit == 1.0
                ? GraceNoteItem.Font
                : GraceNoteItem.Font.Scaled(unit);
            // The ACCIDENTAL's font is a different one — font-size −4, not the head's −3
            // (GraceNoteItem.AccidentalFontSizeStep) — carried through the same ossia factor.
            var graceAccFont = unit == 1.0
                ? GraceNoteItem.AccidentalFont
                : GraceNoteItem.AccidentalFont.Scaled(unit);
            var colX = g.ColumnOffsets;
            double currentX = g.X;
            double lastNoteX = g.X, lastNoteY = staffMiddleY;
            int headIndex = 0;
            int lastGraceStaffPos = 0;
            // Per-head geometry, collected so the stems/beam can be drawn once
            // the whole group's positions are known.
            var headX = new List<double>(g.Notes.Length);
            var headY = new List<double>(g.Notes.Length);
            var beamCounts = new List<int>(g.Notes.Length);
            // Music glyphs from HERE come out of the grace's own design, not the score's:
            // Emmentaler is optically sized, so a grace head is the 14 design's outline at
            // magstep(-3) and not the 20's drawn small (IDrawingContext.MusicFace). The scope
            // covers the same grobs `graceFont` measures.
            using (gc.Source(g.SourcePosition))
            using (gc.MusicFace(GraceNoteItem.DesignSize))
            {
                foreach (var note in g.Notes)
                {
                    if (!colX.IsDefault && headIndex < colX.Length)
                        currentX = g.X + colX[headIndex] * unit;
                    double y = os.YUp(staffMiddleY + note.StaffPosition / 2.0,
                        g.StaffIndex, g.MeasureIndex);
                    // Ledgers under the head — layer 0 with the staff lines.
                    // LILYPOND-REF: scm/define-grobs.scm LedgerLineSpanner (layer . 0)
                    // On an ossia the anchor is the affined middle and the
                    // per-step offsets shrink via `unit`, matching the heads.
                    if (note.NeedsLedger)
                        DrawLedgerLines(note.StaffPosition, currentX,
                            os.YUp(staffMiddleY, g.StaffIndex, g.MeasureIndex), gc,
                            graceFont.NoteheadBlackAdvance,
                            unit: os.Size(1.0, g.StaffIndex));
                    // Same single-ape skyline path as full notes (draw = reserve): a grace
                    // natural clears its head by its real right skyline, not the fixed gap.
                    // ⚠️ NOT THE GRACE'S OWN SIZE: general-grace-settings gives the Accidental
                    // font-size −4 while the head above is −3, so this glyph comes out of the
                    // THIRTEEN design (GraceNoteItem.AccidentalFont / .AccidentalDesignSize) —
                    // measured, see GraceNoteItem.AccidentalFontSizeStep. Metric, outline
                    // skyline and face all three come from that one font; splitting them is
                    // what this island exists to remove.
                    if (note.Accidental is { } acc
                        && AccidentalColumn.CalculateSinglePosition(
                            note.StaffPosition, acc, isCourtesy: false,
                            graceAccFont, graceFont) is { } al)
                    {
                        using (gc.MusicFace(GraceNoteItem.AccidentalDesignSize))
                            DrawAccidentalAtInkLeft(acc, isCourtesy: false, currentX + al.XOffset, y,
                                g.SourcePosition, gc, graceAccFont.Magnification);
                    }
                    gc.DrawNotehead(EmmentalerGlyphs.NoteheadBlack, currentX, y, scaledFontSize, null,
                        graceFont.NoteheadBlackAdvance,
                        graceFont.NoteheadBlack.Height);
                    headX.Add(currentX);
                    headY.Add(y);
                    beamCounts.Add(BeamCountForDuration(note.BaseDuration.Denominator));
                    lastNoteX = currentX;
                    lastNoteY = y;
                    lastGraceStaffPos = note.StaffPosition;
                    headIndex++;
                }

                // Stems (forced UP) plus the connecting beam, or a flag for a lone
                // grace note. Without this the small heads float free of any stem.
                // LILYPOND-REF: scm/music-functions.scm:633-637 score-grace-settings —
                //   ((Voice Stem direction ,UP) (Voice Slur direction ,DOWN)): grace
                //   stems are forced up regardless of pitch, and the auto-slur bows down.
                // The beam's own height comes from the QUANTER, in the layout stage
                // (GraceNoteEngraver.QuantGraceBeam) — the renderer places it, it does not
                // decide it. The pair is in staff positions at the beam's OUTER STEMS, which
                // is where BeamScoringProblem.Solve answers (its last step is AtOuterStems);
                // they land in this frame through the staff middle and the ossia affine.
                // ⚠️ This comment said "at the beam's drawn ends" until 2026-08-01, and the
                //   renderer below believed it — that misreading was half the grace beam's
                //   residual (ledger beam.quant.grace.left).
                (double, double)? beamEnds =
                    g.BeamLeftY is { } bl && g.BeamRightY is { } br
                        ? (os.YUp(staffMiddleY + bl / 2.0, g.StaffIndex, g.MeasureIndex),
                           os.YUp(staffMiddleY + br / 2.0, g.StaffIndex, g.MeasureIndex))
                        : null;
                DrawGraceStemsAndBeam(headX, headY, beamCounts, eff, graceFont,
                    g.Type == GraceNoteType.Acciaccatura, beamEnds, gc);

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
        // Tab grace digits sit only slightly below the main fret size (NOT the notehead
        // grace scale, GraceNoteItem.ScaleFactor): on a tab staff the fret number IS the
        // note, so the size contrast that reads as "grace" in notation would here just
        // make the digit illegibly tiny.
        double fontSize = TabFretFontSize * TabGraceFretScale;
        // The columns are the ones the layout reserved (SpacingRules.GraceColumns), the same
        // as for a notation grace. ⚠️ LILYSHARP-OWN, and knowingly so: LilyPond's TabStaff
        // draws no stem and no beam, so its grace run has no Beam grob and this geometry has
        // no twin to be measured against (HANDOFF §1 ④ — a tab beam reads -76.5 through that
        // path). What is NOT own is the step: reading the reserved columns rather than a
        // literal 1.2 is what keeps the drawn digits inside the room the spacing gave them.
        using (gc.Source(g.SourcePosition))
        {
            foreach (var d in TabGraceDigits(g, tuning, clef, transposition))
            {
                double noteY = tabTopY - (d.StringNum - 1) * stringSpace;
                // No occluding box: the string line is broken around this digit instead, and
                // the bite was booked by the tab staff pass (SharedRenderer.Tab —
                // TabGraceDigits is the shared producer, so the hole and the glyph cannot
                // disagree about where the digit is).
                gc.DrawText(d.Text, d.CenterX,
                    noteY - LilySharp.Core.Svg.Layout.TabConstants.FretBaselineDrop(d.Text, fontSize),
                    fontSize, "serif",
                    FontStyle.Bold, TextAnchor.Middle, Color.Black);
            }
        }
    }

    /// <summary>One tab grace digit: which string it sits on, its text, and its drawn span.</summary>
    internal readonly record struct TabGraceDigit(
        int StringNum, string Text, double CenterX, double Width);

    /// <summary>
    /// The fret digits a tab grace run draws — the ONE producer, read both by the renderer
    /// that draws them and by the tab staff pass that has to break its string lines around
    /// them.
    /// </summary>
    /// <remarks>
    /// The two used to be impossible to disagree because there was only one of them (the
    /// digit painted its own opaque box). With the box replaced by a hole in the line, the
    /// hole is computed in a different pass from the glyph — so the geometry has to come from
    /// a single place or the two drift, which is the defect this repository keeps re-finding
    /// under other names.
    /// </remarks>
    internal static IEnumerable<TabGraceDigit> TabGraceDigits(
        GraceNoteLayout g, TuningType tuning, ClefType clef, int transposition)
    {
        int[] tuningArray = Tunings.GetTuning(tuning);
        int octaveShift = Tunings.SoundingShift(clef, transposition);
        double fontSize = TabFretFontSize * TabGraceFretScale;
        var colX = g.ColumnOffsets;
        double currentX = g.X;
        int headIndex = 0;
        foreach (var note in g.Notes)
        {
            if (!colX.IsDefault && headIndex < colX.Length)
                currentX = g.X + colX[headIndex];
            headIndex++;
            var (stringNum, fret) = Tunings.CalculateFret(note.Midi + octaveShift, tuningArray, 0);
            string fretText = fret.ToString();
            yield return new TabGraceDigit(
                stringNum, fretText, currentX,
                LilySharp.Core.Svg.Layout.TabConstants.FretGlyphWidth(fretText, fontSize));
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
    /// A grace group mixing beamed (≥8th) and unbeamed (≤quarter) durations falls back
    /// to per-head flags rather than a partial beam.
    /// </remarks>
    /// <param name="beamEnds">
    /// The quanted beam's Y at its two OUTER STEMS, from GraceNoteEngraver.QuantGraceBeam —
    /// which is where BeamScoringProblem.Solve answers, its last step being AtOuterStems.
    /// ⚠️ NOT at the beam's drawn ends: those sit half a stem thickness further out, and
    /// reading these two numbers there draws a flatter line than the one that was scored.
    /// ⚠️ Not optional in practice either: the fallback is the pre-2026-08-01 device —
    /// equal-length stems with the beam parallel to the head contour — which is off
    /// LilyPond's quant grid entirely. It survives only for a group the layout could not
    /// quant (a test constructing a GraceNoteLayout by hand), and it now shares this frame,
    /// its stem ends being at the stems by construction.
    /// </param>
    /// <param name="headFont">
    /// The font the HEADS came out of — an up stem attaches at that font's head advance, so
    /// the stem stands where the drawn head ends and not where a scaled 20 would have.
    /// </param>
    private static void DrawGraceStemsAndBeam(
        List<double> xs, List<double> ys, List<int> beamCounts, double scale,
        GlyphMetrics.DesignMetrics headFont,
        bool acciaccatura, (double Left, double Right)? beamEnds, IDrawingContext gc)
    {
        int n = xs.Count;
        if (n == 0) return;

        // A grace stem is drawn exactly as wide as a full-size one.
        // LILYPOND-REF: lily/stem.cc:909-913 Stem::thickness = thickness x line_thickness.
        //   The property is declared 1.3 at scm/define-grobs.scm:3469, so a
        //   stem is 0.13 staff spaces wide — and a fontSize does not reach LINE thickness, so
        //   the grace scaling never touches it.
        // ⚠️ MEASURED, not read (audit/lp-geometry/probes/grace-stem-frame.ly): thickness 1.3
        //   and a drawn stem extent 0.130000 wide in BOTH the grace book and the full-size
        //   control, with the stem standing 0.065 left of its notehead's right edge in both.
        //   Ledger grace.stem.thickness (and its full-size control, exact from the first run).
        double stemThick = EngravingDefaults.StemThickness;
        double stemLen = EngravingDefaults.DefaultStemLength * scale;
        // THE one house for where a stem stands, and the whole point of using it here: it is
        // the expression the beam was SCORED in (BeamScoringProblem's StemXOf reads the same
        // LayoutUtilities.StemX from the same head font) and the one the collision collector
        // reads. Spelling it a second time here is what let the two frames drift — the second
        // spelling pulled the stem back by half a SCALED thickness while the quanter used the
        // unscaled one, and nothing observed either.
        double StemX(int i) => LayoutUtilities.StemX(xs[i], up: true, headFont);
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
        // The quanter's frame: BeamScoringProblem.Solve ends in AtOuterStems, so what comes
        // back is the scored line's Y AT THE OUTER STEMS — not at the beam's drawn ends,
        // which sit half a stem thickness further out (lily/beam.cc:631). Anchoring the line
        // at the stems is therefore the whole of it: evaluated back out at the drawn ends it
        // reproduces the configuration the scorer chose, and evaluated at any inner stem it
        // gives that stem its length. Reading these two numbers at the EDGES instead drew the
        // configuration flattened by 0.13 / x_span, which is where +-0.014991541 of the
        // grace beam's residual came from (ledger beam.quant.grace.left).
        double edgeL = StemX(0), edgeR = StemX(n - 1);
        double beamLeftY = beamEnds?.Left ?? StemEndY(0);
        double beamRightY = beamEnds?.Right ?? StemEndY(n - 1);
        double span = edgeR - edgeL;
        double beamSlope = span > 0.001 ? (beamRightY - beamLeftY) / span : 0.0;
        double BeamY(double x) => beamLeftY + beamSlope * (x - edgeL);

        for (int i = 0; i < n; i++)
            gc.DrawLine(StemX(i), ys[i], StemX(i), BeamY(StemX(i)), Color.Black, stemThick);

        // A grace beam's thickness is DECLARED, not scaled: ly/grace-init.ly sets
        // Voice.Beam.beam-thickness = #0.384 where scm/define-grobs.scm declares 0.48, and the
        // gap between its lines follows from that (lily/beam.cc:130-145). Both are the numbers
        // the QUANTER was handed (GraceNoteEngraver passes the same two constants), so drawing
        // from anything else draws a beam that is not the configuration that was scored.
        // ⚠️ It used to be BeamThickness × scale with scale = magstep(-3) = 0.7071 — 0.339411
        // thick, 0.572757 apart, against LilyPond's 0.384 and 0.648. The grace's HEADS scale by
        // magstep(-3); its beam does not, and the two are different numbers (0.7071 vs 0.8).
        // MEASURED on 2.26.0 (audit/lp-geometry/probes/beam-grace.ly): the grace Beam grob's
        // drawn height is 1.390 = 0.384 + 0.648 + its own dy 0.358, against 1.480 = 0.48 +
        // 0.81 + 0.19 for the full-size control. Ledger grace.beam.thickness / .stack-gap.
        double beamThick = EngravingDefaults.GraceBeamThickness;
        double beamTrans = EngravingDefaults.BeamTranslationOf(
            EngravingDefaults.GraceBeamThickness,
            EngravingDefaults.GraceBeamLengthFraction,
            maxBeams);
        // The beam's ends reach half a stem thickness past its outer stems — the SAME
        // unscaled 0.065 the quanter measured its x_span_ with, so the corner lands exactly
        // on the scored configuration and squares off against the stem's own edge.
        // LILYPOND-REF: lily/beam.cc:631 horizontal_[d] += d * stem_width / 2.
        // Ledger grace.beam.overhang.left / .right.
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
            EngravingDefaults.SlurMidThickness * scale, gc);
    }

}
