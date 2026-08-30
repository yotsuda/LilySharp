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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// WHERE a dot column stands: the X of its first dot, measured from the note column's
/// origin. <see cref="DotConfiguration"/> answers the other half — which staff positions
/// the dots sit at.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/dot-column.cc:229-232 <c>Dot_column::calc_positioning_done</c> —
/// <c>me-&gt;translate_axis (cfg.x_offset () - … + padding, X_AXIS)</c>.
/// LILYPOND-REF: lily/dot-configuration.cc:129-137 <c>Dot_configuration::x_offset</c> —
/// <c>max over dots of problem_-&gt;head_skyline ().height (position)</c>.
/// LILYPOND-REF: lily/dot-formatting-problem.cc:23-28 <c>Dot_formatting_problem</c>'s ctor —
/// the skyline is built RIGHTWARD over the column's support boxes and then floored, by
/// <c>head_skyline_.set_minimum_height (base_x[RIGHT])</c>,
/// where <c>base_x</c> is united from <c>Stem::first_head</c>'s own X extent
/// (lily/dot-column.cc:81-84, in <c>Dot_column::calc_positioning_done</c>).
/// LILYPOND-REF: scm/output-lib.scm:692-704 <c>dot-column-interface::pad-by-one-dot-width</c>
/// — the padding is ONE dot width, the widest of the column's dots.
/// <para>
/// ⚠️ THE SKYLINE IS Y-GATED, AND THAT IS THE WHOLE POINT. A support only pushes the dots
/// right if it stands at a row a DOT is on; nothing else about it matters. MEASURED on
/// LilyPond 2.26.0, and the measurement is a PAIR that turns on one step of pitch
/// (scratch/p299, books s_spacedot_a / dot):
/// <list type="bullet">
/// <item><c>grace { e'8. }</c> — the head sits in a SPACE, its dot stays on the head's own
/// row, and the dot lands 1.226600 right of the head: 0.917939 (the grace head's ink right)
/// + 0.308661 (one grace dot width).</item>
/// <item><c>grace { d'8. }</c> — the head sits ON A LINE, so <see cref="DotConfiguration"/>
/// lifts the dot one position, and THERE the grace's flag is in the way: the dot lands
/// 1.747300 right, which is 1.438627 (the flag's own right edge) + the same 0.308661. The
/// difference, 0.520688, is exactly flag-right minus head-right.</item>
/// </list>
/// A FULL-SIZE flagged note is not pushed either way (MEASURED, books t_flagline_c and
/// t_flagline_d: 1.754200 = 1.304200 + 0.450000 whether the dot is lifted or not), because
/// its stem is long enough that the flag's lowest ink is above every dot row. So the
/// gate is not decoration — it is the only thing that tells the two cases apart, and a
/// flat "head right plus a dot" answers the grace wrongly by half a dot column.
/// </para>
/// <para>
/// ⚠️ ONE HOUSE, THREE CALLERS. <c>SharedRenderer.DrawNote</c>, <c>DrawChord</c> and
/// <c>DrawGraceNotes</c> all ask here. They differ only in WHICH boxes they hand over, which
/// is what LilyPond's <c>side-support-elements</c> differ in too — the rule itself is written
/// once, so a grace cannot drift into a second spelling of it (docs/RULES.md §5.2.1②).
/// </para>
/// </remarks>
internal static class DotColumn
{
    /// <summary>
    /// One support of a dot column: how far RIGHT it reaches, over which rows.
    /// </summary>
    /// <param name="PositionBottom">Lowest staff position it covers.</param>
    /// <param name="PositionTop">Highest staff position it covers.</param>
    /// <param name="XRight">Its right edge, in the note column's frame (staff spaces).</param>
    /// <remarks>
    /// The Y unit is the STAFF POSITION, not the staff space, because that is the unit the
    /// dots are placed in — <c>Dot_column::calc_positioning_done</c> converts every box into
    /// it on the way in (lily/dot-column.cc:136 <c>flag-&gt;extent (commony, Y_AXIS) * (2 / ss)</c>,
    /// and :113-114 the note head's own <c>Interval (-1.1, 1.1)</c> around its position).
    /// </remarks>
    internal readonly record struct Support(
        double PositionBottom, double PositionTop, double XRight);

    /// <summary>
    /// A note head's support box: LilyPond's flat <c>(-1.1, 1.1)</c> around its own row.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/dot-column.cc:113-114, in <c>Dot_column::calc_positioning_done</c> —
    /// <c>else if (has_interface&lt;Note_head&gt; (s)) y = Interval (-1.1, 1.1);</c>, then
    /// <c>y += Staff_symbol_referencer::get_position (s)</c> at :121. It is the head's NOMINAL
    /// height in positions, not its glyph's, so it does not change with the font.
    /// </remarks>
    internal static Support HeadSupport(int staffPosition, double xRight)
        => new(staffPosition - 1.1, staffPosition + 1.1, xRight);

    /// <summary>
    /// A stem's support box: from its head's row to seven positions along it.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/dot-column.cc:103-109, in <c>Dot_column::calc_positioning_done</c> —
    /// <c>Real y1 = Stem::head_positions (s)[-get_grob_direction (s)];
    /// Real y2 = y1 + get_grob_direction (s) * 7;</c>.
    /// </remarks>
    internal static Support StemSupport(int headPosition, bool up, double xRight)
        => up ? new(headPosition, headPosition + 7, xRight)
              : new(headPosition - 7, headPosition, xRight);

    /// <summary>
    /// A flag's support box: its real ink, converted from staff spaces to positions.
    /// </summary>
    /// <param name="spacesBottom">The flag's lowest ink, in staff spaces from the staff centre.</param>
    /// <param name="spacesTop">Its highest, same frame.</param>
    /// <param name="xRight">Its right edge in the note column's frame.</param>
    /// <remarks>
    /// LILYPOND-REF: lily/dot-column.cc:130-141, in <c>Dot_column::calc_positioning_done</c> —
    /// the loop over <c>Stem::flag (stem)</c>. The flag is the one support taken at its
    /// GLYPH's extent rather than a nominal band, which is why a short (grace) stem changes
    /// the answer and a long one does not.
    /// </remarks>
    internal static Support FlagSupport(double spacesBottom, double spacesTop, double xRight)
        => new(spacesBottom * 2, spacesTop * 2, xRight);

    /// <summary>
    /// The offset from the note column's origin to the LEFT edge of the first dot.
    /// </summary>
    /// <param name="headInkRight">The column's first head's own right edge — the floor the
    /// skyline is built on (<c>set_minimum_height</c>), so a column with no support in the
    /// way answers exactly "the head, then one dot width".</param>
    /// <param name="supports">Everything that could stand between; may be empty.</param>
    /// <param name="dotPositions">The rows the dots ended up on — see
    /// <see cref="DotConfiguration.Resolve"/>, which runs FIRST because the row is what
    /// decides whether a support is in the way at all.</param>
    /// <param name="dotWidth">One dot's width, in the font THIS column's dots are drawn from.</param>
    internal static double OffsetX(
        double headInkRight,
        IReadOnlyList<Support> supports,
        IReadOnlyList<int> dotPositions,
        double dotWidth)
    {
        double off = headInkRight;
        if (supports.Count > 0)
        {
            var skyline = Skyline.FromBoxes(
                Support2Boxes(supports), Skyline.Direction.Right);
            foreach (int p in dotPositions)
            {
                double at = skyline.QueryXInRange(p, p);
                if (!double.IsNegativeInfinity(at))
                    off = Math.Max(off, at);
            }
        }
        return off + dotWidth;
    }

    private static IEnumerable<(double YBottom, double YTop, double XLeft, double XRight)>
        Support2Boxes(IReadOnlyList<Support> supports)
    {
        foreach (var s in supports)
            yield return (s.PositionBottom, s.PositionTop, s.XRight, s.XRight);
    }
}
