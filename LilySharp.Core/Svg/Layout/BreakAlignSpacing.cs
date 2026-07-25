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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Break-align symbol types for system prefix element ordering and spacing.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm break-align-orders
/// LILYPOND-REF: lily/break-alignment-interface.cc
/// </remarks>
public enum BreakAlignSymbol
{
    /// <summary>Left edge of the system prefix (the starting reference point).</summary>
    LeftEdge,
    /// <summary>Pitch-range indicator (ambitus) shown at the start of the staff.</summary>
    Ambitus,
    /// <summary>Breath mark positioned among the prefix items.</summary>
    BreathingSign,
    /// <summary>The clef at the start of the line.</summary>
    Clef,
    /// <summary>Naturals cancelling the outgoing key signature.</summary>
    KeyCancellation,
    /// <summary>The key signature.</summary>
    KeySignature,
    /// <summary>The time signature.</summary>
    TimeSignature,
    /// <summary>The barline at the break point.</summary>
    StaffBar,
    /// <summary>The cue clef opening a cue passage.</summary>
    CueClef,
    /// <summary>The clef restoring the main part after a cue passage.</summary>
    CueEndClef,
    /// <summary>The first note column following the prefix.</summary>
    FirstNote,
    /// <summary>Right edge of the system prefix.</summary>
    RightEdge
}

/// <summary>
/// Spacing style types from LilyPond's space-alist.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/break-alignment-interface.cc:108-150 get_break_align_spacing()
/// Each style determines how the space value is interpreted:
/// - ExtraSpace: distance = extent(right) + value (space added to right extent)
/// - MinimumSpace: distance = max(extent(right) + min_pad, value)
/// - FixedSpace: distance = extent(left_right_edge) + value
/// - MinimumFixedSpace: distance = max(extent(left_right_edge) + value, min_from_left_edge)
/// - SemiFixedSpace: distance = extent(left_right_edge) + value/2 + natural/2
/// </remarks>
public enum SpacingStyle
{
    /// <summary>Adds the value to the right item's extent: <c>distance = extent(right) + value</c>.</summary>
    ExtraSpace,
    /// <summary>At least the value from the left item: <c>distance = max(extent(right) + min_pad, value)</c>.</summary>
    MinimumSpace,
    /// <summary>Fixed distance from the left/right edge: <c>distance = extent(left_right_edge) + value</c>.</summary>
    FixedSpace,
    /// <summary>Fixed spacing floored at a minimum: <c>distance = max(extent(left_right_edge) + value, min_from_left_edge)</c>.</summary>
    MinimumFixedSpace,
    /// <summary>Half natural, half fixed: <c>distance = extent(left_right_edge) + value/2 + natural/2</c>.</summary>
    SemiFixedSpace,
    /// <summary>Like <see cref="ExtraSpace"/>, but NOT stretchable: the whole distance is
    /// compressible and none of it grows (staff-spacing.cc:188-192).</summary>
    ShrinkSpace,
    /// <summary>Mostly fixed but slightly compressible, allowing the spacing to shrink under compression.</summary>
    SemiShrinkSpace
}

/// <summary>
/// A spacing entry from LilyPond's space-alist: (spacing-style . value).
/// </summary>
public readonly record struct SpacingEntry(SpacingStyle Style, double Value);

/// <summary>
/// Implements LilyPond's break-alignment-interface for system prefix spacing.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/break-alignment-interface.cc:1-200
/// LILYPOND-REF: scm/define-grobs.scm break-align-orders, Clef.space-alist,
///               KeySignature.space-alist, TimeSignature.space-alist
///
/// The break-alignment system controls ordering and spacing of "breakable" items
/// (clef, key signature, time signature) at the start of each system line.
/// Each item type has a space-alist that maps adjacent item types to spacing values.
/// </remarks>
internal static class BreakAlignSpacing
{
    /// <summary>
    /// Default break-align order for start-of-line (index 2 in LP's break-align-orders vector).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm BreakAlignment.break-align-orders
    /// Start-of-line: left-edge ambitus breathing-sign clef key-cancellation
    ///                key-signature time-signature staff-bar cue-clef cue-end-clef
    /// </remarks>
    public static readonly BreakAlignSymbol[] StartOfLineOrder = new[]
    {
        BreakAlignSymbol.LeftEdge,
        BreakAlignSymbol.Ambitus,
        BreakAlignSymbol.BreathingSign,
        BreakAlignSymbol.Clef,
        BreakAlignSymbol.KeyCancellation,
        BreakAlignSymbol.KeySignature,
        BreakAlignSymbol.TimeSignature,
        BreakAlignSymbol.StaffBar,
        BreakAlignSymbol.CueClef,
        BreakAlignSymbol.CueEndClef,
    };

    /// <summary>
    /// Looks up the spacing between two adjacent break-align symbols.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/break-alignment-interface.cc:108-150
    /// Each break-aligned grob has a space-alist that maps the next symbol
    /// to a (spacing-style . value) pair.
    /// </remarks>
    public static SpacingEntry GetSpacing(BreakAlignSymbol left, BreakAlignSymbol right)
    {
        return left switch
        {
            BreakAlignSymbol.Clef => GetClefSpacing(right),
            BreakAlignSymbol.KeyCancellation => GetKeyCancellationSpacing(right),
            BreakAlignSymbol.KeySignature => GetKeySignatureSpacing(right),
            BreakAlignSymbol.TimeSignature => GetTimeSignatureSpacing(right),
            BreakAlignSymbol.LeftEdge => new SpacingEntry(SpacingStyle.ExtraSpace, 0.0),
            BreakAlignSymbol.StaffBar => GetStaffBarSpacing(right),
            _ => new SpacingEntry(SpacingStyle.ExtraSpace, 1.0)
        };
    }

    /// <summary>
    /// Clef.space-alist from LilyPond.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:914-925 Clef.space-alist (current LP).
    ///
    /// These are the current LP extra-space values. The old minimum-space 3.5/4.2/3.7
    /// values (from an older LP / the small-clef variant at :1120-1130) produced almost
    /// identical output here only because extra-space is measured from the clef's right
    /// extent (~2.7 ss) so 2.7+0.82 ≈ 3.5 for a standard clef; the extra-space form is
    /// LP-faithful AND adapts to clef width (bass / cue / small clefs). Verified against
    /// LilyPond 2.24.4 line-start clef→key→time spacing. COORDINATE_AUDIT.md §4.3 #2.
    /// </remarks>
    private static SpacingEntry GetClefSpacing(BreakAlignSymbol right) => right switch
    {
        // (key-cancellation . (extra-space . 0.82))
        BreakAlignSymbol.KeyCancellation =>
            new SpacingEntry(SpacingStyle.ExtraSpace, 0.82),
        // (key-signature . (extra-space . 0.82))
        BreakAlignSymbol.KeySignature =>
            new SpacingEntry(SpacingStyle.ExtraSpace, 0.82),
        // (time-signature . (extra-space . 1.52))
        BreakAlignSymbol.TimeSignature =>
            new SpacingEntry(SpacingStyle.ExtraSpace, 1.52),
        // (first-note . (minimum-fixed-space . 5.0))
        BreakAlignSymbol.FirstNote =>
            new SpacingEntry(SpacingStyle.MinimumFixedSpace, 5.0),
        // (right-edge . (extra-space . 0.5))
        BreakAlignSymbol.RightEdge =>
            new SpacingEntry(SpacingStyle.ExtraSpace, 0.5),
        // (staff-bar . (extra-space . 0.7))
        BreakAlignSymbol.StaffBar =>
            new SpacingEntry(SpacingStyle.ExtraSpace, 0.7),
        _ => new SpacingEntry(SpacingStyle.ExtraSpace, 1.0)
    };

    /// <summary>
    /// KeyCancellation.space-alist from LilyPond.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:1930-1970 KeyCancellation
    /// </remarks>
    private static SpacingEntry GetKeyCancellationSpacing(BreakAlignSymbol right) => right switch
    {
        // (key-signature . (extra-space . 0.5))
        BreakAlignSymbol.KeySignature =>
            new SpacingEntry(SpacingStyle.ExtraSpace, 0.5),
        // (time-signature . (extra-space . 1.25))
        BreakAlignSymbol.TimeSignature =>
            new SpacingEntry(SpacingStyle.ExtraSpace, 1.25),
        // (first-note . (shrink-space . 2.5))  — define-grobs.scm:1947
        BreakAlignSymbol.FirstNote =>
            new SpacingEntry(SpacingStyle.ShrinkSpace, 2.5),
        // (staff-bar . (extra-space . 0.6))
        BreakAlignSymbol.StaffBar =>
            new SpacingEntry(SpacingStyle.ExtraSpace, 0.6),
        _ => new SpacingEntry(SpacingStyle.ExtraSpace, 0.5)
    };

    /// <summary>
    /// KeySignature.space-alist from LilyPond.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:1972-2014 KeySignature
    /// </remarks>
    private static SpacingEntry GetKeySignatureSpacing(BreakAlignSymbol right) => right switch
    {
        // (time-signature . (extra-space . 1.15))
        BreakAlignSymbol.TimeSignature =>
            new SpacingEntry(SpacingStyle.ExtraSpace, 1.15),
        // (first-note . (shrink-space . 2.5))  — define-grobs.scm:1996
        BreakAlignSymbol.FirstNote =>
            new SpacingEntry(SpacingStyle.ShrinkSpace, 2.5),
        // (staff-bar . (extra-space . 1.1))
        BreakAlignSymbol.StaffBar =>
            new SpacingEntry(SpacingStyle.ExtraSpace, 1.1),
        // (right-edge . (extra-space . 0.5))
        BreakAlignSymbol.RightEdge =>
            new SpacingEntry(SpacingStyle.ExtraSpace, 0.5),
        _ => new SpacingEntry(SpacingStyle.ExtraSpace, 1.0)
    };

    /// <summary>
    /// TimeSignature.space-alist from LilyPond.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:3922-3966 TimeSignature
    /// </remarks>
    private static SpacingEntry GetTimeSignatureSpacing(BreakAlignSymbol right) => right switch
    {
        // (first-note . (semi-shrink-space . 2.0))  — define-grobs.scm:3949
        BreakAlignSymbol.FirstNote =>
            new SpacingEntry(SpacingStyle.SemiShrinkSpace, 2.0),
        // (right-edge . (extra-space . 0.5))
        BreakAlignSymbol.RightEdge =>
            new SpacingEntry(SpacingStyle.ExtraSpace, 0.5),
        // (staff-bar . (extra-space . 1.0))
        BreakAlignSymbol.StaffBar =>
            new SpacingEntry(SpacingStyle.ExtraSpace, 1.0),
        _ => new SpacingEntry(SpacingStyle.ExtraSpace, 1.0)
    };

    /// <summary>
    /// BarLine.space-alist from LilyPond (the <c>staff-bar</c> break-align symbol).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:291-302 BarLine.space-alist.
    /// Transcribed entry for entry. These matter INSIDE a break-align group, where the
    /// unbroken order is <c>clef, staff-bar, key-cancellation, key-signature,
    /// time-signature</c> (:650-664), so the bar line is the LEFT symbol of every pair
    /// that follows it.
    /// <c>next-note (semi-fixed-space . 0.9)</c> has no <see cref="BreakAlignSymbol"/> of
    /// its own; it is owned by <c>SpacingRules.GetBarlineToItemSpace</c>, which cites the
    /// same alist entry.
    /// </remarks>
    private static SpacingEntry GetStaffBarSpacing(BreakAlignSymbol right) => right switch
    {
        // (time-signature . (extra-space . 0.75)) — was falling through to the 1.0
        // default, which is NOT what LilyPond spaces a bar line to a time signature by.
        BreakAlignSymbol.TimeSignature =>
            new SpacingEntry(SpacingStyle.ExtraSpace, 0.75),
        // (clef . (extra-space . 1.0))
        BreakAlignSymbol.Clef =>
            new SpacingEntry(SpacingStyle.ExtraSpace, 1.0),
        // (key-signature . (extra-space . 1.0))
        BreakAlignSymbol.KeySignature =>
            new SpacingEntry(SpacingStyle.ExtraSpace, 1.0),
        // (key-cancellation . (extra-space . 1.0))
        BreakAlignSymbol.KeyCancellation =>
            new SpacingEntry(SpacingStyle.ExtraSpace, 1.0),
        // (ambitus . (extra-space . 1.0))
        BreakAlignSymbol.Ambitus =>
            new SpacingEntry(SpacingStyle.ExtraSpace, 1.0),
        // (first-note . (semi-shrink-space . 1.3))
        BreakAlignSymbol.FirstNote =>
            new SpacingEntry(SpacingStyle.SemiShrinkSpace, 1.3),
        // (right-edge . (extra-space . 0.0))
        BreakAlignSymbol.RightEdge =>
            new SpacingEntry(SpacingStyle.ExtraSpace, 0.0),
        _ => new SpacingEntry(SpacingStyle.ExtraSpace, 1.0)
    };

    /// <summary>
    /// Calculates the effective distance for a spacing entry, considering item extents.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/staff-spacing.cc:166-198 Staff_spacing::get_spacing — the
    /// break-align style set (fixed / extra / semi-fixed / minimum / minimum-fixed /
    /// shrink / semi-shrink). LP builds a SPRING (ideal, min=fixed, stretchability); this
    /// returns the IDEAL distance only, so the shrink/stretch distinctions collapse.
    /// With <c>fixed = last_ext[RIGHT]</c> (= leftItemRightExtent) and distance = value:
    /// - ExtraSpace / FixedSpace / SemiFixedSpace / SemiShrinkSpace: ideal = leftRight + value
    /// - MinimumSpace / MinimumFixedSpace: LP is last_ext[LEFT] + max(item length, value);
    ///   approximated here as max(value, leftRight + minPad) — the left item's LEFT edge and
    ///   length, and the right item's own extent, are not threaded through this signature.
    /// </remarks>
    public static double CalculateDistance(SpacingEntry entry,
        double leftItemRightExtent)
    {
        const double minPad = 0.1;  // minimum padding between items

        return entry.Style switch
        {
            // LILYPOND-REF: extra-space: adds value to the right extent of left item
            SpacingStyle.ExtraSpace =>
                leftItemRightExtent + entry.Value,

            // LILYPOND-REF: minimum-space: at least 'value' from left edge of left item
            SpacingStyle.MinimumSpace =>
                Math.Max(entry.Value, leftItemRightExtent + minPad),

            // LILYPOND-REF: fixed-space: fixed distance from right edge of left item
            SpacingStyle.FixedSpace =>
                leftItemRightExtent + entry.Value,

            // LILYPOND-REF: minimum-fixed-space: at least 'value' from left edge (like minimum-space)
            // but also ensures fixed spacing from right edge when item is wider than value
            SpacingStyle.MinimumFixedSpace =>
                Math.Max(entry.Value, leftItemRightExtent + minPad),

            // semi-fixed-space (staff-spacing.cc:176-179): fixed = leftRight + distance/2,
            // ideal = fixed + distance/2 = leftRight + distance. (The distinction from
            // extra-space is the SPRING min/stretch, which this fixed-distance model drops.)
            SpacingStyle.SemiFixedSpace =>
                leftItemRightExtent + entry.Value,

            // shrink-space (staff-spacing.cc:188-192) / semi-shrink-space (:193-197): the
            // same ideal as extra-space / semi-fixed (leftRight + distance); they differ
            // only by being non-stretchable, which this single-distance model does not
            // represent. <see cref="SpaceAlistDistances"/> is the model that does.
            SpacingStyle.ShrinkSpace =>
                leftItemRightExtent + entry.Value,

            SpacingStyle.SemiShrinkSpace =>
                leftItemRightExtent + entry.Value,

            _ => leftItemRightExtent + entry.Value
        };
    }

    /// <summary>One placed break-align column: its symbol and ink extent, in the column frame.</summary>
    public readonly record struct PlacedColumn(BreakAlignSymbol Symbol, double Left, double Right);

    /// <summary>
    /// LilyPond's <c>Break_alignment_interface::calc_positioning_done</c> as ONE forward walk over
    /// an ordered break-align group — the single engine both the line-start prefix
    /// (<see cref="SolvePrefixColumns"/>) and the mid-line boundary (<c>BoundaryColumn</c>) run, so
    /// the one algorithm cannot drift between them the way two hand-rolled copies could.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/break-alignment-interface.cc:152-283 calc_positioning_done, :239-247:
    ///   extra-space    <c>offsets[r] = extents[l][RIGHT] + distance - extents[r][LEFT]</c>;
    ///   minimum-space  <c>offsets[r] = max(extents[l][RIGHT], distance)</c>.
    /// Every break-aligned stencil here starts its ink at its own origin (<c>extents[.][LEFT] = 0</c>),
    /// so a present item's LEFT = the previous item's ink RIGHT + the LEFT item's space-alist
    /// distance to it (<see cref="GetSpacing"/>). Empty items (<c>Width &lt;= 0</c>) are skipped, as
    /// LilyPond skips empty break-align groups (break-alignment-interface.cc:145-146,155-156): a grob
    /// that draws nothing neither consumes a gap nor anchors its neighbour. <paramref name="startLeft"/>
    /// is where the first present item's LEFT sits — the LeftEdge→Clef gap
    /// (<see cref="EngravingDefaults.ClefGlyphXOffset"/>) at a line start, 0 at a mid-line boundary
    /// whose first grob is the column origin.
    /// </remarks>
    public static IReadOnlyList<PlacedColumn> SolveColumns(
        IEnumerable<(BreakAlignSymbol Symbol, double Width)> items, double startLeft)
    {
        var placed = new List<PlacedColumn>();
        BreakAlignSymbol? prev = null;
        double prevLeft = 0.0, prevWidth = 0.0;
        foreach (var (symbol, width) in items)
        {
            if (width <= 0.0)
                continue;
            double left;
            if (prev is { } p)
            {
                // LP measures the distance off the LEFT item's own RIGHT extent — the exact
                // width, extents[l][RIGHT] with LEFT=0 — NOT a reconstructed prevRight-prevLeft
                // (which rounds differently and shifts a line-start note by ~0.01 at a rounding
                // boundary — test/keysig-change). extents[r][LEFT] is 0, so it drops out.
                var entry = GetSpacing(p, symbol);
                double offset = entry.Style == SpacingStyle.MinimumSpace
                    ? Math.Max(prevWidth, entry.Value)
                    : prevWidth + entry.Value;
                left = prevLeft + offset;
            }
            else
            {
                left = startLeft;
            }
            placed.Add(new PlacedColumn(symbol, left, left + width));
            prevLeft = left;
            prevWidth = width;
            prev = symbol;
        }
        return placed;
    }

    /// <summary>
    /// The shared X of each break-align column at a line start (LeftEdge→Clef→KeySignature→
    /// TimeSignature) plus the prefix right edge — one column table for the whole system, so
    /// every staff draws its clef/key/time at the SAME X and the signatures stay aligned.
    /// </summary>
    /// <remarks>
    /// <c>ClefX/KeyX/TimeX</c> are the LEFT edge of each column, relative to the prefix origin
    /// (0 = line start, before the system indent). A column is present only if its
    /// <c>Has*</c> flag is set; an absent column's X is 0.
    /// <see cref="PrefixColumns.Right"/> is where the prefix ink ends (the first-note spring
    /// starts here, see <see cref="FirstNoteSpring"/>).
    /// </remarks>
    public readonly record struct PrefixColumns(
        double ClefX, double KeyX, double TimeX, double Right, bool HasKey, bool HasTime);

    /// <summary>
    /// Solves the line-start break-align column table — LilyPond's
    /// <c>Break_alignment_interface::calc_positioning_done</c>, ported as a pure forward walk.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/break-alignment-interface.cc:152-256 calc_positioning_done. Each
    /// column's offset = the previous column's GROUP right extent + the space-alist distance
    /// (<c>extents[idx][RIGHT] + distance - extents[next][LEFT]</c>, with the clef/key/time
    /// stencils starting at 0 so <c>[LEFT] = 0</c>). The extents are GROUP extents — the union
    /// across the system's staves — so the caller passes the WIDEST clef (<see cref="clefWidth"/>)
    /// and the WIDEST key's INK width (<paramref name="keyInkWidth"/>, from
    /// <see cref="SpacingRules.KeySignatureInkWidth"/> — the SAME model the draw walk uses, so
    /// a custom (non-traditional) signature reserves exactly what it draws): the whole point of
    /// break-alignment is that one column spans all staves, so a grand staff's bass F clef fixes
    /// the treble staff's meter column and a transposed part's larger key fixes everyone's time
    /// column. A pure forward pass — no fixpoint, no dependency on note positions — so it slots
    /// exactly where the scalar prefix width used to be computed, before the system spring solve.
    /// </remarks>
    public static PrefixColumns SolvePrefixColumns(
        double clefWidth,
        double keyInkWidth,
        bool includeTimeSignature, int timeSigBeats = 4, int timeSigBeatType = 4)
    {
        bool hasKey = keyInkWidth > 0.0;

        // The line-start break-align order (clef, then the key and time when present).
        // LeftEdge → Clef opens the prefix: break-alignment's origin (LeftEdge, extent 0) spaces
        // the clef in by extra-space 0.8 (LILYPOND-REF LeftEdge.space-alist (clef . (extra-space .
        // 0.8))), which is where the clef's LEFT starts (startLeft below).
        var items = new List<(BreakAlignSymbol, double)>
        {
            (BreakAlignSymbol.Clef, clefWidth),   // Clef group extent RIGHT (LEFT = 0)
        };
        if (hasKey)
            items.Add((BreakAlignSymbol.KeySignature, keyInkWidth));
        if (includeTimeSignature)
            items.Add((BreakAlignSymbol.TimeSignature,
                GlyphMetrics.GetTimeSigWidth(timeSigBeats, timeSigBeatType)));

        var placed = SolveColumns(items, EngravingDefaults.ClefGlyphXOffset);

        double clefX = EngravingDefaults.ClefGlyphXOffset, keyX = 0.0, timeX = 0.0;
        foreach (var col in placed)
        {
            switch (col.Symbol)
            {
                case BreakAlignSymbol.Clef: clefX = col.Left; break;
                case BreakAlignSymbol.KeySignature: keyX = col.Left; break;
                case BreakAlignSymbol.TimeSignature: timeX = col.Left; break;
            }
        }

        // The prefix ends at the last item's INK. The prefix→first-note distance is NOT part of
        // the prefix: it is carried by the first measure's leading spring (see FirstNoteSpring)
        // so it can take part in spring solving with the proper minimum — adding it here AND in
        // the spring double-counted the gap and line-start measures came out ~3x wide.
        double right = placed.Count > 0
            ? placed[placed.Count - 1].Right
            : EngravingDefaults.ClefGlyphXOffset;
        return new PrefixColumns(clefX, keyX, timeX, right, hasKey, includeTimeSignature);
    }

    /// <summary>
    /// The system prefix width — the right edge of the break-align column table
    /// (<see cref="SolvePrefixColumns"/>).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/break-alignment-interface.cc.</remarks>
    public static double CalculatePrefixWidth(
        double clefWidth,
        double keyInkWidth,
        bool includeTimeSignature, int timeSigBeats = 4, int timeSigBeatType = 4)
        => SolvePrefixColumns(clefWidth, keyInkWidth,
            includeTimeSignature, timeSigBeats, timeSigBeatType).Right;

    /// <summary>
    /// The FIXED distance, the IDEAL distance and the STRETCHABILITY one space-alist entry
    /// makes against a left grob whose own ink extent is
    /// <paramref name="lastExtLeft"/>..<paramref name="lastExtRight"/> — the middle of
    /// <c>Staff_spacing::get_spacing</c>, transcribed branch for branch.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/staff-spacing.cc:161-200:
    /// <code>
    ///   Real fixed = last_ext[RIGHT];
    ///   Real ideal = fixed + 1.0;
    ///   fixed-space          fixed += distance;                       ideal = fixed;
    ///   extra-space                                                   ideal = fixed + distance;
    ///   semi-fixed-space     fixed += distance / 2;                   ideal = fixed + distance / 2;
    ///   minimum-space                                                 ideal = last_ext[LEFT] + max (last_ext.length (), distance);
    ///   minimum-fixed-space  fixed = last_ext[LEFT] + max (...);      ideal = fixed;
    ///   shrink-space                                                  ideal = fixed + distance;       is_stretchable = false;
    ///   semi-shrink-space    fixed += distance / 2;                   ideal = fixed + distance / 2;   is_stretchable = false;
    ///   Real stretchability = is_stretchable ? ideal - fixed : 0;
    /// </code>
    /// Every distance is in the LEFT COLUMN's frame, because <c>last_ext</c> is
    /// <c>break_item-&gt;extent (col, X_AXIS)</c> (spacing-interface.cc:217) — the grob's
    /// INK relative to the column origin, with no <c>extra-spacing-width</c> on it (that
    /// widens spacing BOXES, which is a different question — <c>min_dist</c>'s).
    /// <para>
    /// ⚠️ The <c>minimum-*</c> pair is the only one measured from the left grob's LEFT
    /// edge, and it ABSORBS that grob's width rather than following it: a Clef's
    /// <c>(first-note . (minimum-fixed-space . 5.0))</c> puts the first note 5.0 out from
    /// where the clef's ink STARTS whenever the clef is narrower than that. Which is why
    /// the caller must pass the ink of the staff's OWN clef and not the break-align
    /// GROUP's: on a notation+tab system the TAB clef's ink opens 0.2 later than the
    /// group's left edge, and that 0.2 lands directly on its first note.
    /// </para>
    /// <para>
    /// ⚠️ <c>minimum-space</c> leaves <c>fixed</c> at <c>last_ext[RIGHT]</c> — only
    /// <c>minimum-fixed-space</c> replaces it. Transcribed, not tidied.
    /// </para>
    /// </remarks>
    public static (double Fixed, double Ideal, double Stretchability) SpaceAlistDistances(
        SpacingEntry entry, double lastExtLeft, double lastExtRight)
    {
        double distance = entry.Value;
        bool isStretchable = true;

        double fixed_ = lastExtRight;
        double ideal = fixed_ + 1.0;
        double length = lastExtRight - lastExtLeft;

        switch (entry.Style)
        {
            case SpacingStyle.FixedSpace:
                fixed_ += distance;
                ideal = fixed_;
                break;
            case SpacingStyle.ExtraSpace:
                ideal = fixed_ + distance;
                break;
            case SpacingStyle.SemiFixedSpace:
                fixed_ += distance / 2;
                ideal = fixed_ + distance / 2;
                break;
            case SpacingStyle.MinimumSpace:
                ideal = lastExtLeft + Math.Max(length, distance);
                break;
            case SpacingStyle.MinimumFixedSpace:
                fixed_ = lastExtLeft + Math.Max(length, distance);
                ideal = fixed_;
                break;
            case SpacingStyle.ShrinkSpace:
                ideal = fixed_ + distance;
                isStretchable = false;
                break;
            case SpacingStyle.SemiShrinkSpace:
                fixed_ += distance / 2;
                ideal = fixed_ + distance / 2;
                isStretchable = false;
                break;
        }

        return (fixed_, ideal, isStretchable ? ideal - fixed_ : 0);
    }
}
