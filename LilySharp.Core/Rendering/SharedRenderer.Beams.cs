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
    // ---------- Beams ----------

    private static void DrawBeams(MultiStaffScore score, ScoreLayout layout, SystemLayout system, IDrawingContext gc,
        double pageHeight)
    {
        var staffByIndex = score.EnumerateStaves().ToDictionary(s => s.GlobalStaffIndex, s => s.Staff);
        // Beams whose notes are hidden under a percent sign are hidden too.
        var percentByStaff = new HashSet<(int Staff, int Measure)>();
        foreach (var prItem in score.PercentRepeats)
            percentByStaff.Add((prItem.StaffIndex, prItem.MeasureIndex));
        foreach (var beam in layout.BeamLayouts)
        {
            // Only draw beams whose first measure is in this system
            bool inSystem = system.Measures.Any(m => m.MeasureIndex == beam.Group.MeasureIndex);
            if (!inSystem) continue;
            if (percentByStaff.Contains((Math.Max(0, beam.StaffIndex), beam.Group.MeasureIndex)))
                continue;

            var grp = beam.Group;

            // A tab beam whose every member sounds below the lowest string is
            // hidden entirely (no beam line, no stems) — see NoteItem.TabBelowRange.
            if (grp.Members.Length > 0
                && grp.Members.All(m => m.Item is NoteItem { TabBelowRange: true }))
                continue;

            // The quanter's Y positions are staff positions relative to the
            // beam's OWN staff middle — resolve that staff in this system
            // (multi-staff scores; -1 = single staff = the system's first).
            // Top-line Y-up of the beam's own staff (page-bottom origin).
            double staffY = beam.StaffIndex >= 0
                ? LayoutUtilities.FindStaffYInSystem(system, beam.StaffIndex)
                : system.Y;

            // Ossia beams get the same treatment as the ossia staff pass: a
            // uniform-scale group anchored at the staff's Y with X compensated
            // back onto the shared columns — stems, beam thickness and slope
            // all shrink with the notation (LP: the beam belongs to the
            // magnified staff's grobs). All Ys below are then staff-LOCAL.
            var beamStaff = beam.StaffIndex >= 0
                && staffByIndex.TryGetValue(beam.StaffIndex, out var bst) ? bst : null;
            bool ossiaBeam = beamStaff?.IsOssia == true;
            IDisposable? ossiaScope = null;
            IDrawingContext bgc = gc;
            if (ossiaBeam)
            {
                // See DrawSystem's ossia group: the flip decorator conjugates this
                // transform and re-flips the content by the page height, so the group
                // translate absorbs the scaled page height (staffY is the top-line
                // Y-up here, = H − deviceTop) and the content's local refpoint is
                // pageHeight to stay byte-identical.
                ossiaScope = gc.BeginGroup(new DrawingTransform(0, staffY - OssiaScale * pageHeight, OssiaScale, OssiaScale));
                bgc = new UnscaledXDrawingContext(gc, OssiaScale);
                staffY = pageHeight;
            }
            double staffMiddleY = staffY - StaffHeight / 2;
            try
            {

            // Resolve each member's staff. Cross-staff beams — and the tab
            // mirror of a notation beam — route members to a staff OTHER than
            // the beam's own StaffIndex, so this must be decided per member.
            int MemberStaffIdx(int i) => (!beam.MemberStaffIndices.IsDefaultOrEmpty
                    && i < beam.MemberStaffIndices.Length && beam.MemberStaffIndices[i] >= 0)
                ? beam.MemberStaffIndices[i] : beam.StaffIndex;
            Staff? MemberStaffOf(int i) => MemberStaffIdx(i) is var si && si >= 0
                && staffByIndex.TryGetValue(si, out var s) ? s : null;

            // A tab beam draws entirely on tab staves; its stem direction comes from
            // the STRINGS (tab heads), not the notated pitch, so a bass run on the
            // bottom strings beams UP like LilyPond — the opposite of the notation
            // group's pitch-based direction.
            bool allTab = grp.Members.Length > 0 && Enumerable.Range(0, grp.Members.Length)
                .All(i => MemberStaffOf(i)?.IsTab == true);
            // A numbers-only tab (`tab … as numbers`) prints fret digits alone — no
            // beams (its stems are already suppressed in DrawTabMeasure). The finally
            // below still disposes any ossia scope.
            if (allTab && MemberStaffOf(0) is { TabNumbersOnly: true })
                continue;
            TabStaffGeometry? tabDirGeom = null;
            bool tabDir = false;
            if (allTab && MemberStaffOf(0) is { } tabDirStaff)
            {
                var g = new TabStaffGeometry(tabDirStaff.Tuning ?? TuningType.Guitar,
                    pageHeight - LayoutUtilities.FindStaffYInSystem(system, MemberStaffIdx(0)),
                    tabDirStaff.TabSourceClef, tabDirStaff.Transposition);
                tabDirGeom = g;
                tabDir = g.GroupStemUp(grp.Members.Select(m => m.Item));
            }

            // Per-member stem direction: kneed beams mix up- and down-stems
            // within one group (LILYPOND-REF: beam.cc:971 consider_auto_knees),
            // which flips the stem's notehead attachment side. A tab beam ignores the
            // knee and uses its string-based direction.
            bool MemberUp(int i) => tabDirGeom.HasValue ? tabDir
                : grp.IsKnee ? grp.Members[i].MemberStemUp : grp.StemUp;

            // Both notation and tab stems attach at the notehead edge (from the
            // note column). On a tab staff the fret digit is centred a
            // TabHeadCenterOffset to the RIGHT of the column, so this edge x lands
            // on the digit and, crucially, matches the companion notation stem's x.
            // The one house, shared with the QUANTER: BeamScoringProblem measures a covered
            // grob's x against the beam's stems, and that is only the same frame if the two
            // spell this offset the same way. See LayoutUtilities.StemAttachX.
            double StemAttachX(int i) =>
                LayoutUtilities.StemX(beam.MemberXPositions[i], MemberUp(i));

            double leftBeamY = staffMiddleY + beam.LeftY / 2.0;
            double rightBeamY = staffMiddleY + beam.RightY / 2.0;

            // A tab beam's height can't come from the notation quanter — its Y is in
            // staff positions, not string lines. Lay it out from the STRING contour so
            // each stem's length is set by its string.
            double leftStemX = StemAttachX(0);
            double rightStemX = StemAttachX(grp.Members.Length - 1);

            if (tabDirGeom.HasValue)
            {
                // Quant the tab beam from the notes' STRING lines, in the string-based
                // stem direction (tabDir) rather than the notation pitch direction.
                int n = grp.Members.Length;
                var xs = new double[n];
                for (int i = 0; i < n; i++) xs[i] = StemAttachX(i);
                var line = TabBeamQuant.Compute(grp, xs, tabDirGeom.Value, tabDir);
                // The tab quanter returns device Y; lift to page Y-up (tab beams are
                // never ossia, so this is the absolute page frame).
                leftBeamY = pageHeight - TabBeamMath.At(line, leftStemX);
                rightBeamY = pageHeight - TabBeamMath.At(line, rightStemX);
            }

            // Extend each beam END outward by half the stem thickness so the beam
            // covers the terminal stems flush; otherwise it stops at the stem
            // centre and reads a stem-width short when the preview is zoomed in.
            // LILYPOND-REF: lily/beam.cc:631 horizontal_[dir] += dir * stem_width/2.
            double halfStem = EngravingDefaults.StemThickness / 2;
            double beamSpanX = rightStemX - leftStemX;
            // The quanted PRIMARY beam line (rank 0) at x — every rank is drawn relative
            // to this: Y = primary + BeamTranslation × rank (LP: positions + beam_dy·rank).
            double PrimaryBeamYAt(double x) => leftBeamY +
                (beamSpanX > 0.001 ? (x - leftStemX) / beamSpanX : 0) * (rightBeamY - leftBeamY);

            // Beam lines via LilyPond's subdivision maths: assign each beam a vertical
            // rank per stem, then collect the ranks into drawable spans. Rank 0 is the
            // primary line; +1 sits BeamTranslation above it, −1 below. This is what
            // keeps a beam through a knee two straight parallel lines, places the beam
            // corners and beamlets, and (via each stem's extreme rank) lets every stem
            // reach the outermost beam on its own side.
            // LILYPOND-REF: lily/beam.cc:294 calc_beaming, :457 calc_beam_segments, :783 print.
            var beamingInput = new BeamSubdivision.StemBeaming[grp.Members.Length];
            for (int i = 0; i < grp.Members.Length; i++)
                beamingInput[i] = new BeamSubdivision.StemBeaming(
                    grp.Members[i].BeamCountLeft, grp.Members[i].BeamCountRight,
                    MemberUp(i) ? 1 : -1, StemAttachX(i));
            var beamRanks = BeamSubdivision.CalcBeaming(beamingInput);
            foreach (var seg in BeamSubdivision.CalcBeamSegments(
                         beamingInput, beamRanks,
                         EngravingDefaults.BeamletLength,
                         EngravingDefaults.BeamletMaxLengthProportion, halfStem))
            {
                double yOff = EngravingDefaults.BeamTranslation * seg.Rank;
                DrawBeamSegment(seg.XLeft, PrimaryBeamYAt(seg.XLeft) + yOff,
                    seg.XRight, PrimaryBeamYAt(seg.XRight) + yOff, bgc);
            }

            // Stems for beam members (replace any individual stems). For knees
            // each stem runs from its OWN notehead (attachment side per member
            // direction) to the shared beam line; for cross-staff members the
            // notehead lives in that member's staff frame.
            double slope = (rightStemX - leftStemX) > 0.001
                ? (rightBeamY - leftBeamY) / (rightStemX - leftStemX) : 0;
            for (int i = 0; i < grp.Members.Length; i++)
            {
                var member = grp.Members[i];
                // A member hidden below the tab's lowest string draws no stem.
                if (member.Item is NoteItem { TabBelowRange: true })
                    continue;
                bool up = MemberUp(i);
                double stemX = StemAttachX(i);
                double primaryBeamY = leftBeamY + slope * (stemX - leftStemX);

                int memberStaffIdx = MemberStaffIdx(i);
                Staff? memberStaff = MemberStaffOf(i);

                double headY;
                if (memberStaff?.IsTab == true)
                {
                    // On a tab staff the stem runs from the FRET NUMBER (at its
                    // string line), not a notehead at a staff position. Keep the
                    // stem's X aligned with the notation staff's stem; only the
                    // near end moves to the digit, with a small gap so the stem
                    // never overlaps the number.
                    // TabStemHeadY returns device Y; lift to page Y-up (tab beams
                    // are never ossia).
                    headY = pageHeight - TabStemHeadY(member.Item, up,
                        pageHeight - LayoutUtilities.FindStaffYInSystem(system, memberStaffIdx), memberStaff);
                }
                else
                {
                    // Ossia beams never cross staves: every member sits on the
                    // ossia's own (local) frame.
                    double memberStaffMiddleY = !ossiaBeam && memberStaffIdx >= 0
                        ? LayoutUtilities.FindStaffYInSystem(system, memberStaffIdx) - StaffHeight / 2
                        : staffMiddleY;
                    headY = memberStaffMiddleY + GetMemberStaffPosition(member, up) / 2.0
                        - StemAttachYOffset(member.Item switch
                        {
                            NoteItem n => n.Notehead,
                            ChordItem ch => ch.Notehead,
                            _ => NoteheadStyle.Default,
                        }, up, noteValue: 8); // beamed heads are always filled (8th or shorter)
                }
                // The stem ends at the OUTERMOST beam rank on its own side — the extreme
                // of this stem's ranks in its direction (up ⇒ max rank, down ⇒ min), so
                // it runs through every beam line that crosses it. For an ordinary beam
                // that extreme is the primary (rank 0) and this is a no-op; in a knee
                // with two straight parallel beams the down-stem reaches the lower line
                // and the up-stem note the upper.
                // LILYPOND-REF: lily/beam.cc:1113-1157 Beam::calc_stem_y —
                //   stem_y = beam_line + beam_translation × beam_multiplicity[stem_dir]
                //   (lily/stem.cc:1269 unites the stem's left+right ranks, indexed by dir).
                int stemRank = beamRanks[i].Multiplicity(up ? 1 : -1);
                double beamY = primaryBeamY + EngravingDefaults.BeamTranslation * stemRank;
                bgc.DrawLine(stemX, headY, stemX, beamY,
                    Color.Black, EngravingDefaults.StemThickness);
            }
            }
            finally
            {
                ossiaScope?.Dispose();
            }
        }
    }

    // LILYPOND-REF: lily/stem.cc Stem::extremal_heads — the stem attaches at the
    // extremal head: lowest (Min) staff position for a stem-up chord, highest (Max)
    // for stem-down.
    /// <summary>
    /// Staff position of the stem's notehead attachment: for chords the head
    /// on the far side from the beam (stem-up beams attach at the bottom head).
    /// </summary>
    private static int GetMemberStaffPosition(BeamMember m, bool stemUp) => m.Item switch
    {
        NoteItem n => n.StaffPosition,
        ChordItem c => stemUp
            ? c.Notes.Min(x => x.StaffPosition)
            : c.Notes.Max(x => x.StaffPosition),
        _ => 0,
    };

    /// <summary>
    /// The Y where a tab-staff stem meets its fret number: the digit's string
    /// line, offset by half the digit height plus a small gap so the stem touches
    /// the number without overlapping it. The stem's X stays aligned with the
    /// notation staff's stem (handled by the caller).
    /// </summary>
    private static double TabStemHeadY(MusicItem item, bool stemUp, double tabStaffTopY, Staff staff)
    {
        var tuningType = staff.Tuning ?? TuningType.Guitar;
        int octaveShift = Tunings.SoundingShift(staff.TabSourceClef, staff.Transposition);
        int[] tuning = Tunings.GetTuning(tuningType);

        int midi = 0;
        int? stringNumber = null;
        int? chordStringNum = null;
        switch (item)
        {
            case NoteItem n:
                midi = n.Midi; stringNumber = n.StringNumber;
                break;
            case ChordItem c when c.Notes.Length > 0:
                // On a tab the digits stack by STRING, so the stem must meet the
                // END of the stack in its direction — the TOP digit (smallest
                // string number) for an up-stem, the BOTTOM for a down-stem. The
                // strings come from the SAME exclusive allocation the drawn
                // chord uses, or the stem could anchor on a digit that moved.
                var chordAlloc = Tunings.CalculateChordFrets(
                    c.Notes.Select(x => (x.Midi + octaveShift, x.StringNumber)).ToList(), tuning);
                int headIdx = 0;
                for (int ci = 1; ci < chordAlloc.Length; ci++)
                {
                    if (stemUp
                        ? chordAlloc[ci].stringNum < chordAlloc[headIdx].stringNum
                        : chordAlloc[ci].stringNum > chordAlloc[headIdx].stringNum)
                        headIdx = ci;
                }
                chordStringNum = chordAlloc[headIdx].stringNum;
                break;
        }

        int stringNum = chordStringNum
            ?? Tunings.CalculateFret(midi + octaveShift, tuning, stringNumber ?? 0).stringNum;
        double stringSpace = EngravingDefaults.TabStringSpace(Tunings.GetStringCount(tuningType));
        double digitY = tabStaffTopY + (stringNum - 1) * stringSpace;
        // The stem starts midway between the digit's ink edge and the next string line —
        // the one house for that window, shared with the tab beam quanter so the drawn stem
        // and the scored one cannot drift. (It used to be spelled out here a second time, as
        // `0.6875 * TabFretFontSize / 2 + 0.3`, which is the same quantity in different words.)
        double clearance = TabConstants.StemClearance(stringSpace);
        return digitY + (stemUp ? -clearance : clearance);
    }

    private static void DrawBeamSegment(double x1, double y1, double x2, double y2, IDrawingContext gc)
    {
        // A beam is a PARALLELOGRAM with VERTICAL ends, not a sloped thick line: a
        // butt-capped thick line caps its ends perpendicular to the slope, leaving a
        // triangle poking past each terminal stem. The endpoints (x1,y1)/(x2,y2) are
        // the beam centreline at the (already stem-extended) left/right x, so the four
        // corners are those ends at ±half the (vertical) beam thickness. This mirrors
        // the grace-beam path (SharedRenderer.GraceNotes.cs — the `Beam` local), so
        // both beam kinds share one geometry. (LP's blot-rounded corners are not
        // reproduced — the grace path omits them too.)
        // LILYPOND-REF: lily/lookup.cc Lookup::beam — parallelogram of width `w`,
        //   thickness `thick`, sloped by `slope`, corners offset so the ends stay
        //   vertical; called from lily/beam.cc:794 Beam::print.
        double beamHalf = EngravingDefaults.BeamThickness / 2;
        gc.DrawFilledQuad(
            (x1, y1 + beamHalf), (x2, y2 + beamHalf),
            (x2, y2 - beamHalf), (x1, y1 - beamHalf), Color.Black);
    }

    // ---------- Accidentals ----------

    /// <summary>Chord accidental column placement (stagger/skylines).</summary>
    private static readonly AccidentalPlacement AccidentalColumn = new();

    /// <summary>
    /// Draws an accidental (with courtesy parens when set) so its ink LEFT
    /// edge lands at <paramref name="inkLeftX"/> — used for chord accidental
    /// columns whose X comes from <see cref="AccidentalPlacement"/>.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/accidental-placement.cc position_apes.</remarks>
    private static void DrawAccidentalAtInkLeft(
        string accidentalKind, bool isCourtesy, double inkLeftX, double noteheadY,
        int sourcePosition, IDrawingContext gc, double scale = 1.0)
    {
        char glyph = EmmentalerGlyphs.AccidentalGlyph(accidentalKind);
        var accBBox = GlyphMetrics.GetAccidentalBBox(accidentalKind);
        // Cue columns pass scale < 1: the glyph AND its bbox-derived offsets shrink
        // together, matching the (already scaled) X from AccidentalPlacement.
        double fs = FontSize * scale;

        if (isCourtesy)
        {
            // Same paren assembly as DrawAccidental, anchored at the ink left.
            // LILYPOND-REF: lily/accidental.cc:35-46 — parenthesize()
            var leftParen = GlyphMetrics.AccidentalLeftParen;
            var rightParen = GlyphMetrics.AccidentalRightParen;
            double accInkLeft = inkLeftX + leftParen.Width * scale;
            using (gc.Source(sourcePosition))
            {
                gc.DrawAttachedGlyph(EmmentalerGlyphs.AccidentalLeftParen,
                    accInkLeft - leftParen.Right * scale, noteheadY, fs);
                gc.DrawAttachedGlyph(glyph, accInkLeft - accBBox.Left * scale, noteheadY, fs);
                gc.DrawAttachedGlyph(EmmentalerGlyphs.AccidentalRightParen,
                    accInkLeft + accBBox.Width * scale - rightParen.Left * scale, noteheadY, fs);
            }
        }
        else
        {
            using (gc.Source(sourcePosition))
                gc.DrawAttachedGlyph(glyph, inkLeftX - accBBox.Left * scale, noteheadY, fs);
        }
    }

}
