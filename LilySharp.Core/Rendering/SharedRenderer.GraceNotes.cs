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
            // The run's column offsets come from the LAYOUT — the same chain the reservation
            // and the beam quanter read (SpacingRules.GraceColumns). The renderer places, it
            // does not decide: it used to step by its own literal (1.2 + 0.3) * eff, which
            // was neither of the two widths the layout had reserved. The offsets are in
            // grace-scaled staff spaces already, so only the OSSIA factor is applied here.
            // ⚠️ THE ORDINARY PASS DOES NOT APPLY IT, because it does not have to: an ossia
            // staff draws inside a group transform that scales the glyphs and leaves X on the
            // shared spacing columns (SharedRenderer.DrawSystem's UnscaledXDrawingContext),
            // which is also the frame the run's reservation was made in. This factor is what
            // keeps the BEAM, drawn out here in the overlay pass, over the heads the staff
            // pass drew — and it is why an ossia grace is the one place the two houses can
            // still disagree about X (HANDOFF §2 U8 ⒝2; test/ossia-beams is the book).
            double unit = os.Size(1.0, g.StaffIndex);
            // THE FONT the beam's stems attach through — the design the head's font-size
            // selected, magnified again by the ossia the staff carries (fontSize composes in
            // LilyPond too). It is the same table the LAYOUT reserved with and the same one
            // the QUANTER measured its x frame in, so the beam cannot land off its stems.
            var graceFont = unit == 1.0
                ? GraceNoteItem.Font
                : GraceNoteItem.Font.Scaled(unit);
            var colX = g.ColumnOffsets;
            double currentX = g.X;
            double lastNoteX = g.X, lastNoteY = staffMiddleY;
            int headIndex = 0;
            int lastGraceStaffPos = 0;
            // Per-head geometry, collected so the beam can be drawn once the whole group's
            // positions are known.
            var headX = new List<double>(g.Columns.Length);
            var headY = new List<double>(g.Columns.Length);
            // The stem FOOT and the stem TIP anchor are two numbers for a chord and
            // one for a single head; see the note where they are filled.
            var headTopY = new List<double>(g.Columns.Length);
            var beamCounts = new List<int>(g.Columns.Length);
            // WHICH columns the one beam covers. Asked ONCE, of the house that owns the
            // sentence, and handed down rather than re-decided — the ordinary pass withholds
            // its stems from exactly these columns by asking the same question
            // (SharedRenderer.BuildBeamedItemsSet).
            int beamedPrefix = GraceNoteEngraver.BeamedPrefix(g.Columns);
            using (gc.Source(g.SourcePosition))
            {
                foreach (var note in g.Columns)
                {
                    if (!colX.IsDefault && headIndex < colX.Length)
                        currentX = g.X + colX[headIndex] * unit;
                    // A REST HOLDS A COLUMN AND DRAWS NO HEAD, so it takes none of the
                    // per-head geometry below and contributes NO entry to headX/headY: it has
                    // no stem, no flag and no beam, and adding one would put a stem on a rest.
                    // Its column is still counted (headIndex++), because the offsets the
                    // layout handed down are per COLUMN.
                    // ⚠️ AND IT IS NO LONGER DRAWN HERE. The ordinary note pass draws it, off
                    // the item it actually is, at the address the layout published
                    // (SharedRenderer.EnumerateStaffItems / ScoreLayout.GraceColumnXs) — the
                    // first grob family to come home under HANDOFF §2 U8 ⒝2, chosen first
                    // because general-grace-settings never names Rest, so it was already
                    // drawn at the staff's own size and moving it changes no size and no
                    // glyph. What it does change is the SOURCE: the drawn rest now carries
                    // its own data-pos instead of the whole group's opening one.
                    if (note.IsRest)
                    {
                        headIndex++;
                        continue;
                    }
                    // WHAT THIS COLUMN OWNS IS DRAWN BY THE ORDINARY ENGRAVERS — the head,
                    // its ledgers, its accidental, and (outside the beamed prefix) its stem,
                    // flag and acciaccatura stroke. They reach it because the grace body is
                    // walked by the ordinary walker and the layout publishes the column's X
                    // (ScoreLayout.GraceColumnXs / SharedRenderer.DrawStaffMeasures), and they
                    // draw it at the size each GROB states (GrobFontSize), which is what
                    // LilyPond does: grace is not a context, it is `\consists Grace_engraver`
                    // inside the ordinary Voice setting a per-grob table
                    // (scm/music-functions.scm:636-650 general-grace-settings).
                    // WHAT IS LEFT HERE is the group's own two spanners, and NOTHING ELSE since
                    // session 315 — the BEAM over its prefix, whose thickness, length-fraction
                    // and prefix rule are all stated for the grace and nowhere else, and the
                    // SLUR to the main note. The DOTS went home with the stem they are measured
                    // against: DrawNote's dot column hangs its flag support off the stem it has
                    // just drawn, so a grace's dot now moves with the SHORTENING, which is what
                    // LilyPond does and what a flat side-model stem could not do
                    // (scratch/p315/measurements.md).
                    // Both spanners need the run's head geometry, which is what this loop gathers.
                    double lowestY = double.MaxValue, highestY = double.MinValue;
                    foreach (var head in note.Heads)
                    {
                        double hy = os.YUp(staffMiddleY + head.StaffPosition / 2.0,
                            g.StaffIndex, g.MeasureIndex);
                        lowestY = Math.Min(lowestY, hy);
                        highestY = Math.Max(highestY, hy);
                    }
                    headX.Add(currentX);
                    // A stem-up stem stands on the BOTTOM of the head column and is measured
                    // from its TOP — Stem::chord_start_y is head_positions(me)[my_dir], the
                    // top head for UP (lily/stem.cc:114-122), while the drawn extent starts
                    // at the other end. For a single head the two are one number.
                    headY.Add(lowestY);
                    headTopY.Add(highestY);
                    beamCounts.Add(BeamCountForDuration(note.BaseDuration.Denominator));
                    lastNoteX = currentX;
                    // The grace slur bows DOWN (score-grace-settings pairs Slur direction
                    // DOWN with Stem UP), so it leaves the column's BOTTOM head.
                    lastNoteY = lowestY;
                    lastGraceStaffPos = note.Lowest.StaffPosition;
                    headIndex++;
                }

                // THE BEAM AND THE STEMS UNDER IT. Every other stem in the run is the
                // ordinary engraver's now; these are not, because a beamed stem ends ON the
                // beam and the beam is the grace's own — its thickness, its length-fraction
                // and the rule for which columns it covers are all stated for the grace and
                // for nothing else (HANDOFF §2 U8 ⒝2 keeps the prefix and the quant here).
                // The ordinary pass is told to leave these columns alone through the same set
                // an ordinary beam uses (SharedRenderer.BuildBeamedItemsSet).
                // LILYPOND-REF: scm/music-functions.scm:652-656 score-grace-settings —
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
                DrawGraceBeam(headX, headY, headTopY, beamCounts, eff, graceFont,
                    beamEnds, beamedPrefix, gc);

                // Grace slur from the last grace notehead to the main notehead.
                // LILYPOND-REF: ly/grace-init.ly startGraceSlur/stopGraceSlur —
                // acciaccatura and appoggiatura are auto-slurred to the main note, and a
                // hand-written `grace { g16( } a8)` is the same two slur events
                // (GraceNoteItem.ExplicitSlur), so it draws the same bow.
                if (g.Columns.Length > 0 &&
                    (g.Type is GraceNoteType.Acciaccatura or GraceNoteType.Appoggiatura
                     || g.ExplicitSlur))
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
                    fontSize, TextRole.TabFret,
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
        foreach (var note in g.Columns)
        {
            if (!colX.IsDefault && headIndex < colX.Length)
                currentX = g.X + colX[headIndex];
            headIndex++;
            // ONE DIGIT PER HEAD: a tab chord prints a fret number on every string it sounds,
            // and a grace chord is no different. The resolver is asked per head so two pitches
            // of one column can land on two strings.
            // ⚠️ THE DIGITS ARE NOT SPREAD HORIZONTALLY. A tab chord's members stand on
            // different STRING LINES, so the seconds shift a notation staff needs has no
            // counterpart here — which is why this loop asks GraceColumnHeads for nothing.
            foreach (var head in note.Heads)
            {
                // The '\N' written on the grace note, when there is one. A string number is
                // not a grob but the resolver's INPUT, so a grace can honour it although it
                // is not a measure item (GraceHeadInfo.StringNumber); 0 means "pick a string".
                var (stringNum, fret) = Tunings.CalculateFret(
                    head.Midi + octaveShift, tuningArray, head.StringNumber ?? 0);
                string fretText = fret.ToString();
                yield return new TabGraceDigit(
                    stringNum, fretText, currentX,
                    LilySharp.Core.Svg.Layout.TabConstants.FretGlyphWidth(fretText, fontSize));
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
    /// Draws a grace group's BEAM over the columns it covers, and the stems that end on it.
    /// Nothing when the group has no beam — every other stem, flag and stroke in the run is
    /// the ordinary engraver's.
    /// </summary>
    /// <remarks>
    /// ⚠️ WHY THIS IS STILL THE GRACE'S OWN AND THE STEMS BESIDE IT ARE NOT. A beamed stem
    /// ends ON the beam, so it cannot be drawn without it; and the beam is a grob LilyPond
    /// states separately for the grace —
    /// LILYPOND-REF: scm/music-functions.scm:636-650 general-grace-settings —
    ///   <c>(Voice Beam beam-thickness 0.384)</c> and <c>(Voice Beam length-fraction 0.8)</c>,
    ///   against 0.48 in scm/define-grobs.scm — plus the PREFIX rule, which is measured and
    ///   not derived: the beam covers the leading run of head columns and stops at the first
    ///   rest, so <c>{ d'16 e'16 r16 f'16 }</c> quants to the same four digits as
    ///   <c>{ d'16 e'16 }</c> and the <c>f'</c> takes a flag (scratch/p308/lp2/measurements.md).
    /// LILYPOND-REF: scm/music-functions.scm:652-656 score-grace-settings — grace
    ///   stems are forced UP (so a stem-up beam stacks its secondary beams toward
    ///   the heads, i.e. downward on the page). The ordinary pass is told the same, at the
    ///   item (SharedRenderer.DrawStaffMeasures).
    /// LILYPOND-REF: lily/beam.cc secondary beams translated by beam-thickness + gap.
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
    /// <param name="ys">Each column's stem FOOT — the bottom of its head column.</param>
    /// <param name="topYs">Each column's stem TIP ANCHOR — the top of its head column,
    /// which is what a stem's LENGTH is measured from for an up stem
    /// (Stem::chord_start_y, lily/stem.cc:114-122). Equal to <paramref name="ys"/>
    /// entry for entry unless a column is a chord.</param>
    private static void DrawGraceBeam(
        List<double> xs, List<double> ys, List<double> topYs, List<int> beamCounts,
        double scale,
        GlyphMetrics.DesignMetrics headFont,
        (double Left, double Right)? beamEnds, int beamedCount,
        IDrawingContext gc)
    {
        // ONE COLUMN IS A FLAG, NOT A BEAM, and the ordinary pass has already drawn it: the
        // gate is the same one BuildBeamedItemsSet applies when it decides which columns to
        // withhold from that pass, so the two cannot disagree about who draws a stem.
        if (beamedCount < 2 || xs.Count == 0) return;

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
        // THE FALLBACK LENGTH, and its only reader since session 315 took the dots home —
        // see GraceNoteEngraver.StemLength for what it departs from.
        double stemLen = GraceNoteEngraver.StemLength(scale);
        // THE one house for where a stem stands, and the whole point of using it here: it is
        // the expression the beam was SCORED in (BeamScoringProblem's StemXOf reads the same
        // LayoutUtilities.StemX from the same head font) and the one the collision collector
        // reads. Spelling it a second time here is what let the two frames drift — the second
        // spelling pulled the stem back by half a SCALED thickness while the quanter used the
        // unscaled one, and nothing observed either.
        // ⚠️ NOT PORTED — the per-duration grace head, in the STEM's frame: this argument
        //   decides where an up stem attaches, and it must follow the head the ordinary pass
        //   actually draws. That pass resolves the glyph from the duration
        //   (EmmentalerGlyphs.GetNotehead in DrawNote/DrawChord), so a beamed grace whose
        //   column is longer than an eighth would attach its stem at a black head's width
        //   while a half head is drawn. Unreachable as it stands — a beam needs two columns
        //   of an eighth or shorter, so every column this method sees is a black head — and
        //   spelt as a constant rather than read off the column because THIS method is not
        //   handed the durations, only their beam counts.
        //   LILYPOND-REF: lily/note-head.cc:207 internal_print — the glyph comes from the
        //     duration log.
        const int graceHeadNoteValue = 4;
        double StemX(int i) =>
            LayoutUtilities.StemX(xs[i], up: true, graceHeadNoteValue,
                NoteheadStyle.Default, headFont);
        // Stem end: the up-stem runs from the head to stemLen above it — up is larger
        // Y-up, so add in the native page Y-up frame. Only the FALLBACK reads it (a group
        // the layout could not quant); a quanted beam gives every stem its own end.
        double StemEndY(int i) => topYs[i] + stemLen;

        // OVER THE PREFIX, NOT OVER THE RUN. beamedCount is GraceNoteEngraver.BeamedPrefix —
        // the same sentence the reservation, the quanter and the ordinary pass's withholding
        // set read — counted over the columns that HAVE HEADS, which is exactly what reaches
        // this method (a rest contributes no entry, having no stem). So the stack depth is
        // the prefix's too: a 32nd standing AFTER the beam is a flag the ordinary pass drew,
        // and letting its beam count into maxBeams would space this beam's lines for a
        // level it never draws (BeamTranslationOf takes maxBeams).
        int maxBeams = 0;
        for (int i = 0; i < beamedCount && i < beamCounts.Count; i++)
            maxBeams = Math.Max(maxBeams, beamCounts[i]);
        if (maxBeams == 0) return;

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
        // THE PREFIX'S OUTER STEMS, not the run's: the quanter scored this beam over
        // [0, beamedCount) and answered AT ITS OUTER STEMS, so anchoring it anywhere else
        // draws a configuration the scorer never chose.
        int last = beamedCount - 1;
        double edgeL = StemX(0), edgeR = StemX(last);
        double beamLeftY = beamEnds?.Left ?? StemEndY(0);
        double beamRightY = beamEnds?.Right ?? StemEndY(last);
        double span = edgeR - edgeL;
        double beamSlope = span > 0.001 ? (beamRightY - beamLeftY) / span : 0.0;
        double BeamY(double x) => beamLeftY + beamSlope * (x - edgeL);

        for (int i = 0; i < beamedCount; i++)
            gc.DrawLine(StemX(i), ys[i], StemX(i), BeamY(StemX(i)), Color.Black, stemThick);

        // A grace beam's thickness is DECLARED, not scaled: scm/music-functions.scm:635-648
        // general-grace-settings has (Voice Beam beam-thickness 0.384) where
        // scm/define-grobs.scm declares 0.48, and the
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

        Beam(0, last, 0);                     // primary across the beamed prefix
        for (int level = 1; level < maxBeams; level++)
        {
            // Secondaries stack toward the heads (below the up-stem beam), which is
            // the negative direction in the page Y-up frame.
            double off = -(level * beamTrans);
            int i = 0;
            while (i < last)
            {
                if (beamCounts[i] > level && beamCounts[i + 1] > level)
                {
                    int j = i;
                    while (j < last && beamCounts[j] > level && beamCounts[j + 1] > level) j++;
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
            new(startX, graceY - graceHalf, graceY + graceHalf),
            new(endX, mainY - mainHalf, mainY + mainHalf),
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
