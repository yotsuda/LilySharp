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
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

internal static partial class SpacingRules
{
    /// <summary>
    /// Creates a spring for a timing column based on duration.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-spanner.cc:musical_column_spacing()
    /// Simplified spring creation for timing-based columns without skyline collision detection.
    /// Uses duration-based spacing for ideal distance.
    /// </remarks>
    public static Spring CreateTimingSpring(Fraction duration,
                                            double? baseShortestDuration = null,
                                            NoteSpacingParameters? noteParams = null)
    {
        // LILYPOND-REF: lily/spacing-basic.cc:109 note_spacing() - increment
        double defaultMin = EngravingDefaults.SpacingIncrement;

        // LILYPOND-REF: lily/spacing-basic.cc:107 note_spacing() - duration space
        double idealDistance = CalculateDurationSpace(duration,
            baseShortestDuration ?? EngravingDefaults.BaseShortestDuration);

        // Ensure minimum distance
        idealDistance = Math.Max(idealDistance, defaultMin);

        // min_distance for timing springs (no skyline collision)
        double minDistance = defaultMin;

        // LILYPOND-REF: lily/note-spacing.cc:229-264 strict_note_spacing
        // In strict mode, enforce minimum distance = ideal distance for proportional spacing
        var np = noteParams ?? NoteSpacingParameters.Default;
        if (np.StrictNoteSpacing)
        {
            minDistance = Math.Max(minDistance, idealDistance);
        }

        // LILYPOND-REF: lily/spacing-basic.cc:115 note_spacing() - inverse_stretch
        double inverseStretchStrength = Math.Max(0.1, idealDistance - defaultMin);

        return new Spring(idealDistance, minDistance, inverseStretchStrength);
    }

    /// <summary>
    /// Creates a spring scaled by the shortest currently-playing note duration across all voices.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-basic.cc:107-162 Spacing_spanner::note_spacing
    /// LILYPOND-REF: lily/spacing-engraver.cc:200-253 stop_translation_timestep
    ///
    /// LP's per-column spring formula:
    ///   <c>fraction = delta_t / shortest_playing</c>
    ///   <c>len = options-&gt;get_duration_space(shortest_playing)</c>
    ///   <c>spring = Spring(fraction * len, fraction * min)</c>
    /// where <c>shortest_playing</c> is the min duration over all voices' notes that are
    /// playing at the left column of the spring (NOT just the time delta to the next column).
    /// In monophonic music <c>shortest_playing == delta_t</c> and this collapses to the
    /// existing <see cref="CreateTimingSpring(Fraction, double?, NoteSpacingParameters?)"/>;
    /// in polyphonic music it produces tighter springs when a faster voice is sounding
    /// underneath a slower voice.
    /// </remarks>
    public static Spring CreateTimingSpringMultiVoice(
        Fraction segmentDuration,
        Fraction shortestPlayingDuration,
        double? baseShortestDuration = null,
        NoteSpacingParameters? noteParams = null,
        Fraction? measureLength = null)
    {
        // ⚠️ The delta_t fallback is LILY#'S OWN. LilyPond never asks this question —
        // a column with no playing grobs is pruned before note_spacing runs — and its
        // own guard for the impossible case (lily/spacing-basic.cc:113-120) is a
        // programming_error plus a WHOLE note, not delta_t. This line used to cite
        // those very lines as if they prescribed delta_t; they do not. Lily# keeps
        // skip-only columns alive (lead-sheet slot grids), and for those delta_t
        // equals the slot's own duration, which is what the measured recipe wants.
        if (shortestPlayingDuration <= Fraction.Zero)
            shortestPlayingDuration = segmentDuration;
        if (shortestPlayingDuration <= Fraction.Zero)
            return CreateTimingSpring(segmentDuration, baseShortestDuration, noteParams);

        // LILYPOND-REF: lily/spacing-basic.cc:144 — clamp shortest_playing to the MEASURE LENGTH
        // (a multi-measure-rest guard), NOT to this segment's delta_t. Clamping to delta_t was a
        // bug: it forced fraction = delta_t / shortest_playing = 1 for every sub-beat column, so an
        // interleaved polyrhythm column (a triplet note landing between two straight eighths) took a
        // FULL note's duration_space instead of its proportional share. The proportional part below
        // (fraction * len) is exactly what keeps the other voice's eighths evenly spaced: two sub-
        // gaps of a note sum back to that note's space only when shortest_playing stays the note.
        Fraction effectivePlaying = shortestPlayingDuration;
        if (measureLength is { } mlen && mlen > Fraction.Zero && mlen < effectivePlaying)
            effectivePlaying = mlen;

        double defaultMin = EngravingDefaults.SpacingIncrement;
        double bsd = baseShortestDuration ?? EngravingDefaults.BaseShortestDuration;

        // LILYPOND-REF: lily/spacing-basic.cc:151 — len = get_duration_space(shortest_playing)
        double len = CalculateDurationSpace(effectivePlaying, bsd);
        // LILYPOND-REF: lily/spacing-basic.cc:155-156 — fraction = delta_t / shortest_playing
        double fraction = segmentDuration.ToDouble() / effectivePlaying.ToDouble();

        // LILYPOND-REF: lily/spacing-basic.cc:157 — Spring(fraction * len, fraction * min).
        // BOTH terms scale by fraction. A sub-beat interleaved column (fraction < 1 — e.g. a triplet
        // note splitting one voice's straight eighth into two sub-gaps) gets its PROPORTIONAL share,
        // not a full-notehead floor. Flooring the ideal at the whole increment (as this did before)
        // inflated the shorter half of the split gap, so the other voice's eighths spread wider on
        // exactly the beats the triplet stems land on. Genuine overlap is still blocked by the
        // skyline rod computed in CreateInterColumnSpring — the ideal need not reserve a full head.
        double idealDistance = fraction * len;
        double minDistance = fraction * defaultMin;

        var np = noteParams ?? NoteSpacingParameters.Default;
        if (np.StrictNoteSpacing)
            minDistance = Math.Max(minDistance, idealDistance);

        // LILYPOND-REF: lily/spacing-basic.cc:160-161 — inverse_stretch_strength = fraction * max(0.1, len - min)
        double inverseStretchStrength = Math.Max(0.1, fraction * Math.Max(0.1, len - defaultMin));

        return new Spring(idealDistance, minDistance, inverseStretchStrength);
    }

    /// <summary>A one-item sequence, for the single-voice callers of
    /// <see cref="ApplyLeftHeadWidth"/> (which takes the simultaneous left column).</summary>
    private static IEnumerable<MusicItem> One(MusicItem item)
    {
        yield return item;
    }

    /// <summary>
    /// Refines a duration-based ideal to the LEFT note column's actual head width.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-spacing.cc:77 Note_spacing::get_spacing —
    ///   ideal = base.ideal_distance() - increment + left_head_end.
    /// The duration space assumes a generic notehead (spacing-increment). LilyPond
    /// swaps that generic width for the left column's ACTUAL head width, so a wide
    /// head (half 1.376 / whole 1.96) reserves proportionally more room than a
    /// black head (1.304). For a black head the net adjustment is
    /// 1.304 - 1.2 = +0.104 ss — the uniform gap LilyPond has over Lily#'s raw
    /// duration spacing. A rest uses its glyph's right extent instead (LilyPond's
    /// g = the rest grob): a quarter rest (~0.95) is NARROWER than the increment,
    /// so the space after a rest shrinks, matching LilyPond ("a quarter rest gets
    /// almost 0.5 ss less horizontal space than a note"). The widest such left
    /// item wins (a safe choice for simultaneous voices); non-musical items leave
    /// the ideal unchanged.
    /// </remarks>
    /// <summary>Whether this item's heads are drawn in the cue font.</summary>
    /// <remarks>
    /// ⚠️ ONE DECISION, TWO READERS (EngravingDefaults.CueDesignSize says so in writing): the
    /// box a cue column reserves has to be the box its head is drawn in. This predicate is the
    /// spacing side of the pair SharedRenderer.Noteheads reads on the drawing side.
    /// </remarks>
    private static bool IsCueItem(MusicItem item) => item switch
    {
        NoteItem n => n.IsCue,
        ChordItem c => c.IsCue,
        _ => false
    };

    /// <summary>
    /// The voice context an item belongs to for spacing: what the routing stamped, or the cue
    /// region its per-note flag stands for.
    /// </summary>
    private static VoiceContextId ContextOf(MusicItem item) =>
        item.VoiceContext != VoiceContextId.Default ? item.VoiceContext
        : IsCueItem(item) ? VoiceContextId.Cue
        : VoiceContextId.Default;

    /// <summary>
    /// The set of contexts holding a NOTE COLUMN in this column, one bit per
    /// <see cref="VoiceContextId"/>; 0 when the column has none.
    /// </summary>
    /// <remarks>
    /// Only the items LilyPond makes a wish for count, so a bar line, a clef or a key change
    /// contributes nothing — which is what makes a change column belong to every voice rather
    /// than to a sixth one of its own. A SPACER contributes nothing either: it engraves no
    /// grob, so no context acknowledges anything at its moment. That is the same reading
    /// <see cref="ApplyLeftHeadWidth"/> already spends on the width (a spacer prices NaN, not
    /// a phantom rest).
    /// LILYPOND-REF: lily/note-spacing-engraver.cc:88-91 acknowledge_rhythmic_grob — every
    /// rhythmic grob, and note columns beside it, is what a wish is made from.
    /// <para>
    /// ⚠️ One bit per enum member, so <see cref="VoiceContextId"/> may not grow past 32. It has
    /// six, and the only way it grows is a new engraving voice — which is a design change, not
    /// a drift — so this is a note rather than a guard.
    /// </para>
    /// </remarks>
    private static int ContextMask(IEnumerable<MusicItem> items)
    {
        int mask = 0;
        foreach (var item in items)
        {
            if (item is not (NoteItem or ChordItem or RestItem { IsSpacer: false }))
                continue;
            mask |= 1 << (int)ContextOf(item);
        }
        return mask;
    }

    /// <summary>
    /// Whether the refinement runs at all over this column pair — it does not across a VOICE
    /// boundary, where the spring keeps its raw duration ideal.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-spanner.cc:352-358 uses a Note_spacing wish only when one of
    /// its <c>right-items</c> IS the column being spaced to, and :380-391 leaves the base spring
    /// untouched when no wish matches. A wish belongs to a VOICE, so the pair that straddles two
    /// of them is refined by neither.
    /// <para>
    /// ⚠️ THE READING IS NOT ABOUT CUES. It used to be spelled as one — "is one side cued and the
    /// other not" — with a note saying a cue region was the only sequential voice change Lily#
    /// could spell. <c>combinedStaff</c> made that false: the part combiner routes ONE stream
    /// through five contexts, so its shared→solo step is a voice change in the middle of a
    /// measure with no cue anywhere. The condition below is LilyPond's own instead — DOES ANY
    /// CONTEXT OCCUPY BOTH COLUMNS — of which the cue reading is the two-context case.
    /// MEASURED, audit/lpreg/pctend.log dumps every wish of
    /// input/regression/part-combine-tuplet-end.ly: the shared voice's wish at the half rest
    /// carries right-items (31.403, 29.313) — the note it engraves next and the bar line — and
    /// NOT the solo note at 14.087 that follows it, so that pair alone loses the refinement and
    /// sits 1.5 − 1.2 = 0.300 tighter than the same music without the combiner (5.502 against
    /// 5.802, control pctend-ctl.ly).
    /// <para>
    /// ⚠️ A VOICE CHANGE ACROSS A BAR LINE COSTS NOTHING, and the same book says so: the last
    /// solo note's wish DOES name the bar-line column, because the engraver adds
    /// <c>currentCommandColumn</c> to the running wish at every timestep, and a bar line
    /// belongs to every voice.
    /// LILYPOND-REF: lily/note-spacing-engraver.cc:115-120 stop_translation_timestep — the
    /// command column joins the previous wish's right-items whenever the staff has spacing.
    /// So the
    /// solo→shared step over the bar line into the a2 keeps its refinement (26.608→bar line
    /// measures the same in both scores) while the shared→solo step inside bar 1 does not. This
    /// falls out of the mask rule below without a clause: a bar-line column contributes no
    /// context at all, and an empty mask is never a boundary.
    /// </para>
    /// audit/lp-geometry/probes/voice-boundary-spacing.ly measured
    /// it with no cue in the book at all: VB-VOICE is four full-size quarters whose last two sit
    /// in a plain <c>\new Voice</c>, and its boundary loses the same 0.104200 that
    /// <c>cue.column.main-to-cue</c> records. VB-OUT is the half that says the refinement is
    /// ABSENT rather than fed a smaller number — leaving a cue, where the LEFT head is the small
    /// one, costs the same 2.898044999134612 to fifteen digits instead of 2.409193907.
    /// </para>
    /// <para>
    /// ⚠️ A NULL right side is NOT a boundary — a bar line is one column for both voices, so the
    /// left voice's wish reaches it and the refinement runs. MEASURED, because the first version
    /// of this note asserted it and cited <c>barline.prev.*</c>, which are non-cue books and
    /// observe no such thing: voice-boundary-spacing.ly VBB-CTL / VBB-CUE put the same four
    /// quarters before a plain bar line with and without the last two in a CueVoice, and the
    /// last-head-to-bar-line gap is 2.787959284848899 against 2.370536764280867. The cue book's
    /// gap IS narrower, so the refinement does run here — the direction is right.
    ///   departs from: the SIZE of it. LilyPond narrows that gap by 0.417422520568032 and this
    ///     engine narrows it by the whole head-width term — 1.304200 traded for the cue head's
    ///     box, measured 0.488853432. The port improved the reading (the gap used to be wrong by
    ///     the full 0.417) without closing it.
    ///   goes away when: a CUE ITEM CAN REPORT ITS OWN STEM RANGE. ⚠️ THE LEFTOVER IS NAMED
    ///     (2026-08-04) AND IT IS NOT THIS PREDICATE'S: it is 1/14 exactly — 0.5/7, i.e.
    ///     <see cref="NoteSpacingParameters.StemSpacingCorrection"/> over the hardcoded 7 of
    ///     different_directions_correction, halved at a bar line. The earlier "3.4e-12
    ///     resemblance, do not fit it" was an artefact of subtracting the nine-digit
    ///     0.488851092; at full precision the trade is 0.488851091996604 and the difference is
    ///     1/14 to 6.4e-16. See <see cref="CalculateStemCorrectionToBarline"/> — LilyPond's
    ///     |intersect| is 4 staff positions for the cue book and 6 for the control, because
    ///     ITS cue stem is shorter, and −(6−4)/7 × 0.25 is the whole of it. Lily#'s
    ///     <see cref="StemSpacingInfo"/> does not consult <c>IsCue</c>, so it spends −3/14 on
    ///     both. MEASURED, DRIVEN AND CONTROLLED: probe voice-boundary-spacing.ly section D.
    ///   observed by: <c>cue.barline.prev.cue-head</c>, against
    ///     <c>cue.barline.prev.full-head-control</c> (opened 2026-08-03 from the numbers above,
    ///     without re-measuring). The control is EXACT and the cue records −0.071430911, which
    ///     is the unnamed 0.071428571431968 PLUS the 0.000002340 six-digit rounding of
    ///     design-13's head that already stopped <c>cue.column.step</c> — that term arriving
    ///     here by a second, independent reading is what says this gap really is spending the
    ///     head-width term and not some other quantity that happens to sit nearby.
    /// </para>
    /// <para>
    /// ⚠️ ADJACENT CUE REGIONS ARE A BOUNDARY TOO, and that is MEASURED rather than argued
    /// (2026-08-15; this note used to be a "departs from" saying the opposite was shipped).
    /// <c>cue { … } cue { … }</c> is two CueVoice contexts, and probe
    /// voice-boundary-spacing.ly section F reads the step across their shared edge as the RAW
    /// duration ideal 2.898044999134611 where the same music in ONE region reads the refined
    /// 2.513393907138011 — the two books differing by one <c>cue</c> keyword and nothing else.
    /// Observed by <c>cue.column.region-edge</c> against <c>cue.column.region-edge-control</c>;
    /// before the port the two drew an IDENTICAL column list.
    /// Since a region has no id, the edge is read off the stamp the collector leaves on the
    /// region's first note (<see cref="MusicItem.BeginsCueRegion"/>) — the same stamp LYS4012
    /// reads, so a boundary the diagnostic names is a boundary the spacing sees.
    ///   departs from: nothing measured. The remaining approximation is TWO CUE REGIONS ALIVE
    ///     AT ONCE in different voices, where a continuing (non-edge) cue item in the right
    ///     column need not belong to a region that occupies the left one. The rule below asks
    ///     that EVERY cue item in the right column begin a region, so that configuration keeps
    ///     the refinement — the pre-port behaviour — rather than losing it on an argument no
    ///     book has tested.
    ///   goes away when: a cue item carries a region id rather than an edge flag; see
    ///     <see cref="Model.MusicItem.BeginsCueRegion"/> for why it carries the edge today (the
    ///     collect resume's suffix splice adopts tails numbered by another walk).
    ///   observed by: NOTHING, and no book can observe it — nothing on disk writes two cue
    ///     regions at all in one measure, let alone simultaneously (grep 2026-08-03,
    ///     re-checked 2026-08-10 and twice on 2026-08-15).
    /// <para>
    /// ⚠️ The SECOND departure this note used to carry — simultaneous voices disagreeing about
    /// being cued, where the old loop gave up and refined — is gone, and not because it was
    /// patched: the mask rule below refines that pair for LilyPond's own reason, namely that
    /// the non-cued voice occupies both columns and its wish spans them.
    /// </para>
    /// </para>
    /// </remarks>
    private static bool CrossesVoiceBoundary(
        IEnumerable<MusicItem> leftItems, IEnumerable<MusicItem>? rightItems)
    {
        if (rightItems is null)
            return false;
        int left = ContextMask(leftItems);
        if (left == 0)
            return false;
        int right = ContextMask(rightItems);
        if (right == 0)
            return false;
        // LILYPOND-REF: lily/spacing-spanner.cc:350-393 musical_column_spacing — the pair is
        // refined once per wish whose right-items name the right column, and merge_springs
        // combines them; no such wish at all leaves the base spring.
        // ⚠️ THIS IS A DERIVATION, NOT A TRANSCRIPTION, and it rests on one premise worth
        // saying out loud: the two columns are ADJACENT. A context's wish chain links the
        // columns IT occupies, consecutively, so a context holding both ends of an adjacent
        // pair has them next to each other in its own chain and its wish spans them — while a
        // context that skips over the pair (the shared voice of pctend.log, whose wish jumps
        // the whole solo run) names some column further on and links nothing here. With a
        // column between them the pair would not be adjacent and would not be spaced as one.
        // So, for the pairs this is ever asked about, "some context occupies both columns" and
        // "some wish spans the pair" are the same statement.
        int shared = left & right;
        if (shared == 0)
            return true;
        // A context OTHER than a cue on both sides settles it: that voice occupies both columns
        // and its wish spans the pair.
        if ((shared & ~(1 << (int)VoiceContextId.Cue)) != 0)
            return false;
        // Only the cue context is shared — and a cue REGION is a context of its own, so this is
        // the one case the mask cannot answer. The pair straddles two regions exactly when no
        // region reaches back over it, i.e. when every cued item in the RIGHT column is the
        // first of its region. MEASURED: probe voice-boundary-spacing.ly section F.
        return EveryCuedItemBeginsItsRegion(rightItems);
    }

    /// <summary>Whether every cued item in a column is the FIRST of its region — so no region
    /// in this column was already open in the column before it.</summary>
    /// <remarks>
    /// The same filter <see cref="ContextMask"/> uses, because the question is about the items
    /// LilyPond makes a wish for; false for a column with no cued item at all, which cannot
    /// reach this call (the shared cue bit says both columns have one).
    /// </remarks>
    private static bool EveryCuedItemBeginsItsRegion(IEnumerable<MusicItem> items)
    {
        bool anyCue = false;
        foreach (var item in items)
        {
            if (item is not (NoteItem or ChordItem) || ContextOf(item) != VoiceContextId.Cue)
                continue;
            if (!item.BeginsCueRegion)
                return false;
            anyCue = true;
        }
        return anyCue;
    }

    internal static Spring ApplyLeftHeadWidth(
        Spring spring, IEnumerable<MusicItem> leftItems, IEnumerable<MusicItem>? rightItems = null,
        bool mergeWishAverage = false)
    {
        if (CrossesVoiceBoundary(leftItems, rightItems))
            return spring;

        // mergeWishAverage: the caller passed ONE left item per WISH (per voice
        // occupying both columns), and LilyPond merges simultaneous wishes by
        // AVERAGING their ideals — spring.cc merge_springs, whose avg_distance the
        // repo already cites for the +0.3 headroom — so the refinement's head term
        // is the wishes' MEAN, not their max. With a single wish (every single-voice
        // book, i.e. the whole note-to-note ledger) mean == max == the voice's own
        // head, so only multi-wish columns can read differently. The max arm remains
        // for the aggregate callers (the closing spring reads the column's grobs
        // through different LilyPond machinery, Staff_spacing, and is unmeasured
        // multi-voice; the same-staff cross-voice fallback likewise).
        double leftHeadEnd = 0;
        double headSum = 0;
        int headCount = 0;
        bool any = false;
        foreach (var p in leftItems)
        {
            double w = p switch
            {
                // The head's INK right edge, not its advance. LilyPond reads
                // g->extent (col, X_AXIS)[RIGHT] — a stencil extent — and the two differ:
                // a whole head advances 1.960000 but its stencil reaches 1.962002. Feeding
                // the advance made every closing gap 0.002 narrow than LilyPond's, which is
                // the whole of barline.prev.whole-note's former residual.
                // ...asked of the font the head is actually DRAWN in. A cue head is not the
                // twenty design scaled — it is the THIRTEEN design read at magstep(-4)
                // (EngravingDefaults.CueFont, the same object the renderer draws with), and
                // 1.304200 * magstep(-4) = 0.821594517 is not LilyPond's 0.815348908. Reading the
                // full-size box here was the whole of cue.column.step's +0.488851092: the
                // renderer shrank the head and the spring did not hear about it.
                //   ⚠️ A REST INSIDE A CUE STILL PRICES FULL SIZE. RestItem carries no IsCue,
                //   so there is nothing to ask; LilyPond's left grob would be the cue-sized
                //   rest. No point observes it — see the branch below.
                NoteItem or ChordItem => GlyphMetrics.GetNoteheadBBox(
                    IsCueItem(p) ? EngravingDefaults.CueFont : GlyphMetrics.Design20,
                    GetNoteValue(p)).Right,
                // A rest is drawn glyph-left-aligned at its column, so its right
                // extent from the column origin is the rest stencil's right edge.
                // ⚠️ A SPACER rest engraves nothing: LilyPond's left head is a real
                // grob read off the note column (note-spacing.cc:46-70 — the "rest"
                // object or first_head), and a spacer has neither. Pricing the glyph
                // of a rest nobody draws put a phantom half-rest 1.5 into every
                // chords-row gap (chord.symbol-width.half-spring-control caught it:
                // MEASURED, probe chord-symbol-width.ly CAL2, a staff-less row's
                // columns carry NO spacing wishes at all, so LilyPond's ideal there
                // is the bare duration spring).
                RestItem r => r.IsSpacer ? double.NaN : GlyphMetrics.GetRestBBox(GetNoteValue(p)).Right,
                _ => double.NaN
            };
            if (double.IsNaN(w))
                continue;
            leftHeadEnd = Math.Max(leftHeadEnd, w);
            headSum += w;
            headCount++;
            any = true;
        }
        if (!any)
            return spring;
        if (mergeWishAverage)
            leftHeadEnd = headSum / headCount;

        double ideal = Math.Max(EngravingDefaults.SpacingIncrement,
            spring.IdealDistance + leftHeadEnd - EngravingDefaults.SpacingIncrement);
        // LILYPOND-REF: lily/note-spacing.cc:113 base.set_ideal_distance (…) — the SETTER,
        // which leaves the duration-built compressibility alone (lily/spring.cc:131-141).
        return spring.WithIdealDistance(ideal);
    }

}
