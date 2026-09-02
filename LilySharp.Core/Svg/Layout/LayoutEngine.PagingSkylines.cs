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
using System.Linq;
using LilySharp.Core.Rendering;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

internal sealed partial class LayoutEngine
{
    /// <summary>
    /// Estimates the additional UP extent a system's above-staff annotations contribute, for a
    /// range of measures.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1025-1054 distribute_loose_lines()
    /// LILYPOND-REF: lily/axis-group-interface.cc:138-173 pure_height estimation
    /// LILYPOND-REF: lily/axis-group-interface.cc:359-474 outside-staff-priority
    /// <para>
    /// ⚠️ IT USED TO ESTIMATE THE DOWN SIDE TOO, and was named for that: lyrics, dynamics,
    /// hairpins and figured bass each had a hand-picked constant here. Every one of them was a
    /// SECOND model of ink this engine already places and already puts into these extents
    /// (HANDOFF §5.2.1②), and they went one at a time as observers were opened for them — the
    /// lyric block to its alignment's own walk, and the other three to
    /// audit/lp-geometry's {figbass,dynamic,hairpin}.page.* readings in 2026-07-30's sessions.
    /// LilyPond never had any of them: a system's pure height comes from the same grobs' pure
    /// extents. ⇒ THE DOWN SIDE IS NOT ESTIMATED AT ALL ANY MORE — it is the down skyline's,
    /// and the caller's only remaining below-staff term is the lyric block's measured
    /// reservation, which it holds itself.
    /// </para>
    /// <para>
    /// ⚠️ AND THE CHORD SYMBOL WENT THE SAME WAY ON 2026-08-28 (session 273), which is why
    /// there is no chord branch below and no <c>chordNames</c> parameter. It set BOTH terms
    /// to a hand-picked 3.0 for any system carrying an INLINE symbol — the argument was
    /// <c>inlineChordNames</c>, so a chord ROW never reached it, which is why every
    /// chord-row book in audit/lp-geometry (CHR1/CHR2, GCF/GCS) was blind to it and one
    /// rerender book held it on an SVG hash alone. Both terms were measured before it went
    /// (audit/lp-geometry <c>page.inline-chord.*</c>, probe inline-chord-page.ly):
    /// </para>
    /// <para>
    /// THE BAND WAS A THIRD CHARGE. It floored the inter-system distance under EVERY x for
    /// ink that exists at a FEW x — the X-aware silhouette already reserves the symbol where
    /// it actually is (<c>AddMarkBox</c> below), and the per-measure annotation extents price
    /// its real ink besides. Book CIB read 13.045000000 against LilyPond's 12.593884 with the
    /// band and 12.595000000 without it, so the band was worth +0.450000000 of pure
    /// over-reservation and nothing else. This is the lyric band's story exactly (2026-08-20,
    /// the scalar retired for the X-aware profile), and <c>CreatePages</c>' own remark named
    /// the trigger — "the chord row keeps this shape until a point measures it the same way".
    /// </para>
    /// <para>
    /// THE UP EXTENT COULD NOT BE OBSERVED DOWNWARD AT ALL, and that is why it went with no
    /// entry of its own rather than waiting for one. On a single staff the page anchor is
    /// <c>margin + max(6, header + upExtent + 2.0 + 1)</c>, so 3.0 made the floor candidate
    /// EXACTLY 6.000000 — a dead tie with top-system-spacing's basic-distance. Raising it
    /// moved the page one for one; LOWERING IT COULD NOT MOVE ANYTHING, so no reading could
    /// ever have watched it and the real chord ink already joins the same extents through
    /// <see cref="EnrichExtentsWithAnnotationProtrusions"/>' chord arm (ink-true since
    /// 2026-08-28). MEASURED rather than argued: the two arms were retired and swept
    /// SEPARATELY over all 572 tracked books, 0 moved either time, and rerender is 0/81 —
    /// with the ledger point moving +0.451116000 → +0.001116000 in the same run, which is
    /// what says the change reached the engine at all (HANDOFF 5.0 ⑸: a 0 read before a
    /// live poison is shown says nothing).
    /// </para>
    /// <para>
    /// ⇒ WHAT REMAINS BELOW IS THE MARK FAMILY AND THE VOLTA, still the unported species:
    /// hand-assembled constants whose LILYPOND-REF names the grob's outside-staff-priority
    /// rather than the number. They stand because no reading watches them yet.
    /// </para>
    /// <para>
    /// ★★★ AND THEY ARE ALL FOUR DOMINATED, MEASURED 2026-08-28 (session 273) — which
    /// CORRECTS scratch/p272/sweep-map.txt's reading of them. That survey found +0.5 moving
    /// nothing and +3.0 reaching three books and a snapshot, and concluded they are "ALIVE
    /// and merely dominated at realistic sizes, so a port must MEASURE, not delete". The
    /// asymmetry it was reading is the same tie this method's chord branch turned out to
    /// have: an estimate that is <c>Math.Max</c>'d against real ink can always be raised
    /// into visibility, and raising it says nothing about whether it is doing any work.
    /// ⇒ THE TEST THAT ANSWERS IT IS LOWERING. All four set to 0.0 at once: 0 of 572 tracked
    /// books moved, rerender 0/81, suite green, EVERY ledger point unmoved. The poison is
    /// live in the same file on the same day (rehearsal 3.0 → 6.0 moves both a titled and an
    /// untitled mark book), so this 0 is domination and not a missed poison (HANDOFF 5.0 ⑸).
    /// ⇒ THEY ARE THE BELOW-STAFF FOUR'S SPECIES after all (lyrics/dynamics/hairpin/figbass,
    /// retired 2026-07-30), not a port's. ⚠️ WHAT IS STILL MISSING BEFORE THEY CAN GO is what
    /// that retirement had and this measurement does not: a FLOOR ARGUMENT per constant —
    /// "no texture can bring the real ink under this number" — or an observer. "Dominated on
    /// today's corpus" is not "cannot bind". ⚠️ AND DELETION NEEDS USER APPROVAL (RULES §5.1),
    /// so it is not this session's to take.
    /// </para>
    /// </remarks>
    private static (double upExtent, double bandUp) EstimateAboveStaffExtents(
        ScoreTextMetrics fonts,
        ImmutableArray<MusicMarkItem> musicMarks,
        ImmutableArray<VoltaBracketItem> voltaBrackets,
        int startMeasure, int endMeasure)
    {
        double upExtent = 0;
        // ⚠️ ALWAYS 0 SINCE 2026-08-28 — the chord symbol was its only writer (see the
        // remarks), so the whole-line band a system can floor the inter-system skyline
        // distance with is now a channel nothing feeds, all the way down through
        // AugmentExtentsWithLooseLines → CreatePages' BandUp → PageLayouter → the
        // `bandUpNext > 0` arm of LayoutUtilities.InterSystemPairMinimum.
        // ⚠️ IT IS NOT RETIRED IN A COMMIT OF ITS OWN, and that is RULES §5.1 rather than
        // an oversight: an independent refactor commit trades the one-island-one-concern
        // rhythm for speculative tidying, and the shape §5.1 prescribes is to hang the
        // removal on the next act that touches this island. It also needs USER APPROVAL,
        // which §5.1 does NOT waive for an output-identity change — only the ledger POINT is
        // waived, and only against the three-part proof. ⇒ The next act here carries it.
        double bandUp = 0;

        // ⚠️ THERE IS NO BELOW-STAFF SIDE HERE ANY MORE — see the remarks. The four constants
        // that used to live here (lyrics, dynamics, hairpins, figured bass) were each a second
        // model of ink this engine places, and each went once a reading watched it. Two of
        // them differed in kind and the readings said which was which: the figure row's
        // OVER-reserved by +1.825204583 (deleting it moved two pages), the dynamic's and the
        // hairpin's were already DOMINATED by the real placed ink (deleting them moved
        // nothing). ⚠️ The hairpin's margin was only 0.04 and that was checked rather than
        // assumed: 3.540000 is the floor for a below-staff hairpin, so no texture can bring
        // the real ink under the 3.5 the branch offered and it cannot come back to life.

        // --- Above-staff elements (upExtent) ---

        if (!musicMarks.IsDefaultOrEmpty)
        {
            foreach (var mark in musicMarks)
            {
                if (mark.MeasureIndex < startMeasure || mark.MeasureIndex >= endMeasure)
                    continue;

                // The metronome mark rests at staff ink + its padding 0.8 and its ink
                // tops out at the \smaller note's stem; stacking can only lift it.
                // LILYPOND-REF: scm/define-grobs.scm:2346 MetronomeMark outside-staff-priority
                if (mark.Type == MusicMarkType.Tempo)
                {
                    var tInk = MetronomeMarkGeometry.Ink(fonts, mark.Text, mark.TempoText,
                        mark.TempoBeatUnit, mark.TempoDots, mark.SwingSubdivision);
                    upExtent = Math.Max(upExtent,
                        MetronomeMarkGeometry.QuietBaselineAboveMiddle(tInk.Bottom)
                        - EngravingDefaults.StaffMiddle + tInk.Top);
                }

                // LILYPOND-REF: scm/define-grobs.scm RehearsalMark.outside-staff-priority = 1500
                if (mark.Type == MusicMarkType.Rehearsal)
                    upExtent = Math.Max(upExtent, 3.0); // boxed rehearsal mark

                // LILYPOND-REF: scm/define-grobs.scm SectionLabel
                if (mark.Type == MusicMarkType.SectionLabel)
                    upExtent = Math.Max(upExtent, 3.5); // boxed section label

                // LILYPOND-REF: scm/define-grobs.scm SegnoMark/CodaMark
                if (mark.Type == MusicMarkType.Segno || mark.Type == MusicMarkType.Coda)
                    upExtent = Math.Max(upExtent, 2.5); // glyph above staff
            }
        }

        // LILYPOND-REF: scm/define-grobs.scm VoltaBracketSpanner.outside-staff-priority = 600
        if (!voltaBrackets.IsDefaultOrEmpty)
        {
            foreach (var vb in voltaBrackets)
            {
                if (vb.StartMeasureIndex < endMeasure && vb.EndMeasureIndex >= startMeasure)
                {
                    upExtent = Math.Max(upExtent, 2.0); // volta bracket height
                    break;
                }
            }
        }

        return (upExtent, bandUp);
    }

    /// <summary>
    /// Measures each system's REAL vertical protrusions from a preliminary
    /// annotation pass and max-merges them into the spacing extents. The
    /// provisional systems already carry final X geometry — only the page Y
    /// changes afterwards — so slurs, ties, tuplet brackets, marks and
    /// dynamics can be laid out, measured, and discarded; the final pass
    /// recomputes them against the re-spaced systems.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1070-1127
    /// build_system_skyline — page spacing reads COMPLETE system stencils
    /// (slurs, brackets, scripts included), not just note skylines.
    /// </remarks>
    /// <param name="rowsAboveFirstStaff">
    /// Per system, <see cref="RowsAboveFirstStaff"/>. The grobs below are anchored on the
    /// TOP STAFF's middle line; this is what puts that middle where the alignment actually
    /// placed it instead of at the nominal device 2.
    /// </param>
    private static void EnrichExtentsWithAnnotationProtrusions(
        ScoreTextMetrics fonts,
        List<(double upExtent, double downExtent)> perSystemExtents,
        ImmutableArray<SystemLayout> systems,
        AnnotationLayouts ann,
        ImmutableArray<TieLayout> ties,
        ImmutableArray<SlurLayout> slurs,
        IReadOnlyList<double> rowsAboveFirstStaff,
        IReadOnlyList<List<ImmutableArray<PedalEngraver.SolvedPedalLine>>>? pedalLines = null)
    {
        int n = Math.Min(perSystemExtents.Count, systems.Length);
        // ⚠️ NEGATIVE INFINITY, NOT 0. The up extent is SIGNED (LayoutUtilities'
        // CalculateUpExtent), so a system whose topmost ink stops below its own origin
        // carries a negative one; seeding this accumulator at 0 and max-merging would clamp
        // that back and hand the page the empty top of a chord row's band.
        double upSeed = double.NegativeInfinity;
        // The top staff's MIDDLE line in this pass's system-relative device frame. It is
        // the nominal 2 exactly while the topmost element is that staff; with a chord or
        // lyric row stacked over it the middle sits that much further down, and reading 2
        // anyway credits every mark with the rows' whole band.
        // LILYPOND-REF: lily/page-layout-problem.cc:1120-1122 up->raise(-first_spaceable_dy).
        double MiddleAt(int s) =>
            2.0 + (s < rowsAboveFirstStaff.Count ? rowsAboveFirstStaff[s] : 0);
        var up = new double[n];
        Array.Fill(up, upSeed);
        var down = new double[n];

        var measureToSystem = new Dictionary<int, int>();
        var bottoms = new double[n];
        for (int i = 0; i < n; i++)
        {
            foreach (var m in systems[i].Measures)
                measureToSystem[m.MeasureIndex] = i;
            // System bottom relative to its top: last visible staff's bottom
            // (4.0 for a single staff).
            bottoms[i] = 4.0;
            if (!systems[i].StaffGroups.IsDefaultOrEmpty)
            {
                foreach (var g in systems[i].StaffGroups)
                    foreach (var st in g.Staves)
                        if (!st.IsHidden)
                            bottoms[i] = Math.Max(bottoms[i], st.Height - st.Y);
            }
        }

        void Add(int measureIndex, double topRel, double bottomRel)
        {
            if (!measureToSystem.TryGetValue(measureIndex, out int s))
                return;
            AddAt(s, topRel, bottomRel);
        }

        // The same two maxima for a grob already attributed to its system (the solved
        // pedal lines arrive per system, not per measure).
        void AddAt(int s, double topRel, double bottomRel)
        {
            up[s] = Math.Max(up[s], -topRel);
            down[s] = Math.Max(down[s], bottomRel - bottoms[s]);
        }

        // The up half alone — for a grob whose DOWN reservation is somebody else's
        // (a note-bound lyric line, whose depth is its alignment minimum).
        // (`//` and not `///`: a local function carries no XML documentation — CS1587.)
        void AddUpOnly(int measureIndex, double topRel)
        {
            if (!measureToSystem.TryGetValue(measureIndex, out int s))
                return;
            up[s] = Math.Max(up[s], -topRel);
        }

        foreach (var t in ann.TupletBrackets)
        {
            // t.*YUp is Y-up from the system top; this pass is system-relative device.
            double startY = -t.StartYUp;
            double endY = -t.EndYUp;
            double hi = Math.Min(startY, endY);
            double lo = Math.Max(startY, endY);
            Add(t.MeasureIndex, hi - (t.IsStemUp ? 1.6 : 0.1), lo + (t.IsStemUp ? 0.7 : 1.7));
        }
        foreach (var v in ann.VoltaBrackets)
        {
            // YUp is Y-up from the system top; this extent pass is system-relative
            // device (down+), which is exactly -YUp.
            double vY = -v.YUp;
            Add(v.StartMeasureIndex, vY - 0.1, vY + 1.6);
        }
        foreach (var m in ann.MusicMarks)
        {
            if (MusicMarkItem.IsSpannerHandled(m.MarkType))
                continue;
            // m.YUp is Y-up above the top-staff middle; system-relative device
            // (down+ from the system top) is MiddleAt(s) − YUp.
            if (!measureToSystem.TryGetValue(m.MeasureIndex, out int ms))
                continue;
            double mY = MiddleAt(ms) - m.YUp;
            // The drawn ink's own envelope, from the one description the stacker and the
            // paging silhouette price the same mark by (mirroring DrawSingleMusicMark) —
            // NOT a flat [mY − 2.1, mY + 0.7]. That envelope stood here until 2026-08-27,
            // 0.8 of air over a boxed label's drawn top, and this scalar prices the FIRST
            // system's Y (CalculateFirstSystemY) and the page breaker's line heights, so
            // a marked first system opened 0.8 too low. The referee that measured it —
            // and this arm's falsifier — is page.section-label.first-staff-refpoint
            // (+0.822688 under the envelope, +0.022688 with the drawn box; the remaining
            // 0.022688 is the DRAW's own top against LilyPond's stencil, a different
            // island).
            var (_, _, emTop, emBottom) = OutsideStaffStacker.MusicMarkExtents(fonts, m);
            Add(m.MeasureIndex, mY - emTop, mY + emBottom);
        }
        foreach (var ct in ann.CustomTexts)
        {
            // ct.YUp is Y-up above the top-staff middle; same conversion as the marks.
            if (!measureToSystem.TryGetValue(ct.MeasureIndex, out int cs))
                continue;
            double ctY = MiddleAt(cs) - ct.YUp;
            // THE EXTENT IS THE STRING'S OWN INK ABOUT ITS BASELINE, at the size and style
            // the draw draws with and the stacker reserved with (one house) — which is also
            // LilyPond's model of the grob: a TextScript's Y-extent comes from its stencil
            // (scm/define-grobs.scm:3818 vertical-skylines from the stencil), and the page's
            // top spring charges that stencil's top. It was a scalar pair [ctY − 1.8,
            // ctY + 0.6] until 2026-08-28 — 0.75/0.25 of the em 2.4 the outside-staff
            // stacker's retired letter-class trio used, at an em the draw no longer has —
            // and that flat 1.8 stood 0.765955 over the drawn ink of a lower-case string
            // (ledger page.custom-text.first-staff-refpoint, measured against 2.26.0 before
            // this arm was changed; the capital half was 0.211526 over).
            var (ctBottom, ctTop) = fonts.Ink(ct.Text, EngravingDefaults.TextScriptFontSize,
                TextRole.Text, Rendering.FontStyle.Italic);
            Add(ct.MeasureIndex, ctY - ctTop, ctY - ctBottom);
        }
        // Chord names ride above the staff and rise (ChordNameEngraver skyline) to
        // clear high notes; their REAL text top must join the system up-extent or a
        // lifted chord line pokes into the header/title.
        // THE EXTENT IS THE SYMBOL'S OWN INK BOX — the same SymbolInk the reservation
        // and the draw read (one house), which is also LilyPond's model of the grob:
        // a ChordName declares no vertical-skylines, so its skyline IS its extent box
        // (lily/grob.cc:81-85 fills the default with
        // Grob::simple_vertical_skylines_from_extents_proc), and the page's pure
        // height reads those extents. It was a scalar pair [cnY − 1.9, cnY + 0.3]
        // until 2026-08-28 — HANDOFF §7.7's flat box beside real ink, measured
        // DOMINATED wherever a chord row reserves (ledger page.grand-chords.*, the
        // guards this arm now answers to).
        foreach (var cn in ann.ChordNames)
        {
            // cn.YUp is Y-up from the system top; the system-relative device Y (old
            // cn.Y) is its negation.
            double cnY = -cn.YUp;
            var (cnBottom, cnTop) = ChordNameEngraver.SymbolInk(fonts, cn.ChordText);
            Add(cn.MeasureIndex, cnY - cnTop, cnY - cnBottom);
        }
        // Lyric text (staff-bound AND row): the ascender rises ~2.11 ss above
        // the baseline at the 3.2 ss lyric font — without it, a first system
        // whose top content is a lyrics/chord ROW grazes the title ink.
        //
        // ⚠️ THE DOWN HALF IS THE ROW'S ONLY. A note-bound line's down reservation is the
        // ALIGNMENT MINIMUM (LyricReservationBelowSystem), not the distance it is DRAWN —
        // and this pass sees the DRAWN one, laid out at force 0, i.e. at
        // nonstaff-relatedstaff-spacing's basic-distance 5.500000. LilyPond reserves the
        // minimum: page-layout-problem.cc:593-599 hands build_system_skyline the minimum
        // translations, and align-interface.cc:235-238 adds basic-distance only behind the
        // pure branch, which that call is not.
        //
        // ⚠️ IT USED TO ADD BOTH, AND THE DRAWN ONE WON. MEASURED 2026-07-27 by
        // perturbation: suppressing this down half for non-row lines moved 13 snapshots
        // (07-lead-sheet, 08-chorale and 11 test/lyrics-*) and no ledger entry, while
        // zeroing the alignment-minimum band moved a DISJOINT set (two system-gap entries
        // and test/lyrics-volta). So the two models bound on different books and the
        // drawn one silently overrode the ported one wherever they met.
        //
        // ⚠️ A LYRICS ROW KEEPS ITS DRAWN EXTENT, AND THAT IS LILYSHARP-OWN, not a second
        // reading of LilyPond. To LilyPond a row is a loose line like any other and its
        // reservation is the same alignment minimum; Lily# places it as an independent
        // staff-like BAND instead (HANDOFF 3, a decided divergence), so it has no alignment
        // minimum to prefer — the drawn extent is the only figure that exists for it.
        // ⚠️ THE REVISIT CAME (2026-08-19, "score = a vertical stack of bands") AND
        // NARROWED THIS BRANCH INSTEAD OF REMOVING IT: a bound row standing directly
        // below its own staff no longer reaches here — RenderSpecParser.FoldAdjacentRows
        // turns it into the staff's attachment before layout, measured byte-identical
        // to the old `with lyrics` clause. What still takes this branch is the
        // STANDALONE row — the lead sheet, and the part sheet carrying another part's
        // words — which LilyPond cannot spell without a staff to hang it on, so the
        // band reading stays the intentional divergence for exactly that remit.
        // Its UP half is kept for every line — a first system whose top content is a
        // lyrics/chord row would otherwise graze the title ink. ⚠️ For a note-bound line the
        // up half is INERT (the line sits below the staff, so 2.11 - lyY is negative); it is
        // called anyway so the two branches read as one rule with one exception, and so that
        // a future line placed ABOVE its staff is not silently dropped.
        foreach (var lyLay in ann.Lyrics)
        {
            // lyLay.YUp is Y-up from the system top; the system-relative device
            // baseline (old lyLay.Y) is its negation.
            double lyY = -lyLay.YUp;
            if (lyLay.Item.IsLyricsRow)
                Add(lyLay.Item.MeasureIndex, lyY - 2.11, lyY + 0.9);
            else
                AddUpOnly(lyLay.Item.MeasureIndex, lyY - 2.11);
        }
        foreach (var tr in ann.TrillSpanners)
        {
            // tr.YUp is Y-up from the system top; this pass is system-relative device.
            // The "tr" glyph rides stencil-offset (0 . -1) below the LINE tr.YUp
            // anchors (DrawTrillSpanners), so a glyph-bearing piece's drawn ink is
            // (glyphTop − offset) up and offset down — LilyPond's own ext (-1.0 . 1.1)
            // — and a glyphless continuation carries just the line, whose ink is the
            // element run's own reach either side (TrillWaveOutline.InkReach — the same
            // house the profile and the drawing read, so this coarse extent cannot drift
            // from them).
            bool trHasGlyph = tr.GlyphX < tr.LineStartX;
            double trWave = TrillWaveOutline.InkReach;
            double trY = -tr.YUp;
            Add(tr.StartMeasureIndex,
                trY - (trHasGlyph
                    ? GlyphMetrics.OrnTrillGlyph.Top - EngravingDefaults.TrillSpannerTextOffsetDown
                    : trWave),
                trY + (trHasGlyph ? EngravingDefaults.TrillSpannerTextOffsetDown : trWave));
        }
        // Figured-bass rows hang below the staff; a skyline-dropped row must
        // widen the gap to the NEXT system, or its digits print through that
        // system's volta boxes / high notes (showcase/04).
        foreach (var fb in ann.FiguredBasses)
        {
            // YUp is Y-up; this extent pass is system-relative device, so reconstruct
            // against this figure's own staff offset (0 for a single/top staff).
            double fbOff = measureToSystem.TryGetValue(fb.MeasureIndex, out int fbSys)
                ? LayoutUtilities.StaffOffsetInSystemDown(systems[fbSys], fb.StaffIndex)
                : 0;
            double fbY = fbOff + (2.0 - fb.YUp);
            Add(fb.MeasureIndex,
                fbY - FiguredBassEngraver.FigureInkTop(
                    fb.FigureTexts.Length > 0 ? fb.FigureTexts[0] : string.Empty),
                fbY + BassFigureAlignment.ColumnDepth(fb.RowOffsets, fb.FigureTexts));
        }
        // The pedal bracket under a staff — the SAME box the staff's down profile was solved
        // with and the X-aware arm below merges (PedalEngraver.BracketStencilBox), for the
        // scalar the breaker and the rows-only fallback read. Per staff, like the figures:
        // Y-up about that staff's middle, so the same StaffOffsetInSystemDown step.
        if (pedalLines is not null)
        {
            for (int s = 0; s < n && s < pedalLines.Count; s++)
            {
                var staves = pedalLines[s];
                for (int staffIndex = 0; staffIndex < staves.Count; staffIndex++)
                {
                    if (staves[staffIndex].IsDefaultOrEmpty)
                        continue;
                    double pdOff = LayoutUtilities.StaffOffsetInSystemDown(systems[s], staffIndex);
                    foreach (var line in staves[staffIndex])
                    {
                        var (_, _, pdBottom, pdTop) =
                            PedalEngraver.BracketStencilBox(line.StartX, line.EndX, line.LineYUp);
                        AddAt(s, pdOff + (2.0 - pdTop), pdOff + (2.0 - pdBottom));
                    }
                }
            }
        }
        // Note-bound scripts (a fermata over the top staff, a staccatissimo
        // under the bottom) extend the system silhouette like any other
        // annotation; Ink is the glyph's real box about its anchor (Y-up).
        foreach (var a in ann.Articulations)
        {
            // a.YUp is Y-up above the staff middle; system-relative device is 2 − YUp.
            double aY = 2.0 - a.YUp;
            Add(a.MeasureIndex, aY - a.Ink.Top, aY - a.Ink.Bottom);
        }
        foreach (var d in ann.Dynamics)
        {
            // d.YUp is Y-up above the staff middle; system-relative device is 2 − YUp.
            // The label's OWN ink, from the font, per glyph — the same house the placement
            // and the stacker read (DynamicEngraver.InkOf; free @text falls back there).
            // ⚠️ THIS SITE WAS MISSED when the three other spellings were unified on it: it
            // kept a flat 1.2 / 0.3 box, and 0.3 against the `f` glyph's real 0.692002 is why
            // audit/lp-geometry dynamic.page.{quiet,deep} opened at -0.412774 and -0.390489,
            // i.e. a page that ends closer under its own ink than LilyPond's does.
            double dY = 2.0 - d.YUp;
            var (dAscent, dDescent) = DynamicEngraver.InkOf(d.Text, d.IsExpressiveText);
            Add(d.MeasureIndex, dY - dAscent, dY + dDescent);
        }
        foreach (var h in ann.Hairpins)
        {
            // h.YUp is Y-up from the system top; this pass is system-relative device.
            // The DRAWN wedge: its arms sit at the layout's own openings (a half-height,
            // capped by HairpinEngraver.Height — which carries the LilyPond citation for that
            // number, and citing it twice is how a second address gets to be wrong) and the
            // rule adds half its thickness, which is exactly the two lines
            // SharedRenderer.DrawHairpins puts on the page. The flat 0.34 it replaces was
            // about half of that (ledger hairpin.page.quiet, -0.543200).
            // ⚠️ LILYSHARP-OWN: THE MAX FOLD. LilyPond's Hairpin carries
            // `vertical-skylines` from its STENCIL, so its profile is the wedge itself and
            // narrows to the apex; this reserves the WIDEST half-height across the whole
            // span, because the pass it feeds registers one box per measure for every
            // annotation class. It can only over-reserve (near the point), never under. It
            // goes when this pass registers outlines pointwise — the island the script,
            // clef and trill seeds already closed on their own side.
            // ⚠️ NO POINT OBSERVES THE FOLD: audit/lp-geometry hairpin.page.quiet reads the
            // DEEPEST ink under the staff, which is the max either way. The pair that would
            // see it is a hairpin whose apex sits under something tall.
            double hY = -h.YUp;
            double hHalf = Math.Max(h.StartOpening, h.EndOpening)
                + EngravingDefaults.StaffLineThickness / 2.0;
            Add(h.StartMeasureIndex, hY - hHalf, hY + hHalf);
        }
        foreach (var sp in ann.TextSpanners)
        {
            // sp.YUp is Y-up from the system top; this pass is system-relative device.
            // Drawn ink about the line: the dashed rule's half thickness both ways,
            // widened by the text's own ink on the piece that carries it — the same
            // extents OutsideStaffStacker.PlaceTextSpanners registers (the old flat
            // 1.2 / 0.3 box was an invention; the 0.3 descent was ledger
            // textspanner.support.staff-to-line's whole +0.25).
            double lineHalf = EngravingDefaults.StaffLineThickness / 2.0;
            double spTop = lineHalf, spBottom = lineHalf;
            if (!string.IsNullOrEmpty(sp.Text))
            {
                var ink = fonts.Ink(
                    sp.Text, TextSpannerEngraver.TextFontSize, TextRole.Text,
                    Rendering.FontStyle.Italic);
                spTop = Math.Max(spTop, ink.Top);
                spBottom = Math.Max(spBottom, -ink.Bottom);
            }
            double spY = -sp.YUp;
            Add(sp.StartMeasureIndex, spY - spTop, spY + spBottom);
        }
        foreach (var bn in ann.BarNumbers)
        {
            if (!measureToSystem.TryGetValue(bn.MeasureIndex, out int s))
                continue;
            // bn.YUp is Y-up from the system top; the system-relative device value
            // (the old bn.Y - system.Y) is just -YUp.
            double rel = -bn.YUp;
            // The digits' OWN ink over their baseline, from the face — the same face and the
            // same call the WIDTH beside this already uses. It was a bare 1.3 until
            // 2026-07-28, which is a cap height nothing states: LilyPond reserves what the
            // glyphs draw, since a BarNumber's vertical-skylines come from its stencil.
            // LILYPOND-REF: lily/grob.cc:85-89 simple_vertical_skylines_from_extents — a text
            // grob's extent IS its stencil's, so there is no designed box to round up to.
            // MEASURED (audit/lp-geometry/probes/page-vertical.ly, books BNL/BNH): LilyPond
            // puts the baseline 3.076208 over the staff refpoint and the ink top at 4.305433,
            // i.e. 1.229225 — against the 1.3 this used to reserve. Closing it took
            // system.clef-floor.floor-bound-distance to exact and lyrics.*.system-gap from
            // +0.207200 to +0.143468.
            double capTop = fonts.Ink(
                bn.Text, BarNumberEngraver.FontSize,
                TextRole.BarNumber, Rendering.FontStyle.Bold).Top;
            up[s] = Math.Max(up[s], -(rel - capTop));
        }

        // Ties and slurs now store WITHIN-SYSTEM device Y (step 2d), so the negated
        // bow Y each caller passes is already system-relative — no system.Y subtraction.
        void AddCurve(int measureIndex, double y0, double y1, double c1, double c2)
        {
            if (!measureToSystem.TryGetValue(measureIndex, out int s))
                return;
            // Curve extreme ~ 3/4 of the way from endpoints to controls.
            double topRel = Math.Min(Math.Min(y0, y1), Math.Min(y0, y1) * 0.25 + Math.Min(c1, c2) * 0.75);
            double botRel = Math.Max(Math.Max(y0, y1), Math.Max(y0, y1) * 0.25 + Math.Max(c1, c2) * 0.75);
            up[s] = Math.Max(up[s], -topRel);
            down[s] = Math.Max(down[s], botRel - bottoms[s]);
        }

        foreach (var t in ties)
        {
            // A broken tie's continuation piece (IsBrokenLeft) lives on a LATER
            // system at that system's Y — attribute its extent to the system holding
            // its END, or its low Y leaks onto the start system and forces a huge
            // inter-system gap.
            int mi = t.IsBrokenLeft ? t.Tie.EndMeasureIndex : t.Tie.StartMeasureIndex;
            // Bow Y is now page Y-up (= -device); reflect back for this device extent pass.
            AddCurve(mi, -t.StartYUp, -t.EndYUp, -t.Control1.Y, -t.Control2.Y);
        }
        foreach (var sl in slurs)
        {
            int mi = sl.IsBrokenLeft ? sl.Slur.EndMeasureIndex : sl.Slur.StartMeasureIndex;
            AddCurve(mi, -sl.StartYUp, -sl.EndYUp, -sl.Control1.Y, -sl.Control2.Y);
        }

        for (int i = 0; i < n; i++)
        {
            var ext = perSystemExtents[i];
            perSystemExtents[i] = (
                Math.Max(ext.upExtent, up[i]),
                Math.Max(ext.downExtent, down[i]));
        }
    }

    /// <summary>
    /// The ONE spelling of the script family's attribution and anchor: which system a
    /// note-bound script's ink belongs to, and its anchor in that system's Y-up frame.
    /// Both consumers of a script-augmented skyline append through here — the paging
    /// augment (<see cref="AugmentSkylinesForPaging"/>) and the final annotation pass's
    /// lyric support (<see cref="AugmentSkylinesWithScripts"/>).
    /// </summary>
    /// <remarks>
    /// ArticulationLayout.YUp is Y-up (staff-spaces above the staff middle); the system
    /// skyline is Y-up too (system-top origin). Translate against this staff's
    /// system-local middle, and take the offset in the SAME frame so the whole line adds —
    /// the middle sits half a staff BELOW the staff top, which in Y-up subtracts. Ink
    /// Top/Bottom stay up-positive, so they ADD. This is the same expression as
    /// OutsideStaffStacker's articulation branch; the two used to be one Y-up and one
    /// Y-down spelling of it. The merge itself is the Script grob's one profile (the
    /// padded outline) — see MergeScriptProfile's remark for what per-consumer copies cost.
    /// </remarks>
    private static void AppendScriptSteps(
        ImmutableArray<ArticulationLayout> articulations,
        ImmutableArray<SystemLayout> systems,
        Dictionary<int, int> measureToSystem,
        Func<int, PagingAugmentProgram.Builder> builderAt)
    {
        if (articulations.IsDefaultOrEmpty)
            return;
        foreach (var a in articulations)
        {
            if (!measureToSystem.TryGetValue(a.MeasureIndex, out int sysIdx))
                continue;
            var sys = systems[sysIdx];
            double staffMidUp = LayoutUtilities.StaffMiddleUpInSystem(sys, a.StaffIndex);
            builderAt(sysIdx).AddScript(a, a.YUp + staffMidUp);
        }
    }

    /// <summary>
    /// Returns per-system skylines with the scripts' ink merged in — the final annotation
    /// pass's consumer of the script family (the lyric rows drop below these). The input
    /// skylines are NOT mutated; non-augmented systems reuse the originals. The steps come
    /// from <see cref="AppendScriptSteps"/>, the same one spelling the paging augment
    /// consumes, and are replayed by <see cref="PagingAugmentProgram.Execute"/> in the
    /// same one-wrapper-per-script association the family loop always had.
    /// </summary>
    private static IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)>? AugmentSkylinesWithScripts(
        IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)>? systemSkylines,
        ImmutableArray<ArticulationLayout> articulations,
        ImmutableArray<SystemLayout> systems)
    {
        if (systemSkylines == null || articulations.IsDefaultOrEmpty)
            return systemSkylines;

        var measureToSystem = new Dictionary<int, int>();
        for (int s = 0; s < systems.Length && s < systemSkylines.Count; s++)
            foreach (var m in systems[s].Measures)
                measureToSystem[m.MeasureIndex] = s;

        var builders = new PagingAugmentProgram.Builder?[systemSkylines.Count];
        AppendScriptSteps(articulations, systems, measureToSystem,
            s => builders[s] ??= new PagingAugmentProgram.Builder());

        return new LazyScriptAugmentedSkylines(systemSkylines, builders);
    }

    /// <summary>
    /// The script-augmented per-system skylines, each system's merge run on FIRST ACCESS —
    /// the same values <see cref="AugmentSkylinesWithScripts"/> always returned, priced per
    /// system that somebody actually reads.
    /// </summary>
    /// <remarks>
    /// ⚠️ MEASURED, and it is why this is a list and not an eager array. The three consumers
    /// are the rows that drop below the staff (lyrics, figured bass, chord names), and a
    /// score can carry a thousand staccati with none of them — a piano piece, and every
    /// synthetic script book in audit/lpreg. On those the whole augment was built and then
    /// read by nobody: 840.5 MB of perf-fingstack1k's 2191.7 MB keystroke and 299.7 MB of
    /// perf-scripts1k's 698.1 MB (session 191, Release, allocation — deterministic where
    /// time is not).
    /// <para>
    /// ⚠️ A LIST AND NOT A GUARD AT THE CALL SITE, deliberately. A guard would have to name
    /// the consumers ("if there are no lyrics and no figures and no chord names, skip"), and
    /// the fourth consumer to arrive would silently get UN-augmented skylines — a lyric row
    /// engraved over a marcato, with nothing red. Demand-driven, a new consumer is correct by
    /// construction: reading the list is what builds it.
    /// </para>
    /// <para>
    /// Identity is preserved both ways: a system with no script steps hands back the caller's
    /// own instance (as the eager array did), and a system that is read twice gets the same
    /// augmented instance rather than a second merge.
    /// </para>
    /// </remarks>
    private sealed class LazyScriptAugmentedSkylines
        : IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)>
    {
        private readonly IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)> _base;
        private readonly PagingAugmentProgram.Builder?[] _builders;
        private readonly (VerticalSkyline up, VerticalSkyline down)?[] _done;

        public LazyScriptAugmentedSkylines(
            IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)> baseline,
            PagingAugmentProgram.Builder?[] builders)
        {
            _base = baseline;
            _builders = builders;
            _done = new (VerticalSkyline up, VerticalSkyline down)?[baseline.Count];
        }

        public int Count => _base.Count;

        public (VerticalSkyline up, VerticalSkyline down) this[int index]
        {
            get
            {
                if (_done[index] is { } cached)
                    return cached;
                var value = _builders[index] is { } b
                    ? b.Build().Execute(_base[index])
                    : _base[index];
                _done[index] = value;
                return value;
            }
        }

        public IEnumerator<(VerticalSkyline up, VerticalSkyline down)> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
                yield return this[i];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }

    /// <summary>
    /// Paging skylines: the per-system skylines plus the annotation ink that
    /// hangs outside the staves — note-bound scripts (both directions) and
    /// figured-bass rows. The optimal page-stacking path spaces systems by
    /// skyline DISTANCE (PageLayouter), so anything missing from these
    /// silhouettes can print into the neighbouring system (showcase/04:
    /// figured-bass digits through the next system's volta boxes).
    /// LILYPOND-REF: lily/page-layout-problem.cc build_system_skyline — LP's
    /// paging skylines contain every grob of the system.
    /// </summary>
    /// <remarks>
    /// Restructured system-major (session 142): each family loop APPENDS its per-system
    /// steps, with every merge argument resolved, to that system's
    /// <see cref="PagingAugmentProgram"/> instead of merging on the spot; the program then
    /// replays the identical merge sequence per system (same family order, same
    /// within-family array order, same one-wrapper-per-step association — see the program's
    /// remarks for why the association is load-bearing). That makes one system's augment a
    /// pure function of (its base skyline pair, its program), which is what
    /// <see cref="SystemLayoutCache.GetOrComputePagingAugment"/> memoizes: on a keystroke,
    /// only systems whose base skyline instance or resolved annotation ink changed
    /// re-merge. MEASURED (session 142, Release): the merge was 209.5 ms of a 746.9 ms
    /// v2bow1k keystroke, ~all of it in unchanged systems' bow re-seeding.
    /// </remarks>
    private static List<(VerticalSkyline up, VerticalSkyline down)>? AugmentSkylinesForPaging(
        ScoreTextMetrics fonts,
        List<(VerticalSkyline up, VerticalSkyline down)>? skylines,
        ImmutableArray<ArticulationLayout> articulations,
        ImmutableArray<FiguredBassLayout> figuredBasses,
        ImmutableArray<VoltaBracketLayout> voltaBrackets,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MusicMarkLayout> musicMarks = default,
        ImmutableArray<CustomTextLayout> customTexts = default,
        ImmutableArray<ChordNameLayout> chordNames = default,
        ImmutableArray<DynamicLayout> dynamics = default,
        ImmutableArray<BarNumberLayout> barNumbers = default,
        ImmutableArray<TupletBracketLayout> tupletBrackets = default,
        ImmutableArray<SlurLayout> slurs = default,
        ImmutableArray<TieLayout> ties = default,
        ImmutableArray<TextSpannerLayout> textSpanners = default,
        SystemLayoutCache? systemCache = null,
        IReadOnlyList<VerticalSkyline?>? lyricBands = null,
        IReadOnlyList<List<ImmutableArray<PedalEngraver.SolvedPedalLine>>>? pedalLines = null)
    {
        if (skylines == null)
            return null;
        int systemCount = skylines.Count;

        var measureToSystem = new Dictionary<int, int>();
        for (int s = 0; s < systems.Length && s < systemCount; s++)
            foreach (var m in systems[s].Measures)
                measureToSystem[m.MeasureIndex] = s;

        var builders = new PagingAugmentProgram.Builder?[systemCount];
        PagingAugmentProgram.Builder BuilderAt(int s) => builders[s] ??= new();

        // NOTE-BOUND SCRIPTS, both directions — above scripts into the UP skyline, below
        // scripts into the DOWN one. LilyPond's axis-group skyline contains the note-bound
        // scripts, so anything spaced against it (the chord-name line above, the
        // figured-bass row below) clears a staccato or fermata over a protruding note;
        // these system skylines are built before script layout exists, hence this second
        // pass. Inner-staff scripts merge harmlessly (the system silhouette already
        // dominates them). LILYPOND-REF: lily/axis-group-interface.cc:359-474 — grobs
        // without outside-staff-priority stay in the support skyline.
        AppendScriptSteps(articulations, systems, measureToSystem, BuilderAt);

        // A tuplet bracket is ordinary ink inside its staff's axis group in LilyPond, so
        // the next system has to clear it exactly as it clears the notes. This skyline is
        // the one the PAGE spaces systems by; MultiStaffLayouter seeds the other one, the
        // per-staff skyline Align_interface reads, and seeding only that left the bracket
        // reserved between staves and not between systems.
        // (EnrichExtentsWithAnnotationProtrusions does add tuplets to the scalar extents,
        // but those are only the fallback for an EMPTY skyline — see CreatePages — so they
        // never decide anything here.)
        // LILYPOND-REF: scm/define-grobs.scm TupletBracket carries vertical-skylines from
        //   its stencil and sets no outside-staff-priority, so axis-group-interface keeps
        //   it inside; lily/page-layout-problem.cc:1070-1127 build_system_skyline spaces
        //   pages by the COMPLETE system stencil.
        if (!tupletBrackets.IsDefaultOrEmpty)
        {
            foreach (var group in tupletBrackets.GroupBy(t => t.MeasureIndex))
            {
                if (!measureToSystem.TryGetValue(group.Key, out int s))
                    continue;
                // *YUp here IS the system frame (the annotation pass baked the staff
                // offset in through staffYAt), which is this skyline's own frame.
                // staffTopUp 0: the SYSTEM skyline's origin is the top staff's top line and
                // these layouts are already in it (the annotation pass baked the staff offset
                // in), so there is no half-staff to close here. The PER-STAFF seeding passes a
                // real one, because that skyline is about the staff's reference point.
                // ⚠️ StaffSize.FullSize for the same reason staffTopUp is 0: these layouts
                // arrive in the SYSTEM's frame and units, the annotation pass having baked
                // both in, so sizing them again here would apply the magnification twice.
                // ⚠️ WHAT THAT LEAVES OPEN, named rather than hidden: the annotation pass
                // does not know about magnification either, so an ossia's bracket reaching
                // THIS path is reserved full size. It is the same unit question as
                // SkylineBuilder's, one frame further out, and it goes when the annotation
                // layouts carry the staff they belong to.
                BuilderAt(s).AddTupletGroup(group.ToImmutableArray());
            }
        }

        // A slur is the same kind of inside-staff grob as the tuplet bracket -- it carries
        // vertical-skylines from its stencil and sets no outside-staff-priority
        // (scm/define-grobs.scm Slur), so the next system must clear its bow exactly as it
        // clears the notes. MultiStaffLayouter.BuildAllStaffSkylines seeds the OTHER skyline
        // (the per-staff one Align_interface reads); this is the one the PAGE spaces systems
        // by, and until now it reserved the bow nowhere between systems. The bow's *YUp is the
        // WITHIN-SYSTEM Y-up the prelim scorer produced (staffMiddleDown is the within-system
        // staff offset), the same frame AddTupletBracketsToSkyline arrives in, so it seeds
        // without a further offset -- once ElementCoordinator.LayoutSlurs stopped letting a
        // slur on one system collide with one on another (which had drifted each system's bow
        // deeper). Attribution to a system mirrors EnrichExtentsWithAnnotationProtrusions: a
        // broken continuation piece belongs to the system holding its END.
        // audit/lp-geometry system.slur-{under,over}-notes.
        if (!slurs.IsDefaultOrEmpty)
        {
            // staffTopUp 0 and FullSize — the system frame and its units again, as for
            // the brackets above, and open in the same way for an ossia. One bow-group
            // step per system, the bows in array order, exactly the old GroupBy's slice.
            var slursBySystem = new List<SlurLayout>?[systemCount];
            foreach (var sl in slurs)
            {
                if (measureToSystem.TryGetValue(
                        sl.IsBrokenLeft ? sl.Slur.EndMeasureIndex : sl.Slur.StartMeasureIndex, out int s))
                    (slursBySystem[s] ??= new List<SlurLayout>()).Add(sl);
            }
            for (int s = 0; s < systemCount; s++)
                if (slursBySystem[s] is { } sysSlurs)
                    BuilderAt(s).AddBowGroup(sysSlurs);
        }

        // A tie is the same inside-staff grob as the slur one line up -- vertical-skylines from
        // its stencil, no outside-staff-priority (scm/define-grobs.scm Tie) -- so the next
        // system must clear its bow exactly as it clears the notes. SkylineBuilder.BuildStaffSkylines
        // seeds the tie into the OTHER skyline (the per-staff one Align_interface reads, which
        // staff.staff.tie-{under,over}-notes measure); this is the one the PAGE spaces systems
        // by, and until now it reserved the bow nowhere between systems -- the hole the slur had
        // before it was seeded here. Unlike the slur, the tie carries no cross-system collision
        // term (TieFormattingProblem scores each bow against its own notes, with no existingSlurs
        // analogue), so no LayoutTies fix is needed first. Attribution to a system mirrors the
        // slur: a broken continuation piece belongs to the system holding its END.
        // audit/lp-geometry system.tie-{under,over}-notes.
        if (!ties.IsDefaultOrEmpty)
        {
            // Same shape as the slurs — a tie's bow group comes AFTER the slur group for
            // every system, which is the family order the old loops produced.
            var tiesBySystem = new List<TieLayout>?[systemCount];
            foreach (var t in ties)
            {
                if (measureToSystem.TryGetValue(
                        t.IsBrokenLeft ? t.Tie.EndMeasureIndex : t.Tie.StartMeasureIndex, out int s))
                    (tiesBySystem[s] ??= new List<TieLayout>()).Add(t);
            }
            for (int s = 0; s < systemCount; s++)
                if (tiesBySystem[s] is { } sysTies)
                    BuilderAt(s).AddBowGroup(sysTies);
        }

        foreach (var fb in figuredBasses)
        {
            if (!measureToSystem.TryGetValue(fb.MeasureIndex, out int s))
                continue;
            double half = FiguredBassEngraver.MinFigureBoxWidth;
            // YUp is Y-up; this inter-system skyline is Y-up too (system-top origin), so
            // take the figure's own staff offset in that frame as well and the line adds.
            // The staff middle is half a staff below the staff top, hence the StaffMiddle
            // subtraction; the figure column then extends downward (smaller Y-up).
            double fbStaffOffsetUp = LayoutUtilities.StaffOffsetInSystemUp(systems[s], fb.StaffIndex);
            double fbY = fb.YUp - EngravingDefaults.StaffMiddle + fbStaffOffsetUp;
            double top = fbY + FiguredBassEngraver.FigureInkTop(
                fb.FigureTexts.Length > 0 ? fb.FigureTexts[0] : string.Empty);
            double bottom = fbY - BassFigureAlignment.ColumnDepth(fb.RowOffsets, fb.FigureTexts);
            BuilderAt(s).AddFiguredBassBox(fb.X - half, fb.X + half, bottom, top);
        }

        // Volta brackets and their "End1"-style label boxes rise above the
        // staff: without them in the UP silhouette, a previous system's
        // figured-bass digits settle onto the boxes.
        foreach (var v in voltaBrackets)
        {
            if (!measureToSystem.TryGetValue(v.StartMeasureIndex, out int s))
                continue;
            // YUp is Y-up from the system top; this skyline is Y-up too, so use it directly.
            double vY = v.YUp;
            BuilderAt(s).AddVoltaBox(v.StartX, v.EndX, vY - 1.6, vY + 0.1);
        }

        // Section labels, rehearsal marks and navigation text (Fine, D.C. …)
        // stack above (or below) the staff like any other annotation. Without
        // their boxes in the silhouette, the X-aware inter-system distance let
        // a label above system 2 print through system 1's figured bass. The
        // box is added to BOTH sides — merging on the side the mark does not
        // protrude toward is a no-op, so no direction bookkeeping is needed.
        // LILYSHARP-OWN: the SILHOUETTE margin, not ink. It widens a mark's box before the
        // inter-system distance reads it, so a label whose ink only just clears the system
        // below still keeps a hair of air; LilyPond gets the same effect from measuring
        // outlines pointwise with outside-staff-padding, which this X-aware box stands in
        // for. It departs from lily/axis-group-interface.cc:45
        // default_outside_staff_padding_ = 0.46 in being applied HORIZONTALLY to a box
        // rather than as a clearance between two skylines, and it disappears when this
        // silhouette carries outlines instead of boxes. NOTHING OBSERVES IT TODAY: no
        // ledger point reads an inter-system distance driven by a mark box.
        // ⚠️ It was spelled twice as a literal 0.4 in the loop below before 2026-08-18, and
        // naming it is what made the tempo arm's second defect visible — a margin is added
        // to an EXTENT, and the tempo's extent is left-anchored, so "half width plus 0.4"
        // could never have been the same sentence for both.
        const double MarkSilhouetteMargin = 0.4;

        void AddMarkBox(int measureIndex, double x0, double x1, double top, double bottom)
        {
            if (!measureToSystem.TryGetValue(measureIndex, out int s))
                return;
            BuilderAt(s).AddMarkBox(x0, x1, bottom, top);
        }
        // Y-up from the system's ORIGIN down to the top line of the staff a score-context
        // grob resolves to — the step between the frame such a grob's YUp is stored in and
        // the frame this skyline is in. Zero for every system whose first element is a
        // staff, which is why the two frames read alike everywhere else.
        // ⚠️ THE SAME RESOLUTION THE DRAW MAKES, and for the same reason: the `-1` sentinel
        // on a score-context layout means THE TOP STAFF, and left unresolved it falls
        // through StaffOffsetInSystemUp's `staffIndex >= 0` guard to 0 — the system top,
        // which is a staff's top line only while the system opens on a staff. See
        // SharedRenderer.DrawMusicMarks, which closed the drawing half of this in session
        // 243 after a user report: with a chords row leading, the mark was DRAWN a whole
        // row band above the line its room had been reserved on.
        double ScoreGrobStaffTopUp(int s, int staffIndex)
            => LayoutUtilities.StaffOffsetInSystemUp(
                systems[s], LayoutUtilities.ResolveScoreGrobStaff(systems[s], staffIndex));
        if (!musicMarks.IsDefaultOrEmpty)
        {
            foreach (var m in musicMarks)
            {
                if (MusicMarkItem.IsSpannerHandled(m.MarkType))
                    continue;
                // The system this mark's box will be merged into — resolved HERE rather than
                // only inside AddMarkBox because the box's own vertical frame is that
                // system's. A mark on another page has none, and AddMarkBox would drop it.
                if (!measureToSystem.TryGetValue(m.MeasureIndex, out int ms)
                    || ms >= systems.Length)
                    continue;
                // The WIDTH is the mark's own extent,
                // read from the one home — MusicMarkEngraver.MarkXExtent, which answers per
                // TYPE: the metronome mark's laid-out ink (left-anchored), a boxed label's
                // string at its own boxed size and weight plus LilyPond's markup box
                // padding, a segno/coda glyph's square, and a plain-text mark's advance at
                // the size and style the draw uses.
                //
                // ⚠️⚠️ ★ THIS SITE USED TO SPELL ALL FOUR ITSELF, and only one of the four
                // was right (2026-08-18, session 204). Session 203 collapsed the PLAIN-TEXT
                // arm into the one home and left the sentence around it — "the size and
                // style come from the one home" — which reads as if the whole site had
                // moved. It had not:
                //   * a TEMPO was priced as the advance of its text at the plain-text em,
                //     and then CENTRED on an anchor the draw treats as a left edge;
                //   * a boxed SECTION LABEL or REHEARSAL mark, drawn 2.2/2.4 BOLD inside a
                //     box, was priced at the plain-text 2.8 BOLD ITALIC;
                //   * a segno/coda glyph got a hand-written 1.0 half-width beside the 1.2
                //     the same glyph is given everywhere else.
                // FOUND by porting the plain-text em: eighteen books moved and thirteen of
                // them carry no plain-text mark at all — they carry `form main { Intro A1
                // … }`, i.e. section labels, riding a constant that has nothing to do with
                // them. HANDOFF §7.6: a comment claiming N spellings were unified does not
                // make the site exhaustive; count the arms.
                var (mx0, mx1) = MusicMarkEngraver.MarkXExtent(fonts, m, m.X);
                // The margin is the SILHOUETTE's, not the ink's, so it is added here and not
                // in the one home — and symbols keep going without it, exactly as before.
                double margin = m.IsSymbol ? 0.0 : MarkSilhouetteMargin;
                // YUp is Y-up above the RESOLVED STAFF'S MIDDLE; the skyline is Y-up from
                // the SYSTEM ORIGIN. Two steps, not one: down to that staff's top line
                // (ScoreGrobStaffTopUp) and the half staff from the top line to the middle.
                // ⚠️ THE SECOND STEP ALONE WAS THE WHOLE TRANSLATION UNTIL 2026-08-25, and
                // it is right exactly when the first step is 0 — i.e. when the system opens
                // on a staff. A chords or lyrics row leading the system moves the origin
                // above the staff, and the mark's box floated up with it: MEASURED on
                // audit/lp-geometry's ROWM family, where m.YUp is 5.601073 in all four
                // books and the box's top read 5.701073 in all four while the origin sat
                // 2.000000 / 4.500000 / 5.947093 above the staff's reference point. The
                // inter-system gap is then max(basic, … + upMax + originToFirstStaff + …),
                // so the row's whole band was charged to the mark on EVERY x — 12.241073
                // with no row against 16.188166 with one (lyrics.chord-row.marked.*).
                double mY = ScoreGrobStaffTopUp(ms, m.StaffIndex) + m.YUp
                    - EngravingDefaults.StaffMiddle;
                // The VERTICAL envelope is the drawn ink's, from the one description the
                // stacker prices the same mark by (mirroring DrawSingleMusicMark) — NOT a
                // flat [mY − 0.7, mY + 2.1]. That envelope stood here until 2026-08-27 and
                // put 0.8 of air over a boxed label's drawn top; the padded skyline term
                // then poked 0.241073 over the basic-distance floor on every marked
                // interior pair (ledger lyrics.chord-row.marked.*.gap-second, all five).
                // LilyPond has no envelope to port: what joins its system skyline is the
                // grob's own stencil skyline (lily/axis-group-interface.cc:359-474), so the
                // port is to stop having one. The X half made this same move in session 204
                // (MarkXExtent); this is the Y half.
                var (_, _, mTop, mBottom) = OutsideStaffStacker.MusicMarkExtents(fonts, m);
                AddMarkBox(m.MeasureIndex, mx0 - margin, mx1 + margin, mY + mTop, mY - mBottom);
            }
        }
        if (!customTexts.IsDefaultOrEmpty)
        {
            foreach (var ct in customTexts)
            {
                // ⚠️ The size and style are the ones DrawCustomTexts draws with (and the
                // outside-staff stacker already reserved with): TextScript's own em, italic.
                // This site said 2.0 Bold, which over-reserved a short "rit." by 0.102429921
                // and a "molto espressivo" by 1.024299213 staff spaces — the harmless
                // direction, but still a box around a string nobody draws.
                // THE BOX STARTS AT THE PEN ORIGIN, because that is where the draw
                // starts: DrawCustomTexts writes START-anchored at ct.X, and the ledger's
                // textscript.x.pen-to-notehead-left reads that origin ON the anchor
                // column to fifteen digits. ⚠️ IT WAS CENTRED on the origin until
                // 2026-08-28 — [ct.X − advance/2, ct.X + advance/2] — i.e. half an
                // advance (6.04 staff spaces in the observer book) left of the ink, which
                // charged a previous system for text that was not over it and let real
                // text through where it was: MEASURED as a SIGN FLIP on one shift
                // (ledger page.custom-text.x.left-of-ink.gap-first +1.494000 against
                // LilyPond's exact no-text control, and page.custom-text.x.under-ink
                // −0.068845 where only the box's 45° flank survived). The same move the
                // mark arm's X made in session 204 (MusicMarkEngraver.MarkXExtent).
                double advance =
                    fonts.Advance(ct.Text, EngravingDefaults.TextScriptFontSize, TextRole.Text,
                        Rendering.FontStyle.Italic);
                // The system this text's box belongs to — resolved HERE, not only inside
                // AddMarkBox, because the box's own vertical frame is that system's (the
                // mark arm above makes the same move).
                if (!measureToSystem.TryGetValue(ct.MeasureIndex, out int cs))
                    continue;
                // YUp is Y-up above the RESOLVED STAFF'S MIDDLE; the skyline is Y-up from
                // the SYSTEM ORIGIN. Two steps, not one — down to that staff's top line
                // (ScoreGrobStaffTopUp) and the half staff from the top line to the middle
                // — the mark arm's 2026-08-25 shape.
                // ⚠️ THE SECOND STEP ALONE WAS THE WHOLE TRANSLATION UNTIL 2026-08-28, and
                // it is right exactly when the system opens on a staff. A chord row leading
                // the system moves the origin above the staff and the text's box floated up
                // with it: MEASURED on book CTW (ledger
                // page.custom-text.leading-row.gap-first), whose pair gap read 14.342093
                // against LilyPond's 12.570929 — and the whole Lily#-side excess 2.342093
                // died when this translation went two-step, every other custom-text book
                // standing still. The remaining −0.570929 on that entry is LilyPond's
                // loose-line redistribution (the subsystem Lily# deliberately lacks — see
                // LayoutUtilities.InterSystemPairMinimum remark ②), not this frame.
                double ctY = ScoreGrobStaffTopUp(cs, ct.StaffIndex) + ct.YUp
                    - EngravingDefaults.StaffMiddle;
                // The VERTICAL pair is the string's own ink about its baseline — the same
                // one house the Enrich arm above now reads and the draw draws (see its
                // remark for what the scalar [1.8/0.6] was and what it cost). The ledger
                // holds the two arms apart: the page anchors answer to Enrich, these gaps
                // to this box (page.custom-text.gap.*, +0.765955 / +0.211526 before this
                // change, each separated from the other arm by a ±0.03 poison).
                var (ctBottom, ctTop) = fonts.Ink(ct.Text, EngravingDefaults.TextScriptFontSize,
                    TextRole.Text, Rendering.FontStyle.Italic);
                AddMarkBox(ct.MeasureIndex, ct.X - 0.2, ct.X + advance + 0.2,
                    ctY + ctTop, ctY + ctBottom);
            }
        }
        // Inline chord symbols: their scalar height joins the up-extents, but
        // the X-aware inter-system Distance() never saw them — on a ragged
        // (natural-gap) page a below-staff jump text ("D.S. al Coda") printed
        // straight onto the next system's chord letters. Same envelope the
        // scalar extents use (cap ascent 1.9, descent 0.3).
        // ⚠️ THE LAST FLAT BOX ON A CHORD NAME, and the arm beside it (the per-measure
        // annotation extents, :338-344) reads real ink through ChordNameEngraver.SymbolInk.
        // Second spelling of one quantity, HANDOFF §5.2.1② — it has an observer since
        // 2026-08-28 (audit/lp-geometry page.inline-chord.gap-first, whose whole residual
        // +0.001116000 is this box plus the face term; poison 1.9 → 1.95 moves that reading
        // one for one and reddens it alone out of 6359 tests).
        // ★★★ ⚠️ BUT THE PORT MAKES THE NUMBER WORSE, AND THAT IS MEASURED, NOT FEARED
        // (session 273, before any port was attempted — §5.0's "measure before building").
        // Swapping this box for SymbolInk lands the entry on +0.008366371, i.e. 0.00725
        // FURTHER FROM LilyPond than the flat 1.9 sits today: Lily#'s TeX Gyre Heros inks
        // a capital taller than LilyPond's Nimbus Sans, so the scalar is nearer only by
        // accident of the face. ⇒ THE WHOLE OF THAT +0.008366371 WOULD BE THE FACE TERM,
        // the same island as page.chord-row.staff-to-chord-baseline and the same decision
        // (shipping Nimbus Sans). So this is NOT a free structural repair to hang on the
        // next act: it trades a headline fidelity number for one spelling, and RULES §5.2's
        // "do not fit a constant to the output" argues for taking that trade — but it is
        // the USER'S trade to take. Do not port this arm silently. Blast radius, measured
        // the same day: a +0.05 poison moves 1 of 572 tracked books (samples/greensleeves,
        // guarded by neither a snapshot nor the 81-book rerender corpus).
        if (!chordNames.IsDefaultOrEmpty)
        {
            foreach (var cn in chordNames)
            {
                double halfW = ChordNameEngraver.SymbolInkWidth(fonts, cn.ChordText) / 2 + 0.3;
                double cnY = cn.YUp; // cn.YUp is Y-up from the system top (skyline frame)
                AddMarkBox(cn.MeasureIndex, cn.X - halfW, cn.X + halfW, cnY + 1.9, cnY - 0.3);
            }
        }
        // Dynamics and free expressive text (@text) — the SAME shape as the chord names one
        // line up, and found the same way: a below-staff "D.S. Time Straight" under system 1
        // had its descender printed over by a section label's white-filled box above system 2
        // (scratch/ベースタブLy/blogger.lys — the text's ink bottom 22.39 against the box top
        // 22.18, so 0.21 ss inside it, and the box is drawn later so it ERASES the letters).
        // EnrichExtentsWithAnnotationProtrusions has counted these in the scalar extents all
        // along; the X-aware Distance() is what never saw them, and it wins wherever it can
        // prove room. MEASURED before the fix: adding a wide below-staff @text — or a plain
        // @pp — to a two-system book moved the next system by exactly nothing, while a
        // ledger-line note under the same column moved it 1.40.
        // Both houses are the ones the placement and the stacker read: the half-width is half
        // the DRAWN advance (DynamicEngraver.LabelHalfWidth) and the vertical pair is the
        // label's own font ink (InkOf — for @text that is the conservative fallback, since
        // only the dynamic GLYPHS have measured ink).
        // No silhouette margin here: unlike the mark box above, this one is the drawn ink and
        // there is nothing to widen it for — the fallback descent is already deeper than the
        // ink it stands in for.
        // LILYPOND-REF: lily/page-layout-problem.cc build_system_skyline — the system skyline
        //   contains the DynamicText grob like any other outside-staff stencil.
        if (!dynamics.IsDefaultOrEmpty)
        {
            foreach (var d in dynamics)
            {
                double halfW = DynamicEngraver.LabelHalfWidth(fonts, d.Text, d.IsExpressiveText);
                var (dAscent, dDescent) = DynamicEngraver.InkOf(d.Text, d.IsExpressiveText);
                // The system this dynamic's box belongs to — same resolution as the mark
                // and custom-text arms above.
                if (!measureToSystem.TryGetValue(d.MeasureIndex, out int ds))
                    continue;
                // d.YUp is Y-up above ITS OWN staff's middle; the skyline is Y-up from the
                // SYSTEM ORIGIN. Two steps — down to that staff's top line
                // (ScoreGrobStaffTopUp, which also carries a LOWER staff's offset) and the
                // half staff from the top line to the middle — the mark arm's 2026-08-25
                // shape.
                // ⚠️ THE SECOND STEP ALONE STOOD HERE UNTIL 2026-08-28, which was wrong in
                // two regimes at once: a chord row leading the system floated the box up
                // with the origin — MEASURED on book DYW (ledger
                // page.dynamics.leading-row.gap-first), where the below-staff \pp rose OUT
                // of the down silhouette and the pair gap under it read 13.090000 for
                // LilyPond's 15.442035, the COLLISION direction — and a lower staff's
                // dynamic was priced in the TOP staff's frame (no book bound on that half
                // before this fix; the sweep is its record).
                double dY = ScoreGrobStaffTopUp(ds, d.StaffIndex) + d.YUp
                    - EngravingDefaults.StaffMiddle;
                AddMarkBox(d.MeasureIndex, d.X - halfW, d.X + halfW, dY + dAscent, dY - dDescent);
            }
        }

        // ★ THE rit./accel. SPANNER, X-AWARE (2026-08-30). Its ink was already in the
        // SCALAR extents (EnrichExtentsWithAnnotationProtrusions' TextSpanners arm), which
        // prices it for the page BREAKER — and nothing more. The spring BETWEEN two systems
        // is LayoutUtilities.InterSystemPairMinimum, and with skylines present that reads
        // the X-aware Distance() ALONE: nextUpExtent is consulted only as the fallback for a
        // rows-only lead sheet (scalarFloorForSpaceablelessPrev). So a family that lands in
        // the scalar and NOT here is invisible to that spring however tall it is. A `rit.`
        // above a system's TOP staff was therefore drawn where nothing had reserved room,
        // and the system above it was spaced against a silhouette the spanner was not in.
        // MEASURED on the reported book (2026-08-30, Untitled-6.lys): the A2 system's rit.
        // sat at 61.030000 with its ink top at 59.6 while the previous system's second verse
        // "Like" reached 60.3 — printed through. The dynamics arm above is this same pair,
        // and its own remark records the same shape being fixed on 2026-08-28.
        // LILYPOND-REF: lily/page-layout-problem.cc:1093-1108 build_system_skyline — a
        //   VerticalAxisGroup's skyline carries its outside-staff grobs once
        //   skyline_spacing has placed them, so they reach the page like any other ink.
        // ⚠️⚠️ TWO BOXES, NOT ONE SPANNING BOX, and that is the whole difference between
        // this and the version that regressed a ledger point. The LABEL's height exists only
        // where the label is; the dashed rule after it is a hairline. Charging the label's
        // height across the spanner's whole span floors the gap under EVERY x for ink that
        // is at a FEW — which is exactly the retired chord band's mistake (see
        // page.inline-chord.gap-first's recorded cause). MEASURED: merging the top staff's
        // WHOLE skyline into the system silhouette instead of this drove that same ledger
        // point 2.500000 AWAY from LilyPond.
        if (!textSpanners.IsDefaultOrEmpty)
        {
            double spLineHalf = EngravingDefaults.StaffLineThickness / 2.0;
            foreach (var sp in textSpanners)
            {
                double spTop = spLineHalf, spBottom = spLineHalf;
                if (!string.IsNullOrEmpty(sp.Text))
                {
                    // The same ink the scalar arm and OutsideStaffStacker.PlaceTextSpanners
                    // read — one description of this label's height, not a third.
                    var ink = fonts.Ink(
                        sp.Text, TextSpannerEngraver.TextFontSize, TextRole.Text,
                        Rendering.FontStyle.Italic);
                    spTop = Math.Max(spTop, ink.Top);
                    spBottom = Math.Max(spBottom, -ink.Bottom);
                    if (sp.LineStartX > sp.StartX)
                        AddMarkBox(sp.StartMeasureIndex, sp.StartX, sp.LineStartX,
                            sp.YUp + spTop, sp.YUp - spBottom);
                }
                if (sp.EndX > sp.LineStartX)
                    AddMarkBox(sp.StartMeasureIndex, sp.LineStartX, sp.EndX,
                        sp.YUp + spLineHalf, sp.YUp - spLineHalf);
            }
        }

        // Line-start bar numbers sit in the band above the staff start where
        // only the staff-symbol roof exists; without their ink in the UP
        // silhouette, Distance() lets the previous system's staff lines crowd
        // the number (their scalar up-extent is overridden by the X-aware
        // distance). Same cap envelope Enrich uses — the digits' own ink from
        // the face, see the note there for what it replaced and what it closed.
        // LILYPOND-REF: lily/page-layout-problem.cc build_system_skyline —
        // the system skyline contains the BarNumber grob.
        if (!barNumbers.IsDefaultOrEmpty)
        {
            foreach (var bn in barNumbers)
            {
                if (!measureToSystem.TryGetValue(bn.MeasureIndex, out int s))
                    continue;
                // bn.YUp is Y-up from the system top; the skyline is Y-up too.
                double rel = bn.YUp;
                double w = fonts.Advance(
                    bn.Text, BarNumberEngraver.FontSize,
                    TextRole.BarNumber, Rendering.FontStyle.Bold);
                double x0 = bn.RightAligned ? bn.X - w : bn.X;
                double capTop = fonts.Ink(
                    bn.Text, BarNumberEngraver.FontSize,
                    TextRole.BarNumber, Rendering.FontStyle.Bold).Top;
                BuilderAt(s).AddBarNumberBox(x0, x0 + w, rel, rel + capTop);
            }
        }

        // THE PEDAL BRACKET under a staff, X-AWARE (2026-09-02, session 320; user report,
        // petite-valse.lys: the bracket under system 1's left hand drawn through the trill
        // and the fermata over system 2's right hand). PedalEngraver.SolveAndSeed seeds the
        // bracket into the STAFF's down profile — that is LilyPond's skyline_spacing merging
        // the SustainPedalLineSpanner (priority 1000) into its VerticalAxisGroup's
        // vertical-skylines — but that profile is what the alignment and the lyric floor
        // read; the page reads THIS silhouette, and the bracket was in none of its arms, so
        // the pair under it sat on the basic-distance floor: ledger
        // page.pedal-bracket.gap-first read 12.000000 against LilyPond's 13.345000 (the
        // bracket's ink bottom 5.300000 + the a''' top 7.045000 + padding 1).
        // LILYPOND-REF: lily/axis-group-interface.cc:969-978 skyline_spacing — the placed
        //   outside-staff grobs' skylines are merged into the group's own outline.
        // LILYPOND-REF: lily/page-layout-problem.cc:1093-1108 build_system_skyline — each
        //   element's vertical-skylines, raised by its own translation dy, merged into the
        //   system's. This arm is that loop's body for the one element the two-edge
        //   silhouette (SkylineBuilder.OuterStaff — a declared Lily#-own deviation from
        //   that loop) leaves out of the page: the stencil's box, at the line the staff's
        //   profile was solved with, raised by the staff's translation in the system.
        // ⚠️ THE BOX IS THE STENCIL'S OWN, from the one spelling the staff-profile merge
        // reads (PedalEngraver.BracketStencilBox) — not a box fitted to the number above.
        // Appended after the bar numbers and before the lyric band so every earlier family
        // keeps its association on systems without a pedal (the program remark's ULP
        // discipline); a system with one changes anyway.
        if (pedalLines is not null)
        {
            for (int s = 0; s < systemCount && s < pedalLines.Count && s < systems.Length; s++)
            {
                var staves = pedalLines[s];
                for (int staffIndex = 0; staffIndex < staves.Count; staffIndex++)
                {
                    if (staves[staffIndex].IsDefaultOrEmpty)
                        continue;
                    double staffMidUp = LayoutUtilities.StaffMiddleUpInSystem(systems[s], staffIndex);
                    foreach (var line in staves[staffIndex])
                    {
                        var (x0, x1, bottom, top) =
                            PedalEngraver.BracketStencilBox(line.StartX, line.EndX, line.LineYUp);
                        BuilderAt(s).AddMarkBox(x0, x1, bottom + staffMidUp, top + staffMidUp);
                    }
                }
            }
        }

        // The lyric block at its ALIGNMENT MINIMUM — the last family, appended after every
        // other step so the existing families keep their association (the program remark's
        // ULP discipline). This is what makes the inter-system floor read the band WITH X:
        // LilyPond has the block in build_system_skyline's input (:593-599) and its floor is
        // up.distance(down) (:625-632); the scalar that used to stand in for this spread the
        // band's deepest point under every X — audit/lp-geometry lyrics.band-floor.*.
        if (lyricBands is not null)
        {
            for (int s = 0; s < systemCount && s < lyricBands.Count; s++)
                if (lyricBands[s] is { IsEmpty: false } band)
                    BuilderAt(s).AddLyricBand(band);
        }

        // Replay each system's program — through the memo when a cache rides along, so an
        // unchanged system's merges are skipped entirely. A system no step touched keeps
        // its ORIGINAL skyline pair, exactly as the family-major loops left it.
        var result = new List<(VerticalSkyline up, VerticalSkyline down)>(systemCount);
        for (int s = 0; s < systemCount; s++)
        {
            if (builders[s] is not { } builder)
            {
                result.Add(skylines[s]);
                continue;
            }
            var program = builder.Build();
            result.Add(systemCache is null
                ? program.Execute(skylines[s])
                : systemCache.GetOrComputePagingAugment(s, skylines[s], program));
        }
        return result;
    }

    /// <param name="rowsAboveFirstStaff">
    /// Per system, <see cref="RowsAboveFirstStaff"/> — the raise that brings a STAFF-framed
    /// estimate into the ORIGIN frame <paramref name="perSystemExtents"/> is kept in.
    /// </param>
    private static void AugmentExtentsWithLooseLines(
        ScoreTextMetrics fonts,
        List<(double upExtent, double downExtent)> perSystemExtents,
        ImmutableArray<MusicMarkItem> musicMarks,
        ImmutableArray<VoltaBracketItem> voltaBrackets,
        List<(int startMeasure, int measureCount)> systemMeasureRanges,
        List<double>? perSystemBandUps,
        IReadOnlyList<double> rowsAboveFirstStaff)
    {
        // ⚠️ THE UP SIDE ONLY, since 2026-08-20. The lyric block's DOWN reservation is no
        // longer a scalar anywhere: its minimum profile joins the paging silhouette itself
        // (LyricReservationBelowSystem → PagingAugmentProgram.Builder.AddLyricBand) and its
        // deepest point joins the down extent where that extent is built (LayoutSystems) —
        // audit/lp-geometry lyrics.band-floor.* measured the scalar spreading the band
        // under every X. The chord-row band above keeps this shape until a point measures
        // it the same way.
        for (int i = 0; i < perSystemExtents.Count && i < systemMeasureRanges.Count; i++)
        {
            var (start, count) = systemMeasureRanges[i];
            var (looseUp, bandUp) = EstimateAboveStaffExtents(
                fonts, musicMarks, voltaBrackets, start, start + count);
            perSystemBandUps?.Add(bandUp);

            // ⚠️ TWO FRAMES MET IN THIS Math.Max UNTIL 2026-08-25. Every constant
            // EstimateAboveStaffExtents returns is measured ABOVE THE STAFF (3.5 for a
            // boxed section label, 3.0 for a rehearsal mark, 2.0 for a volta bracket) while
            // ext.upExtent is the silhouette's ink above the system's ORIGIN, and the two
            // are the same number only while the topmost element IS the staff. On a lead
            // sheet the staff-framed one won the max and then had the rows' band added to it
            // again downstream — HANDOFF 5.2.1② with the frames as the two spellings.
            double looseUpFromOrigin = looseUp
                - (i < rowsAboveFirstStaff.Count ? rowsAboveFirstStaff[i] : 0);
            // ⚠️ NO `> 0` GUARD any more: the extent is SIGNED (CalculateUpExtent), so a
            // system whose loose ink stops BELOW its origin has to be able to say so.
            var ext = perSystemExtents[i];
            perSystemExtents[i] = (Math.Max(ext.upExtent, looseUpFromOrigin), ext.downExtent);
        }
    }

}
