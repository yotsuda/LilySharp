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
/// (scratch/p299, books s_spacedot_a / dot; Lily#'s absolute <c>c</c> is LilyPond's <c>c'</c>):
/// <list type="bullet">
/// <item><c>grace { e'8. }</c> — the head sits in a SPACE, its dot stays on the head's own
/// row, and the dot lands 1.226600 right of the head: 0.917939 (the grace head's ink right)
/// + 0.308661 (one grace dot width).</item>
/// <item><c>grace { d'8. }</c> — the head sits ON A LINE, so <see cref="DotConfiguration"/>
/// lifts the dot one position, and THERE the grace's flag is in the way: the dot lands
/// 1.747300 right, which is 1.438627 (the flag's own right edge) + the same 0.308661. The
/// difference, 0.520688, is exactly flag-right minus head-right.</item>
/// </list>
/// ⚠️⚠️ "ON A LINE" IS THE PROXY, NOT THE CAUSE, and reading the pair above as the rule is
/// what let a flat side-model stem stand for six sessions. RE-MEASURED on 2.26.0 with a grob
/// dump over six books (scratch/p315/measurements.md): what decides the push is the DRAWN
/// stem's length, because the flag hangs off its end. <c>\grace { g'8. }</c> is on a line and
/// lifts its dot exactly as <c>\grace { d''8. }</c> does, yet answers 1.226585 — it stands
/// BELOW the middle line, so nothing shortens its 2.80 stem and the flag's lowest ink stays
/// 0.7935 of a position above the lifted dot. The 1.747274 books are the ones whose stem was
/// shortened to 2.50, and <c>\grace { g'16. }</c> (unshortened, but a sixteenth flag reaching
/// 0.354 deeper) joins them. Lily# drew 1.7473 for that first book until session 315, when the
/// grace's dots came home to <c>DrawNote</c> and started reading the stem it draws.
/// ⚠️⚠️ "A FULL-SIZE FLAGGED NOTE IS NOT PUSHED EITHER WAY" STOOD HERE AND IT IS FALSE.
/// It read "MEASURED, books t_flagline_c and t_flagline_d: 1.754200 = 1.304200 + 0.450000
/// whether the dot is lifted or not, because its stem is long enough that the flag's lowest
/// ink is above every dot row" — and the reason given cannot be true of Emmentaler, whose
/// eighth flag curls back down to 3.0502 BELOW the stem end, which is 0.45 above the head of
/// a 3.5 stem. RE-MEASURED on 2.26.0 by dumping the grobs (scratch/p314/flagdot-dump.ly, the
/// dot's LEFT minus its head's LEFT):
/// <list type="bullet">
/// <item><c>g'8.</c> head ON A LINE, dot lifted — 2.517400 = 1.239200 (the up stem's centre)
/// + 0.828200 (the flag's own right) + 0.450000. LilyPond pushes it.</item>
/// <item><c>f'8.</c> head in a SPACE, dot not lifted — 1.754200. The dot's row is the head's
/// own, below the flag's ink, so the floor answers.</item>
/// <item><c>g'16.</c> / <c>f'16.</c> — 2.517400 BOTH, lifted or not: the sixteenth's flag
/// bottom is −3.550200, deep enough to reach even an unlifted dot.</item>
/// </list>
/// Lily# answered 1.754200 for three of those four until session 314. So the gate tells the
/// grace's two cases apart AND the full-size note's — a flat "head right plus a dot" is wrong
/// for both, by half a dot column in the grace and by 0.763200 at full size.
/// ⚠️ The observer is <c>test/dotted-flag-dot-column</c>, added with the fix: the tracked
/// corpus had no unbeamed flagged dotted note at all, which is why 232 snapshots agreed with
/// the wrong answer (two of the owner's real books did not).
/// </para>
/// <para>
/// ⚠️ ONE HOUSE, TWO CALLERS — and every note in the score, grace and cue included, reaches
/// it through one of them. <c>SharedRenderer.DrawNote</c> and <c>DrawChord</c> ask here; the
/// grace overlay pass stopped asking in session 315, because it stopped drawing dots at all.
/// The paragraph said THREE for several sessions while <c>GraceNoteEngraver.Dots</c> was the
/// only caller and the ordinary two answered with a flat "head ink right plus one dot": the
/// claim was of the RULE, not of the callers, and reading it as the latter is what got HANDOFF
/// §2 U8 ⒝2's dot ticket backwards (§1, session 313 — the GRACE house held the ported rule and
/// the ordinary one did not). The two callers differ only in WHICH boxes they hand over, which
/// is what LilyPond's <c>side-support-elements</c> differ in too — the rule itself is written
/// once, so a grace cannot drift into a second spelling of it (docs/RULES.md §5.2.1②).
/// </para>
/// <para>
/// ⚠️ THE STEM IS THE SUPPORT THAT NEVER BINDS, and saying which one does is the point: an UP
/// stem's right edge IS the head's own attachment point, so the max cannot pick it over the
/// floor (it becomes observable only for a head whose attachment sits left of its ink). THE
/// FLAG BINDS OFTEN — see the four measured cases above. Session 314 wired both into
/// <c>DrawNote</c> and <c>DrawChord</c>; of 63 of the owner's real books, two moved, and both
/// moved onto LilyPond's number.
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
    /// <param name="headInkRight">The column's first head's own right edge — the FLOOR every
    /// support is measured against (LilyPond's <c>set_minimum_height</c>), so a column with no
    /// support in the way answers exactly "the head, then one dot width".</param>
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
        // THE MAX IS TAKEN PER SUPPORT, over the rows THAT support covers — which is what a
        // skyline's height at one y is, and NOT what Lily#'s Skyline answers for this input.
        // ⚠️⚠️ THIS WAS `Skyline.FromBoxes(...).QueryXInRange(p, p)` UNTIL SESSION 315, AND THE
        // TWO ARE NOT THE SAME FUNCTION. That `Skyline` — a simplified flat-segment class,
        // deleted in the same session once this was its last caller in the engine; it is in the
        // history of Svg/Layout/Skyline.cs — merged any two OVERLAPPING boxes into ONE segment
        // spanning their union with the outermost X, so a stem's box (which always overlaps its
        // flag's) carried the FLAG's x down over the stem's whole seven positions: the Y gate
        // this class exists for was switched off for every row between the head and the flag's
        // real ink. It survived session 314's four full-size books by
        // luck: a stem-up stem's box BEGINS at the head's own row and the query is strict at
        // both edges, so an unlifted dot fell out of the merged segment anyway, and a lifted
        // one was inside the flag's own box in all four. A GRACE is where the two answers come
        // apart, because a grace stem is short enough for the flag's ink to end above a lifted
        // dot: MEASURED on canonical 2.26.0, `\grace { g'8. }` answers 1.226585 and the merged
        // skyline drew 1.747274 (scratch/p315/measurements.md, six books; the same defect makes
        // any full-size note whose stem is extended to the middle line answer as if its flag
        // were on the dot's row).
        // LILYPOND-REF: lily/dot-configuration.cc:129-137 Dot_configuration::x_offset —
        //   `max over dots of problem_->head_skyline ().height (position)`, and
        //   lily/skyline.cc Skyline::height (Real y) reads the profile AT y: a taller building
        //   never lends its height to a shorter one's rows.
        // ⚠️ STRICT AT BOTH EDGES, and one measured book turns on it: a full-size `f8.` in a
        // space keeps its dot on the head's own row, which is exactly where an up stem's box
        // begins, and LilyPond leaves that dot at the head's ink right (1.754200). Widening
        // either comparison to >= pushes it to 2.517400.
        double off = headInkRight;
        foreach (var s in supports)
            foreach (int p in dotPositions)
                if (s.PositionBottom < p && p < s.PositionTop)
                    off = Math.Max(off, s.XRight);
        return off + dotWidth;
    }
}
