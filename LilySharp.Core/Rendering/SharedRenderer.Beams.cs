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

    private static void DrawBeams(MultiStaffScore score, ScoreLayout layout, SystemLayout system, IDrawingContext gc)
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
                ossiaScope = gc.BeginGroup(new DrawingTransform(0, staffY, OssiaScale, OssiaScale));
                bgc = new UnscaledXDrawingContext(gc, OssiaScale);
                staffY = 0;
            }
            double staffMiddleY = staffY + StaffHeight / 2;
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

            // Per-member stem direction: kneed beams mix up- and down-stems
            // within one group (LILYPOND-REF: beam.cc:894-982 consider_auto_knees),
            // which flips the stem's notehead attachment side.
            bool MemberUp(int i) => grp.IsKnee ? grp.Members[i].MemberStemUp : grp.StemUp;

            // Both notation and tab stems attach at the notehead edge (from the
            // note column). On a tab staff the fret digit is centred a
            // TabHeadCenterOffset to the RIGHT of the column, so this edge x lands
            // on the digit and, crucially, matches the companion notation stem's x.
            double StemAttachX(int i) => beam.MemberXPositions[i]
                + (MemberUp(i) ? EngravingDefaults.StemUpAttachX : EngravingDefaults.StemDownAttachX);

            double leftBeamY = StaffFrame.PositionToDevice(beam.LeftY, staffMiddleY);
            double rightBeamY = StaffFrame.PositionToDevice(beam.RightY, staffMiddleY);

            // A tab beam's height can't come from the notation quanter — its Y is
            // in staff positions, not string lines, so mapped onto the tab staff it
            // can land right on the fret numbers and leave stub stems. Instead lay a
            // tab beam HORIZONTAL a fixed distance past the OUTERMOST digit, so each
            // stem's length is set by its string: a low string gets a long stem, a
            // high (open) string a short one — but never a stub.
            bool allTab = grp.Members.Length > 0 && Enumerable.Range(0, grp.Members.Length)
                .All(i => MemberStaffOf(i)?.IsTab == true);
            if (allTab)
            {
                const double tabBeamStem = 3.0; // shortest stem, on the outermost string
                double extreme = grp.StemUp ? double.MaxValue : double.MinValue;
                for (int i = 0; i < grp.Members.Length; i++)
                {
                    double nearY = TabStemHeadY(grp.Members[i].Item, grp.StemUp,
                        LayoutUtilities.FindStaffYInSystem(system, MemberStaffIdx(i)), MemberStaffOf(i)!);
                    extreme = grp.StemUp ? Math.Min(extreme, nearY) : Math.Max(extreme, nearY);
                }
                leftBeamY = rightBeamY = extreme + (grp.StemUp ? -tabBeamStem : tabBeamStem);
            }

            double leftStemX = StemAttachX(0);
            double rightStemX = StemAttachX(grp.Members.Length - 1);

            // Extend each beam END outward by half the stem thickness so the beam
            // covers the terminal stems flush; otherwise it stops at the stem
            // centre and reads a stem-width short when the preview is zoomed in.
            // LILYPOND-REF: lily/beam.cc:631 horizontal_[dir] += dir * stem_width/2.
            double halfStem = EngravingDefaults.StemThickness / 2;
            double beamSlope = rightStemX > leftStemX + 0.001
                ? (rightBeamY - leftBeamY) / (rightStemX - leftStemX) : 0.0;

            // Primary beam — drawn as a thick filled rectangle (sloped by polygon)
            DrawBeamSegment(leftStemX - halfStem, leftBeamY - beamSlope * halfStem,
                rightStemX + halfStem, rightBeamY + beamSlope * halfStem, bgc);

            // Secondary beams (16th+) stack toward the noteheads of the beam's
            // overall direction. Each level draws full segments between adjacent
            // members that both carry the beam, plus short partial beams (beamlets)
            // for members that carry it in isolation — e.g. the 16th in a
            // dotted-8th + 16th pair, whose second beam is a left-pointing stub.
            // LILYPOND-REF: lily/beam.cc Beam::print / fractional (stub) beams.
            int maxBeamCount = grp.Members.Max(m => m.BeamCount);
            for (int level = 1; level < maxBeamCount; level++)
            {
                double offset = level * EngravingDefaults.BeamTranslation;
                if (!grp.StemUp) offset = -offset;
                double beamSpanX = rightStemX - leftStemX;
                double BeamYAt(double x) => leftBeamY + offset +
                    (beamSpanX > 0.001 ? (x - leftStemX) / beamSpanX : 0) * (rightBeamY - leftBeamY);

                for (int i = 0; i < grp.Members.Length; i++)
                {
                    if (grp.Members[i].BeamCount <= level) continue;
                    bool rightFull = i < grp.Members.Length - 1 && grp.Members[i + 1].BeamCount > level;
                    bool leftFull = i > 0 && grp.Members[i - 1].BeamCount > level;

                    if (rightFull)
                    {
                        // Full segment i -> i+1 (drawn once, from its left member).
                        double xa = StemAttachX(i);
                        double xb = StemAttachX(i + 1);
                        // Flush the whole-beam ends with the terminal stems (as above).
                        if (i == 0) xa -= halfStem;
                        if (i + 1 == grp.Members.Length - 1) xb += halfStem;
                        DrawBeamSegment(xa, BeamYAt(xa), xb, BeamYAt(xb), bgc);
                    }
                    else if (!leftFull)
                    {
                        // Isolated at this level: a beamlet (fractional beam) stub.
                        // It points back toward the previous note; the first note of
                        // the group points forward instead.
                        double x0 = StemAttachX(i);
                        double x1 = x0 + (i > 0 ? -EngravingDefaults.BeamletLength : EngravingDefaults.BeamletLength);
                        // Flush the stub's outer end (at a terminal stem) with the stem.
                        if (i == 0) x0 -= halfStem;
                        else if (i == grp.Members.Length - 1) x0 += halfStem;
                        DrawBeamSegment(x0, BeamYAt(x0), x1, BeamYAt(x1), bgc);
                    }
                    // else (leftFull && !rightFull): this member is the right end of a
                    // full segment already drawn from i-1; nothing more to do.
                }
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
                double beamY = leftBeamY + slope * (stemX - leftStemX);

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
                    headY = TabStemHeadY(member.Item, up,
                        LayoutUtilities.FindStaffYInSystem(system, memberStaffIdx), memberStaff);
                }
                else
                {
                    // Ossia beams never cross staves: every member sits on the
                    // ossia's own (local) frame.
                    double memberStaffMiddleY = !ossiaBeam && memberStaffIdx >= 0
                        ? LayoutUtilities.FindStaffYInSystem(system, memberStaffIdx) + StaffHeight / 2
                        : staffMiddleY;
                    headY = memberStaffMiddleY - GetMemberStaffPosition(member, up) * 0.5
                        + StemAttachYOffset(member.Item switch
                        {
                            NoteItem n => n.Notehead,
                            ChordItem ch => ch.Notehead,
                            _ => NoteheadStyle.Default,
                        }, up);
                }
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
        int octaveShift = Tunings.OctaveShift(tuningType, staff.TabSourceClef);
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
        // Half the digit height (0.6875 × font) plus a small gap, so the stem meets
        // the bigger number without overlapping it.
        double clearance = 0.6875 * TabFretFontSize / 2 + 0.3;
        return digitY + (stemUp ? -clearance : clearance);
    }

    private static void DrawBeamSegment(double x1, double y1, double x2, double y2, IDrawingContext gc)
    {
        // Sloped beam as a filled polygon would be ideal; simple thick line is a
        // good Phase 2-A approximation (LP uses precise quad polygons).
        gc.DrawLine(x1, y1, x2, y2, Color.Black, EngravingDefaults.BeamThickness);
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

    private static void DrawAccidental(
        string accidentalKind, bool isCourtesy, double noteheadX, double noteheadY,
        int sourcePosition, IDrawingContext gc, double scale = 1.0)
    {
        char glyph = EmmentalerGlyphs.AccidentalGlyph(accidentalKind);
        // Grace-note accidentals are reduced with the grace head (font-size -3 ≈
        // 0.65); the glyph size AND all the bbox-derived offsets scale together.
        // LILYPOND-REF: scm/music-functions.scm general-grace-settings — grace grobs
        //   inherit the reduced font-size, accidentals included.
        double fs = FontSize * scale;
        var accBBox = GlyphMetrics.GetAccidentalBBox(accidentalKind);
        double accWidth = accBBox.Width * scale;
        double gap = GlyphMetrics.AccidentalNoteGap * scale;

        if (isCourtesy)
        {
            // Parens attach at the accidental's INK edges with zero padding —
            // add_at_edge juxtaposes stencil extents, so positioning must use
            // each glyph's bounding box, not its advance. The paren glyphs are
            // designed for this: leftparen draws BEHIND its origin (ink
            // [-0.60,-0.15], advance 0), so placing it "at" a position and
            // stepping by advance leaves a gap that pushes the accidental
            // against the right paren.
            // LILYPOND-REF: lily/accidental.cc:35-46 — parenthesize()
            var leftParen = GlyphMetrics.AccidentalLeftParen;
            var rightParen = GlyphMetrics.AccidentalRightParen;
            double lpWidth = leftParen.Width * scale;
            double rpWidth = rightParen.Width * scale;
            double totalInk = lpWidth + accWidth + rpWidth;

            // Ink left edge of the whole "(♮)" assembly.
            double inkLeft = noteheadX - gap - totalInk;
            double accInkLeft = inkLeft + lpWidth;
            using (gc.Source(sourcePosition))
            {
                // Each origin is chosen so the glyph's INK lands flush.
                gc.DrawAttachedGlyph(EmmentalerGlyphs.AccidentalLeftParen,
                    accInkLeft - leftParen.Right * scale, noteheadY, fs);
                gc.DrawAttachedGlyph(glyph, accInkLeft - accBBox.Left * scale, noteheadY, fs);
                gc.DrawAttachedGlyph(EmmentalerGlyphs.AccidentalRightParen,
                    accInkLeft + accWidth - rightParen.Left * scale, noteheadY, fs);
            }
        }
        else
        {
            using (gc.Source(sourcePosition))
                gc.DrawAttachedGlyph(glyph, noteheadX - accWidth - gap, noteheadY, fs);
        }
    }

}
