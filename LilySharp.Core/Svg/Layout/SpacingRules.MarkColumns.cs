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
    /// The mark column's own spring to the musical column of its moment — LilyPond's
    /// <c>standard_breakable_column_spacing</c> for <c>dt == 0</c>: the ideal 0.5 over a minimum
    /// of 0, with the default strengths (stretch = ideal, compress = ideal − min).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-spanner.cc:478-536 Spacing_spanner::breakable_column_spacing — the mark column carries no Staff_spacing wish, so springs.empty () takes the standard branch.
    /// LILYPOND-REF: lily/spacing-basic.cc:41-83 Spacing_spanner::standard_breakable_column_spacing — <c>ideal = min_dist + 0.5</c> for dt == 0; min_dist is Paper_column::minimum_distance, 0 against a column with no skyline.
    /// LILYPOND-REF: lily/spring.cc:49-60 Spring::Spring (dist, min_dist) — set_default_strength (:198-216).
    /// </remarks>
    internal const double MarkColumnToNoteIdeal = 0.5;

    /// <summary>The mark column → note spring (<see cref="MarkColumnToNoteIdeal"/>).</summary>
    internal static Spring MarkColumnToNoteSpring()
        => new(MarkColumnToNoteIdeal, 0.0, MarkColumnToNoteIdeal, MarkColumnToNoteIdeal);

    /// <summary>
    /// The pair across a mid-bar rehearsal mark's column (<see cref="Measure.MarkColumnTimings"/>):
    /// the previous musical column → the mark column, then the mark column → the musical column of
    /// the mark's moment, in series.
    /// </summary>
    /// <param name="baseSpring">The pair's DURATION spring as Spacing_spanner::note_spacing builds
    /// it — before the left head width, the skyline minimum, the stem correction and
    /// merge_springs, none of which reach a pair with no wish.</param>
    /// <param name="rod">The two musical columns' separation rod, over both parts (0 for none).</param>
    /// <remarks>
    /// <para>
    /// LEFT — lily/spacing-spanner.cc:322-393 musical_column_spacing: every Note_spacing wish of
    /// the left column names its next NOTE column as its right item, and that column is not the
    /// mark column (a RehearsalMark is a Score grob, so no staff's Separating_line_group_engraver
    /// hands it to the wish), so no wish matches (:350-378) and springs.empty () keeps the
    /// duration spring as it is — its minimum too, the right column being non-musical (:382).
    /// </para>
    /// <para>
    /// RODS — lily/spacing-spanner.cc:228-297 set_column_rods skips a column whose separation item
    /// is empty (:242-244), which the mark column's is, so the rod runs from the previous musical
    /// column to the next over both springs.
    /// </para>
    /// <para>
    /// MEASURED (2.26.0, Lab sessions/p838/mk, the column dump): <c>r2. a4@mark</c> puts the rest
    /// 4.800 before the mark column and the mark column 0.500 before the a4, where the pair with no
    /// mark is the wish's 5.100; <c>c4 d4@mark</c> 2.898 + 0.500 against 3.000.
    /// </para>
    /// </remarks>
    internal static Spring MarkColumnSeries(Spring baseSpring, double rod)
    {
        var right = MarkColumnToNoteSpring();
        var series = Spring.InSeries(ImmutableArray.Create(baseSpring, right),
                                     baseSpring.MinDistance + right.MinDistance);
        return rod > 0 ? series.WithRangeRod(0, 2, rod) : series;
    }

    /// <summary>
    /// Whether the bar line → first column spring earns full-measure-extra-space because the
    /// column AFTER the first one is a mid-bar mark column standing more than half a bar later.
    /// </summary>
    /// <param name="first">The first column's moment (the bar line's column is at 0).</param>
    /// <param name="second">The second column's moment.</param>
    /// <param name="measureLength">The bar's length.</param>
    /// <param name="markColumns">The bar's <see cref="Measure.MarkColumnTimings"/>.</param>
    /// <remarks>
    /// LILYPOND-REF: lily/spacing-spanner.cc:446-472 Spacing_spanner::fills_measure — the column of the
    ///   next RANK after the first musical column is the mark's non-musical column, used (it holds the
    ///   mark), broken (every non-musical column is), and <c>dt &gt; measure-length / 2</c>.
    /// MEASURED (2.26.0, Lab sessions/p838/mk): <c>r2. a4@mark</c> bar line → rest 2.09 where the bar
    /// with no mark reads 1.09; <c>c2 d2@mark</c> (dt exactly half) 1.218, no extra.
    /// ⚠️ <paramref name="measureLength"/> is the bar's own length where LilyPond reads the meter's
    /// <c>measure-length</c>: they differ on a shortened bar only, and a leading pickup opens its line,
    /// where the left column is the broken bar line and the extra space is not given at all (:485).
    /// </remarks>
    internal static bool MarkColumnFillsMeasure(
        Fraction first, Fraction second, Fraction measureLength, ImmutableArray<Fraction> markColumns)
        => first == Fraction.Zero
           && !markColumns.IsDefaultOrEmpty
           && markColumns.Contains(second)
           && second - first > measureLength * new Fraction(1, 2);

    /// <summary>Whether a skip starts strictly between the two moments — its unused column is then
    /// the next rank, which fills_measure reads as unused (the single-measure estimate's spelling of
    /// MeasureLayouter.UnusedOnsetBetween).</summary>
    internal static bool SkipStartsBetween(ImmutableArray<MusicItem> items, Fraction from, Fraction to)
    {
        var t = Fraction.Zero;
        foreach (var item in items)
        {
            if (item is RestItem { IsSpacer: true } && t > from && t < to)
                return true;
            t += item.Duration;
        }
        return false;
    }
}
