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
    /// Creates a spring between two music items.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-basic.cc:100-130 note_spacing()
    /// LILYPOND-REF: lily/note-spacing.cc:204-315 stem_dir_correction()
    /// - ideal_distance = get_duration_space(duration)
    /// - min_distance = max(increment, skyline_collision_distance)
    /// - inverse_stretch_strength = max(0.1, ideal - min)
    /// - stem direction optical correction applied to ideal
    /// </remarks>
    public static Spring CreateSpring(MusicItem? prevItem, MusicItem? nextItem, Fraction prevDuration,
                                      NoteSpacingParameters? noteParams = null,
                                      double? baseShortestDuration = null)
    {
        var np = noteParams ?? NoteSpacingParameters.Default;

        // LILYPOND-REF: lily/spacing-basic.cc:109 note_spacing() - increment
        double defaultMin = EngravingDefaults.SpacingIncrement;

        // Skyline-based collision distance (rod)
        double skylineDistance = CalculateSkylineDistance(prevItem, nextItem, staffY: 0);

        // min_distance = max(defaultMin, skylineDistance) - ensures no collision
        double minDistance = Math.Max(defaultMin, skylineDistance);

        // LILYPOND-REF: lily/spacing-basic.cc:107 note_spacing() - duration space
        double idealDistance = CalculateDurationSpace(prevDuration,
            baseShortestDuration ?? EngravingDefaults.BaseShortestDuration);

        // --- Stem direction optical correction ---
        // LILYPOND-REF: lily/note-spacing.cc:204-315 stem_dir_correction
        idealDistance += CalculateStemCorrection(prevItem, nextItem, np);

        // LILYPOND-REF: lily/note-spacing.cc:229-264 strict_note_spacing
        // In strict mode, enforce minimum distance = duration-based ideal distance.
        // This prevents compression below proportional spacing.
        if (np.StrictNoteSpacing)
        {
            minDistance = Math.Max(minDistance, idealDistance);
        }

        // A whole-display tremolo pair whose RIGHT half carries accidentals gets the
        // Beam's minimum-length as a spacing rod between its two columns, so the
        // gapped beam keeps printable width past the accidentals. MEASURED: the
        // accidental book's three such pairs space head-origin to head-origin at
        // exactly 6.00 (m2 27.93→33.93, m3 39.88→45.88, b''–cis''' 84.82→90.82);
        // the accidental-free pair keeps its natural 4.86.
        // LILYPOND-REF: lily/beam.cc:429-449 tremolo_springs_and_rods — gated on
        //   the accidentals and on duration_log <= 0, it calls set_spacing_rods;
        //   the distance is the Beam grob's minimum-length 6.0
        //   (scm/define-grobs.scm Beam) via lily/spanner.cc:429-473 set_spacing_rods.
        minDistance = Math.Max(minDistance, TremoloPairRod(prevItem, nextItem));

        // LILYPOND-REF: lily/spacing-basic.cc note_spacing()
        //   ret.set_inverse_stretch_strength(fraction * std::max(0.1, (len - min)));
        // where min = increment_ (NOT skyline min_distance).
        // Skyline min_distance is set later via set_min_distance() but does NOT
        // affect inverse_stretch_strength. This ensures accidentals (which increase
        // skyline min_distance) don't make springs stiffer — they stretch equally.
        double inverseStretchStrength = Math.Max(0.1, idealDistance - defaultMin);

        return new Spring(idealDistance, minDistance, inverseStretchStrength);
    }

    /// <summary>The Beam grob's minimum-length (scm/define-grobs.scm Beam), the rod a
    /// whole-display tremolo pair with accidentals spans between its two columns.</summary>
    private const double TremoloPairAccidentalRod = 6.0;

    /// <summary>
    /// The tremolo pair's spacing rod between <paramref name="prev"/> and
    /// <paramref name="next"/>: the Beam's minimum-length (6.0) when they are the two
    /// halves of a whole-DISPLAY pair whose right half carries accidentals, else 0.
    /// One house for BOTH spring systems (this file's estimate and the
    /// timing-column layout), so the pair cannot drift.
    /// </summary>
    internal static double TremoloPairRod(MusicItem? prev, MusicItem? next) =>
        IsWholeTremoloPairStart(prev) && IsTremoloPairEndWithAccidentals(next)
            ? TremoloPairAccidentalRod
            : 0.0;

    /// <summary>The LEFT half of a whole-DISPLAY two-note tremolo pair (the only beam
    /// whose stems are invisible).</summary>
    private static bool IsWholeTremoloPairStart(MusicItem? item) => item switch
    {
        NoteItem { TremoloPairBeams: > 0, HasBeamStart: true } n =>
            GlyphMetrics.NoteValueOf(n.BaseDuration) <= 1,
        ChordItem { TremoloPairBeams: > 0, HasBeamStart: true } c =>
            GlyphMetrics.NoteValueOf(c.BaseDuration) <= 1,
        _ => false,
    };

    /// <summary>The RIGHT half of a two-note tremolo pair, carrying at least one
    /// accidental (LilyPond's get_accidentals reads the LAST stem's heads).</summary>
    private static bool IsTremoloPairEndWithAccidentals(MusicItem? item) => item switch
    {
        NoteItem { TremoloPairBeams: > 0, HasBeamEnd: true } n => n.Accidental != null,
        ChordItem { TremoloPairBeams: > 0, HasBeamEnd: true } c =>
            c.Notes.Any(x => x.Accidental != null),
        _ => false,
    };

    /// <summary>
    /// Calculates stem direction optical correction for spacing ([Wanske] p.138:
    /// up-stem→down-stem needs extra space, down-stem→up-stem less).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-spacing.cc:204-315 stem_dir_correction:
    /// - opposite directions, BOTH STEMS IN ONE BEAM → the knee correction, which
    ///   REPLACES the overlap one: −note_head_width · rightDir ·
    ///   knee-spacing-correction (knee_correction, :117-137, selected at :288-293)
    /// - opposite directions otherwise → correction scales with the stems' vertical
    ///   OVERLAP: min(|overlap|/7, 1) · leftDir · stem-spacing-correction
    ///   (different_directions_correction, :140-160)
    /// - same direction → only when the head ranges do NOT overlap and the gap
    ///   exceeds one staff position: ±same-direction-correction depending on
    ///   which side is lower (same_direction_correction, :162-197); skipped
    ///   when an accidental sticks out of the right side (:305-308)
    /// - a FLAG hanging from the LEFT stem (an unbeamed eighth or shorter) → no correction
    ///   of any kind (:260-266, "Correction doesn't seem appropriate when there is a large
    ///   flag hanging from the note"). Until 2026-09-03 this gate was a documented
    ///   simplification; MEASURED (2.26.0, scratch/p322/fx/w-h8-bass-fis.ly) it is the
    ///   whole of a +0.10 per bar that <c>fis,,2 fis,,8 fis,, r cis,</c> carried into the
    ///   bar line — LilyPond's flagged eighth → bar line gap is 2.567400, the flag's
    ///   skyline + the 0.3 headroom and nothing else, where the correction read 0.1653.
    /// Stem directions ARE beam-resolved — the collector bakes the beam's
    /// direction, and its identity (<see cref="NoteItem.BeamId"/>), into the items.
    /// </remarks>
    internal static double CalculateStemCorrection(MusicItem? prevItem, MusicItem? nextItem,
                                                   NoteSpacingParameters noteParams)
    {
        if (StemSpacingInfo(prevItem) is not { } l || StemSpacingInfo(nextItem) is not { } r)
            return 0;

        // LILYPOND-REF: lily/note-spacing.cc:264-266 stem_dir_correction — the left-side
        // flag gate returns before any branch below, the same-direction one included.
        if (HasHangingFlag(prevItem))
            return 0;

        int leftDir = l.StemUp ? 1 : -1;
        int rightDir = r.StemUp ? 1 : -1;

        if (leftDir != rightDir)
        {
            // LILYPOND-REF: note-spacing.cc:288-293 knee_correction replaces
            // different_directions_correction — inside ONE beam the knee branch takes over
            // entirely (LilyPond writes it as an if/else, not as a sum).
            if (l.BeamId is { } leftBeam && leftBeam == r.BeamId)
                return KneeCorrection(nextItem, rightDir, noteParams);

            // LILYPOND-REF: note-spacing.cc:140-160 different_directions_correction
            double lo = Math.Max(l.StemMin, r.StemMin);
            double hi = Math.Min(l.StemMax, r.StemMax);
            if (hi <= lo)
                return 0;
            // Overlap in staff positions (half-spaces); 7 is LilyPond's hardcoded scale.
            return Math.Min((hi - lo) / 7.0, 1.0) * leftDir * noteParams.StemSpacingCorrection;
        }

        // LILYPOND-REF: note-spacing.cc:305-308 — same-direction correction only
        // without accidentals sticking out of the right hand side.
        if (HasAccidental(nextItem))
            return 0;

        // LILYPOND-REF: note-spacing.cc:162-197 same_direction_correction —
        // applies only when the two head ranges are disjoint by more than one
        // staff position; sign depends on which side is lower.
        bool headsOverlap = Math.Max(l.HeadMin, r.HeadMin) <= Math.Min(l.HeadMax, r.HeadMax);
        if (headsOverlap)
            return 0;

        int lowest = l.HeadMin > r.HeadMax ? 1 : -1; // +1 = RIGHT side is lower
        double delta = lowest > 0 ? l.HeadMin - r.HeadMax : r.HeadMin - l.HeadMax;
        return delta > 1 ? -lowest * noteParams.SameDirectionCorrection : 0;
    }

    /// <summary>
    /// An unbeamed eighth or shorter — a note whose stem carries a FLAG. Read off the
    /// item the way <see cref="ItemSkylineFactory"/> reads it for the flag's box: the
    /// collector has resolved beaming before any spacing runs.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/note-spacing.cc:264-266 stem_dir_correction —
    /// <c>Stem::duration_log (stem) &gt; 2 &amp;&amp; !Stem::get_beam (stem)</c>.</remarks>
    private static bool HasHangingFlag(MusicItem? item) => item switch
    {
        NoteItem { IsBeamed: false } n => GetNoteValue(n) >= 8,
        ChordItem { IsBeamed: false } c => GetNoteValue(c) >= 8,
        _ => false,
    };

    /// <summary>
    /// The optical correction for a KNEE — two columns of one beam whose stems point
    /// opposite ways. Unlike the overlap correction it does not scale with anything the
    /// two stems share: it is one note-head width, signed by the RIGHT stem's direction,
    /// so an up→down pair is pushed apart by as much as a down→up pair is pulled together.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-spacing.cc:117-137 knee_correction —
    /// <c>-note_head_width * get_grob_direction (right_stem) * knee-spacing-correction</c>,
    /// where note_head_width is the right stem's SUPPORT HEAD extent[RIGHT] taken in the
    /// column's frame, less the stem's own thickness (:131), and the spacing increment
    /// stands in when that stem has no head at all (:120).
    /// <para>
    /// MEASURED (2.26.0, audit/lp-geometry/probes/beam-column-spacing.ly): for a black head
    /// the term is 1.304200 − 0.130000 = 1.174200, and LilyPond's kneed bar
    /// <c>c'8 c' c' c'''</c> has column gaps 2.5042 / 2.5042 / 3.6784 — the last one wide by
    /// exactly that. Perturbing knee-spacing-correction to 0 / 0.5 / 2 moves both signs of
    /// the term in proportion (2.5042 flat / ±0.5871 / +2.3484), which is what says this is
    /// the term and not the overlap branch beside it: that branch never reads this property.
    /// The down→up gap saturates at 1.8042 under a large correction because the spring's
    /// MINIMUM distance stops it — the rod, not this term.
    /// </para>
    /// </remarks>
    private static double KneeCorrection(MusicItem? rightItem, int rightDir,
                                         NoteSpacingParameters noteParams)
    {
        // LILYPOND-REF: note-spacing.cc:120 knee_correction's note_head_width seed — the
        // spacing increment (Spacing_options::increment_) when the stem carries
        // no head. Written as LilyPond writes it. Nothing head-less reaches here today
        // (StemSpacingInfo already returned null for it), but that is a property of this
        // caller, not of the rule.
        double noteHeadWidth = EngravingDefaults.SpacingIncrement;

        if (SupportHeadRightExtent(rightItem) is { } headRight)
        {
            noteHeadWidth = headRight;
            // LILYPOND-REF: note-spacing.cc:131 note_head_width -= Stem::thickness (right_stem)
            // — and the stem's thickness is a LINE thickness, not a head quantity:
            // LILYPOND-REF: lily/stem.cc:909-913 Stem::thickness = thickness · line_thickness
            // (scm/define-grobs.scm:3469 Stem (thickness . 1.3) over the 0.1 ss line).
            noteHeadWidth -= EngravingDefaults.StemThickness;
        }

        return -noteHeadWidth * rightDir * noteParams.KneeSpacingCorrection;
    }

    /// <summary>
    /// The right edge of the stem's support head, measured in its COLUMN's frame — the
    /// quantity <c>head-&gt;extent (head-&gt;get_column (), X_AXIS)[RIGHT]</c> reads. Null
    /// for an item with no head.
    /// </summary>
    /// <remarks>
    /// The support head is the one the stem starts from —
    /// LILYPOND-REF: lily/stem.cc:179-204 Stem::support_head, the head with the widest part
    /// inside the stem, which for a chord of one glyph is the first, i.e. the extreme head
    /// in the stem's direction. That head
    /// is never the displaced one: <see cref="ChordHeadPositioning"/> gives it offset 0 and
    /// walks the reversals off it, so its column-frame right edge is the head glyph's own
    /// right edge — the same <c>ell</c> that file takes from stem.cc:684.
    /// </remarks>
    private static double? SupportHeadRightExtent(MusicItem? item) => item switch
    {
        NoteItem or ChordItem => GlyphMetrics.GetNoteheadBBox(GetNoteValue(item)).Right,
        _ => null,
    };

    /// <summary>
    /// Stem-direction optical correction for the spring that runs from a note column
    /// INTO a bar line, where the bar line stands in for the right-hand stem.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-spacing.cc:281-286 stem_dir_correction — when the right
    /// column carries a bar line, LilyPond synthesises the right-hand stem from the bar:
    /// <code>
    ///   stem_dirs[RIGHT] = -stem_dirs[LEFT];
    ///   stem_posns[RIGHT] = bar_yextent;
    ///   stem_posns[RIGHT] *= 2;
    /// </code>
    /// so the directions are opposite BY CONSTRUCTION and
    /// different_directions_correction always runs, then is HALVED (:299-300).
    /// ⚠️ THE THREE ADDRESSES IN THIS BLOCK WERE ALL WRONG UNTIL 2026-08-04 (session 84),
    /// each by about thirty-eight lines: :243-248 is the accidentals check and the stemless
    /// guard, :263-264 is the large-flag gate, and :200-201 is a comment. The citation
    /// ratchet only checks that a symbol name shares the line with an address, so a stale
    /// range that names the right function survives it — the range has to be opened.
    /// LILYPOND-REF: lily/staff-spacing.cc:73 Staff_spacing::bar_y_positions — the bar's Y extent divided
    /// by the staff space, i.e. staff-spaces; the <c>*= 2</c> above converts it to staff
    /// POSITIONS (half-spaces), the unit StemSpacingInfo already reports.
    ///
    /// A plain bar line spans the staff, so on a normal five-line staff that extent is
    /// ±2 staff-spaces → ±4 staff positions. (LilyPond takes this from the bar grob and
    /// only for glyphs beginning "|" or "."; this path is the ordinary staff bar, and
    /// like the item→bar-line skyline beside it, it assumes the standard staff.)
    ///
    /// Returns 0 when the left column has no visible stem — a whole note or a rest —
    /// which is LilyPond's `if (!stem || Stem::is_invisible (stem)) return;` (:248-249)
    /// and is why `c'1 c'1` needs no correction at all.
    /// </remarks>
    internal static double CalculateStemCorrectionToBarline(
        MusicItem? prevItem, NoteSpacingParameters noteParams)
    {
        if (StemSpacingInfo(prevItem) is not { } l)
            return 0;

        // The left-side flag gate stands BEFORE the bar branch in stem_dir_correction's
        // loop, so a flagged note before a bar line takes no correction either.
        // LILYPOND-REF: lily/note-spacing.cc:264-266 stem_dir_correction.
        if (HasHangingFlag(prevItem))
            return 0;

        // The bar line's Y extent in staff positions: the staff's own half-height.
        const double barHalfHeightPositions = 4.0;

        int leftDir = l.StemUp ? 1 : -1;
        double lo = Math.Max(l.StemMin, -barHalfHeightPositions);
        double hi = Math.Min(l.StemMax, barHalfHeightPositions);
        if (hi <= lo)
            return 0;

        double correction =
            Math.Min((hi - lo) / 7.0, 1.0) * leftDir * noteParams.StemSpacingCorrection;

        // LILYPOND-REF: note-spacing.cc:263-264 — halved when the right side is a bar.
        return correction * 0.5;
    }

    /// <summary>
    /// Merges the per-voice stem-direction spacing wishes for the column pair
    /// (<paramref name="tLeft"/> → <paramref name="tRight"/>) into a single spring.
    /// Each voice with a note/chord column at BOTH moments contributes one wish:
    /// the duration-proportional <paramref name="baseSpring"/> refined by that
    /// voice's stem-direction correction. The wishes are combined with
    /// <see cref="Spring.MergeSprings"/>, exactly as LilyPond merges the simultaneous
    /// voices' spacing wishes for a musical column pair.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-spanner.cc:322-393 Spacing_spanner::musical_column_spacing
    ///   — collect each voice's Note_spacing wish, then <c>spring = merge_springs (springs)</c>.
    /// LILYPOND-REF: lily/spring.cc:101-131 merge_springs.
    /// For monophonic music exactly one voice contributes, so the result equals
    /// that single wish (base + its own correction) — identical to applying the
    /// correction directly, which keeps all single-voice spacing unchanged.
    /// </remarks>
    internal static Spring MergeVoiceStemWishes(
        Spring baseSpring, IReadOnlyList<Measure> voices,
        Fraction tLeft, Fraction tRight, NoteSpacingParameters noteParams)
    {
        var wishes = new List<Spring>();
        foreach (var voice in voices)
        {
            var left = NoteColumnAt(voice, tLeft);
            var right = NoteColumnAt(voice, tRight);
            if (left is null || right is null)
                continue;

            double corr = CalculateStemCorrection(left, ApproachColumn(right), noteParams);
            // LILYPOND-REF: lily/note-spacing.cc:111-113 — stem_dir_correction adjusts the
            // ideal and hands it to base.set_ideal_distance, which does not touch either
            // strength (lily/spring.cc:131-141). The clamp is at ZERO, not at the minimum
            // (:113 max (0.0, ideal)) — the caller has already replaced the base spring's
            // minimum with the skyline distance, and a knee pull may legitimately take
            // the ideal below the old increment floor (spacing-correction-accidentals.ly:
            // the down→up pair's ideal is 1.330, under the 1.2 + 0.3 headroom the old
            // min-clamped spelling froze it at).
            wishes.Add(corr != 0
                ? baseSpring.WithIdealDistance(
                    Math.Max(0.0, baseSpring.IdealDistance + corr))
                : baseSpring);
        }
        return wishes.Count > 0 ? Spring.MergeSprings(wishes) : baseSpring;
    }

    /// <summary>
    /// Merges the per-voice stem-direction spacing wishes for the pair
    /// (last note column at <paramref name="tLeft"/> → the bar line) into a single
    /// spring — the bar-line counterpart of <see cref="MergeVoiceStemWishes"/>.
    /// Every voice sounding a note/chord column at that moment contributes one wish:
    /// the base spring refined by that voice's correction against the bar line's
    /// virtual stem.
    /// </summary>
    /// <remarks>
    /// LilyPond runs the note → bar-line pair through the SAME per-voice merge as a
    /// note → note pair: spacing-spanner.cc:183-199 generate_pair_spacing dispatches on
    /// the LEFT column being musical, so a musical → breakable pair also goes to
    /// musical_column_spacing (:322-393), which collects one Note_spacing wish per voice
    /// and ends in <c>merge_springs</c>. The wish itself carries the bar-line branch of
    /// the stem correction (note-spacing.cc:243-264), ported as
    /// <see cref="CalculateStemCorrectionToBarline"/>.
    ///
    /// Verified on LilyPond 2.24.4, last-column → bar-line-column distance over one 4/4
    /// bar of quarters: stems up throughout 3.393249, stems down throughout 3.192257,
    /// and the two as simultaneous voices 3.292753 — exactly their average, which is
    /// what merge_springs does when the wishes share a min distance.
    ///
    /// This depends on the voice-forced stem directions being resolved into the model
    /// before spacing (MeasureCollector.ResolveVoiceStemDirections); with the
    /// pitch-derived directions it saw previously, the merge moved the spring the wrong
    /// way in polyphony.
    ///
    /// LILYPOND-REF: lily/note-spacing.cc:113 — the corrected ideal is clamped at 0.0
    /// (not at the min distance), matching the single-voice path this replaces.
    /// </remarks>
    internal static Spring MergeVoiceStemWishesToBarline(
        Spring baseSpring, IReadOnlyList<Measure> voices,
        Fraction tLeft, NoteSpacingParameters noteParams)
    {
        var wishes = new List<Spring>();
        foreach (var voice in voices)
        {
            if (NoteColumnAt(voice, tLeft) is not { } left)
                continue;

            double corr = CalculateStemCorrectionToBarline(left, noteParams);
            // LILYPOND-REF: lily/note-spacing.cc:111-113, as in MergeVoiceStemWishes.
            wishes.Add(corr != 0
                ? baseSpring.WithIdealDistance(Math.Max(0, baseSpring.IdealDistance + corr))
                : baseSpring);
        }
        return wishes.Count > 0 ? Spring.MergeSprings(wishes) : baseSpring;
    }

    /// <summary>
    /// The optical correction a DOWN stem standing just after a bar line earns, taken as
    /// the maximum over the columns at that moment.
    /// </summary>
    /// <remarks>
    /// "A stem following a bar-line creates an optical illusion similar to the one
    /// mentioned in note-spacing.cc. We correct for it here." The correction is the length
    /// of the overlap between the stem and the bar line, over 7, clamped to 1, times
    /// StaffSpacing's stem-spacing-correction — and it applies ONLY to a down stem, so an
    /// up stem after a bar line earns nothing.
    /// <para>
    /// UNITS: staff-spacing works in staff-SPACES here (it divides the bar's Y extent by
    /// the staff space, giving ±2), whereas note-spacing.cc multiplies that same extent by
    /// 2 and works in staff POSITIONS. Both then divide by 7, so the two are NOT
    /// interchangeable — see CalculateStemCorrectionToBarline, which is the positions one.
    /// StemSpacingInfo reports positions, hence the halving below.
    /// </para>
    /// <para>
    /// Verified on 2.24.4, bar-line ink right edge → next notehead ink left edge with
    /// `c'4 d' e' f'` before the bar line:
    ///   `g'4 a' b' c''`            up stems            0.900000  (correction 0)
    ///   `\clef bass c,4 d, e, f,`  up stems, clef      0.900000  (correction 0)
    ///   `a''4 b'' c''' d'''`       down, head pos 6    1.042857  (correction 0.142857)
    ///   `\clef bass g4 a b c'`     down, head pos 3    1.089365  (correction 0.189365)
    /// The last two reproduce exactly: pos 6 gives a stem spanning (-0.5, 2.813894) ss,
    /// clipped by the bar to (-0.5, 2.0), length 2.5 → 2.5/7 × 0.4 = 0.14285714; pos 3
    /// gives (-2.0, 1.313894), already inside the bar, length 3.313894 → 0.18936537.
    /// This also disproves the reading that the residual came from the CLEF: a clef with
    /// up stems earns nothing, and a down stem with no clef earns a third value.
    /// </para>
    /// LILYPOND-REF: lily/staff-spacing.cc:36-67 optical_correction, :69-93
    ///   bar_y_positions, :95-110 next_notes_correction, :206-208 (applied to BOTH
    ///   fixed and ideal).
    /// </remarks>
    internal static double BarlineToNextNotesCorrection(IReadOnlyList<MusicItem>? nextItems)
    {
        if (nextItems == null)
            return 0;
        double maxOptical = 0;
        foreach (var item in nextItems)
            maxOptical = Math.Max(maxOptical, BarlineToStemOpticalCorrection(item));
        return maxOptical;
    }

    /// <remarks>LILYPOND-REF: lily/staff-spacing.cc:43-67 Staff_spacing::optical_correction.</remarks>
    private static double BarlineToStemOpticalCorrection(MusicItem? item)
    {
        if (StemSpacingInfo(item) is not { } s)
            return 0;

        // LILYPOND-REF: lily/staff-spacing.cc:55 — `d == DOWN` only.
        if (s.StemUp)
            return 0;

        // A plain bar line spans the staff: ±2 staff-spaces, i.e. ±4 staff positions.
        // LILYPOND-REF: lily/staff-spacing.cc:78-90 bar_y_positions — only for glyphs
        //   beginning "|" or "."; an empty interval yields no correction at all.
        const double barHalfHeightPositions = 4.0;

        double lo = Math.Max(s.StemMin, -barHalfHeightPositions);
        double hi = Math.Min(s.StemMax, barHalfHeightPositions);
        if (hi <= lo)
            return 0;

        // Positions → staff-spaces, because this formula is the staff-spacing one.
        double overlapStaffSpaces = (hi - lo) / 2.0;
        return Math.Min(Math.Abs(overlapStaffSpaces / 7.0), 1.0) * StaffSpacingStemCorrection;
    }

    /// <summary>
    /// The note or chord column starting exactly at moment <paramref name="t"/> in
    /// <paramref name="measure"/>, or null if that voice rests (or has no column)
    /// there. Zero-duration change items sharing the moment are skipped.
    /// </summary>
    private static MusicItem? NoteColumnAt(Measure measure, Fraction t)
    {
        var cur = Fraction.Zero;
        foreach (var item in measure.Items)
        {
            // ⚠️ NOT A GRACE COLUMN. A grace takes no measure time, so it shares the moment of
            // the note it leads and stands BEFORE that note in the item list — first past the
            // post for a scan shaped like this one. The wish being refined belongs to the MAIN
            // note's column (LilyPond's Note_spacing reads the stem of the column the wish was
            // filed from, and a grace's stem lives in the grace part of the moment), so taking
            // a grace stem here priced the pair off the wrong stem: MEASURED, it moved
            // `c4 grace { d16 } f4 g4 a4`'s f→g spring by 0.250000 while the grace's own
            // approach stayed exact.
            if (cur == t && !item.GraceTime && item is NoteItem or ChordItem)
                return item;
            if (cur > t)
                return null;
            cur += item.Duration;
        }
        return null;
    }

    /// <summary>
    /// Stem and head vertical ranges (staff positions, +up) used by the stem
    /// direction correction, and the identity of the beam the stem hangs from
    /// (<see cref="NoteItem.BeamId"/>; null when unbeamed). Null for stemless items
    /// (rests, whole notes) — LilyPond's <c>if (!stem || Stem::is_invisible (stem))
    /// return;</c> at note-spacing.cc:248-249.
    /// <para>
    /// The SAME range is the stem's box in the column's horizontal skyline
    /// (<see cref="ItemSkylineFactory"/>) — LilyPond reads one Y-extent for both, so
    /// this stays one house. The two are otherwise unrelated mechanisms: the optical
    /// correction moves the spring's IDEAL, the skyline sets its MINIMUM.
    /// </para>
    /// <para>
    /// ✔ A CUE ITEM GETS A CUE RANGE (session 85). It used to get a full-size one, which the
    /// ledger measured: LilyPond's cue stem is shorter — dumped, its Y-extent is
    /// (0.0 . 2.4052059400555286) against a full stem's (−1.0 . 2.3138) — so at a bar line it
    /// overlaps the bar's band by 4 staff positions where the full stem overlaps by 6, and the
    /// correction differs by exactly (6−4)/7 × 0.25 = 1/14. That 1/14 was the whole of what
    /// ledger <c>cue.barline.prev.cue-head</c> recorded beyond the metrics table's rounding.
    /// MEASURED, DRIVEN AND CONTROLLED: probe voice-boundary-spacing.ly section D.
    /// </para>
    /// <para>
    /// ✔ THE LAW IS MEASURED (probe section E), and it is NOT "multiply the range by
    /// magstep(−4)". LILYPOND-REF: ly/engraver-init.ly:436 CueVoice — <c>\override
    /// Stem.length-fraction = #(magstep -4)</c>, applied at lily/stem.cc:557
    /// <c>length *= length-fraction</c> AFTER the shortening at :541-554. Three parts, and
    /// only the first is a scale — the port spends one line on each:
    /// <list type="number">
    /// <item>the length from the head's CENTRE is <c>(7 − shorten) × magstep(−4)</c> — measured
    ///   on a middle-line note as 6.666666666666667 × magstep = 4.199736832982911, equal to the
    ///   dumped value as doubles. <see cref="EngravingDefaults.CueStemDetails"/> carries the
    ///   fraction into the ONE length house;</item>
    /// <item>the rule that a stem outside the staff reaches the middle line then FLOORS it.
    ///   It is inactive at full size and active once scaled: a scaled g′′ stem would stop at
    ///   +0.590276325367943 and LilyPond stops it at 0.
    ///   <see cref="StemCalculator.CalculateStemEndY"/> already ran that rule after the length,
    ///   so it needed nothing — but Lily#'s OWN 2.5 floor below it did (see there);</item>
    /// <item><c>stem-begin-position</c> does NOT scale — 0.3724 → 0.18958811988894286, a
    ///   ratio of 0.509098 against magstep's 0.629961. It is the design-13 glyph's own
    ///   attachment, exactly as the head WIDTH is (0.815348908 is not 1.304200 × magstep), and
    ///   <see cref="StemBeginPosition"/> asks the cue FONT for it.</item>
    /// </list>
    /// ⚠️ EIGHTHS ARE STILL OPEN: a flagged stem is lengthened to carry its flag and the flag
    /// scales by its own font size, so the two terms are not one product and the probe does not
    /// separate them (4.039985 measured against this fraction's 4.252234 — nearer than the
    /// 6.750000 a full-size stem gave, and still not the law). Measure the flag term before
    /// porting anything about eighths.
    /// ⚠️ BEAMED CUES ARE UNTOUCHED: a beamed stem ends on the quanter's beam, and
    /// <see cref="BeamScoringProblem"/> takes a <c>lengthFraction</c> that only the grace and
    /// tab callers pass. This method's <c>BeamId</c> hands beamed columns to that path.
    /// </para>
    /// </summary>
    /// <param name="stemUpOverride">The direction to read the band at, instead of the item's
    /// own. ⚠️ FOR ONE CALLER AND ONE REASON: the collect-phase beam bake
    /// (<c>MeasureCollector.ResolveBeamStemDirections</c>) knows the direction it is ABOUT to
    /// stamp, and asking this house for the band at that direction is the only thing it needed
    /// the stamped item for. Passing the direction instead of materialising the stamp is what
    /// lets the bake build each note ONCE (MEASURED, session 192: the two-pass shape cost
    /// 3.00 MB of a 47.7 MB perf-plain1k keystroke, an extra NoteItem per beamed note).
    /// Null — every other caller — reads the item's own <c>StemUp</c>, unchanged.</param>
    internal static (bool StemUp, double StemMin, double StemMax, double HeadMin, double HeadMax,
                    int? BeamId)?
        StemSpacingInfo(MusicItem? item, bool? stemUpOverride = null)
    {
        switch (item)
        {
            case NoteItem n:
            {
                bool stemUp = stemUpOverride ?? n.StemUp;
                int noteValue = n.BaseDuration.Denominator;
                if (n.BaseDuration.Numerator != 1) noteValue = 1;
                if (noteValue < 2)
                    return null; // whole notes have no stem (Stem::is_invisible)
                // The stem's y-extent runs from where it MEETS THE HEAD (not the head
                // centre) to the tip; the head-side end sits a stem-attachment offset
                // off centre. LILYPOND-REF: lily/stem.cc:934-963.
                double beginPos = StemBeginPosition(n.StaffPosition, stemUp, noteValue, n.IsCue);
                double endPos = StemEndPosition(n.StaffPosition, stemUp, noteValue, n.StaffPosition, n.IsCue);
                // A beamed stem's band is the PURE beamed one: the tip carries the beam
                // group's united reach (baked at collect time), the head side stays this
                // stem's own — the overshoot clip in one number. Every reader of this
                // house wants exactly that: LilyPond's skyline boxes, its note-spacing
                // stem correction and its staff-spacing optical correction all read
                // pure_y_extent, whose beamed branch this is.
                // LILYPOND-REF: lily/stem.cc:387-447 Stem::internal_pure_height;
                // LILYPOND-REF: lily/note-spacing.cc:272-273 stem_dir_correction —
                //   stem->pure_y_extent (stem, 0, INT_MAX) * (2 / ss);
                // LILYPOND-REF: lily/staff-spacing.cc:55-59 optical_correction —
                //   the same pure_y_extent, intersected with the bar's band.
                if (n.PureBeamedStemTip is { } beamedTip)
                    endPos = beamedTip;
                return (stemUp,
                    Math.Min(beginPos, endPos), Math.Max(beginPos, endPos),
                    n.StaffPosition, n.StaffPosition, n.BeamId);
            }
            case ChordItem c when c.Notes.Length > 0:
            {
                bool stemUp = stemUpOverride ?? c.StemUp;
                int noteValue = c.BaseDuration.Denominator;
                if (c.BaseDuration.Numerator != 1) noteValue = 1;
                if (noteValue < 2)
                    return null;
                int minPos = c.Notes.Min(x => x.StaffPosition);
                int maxPos = c.Notes.Max(x => x.StaffPosition);
                int tipPos = stemUp ? maxPos : minPos;
                // Head-side end: the reference head is the one the stem starts from
                // (lowest for an up stem, highest for a down stem), offset by the
                // stem attachment. LILYPOND-REF: lily/stem.cc:934-963.
                double beginPos = StemBeginPosition(stemUp ? minPos : maxPos, stemUp, noteValue, c.IsCue);
                double endPos = StemEndPosition(tipPos, stemUp, noteValue, tipPos, c.IsCue);
                // The PURE beamed tip, exactly as in the NoteItem arm above.
                // LILYPOND-REF: lily/stem.cc:387-447 Stem::internal_pure_height.
                if (c.PureBeamedStemTip is { } beamedTip)
                    endPos = beamedTip;
                return (stemUp,
                    Math.Min(beginPos, endPos), Math.Max(beginPos, endPos),
                    minPos, maxPos, c.BeamId);
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// Where the stem MEETS THE HEAD, in staff positions (+up) — the head-side end of
    /// the stem's y-extent. Not the head centre: the stem attaches a fraction of the
    /// head height off centre (up-stem above, down-stem below).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/stem.cc:934-963 internal_calc_stem_begin_position — :950 takes the
    ///   reference head's staff position, :954-959 add
    ///   <c>head_height.linear_combination (y_attach) * 2 / ss</c>.
    /// LILYPOND-REF: lily/note-head.cc:164-196 get_stem_attachment — the OTHER half of the
    ///   round trip: LilyPond NORMALISES the font's attachment point out of the FONT's box
    ///   (<c>att = 2 (wxwy − centre) / length</c>, over
    ///   <c>b = fm-&gt;get_indexed_char_dimensions (k)</c>), and stem.cc:954-959 puts it back
    ///   through <c>head-&gt;extent (head, Y_AXIS)</c> — the GROB's extent.
    /// <para>
    /// ⚠️ THIS METHOD IS NOT A LINE-FOR-LINE PORT, and the licence for collapsing it is a
    /// MEASUREMENT rather than an algebraic identity. LilyPond's two steps read TWO DIFFERENT
    /// EXPRESSIONS for the head's box — the font's char dimensions on the way out, the grob's
    /// Y-extent on the way back — and they cancel only when those two agree. For a plain
    /// notehead they do, and that was measured, not assumed: probe
    /// notehead-stem-attachment.ly dumps the grob extent as ±0.545 against the font table's
    /// ±0.545, and the resulting stem-begin-position as the attachment point itself. So this
    /// returns the attachment point directly. ⇒ IF A HEAD EVER GETS A STENCIL EXTENT THAT IS
    /// NOT ITS FONT BOX (a styled head, a scaled stencil, a grob with a custom stencil), this
    /// collapse stops being valid and the two steps have to be spelled out. Lily# has ONE box
    /// per head today, which is why writing them out would read as a round trip to nowhere.
    /// </para>
    /// <para>
    /// ✔ MEASURED ON THE LILYPOND THIS CORPUS USES (2.26.0), probe
    /// notehead-stem-attachment.ly, session 85 — both heads, on the middle line where the head
    /// position contributes nothing, and again one space lower as the falsifier:
    /// <code>
    ///                 stem-attachment       Y-extent   stem-begin-position
    ///   black s2      0.341651376146789     ±0.545     −0.372400   (= 0.186200 × 2)
    ///   half  s1      0.475229357798165     ±0.545     −0.518000   (= 0.259000 × 2)
    ///   the same heads at staff position −2, stem down:  −0.6276 and −0.4820
    ///                                                    (= −2 + the offset, to 15 digits)
    /// </code>
    /// ⚠️ THIS METHOD HELD TWO SPELLINGS OF THAT ONE QUANTITY UNTIL THE PROBE ABOVE. It kept
    /// LilyPond's normalised attachment as a pair of constants (black 0.34147639283381404,
    /// half 0.4752405486932206) and put them back through our own box, which gave 0.372209268
    /// and 0.518012198 — near, and not the dumps. THE BOX WAS NEVER THE PROBLEM: ours is
    /// ±0.545 and so is LilyPond's. The constants were, and their own doc comment said why —
    /// they were "dumped on LilyPond 2.24.4", and 2.26.0 REBUILT Emmentaler. A number copied
    /// out of one release and read against another font is stale in a way no arithmetic can
    /// show; only asking the version in use can. Reading the font removes the vintage
    /// entirely — there is now nothing here to go stale when the bundled font moves.
    /// </para>
    /// <para>
    /// ⚠️ THE <c>dir *</c> BELOW IS A SECOND DEPARTURE, and it is measured too. LilyPond does
    /// not multiply by the direction: <c>get_stem_attachment</c> asks the font for a point PER
    /// DIRECTION (<c>fm-&gt;attachment_point (key, dir)</c>) and negates only when that lookup
    /// says <c>rotate</c> (note-head.cc:182-192), so a font is free to give a head a down
    /// attachment that is not the mirror of its up one. Lily#'s extracted table holds the UP
    /// point only, so a sign flip is all it CAN do. That it is the right answer for these two
    /// heads was measured rather than assumed — the probe reads both directions:
    /// <c>(1.0 . 0.341651376146789)</c> for the up stem against
    /// <c>(−1.0 . −0.341651376146789)</c> for the down, and the begin positions come out
    /// −2 + 0.3724 and 0 − 0.3724. ⇒ The limit is that it has been measured for
    /// <c>noteheads.s1</c> and <c>s2</c> and nothing else; a head style whose font disagrees
    /// would need the down point extracted.
    /// </para>
    /// <para>
    /// A CUE HEAD THEREFORE NEEDS NO SPECIAL CASE, only its own font: design 13's attachment
    /// is 0.150476, and at magstep(−4) that is LilyPond's 0.18958811988894286 (probe
    /// voice-boundary-spacing.ly section E (3)). Scaling design 20's 0.3724 would have given
    /// 0.234598 and been wrong — the head WIDTH is not a scale either.
    /// </para>
    /// </remarks>
    private static double StemBeginPosition(int headPosition, bool stemUp, int noteValue,
        bool isCue = false)
    {
        var font = isCue ? EngravingDefaults.CueFont : GlyphMetrics.Design20;
        int dir = stemUp ? 1 : -1;
        return headPosition
               + dir * GlyphMetrics.GetNoteheadStemAttachment(font, noteValue).Y * 2.0;
    }

    /// <summary>
    /// Unbeamed stem end in staff positions (+up), via the LilyPond stem-length
    /// rules (stem.cc internal_calc_stem_end_position).
    /// </summary>
    private static double StemEndPosition(int attachPos, bool stemUp, int noteValue,
        int staffPosition, bool isCue = false)
    {
        // StemCalculator works in the renderer's Y-down staff-space frame with
        // the staff middle at staffTopDown + 2; use middle = 0 → staffTopDown = −2.
        double attachY = -attachPos * 0.5;
        double endY = StemCalculator.CalculateStemEndY(
            attachY, stemUp, staffTopDown: -2.0,
            StemCalculator.GetDurationLog(noteValue), staffPosition,
            isCue ? EngravingDefaults.CueStemDetails : null);
        return -endY * 2.0;
    }

}
