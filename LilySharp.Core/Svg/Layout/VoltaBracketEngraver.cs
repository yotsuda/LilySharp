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
    internal const double LineThickness = 1.6 * EngravingDefaults.LineThickness;

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
    private const double YOffsetYUp =
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

    // Padding from barline
    private const double StartPadding = 0.3;
    private const double EndPadding = 0.3;

    /// <summary>
    /// Calculates layout for all volta brackets.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/volta-bracket.cc — brackets split at system breaks
    /// When a volta bracket spans multiple systems, it is split into segments.
    /// The first segment shows the volta text and has no right hook.
    /// Continuation segments have no left hook and no text.
    /// The last segment has a right hook if the bracket is closed.
    /// </remarks>
    public static ImmutableArray<VoltaBracketLayout> Calculate(
        ImmutableArray<VoltaBracketItem> voltaBrackets,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (voltaBrackets.IsDefaultOrEmpty)
            return ImmutableArray<VoltaBracketLayout>.Empty;

        var measureToSystemIdx = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);
        var layouts = ImmutableArray.CreateBuilder<VoltaBracketLayout>();

        for (int bi = 0; bi < voltaBrackets.Length; bi++)
        {
            var bracket = voltaBrackets[bi];
            if (bracket.StartMeasureIndex >= measureLayouts.Length ||
                bracket.EndMeasureIndex >= measureLayouts.Length)
                continue;

            foreach (var (segment, _) in SpannerBreakSubstitution.BrokenPieces(
                bracket.StartMeasureIndex, bracket.EndMeasureIndex, systems, measureToSystemIdx))
            {
                if (segment.StartMeasureIndex >= measureLayouts.Length ||
                    segment.EndMeasureIndex >= measureLayouts.Length)
                    continue;

                var segStartMeasure = measureLayouts[segment.StartMeasureIndex];
                var segEndMeasure = measureLayouts[segment.EndMeasureIndex];

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

                layouts.Add(new VoltaBracketLayout(
                    segment.StartMeasureIndex,
                    segment.EndMeasureIndex,
                    segStartMeasure.X + StartPadding,
                    segEndMeasure.X + segEndMeasure.Width - EndPadding,
                    // Y-up from the system top (the renderer resolves the segment's system top).
                    YOffsetYUp + staffBelowTop,
                    segText,
                    segClosed,
                    bracket.SourcePosition,
                    bi
                ));
            }
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// Gets the edge height for volta bracket hooks.
    /// </summary>
    public static double GetEdgeHeight() => EdgeHeight;
}
