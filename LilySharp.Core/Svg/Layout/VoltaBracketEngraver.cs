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
/// Layout information for a volta bracket.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/volta-bracket.cc:60-120 print method
/// </remarks>
public readonly record struct VoltaBracketLayout(
    int StartMeasureIndex,      // First measure of this volta
    int EndMeasureIndex,        // Last measure of this volta
    double StartX,              // X position of bracket start
    double EndX,                // X position of bracket end
    double YUp,                 // Y-up (frame B): staff-spaces ABOVE the system top,
                                // up-positive. The renderer reflects it to device
                                // against the segment's system top (sy − YUp).
    string VoltaText,           // Text to display (e.g., "1.")
    bool IsClosed,              // Has right hook
    int SourcePosition,         // For click-to-source mapping
    int SourceIndex = -1        // F3/B: index into score.VoltaBrackets (shared by all broken pieces)
);

/// <summary>
/// Calculates positions for volta brackets.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/volta-bracket.cc:1-170 Volta_bracket_interface
/// LILYPOND-REF: lily/volta-engraver.cc:1-150 Volta_engraver
///
/// LilyPond volta brackets:
/// - Start with a vertical hook (downward)
/// - Have horizontal line at consistent Y above staff
/// - Display number text at start
/// - End with vertical hook if closed, or open if continuing
/// </remarks>
internal static class VoltaBracketEngraver
{
    // LILYPOND-REF: scm/define-grobs.scm:4297 edge-height = (2.0 . 2.0) (VoltaBracket grob)
    private const double EdgeHeight = 2.0;

    /// <summary>The space LilyPond's side-position step leaves between the staff's ink and
    /// the bracket's lowest ink.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:4320-4346 side-position-interface is among
    /// VoltaBracketSpanner's own interfaces there, and its
    /// <c>(padding . 1)</c> — with <c>Y-offset = side-position-interface::y-aligned-side</c>
    /// and no staff-padding of its own.</remarks>
    private const double StaffPadding = 1.0;

    /// <summary>The bracket's drawn line thickness, in staff spaces: LilyPond's own
    /// <c>1.6 × line-thickness</c>.</summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:4293-4318 VoltaBracket, beside volta-number-offset:
    ///   <c>(thickness . 1.6)</c>, in line-thickness units.
    /// <para>
    /// ⚠️ IT WAS A BARE 0.13, the same shadowing <c>EngravingDefaults.TupletBracketThickness</c>
    /// was repaired for, and it was left there on the belief that no entry could see it: "all
    /// three <c>page.volta.*</c> entries read this line's OWN bottom edge on each engine, so
    /// the weight falls out of every one of them". THAT IS TRUE OF TWO OF THEM AND FALSE OF
    /// THE THIRD. Where the bracket stands on ink, the grob that meets the support is the
    /// NUMBER, and the number hangs <c>volta-number-offset</c> below the line's CENTRE while
    /// the reading is taken at the line's BOTTOM EDGE — so exactly half the thickness
    /// difference survives into <c>page.volta.plain.staff-to-line</c>. MEASURED by poisoning
    /// 0.13 → 0.16 with nothing else changed: that entry moved −0.014999943 (its residual
    /// +0.017625057 → +0.002625057) and the other two did not move at all.
    /// </para>
    /// <para>
    /// The 0.002625057 left over is the FACE, and it is a different island: LilyPond's "2."
    /// inks 1.2598 tall against this face's 1.2624 at the same declared size (see
    /// <see cref="NumberFontSize"/>).
    /// </para>
    /// <para>
    /// ONE HOME, and it has to be: the DRAW (<c>SharedRenderer.DrawVoltaBrackets</c>), the
    /// RESERVATION (<c>OutsideStaffStacker.PlaceVoltas</c>) and the placement below all
    /// measure from this line's EDGES, and a second spelling would put the bracket's ink
    /// where nothing reserved room for it.
    /// </para>
    /// </remarks>
    internal static double LineThickness => 1.6 * EngravingDefaults.LineThickness;

    /// <summary>Where the bracket sits when nothing above the staff pushes it: its lowest ink
    /// one <c>padding</c> above the staff's own, expressed as the LINE's centre.</summary>
    /// <remarks>
    /// LILYPOND-REF: lily/side-position-interface.cc:88-135 axis_aligned_side_helper, which
    ///   <c>Side_position_interface::y_aligned_side</c> calls — the padding is added to the SUPPORT'S
    ///   EXTENT edge, which for a staff symbol is the top line's outer edge, not its centre.
    /// <para>
    /// Each of the four terms is somebody's declaration, which is why the number is written
    /// as the sum: half a staff line (the staff's ink reaches that far above the line this
    /// engine draws at 0), LilyPond's padding, LilyPond's edge-height, and half of the line
    /// this engine draws — the anchor stored here is the line's CENTRE while the padding
    /// chain is about its edges.
    /// </para>
    /// <para>
    /// ⚠️ IT WAS A FLAT 3.0, declared LILYSHARP-OWN as "a fixed hand-tuned offset above the
    /// staff that matches typical LP output". That was 0.115 low, and the two halves of the
    /// miss are exactly the two edges this sum now spells: 0.05 for standing on the top
    /// line's CENTRE where LilyPond stands on the staff's INK, and 0.065 for hanging the
    /// edge-height from the line's centre where LilyPond hangs it from the line's BOTTOM.
    /// Ledger <c>page.volta.no-ink.staff-to-line</c> is the observer, and it is the only one
    /// of the three that can see this number at all — the other two stand the bracket on ink,
    /// where the clearance binds and this floor is slack.
    /// </para>
    /// </remarks>
    private static double YOffsetYUp =>
        EngravingDefaults.StaffLineThickness / 2.0   // the staff's ink above its top line
        + StaffPadding                               // VoltaBracketSpanner (padding . 1)
        + EdgeHeight                                 // VoltaBracket edge-height
        + LineThickness / 2.0;                       // the stored anchor is the line's centre

    /// <summary>Where the volta number sits inside the bracket: its left edge this far right
    /// of the bracket's left end, and its ink TOP this far below the line's centre.</summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:4305 volta-number-offset = (1.0 . -0.5), on the
    ///   VoltaBracket grob;
    /// LILYPOND-REF: lily/volta-bracket.cc:100-109 Volta_bracket_interface::print — the
    ///   number is aligned UP, translated by the offset's Y, and added at the bracket's LEFT
    ///   edge with padding <c>-(its width) - offset X</c>, which lands its left edge exactly
    ///   <c>offset X</c> inside the bracket.
    /// <para>
    /// ⚠️ THEY WERE 0.5 AND 0.3, unsourced. The Y is the load-bearing one: an ending's first
    /// note collides with the NUMBER's box rather than with the bracket's line, on both
    /// engines — LilyPond drops the bracket to its floor when the number is suppressed, and
    /// raises it by exactly 2.5 when the number is pushed 2.5 down.
    /// </para>
    /// </remarks>
    internal const double NumberOffsetX = 1.0;

    /// <inheritdoc cref="NumberOffsetX"/>
    internal const double NumberOffsetY = 0.5;

    /// <summary>The volta number's font size, in staff spaces, from LilyPond's own scale.</summary>
    /// <remarks>
    /// LILYPOND-REF: <c>scm/define-grobs.scm</c> VoltaBracket <c>(font-size . -2)</c> —
    /// magnification steps of 2^(1/6) — applied to scm/paper.scm:78's <c>text-font-size</c>
    /// of 11 pt, with one staff space = 5 pt at the default 20 pt staff. It is the same
    /// derivation <see cref="BarNumberEngraver.FontSize"/> and
    /// <c>TupletBracketEngraver.NumberFontSize</c> carry for the same declaration; this grob
    /// was the last member of the Numbers family still drawing at
    /// <c>SharedRenderer.FontSize * 0.6</c> = 2.4, an unsourced 37% larger.
    /// <para>
    /// ⚠️ LILYPOND APPLIES A SECOND -2 AND THIS DELIBERATELY DOES NOT. Its number goes
    /// through the <c>volta-number</c> markup command (scm/define-markup-commands.scm), which adds
    /// <c>fontsize -2</c> — but it does so while switching to <c>font-encoding fetaText</c>,
    /// whose digits are proportionally far taller than a text face's. Lily#'s Numbers family
    /// is set in the TEXT face (<c>TextRole.Volta</c> in <c>TextRoleGroup.Numbers</c>), a
    /// standing divergence of its own, and taking the second magstep without the taller face
    /// would draw the number about a fifth SHORTER than LilyPond's ink instead of matching
    /// it. MEASURED: LilyPond's "2." inks 1.2598 tall (read off the volta-number-offset
    /// poison, which drags the grob's extent with it); at this size Lily#'s face gives
    /// 1.2624. ⇒ the remaining 0.0026 is the FACE, and it belongs with the other face
    /// islands rather than to this number.
    /// </para>
    /// <para>
    /// A PROPERTY, not a <c>static readonly</c>, for the reason
    /// <c>TupletBracketEngraver.NumberFontSize</c> gives: static initialisation order between
    /// partial classes is undefined, and reading a not-yet-initialised default is how a whole
    /// family of widths was once silently zeroed.
    /// </para>
    /// </remarks>
    internal static double NumberFontSize => 11.0 * System.Math.Pow(2.0, -2.0 / 6.0) / 5.0;

    /// <summary>The number's em for THIS score: <see cref="NumberFontSize"/> unless the
    /// score's <c>fonts { }</c> wrote a <c>step</c> or <c>size</c> for <c>volta</c>; the draw
    /// and the outside-staff reservation both read it.</summary>
    internal static double NumberEm(Rendering.ScoreTextMetrics fonts)
        => fonts.Size(Rendering.TextRole.Volta, NumberFontSize);

    /// <summary>The number's weight and slant: bold unless the score wrote a style for
    /// <c>volta</c>.</summary>
    internal static Rendering.FontStyle NumberStyle(Rendering.ScoreTextMetrics fonts)
        => fonts.Style(Rendering.TextRole.Volta, Rendering.FontStyle.Bold);

    /// <summary>
    /// Calculates layout for all volta brackets.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/volta-bracket.cc — brackets split at system breaks
    /// When a volta bracket spans multiple systems, it is split into segments.
    /// The first segment shows the volta text and has no right hook.
    /// Continuation segments have no left hook and no text.
    /// The last segment has a right hook if the bracket is closed.
    /// Where each piece's line starts and ends is <see cref="PieceEnds"/>.
    /// </remarks>
    public static ImmutableArray<VoltaBracketLayout> Calculate(
        ImmutableArray<VoltaBracketItem> voltaBrackets,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        MultiStaffScore score)
    {
        if (voltaBrackets.IsDefaultOrEmpty)
            return ImmutableArray<VoltaBracketLayout>.Empty;

        var measureToSystemIdx = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);
        // Lent (see t_layouts); ToImmutable copies, so the builder is finished with there.
        var layouts = t_layouts ?? ImmutableArray.CreateBuilder<VoltaBracketLayout>();
        t_layouts = null;

        for (int bi = 0; bi < voltaBrackets.Length; bi++)
        {
            var bracket = voltaBrackets[bi];
            if (bracket.StartMeasureIndex >= measureLayouts.Length ||
                bracket.EndMeasureIndex >= measureLayouts.Length)
                continue;

            foreach (var (segment, _) in SpannerBreakSubstitution.BrokenPieces(
                bracket.StartMeasureIndex, bracket.EndMeasureIndex, systems, measureToSystemIdx))
            {
                // `voltaBracket line`: the bracket stops at the end of the system it starts
                // in. The first piece of a broken bracket is never its last, so it already
                // ends straight — the cut says "not the ending's end" (owner's design
                // 2026-09-28; LilyPond's own first piece of a broken bracket is drawn the
                // same way: lily/volta-bracket.cc:115-142 Volta_bracket_interface::modify_edge_height).
                if (bracket.FirstSystemOnly && !segment.IsFirst)
                    break;
                if (segment.StartMeasureIndex >= measureLayouts.Length ||
                    segment.EndMeasureIndex >= measureLayouts.Length)
                    continue;

                // First segment shows volta text; continuation pieces are empty.
                string segText = segment.IsFirst ? bracket.VoltaText : "";
                // Only the last segment carries the right hook (if the bracket is closed).
                bool segClosed = segment.IsLast && bracket.IsClosed;

                // The floor hangs off the STAFF the bracket supports itself on, not off the
                // system's top edge: when a chords row leads the system the top staff sits
                // below that edge by the row's band, and a floor measured from the edge stood
                // the bracket that band too high — above every symbol, so the pass never
                // met one, and a second ending's label found a pocket under the hooks
                // (owner report, session 328, scratch/p328/volta). LilyPond side-positions
                // the spanner against the staves it spans (lily/volta-engraver.cc:407,:497
                // Side_position_interface::add_support) and its floor is the staff's ink +
                // padding; the row's symbols reach it through the outside-staff pass instead
                // (OutsideStaffStacker.SeedAboveTrackers), the way LilyPond's System-level
                // pass sees the ChordNames line. Ledger: page.volta.chord-row.symbol-to-line.
                double staffBelowTop = measureToSystemIdx.TryGetValue(segment.StartMeasureIndex, out int segSys)
                    && segSys >= 0 && segSys < systems.Length
                    ? LayoutUtilities.StaffOffsetInSystemUp(
                        systems[segSys], LayoutUtilities.TopScoreGrobStaff(systems[segSys]))
                    : 0.0;

                var (startX, endX) = PieceEnds(score, bracket, segment, systems, measureLayouts,
                    measureToSystemIdx);

                layouts.Add(new VoltaBracketLayout(
                    segment.StartMeasureIndex,
                    segment.EndMeasureIndex,
                    startX,
                    endX,
                    // Y-up from the system top (the renderer resolves the segment's system top).
                    YOffsetYUp + staffBelowTop,
                    segText,
                    segClosed,
                    bracket.SourcePosition,
                    bi
                ));
            }
        }

        var engraved = layouts.ToImmutable();
        layouts.Clear();
        t_layouts = layouts;
        return engraved;
    }

    /// <summary>
    /// Where one piece's line starts and ends, as its line's CENTRE (what the renderer draws
    /// between): LilyPond's bounds and <c>spanner_length</c>, the <c>left</c> a piece after a
    /// line break starts past, and the <c>shorten-pair</c> its bar lines give it.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/volta-bracket.cc:48-113 Volta_bracket_interface::print — a piece
    ///   whose left bound opens a line starts <c>left</c> past it, the right edge of the
    ///   column's break alignment (:60-69); the line runs from 0 to
    ///   <c>spanner_length () - left</c>, shortened by <c>shorten-pair</c> at each end
    ///   (lily/bracket.cc:52-55 Bracket::make_bracket), and is translated by <c>left</c> (:111).
    /// LILYPOND-REF: lily/spanner.cc:310-330 Spanner::spanner_length — the right bound's X
    ///   minus the left bound's.
    /// So in the system's frame the line runs from <c>leftBound + left + shorten[LEFT]</c> to
    /// <c>rightBound - shorten[RIGHT]</c>. The bounds are <see cref="Bounds"/>, the shorten
    /// pair <see cref="ShortenPair"/>.
    /// <para>
    /// Until session 692 the ends were a bare 0.3 inside the bar lines' measure edges at both
    /// ends of every piece, unsourced: a first ending's hook stood 0.38 right of LilyPond's, its
    /// closing hook 0.71 right, and a piece the break cuts stopped short of the line's end —
    /// 3.17 short where a courtesy meter follows (ABC.lys bar 36).
    /// </para>
    /// </remarks>
    private static (double StartX, double EndX) PieceEnds(MultiStaffScore score,
        VoltaBracketItem bracket, SpannerBreakSegment segment, ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts, IReadOnlyDictionary<int, int> measureToSystem)
    {
        var voice = score.PrimaryContentStaff.PrimaryVoice;
        var bars = new BarWalk(score, voice, systems, measureLayouts, measureToSystem);
        var system = systems[segment.SystemIndex];
        var (left, right) = Bounds(bars, bracket, segment, system, score);
        var (shortenLeft, shortenRight) = ShortenPair(bars, bracket, segment.SystemIndex, left);
        return (left.X + shortenLeft, right.X - shortenRight);
    }

    /// <summary>A bound as <see cref="PieceEnds"/> reads it: the X the line is measured from —
    /// for a bound that opens a line already moved past the column's break alignment (the
    /// print's <c>left</c>) — and whether the bound's own X extent is empty.</summary>
    private readonly record struct Bound(double X, bool ExtentEmpty);

    /// <summary>
    /// A piece's two bounds.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/volta-engraver.cc:394-404 Volta_engraver::acknowledge_bar_line →
    ///   lily/volta-bracket.cc:144-151 Volta_bracket_interface::add_bar →
    ///   lily/spanner.cc:414-424 add_bound_item — the first bar line of the ending's first
    ///   timestep is the LEFT bound, and the bar line of the timestep that ends it the RIGHT;
    ///   lily/volta-engraver.cc:482-485, :521-522 — a timestep with no bar line bounds it on its
    ///   command column instead.
    /// LILYPOND-REF: lily/spanner.cc:92-101 Spanner::do_break_processing — a piece a break cuts
    ///   is bounded by the system-edge columns, and a bound on a break column by its broken
    ///   piece on this system's side (find_prebroken_piece (-d)).
    /// A LEFT bound on a line's opening column (break status RIGHT) is read at the right edge
    /// of that column's break alignment: lily/volta-bracket.cc:60-69 with
    /// lily/paper-column.cc:167-218 Paper_column::break_align_width.
    /// The end-of-line column's X is the system's staff span end
    /// (<c>SharedRenderer.StaffRightEdges</c>: the widest staff's end-of-line suffix).
    /// </remarks>
    private static (Bound Left, Bound Right) Bounds(BarWalk bars, VoltaBracketItem bracket,
        SpannerBreakSegment segment, SystemLayout system, MultiStaffScore score)
    {
        int first = bracket.StartMeasureIndex, last = bracket.EndMeasureIndex;
        Bound left;
        if (segment.IsFirst && !bars.OpensSystem(first))
        {
            // Mid-line: the bar line there, or the command column when there is none.
            // ⚠️ LILYSHARP-OWN: that column's X extent is taken as EMPTY, where
            // scm/bar-line.scm:1212 reads its real extent (the break-aligned items on it) — the
            // model carries no column. It matters only to an ending opening mid-line with no
            // bar line, which a form never writes. No observer; it goes when the column is modelled.
            left = bars.PieceAt(first, segment.SystemIndex) is { } bar
                ? new Bound(bar.RefX, bar.Glyph.Length == 0)
                : new Bound(bars.Measure(first).X, ExtentEmpty: true);
        }
        else
        {
            // The line's opening column, or the begin-of-line piece of the bar line on it:
            // either way its break status is RIGHT, so the line is measured from the column's
            // break alignment. A dead piece (a bar line with no begin-of-line glyph) has no
            // extent; the column has one whenever a staff engraves prefatory matter.
            bool extentEmpty = segment.IsFirst && bars.HasBar(first)
                ? bars.PieceAt(first, segment.SystemIndex) is null
                : !HasNotationStaff(score);
            left = new Bound(BreakAlignRight(score, system), extentEmpty);
        }

        Bound right;
        if (segment.IsLast)
        {
            right = bars.PieceAt(last + 1, segment.SystemIndex) is { } bar
                ? new Bound(bar.RefX, bar.Glyph.Length == 0)
                : new Bound(bars.Measure(last).X + bars.Measure(last).Width, ExtentEmpty: true);
        }
        else
        {
            var (_, notationRight, tabRight) = Rendering.SharedRenderer.StaffRightEdges(score, system);
            right = new Bound(Math.Max(notationRight, tabRight), ExtentEmpty: false);
        }
        return (left, right);
    }

    /// <summary>
    /// The right edge of a line's opening break alignment: the union of every staff's
    /// line-start prefatory ink (clef, key, meter, an opening <c>.|:</c>).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/paper-column.cc:167-218 Paper_column::break_align_width —
    ///   <c>break-alignment</c> is the column's whole BreakAlignment, one grob across the
    ///   staves; with an empty extent it answers the column's own X.
    /// </remarks>
    private static double BreakAlignRight(MultiStaffScore score, SystemLayout system)
    {
        double x0 = system.Measures[0].X;
        double right = double.NegativeInfinity;
        foreach (var (_, staff, index) in score.EnumerateStaves())
            if (!staff.IsTextRow)
                right = Math.Max(right, x0 + system.LineStartRightOf(index));
        return double.IsNegativeInfinity(right) ? x0 : right;
    }

    private static bool HasNotationStaff(MultiStaffScore score)
    {
        foreach (var (_, staff, _) in score.EnumerateStaves())
            if (!staff.IsTextRow)
                return true;
        return false;
    }

    /// <summary>
    /// The piece's <c>shorten-pair</c>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/bar-line.scm:1135-1245 ly:volta-bracket::calc-shorten-pair, line for
    ///   line. <c>bars-left</c> holds every bar line the ending's timesteps met but the last,
    ///   <c>bars-right</c> that last one (lily/volta-engraver.cc:349-354, :394-404); break
    ///   substitution leaves each array only this system's pieces, and <c>grob::is-live?</c>
    ///   drops the ones with no glyph there. Its left bar line is the FIRST live one — on a
    ///   piece after a line break usually a bar line inside the ending, since the break's own
    ///   begin-of-line "|" is dead — and its right bar line the last of <c>bars-right</c>.
    /// <para>
    /// NOT LITERAL, DERIVED: LilyPond's arrays hold every staff's bar lines in the order they
    /// were acknowledged, the top staff's first, and the right one is matched to the left one by
    /// <c>vertical-axis-group-index</c>. Here the primary content staff's bars stand for them
    /// (<see cref="BarWalk"/>): the same answer while the staves draw the same bar lines at an
    /// ending's edges, as a form's repeats make them. A score whose staves draw different bars
    /// there would need the bars of every staff, in engraver order, from the model.
    /// </para>
    /// </remarks>
    private static (double Left, double Right) ShortenPair(BarWalk bars, VoltaBracketItem bracket,
        int systemIndex, Bound leftBound)
    {
        double voltaHalfLineThickness = LineThickness / 2.0;

        BarPiece? leftBar = null;
        for (int k = bracket.StartMeasureIndex; k <= bracket.EndMeasureIndex && leftBar is null; k++)
            leftBar = bars.PieceAt(k, systemIndex);
        BarPiece? rightBar = bars.PieceAt(bracket.EndMeasureIndex + 1, systemIndex);

        string leftGlyph = leftBar?.Glyph ?? "";
        string rightGlyph = rightBar?.Glyph ?? "";
        bool noLeftBarOrBroken = leftBar is not { BreakDir: 0 };
        bool noRightBarOrBroken = rightBar is not { BreakDir: 0 };
        var leftSpan = BarGlyphs.SpanExtent(leftGlyph);
        var rightSpan = BarGlyphs.SpanExtent(rightGlyph);

        double leftShorten = noLeftBarOrBroken
            ? Math.Max(0, leftSpan.End)
              - Math.Max(0, BarGlyphs.CompoundExtent(leftGlyph).End)
              - voltaHalfLineThickness
              - (leftBound.ExtentEmpty ? -0.5 : leftBar is not null ? 0 : -1)
            : Math.Max(0, leftSpan.End) - voltaHalfLineThickness;

        double rightShorten = noRightBarOrBroken
            ? -Math.Max(0, rightSpan.End) + voltaHalfLineThickness
            : Math.Min(0, rightSpan.Start) - voltaHalfLineThickness;

        return (leftShorten, rightShorten);
    }

    /// <summary>One broken piece of a bar line, as the shorten pair reads it: its glyph there
    /// (<c>glyph-name</c>), its break direction (−1 end of line, 0 mid-line, +1 start of line)
    /// and its reference point's X, where its main stencil starts.</summary>
    private readonly record struct BarPiece(string Glyph, int BreakDir, double RefX);

    /// <summary>
    /// The bar lines at a voice's measure boundaries as LilyPond's items: boundary k stands
    /// between measure k−1 and measure k; at a line break it is two pieces, the end-of-line one
    /// on the earlier system and the begin-of-line one on the later.
    /// </summary>
    /// <remarks>
    /// The glyph at each place is the one the pen draws there (the renderer's
    /// <c>EndBarWithBreakPieces</c> / <c>StartBarWithBreakPieces</c> and
    /// <see cref="MultiStaffLayouter.DrawnLineStartBarline"/>); its reference point is found
    /// from the drawn ink, since the main stencil starts at X = 0 (scm/bar-line.scm:756-802).
    /// <para>
    /// ⚠️ LILYSHARP-OWN: the plain bar lines inside a compressed multi-measure rest, which the
    /// pen does not draw and LilyPond never creates, are still read here — departs from
    /// lily/volta-engraver.cc:394-404, whose <c>bars-left</c> never holds them. They matter
    /// only as the first bar line of a piece after a break, where they would give 0.11 for
    /// LilyPond's 0.92. It goes when this walk asks the pen's own suppression
    /// (<c>SharedRenderer.IsMmrInnerEndBarline</c>). No observer.
    /// </para>
    /// </remarks>
    private readonly struct BarWalk(MultiStaffScore score, Voice voice,
        ImmutableArray<SystemLayout> systems, ImmutableArray<MeasureLayout> measureLayouts,
        IReadOnlyDictionary<int, int> measureToSystem)
    {
        public MeasureLayout Measure(int index) => measureLayouts[index];

        private int SystemOf(int measure)
            => measure >= 0 && measure < measureLayouts.Length
               && measureToSystem.TryGetValue(measure, out int s) ? s : -1;

        /// <summary>Whether boundary <paramref name="k"/> opens a system (a break, or the
        /// music's start).</summary>
        public bool OpensSystem(int k) => k == 0 || SystemOf(k - 1) != SystemOf(k);

        /// <summary>Whether boundary <paramref name="k"/> has a bar line at all (on either
        /// side of a break).</summary>
        public bool HasBar(int k)
        {
            if (OpensSystem(k))
                return (k > 0 && PieceAt(k, SystemOf(k - 1)) is not null)
                       || PieceAt(k, SystemOf(k)) is not null;
            return PieceAt(k, SystemOf(k)) is not null;
        }

        /// <summary>The live piece of boundary <paramref name="k"/>'s bar line on system
        /// <paramref name="systemIndex"/>, or null.</summary>
        public BarPiece? PieceAt(int k, int systemIndex)
        {
            int n = Math.Min(voice.Measures.Length, measureLayouts.Length);
            if (k < 0 || k > n || systemIndex < 0)
                return null;
            int before = k > 0 ? SystemOf(k - 1) : -1;
            int after = k < n ? SystemOf(k) : -1;

            if (before >= 0 && before == after)
            {
                if (systemIndex != before)
                    return null;
                var prev = voice.Measures[k - 1];
                var next = voice.Measures[k];
                // The one bar line at a mid-line boundary: a repeat-start the plain bar before
                // it yields to (the renderer's EndBarYieldsToRepeatStart), or a start bar line
                // with no end bar before it, is drawn from the next measure's X; any other from
                // the previous measure's end.
                if (next.StartBarline != BarlineType.None
                    && (prev.EndBarline == BarlineType.None
                        || (prev.EndBarline == BarlineType.Single && next.StartBarline == BarlineType.RepeatStart)))
                    return FromInkLeft(next.StartBarline, 0, measureLayouts[k].X);
                return prev.EndBarline == BarlineType.None
                    ? null
                    : FromInkRight(prev.EndBarline, 0, measureLayouts[k - 1].X + measureLayouts[k - 1].Width);
            }

            if (systemIndex == before)
                return FromInkRight(EngravingDefaults.LineEndBarline(voice.Measures[k - 1].EndBarline), -1,
                    measureLayouts[k - 1].X + measureLayouts[k - 1].Width);
            if (systemIndex == after)
                return FromInkLeft(MultiStaffLayouter.DrawnLineStartBarline(voice, k), 1,
                    measureLayouts[k].X + MultiStaffLayouter.LineStartBarGap(score, systems[after]));
            return null;
        }

        private static BarPiece? FromInkLeft(BarlineType type, int breakDir, double inkLeft)
            => BarGlyphs.Glyph(type) is { } g
                ? new BarPiece(g, breakDir, inkLeft - BarGlyphs.CompoundExtent(g).Start)
                : null;

        private static BarPiece? FromInkRight(BarlineType type, int breakDir, double inkRight)
            => BarGlyphs.Glyph(type) is { } g
                ? new BarPiece(g, breakDir, inkRight - BarGlyphs.CompoundExtent(g).End)
                : null;
    }

    /// <summary>
    /// The X extents LilyPond's bar-line stencils have about their reference point.
    /// </summary>
    private static class BarGlyphs
    {
        /// <summary>An X interval; <see cref="Empty"/> reads as LilyPond's empty interval
        /// (+∞ . −∞), which the shorten pair's <c>max 0</c> / <c>min 0</c> turn into 0.</summary>
        public readonly record struct Extent(double Start, double End)
        {
            public static Extent Empty => new(double.PositiveInfinity, double.NegativeInfinity);
        }

        /// <summary>The LilyPond glyph of a Lily# bar line type.</summary>
        /// <remarks>LILYPOND-REF: scm/bar-line.scm:1279-1313 define-bar-line — "|", "||",
        /// "|.", ".|:", ":|.", ":|.|:" (Lily#'s combined repeat draws both thin bars) and
        /// "!".</remarks>
        public static string? Glyph(BarlineType type) => type switch
        {
            BarlineType.Single => "|",
            BarlineType.Double => "||",
            BarlineType.Final => "|.",
            BarlineType.RepeatStart => ".|:",
            BarlineType.RepeatEnd => ":|.",
            BarlineType.RepeatBoth => ":|.|:",
            BarlineType.Dashed => "!",
            _ => null,
        };

        /// <summary>The span glyph of a bar glyph, unpadded (#f → null).</summary>
        /// <remarks>LILYPOND-REF: scm/bar-line.scm:1279-1313 define-bar-line, the last
        /// argument (#t = the glyph itself).</remarks>
        private static string? SpanGlyph(string glyph) => glyph switch
        {
            "|" or "||" or "|." or "!" => glyph,
            ".|:" => ".|",
            ":|." => " |.",
            ":|.|:" => " |.|",
            _ => null,
        };

        /// <summary>One glyph character's stencil width.</summary>
        /// <remarks>LILYPOND-REF: scm/bar-line.scm make-simple-bar-line (hair-thickness),
        /// make-thick-bar-line (thick-thickness), make-colon-bar-line (the dot glyph),
        /// make-dashed-bar-line (hair-thickness); the widths are the ones Lily# draws
        /// (<see cref="EngravingDefaults.BarlineDrawnWidth"/> sums the same pieces).</remarks>
        private static double Width(char c) => c switch
        {
            '|' or '!' => EngravingDefaults.ThinBarlineThickness,
            '.' => EngravingDefaults.ThickBarlineThickness,
            ':' => 2 * EngravingDefaults.RepeatDotRadius,
            _ => 0.0,
        };

        private static double Kern => EngravingDefaults.BarlineSeparation;

        /// <summary>The bar line's own stencil extent.</summary>
        /// <remarks>
        /// LILYPOND-REF: scm/bar-line.scm:734-810 bar-line::compound-bar-line — glyphs whose
        ///   (padded, scm/bar-line.scm:40-52 get-span-glyph) span character is the replacement
        ///   character, while no main glyph has been stacked yet, build the neg-stencil; the
        ///   rest stack from X = 0 with <c>kern</c> between; the neg-stencil is attached on the
        ///   LEFT with <c>kern</c>. BarLine's <c>right-justified</c> is #f
        ///   (scm/define-grobs.scm:287), so nothing is translated.
        /// </remarks>
        public static Extent CompoundExtent(string glyph)
        {
            if (glyph.Length == 0)
                return Extent.Empty;
            string? span = SpanGlyph(glyph)?.PadRight(glyph.Length, ' ');
            double main = 0, neg = 0;
            bool firstMain = true, firstNeg = true;
            for (int i = 0; i < glyph.Length; i++)
            {
                if (span is not null && span[i] == ' ' && firstMain)
                {
                    neg += (firstNeg ? 0 : Kern) + Width(glyph[i]);
                    firstNeg = false;
                }
                else
                {
                    main += (firstMain ? 0 : Kern) + Width(glyph[i]);
                    firstMain = false;
                }
            }
            if (firstMain)
                return Extent.Empty;
            return new Extent(firstNeg ? 0 : -(neg + Kern), main);
        }

        /// <summary>The span bar's stencil extent for a bar glyph.</summary>
        /// <remarks>
        /// LILYPOND-REF: scm/bar-line.scm:984-1030 span-bar::compound-bar-line — the unpadded
        ///   span glyph walked against the bar glyph; leading replacement characters are
        ///   dropped, a later one is a spacer as wide as its bar glyph; stacked from X = 0 with
        ///   <c>kern</c>. A glyph whose span glyph is not a string gives the empty stencil.
        /// </remarks>
        public static Extent SpanExtent(string glyph)
        {
            if (SpanGlyph(glyph) is not { } span)
                return Extent.Empty;
            double right = 0;
            bool first = true;
            for (int i = 0; i < Math.Min(glyph.Length, span.Length); i++)
            {
                if (span[i] == ' ' && first)
                    continue;
                right += (first ? 0 : Kern) + Width(span[i] == ' ' ? glyph[i] : span[i]);
                first = false;
            }
            return first ? Extent.Empty : new Extent(0, right);
        }
    }

    /// <summary>
    /// The builder <see cref="Calculate"/> collects the bracket pieces into, lent from one the
    /// thread keeps between calls.
    /// </summary>
    /// <remarks>
    /// MEASURED (session 475's census at HEAD, Release, the reader's corpus, eight forward
    /// keystrokes a book): 0.62 builds a keystroke at 9.07 pieces (max 34), 743 B a keystroke,
    /// none reachable once the render returned — <c>ToImmutable</c> copies (session 459's
    /// probe) and the one exit after the rent is the last line. RENTING TAKES IT OUT OF THE
    /// DRAWER (session 421's idiom), THE CLEARING IS ON GIVE (session 456): a builder given
    /// back dirty would hand the next score this one's brackets. WHAT IT RETAINS is one emptied
    /// builder a thread at that thread's most-bracketed score.
    /// </remarks>
    [ThreadStatic]
    private static ImmutableArray<VoltaBracketLayout>.Builder? t_layouts;

    /// <summary>
    /// Gets the edge height for volta bracket hooks.
    /// </summary>
    public static double GetEdgeHeight() => EdgeHeight;
}
