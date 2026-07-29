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
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for a dynamic marking.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: define-grobs.scm:1433-1460 DynamicText grob
/// LILYPOND-REF: define-grobs.scm:1401-1431 DynamicLineSpanner grob
/// </remarks>
public readonly record struct DynamicLayout(
    int MeasureIndex,       // Measure containing this dynamic
    int ItemIndex,          // Item index within measure (for X alignment)
    double X,               // Absolute X position (staff spaces from score start)
    double YUp,             // Y in the LilyPond-native Y-up frame: staff-spaces ABOVE
                            // this dynamic's staff middle line, up-positive (frame B).
                            // The renderer/stacker reflect it to device (middle − Y-up)
                            // against the staff middle they resolve.
    string Text,            // Dynamic text ("p", "ff", etc.)
    int SourcePosition,     // For click-to-source mapping (re-derived at render from SourceIndex)
    int SourceIndex = -1,   // F3/B: index into score.Dynamics — the position-independent
                            // reference the renderer resolves data-pos from the LIVE score, so a
                            // reused (cached) layout emits fresh data-pos. See SharedRenderer.ResolveDataPos.
                            // -1 = "no source" (left unresolved): used by unit tests that build layouts directly.
    bool IsAbove = false,   // Forced above the staff (from @f.up); default below.
    int StaffIndex = 0,     // Which staff this dynamic hangs under (per-staff stacking).
    bool IsExpressiveText = false // @text("…"): plain italic, not a dynamic level.
);

/// <summary>
/// Calculates positions for dynamic markings.
/// Implements LilyPond's dynamic positioning algorithm.
/// </summary>
/// <remarks>
/// LILYPOND-REF: dynamic-align-engraver.cc:36-61 Dynamic_align_engraver class
/// LILYPOND-REF: side-position-interface.cc:92-111 axis_aligned_side_helper
///
/// LilyPond places dynamics below the staff (direction = DOWN) with:
/// - outside-staff-priority: 250
/// - padding: 0.6 staff spaces
/// - staff-padding: 0.1 staff spaces
/// - Y-offset calculated by side-position-interface::y-aligned-side
/// </remarks>
internal static class DynamicEngraver
{
    // One home: EngravingDefaults' outside-staff declaration table (define-grobs.scm
    // values; the LILYPOND-REFs live beside the table entries).
    private const double Padding = EngravingDefaults.DynamicLineSpannerPadding;
    private const double StaffPadding = EngravingDefaults.DynamicLineSpannerStaffPadding;
    private const double MinimumSpace = EngravingDefaults.DynamicLineSpannerMinimumSpace;

    // LILYPOND-REF: define-grobs.scm:1450 DynamicText (Y-offset . (scale-by-font-size
    //   -0.6)) — "center on an 'm'". side-position places the SPANNER, and the text hangs
    //   this far below the spanner's own origin, so the two frames differ by 0.6.
    private const double TextOffsetInSpanner = 0.6;

    // Staff geometry (5 lines = 4 staff spaces)
    private const double StaffMiddle = EngravingDefaults.StaffMiddle;  // staff bottom (4.0) / 2

    // LILYSHARP-OWN: ink above / below the baseline for a label LilyPond does NOT spell in
    // the fetaText dynamic letters — free expressive text (@text), which has no LilyPond
    // grob at all: Lily# rides it on the DynamicText pipeline and draws it in a serif face
    // (SharedRenderer.DrawDynamics). There is no LilyPond formula to port, so these stay
    // nominal. A real dynamic never reaches them: GlyphMetrics.TryGetDynamicInk answers
    // from the font instead, per glyph.
    // ⚠️ DEBT, carried unchanged rather than re-tuned: these are the values the code
    // already had, and 0.64's old comment derived it by MEASURING a LilyPond 2.24.4
    // forced-up dynamic. That derivation is void — the thing it was fitting is the `f`
    // glyph's 0.692002 ink, which now comes from the font — but replacing the number for
    // free text would be fitting a second time. It needs a source, not a better guess.
    // These are also the single fallback for all three paths that used to keep their own
    // (this file's 0.64, the stacker's 0.3, the skyline's 0.3): three numbers for ONE
    // quantity is the duplication that let the real defect hide. 0.64 is the largest, so
    // unifying on it can only reserve more room, never overlap.
    private const double FallbackAscent = 1.2;
    private const double FallbackDescent = 0.64;

    /// <summary>
    /// A label's own ink above (<c>Ascent</c>) and below (<c>Descent</c>) its baseline,
    /// in staff spaces. See <see cref="GlyphMetrics.TryGetDynamicInk"/>: LilyPond's
    /// DynamicText extent is the drawn glyphs' ink, so it differs per dynamic.
    /// </summary>
    internal static (double Ascent, double Descent) InkOf(string? text, bool expressive)
        => !expressive && GlyphMetrics.TryGetDynamicInk(text, out double bottom, out double top)
            ? (top, -bottom)
            : (FallbackAscent, FallbackDescent);

    // Vertical step between two dynamics that fall on the same note column.
    internal const double StackStep = 2.0;

    /// <summary>
    /// Calculates layout for all dynamics in a score.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: side-position-interface.cc:193-400 aligned_side()
    /// LILYPOND-REF: dynamic-align-engraver.cc:120-180 process_acknowledged()
    ///
    /// Dynamics are placed below the staff, avoiding collision with notes
    /// that extend below the staff (low notes, stems down).
    /// </remarks>
    public static ImmutableArray<DynamicLayout> Calculate(
        Score score,
        ImmutableArray<DynamicItem> dynamics,
        ImmutableArray<MeasureLayout> measureLayouts,
        ImmutableArray<Voice> voices = default,
        Dictionary<int, ImmutableArray<Voice>>? voicesByStaff = null,
        Dictionary<int, ImmutableArray<Measure>>? measuresByStaff = null)
    {
        if (dynamics.IsDefaultOrEmpty)
            return ImmutableArray<DynamicLayout>.Empty;

        var layouts = ImmutableArray.CreateBuilder<DynamicLayout>(dynamics.Length);

        var fallbackVoices = voices.IsDefaultOrEmpty ? ImmutableArray.Create(score.Voice) : voices;

        // Two voices can carry a dynamic on the SAME note column (e.g. an upper
        // voice @f and a lower voice @p in a << \\ >>). They share (measure,
        // item) and would draw on top of each other; stack the 2nd+ downward so
        // both stay legible. Keyed by staff too: same-column dynamics on
        // DIFFERENT staves are independent and must not stack onto each other.
        // Keyed by side too (IsAbove): a column may carry one dynamic above and one
        // below, each stacking AWAY from the staff independently.
        var stackAt = new Dictionary<(int, int, int, bool), int>();

        for (int di = 0; di < dynamics.Length; di++)
        {
            var dynamic = dynamics[di];
            // Find the measure layout
            if (dynamic.MeasureIndex >= measureLayouts.Length)
                continue;

            var measureLayout = measureLayouts[dynamic.MeasureIndex];

            // Bounds guard (single-staff layouts only; multi-staff layouts
            // resolve through timing-aligned columns).
            if (measureLayout.Columns.IsDefaultOrEmpty
                && dynamic.ItemIndex >= measureLayout.Items.Length)
                continue;

            // Resolve this dynamic's OWN staff: its voices (to clear the right
            // stems), its measures (for timing), and the staff's vertical offset
            // within the system (so it sits under its own staff, not the first).
            var dynVoices = voicesByStaff != null
                && voicesByStaff.TryGetValue(dynamic.StaffIndex, out var vv) ? vv : fallbackVoices;
            var dynMeasures = LayoutUtilities.ResolveStaffMeasures(measuresByStaff, dynamic.StaffIndex, score.Voice.Measures);

            // Calculate X position (centered on the note)
            // LILYPOND-REF: define-grobs.scm:1444 self-alignment-X = CENTER
            double x = measureLayout.X + LayoutUtilities.GetItemXOffset(
                dynMeasures, dynamic.MeasureIndex, dynamic.ItemIndex, measureLayout);

            // This label's own ink, from the font — the glyph is what LilyPond's
            // DynamicText extent is made of, so it is per-dynamic, not a constant.
            var (ascent, descent) = InkOf(dynamic.Text, dynamic.IsExpressiveText);

            // The supports: EVERY voice's note column at this timing (a lower voice's
            // down-stem must not be overlapped by a dynamic positioned from the upper
            // voice's stem-up note), floored by the staff symbol.
            // LILYPOND-REF: side-position-interface.cc:265-330 skyline-based positioning
            double dir = dynamic.IsAbove ? 1.0 : -1.0;
            double supportEdge = ColumnSupportEdge(
                dynVoices, dynamic.MeasureIndex, dynamic.ItemIndex, dir);

            // A wide (or CJK, full-em) label centred on this note also covers its
            // neighbours' columns; those noteheads are supports too, so the label never
            // overprints an adjacent lower note. (Below-staff placement; the
            // forced-above path is a follow-up.)
            if (!dynamic.IsAbove)
                supportEdge = WidenToNeighbors(dynMeasures, dynamic.MeasureIndex,
                    measureLayout, x, LabelHalfWidth(dynamic.Text, dynamic.IsExpressiveText),
                    supportEdge);

            double y = BaselineY(dir, supportEdge, ascent, descent);

            var key = (dynamic.MeasureIndex, dynamic.ItemIndex, dynamic.StaffIndex, dynamic.IsAbove);
            int depth = stackAt.GetValueOrDefault(key, 0);
            stackAt[key] = depth + 1;
            // Stack each successive same-column dynamic AWAY from the staff. In the
            // native Y-up frame that is up (+) for above and down (−) for below.
            y += (dynamic.IsAbove ? depth : -depth) * StackStep;

            // y is already in the LilyPond-native Y-up frame (staff-spaces above the
            // staff middle); no staff offset is baked — the renderer/stacker resolve
            // the staff middle at their own boundary.
            double yUp = y;

            layouts.Add(new DynamicLayout(
                dynamic.MeasureIndex,
                dynamic.ItemIndex,
                x,
                yUp,
                dynamic.Text,
                dynamic.SourcePosition,
                di,
                dynamic.IsAbove,
                dynamic.StaffIndex,
                dynamic.IsExpressiveText
            ));
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// The staff symbol's own extent on either side — the outermost line's INK, half a
    /// line thickness past its centre. Written as the derivation, not as 2.05.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/side-position-interface.cc:323-330 —
    ///   <c>if (include_staff) dim.set_minimum_height (staff_extents[dir]);</c> puts the
    ///   staff symbol's extent under the supports' skyline as a MINIMUM, so a dynamic with
    ///   nothing hanging below it still sides off the staff. Asked of the grob, the staff
    ///   extent is (-2.05 . 2.05) — the same ink 854a0e95 seeded into the skylines.
    ///   <c>include_staff</c> (:217-220) is true exactly because DynamicLineSpanner sets
    ///   staff-padding.
    /// </remarks>
    private static double StaffExtent
        => StaffMiddle + EngravingDefaults.StaffLineThickness / 2;

    /// <summary>
    /// The DynamicText baseline that <c>side-position-interface</c> produces for a
    /// DynamicLineSpanner whose supports reach <paramref name="supportEdge"/> on the
    /// <paramref name="dir"/> side (+1 up, −1 down), in the native Y-up frame.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/side-position-interface.cc:188-455 aligned_side, transcribed for
    ///   this grob (side-axis Y, so <c>a == Y_AXIS</c> and <c>ss == 1</c> staff space):
    /// <code>
    ///   :323-330  if (include_staff) dim.set_minimum_height (staff_extents[dir]);
    ///   :354-358  total_off = dir * dim.distance (my_dim, horizon-padding);
    ///   :370      total_off += dir * ss * padding;
    ///   :384-385  if (minimum_space >= 0 &amp;&amp; total_off * dir &lt; minimum_space)
    ///               total_off = minimum_space * dir;
    ///   :433-453  diff = dir * staff_extent[dir] + staff_padding - dir * total_off;
    ///             total_off += dir * max (diff, 0.0);
    /// </code>
    ///   <c>my_dim</c> is the spanner's OWN skyline on the facing side (<c>skyp[-dir]</c>,
    ///   :225,259), so the distance is taken to that edge and not to its origin — which is
    ///   why the text's -0.6 offset inside the spanner enters here and cancels again when
    ///   the baseline is read back out.
    /// ⚠️ staff-padding is NOT a second padding, which is what Lily# used to spend: the
    ///   :433-453 block is a FLOOR on the grob's own refpoint, reached only when the
    ///   supports put the grob nearer than that. Nor does the staff enter at its line
    ///   CENTRES; :323-330 takes its extent. The two errors cancelled, which is how a test
    ///   pinned to LilyPond's forced-up clearance passed for so long — 2.0 + 0.1 + 0.6 and
    ///   a nominal 0.64 descent reach the same total as 2.05 + 0.6 and the `f` glyph's
    ///   own 0.692002.
    /// </remarks>
    private static double BaselineY(double dir, double supportEdge,
        double ascent, double descent)
    {
        // The spanner's own Y-extent about its origin: the text hangs
        // TextOffsetInSpanner below it, so both of the text's edges shift with it.
        double myTop = ascent - TextOffsetInSpanner;
        double myBottom = -(descent + TextOffsetInSpanner);
        // my_dim = skyp[-dir]: the edge that FACES the supports.
        double myFacing = dir > 0 ? myBottom : myTop;

        // :354-358 — the offset that brings my facing edge onto the support skyline.
        double totalOff = supportEdge - myFacing;
        // :370
        totalOff += dir * Padding;
        // :384-385
        if (totalOff * dir < MinimumSpace)
            totalOff = MinimumSpace * dir;
        // :433-453 — dir * staff_extent[dir] is StaffExtent on either side, and
        // (staff_position - parent_position) drops out: both refpoints are this staff.
        double diff = StaffExtent + StaffPadding - dir * totalOff;
        totalOff += dir * Math.Max(diff, 0.0);

        // total_off positions the SPANNER; the baseline sits TextOffsetInSpanner below.
        return totalOff - TextOffsetInSpanner;
    }

    /// <summary>
    /// Baseline Y in the native Y-up frame (staff-spaces above the staff middle,
    /// up-positive; a below-staff dynamic is negative) the dynamic at a given note
    /// column occupies, BEFORE same-column stacking. Exposed so the inter-staff
    /// skyline can widen the gap by the dynamic's downward reach (otherwise a low
    /// lower-voice's dynamic overlaps the staff below).
    /// </summary>
    internal static double ColumnBaselineY(
        ImmutableArray<Voice> voices, int measureIndex, int itemIndex,
        double ascent, double descent)
        => BaselineY(-1.0, ColumnSupportEdge(voices, measureIndex, itemIndex, -1.0),
            ascent, descent);

    /// <summary>
    /// ABOVE-staff mirror of <see cref="ColumnBaselineY"/>: the baseline Y a forced-above
    /// dynamic occupies in the native Y-up frame (positive, above the staff middle),
    /// BEFORE same-column stacking. Exposed so the inter-staff skyline widens the gap
    /// to the staff ABOVE by the dynamic's upward reach.
    /// </summary>
    internal static double ColumnAboveBaselineY(
        ImmutableArray<Voice> voices, int measureIndex, int itemIndex,
        double ascent, double descent)
        => BaselineY(1.0, ColumnSupportEdge(voices, measureIndex, itemIndex, 1.0),
            ascent, descent);

    /// <summary>
    /// The supports' skyline edge on the <paramref name="dir"/> side (+1 up, −1 down):
    /// the extreme of EVERY voice's note column at this timing — so in a
    /// &lt;&lt; \\ &gt;&gt; the dynamic sides off the lower voice's down-stem rather than
    /// sitting through it — with the staff symbol's own extent as the minimum.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/side-position-interface.cc:265-321 (supports merged into
    ///   <c>dim</c>) and :323-330 (<c>dim.set_minimum_height (staff_extents[dir])</c>).
    /// </remarks>
    /// <summary>
    /// The UP support edge of one note column in the staff-middle Y-up frame — exposed
    /// for the trill spanner's aligned_side, which reads the same side supports (its
    /// spanned note columns, floored by the staff extent) on the UP side.
    /// </summary>
    internal static double ColumnUpEdge(
        ImmutableArray<Voice> voices, int measureIndex, int itemIndex)
        => ColumnSupportEdge(voices, measureIndex, itemIndex, 1.0);

    private static double ColumnSupportEdge(
        ImmutableArray<Voice> voices, int measureIndex, int itemIndex, double dir)
    {
        var vs = voices.IsDefaultOrEmpty ? ImmutableArray<Voice>.Empty : voices;
        // :323-330 — the staff is the floor under whatever the notes contribute.
        double edge = dir * StaffExtent;
        bool multiVoice = vs.Length > 1;
        for (int vi = 0; vi < vs.Length; vi++)
        {
            var voice = vs[vi];
            if (measureIndex >= voice.Measures.Length)
                continue;
            var items = voice.Measures[measureIndex].Items;
            if (itemIndex >= items.Length)
                continue;
            // In a multi-voice staff the stems are force-flipped (voice 1 up,
            // voice 2 down) regardless of the note's pitch-default StemUp, so a
            // low note in the lower voice still has a long DOWN stem to clear.
            bool? forcedStemUp = multiVoice ? VoiceDefaults.GetDefaultStemUp(vi + 1) : null;
            double e = dir > 0
                ? GetHighestExtent(items[itemIndex], forcedStemUp)
                : GetLowestExtent(items[itemIndex], forcedStemUp);
            edge = dir > 0 ? Math.Max(edge, e) : Math.Min(edge, e);
        }
        return edge;
    }

    // The dynamic/expressive label is drawn at FontSize*0.5 = 2.0 with
    // TextAnchor.Middle (see DrawDynamics), so it extends this far to each side of
    // the note. TextFontMetrics gives a CJK glyph a full em — the bundled Latin face has
    // no glyph for it and the renderer draws it from a system fallback — so a "これ" label
    // reports its true (wide) extent.
    private const double DynamicFontSize = 2.0;

    private static double LabelHalfWidth(string text, bool expressive)
    {
        double w = expressive
            ? LilySharp.Core.Rendering.TextFontMetrics.Serif(text, DynamicFontSize)
            : LilySharp.Core.Rendering.TextFontMetrics.SerifBold(text, DynamicFontSize);
        return w / 2.0;
    }

    /// <summary>
    /// Pushes a below-staff label clear of EVERY note whose column its width
    /// overlaps, not just the annotated note's. A wide (or CJK, full-em) label
    /// centred on a high note otherwise overprints its lower neighbours.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/side-position-interface.cc:189-320 aligned_side() — an
    ///   outside-staff grob is pushed off the skyline of EVERY support whose skyline
    ///   overlaps its horizontal extent, not one column.
    /// LILYPOND-REF: lily/axis-group-interface.cc:45,395 outside-staff skyline
    ///   (default_outside_staff_padding_ = 0.46).
    /// </remarks>
    private static double WidenToNeighbors(
        ImmutableArray<Measure> measures, int measureIndex,
        MeasureLayout measureLayout, double labelX, double halfWidth, double supportEdge)
    {
        if (measureIndex >= measures.Length)
            return supportEdge;
        var items = measures[measureIndex].Items;
        for (int j = 0; j < items.Length; j++)
        {
            double itemX = measureLayout.X + LayoutUtilities.GetItemXOffset(
                measures, measureIndex, j, measureLayout);
            // The note's head lies (even partly) under the label's horizontal span.
            if (Math.Abs(itemX - labelX) > halfWidth + EngravingDefaults.NoteheadHalfWidth)
                continue;
            // Native Y-up: the neighbour joins the support skyline (the min, below).
            supportEdge = Math.Min(supportEdge, GetLowestExtent(items[j]));
        }
        return supportEdge;
    }

    /// <summary>
    /// Gets the lowest Y extent of a music item in the native Y-up frame
    /// (staff-spaces above the staff middle, up-positive; lowest = smallest).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: stem.cc:461-468 calc_stem_end_position
    /// Accounts for note position and stem direction.
    /// LILYPOND-REF: lily/stem.cc Stem::is_normal_stem — a stem exists only for
    ///   duration-log >= 1, i.e. a half note or shorter. A whole note has none, so it
    ///   must not reserve one; 89aaa29f removed the same phantom stem from SkylineBuilder
    ///   and the renderer has always branched on it (SharedRenderer.Noteheads.cs).
    /// The head's own extent is its GLYPH INK (LILC, ±0.545), not the nominal ±0.5 box —
    ///   the same move 22120764 made for the skyline.
    /// </remarks>
    private static double GetLowestExtent(MusicItem item, bool? forcedStemUp = null)
    {
        switch (item)
        {
            case NoteItem note:
                // StaffPosition (half-spaces, positive = up) → Y-up staff-spaces above
                // the middle line: Y = StaffPosition * 0.5.
                // LILYPOND-REF: staff-symbol-referencer.cc:76-89 get_position
                double noteY = note.StaffPosition * 0.5;

                // If stem down, subtract stem length below the notehead (lower = smaller
                // Y-up). forcedStemUp (multi-voice) overrides the pitch-default direction.
                if (!(forcedStemUp ?? note.StemUp) && HasStem(note.BaseDuration))
                {
                    return noteY - EngravingDefaults.DefaultStemLength;
                }

                return noteY + HeadBBox(note.BaseDuration).Bottom;

            case ChordItem chord:
                // Find lowest note in chord (most negative StaffPosition = lowest on staff)
                int lowestPos = chord.Notes.Min(n => n.StaffPosition);
                double lowestNoteY = lowestPos * 0.5;

                // If stem down, subtract stem length from lowest note
                if (!(forcedStemUp ?? chord.StemUp) && HasStem(chord.BaseDuration))
                {
                    return lowestNoteY - EngravingDefaults.DefaultStemLength;
                }

                return lowestNoteY + HeadBBox(chord.BaseDuration).Bottom;

            case RestItem:
                // Rest is typically around middle of staff (1 ss below the middle line)
                return -1.0;

            default:
                return -StaffMiddle;
        }
    }

    /// <summary>
    /// Highest Y extent (most-above = largest Y-up, staff-spaces above the staff
    /// middle) of a music item — the mirror of <see cref="GetLowestExtent"/>,
    /// accounting for an up-stem.
    /// </summary>
    private static double GetHighestExtent(MusicItem item, bool? forcedStemUp = null)
    {
        switch (item)
        {
            case NoteItem note:
            {
                double noteY = note.StaffPosition * 0.5;
                // Stem up: the stem extends UP (larger Y-up) above the notehead.
                if ((forcedStemUp ?? note.StemUp) && HasStem(note.BaseDuration))
                    return noteY + EngravingDefaults.DefaultStemLength;
                return noteY + HeadBBox(note.BaseDuration).Top;
            }
            case ChordItem chord:
            {
                int highestPos = chord.Notes.Max(n => n.StaffPosition);
                double highestNoteY = highestPos * 0.5;
                if ((forcedStemUp ?? chord.StemUp) && HasStem(chord.BaseDuration))
                    return highestNoteY + EngravingDefaults.DefaultStemLength;
                return highestNoteY + HeadBBox(chord.BaseDuration).Top;
            }
            case RestItem:
                return 1.0; // 1 ss above the middle line

            default:
                return StaffMiddle; // staff top
        }
    }

    /// <summary>
    /// True when a note of this written duration carries a stem at all.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/stem.cc Stem::is_normal_stem — duration-log >= 1, i.e. a half
    ///   note or shorter. Note value 2 = half, so the test is <c>&gt;= 2</c>; identical to
    ///   the guard 89aaa29f put in SkylineBuilder and to SharedRenderer.Noteheads.cs.
    /// </remarks>
    private static bool HasStem(Semantics.Fraction baseDuration)
        => LayoutUtilities.GetNoteValueFromFraction(baseDuration) >= 2;

    /// <summary>The notehead's own glyph ink for this written duration.</summary>
    /// <remarks>
    /// LILYPOND-REF: lily/grob.cc:85-89 simple_vertical_skylines_from_extents with
    ///   lily/open-type-font.cc:288,389-407 — the head's extent is its LILC bbox
    ///   (±0.545), not a nominal half staff space. Reached through the SAME note-value
    ///   mapping SkylineBuilder uses, so the two paths cannot drift apart.
    /// </remarks>
    private static GlyphMetrics.BBox HeadBBox(Semantics.Fraction baseDuration)
        => GlyphMetrics.GetNoteheadBBox(
            LayoutUtilities.GetNoteValueFromFraction(baseDuration));
}
