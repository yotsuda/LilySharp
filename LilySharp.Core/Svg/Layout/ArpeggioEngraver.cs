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
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout for a single arpeggio marking. Vertical coordinates are stored in the
/// LilyPond-native <b>Y-up</b> frame (staff-spaces above THIS arpeggio's staff
/// middle line, up-positive — frame B), resolved against the staff middle the renderer
/// resolves. X is in staff spaces as before.
/// </summary>
/// <remarks>
/// ⚠️ THE TWO CONSUMERS READ THIS IN OPPOSITE DIRECTIONS, so anything taking these values
/// must say which frame it is in:
/// <list type="bullet">
/// <item><description><c>SharedRenderer.DrawArpeggios</c> STAYS IN Y-UP — it adds the staff
/// middle's page Y-up and draws, and <c>YFlipDrawingContext</c> flips at the output boundary.
/// A larger number is higher on the page.</description></item>
/// <item><description><c>ItemSkylineFactory.AddArpeggio</c> CONVERTS TO DEVICE
/// (<c>staffY − yUp</c>), because the skyline runs Y-down. A larger number is lower on the
/// page, and <c>ColumnPart</c>'s <c>yBottom</c> is the numerically smaller one.</description></item>
/// </list>
/// Units are staff spaces on both sides; only the direction differs. The frame is named in
/// each consumer rather than assumed — a bare <c>topY</c> in the Y-up drawer read as device
/// and put the bracket's end ticks outside its interval on 2026-08-03.
/// </remarks>
public readonly record struct ArpeggioLayout(
    double X,
    double TopYUp,
    double BottomYUp,
    int Copies,              // whole wiggle glyphs stacked upward from BottomYUp; 0 = a bracket
    int SourcePosition,
    int SourceIndex = -1,    // F3/B: index into score.Arpeggios (data-pos resolved at render)
    int MeasureIndex = -1,   // page membership for multi-page rendering (-1 = draw on every page)
    int StaffIndex = -1,     // owning staff (ossia shrink); -1 = unknown/test construction
    bool Bracket = false);   // non-arpeggiate: straight bracket, not a wave

/// <summary>
/// Calculates arpeggio layouts from detected arpeggio items.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/arpeggio.cc:117-188 get_squiggle, add_at_edge (Arpeggio::print) and
/// LILYPOND-REF: scm/define-grobs.scm:205-229 side-position-interface — Arpeggio
///   (padding . 0.5) (direction . LEFT) (side-axis . X).
/// The arpeggio is a STACK OF GLYPHS placed to the left of a chord: the wiggle is
/// <c>scripts.arpeggio</c>, whole copies of it are laid edge to edge until the pile covers
/// the chord, and both of its dimensions are therefore the font's rather than this
/// engraver's. <c>protrusion</c> belongs to the BRACKET spelling, not to the wiggle.
/// </remarks>
internal static class ArpeggioEngraver
{
    // LILYPOND-REF: scm/define-grobs.scm:210 Arpeggio (padding . 0.5) — this is the
    // gap between the arpeggio's RIGHT edge and the note column's LEFT edge
    // (side-position-interface with side-axis = X, direction = LEFT), NOT an offset
    // from the notehead center.
    internal const double Padding = 0.5;

    /// <summary>
    /// The wiggle's width — the <c>scripts.arpeggio</c> glyph's own designed extent, because
    /// that is what the grob's X-extent IS.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/arpeggio.cc:313-319 get_squiggle (Arpeggio::width) — the callback
    ///   returns <c>get_squiggle (me).extent (X_AXIS)</c>, and
    /// LILYPOND-REF: scm/define-grobs.scm:205-229 side-position-interface — the Arpeggio
    ///   entry declares that callback as the grob's <c>X-extent</c>. So an arpeggio's width
    ///   is a font metric and never a number the engraver chooses.
    /// </remarks>
    internal static double WiggleWidth => GlyphMetrics.Arpeggio.Right - GlyphMetrics.Arpeggio.Left;

    /// <summary>
    /// One wiggle's height — the stacking STEP, since the stencil is whole copies laid edge
    /// to edge. The glyph is designed one staff space tall (mf/feta-scripts.mf:1892-1905,
    /// <c>height# := staff_space#</c>), which is why an arpeggio's drawn length always comes
    /// out a whole number of spaces.
    /// </summary>
    internal static double WiggleHeight => GlyphMetrics.Arpeggio.Top - GlyphMetrics.Arpeggio.Bottom;

    // LILYPOND-REF: lily/arpeggio.cc:161-181 add_at_edge (Arpeggio::print) — the epsilon the
    // stacking loop tests with, which keeps a chord reaching the centre line from picking up
    // one squiggle too many on a rounding error.
    // Copied as the literal it is; LilyPond's own comment says it is far above the ~1e-16 the
    // error runs at and far below anything that would change the count.
    private const double StackEpsilon = 1e-3;

    /// <summary>
    /// Where the wiggle's glyph origin goes for a column whose ink starts at
    /// <paramref name="columnLeftX"/>: its own width and the padding to the left of that.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:208-221 x-aligned-side — Arpeggio (direction . LEFT)
    ///   (side-axis . X) (padding . 0.5) with
    ///   <c>X-offset = ly:side-position-interface::x-aligned-side</c>, the grob's own extent
    ///   is set <c>padding</c> clear of the SUPPORT's extent, and both are ink extents. The
    ///   glyph's box starts at 0, so its origin IS its ink left.
    /// <para>
    /// ⚠️ THE COLUMN'S LEFT IS THE HEAD'S INK LEFT, not its centre. Until 2026-08-03 this
    /// subtracted a further half head width on the stated ground that the column X was the
    /// centre, which stood every wiggle that much too far left — measured as
    /// audit/lp-geometry <c>arpeggio.x.right-edge-to-head.*</c>, whose two books differed by
    /// exactly half the difference of their head widths.
    /// </para>
    /// </remarks>
    internal static double WiggleOriginX(double columnLeftX)
        => columnLeftX - Padding - WiggleWidth;

    /// <summary>
    /// The pile a chord spanning <paramref name="minPosition"/>..<paramref name="maxPosition"/>
    /// (staff positions) gets: where its bottom sits in the staff's Y-up frame, and how many
    /// whole glyphs stand on it.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/arpeggio.cc:117-188 add_at_edge, translate_axis (Arpeggio::print) —
    ///   the head interval comes from <c>positions</c> (:83-115 calc_positions, the stems'
    ///   head positions halved into
    ///   staff spaces), then :145-146 drops the DOWN end half a space "to include note head
    ///   in interval", :150-151 widens both ends by half a space when what is left is under
    ///   1.5, :180-181 stacks squiggles with <c>add_at_edge</c> while the pile is shorter
    ///   than the interval, and :183 translates the pile so it STARTS at the interval's down
    ///   end. The quantisation is the whole point: the pile is whole glyphs, so its length is
    ///   a whole number of spaces and reaches PAST what was asked for.
    /// <para>
    /// ⚠️ <c>protrusion</c> IS NOT PART OF THIS. That property belongs to the chord BRACKET,
    /// where it is the horizontal tick width (:190-201 Chord_bracket::print hands it to
    /// <c>Lookup::bracket</c>); the wiggle's stencil never reads it. Lily# used to extend the
    /// head span by 0.4 at both ends and draw exactly that — no quantisation, and the wrong
    /// end treatment — which is the residual audit/lp-geometry <c>arpeggio.y.length</c> holds.
    /// </para>
    /// </remarks>
    internal static (double BottomYUp, int Copies) Pile(double minPosition, double maxPosition)
    {
        double lo = minPosition * 0.5 - 0.5;
        double hi = maxPosition * 0.5;
        if (hi - lo < 1.5)
        {
            lo -= 0.5;
            hi += 0.5;
        }

        double wanted = hi - lo;
        int copies = 0;
        while (copies * WiggleHeight + StackEpsilon < wanted)
            copies++;
        return (lo, copies);
    }

    /// <summary>
    /// A non-arpeggiated chord's BRACKET instead of the wiggle: the same head interval
    /// widened by 0.75 either side.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/arpeggio.cc:205-214 Chord_bracket::print —
    ///   <c>y_extent.widen (0.75)</c> on <c>positions</c>, then :191-201 draws the bracket
    ///   with <c>protrusion</c> as the tick width. ⚠️ It does NOT take the wiggle's
    ///   half-space drop or its quantisation: a bracket is one drawn shape, not a stack.
    /// <para>
    /// ⚠️ NOTHING OBSERVES THIS. <c>@arpeggio.bracket</c> is dropped by the exporter and no
    /// fixture carries one, so there is no ledger point and no twin — it is ported literally
    /// because it lives in the function that was being ported and because the numbers it
    /// replaced (a 0.4 overhang and a 0.7 tick) were invented.
    /// </para>
    /// </remarks>
    internal static (double BottomYUp, double TopYUp) BracketExtent(
        double minPosition, double maxPosition)
        => (minPosition * 0.5 - BracketWiden, maxPosition * 0.5 + BracketWiden);

    // LILYPOND-REF: lily/arpeggio.cc:211 Chord_bracket::print — y_extent.widen (0.75).
    private const double BracketWiden = 0.75;

    // LILYPOND-REF: scm/define-grobs.scm:811-835 chord-bracket-interface — the ChordBracket
    // entry's (protrusion . 0.4), how far the bracket's end ticks reach PAST the spine,
    // handed to Lookup::bracket as `width` at lily/arpeggio.cc:198-200.
    internal const double BracketProtrusion = 0.4;

    /// <summary>
    /// The bracket's line thickness — its spine's width, and the height of each end tick.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/arpeggio.cc:194-196 Chord_bracket::print — <c>line-thickness</c> from
    ///   the layout times the grob's own <c>thickness</c>, which scm/define-grobs.scm:811-835
    ///   declares as <c>(thickness . 1)</c> for ChordBracket.
    /// ⚠️ NOT <c>EngravingDefaults.StaffLineThickness</c>, which is the same number by way of
    /// the StaffSymbol's own <c>thickness</c> of 1. Two grobs arriving at one value is not one
    /// quantity, and spelling it through the staff would put a false address on it.
    /// </remarks>
    internal const double BracketThickness = 1.0 * EngravingDefaults.LineThickness;

    /// <summary>
    /// The bracket's own X extent about its ORIGIN — negative to the left. This is the one
    /// fact the placement, the reservation and the drawing all read; none of them restates it.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/arpeggio.cc:216-225 <c>Chord_bracket::width</c> — the grob's X-extent
    ///   (scm/define-grobs.scm:824) is its own stencil's, and
    /// LILYPOND-REF: lily/lookup.cc:542-560 <c>Lookup::bracket</c> — that stencil is a spine
    ///   spanning <c>(-thick/2 . thick/2)</c> (:546-547) plus end ticks spanning
    ///   <c>oi = (-thick/2 . thick/2 + protrusion)</c> (:552). So the extent runs from the
    ///   spine's LEFT edge to <c>protrusion</c> past its RIGHT one — WIDER THAN THE PROTRUSION,
    ///   by half a thickness at each end for different reasons.
    /// <para>
    /// ⚠️ IT IS SPELLED ONCE ON PURPOSE. Until 2026-08-03 the placement subtracted the
    /// protrusion alone, standing every bracket half a thickness too far right, and the drawing
    /// dropped the same half thickness off the ink's left edge, so the two cancelled and the
    /// clearance reading stayed EXACT while both were wrong. Three sites each doing their own
    /// arithmetic on <c>thick</c> and <c>protrusion</c> is what let that happen; they now read
    /// this, and a fourth site would too.
    /// </para>
    /// </remarks>
    internal static (double Left, double Right) BracketXExtent
        => (-BracketThickness / 2, BracketThickness / 2 + BracketProtrusion);

    /// <summary>How wide the bracket is — the span of <see cref="BracketXExtent"/>.</summary>
    internal static double BracketWidth => BracketXExtent.Right - BracketXExtent.Left;

    /// <summary>
    /// Calculates layout positions for all arpeggio items.
    /// </summary>
    public static ImmutableArray<ArpeggioLayout> Calculate(
        ImmutableArray<ArpeggioItem> arpeggios,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<Measure> measures = default,
        Dictionary<int, ImmutableArray<Measure>>? measuresByStaff = null)
    {
        if (arpeggios.IsDefaultOrEmpty || arpeggios.Length == 0)
            return ImmutableArray<ArpeggioLayout>.Empty;

        var measureMap = new Dictionary<int, (SystemLayout system, MeasureLayout measure)>();
        foreach (var system in systems)
        {
            foreach (var ml in system.Measures)
            {
                measureMap[ml.MeasureIndex] = (system, ml);
            }
        }

        var layouts = new List<ArpeggioLayout>();

        for (int ai = 0; ai < arpeggios.Length; ai++)
        {
            var arp = arpeggios[ai];
            if (!measureMap.TryGetValue(arp.MeasureIndex, out var info))
                continue;

            var (system, measure) = info;

            // Resolve this arpeggio's OWN staff (multi-staff) measures for the item X.
            // The staff's vertical offset is no longer needed here: the Y is stored
            // relative to the staff middle (frame B) and resolved to the right staff
            // at draw time.
            var arpMeasures = LayoutUtilities.ResolveStaffMeasures(measuresByStaff, arp.StaffIndex, measures);

            // Get X position of the chord item, then place arpeggio to the left
            // LILYPOND-REF: scm/define-grobs.scm:206 (direction . ,LEFT)
            double itemX = measure.X + LayoutUtilities.GetItemXOffset(
                arpMeasures, arp.MeasureIndex, arp.ItemIndex, measure);

            // Most-negative within-chord head displacement. A head reversed to the
            // LEFT of the stem (a second in a stem-down chord) extends the column's
            // left ink past the un-displaced column, so the arpeggio must clear
            // THAT head, not the column. LILYPOND-REF: lily/stem.cc:606-760
            // calc_positioning_done (reversed heads); the arpeggio's side-position
            // (LEFT) clears the real head extents. Mirrors SpacingRules' left-extent.
            double minHeadOffset = 0;
            if (!arpMeasures.IsDefaultOrEmpty
                && arp.MeasureIndex < arpMeasures.Length
                && arp.ItemIndex < arpMeasures[arp.MeasureIndex].Items.Length
                && arpMeasures[arp.MeasureIndex].Items[arp.ItemIndex] is ChordItem arpChord)
            {
                int nv = arpChord.BaseDuration.Denominator <= 1 ? 1
                       : arpChord.BaseDuration.Denominator <= 2 ? 2 : 4;
                foreach (var off in ChordHeadPositioning.CalculateOffsets(
                             arpChord.Notes, arpChord.StemUp, nv))
                    minHeadOffset = Math.Min(minHeadOffset, off);
            }
            double columnLeftX = itemX + minHeadOffset;

            // Y-up staff-space from the staff middle line (frame B): a head at
            // staff-position p sits p/2 spaces above the middle. This reads
            // sign-for-sign with LP's up-positive frame; the renderer reflects it to
            // device at draw time against the staff middle it resolves, so no
            // system.Y / staff offset is baked here.
            var (bottomYUp, copies) = Pile(arp.MinStaffPosition, arp.MaxStaffPosition);
            double topYUp = bottomYUp + copies * WiggleHeight;
            double arpeggioX = WiggleOriginX(columnLeftX);
            if (arp.Bracket)
            {
                // A bracket is one drawn shape rather than a stack, with its own Y extent and
                // its own X extent — see BracketExtent and BracketXExtent. The stored X is the
                // grob's ORIGIN, and side-position clears the grob's EXTENT, so the origin
                // stands back by that extent's RIGHT edge rather than by the protrusion.
                (bottomYUp, topYUp) = BracketExtent(arp.MinStaffPosition, arp.MaxStaffPosition);
                copies = 0;
                arpeggioX = columnLeftX - Padding - BracketXExtent.Right;
            }

            layouts.Add(new ArpeggioLayout(
                X: arpeggioX,
                TopYUp: topYUp,
                BottomYUp: bottomYUp,
                Copies: copies,
                SourcePosition: arp.SourcePosition,
                SourceIndex: ai,
                MeasureIndex: arp.MeasureIndex,
                StaffIndex: arp.StaffIndex,
                Bracket: arp.Bracket));
        }

        return layouts.ToImmutableArray();
    }
}
