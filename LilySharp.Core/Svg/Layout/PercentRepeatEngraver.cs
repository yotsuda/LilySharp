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
/// Layout result for a percent repeat symbol.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:2788-2807 - PercentRepeat grob
/// </remarks>
public readonly record struct PercentRepeatLayout(
    int MeasureIndex,
    double X,                // X center of the percent symbol (staff spaces)
    double YUp,              // Y-up (frame B): staff-spaces above the staff middle (0 = middle)
    double Width,            // Measure width for proportional sizing
    int SourcePosition,
    int SourceIndex = -1,    // F3/B: index into score.PercentRepeats (data-pos resolved at render)
    int StaffIndex = -1,      // owning staff, so the draw can resolve its staff middle
    bool IsDouble = false,    // two slashes on the bar line: a TWO-measure body's sign
    // A beat slash — a body shorter than a measure. ⚠️ ITS X IS THE LEFT EDGE of the group,
    // not the centre, because LilyPond's beat_slash callback (unlike double_percent) never
    // calls align_to (X_AXIS, CENTER): the stencil hangs off the rhythmic column it belongs
    // to. Measured on 2.26.0 (scratch/p282/slashprobe-lpsvg.svg): the two-slash group of
    // `\repeat percent 2 { c16 d e f }` has its leftmost slash origin at 27.1376, and the
    // beat's column — the four sixteenths sit 2.5042 apart from 17.1208 — is 27.1376.
    // LILYPOND-REF: lily/percent-repeat-interface.cc:107-121 beat_slash vs :96-101
    //   double_percent — only the latter re-aligns.
    bool IsBeatSlash = false,
    // LilyPond's slash-count, carried through: 0 draws the dotted DoubleRepeatSlash, N ≥ 1
    // draws N plain slashes. Meaningless unless IsBeatSlash.
    int SlashCount = 0
);

/// <summary>
/// Calculates layout positions for percent repeat symbols.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/percent-repeat-interface.cc - x_percent() rendering
/// LILYPOND-REF: scm/define-grobs.scm:2788-2807 - self-alignment-X = CENTER
///
/// The percent symbol is centered horizontally and vertically within the measure.
/// </remarks>
internal static class PercentRepeatEngraver
{
    /// <summary>
    /// The geometry of a percent-family sign's stencil, in the drawing's own staff space —
    /// the ONE spelling the renderer draws by and the spacing reserves by. Every length is in
    /// staff spaces of the page (the sign's own staff space is folded in).
    /// </summary>
    /// <param name="PlainSlash">A beat slash with a slash count of one or more: steeper, more
    /// tightly kerned, and dot-less.</param>
    /// <param name="Slashes">How many parallelogram copies the group draws.</param>
    /// <param name="SlashWidth">One slash's horizontal run, <c>wid = 2.0 / slope · ss</c>.</param>
    /// <param name="SlashHeight">One slash's rise, <c>wid · slope</c>.</param>
    /// <param name="Thick">The perpendicular thickness, <c>thickness · ss</c>.</param>
    /// <param name="XWidth">The horizontal cut of the parallelogram's ends, <c>hypot (t, t/s)</c>.</param>
    /// <param name="SlashInk">One slash's ink width, <c>wid + x_width</c>.</param>
    /// <param name="PairGap">Consecutive slash origins' distance, <c>ink − kern · ss</c>; 0 for one slash.</param>
    /// <param name="GroupWidth">The whole group's ink width — the stencil's X extent.</param>
    /// <param name="DotKern">The dot's overlap into the group, <c>dot-negative-kern · ss</c>.</param>
    /// <param name="DotDy">The dots' vertical offset from the middle, <c>0.5 · ss</c>.</param>
    internal readonly record struct SignGeometry(
        bool PlainSlash, int Slashes, double SlashWidth, double SlashHeight, double Thick,
        double XWidth, double SlashInk, double PairGap, double GroupWidth, double DotKern, double DotDy);

    /// <summary>
    /// The stencil geometry of a sign, from LilyPond's grob properties and its three
    /// stencil builders.
    /// </summary>
    /// <remarks>
    /// FOUR GROBS SHARE THIS DRAWING and they differ in three numbers: how many slashes,
    /// how steep, and how hard the copies overlap. The plain beat slash is the odd one —
    /// steeper (1.7) and more tightly kerned (0.85) than the percent family, and it carries
    /// NO dots.
    /// LILYPOND-REF: scm/define-grobs.scm — the RepeatSlash entry (slope 1.7,
    ///   slash-negative-kern 0.85) against the DoubleRepeatSlash entry (slope 1.0,
    ///   slash-negative-kern 1.6, dot-negative-kern 0.75), which is the picture the
    ///   PercentRepeat / DoublePercentRepeat pair already draws. Range-less like the
    ///   neighbouring citation: the grob names are one word each (HANDOFF §5.2.1⑦).
    /// LILYPOND-REF: lily/percent-repeat-interface.cc:107-121 beat_slash — count 0
    ///   draws x_percent (me, 2), i.e. WITH dots, and any other count brew_slash
    ///   (me, count), i.e. without.
    /// LILYPOND-REF: lily/percent-repeat-interface.cc:40-49 brew_slash —
    ///   "Scale everything by staff-space": wid = 2.0/slope·ss and thick = thickness·ss;
    ///   :69-77 x_percent translates each dot ±0.5·ss. A TabStaff sets
    ///   StaffSymbol.staff-space = 1.5, so its sign is one-and-a-half-sized.
    /// <para>
    /// THE SLASH IS A PARALLELOGRAM, NOT A STROKED LINE, and the difference is the ENDS:
    /// LilyPond cuts them HORIZONTALLY, so the shape's height is exactly <c>wid·slope</c>
    /// and its width <c>wid + x_width</c>. A stroked line of the same perpendicular
    /// thickness cuts them square to the slope instead, which on a 45° slash pushes each
    /// corner out by thick/(2√2) in BOTH axes — the ink comes out 0.509 too tall and 0.51
    /// too narrow on a TabStaff (ss 1.5), and a user reported the tab sign as looking too
    /// thick. It is not: the perpendicular thickness was right all along (0.720 = LP's,
    /// measured), the outline was not.
    /// LILYPOND-REF: lily/lookup.cc:519-539 repeat_slash — the four points are
    ///   (0,0) (x_width,0) (x_width+w,height) (w,height) with
    ///   x_width = hypot (t, t/s) and height = w·s, and the box is (0, w + x_width).
    /// EVERY COPY BEYOND THE FIRST is added at the group's right edge with a NEGATIVE
    /// padding, so consecutive origins end up (slash ink width − kern·ss) apart. ZERO for a
    /// single slash, which then draws exactly one.
    /// LILYPOND-REF: lily/percent-repeat-interface.cc:37-60 brew_slash — the
    ///   `for (int i = count - 1; i--;) add_at_edge (X_AXIS, RIGHT, slash,
    ///   -slash_neg_kern)` loop. It is a LOOP, not a pair: a sixteenth-note beat slash
    ///   asks for two and nothing stops a thirty-second asking for three.
    /// </para>
    /// <para>
    /// The dots never widen the group: each overlaps it by <c>dot-negative-kern · ss</c>
    /// (0.75 at ss 1) from its edge, more than a dot's diameter (0.45), so the stencil's X
    /// extent IS the slash group's. MEASURED, 2.26.0 (scratch/p333/fx dp-settings.ly,
    /// DoublePercentRepeat X-extent): 3.757645 on a staff and 5.636468 on a 1.5-space tab —
    /// this function's GroupWidth to six digits in both.
    /// </para>
    /// </remarks>
    internal static SignGeometry Geometry(bool isBeatSlash, int slashCount, bool isDouble, double staffSpace)
    {
        // LILYPOND-REF: scm/define-grobs.scm — the PercentRepeat and DoublePercentRepeat
        //   entries: dot-negative-kern 0.75, slash-negative-kern 1.6, slope 1.0,
        //   thickness 0.48. Range-less: the grob names are one word each.
        const double thickness = 0.48;
        const double dotKern = 0.75;
        bool plainSlash = isBeatSlash && slashCount >= 1;
        double slope = plainSlash ? 1.7 : 1.0;
        double slashKern = plainSlash ? 0.85 : 1.6;
        int slashes = isBeatSlash
            ? (slashCount >= 1 ? slashCount : 2)
            : (isDouble ? 2 : 1);
        double slashWidth = 2.0 / slope * staffSpace;
        double slashHeight = slashWidth * slope;
        double thick = thickness * staffSpace;
        double xWidth = System.Math.Sqrt(thick * thick + thick / slope * (thick / slope));
        double slashInk = slashWidth + xWidth;
        double pairGap = slashes > 1 ? slashInk - slashKern * staffSpace : 0.0;
        double groupWidth = slashInk + (slashes - 1) * pairGap;
        return new SignGeometry(plainSlash, slashes, slashWidth, slashHeight, thick,
            xWidth, slashInk, pairGap, groupWidth, dotKern * staffSpace, 0.5 * staffSpace);
    }

    /// <summary>
    /// The DOUBLE percent sign's ink width on a staff of <paramref name="staffSpace"/> —
    /// what the sign reserves on the bar-line column it is centred on
    /// (<see cref="SpacingRules.MmrRodMinimumDistance"/>).
    /// </summary>
    internal static double DoublePercentInkWidth(double staffSpace)
        => Geometry(isBeatSlash: false, slashCount: 0, isDouble: true, staffSpace).GroupWidth;

    /// <summary>
    /// Calculates percent repeat layouts from collected items.
    /// </summary>
    /// <param name="measures">The primary voice's measures, when known — the bar line a
    /// DOUBLE sign is centred on is the one OPENING its measure, and where that measure
    /// declares no start bar line of its own the line is the previous measure's END line,
    /// drawn inside the previous measure's width; the sign's centre is then that line's
    /// LEFT edge, one drawn bar-line width before this measure's X. Empty (the single-staff
    /// tests) puts the centre at the measure's X.</param>
    /// <remarks>
    /// LILYPOND-REF: lily/percent-repeat-interface.cc:94-103 double_percent —
    ///   <c>m.align_to (X_AXIS, CENTER)</c> on the grob's own X, which break alignment puts
    ///   at the bar line's (both are staff-bar). MEASURED, 2.26.0 (scratch/p333/fx
    ///   dp-settings.ly): the DoublePercentRepeat's X equals the BarLine's X, and the
    ///   BarLine's extent starts AT its X — the sign is centred on the bar line's left edge.
    /// </remarks>
    public static ImmutableArray<PercentRepeatLayout> Calculate(
        ImmutableArray<PercentRepeatItem> percentRepeats,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        ImmutableArray<Measure> measures = default)
    {
        if (percentRepeats.IsDefaultOrEmpty || systems.IsDefaultOrEmpty || measureLayouts.IsDefaultOrEmpty)
            return ImmutableArray<PercentRepeatLayout>.Empty;

        // The sign belongs to the staff the repeat was WRITTEN on (StaffIndex); a
        // cello percent must not print its ％ over the flute. Its own staff middle
        // is resolved at draw time.
        // LILYPOND-REF: lily/percent-repeat-engraver.cc — the engraver lives
        // in the Voice context of its own staff.
        var results = ImmutableArray.CreateBuilder<PercentRepeatLayout>(percentRepeats.Length);

        for (int i = 0; i < percentRepeats.Length; i++)
        {
            var item = percentRepeats[i];
            if (item.MeasureIndex >= measureLayouts.Length)
                continue;

            var ml = measureLayouts[item.MeasureIndex];

            // Center of the measure — or, for the DOUBLE sign, the BAR LINE this measure
            // opens on, because that bar is the one between the two measures the sign
            // stands for and LilyPond break-aligns the item to it.
            // LILYPOND-REF: scm/define-grobs.scm — the DoublePercentRepeat entry (:1290-1292):
            //   break-align-symbol = staff-bar. Range-less on purpose: one-word grob name.
            // LILYPOND-REF: lily/percent-repeat-interface.cc:96-101 double_percent — the
            //   stencil is align_to'd CENTER on X, so the bar line is its middle.
            // A BEAT slash stands at its own moment inside the measure, so it reads the same
            // X the notehead pass reads: the timing columns when a multi-staff score has
            // filled them, and the per-item slots otherwise. Matching that pass exactly is
            // what keeps the slash on the beat rather than near it — the two grids do not
            // agree, which is why SharedRenderer.Noteheads picks between them the same way.
            double x;
            if (item.IsBeatSlash)
            {
                x = !ml.Columns.IsDefaultOrEmpty && ml.Columns.Length > 0
                    ? ml.X + ml.GetXForTiming(item.BeatTiming!.Value)
                    : item.BeatItemIndex >= 0 && item.BeatItemIndex < ml.Items.Length
                        ? ml.X + ml.Items[item.BeatItemIndex].X
                        : ml.X;
            }
            else if (item.IsDouble)
            {
                // The bar line this measure opens on: its own start line's left edge is
                // this measure's X; a boundary line owned by the previous measure's end
                // sits one drawn width before it (see the `measures` parameter).
                double openingBarLineWidth =
                    !measures.IsDefault && item.MeasureIndex > 0
                    && item.MeasureIndex < measures.Length
                    && measures[item.MeasureIndex].StartBarline == BarlineType.None
                        ? SpacingRules.GetBarlineWidth(measures[item.MeasureIndex - 1].EndBarline)
                        : 0.0;
                x = ml.X - openingBarLineWidth;
            }
            else
            {
                x = ml.X + ml.Width / 2;
            }

            // Y-up (frame B): the percent sign is centred on the OWN staff's middle
            // line = 0 staff-spaces above the middle. The staff (and thus its device
            // middle) is resolved at draw time from StaffIndex.
            results.Add(new PercentRepeatLayout(
                item.MeasureIndex, x, 0.0, ml.Width, item.SourcePosition, i,
                StaffIndex: item.StaffIndex, IsDouble: item.IsDouble,
                IsBeatSlash: item.IsBeatSlash, SlashCount: item.SlashCount));
        }

        return results.ToImmutable();
    }
}
