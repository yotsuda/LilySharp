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
    /// Creates a spring for grace note spacing with tighter parameters.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-basic.cc:163-180 grace note spring
    /// LILYPOND-REF: scm/define-grobs.scm:1721 GraceSpacing
    /// Grace notes use: spacing-increment=0.8, shortest-duration-space=1.6,
    /// inverse_stretch_strength = increment / 2.0
    /// </remarks>
    public static Spring CreateGraceSpring(Fraction graceDuration,
                                            GraceSpacingParameters? graceParams = null,
                                            double? baseShortestDuration = null)
    {
        var gp = graceParams ?? GraceSpacingParameters.Default;

        double durationValue = graceDuration.ToDouble();
        if (durationValue <= 0)
            durationValue = gp.BaseShortestDuration;

        // LILYPOND-REF: lily/grace-spacing-engraver.cc — use per-group common shortest duration
        double bsd = baseShortestDuration ?? gp.BaseShortestDuration;

        // Same Gourlay formula as regular notes, but with grace parameters
        double ratio = durationValue / bsd;
        double spaceFactor = ratio < 1.0
            ? gp.ShortestDurationSpace + ratio - 1.0
            : gp.ShortestDurationSpace + Math.Log2(ratio);

        double idealDistance = spaceFactor * gp.SpacingIncrement;
        double minDistance = gp.SpacingIncrement;

        // LILYPOND-REF: spacing-basic.cc:174
        // inverse_stretch_strength = increment / 2.0 (more rigid than normal)
        double inverseStretchStrength = gp.SpacingIncrement / 2.0;

        return new Spring(idealDistance, minDistance, inverseStretchStrength);
    }

    /// <summary>
    /// Calculates the common shortest duration within a grace note group.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/grace-spacing-engraver.cc — common-shortest-duration per grace sequence
    /// Each grace group independently determines its base shortest duration,
    /// rather than using a global default. This ensures that a group of sixteenth
    /// grace notes spaces differently from a group of eighth grace notes.
    /// </remarks>
    public static double CalculateGraceGroupShortestDuration(
        ImmutableArray<GraceColumnInfo> notes)
    {
        double shortest = double.MaxValue;

        foreach (var note in notes)
        {
            // The MOMENT, dots included — see GraceColumnInfo.Length. LilyPond compares
            // grace_part_ moments here, and a dotted eighth is longer than a plain one.
            double dur = note.Length.ToDouble();
            if (dur > 0 && dur < shortest)
                shortest = dur;
        }

        // Fall back to default grace duration (eighth note)
        return shortest < double.MaxValue
            ? shortest
            : GraceSpacingParameters.Default.BaseShortestDuration;
    }

    /// <summary>
    /// Where a grace run's columns sit: an offset per grace from the run's FIRST column,
    /// plus the distance from the LAST grace column to the main note's column.
    /// </summary>
    /// <remarks>
    /// One object because the run is one chain: the reservation, the drawn heads and the
    /// beam quanter's x frame all have to read the same numbers. Until 2026-08-01 they read
    /// four different ones — see <see cref="GraceColumns"/>.
    /// <para>
    /// <c>Gaps</c>, one per column (the last is the gap to the main note), is what each gap
    /// gives under compression: its ROD and its inverse compress strength. Default for a
    /// run that stays rigid.
    /// </para>
    /// </remarks>
    internal readonly record struct GraceColumnLayout(
        ImmutableArray<double> Offsets, double ToMain,
        ImmutableArray<(double Rod, double InverseCompress)> Gaps = default)
    {
        /// <summary>The natural length of gap <paramref name="k"/>.</summary>
        public double Gap(int k) => k + 1 < Offsets.Length ? Offsets[k + 1] - Offsets[k] : ToMain;

        /// <summary>First grace column → the main note's column.</summary>
        public double Span => (Offsets.IsDefaultOrEmpty ? 0 : Offsets[^1]) + ToMain;
    }

    /// <summary>
    /// A grace run's column positions, LilyPond's way: one spring per column, floored by the
    /// two columns' facing separation skylines.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LilyPond has no grace-column WIDTH. Every gap of the run — including the last grace to
    /// the main note, which is not a junction of its own — is
    /// <c>max(ideal, min_dist + 0.3)</c>:
    /// </para>
    /// <list type="bullet">
    /// <item>LILYPOND-REF: lily/spacing-basic.cc:163-180 <c>Spacing_spanner::note_spacing</c> —
    ///   when <c>delta_t.grace_part_</c> is non-zero the spring's options come from the
    ///   GraceSpacing grob: <c>len = grace_opts.get_duration_space (delta_t.grace_part_)</c>.</item>
    /// <item>LILYPOND-REF: lily/spacing-options.cc:71-107 <c>Spacing_options::get_duration_space</c>
    ///   — the Gourlay formula, here with GraceSpacing's own parameters
    ///   (scm/define-grobs.scm:1721-1725: <c>shortest-duration-space</c> 1.6,
    ///   <c>spacing-increment</c> 0.8).</item>
    /// <item>LILYPOND-REF: scm/output-lib.scm:1403-1422 grace-spacing::calc-shortest-duration
    ///   — the ratio is taken against the
    ///   MINIMUM gap of the run's OWN columns, so a run of equal graces always has ratio 1
    ///   whatever the note value, and the ratio-below-1 branch is unreachable here.</item>
    /// <item>LILYPOND-REF: lily/note-spacing.cc:42-115 <c>Note_spacing::get_spacing</c> —
    ///   <c>ideal = base.ideal_distance () - increment + left_head_end</c>, where
    ///   <c>left_head_end</c> is the RIGHT edge of the left column's first note head measured
    ///   in that column, and <c>min_dist</c> is the facing skylines' distance.</item>
    /// <item>LILYPOND-REF: lily/spring.cc:103-129 <c>merge_springs</c>, :122 — the
    ///   <see cref="SpringHeadroom"/> above the minimum, applied even to a single wish
    ///   (lily/spacing-spanner.cc:392-393).</item>
    /// </list>
    /// <para>
    /// MEASURED (audit/lp-geometry/probes/grace-column-width.ly, 14 books, ledger
    /// <c>grace.column.*</c>): the corpus texture reads the FLOOR, not the spring —
    /// 1.6*0.8 - 0.8 + 0.917939 = 1.397939 against a floor of 0.917939 + 0.1 + 0.1 + 0.3 =
    /// 1.417939, and LilyPond draws 1.417939. Only a run with mixed durations gets far
    /// enough above the floor to read the ideal (2.197939 for the eighth of a run whose
    /// minimum is a sixteenth), which is why the mixed books are in the ledger.
    /// </para>
    /// </remarks>
    internal static GraceColumnLayout GraceColumns(
        ImmutableArray<GraceColumnInfo> notes, MusicItem? mainItem,
        GraceSpacingParameters? graceParams = null)
    {
        if (notes.IsDefaultOrEmpty)
            return new GraceColumnLayout(ImmutableArray<double>.Empty, 0);

        var gp = graceParams ?? GraceSpacingParameters.Default;
        double dtMin = CalculateGraceGroupShortestDuration(notes);
        // The beam covers the run's leading COLUMNS and stops at the first rest, so whether a
        // column carries a flag — which is ink in its own RIGHT skyline — is a per-column
        // question. THE gate is GraceNoteEngraver.BeamedPrefix, the same sentence
        // QuantGraceBeam, .Dots and the renderer's DrawGraceStemsAndBeam read, so a column
        // cannot be beamed for one of them and flagged for another (it was spelt three ways
        // until session 299 wrote a fourth, and was run-wide until session 308 measured that
        // LilyPond beams only the prefix).
        int beamedPrefix = GraceNoteEngraver.BeamedPrefix(notes);

        var offsets = ImmutableArray.CreateBuilder<double>(notes.Length);
        var gaps = ImmutableArray.CreateBuilder<(double Rod, double InverseCompress)>(notes.Length);
        double x = 0, toMain = 0;
        for (int i = 0; i < notes.Length; i++)
        {
            offsets.Add(x);
            bool beamed = i < beamedPrefix;
            GraceColumnInfo? next = i + 1 < notes.Length ? notes[i + 1] : null;
            bool nextBeamed = i + 1 < beamedPrefix;
            double minDistance, rod, correction;
            if (notes[i].TabDigitHalfWidth > 0 && (next is { } tn ? !tn.IsRest : mainItem is not null))
            {
                // A TAB-ONLY run (GraceColumnInfo.TabDigitHalfWidth): the columns are fret
                // digits, drawn centred on the column's X, with no stem — LilyPond's TabStaff
                // stems are invisible, so stem_dir_correction returns before any branch
                // (lily/note-spacing.cc:248-249 Stem::is_invisible). The floor is the two digits'
                // boxes on one row (⒝: the digits' strings are not read), the main note's digit
                // centred TabHeadCenterOffset right of its column (one digit wide: ⒝ a two-digit
                // main fret reaches further left).
                // LILYPOND-REF: lily/note-spacing.cc:42-115 Note_spacing::get_spacing — left_head_end is the digit's right edge.
                double mainLeft = next is { } ng
                    ? ng.TabDigitHalfWidth
                    : TabConstants.FretGlyphWidthAtDefault("0") / 2.0 - EngravingDefaults.TabHeadCenterOffset;
                minDistance = notes[i].TabDigitHalfWidth + DefaultExtraSpacingWidth
                              + mainLeft + DefaultExtraSpacingWidth;
                rod = minDistance + SeparationRodPadding;
                correction = 0;
            }
            else if (!notes[i].IsRest && (next is { } n ? !n.IsRest : mainItem is not null))
            {
                // Two sounding columns: LilyPond's own Note_spacing pair — the skylines' distance
                // for the minimum (a grace and its neighbour at different heights need not meet)
                // and the optical stem correction on the ideal. MEASURED (Lab sessions/p728/first):
                // 1.147939 / 1.647939 / 1.726510 where the flat reaches gave 1.417939 to all three.
                // LILYPOND-REF: lily/note-spacing.cc:78-83 Note_spacing::get_spacing — the minimum; :111 the correction.
                (minDistance, rod) = SkylineFloorPair(
                    ItemSkylineFactory.CreateGraceWishSkyline(notes[i], beamed, HorizontalDirection.Right),
                    next is { } nc
                        ? ItemSkylineFactory.CreateGraceWishSkyline(nc, nextBeamed, HorizontalDirection.Left)
                        : ItemSkylineFactory.SharedWishLeftSkylineAtColumn(mainItem!, 0.0, 0.0));
                correction = GraceStemCorrection(notes[i], beamed, next, nextBeamed, mainItem,
                    NoteSpacingParameters.Default, gp.SpacingIncrement);
            }
            else
            {
                // A REST on either side keeps the flat reaches (GraceColumnRightReach's remarks
                // carry its glyph and the spacer's measured width) and no stem correction.
                double rightReach = GraceColumnRightReach(notes[i], beamed);
                double leftReach = next is { } nr
                    ? GraceColumnLeftReach(nr)
                    : MainColumnLeftReach(mainItem);
                minDistance = rightReach + leftReach;
                rod = minDistance + SeparationRodPadding;
                correction = 0;
            }
            double dotRod = GraceDotRod(notes[i], beamed, next, mainItem);
            double gap = Math.Max(
                GraceColumnGap(notes[i], dtMin, gp, minDistance, correction), dotRod);
            rod = Math.Max(rod, dotRod);
            // A slur from this column to the next (or out to the main note) rods the two
            // columns the Slur's minimum-length apart — the rule SlurPairRod states for the
            // main grid. MEASURED (2.26.0, Lab sessions/p727/span/inner.ly): 1.5 with the slur,
            // the 1.417939 floor without it.
            // LILYPOND-REF: lily/spanner.cc:429-473 set_spacing_rods — minimum-length between the bound columns.
            bool nextEndsSlur = i + 1 < notes.Length
                ? notes[i + 1].SlurEnd
                : mainItem is not null
                  && Collector.SlurDetector.TryGetSlurFlags(mainItem, out _, out bool mainEnd) && mainEnd;
            if (notes[i].SlurStart && nextEndsSlur)
            {
                gap = Math.Max(gap, SlurScoringProblem.MinimumLengthSpaces);
                rod = Math.Max(rod, SlurScoringProblem.MinimumLengthSpaces);
            }
            // Under compression the gap closes to its rod at the grace spring's own compress
            // strength — Spring (len, increment) defaults it to len - increment, and neither
            // Note_spacing nor merge_springs (one wish) changes it. MEASURED (2.26.0, Lab
            // sessions/p732/compress, ledger grace.compress.*): 1.417939 -> 1.217939 = the
            // padding-free skyline 1.117939 + 0.1.
            // LILYPOND-REF: lily/spring.cc:204-210 set_default_compress_strength — ideal - min, run by the constructor:
            // LILYPOND-REF: lily/spacing-basic.cc:163-175 Spacing_spanner::note_spacing — ret = Spring (len, min) in the grace branch.
            // LILYPOND-REF: lily/spacing-spanner.cc:228-297 set_column_rods — the rod, padding + skyline distance.
            double inverseCompress = Math.Max(0.0,
                CreateGraceSpring(notes[i].Length, gp, dtMin).IdealDistance - gp.SpacingIncrement);
            gaps.Add((Math.Min(rod, gap), inverseCompress));
            if (i + 1 < notes.Length) x += gap; else toMain = gap;
        }
        return new GraceColumnLayout(offsets.ToImmutable(), toMain, gaps.MoveToImmutable());
    }

    /// <summary>
    /// The inverse stretch strength of ONE grace spring — half the GraceSpacing increment:
    /// "Grace notes should not stretch very much".
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-basic.cc:163-175 Spacing_spanner::note_spacing — set_inverse_stretch_strength (grace_opts.increment_ / 2.0).
    /// MEASURED (2.26.0, Lab sessions/p729/stretch): on a justified line every grace gap of
    /// `\grace { d''16 e''16 } c''4` grows by the same amount, and that amount over a quarter
    /// spring's growth is 0.4 / 1.698 (the quarter's fraction · (len − min)).
    /// </remarks>
    internal static double GraceSpringInverseStretch(GraceSpacingParameters? graceParams = null)
        => (graceParams ?? GraceSpacingParameters.Default).SpacingIncrement / 2.0;

    /// <summary>
    /// A grace run's columns at the system's solved <paramref name="force"/>: each of its
    /// springs — column to column, and the last one to the main note — grows by
    /// force × <see cref="GraceSpringInverseStretch"/>, as every spring of a LilyPond line does.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spring.cc:218-237 Spring::length — distance + force × inverse_stretch_strength.
    /// The run is drawn hanging off its main column, so the main column's position already
    /// carries the stretch of the spring INTO the run (SpringIntoGraceRun adds the run's
    /// springs' inverse strengths in series); this puts the same stretch back between the run's
    /// own columns.
    /// On a COMPRESSED line (force &lt; 0) each gap closes by force × its own compress
    /// strength down to its rod (<see cref="GraceColumnLayout"/>.Gaps) — the lengths the
    /// series spring into the run (<see cref="SpringIntoGraceRun"/>) gave the solver, so the
    /// drawn run and the solved main column agree.
    /// </remarks>
    internal static GraceColumnLayout StretchGraceColumns(GraceColumnLayout columns, double force,
        GraceSpacingParameters? graceParams = null)
    {
        if (force < 0 && !columns.Offsets.IsDefaultOrEmpty && !columns.Gaps.IsDefaultOrEmpty)
        {
            var closed = ImmutableArray.CreateBuilder<double>(columns.Offsets.Length);
            double at = 0, last = 0;
            for (int k = 0; k < columns.Offsets.Length; k++)
            {
                closed.Add(at);
                var (rod, inverseCompress) = columns.Gaps[k];
                last = Math.Max(rod, columns.Gap(k) + force * inverseCompress);
                at += last;
            }
            return new GraceColumnLayout(closed.MoveToImmutable(), last, columns.Gaps);
        }
        if (!(force > 0) || columns.Offsets.IsDefaultOrEmpty)
            return columns;
        double grow = force * GraceSpringInverseStretch(graceParams);
        var offsets = ImmutableArray.CreateBuilder<double>(columns.Offsets.Length);
        for (int k = 0; k < columns.Offsets.Length; k++)
            offsets.Add(columns.Offsets[k] + k * grow);
        return new GraceColumnLayout(offsets.MoveToImmutable(), columns.ToMain + grow);
    }

    /// <summary>
    /// The paper-column ROD a dotted grace column puts on its gap: 0.1 plus the distance from
    /// its dots' right skyline to the next column's left one. Negative infinity when there is
    /// nothing to measure — no dots, or no next column known.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/separation-item.cc:47-68 Separation_item::set_distance — padding plus
    ///   <c>lines[LEFT][RIGHT].distance (right)</c>, the right column's left skyline merged with
    ///   its conditional one. The rod and the spring (<see cref="GraceColumnGap"/>) are separate
    ///   constraints, and the gap is the larger.
    /// MEASURED (2.26.0, scratch/p393/lpdump, grace-dot-flag-column, main note c''1): the rods
    /// through the dots are 2.4559 (d''8., d''16.), 2.3151 (g'16.) and 1.9352 (g'16. to a
    /// beamed g'16), each the drawn gap to four places; g'8.'s rod 1.7945 loses to its spring
    /// 1.9386. The g'16.'s 2.3151 is its dot box's lower corner against the main head's upper
    /// one, which no flat reach can give.
    /// </remarks>
    private static double GraceDotRod(GraceColumnInfo left, bool beamed,
                                      GraceColumnInfo? next, MusicItem? mainItem)
    {
        if (left.Dots == 0 || left.IsRest)
            return double.NegativeInfinity;
        HorizontalSkyline? nextLeft = next is { } n
            ? ItemSkylineFactory.CreateGraceLeftSkyline(n)
            : mainItem is not null
                ? ItemSkylineFactory.SharedLeftSkylineAtColumn(mainItem, 0.0, 0.0)
                : null;
        if (nextLeft is null)
            return double.NegativeInfinity;
        return SeparationRodPadding
               + ItemSkylineFactory.CreateGraceDotRightSkyline(left, beamed).Distance(nextLeft);
    }

    /// <summary>One gap of a grace run — the spring, floored by the skyline distance.</summary>
    private static double GraceColumnGap(GraceColumnInfo left, double dtMin,
                                         GraceSpacingParameters gp, double minDistance,
                                         double stemCorrection)
    {
        var baseSpring = CreateGraceSpring(left.Length, gp, dtMin);
        // LILYPOND-REF: lily/note-spacing.cc:77 — ideal = base.ideal - increment + left_head_end.
        // LILYPOND-REF: lily/note-spacing.cc:111-113 stem_dir_correction — added, then floored at 0.
        // A tab-only grace's head is its fret digit (GraceColumnInfo.TabDigitHalfWidth: drawn
        // centred on the column, so its right edge is that half width).
        double headEnd = left.TabDigitHalfWidth > 0 ? left.TabDigitHalfWidth : GraceHeadEnd(left);
        double ideal = Math.Max(0.0,
            baseSpring.IdealDistance - gp.SpacingIncrement + headEnd + stemCorrection);
        // LILYPOND-REF: lily/note-spacing.cc:78-83 set_min_distance, then lily/spring.cc:122.
        return Math.Max(ideal, minDistance + SpringHeadroom);
    }

    /// <summary>
    /// The grace note head's right edge in its own column — LilyPond's
    /// <c>left_head_end</c> for a grace spring.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-spacing.cc:47-70 — <c>left_head_end =
    /// g-&gt;extent (col, X_AXIS)[RIGHT]</c>, the head's right edge IN THE FONT ITS GROB READS.
    /// MEASURED: LilyPond reports 0.917939 for a grace head and 1.304200 for a full-size
    /// black one. The grace one is NOT the full-size one scaled (that is 0.922205): Emmentaler
    /// is optically sized, so a font-size −3 grob reads the FOURTEEN design's head, 1.298161
    /// in its own staff spaces, and magstep(−3) of that is 0.917939 — LilyPond's own number to
    /// six places. <see cref="GraceColumnInfo.Font"/> is that font (the −7 one inside a cue),
    /// so this reads a width and multiplies nothing.
    /// </remarks>
    private static double GraceHeadEnd(GraceColumnInfo column) => column.Font.NoteheadBlack.Width;

    /// <summary>
    /// How far a grace column's ink reaches RIGHT of its origin, in the separation-skyline
    /// sense: the head (plus its flag when the run is not beamed) widened by
    /// <see cref="DefaultExtraSpacingWidth"/>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/separation-item.cc:120-190 Separation_item::boxes — every grob's box
    /// is widened by its own <c>extra-spacing-width</c>, defaulting to <c>(-0.1 . 0.1)</c>
    /// (:166-167). MEASURED: a beamed grace column's right skyline is 1.017939 and a flagged
    /// one's is 1.538627 (probes/grace-column-width.ly, books GCW2 and GCW1).
    /// </remarks>
    private static double GraceColumnRightReach(GraceColumnInfo note, bool beamed)
    {
        // A REST'S COLUMN IS AS WIDE AS ITS FULL-SIZE GLYPH, and it carries no flag whatever
        // its value: a rest has no stem to hang one off.
        // ⚠️ FULL SIZE, not the grace's — general-grace-settings never names Rest, so the
        // glyph comes out of the staff's own font (see GraceColumnInfo.IsRest). MEASURED:
        // the gap after a rest is 1.7000 where the gap after a grace head is 1.4180
        // (scratch/p308/lp2, r1_split and r3_resthead), and Lily# draws 1.70 against 1.42.
        //
        // ⚠️ THE SPACER'S OWN WIDTH IS AN INVENTION, AND IT IS MEASURABLY TOO WIDE.
        // A spacer has no ink, so this gives it nothing but the default box — and the gap
        // that comes out is the SPRING's ideal rather than this floor, which LilyPond
        // undercuts. MEASURED (scratch/p308/lp2/t1_spacermid against ab/e1_spacermid,
        // `grace { d'16 s16 e'16 }`): LilyPond puts d' and e' 2.5600 apart and Lily# puts
        // them 3.3400 apart — 0.78 too wide. The same pair with a REST between them agrees
        // to the printed digit (3.6387 against 3.64), so what is wrong is the spacer's
        // spring and not the column machinery.
        //   departs from: lily/spacing-basic.cc — what a spring is worth over a column that
        //     declares no grob at all is not the same question as over one that declares a
        //     zero-width one, and this reads the second.
        //   goes away when: someone measures LilyPond's grace spring over a spacer the way
        //     grace.column.* measures it over a head, and puts the point in the ledger.
        //   observed by: NOTHING. No ledger point covers a grace spacer, and the drawn
        //     STRUCTURE is right (the spacer holds a column and ends the beamed prefix, both
        //     measured), so the sweep and every net come through green on the 0.78.
        // ⚠️ Reach measured before this was left standing: 2 books on a disk of 1997, and
        // both are this session's own probes.
        if (note.IsRest)
            return (note.IsSpacer
                       ? 0
                       : GlyphMetrics.GetRestBBox(GlyphMetrics.NoteValueOf(note.BaseDuration)).Right)
                   + DefaultExtraSpacingWidth;
        double ink = GraceHeadEnd(note);
        // A CHORD widens the head half of this: a reversed second is drawn on the far side of
        // the stem, so the column's head ink ends at that head's right edge instead of the
        // support head's. A column with one head, or with no seconds, answers GraceHeadEnd
        // again and the single-note books stay byte-identical.
        // ⚠️ GraceHeadEnd is the head's WIDTH and HeadInkRight is its RIGHT edge; they are the
        // same number only because an Emmentaler notehead's box starts at 0 (its Left is
        // 0.000000 in every design). The max below keeps the two readings from disagreeing.
        if (note.Heads.Length > 1)
            ink = Math.Max(ink, GraceColumnHeads.HeadInkRight(note));
        if (!beamed && note.BaseDuration.Numerator == 1 && note.BaseDuration.Denominator >= 8)
        {
            // The flag hangs off the STEM, so its reach is the stem's x plus the flag's own
            // width — both read from the grace's OWN font (see GraceHeadEnd). The stem's x is
            // the one house, LayoutUtilities.StemAttachX, which is where the drawn flag is
            // put too (SharedRenderer.DrawGraceStemsAndBeam).
            // MEASURED: 0.852939 + 0.585689 = 1.438627 is LilyPond's own reading to nine
            // places (ledger grace.column.single.to-main). It hung off the head's ADVANCE
            // until 2026-08-02, 0.063472 too far right.
            // A lower voice's grace hangs its flag off a DOWN stem (GraceColumnInfo.StemDown):
            // the same two terms in that direction.
            var font = note.Font;
            bool up = note.StemUp;
            var flag = GlyphMetrics.GetFlagBBox(font, note.BaseDuration.Denominator, stemUp: up);
            if (flag != default)
                ink = Math.Max(ink,
                    LayoutUtilities.StemAttachX(
                        up, GlyphMetrics.NoteValueOf(note.BaseDuration),
                        NoteheadStyle.Default, font)
                    + flag.Width);
        }
        // ⚠️ THE DOTS ARE NOT ADDED HERE, and not because they reserve nothing: a Dots grob is
        // an element of the PAPER column, not of the note column, so it reaches a neighbour
        // through the ROD and never through this spring floor — GraceDotRod, a skyline, is
        // where it is priced (session 392). A flat term here was tried in session 299 and
        // LilyPond disagreed: `grace { d'8. }` and `grace { d'8 }` engrave with the SAME staff
        // width (scratch/p299/lp), because the dot's box sits at a row the main head does not
        // occupy. GraceBodyValidatorTests.ADottedGrace_IsDrawn still watches that book.
        return ink + DefaultExtraSpacingWidth;
    }

    /// <summary>
    /// How far a grace column's ink reaches LEFT of its origin: nothing but the head, unless
    /// the grace carries an accidental, which hangs left and declares a wider box.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:40 Accidental's extra-spacing-width <c>(extra-spacing-width . (-0.2 . 0.0))</c>
    /// — see <see cref="AccidentalExtraSpacingWidthLeft"/>. MEASURED (book GCWA): an
    /// accidental on the SECOND grace of a pair pushes that gap from 1.417939 to 2.560895,
    /// which is 1.017939 + (1.042957 + 0.2) + 0.3.
    /// </remarks>
    private static double GraceColumnLeftReach(GraceColumnInfo column)
    {
        // A rest carries no accidental, so its column reaches left by the default box alone.
        if (column.IsRest)
            return DefaultExtraSpacingWidth;
        // The LEFTMOST of the column's packed accidentals — for a single head that is its one
        // accidental, and for a chord it is whichever the stacking pushed out furthest. Both
        // answers come out of GraceColumnHeads, which is also what the renderer draws from.
        double ink = GraceColumnHeads.AccidentalInkLeft(column);
        return ink > 0 ? ink + AccidentalExtraSpacingWidthLeft : DefaultExtraSpacingWidth;
    }

    /// <summary>The same reading for the MAIN note's column, which closes the run.</summary>
    private static double MainColumnLeftReach(MusicItem? mainItem)
    {
        if (mainItem is null)
            return DefaultExtraSpacingWidth;
        double acc = CalculateLeftExtent(mainItem);
        return acc > 0 ? acc + AccidentalExtraSpacingWidthLeft : DefaultExtraSpacingWidth;
    }

    /// <summary>
    /// The distance a grace run needs in front of its main note — the first grace column to
    /// the main column.
    /// </summary>
    /// <remarks>
    /// This is <see cref="GraceColumnLayout.Span"/>, i.e. the sum of the run's own gaps and
    /// nothing else. There is no junction padding:
    /// LILYPOND-REF lily/spacing-basic.cc:163 Spacing_spanner::note_spacing
    /// takes the grace branch for the last-grace-to-main pair too, because
    /// <c>delta_t.grace_part_</c> is non-zero there. Lily# used to add 0.4 here and another
    /// 0.4 when placing the group; MEASURED (ledger grace.column.*.to-main) LilyPond adds
    /// neither.
    /// <para>
    /// A LEADING accidental is the one thing outside the chain: it hangs left of the FIRST
    /// column, so a caller reserving room in front of the main note has to add that reach on
    /// top of the span. (LilyPond does not add it either — it falls out of the approach
    /// spring's own min_dist, which this measure stands in for.)
    /// </para>
    /// </remarks>
    public static double CalculateGraceGroupSpringWidth(
        ImmutableArray<GraceColumnInfo> notes,
        GraceSpacingParameters? graceParams = null)
    {
        if (notes.IsDefaultOrEmpty)
            return 0;
        double span = GraceColumns(notes, mainItem: null, graceParams).Span;
        return span + GraceColumnLeftReach(notes[0]) - DefaultExtraSpacingWidth;
    }

    /// <summary>
    /// Adjusts a spring's MinDistance to accommodate grace notes before the next item.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/grace-spacing-engraver.cc:36-80 Grace_spacing::calc_springs
    /// Uses spring-based grace group width when note info is available,
    /// falls back to fixed-width calculation for backward compatibility.
    /// </remarks>
    public static Spring AdjustSpringForGraceNotes(Spring spring, int graceNoteCount)
    {
        if (graceNoteCount <= 0)
            return spring;

        double graceWidth = GraceNoteEngraver.GetGraceGroupWidth(graceNoteCount);
        double newMin = Math.Max(spring.MinDistance, spring.MinDistance + graceWidth);
        double newIdeal = Math.Max(spring.IdealDistance, newMin);

        return new Spring(newIdeal, newMin, spring.InverseStretchStrength);
    }

    /// <summary>
    /// What LilyPond charges the spring that RUNS INTO a grace: it is scaled by
    /// <c>0.8</c> — LilyPond's own comment on the number is "Ugh. 0.8 is arbitrary."
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-spanner.cc:396-403 in musical_column_spacing — applied when the RIGHT column has a
    ///   grace part and the LEFT column has none, i.e. exactly once per grace run, at its
    ///   approach. The spring itself is an ORDINARY note spring: lily/spacing-basic.cc takes
    ///   the main-part branch because the left column carries no grace.
    /// MEASURED (ledger grace.column.approach): LilyPond spaces that gap at 2.401796 where
    /// the same book's ordinary quarter gap is 3.002245, and 3.002245 × 0.8 = 2.401796 to
    /// fifteen places.
    /// </remarks>
    public const double GraceApproachScale = 0.8;

    /// <summary>
    /// Makes room for the grace notes hanging left of the next column: the approach is
    /// SHRUNK the way LilyPond shrinks it, and the run's own width is what is added.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-spanner.cc:396-403 musical_column_spacing (the 0.8);
    ///   lily/grace-spacing-engraver.cc:36-80 Grace_spacing::calc_springs (the run's own
    ///   springs, whose total is <see cref="CalculateGraceGroupSpringWidth"/>).
    /// <para>
    /// ⚠️ THE TWO ENGINES MAKE ROOM IN OPPOSITE DIRECTIONS, and this used to make room the
    /// other way: it added the run's width to the spring and left the approach alone, so the
    /// gap before a grace came out 0.850449 too wide (ledger grace.column.approach, open
    /// since 2026-08-01). LilyPond does not widen anything — it takes the spring it already
    /// had and shrinks it, then the grace columns live inside the run's own springs.
    /// </para>
    /// <para>
    /// Lily# draws a run as glyphs hanging off the main column rather than as columns of its
    /// own, so both halves land on ONE spring here: scale first (that is the approach), then
    /// add the run (that is what the grace columns would have spanned). The scaling is
    /// <see cref="Spring.Scale"/>, which is LilyPond's <c>Spring::operator*=</c> and so
    /// refuses to push the ideal below the rod.
    /// </para>
    /// </remarks>
    public static Spring AdjustSpringForGraceNotes(Spring spring,
        ImmutableArray<GraceColumnInfo> graceNotes,
        GraceSpacingParameters? graceParams = null,
        MusicItem? mainItem = null,
        (double SkyMin, double Rod)? approachFloor = null)
        => graceNotes.IsDefaultOrEmpty
            ? spring
            : SpringIntoGraceRun(spring,
                GraceColumns(graceNotes, mainItem, graceParams),
                CalculateGraceGroupSpringWidth(graceNotes, graceParams),
                GraceSpringInverseStretch(graceParams), approachFloor);

    /// <summary>
    /// The spring that runs into a grace run, given how wide the run itself is: LilyPond's
    /// 0.8 on the approach, then the run.
    /// </summary>
    /// <remarks>
    /// ⚠️ ONE HOME for the rule, because Lily# builds springs in two places and they must
    /// agree — the column system (<see cref="AdjustSpringForGraceNotes(Spring,
    /// ImmutableArray{Model.GraceColumnInfo}, GraceSpacingParameters, Model.MusicItem, System.Nullable{System.ValueTuple{double, double}})"/>) and the drawn
    /// timing-column system (MeasureLayouter). The 0.8 was added to the first alone at
    /// first and the ledger did not move a hair, because the drawn output comes from the
    /// second (HANDOFF §2 A's "two places computing one quantity", in its spring form).
    /// <para>
    /// The run's own springs are in SERIES with the approach, and the result is a series
    /// spring (<see cref="Spring.Series"/>): under one force their lengths add, so the
    /// stretch strengths add (StretchGraceColumns hands the same stretch back to the run's
    /// columns when the run is placed), and on a compressed line each grace spring stops at
    /// its own rod while the approach still gives — until session 732 the run was one rigid
    /// block there.
    /// LILYPOND-REF: lily/spring.cc:218-237 Spring::length — distance + force × inverse_stretch_strength.
    /// LILYPOND-REF: lily/simple-spacer.cc:232-287 compress_line — each spring blocks at its own force.
    /// </para>
    /// </remarks>
    /// <param name="run">
    /// The run's columns as they are PLACED. Its span — the ANCHOR-TO-ANCHOR width, first
    /// grace to main note — is what the ideal grows by, because it is the distance the drawn
    /// glyphs actually occupy between two column origins; its gaps become the series parts.
    /// </param>
    /// <param name="graceRunClearance">
    /// The same plus whatever ink hangs LEFT of the first grace's anchor. This is what the
    /// MIN grows by. ⚠️ Putting it in the ideal instead pushes the approach out by exactly
    /// that ink (0.2 in the ledger's book): LilyPond keeps the clearance in the approach
    /// spring's own min_dist, so it binds only when the line is squeezed and never widens a
    /// comfortable line.
    /// </param>
    /// <param name="gapStretch">
    /// The inverse stretch strength of ONE of the run's springs —
    /// <see cref="GraceSpringInverseStretch"/> (each column has its spring to the next, the
    /// last one to the main note).
    /// </param>
    /// <param name="approachFloor">
    /// The previous column against the run's FIRST grace column
    /// (<see cref="GraceApproachFloor"/>): the approach's own minimum, which sets its compress
    /// strength, and its rod. Null where no previous note column is known (a run opening a
    /// line), which keeps the approach's minimum the main note's plus the run's left ink.
    /// </param>
    public static Spring SpringIntoGraceRun(
        Spring spring, GraceColumnLayout run, double graceRunClearance, double gapStretch,
        (double SkyMin, double Rod)? approachFloor = null)
    {
        if (graceRunClearance <= 0)
            return spring;

        double graceRunSpan = run.Span;
        var approach = spring.Scale(GraceApproachScale);
        double newMin = approach.MinDistance + graceRunClearance;
        double newIdeal = Math.Max(approach.IdealDistance + graceRunSpan, newMin);
        int columns = run.Offsets.IsDefaultOrEmpty ? 0 : run.Offsets.Length;
        if (columns == 0 || run.Gaps.IsDefaultOrEmpty)
            return new Spring(newIdeal, newMin, approach.InverseStretchStrength + columns * gapStretch);

        // The approach is LilyPond's note spring between the previous column and the FIRST
        // grace column, scaled: operator*= leaves its compress strength ideal - min, the min
        // being the skyline distance to that grace, and the rod over the same pair floors it.
        // MEASURED (2.26.0, ledger grace.compress.*): at the line's force -0.80 the beamed
        // run's approach compresses at 0.898 and the flagged eighth's at 1.079 — the two
        // runs' different first-grace skylines.
        // LILYPOND-REF: lily/spring.cc:85-93 Spring::operator*= — inverse_compress_strength_ = max (0, ideal - min).
        double headIdeal = newIdeal - graceRunSpan;
        var parts = new Spring[columns + 1];
        parts[0] = approachFloor is { } floor
            ? new Spring(headIdeal, floor.Rod,
                approach.InverseStretchStrength, Math.Max(0.0, headIdeal - floor.SkyMin))
            : new Spring(headIdeal, newMin - graceRunSpan,
                approach.InverseStretchStrength, approach.InverseCompressStrength);
        for (int k = 0; k < columns; k++)
            parts[k + 1] = new Spring(run.Gap(k), run.Gaps[k].Rod, gapStretch, run.Gaps[k].InverseCompress);
        return Spring.InSeries(ImmutableArray.Create(parts), newMin);
    }

    /// <summary>
    /// The spring into <paramref name="main"/>'s grace run, floored: <paramref name="prev"/>'s
    /// column against the run's FIRST grace column — the padding-free skyline distance (the
    /// spring's minimum) and the rod (padding + the separation skylines' distance). Null when
    /// <paramref name="main"/> leads with no grace.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-spacing.cc:78-83 Note_spacing::get_spacing — min_dist, the skylines' distance.
    /// LILYPOND-REF: lily/separation-item.cc:47-68 Separation_item::set_distance — the rod.
    /// ⒝ The grace column's side of both is its wish skyline (CreateGraceWishSkyline), as
    /// <see cref="GraceColumns"/> reads it.
    /// </remarks>
    internal static (double SkyMin, double Rod)? GraceApproachFloor(MusicItem prev, MusicItem main,
        int staffLines = EngravingDefaults.DefaultStaffLines)
    {
        var grace = GraceNotesOf(main);
        if (grace.IsDefaultOrEmpty)
            return null;
        var first = ItemSkylineFactory.CreateGraceWishSkyline(
            grace[0], GraceNoteEngraver.BeamedPrefix(grace) > 0, HorizontalDirection.Left);
        double sky = SkylineFloorPair(
            ItemSkylineFactory.SharedWishRightSkylineAtColumn(prev, 0.0, 0.0, staffLines), first).SkyMin;
        double rod = SkylineFloorPair(
            ItemSkylineFactory.SharedRightSkylineAtColumn(prev, 0.0, 0.0, staffLines), first).Rod;
        return (sky, rod);
    }

    /// <summary>
    /// The column a spring coming from the left actually ARRIVES at: the FIRST GRACE when a
    /// run leads the item, the item itself otherwise.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-spanner.cc:396-403 musical_column_spacing — the spring
    ///   whose RIGHT column has the grace part is the one that stops at the grace, so that
    ///   column is what the pair is built from.
    /// LILYPOND-REF: lily/note-spacing.cc:162-197 same_direction_correction — the rule that
    ///   then reads the two stems, and which wants the head ranges more than one staff
    ///   position apart.
    /// LILYPOND-REF: scm/music-functions.scm:652-656 score-grace-settings —
    ///   <c>((Voice Stem direction ,UP))</c>, why the stand-in's stem is forced up — and
    ///   :666-674 make-voice-props-set, why a lower voice's is DOWN (GraceColumnInfo.StemDown).
    /// <para>
    /// LilyPond's spring stops at the grace column — the run is columns of its own, so the
    /// pair whose stems the optical correction compares is (previous note, first grace), not
    /// (previous note, main note). Lily# hangs the run off the main column and therefore has
    /// ONE spring where LilyPond has three, so the correction has to be told which column
    /// the spring's right end really is.
    /// </para>
    /// <para>
    /// MEASURED, and it is the whole of what was left of grace.column.approach: in that book
    /// the ordinary spring arrives at 3.252245 against the control's 3.002245, and c→f is
    /// three staff positions (the correction fires) where c→d is one (LilyPond's
    /// lily/note-spacing.cc:162-197 same_direction_correction wants more than one). The 0.25
    /// it added became 0.2 after the approach scaling.
    /// </para>
    /// <para>
    /// ⚠️ The stand-in's stem is STATED, not derived from its pitch: a grace stem is up
    /// whatever the note (scm/music-functions.scm:652-656 score-grace-settings, the same
    /// rule GraceNoteEngraver draws by), down in a lower voice (GraceColumnInfo.StemDown).
    /// Letting the pitch decide would flip the correction's sign on any grace above the
    /// middle line.
    /// </para>
    /// </remarks>
    private static MusicItem? ApproachColumn(MusicItem? item)
    {
        if (item == null)
            return null;
        var grace = GraceNotesOf(item);
        if (grace.IsDefaultOrEmpty)
            return item;

        // A CHORD stands in as a CHORD, so the correction reads the whole head RANGE rather
        // than one of its heads: LilyPond's same_direction_correction takes a
        // Drul_array<Interval> of head positions (lily/note-spacing.cc:162-197), and
        // CalculateStemCorrection's StemSpacingInfo already answers that interval for a
        // ChordItem. Picking one head here would have been a second, narrower spelling of a
        // rule this repository already owns.
        // The column's own direction — UP, or a lower voice's DOWN (GraceColumnInfo.StemDown).
        return GraceColumnHeads.StandIn(grace[0], sourcePosition: 0, stemUpOverride: grace[0].StemUp);
    }

    /// <summary>
    /// The bar line's optical correction when a grace run opens the bar: the max over the
    /// columns that carry a leading grace, each read through its first grace
    /// (<see cref="ApproachColumn"/>) — the grace column's own stems, which are the only note
    /// columns of that paper column.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/staff-spacing.cc:95-110 next_notes_correction — the max of
    /// optical_correction over the right column's note columns.
    /// </remarks>
    private static double LeadGraceOpticalCorrection(in ItemColumn firstItems)
    {
        double max = 0;
        for (int i = 0; i < firstItems.Count; i++)
        {
            var item = firstItems[i];
            if (GraceNotesOf(item).IsDefaultOrEmpty)
                continue;
            max = Math.Max(max, BarlineToStemOpticalCorrection(ApproachColumn(item)));
        }
        return max;
    }

    /// <summary>The leading grace notes hanging left of an item's column, if any.</summary>
    private static ImmutableArray<GraceColumnInfo> GraceNotesOf(MusicItem item) => item switch
    {
        NoteItem n => n.LeadingGrace,
        ChordItem c => c.LeadingGrace,
        _ => ImmutableArray<GraceColumnInfo>.Empty
    };

    /// <summary>
    /// Whether a grace run hangs in front of <paramref name="item"/> — i.e. whether LilyPond
    /// has a GRACE column between the previous column and this item's own.
    /// </summary>
    /// <remarks>
    /// Lily# hangs the run off the main column, so a caller asking about the column AFTER a bar
    /// line has to be told that LilyPond's spring stops one column earlier. That changes
    /// <c>fills_measure</c>: its <c>next</c> column is then the main note, which is musical, so a
    /// whole note behind a grace earns no full-measure-extra-space.
    /// LILYPOND-REF: lily/spacing-spanner.cc:446-455 Spacing_spanner::fills_measure — false when the next column is musical
    /// </remarks>
    internal static bool HasLeadingGraceColumn(MusicItem? item) =>
        item != null && !GraceNotesOf(item).IsDefaultOrEmpty;

    // ========================================
    // Mid-measure change items (the missing non-musical column)
    // ========================================

}
