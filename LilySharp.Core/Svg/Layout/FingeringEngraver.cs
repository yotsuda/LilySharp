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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout for a single Fingering grob (a finger number digit attached to a note).
/// Coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/fingering-engraver.cc — Fingering grob
/// LILYPOND-REF: scm/define-grobs.scm — Fingering (font-size . -5, padding . 0.5)
/// </remarks>
public readonly record struct FingeringLayout(
    // Measure containing the host note.
    int MeasureIndex,
    // Item index of the host note within its measure.
    int ItemIndex,
    // Finger number (typically 1-5).
    int Number,
    // X coordinate of the digit center (staff spaces from score start).
    double X,
    // Y of the digit baseline in the LilyPond-native Y-up frame: staff-spaces
    // above THIS fingering's staff middle line, up-positive (frame B). The
    // renderer reflects it to device against the staff middle it resolves.
    double YUp,
    // True = above the staff, false = below.
    bool IsAbove,
    // Source position for click-to-source mapping.
    int SourcePosition,
    // F3/B: staff index of the host note/chord, so a reused layout re-derives
    // data-pos from the live score (SharedRenderer.ResolveDataPos). -1 = unresolved.
    int StaffIndex = -1,
    // The fingering's script-priority in its note's SCRIPT COLUMN. A vertically-oriented
    // fingering is a script like any other: priority 100 (the Fingering grob's
    // declaration) + direction × the head's staff position, sorted into the same
    // per-note walk that stacks staccato under tenuto under a bow
    // (ArticulationEngraver.CalculateWithFingerings).
    // ⚠️ A CHORD'S FINGERINGS USED TO SIT THIS OUT with int.MinValue, on the ground that
    // "FingeringColumn is a different mechanism". FingeringColumn serves the LEFT/RIGHT
    // orientations only (its own callback is ly:fingering-column::calc-positioning-done,
    // reached from position_scripts' horiz branch); a chord's UP/DOWN fingerings get the
    // same script-priority line as a single note's, from the same loop.
    // LILYPOND-REF: lily/new-fingering-engraver.cc:334-335 set_property
    //   "script-priority" (finger_prio + d * ft.position_);
    //   scm/define-grobs.scm:1554 Fingering script-priority = 100.
    int ColumnPriority = int.MinValue);

/// <summary>
/// Calculates positions for fingering numbers attached to notes.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/new-fingering-engraver.cc New_fingering_engraver — the engraver that
///   actually places a Fingering (fingering-engraver.cc is the one that only makes it).
/// LILYPOND-REF: scm/define-grobs.scm:1540-1568 Fingering — ly:script-interface::calc-positioning-done at :1553
///   font-size = -5 (very small), padding = 0.5, staff-padding = 0.5, avoid-slur = #'around,
///   side-axis = Y (set by the engraver, per orientation).
///
/// Fingerings are NOT placed opposite the stem, and the citation that used to say so here
/// (script-interface.cc, "direction calculation") was the wrong grob's rule:
/// New_fingering_engraver buckets them by <c>fingeringOrientations</c> — the Voice default
/// <c>'(up down)</c> — and each bucket is stacked OUTSIDE the staff by side-position. See
/// <see cref="BuildLayouts"/>, the port of that function, which serves a lone fingering and
/// a chord's alike.
/// ⚠️ <c>avoid-slur #'around</c> is NOT ported: a fingering does not dodge a slur here. It
/// is visible on corpus book chord-repetition, whose down digit sits under the slur — but
/// measured (scratch cr-probe.ly), LilyPond's own slur moves that book's digits by nothing,
/// so the sighting is not yet a measurement.
/// </remarks>
internal static class FingeringEngraver
{
    /// <summary>
    /// The Fingering grob's own side-position padding: its float off whatever supports it —
    /// the staff's ink, a notehead, or the digit already placed under it.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:1540-1568 Fingering — (padding . 0.5).
    /// ⚠️ Fingering ALSO declares (staff-padding . 0.5), and the two are NOT the same device:
    /// staff-padding drives a second clamp on the grob's OFFSET
    /// (side-position-interface.cc:433-451), while the staff reaches the digit through the
    /// SUPPORT skyline (:323-329 include_staff, <c>dim.set_minimum_height (staff_extents
    /// [dir])</c>) and is cleared by this padding like any other support. MEASURED
    /// (ledger fingering.chord.below.staff-to-ink-top, both books): the digit's INK clears
    /// the staff's ink by 0.5 on both sides, which the offset clamp cannot produce — the
    /// prediction that said it could was written down and falsified.
    /// The two numbers being equal is why the distinction was invisible for so long.
    /// </remarks>
    internal const double Padding = 0.5;

    /// <summary>
    /// The StaffSymbol's own ink edge — the outermost line's outer edge, which is what
    /// <c>include_staff</c> puts into the support skyline.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/side-position-interface.cc:323-329 — the staff's own EXTENT, and
    /// probes/glyph-skyline.ly dumps that extent as (−2.05 . 2.05).
    /// </remarks>
    private const double StaffInk =
        EngravingDefaults.StaffMiddle + EngravingDefaults.StaffLineThickness / 2.0;

    /// <summary>
    /// The digits of a fingering as feta-text glyph metrics AT THE FINGERING'S OWN SIZE
    /// (font-size −5 of the 4-ss staff-height base — the same em the figured bass shares,
    /// <see cref="EngravingDefaults.FiguredBassFontSize"/>, both being fetaText at −5):
    /// the mapped glyph run, its ink box relative to the run's LEFT BASELINE origin, and
    /// its advance width. One home for the pen (SharedRenderer.DrawFingerings), the
    /// script-column profile (ArticulationEngraver) and the placement below.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:1547-1568 Fingering (outside-staff-interface
    ///   family) — font-encoding fetaText, font-features cv47 ss01, font-size −5;
    ///   its stencil is text-interface::print of fingering::calc-text (:1559-1560),
    ///   so the drawn ink IS these glyphs at that size.
    /// ⚠️ IT WAS A SERIF DIGIT AT 0.56 EM until 2026-08-08 — roughly HALF LilyPond's ink
    /// (feta numerals are 2.0 design-ss tall → 1.12 at −5), so everything stacked over a
    /// fingering sat a half-space too low. Measured on script-stack-order1: a bow over a
    /// fingering is at −5.33 (LP) = digit top 1.13 + padding 0.20.
    /// <para>
    /// ⚠️ THE BOX'S X IS THE ADVANCE, NOT THE INK — <c>Left</c> is 0 and <c>Right</c> is the
    /// advance <c>width</c>, while LilyPond's Fingering X-extent is the glyph's own stencil.
    /// MEASURED (probes/fingering-slur.ly, book FSB): LP reads <c>xext = (0.0 . 0.819439)</c>
    /// for a "1" at font-size −5 where this answers about 0.90, so the box runs ~0.043 too far
    /// right. It is invisible to every consumer that only asks how TALL a digit is, and
    /// visible to the one that asks where its EDGE is: ledger
    /// <c>fingering.slur.bound-note.staff-to-ink-bottom</c> keeps +0.005981557 of which
    /// +0.016984728 is this (the rest is the curve sampler, the other way).
    /// ⚠️ Correcting it is output-moving for all THREE consumers named above — the pen, the
    /// script-column profile and the placement — so it needs its own point first, exactly as
    /// the Y side of this box did.
    /// </para>
    /// </remarks>
    internal static (string Glyphs, GlyphMetrics.BBox Ink, double Width) DigitRun(int number)
    {
        // The ten single digits — every fingering in practice — are answered from a
        // table built once: DigitRun runs per fingering in the island pass, the
        // column flush AND every preview redraw, and each uncached call walks the
        // glyph run three times (pieces, width, ink). A pure function of the number,
        // so the memo is exact.
        if (number is >= 0 and <= 9)
            return SingleDigitRuns[number] ??= BuildDigitRun(number);
        return BuildDigitRun(number);
    }

    private static readonly (string, GlyphMetrics.BBox, double)?[] SingleDigitRuns =
        new (string, GlyphMetrics.BBox, double)?[10];

    private static (string Glyphs, GlyphMetrics.BBox Ink, double Width) BuildDigitRun(int number)
    {
        string text = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var glyphs = new System.Text.StringBuilder(text.Length);
        foreach (var piece in FiguredBassGlyphRun.Pieces(text))
            if (piece.IsGlyph)
                glyphs.Append(piece.Ch);
        double width = FiguredBassGlyphRun.Width(text);
        return (glyphs.ToString(),
            new GlyphMetrics.BBox(0.0, FiguredBassGlyphRun.InkBottom(text),
                width, FiguredBassGlyphRun.InkTop(text)),
            width);
    }

    /// <summary>
    /// Calculates layouts for all fingerings in a single-staff score.
    /// </summary>
    public static ImmutableArray<FingeringLayout> Calculate(
        Score score,
        ImmutableArray<SystemLayout> systems,
        int staffIndex = -1,
        ImmutableArray<BeamLayout> beamLayouts = default)
    {
        if (score.Voices.IsDefaultOrEmpty)
            return ImmutableArray<FingeringLayout>.Empty;

        var measureMap = LayoutUtilities.BuildMeasureLayoutMap(systems);
        var systemMap = LayoutUtilities.BuildMeasureMap(systems);
        return Calculate(score, measureMap, systemMap.ContainsKey, staffIndex, beamLayouts);
    }

    /// <summary>
    /// <see cref="Calculate(Score, ImmutableArray{SystemLayout}, int)"/> for a caller that
    /// holds ONE system's measure layouts rather than the placed systems — the shape the
    /// per-staff skyline pass runs in.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE SAME ENGRAVER, NOT A SECOND SPELLING (the argument
    /// <c>MultiStaffLayouter.StaffArticulationLayouts</c> makes for the scripts): the
    /// reservation has to be the geometry that will be DRAWN, and the only way to promise
    /// that is to ask the same function. The answer is staff-local already — a
    /// <see cref="FingeringLayout"/>'s <c>YUp</c> is about its own staff's middle line and
    /// no staff offset is ever baked in — so the profile lands in the per-staff skyline's
    /// own frame with nothing to translate.
    /// </remarks>
    public static ImmutableArray<FingeringLayout> Calculate(
        Score score,
        ImmutableArray<MeasureLayout> measureLayouts,
        int staffIndex,
        ImmutableArray<BeamLayout> beamLayouts = default)
    {
        if (score.Voices.IsDefaultOrEmpty || measureLayouts.IsDefaultOrEmpty)
            return ImmutableArray<FingeringLayout>.Empty;
        var map = new Dictionary<int, MeasureLayout>();
        foreach (var ml in measureLayouts)
            map[ml.MeasureIndex] = ml;
        return Calculate(score, map, _ => true, staffIndex, beamLayouts);
    }

    /// <remarks>
    /// ⚠️ THE WALK IS OVER THE MEASURES THIS CALLER HOLDS, not over the score's. The
    /// difference is invisible in the answer and not in the WORK: this body runs once per
    /// (system, staff) from the skyline pass, so iterating <c>voice.Measures</c> there made
    /// every one of a score's systems pay for all of its measures — the O(score) per-system
    /// shape <c>MultiStaffLayouter.StaffArticulationLayouts</c>'s own remark warns about, and
    /// a preview relayout is exactly the caller that cannot afford it.
    /// The keys are walked in ascending order so the layouts come out in the order the
    /// measures do, which is the order the renderer draws them in.
    /// </remarks>
    private static ImmutableArray<FingeringLayout> Calculate(
        Score score,
        Dictionary<int, MeasureLayout> measureMap,
        System.Func<int, bool> isPlaced,
        int staffIndex,
        ImmutableArray<BeamLayout> beamLayouts = default)
    {
        var layouts = ImmutableArray.CreateBuilder<FingeringLayout>();

        // Which beam each note belongs to — the STEM is a support of every fingering and a
        // beamed stem ends on the beam, so the gate below needs the same map the scripts use.
        // Empty (and free) for the unbeamed book, which is why the caller may omit the beams.
        var beamedTips = beamLayouts.IsDefaultOrEmpty
            ? null
            : ArticulationEngraver.BuildBeamedStemTips(beamLayouts);

        var voice = score.Voice;
        var indices = new List<int>(measureMap.Count);
        foreach (int mi in measureMap.Keys)
            if (mi >= 0 && mi < voice.Measures.Length && isPlaced(mi))
                indices.Add(mi);
        indices.Sort();

        foreach (int mi in indices)
        {
            var measure = voice.Measures[mi];
            var measureLayout = measureMap[mi];

            for (int ii = 0; ii < measure.Items.Length; ii++)
            {
                var item = measure.Items[ii];
                // The engraver serves the staff's PRIMARY voice (its callers build a score of
                // that one voice), so the beam lookup asks for voice 0 — the same key
                // ArticulationEngraver queries with the script's own voice index.
                NoteColumnLayout? column = null;
                if (beamedTips != null
                    && beamedTips.TryGetValue((System.Math.Max(0, staffIndex), 0, mi, ii),
                        out var beamTip))
                    column = NoteColumnLayout.Of(item, beamTip.StemUp, beamTip.Beam, beamTip.MemberX);

                if (item is NoteItem note && note.Fingering.HasValue)
                {
                    BuildLayouts(
                        new[] { (note.StaffPosition, note.Fingering.Value) },
                        new[] { note.StaffPosition },
                        note.BaseDuration, note.SourcePosition,
                        mi, ii, measureLayout, staffIndex, layouts, voice.Measures, column);
                }
                else if (item is ChordItem chord)
                {
                    var fingered = chord.Notes
                        .Where(n => n.Fingering.HasValue)
                        .Select(n => (n.StaffPosition, n.Fingering!.Value))
                        .ToArray();
                    if (fingered.Length == 0)
                        continue;
                    BuildLayouts(
                        fingered,
                        chord.Notes.Select(n => n.StaffPosition).ToArray(),
                        chord.BaseDuration, chord.SourcePosition,
                        mi, ii, measureLayout, staffIndex, layouts, voice.Measures, column);
                }
            }
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// Places every fingering of ONE note or chord — the port of
    /// <c>New_fingering_engraver::position_scripts</c> for the vertical orientations.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/new-fingering-engraver.cc:182-268 position_scripts — the scripts
    ///   are sorted by their head's staff-position (:202-204, operator&lt;) and, for
    ///   <c>fingeringOrientations</c> <c>'(up down)</c> (ly/engraver-init.ly, the Voice
    ///   default), cut at <c>center = size / 2</c>: <c>[0, center)</c> goes DOWN and the
    ///   rest UP (:248-253). One fingering therefore goes UP — <c>1 / 2 == 0</c> leaves the
    ///   down bucket empty — which is the melodic default a lone digit has always had here.
    /// LILYPOND-REF: :206-208 — EVERY head of the timestep becomes a side-support of EVERY
    ///   fingering, so the up bucket clears the chord's TOP head and the down bucket its
    ///   BOTTOM one, not each digit's own note.
    /// LILYPOND-REF: :334-335 — script-priority 100 + d·position, and
    ///   lily/script-column.cc:131-187 order_grobs makes each later script of a direction a
    ///   side-support of all the earlier ones (Fingering declares no
    ///   outside-staff-priority, so that branch is the one that runs). ⇒ within a bucket the
    ///   digit NEARER the staff is placed first and the next stacks on its ink.
    /// LILYPOND-REF: lily/side-position-interface.cc:323-329 include_staff — the STAFF's own
    ///   extent joins the support skyline, so a digit clears the staff's ink by
    ///   <see cref="Padding"/> exactly as it clears a head.
    /// <para>
    /// ⚠️ WHAT THIS REPLACED, AND WHY IT WAS NOT A DIFFERENT-BUT-EQUAL ARRANGEMENT: a
    /// chord's digits used to be set at their own heads' heights, under a remark calling it
    /// "a simpler, equally LP-compatible arrangement" that cited FingeringColumn. It is not
    /// equal and FingeringColumn is not the mechanism: on corpus book chord-repetition the
    /// three digits printed on the noteheads and the stem, unreadable. Six ledger points
    /// (fingering.chord.*, books CFL/CFH of probes/chord-fingering.ly) measure what
    /// LilyPond does instead, and they were raised BEFORE this port.
    /// </para>
    /// ★ THE STEM IS A SUPPORT IN LILYPOND (:186-190), UNCONDITIONALLY — and the beam never
    /// is. What Fingering's <c>add-stem-support = only-if-beamed</c> (scm/define-grobs.scm:1543,
    /// scm/output-lib.scm:463) decides is HOW MUCH of the stem counts:
    /// lily/side-position-interface.cc:302-305 flattens the stem's skyline to its own maximum
    /// when the predicate holds, so the digit clears the stem's FULL reach at every x instead
    /// of only where the thin line stands. Ported here as <paramref name="column"/>: non-null
    /// exactly when this note is BEAMED, and read for its reach on the bucket's side.
    /// <para>
    /// ⚠️ THE GATE STANDS IN FOR THE FLATTENING, and that is a modelling choice, not a
    /// transcription. This support set is a SCALAR (the derivation above says so), so there is
    /// no x at which a thin stem could fail to stand under the digit — an unflattened stem
    /// would be over-applied here. Including the stem only when the flattening fires is what
    /// makes the scalar reproduce both books. MEASURED, and the pair is what forced it: the
    /// FLAGGED stem of ledger <c>fingering.chord.flagged-inner.staff-to-ink-bottom</c> reaches
    /// 2.500000, HIGHER than the beamed stem's 2.240000, yet its digit reads the staff clamp
    /// 2.550000 while the beamed one reads 2.740000. A model that counted the stem's height
    /// whenever there is a stem would put the FLAGGED book above the beamed one.
    /// </para>
    /// <para>
    /// ⚠️ AND THIS GATE IS THE THING TO DELETE, not to extend, when this support set becomes
    /// POINTWISE. The moment the walk reads a skyline instead of a scalar — the move
    /// <see cref="NoteColumnLayout"/>'s own remark records the dynamics and the trill already
    /// making, because a scalar edge could not answer two books at any value — the stem should
    /// go in UNCONDITIONALLY, as LilyPond puts it in, and <c>add-stem-support</c> should become
    /// what it actually is: the stem profile's own maximum imposed on itself. The two
    /// spellings agree on every book measured today and stop agreeing the moment a digit and a
    /// stem stand at different x, which is a chord with a seconds shift — the same texture
    /// this method's scalar derivation already names as its limit.
    /// </para>
    /// ⚠️ THE FLAG IS A SUPPORT TOO (:188-190) and is not here: the same argument retires it
    /// (it only ever stands beside an unbeamed stem, where the gate is shut), and the flagged
    /// book measures that its ink changes nothing.
    /// ⚠️ THE ACCIDENTALS ARE SUPPORTS TOO on a chord (:329-332). Not ported: no point
    /// observes it, and a chord whose accidental out-reaches its own head vertically does
    /// not exist (an accidental is centred on its head and is barely taller).
    /// </remarks>
    private static void BuildLayouts(
        (int Position, int Number)[] fingered,
        int[] headPositions,
        Fraction baseDuration,
        int sourcePosition,
        int measureIndex, int itemIndex,
        MeasureLayout measureLayout, int staffIndex,
        ImmutableArray<FingeringLayout>.Builder layouts,
        ImmutableArray<Measure> measures,
        NoteColumnLayout? column = null)
    {
        if (measureLayout.Columns.IsDefaultOrEmpty
            && itemIndex >= measureLayout.Items.Length)
            return;

        double centerX = CentreX(baseDuration, measureIndex, itemIndex, measureLayout, measures);

        // The support set: every head's own INK edge, and the staff's.
        // ⚠️ DERIVATION, NOT A TRANSCRIPTION, and it is the shape of the code that differs:
        // LilyPond merges each support's SKYLINE and takes a pointwise distance
        // (side-position-interface.cc:288-320), where this takes the max/min of the heads'
        // edges — a scalar. The two agree exactly while the heads share one X, which is
        // every chord without a second and every single note; a chord whose seconds are
        // shifted apart would let LilyPond's digit tuck beside the offset head where this
        // clears it. No point reaches that texture, and it is named here rather than
        // silently approximated.
        // The head's half-ink is the glyph's, not the nominal 0.5 — measured by the CFH
        // book, whose up bucket clears the top head's 0.545 and not a nominal half
        // (ledger fingering.chord.above-head-inner.staff-to-ink-bottom = 4.045000, whose
        // named fork 4.000000 did not fire). EngravingDefaults.NoteheadHalfHeight's own
        // remark listed this site as one that could not be corrected while no point
        // observed it; one does now.
        int noteValue = baseDuration.Numerator == 1 ? (int)baseDuration.Denominator : 1;
        double headHalfInk = GlyphMetrics.GetNoteheadBBox(noteValue).Top;
        double headsTop = double.NegativeInfinity, headsBottom = double.PositiveInfinity;
        foreach (int p in headPositions)
        {
            headsTop = System.Math.Max(headsTop, p * 0.5 + headHalfInk);
            headsBottom = System.Math.Min(headsBottom, p * 0.5 - headHalfInk);
        }

        // The split. A stable sort by staff position, then LilyPond's own cut.
        var sorted = fingered.OrderBy(f => f.Position).ToArray();
        int center = sorted.Length / 2;

        // The BEAMED stem's reach, on each side, in the same Y-up-about-the-middle frame the
        // heads are in — <c>column</c> is non-null exactly when this note is beamed (see the
        // remark). The house that answers it is the one the tuplet bracket and the scripts
        // already read, so the digit clears the SAME quanted beam face they do.
        // LILYPOND-REF: lily/side-position-interface.cc:302-305 add-stem-support.
        double stemUpReach = double.NegativeInfinity, stemDownReach = double.PositiveInfinity;
        if (column is { } col)
        {
            stemUpReach = EngravingDefaults.StaffMiddle - col.OutwardTipDeviceY(towardUp: true);
            stemDownReach = EngravingDefaults.StaffMiddle - col.OutwardTipDeviceY(towardUp: false);
        }

        // UP, inner to outer: priority 100 + position ascends with the position, so the
        // bucket is walked in the order it was just sorted into.
        double support = System.Math.Max(System.Math.Max(StaffInk, headsTop), stemUpReach);
        for (int i = center; i < sorted.Length; i++)
        {
            var ink = DigitRun(sorted[i].Number).Ink;
            double yUp = support + Padding - ink.Bottom;
            layouts.Add(new FingeringLayout(
                MeasureIndex: measureIndex,
                ItemIndex: itemIndex,
                Number: sorted[i].Number,
                X: centerX,
                YUp: yUp,
                IsAbove: true,
                SourcePosition: sourcePosition,
                StaffIndex: staffIndex,
                ColumnPriority: 100 + sorted[i].Position));
            support = yUp + ink.Top;
        }

        // DOWN, inner to outer: priority 100 − position, so ascending priority is
        // DESCENDING position — the highest note of the down bucket is nearest the staff.
        // ⚠️ DERIVATION: LilyPond SORTS the bucket by the script-priority it has just set
        // (script-column.cc:131-187 order_grobs, a stable sort on that property) where this
        // walks the position-sorted bucket BACKWARDS. Same order, fewer moving parts — but
        // only because this direction's priority is 100 − position, which is monotonically
        // decreasing in the key the bucket is already sorted by. If a fingering ever gets a
        // priority that is not that function (LilyPond's finger_prio comes from the grob,
        // and StringNumber/StrokeFinger have their own), the sort has to come back.
        support = System.Math.Min(System.Math.Min(-StaffInk, headsBottom), stemDownReach);
        for (int i = center - 1; i >= 0; i--)
        {
            var ink = DigitRun(sorted[i].Number).Ink;
            double yUp = support - Padding - ink.Top;
            layouts.Add(new FingeringLayout(
                MeasureIndex: measureIndex,
                ItemIndex: itemIndex,
                Number: sorted[i].Number,
                X: centerX,
                YUp: yUp,
                IsAbove: false,
                SourcePosition: sourcePosition,
                StaffIndex: staffIndex,
                ColumnPriority: 100 - sorted[i].Position));
            support = yUp + ink.Bottom;
        }
    }

    /// <summary>The digit run's X centre: its host head's own INK centre.</summary>
    /// <remarks>
    /// Centered on the notehead glyph (self-alignment-X = CENTER), via the Items/Columns-aware
    /// resolver. The centre is the head's own INK centre: what <c>aligned_on_parent</c> centres
    /// on is the PARENT's stencil extent, and a NoteHead's extent is its ink — 1.9620 on the
    /// whole head, 1.3042 on the black one, dumped out of LilyPond in
    /// audit/lp-geometry/probes/dynamic-support.ly's books, against advances of 1.960 and 1.304.
    /// The ledger reads it as fingering.whole.column-to-ink-centre (book FNG, exact).
    /// LILYPOND-REF: lily/self-alignment-interface.cc:147 aligned_on_parent — he = him->extent (him, a), the parent's own stencil extent
    /// <para>
    /// ⚠️ ONE X FOR ALL OF A CHORD'S DIGITS, where LilyPond gives each its OWN head as
    /// X-parent (new-fingering-engraver.cc:326-332 — the note_column branch needs
    /// <c>X-align-on-main-noteheads</c>, which Fingering does not declare). The two agree
    /// whenever the chord's heads share a column, which is every chord without a second;
    /// the books CFL/CFH are such chords and both engines put the three digits within their
    /// own widths of one x. No ledger point measures a chord fingering's X, so this stays as
    /// it was rather than being changed unobserved.
    /// </para>
    /// </remarks>
    private static double CentreX(
        Fraction baseDuration, int measureIndex, int itemIndex,
        MeasureLayout measureLayout, ImmutableArray<Measure> measures)
        => measureLayout.X + LayoutUtilities.GetItemXOffset(
               measures, measureIndex, itemIndex, measureLayout)
           + GlyphMetrics.GetNoteheadBBox(
               baseDuration.Numerator == 1 ? (int)baseDuration.Denominator : 1).CenterX;
}
