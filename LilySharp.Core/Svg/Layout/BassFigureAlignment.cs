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

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Stacks a staff's figure ROWS — LilyPond's <c>BassFigureAlignment</c>, the grob whose only
/// job is to put one <c>BassFigureLine</c> under the next.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:366-385 BassFigureAlignment (bass-figure-alignment-interface
/// at :383) — <c>axes (Y)</c>, <c>stacking-dir DOWN</c>, <c>padding -inf.0</c> and
/// <c>positioning-done ly:align-interface::align-to-minimum-distances</c>.
/// LILYPOND-REF: lily/align-interface.cc:163-285 <c>internal_get_minimum_translations</c> —
/// the loop transcribed below.
/// LILYPOND-REF: scm/define-grobs.scm:444-464 BassFigureLine's <c>staff-staff-spacing</c>
/// (outside-staff-axis-group-interface at :461) — the stacked element, whose spec at
/// :449-450 is <c>((minimum-distance . 1.5) (padding . 0.1))</c>.
/// <para>
/// ⚠️ WHY THE SPEC IS THE ONE THAT IS READ, since it is not obvious that a
/// <c>staff-staff-spacing</c> reaches a pair of figure lines: a BassFigureLine declares no
/// <c>staff-affinity</c>, so <c>Page_layout_problem::is_spaceable</c>
/// (lily/page-layout-problem.cc:1174-1177) says yes for both, and
/// <c>get_spacing_spec</c> (:1277-1281) hands back the UPPER line's
/// <c>staff-staff-spacing</c>. Its <c>padding</c> then OVERWRITES the alignment's own
/// <c>-inf</c> (align-interface.cc:225-226), which is why that <c>-inf</c> never appears in a
/// step — it survives only as the first element's, where <c>max (0.0, dy)</c> eats it.
/// </para>
/// <para>
/// ⚠️ THE MINIMUM IS A BRANCH, NOT A CONSTANT. For plain digits it wins and the step is 1.5 —
/// which is what LilyPond's dump reads in every book of
/// <c>audit/lp-geometry/probes/figured-bass-placement.ly</c>, since a
/// <c>fattened.fixedwidth</c> digit sits on its baseline and caps at 1.122462 here, offering
/// only 1.222462 of ink. The other branch IS reachable: a figured-bass sharp descends 0.141430
/// and caps at 1.263972, so a sharp over a sharp offers 1.505402 and the ink decides. Writing
/// the 1.5 alone would have fitted the probe's texture (HANDOFF §5.2).
/// </para>
/// <para>
/// ⚠️ IT REPLACES <c>FiguredBassEngraver.FigureSpacing = 1.6</c>, a hand-picked per-row height
/// that the RENDERER contradicted with a 1.5 of its own — one quantity, two spellings, and
/// neither LilyPond's (HANDOFF §5.2.1②). Its observer was the ledger's
/// <c>figbass.upper-staff.staff-gap</c>, which carried +0.600000 = the extra 0.1 per step plus
/// a 0.5 tail reserved under a lowest figure that has no descent at all.
/// </para>
/// </remarks>
internal static class BassFigureAlignment
{
    /// <summary>The floor under one row-to-row step.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:449 <c>staff-staff-spacing.minimum-distance</c>
    /// of BassFigureLine.</remarks>
    internal const double LineMinimumDistance = 1.5;

    /// <summary>The gap left between one row's descenders and the next row's ink when the ink
    /// beats <see cref="LineMinimumDistance"/>.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:450 <c>staff-staff-spacing.padding</c> of
    /// BassFigureLine, read at lily/align-interface.cc:225-226 <c>read_spacing_spec</c>.</remarks>
    internal const double LinePadding = 0.1;

    /// <summary>One figure column of the alignment: where it sits and what it stacks.</summary>
    internal readonly record struct Column(double X, ImmutableArray<string> Texts);

    /// <summary>
    /// The baseline of each ROW, as a distance BELOW the top row's baseline (so entry 0 is
    /// always 0). One alignment is one staff of one system; the rows are its lines.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc:201-285 <c>internal_get_minimum_translations</c>'s
    /// element loop, at <c>stacking-dir DOWN</c>, in the equivalent ABSOLUTE frame: LilyPond raises its
    /// accumulated <c>down_skyline</c> by each <c>dy</c> so that the next
    /// <c>down_skyline.distance (…)</c> is measured from the current element (:272), where this
    /// leaves the placed rows where they are and reads the distance as the next baseline's
    /// absolute offset. The two differ by the running <c>where</c> and nothing else, so
    /// <c>dy = max (ink + padding, minimum-distance)</c> becomes
    /// <c>offset = max (required + padding, previous + minimum-distance)</c>.
    /// <para>
    /// ⚠️ THE ROW, NOT THE COLUMN, IS WHAT IS STACKED. The elements are BassFigureLines, which
    /// span the whole alignment, so one step serves every column and it is decided by the
    /// TALLEST ink anywhere in the pair of rows that overlaps horizontally — which is why the
    /// distance is taken between SKYLINES (align-interface.cc:71-88 reads the lines' own
    /// <c>vertical-skylines</c>, not their bounding boxes) rather than per column.
    /// </para>
    /// <para>
    /// ⚠️ TWO BRANCHES OF THAT LOOP ARE ABSENT HERE, and neither is a shortcut — LilyPond does
    /// not reach them for THIS grob, which is a different thing from Lily# skipping them
    /// (HANDOFF §5.2, "LilyPond computes it, so we compute it"):
    /// <list type="number">
    /// <item>THE SPACEABLE BRANCH (:240-268, the constraint from the previous spaceable staff,
    /// and <c>get_fixed_spacing</c>) is gated on <c>include_fixed_spacing</c>, which :185-186
    /// sets to false whenever the alignment's Y-parent is not a System — and it names
    /// BassFigureAlignment as the example. It is dead for every figure row there can be.</item>
    /// <item>THE FIRST ELEMENT'S <c>dy</c> (:217-219) is
    /// <c>max_height + padding</c> with the ALIGNMENT's own padding, i.e. <c>-inf</c> here, so
    /// <c>max (0.0, dy)</c> at :271 makes it 0 — which is what entry 0 is. Its back-patch of
    /// EARLIER (empty) elements would write <c>-stacking_dir * -inf</c> into them; that path
    /// cannot run for a figure row, since row 0 is empty only when the alignment has no
    /// figures at all, and this returns an empty array for that.</item>
    /// </list>
    /// </para>
    /// </remarks>
    internal static ImmutableArray<double> RowOffsets(IReadOnlyList<Column> columns)
    {
        int rows = 0;
        foreach (var c in columns)
            if (!c.Texts.IsDefault) rows = Math.Max(rows, c.Texts.Length);
        if (rows == 0) return ImmutableArray<double>.Empty;

        var offsets = ImmutableArray.CreateBuilder<double>(rows);
        // The profile of everything placed so far, in the frame whose origin is the TOP row's
        // baseline and whose rows sit at −offset (Y-up).
        var placed = new VerticalSkyline(VerticalDirection.Down);
        double where = 0;
        bool anyPlaced = false;

        for (int r = 0; r < rows; r++)
        {
            var up = new VerticalSkyline(VerticalDirection.Up);
            var down = new VerticalSkyline(VerticalDirection.Down);
            // One row is one box per column, all merged before anything reads either
            // skyline — the batch contract exactly. Without it each of a long row's
            // columns re-resolves the whole profile, which cost 10% of a
            // figure-on-every-note score's build when this was first written.
            up.BeginBatch();
            down.BeginBatch();
            foreach (var c in columns)
            {
                if (c.Texts.IsDefault || r >= c.Texts.Length) continue;
                up.Merge(FiguredBassEngraver.ColumnUpSkyline(c.X, c.Texts[r]));
                down.Merge(FiguredBassEngraver.ColumnDownSkyline(c.X, c.Texts[r]));
            }
            up.EndBatch();
            down.EndBatch();
            // An empty element takes the running position and contributes nothing
            // (align-interface.cc:209-213).
            if (up.IsEmpty)
            {
                offsets.Add(where);
                continue;
            }

            if (anyPlaced)
            {
                // ink: how far down this row's baseline must go for its up-skyline to clear
                // what is already placed. −inf when nothing overlaps horizontally, in which
                // case only the minimum speaks.
                double required = placed.Distance(up);
                double byInk = double.IsInfinity(required) || double.IsNaN(required)
                    ? double.NegativeInfinity
                    : required + LinePadding;
                where = Math.Max(byInk, where + LineMinimumDistance);
            }

            placed.Merge(Shifted(down, -where));
            offsets.Add(where);
            anyPlaced = true;
        }

        return offsets.ToImmutable();
    }

    /// <summary>
    /// How deep one column's ink reaches below the top row's baseline — its last row's
    /// offset plus that row's own descent.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:374 <c>axis-group-interface::height</c>, which is
    /// BassFigureAlignment's <c>Y-extent</c> — the group's extent is its children's, so the
    /// bottom edge is the lowest line's stencil bottom and not a tail added to the baseline.
    /// ⚠️ It is asked per COLUMN because that is what the callers reserve and draw: a column
    /// with fewer figures than the tallest one stops at its own last row.
    /// </remarks>
    internal static double ColumnDepth(ImmutableArray<double> rowOffsets, ImmutableArray<string> texts)
    {
        if (texts.IsDefaultOrEmpty || rowOffsets.IsDefaultOrEmpty) return 0;
        int last = Math.Min(texts.Length, rowOffsets.Length) - 1;
        if (last < 0) return 0;
        return rowOffsets[last] - FiguredBassGlyphRun.InkBottom(texts[last]);
    }

    private static VerticalSkyline Shifted(VerticalSkyline sky, double dy)
    {
        var copy = new VerticalSkyline(sky.Direction);
        copy.Merge(sky);
        copy.Raise(dy);
        return copy;
    }
}
