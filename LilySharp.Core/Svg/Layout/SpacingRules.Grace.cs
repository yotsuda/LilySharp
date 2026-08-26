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
        ImmutableArray<GraceNoteInfo> notes)
    {
        double shortest = double.MaxValue;

        foreach (var note in notes)
        {
            double dur = note.BaseDuration.ToDouble();
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
    /// </remarks>
    internal readonly record struct GraceColumnLayout(
        ImmutableArray<double> Offsets, double ToMain)
    {
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
        ImmutableArray<GraceNoteInfo> notes, MusicItem? mainItem,
        GraceSpacingParameters? graceParams = null)
    {
        if (notes.IsDefaultOrEmpty)
            return new GraceColumnLayout(ImmutableArray<double>.Empty, 0);

        var gp = graceParams ?? GraceSpacingParameters.Default;
        double dtMin = CalculateGraceGroupShortestDuration(notes);
        // A run draws a beam only when EVERY head carries one; otherwise each head draws a
        // flag, and the flag is ink in its column's RIGHT skyline. Same gate as
        // GraceNoteEngraver.QuantGraceBeam and the renderer's DrawGraceStemsAndBeam.
        bool beamed = notes.Length >= 2
            && notes.All(n => n.BaseDuration.Denominator >= 8);

        var offsets = ImmutableArray.CreateBuilder<double>(notes.Length);
        double x = 0, toMain = 0;
        for (int i = 0; i < notes.Length; i++)
        {
            offsets.Add(x);
            double rightReach = GraceColumnRightReach(notes[i], beamed);
            double leftReach = i + 1 < notes.Length
                ? GraceColumnLeftReach(notes[i + 1])
                : MainColumnLeftReach(mainItem);
            double gap = GraceColumnGap(notes[i], dtMin, gp, rightReach + leftReach);
            if (i + 1 < notes.Length) x += gap; else toMain = gap;
        }
        return new GraceColumnLayout(offsets.ToImmutable(), toMain);
    }

    /// <summary>One gap of a grace run — the spring, floored by the skyline distance.</summary>
    private static double GraceColumnGap(GraceNoteInfo left, double dtMin,
                                         GraceSpacingParameters gp, double minDistance)
    {
        var baseSpring = CreateGraceSpring(left.BaseDuration, gp, dtMin);
        // LILYPOND-REF: lily/note-spacing.cc:77 — ideal = base.ideal - increment + left_head_end.
        double ideal = baseSpring.IdealDistance - gp.SpacingIncrement + GraceHeadEnd;
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
    /// six places. <see cref="GraceNoteItem.Font"/> is that font, so this reads a width and
    /// multiplies nothing.
    /// </remarks>
    private static double GraceHeadEnd => GraceNoteItem.Font.NoteheadBlack.Width;

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
    private static double GraceColumnRightReach(GraceNoteInfo note, bool beamed)
    {
        double ink = GraceHeadEnd;
        if (!beamed && note.BaseDuration.Numerator == 1 && note.BaseDuration.Denominator >= 8)
        {
            // The flag hangs off the STEM, so its reach is the stem's x plus the flag's own
            // width — both read from the grace's OWN font (see GraceHeadEnd). The stem's x is
            // the one house, LayoutUtilities.StemAttachX, which is where the drawn flag is
            // put too (SharedRenderer.DrawGraceStemsAndBeam).
            // MEASURED: 0.852939 + 0.585689 = 1.438627 is LilyPond's own reading to nine
            // places (ledger grace.column.single.to-main). It hung off the head's ADVANCE
            // until 2026-08-02, 0.063472 too far right.
            var font = GraceNoteItem.Font;
            var flag = GlyphMetrics.GetFlagBBox(font, note.BaseDuration.Denominator, stemUp: true);
            if (flag != default)
                ink = Math.Max(ink,
                    LayoutUtilities.StemAttachX(
                        up: true, GlyphMetrics.NoteValueOf(note.BaseDuration),
                        NoteheadStyle.Default, font)
                    + flag.Width);
        }
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
    private static double GraceColumnLeftReach(GraceNoteInfo note)
    {
        if (note.Accidental is not { } acc)
            return DefaultExtraSpacingWidth;
        var placement = new AccidentalPlacement();
        // Two fonts, because the two grobs carry two font-sizes: the accidental is −4 and the
        // head it clears is −3 (scm/music-functions.scm:635-648 general-grace-settings).
        var layout = placement.CalculateSinglePosition(
            note.StaffPosition, acc, isCourtesy: false,
            GraceNoteItem.AccidentalFont, GraceNoteItem.Font);
        return layout is { } al
            ? Math.Abs(al.XOffset) + AccidentalExtraSpacingWidthLeft
            : DefaultExtraSpacingWidth;
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
        ImmutableArray<GraceNoteInfo> notes,
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
        ImmutableArray<GraceNoteInfo> graceNotes,
        GraceSpacingParameters? graceParams = null,
        MusicItem? mainItem = null)
        => graceNotes.IsDefaultOrEmpty
            ? spring
            : SpringIntoGraceRun(spring,
                GraceColumns(graceNotes, mainItem, graceParams).Span,
                CalculateGraceGroupSpringWidth(graceNotes, graceParams));

    /// <summary>
    /// The spring that runs into a grace run, given how wide the run itself is: LilyPond's
    /// 0.8 on the approach, then the run.
    /// </summary>
    /// <remarks>
    /// ⚠️ ONE HOME for the rule, because Lily# builds springs in two places and they must
    /// agree — the column system (<see cref="AdjustSpringForGraceNotes(Spring,
    /// ImmutableArray{Model.GraceNoteInfo}, GraceSpacingParameters, Model.MusicItem)"/>) and the drawn
    /// timing-column system (MeasureLayouter). The 0.8 was added to the first alone at
    /// first and the ledger did not move a hair, because the drawn output comes from the
    /// second (HANDOFF §2 A's "two places computing one quantity", in its spring form).
    /// </remarks>
    /// <param name="graceRunSpan">
    /// The run's own ANCHOR-TO-ANCHOR width — first grace to main note. This is what the
    /// ideal grows by, because it is the distance the drawn glyphs actually occupy between
    /// two column origins.
    /// </param>
    /// <param name="graceRunClearance">
    /// The same plus whatever ink hangs LEFT of the first grace's anchor. This is what the
    /// MIN grows by. ⚠️ Putting it in the ideal instead pushes the approach out by exactly
    /// that ink (0.2 in the ledger's book): LilyPond keeps the clearance in the approach
    /// spring's own min_dist, so it binds only when the line is squeezed and never widens a
    /// comfortable line.
    /// </param>
    public static Spring SpringIntoGraceRun(
        Spring spring, double graceRunSpan, double graceRunClearance)
    {
        if (graceRunClearance <= 0)
            return spring;

        var approach = spring.Scale(GraceApproachScale);
        double newMin = approach.MinDistance + graceRunClearance;
        double newIdeal = Math.Max(approach.IdealDistance + graceRunSpan, newMin);
        return new Spring(newIdeal, newMin, approach.InverseStretchStrength);
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
    ///   <c>((Voice Stem direction ,UP))</c>, why the stand-in's stem is forced up.
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
    /// ⚠️ The stand-in's stem is forced UP, not derived from its pitch: a grace stem is up
    /// whatever the note (scm/music-functions.scm:652-656 score-grace-settings, the same
    /// rule GraceNoteEngraver draws by). Letting the pitch decide would flip the correction's
    /// sign on any grace above the middle line.
    /// </para>
    /// </remarks>
    private static MusicItem? ApproachColumn(MusicItem? item)
    {
        if (item == null)
            return null;
        var grace = GraceNotesOf(item);
        if (grace.IsDefaultOrEmpty)
            return item;

        var first = grace[0];
        return new NoteItem(
            first.StaffPosition, first.BaseDuration, 0,
            first.Accidental, first.NeedsLedger, 0)
        {
            StemUpOverride = true,
        };
    }

    /// <summary>The leading grace notes hanging left of an item's column, if any.</summary>
    private static ImmutableArray<GraceNoteInfo> GraceNotesOf(MusicItem item) => item switch
    {
        NoteItem n => n.LeadingGrace,
        ChordItem c => c.LeadingGrace,
        _ => ImmutableArray<GraceNoteInfo>.Empty
    };

    // ========================================
    // Mid-measure change items (the missing non-musical column)
    // ========================================

}
