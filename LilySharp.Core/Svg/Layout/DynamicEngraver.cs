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
    // LILYPOND-REF: define-grobs.scm:1408 padding = 0.6
    private const double Padding = 0.6;

    // LILYPOND-REF: define-grobs.scm:1411 staff-padding = 0.1
    private const double StaffPadding = 0.1;

    // Staff geometry (5 lines = 4 staff spaces)
    private const double StaffBottom = 4.0;
    private const double StaffMiddle = EngravingDefaults.StaffMiddle;  // StaffBottom / 2

    // Text ascent above baseline for dynamic text (font-size 2.0, bold italic serif).
    // Approximate cap-height ratio ~0.6 × font-size.
    // LILYPOND-REF: define-grobs.scm:1450 Y-offset = (scale-by-font-size -0.6)
    private const double TextAscent = 1.2;

    // Text descent BELOW the baseline for dynamic glyphs (the f / p swashes reach
    // well under the baseline). Only matters for ABOVE placement, where the descender
    // is the edge that faces the staff/notes — a pure ascent mirror would let it
    // intrude. Measured from LilyPond 2.24.4: a forced-up dynamic's baseline sits
    // 1.34 ss above the staff top = (staff-padding 0.1 + padding 0.6 + descent 0.64).
    private const double TextDescent = 0.64;

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
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        ImmutableArray<Voice> voices = default,
        Dictionary<int, ImmutableArray<Voice>>? voicesByStaff = null,
        Dictionary<int, ImmutableArray<Measure>>? measuresByStaff = null,
        Func<int, int, double>? staffYAt = null)
    {
        if (dynamics.IsDefaultOrEmpty)
            return ImmutableArray<DynamicLayout>.Empty;

        var layouts = ImmutableArray.CreateBuilder<DynamicLayout>(dynamics.Length);

        // LILYPOND-REF: side-position-interface.cc:323-337 staff padding
        // Native Y-up (staff-spaces above the staff middle, up-positive): the
        // baseline sits below the staff bottom (staff bottom is StaffMiddle ss
        // BELOW the middle line, i.e. -StaffMiddle) by staff-padding + padding, and
        // one ascent further down so the ink top (baseline + TextAscent) clears the
        // staff bottom by staff-padding + padding.
        double baseYUp = -StaffMiddle - StaffPadding - Padding - TextAscent;
        // ABOVE baseline: the glyph's DESCENDER (the edge facing the staff) must clear
        // the staff top (+StaffMiddle) by staff-padding + padding, and the baseline
        // sits one descent further up. Matches LilyPond's forced-up dynamic (baseline
        // 1.34 ss above the staff top).
        double aboveBaseYUp = StaffMiddle + StaffPadding + Padding + TextDescent;

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

            // Calculate Y position with collision avoidance against EVERY voice's
            // note column (a lower voice's down-stem must not be overlapped by a
            // dynamic positioned from the upper voice's stem-up note).
            // LILYPOND-REF: side-position-interface.cc:266-320 skyline-based positioning
            double y = dynamic.IsAbove
                ? CalculateYPositionAboveVoices(
                    dynVoices, dynamic.MeasureIndex, dynamic.ItemIndex, aboveBaseYUp)
                : CalculateYPositionAcrossVoices(
                    dynVoices, dynamic.MeasureIndex, dynamic.ItemIndex, baseYUp);

            // A wide (or CJK, full-em) label centred on this note also covers its
            // neighbours' columns; clear those noteheads too so the label never
            // overprints an adjacent lower note. (Below-staff placement; the
            // forced-above path is a follow-up.)
            if (!dynamic.IsAbove)
                y = ClearNeighbors(dynMeasures, dynamic.MeasureIndex, measureLayout,
                    x, LabelHalfWidth(dynamic.Text, dynamic.IsExpressiveText), y);

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
    /// Baseline Y in the native Y-up frame (staff-spaces above the staff middle,
    /// up-positive; a below-staff dynamic is negative) the dynamic at a given note
    /// column occupies, BEFORE same-column stacking. Exposed so the inter-staff
    /// skyline can widen the gap by the dynamic's downward reach (otherwise a low
    /// lower-voice's dynamic overlaps the staff below).
    /// </summary>
    internal static double ColumnBaselineY(
        ImmutableArray<Voice> voices, int measureIndex, int itemIndex)
    {
        var vs = voices.IsDefaultOrEmpty ? ImmutableArray<Voice>.Empty : voices;
        double baseYUp = -StaffMiddle - StaffPadding - Padding - TextAscent;
        return vs.IsEmpty
            ? baseYUp
            : CalculateYPositionAcrossVoices(vs, measureIndex, itemIndex, baseYUp);
    }

    /// <summary>
    /// ABOVE-staff mirror of <see cref="ColumnBaselineY"/>: the baseline Y a forced-above
    /// dynamic occupies in the native Y-up frame (positive, above the staff middle),
    /// BEFORE same-column stacking. Exposed so the inter-staff skyline widens the gap
    /// to the staff ABOVE by the dynamic's upward reach.
    /// </summary>
    internal static double ColumnAboveBaselineY(
        ImmutableArray<Voice> voices, int measureIndex, int itemIndex)
    {
        var vs = voices.IsDefaultOrEmpty ? ImmutableArray<Voice>.Empty : voices;
        double aboveBaseYUp = StaffMiddle + StaffPadding + Padding + TextDescent;
        return vs.IsEmpty
            ? aboveBaseYUp
            : CalculateYPositionAboveVoices(vs, measureIndex, itemIndex, aboveBaseYUp);
    }

    /// <summary>Upward ink reach of a dynamic above its baseline (text ascends up).</summary>
    internal const double DynamicAboveAscent = TextAscent;

    /// <summary>
    /// Dynamic Y that clears the deepest note/stem of ANY voice at the column —
    /// so in a &lt;&lt; \\ &gt;&gt; the dynamic sits below the lower voice's
    /// down-stem instead of through it.
    /// </summary>
    private static double CalculateYPositionAcrossVoices(
        ImmutableArray<Voice> voices, int measureIndex, int itemIndex, double baseY)
    {
        bool multiVoice = voices.Length > 1;
        double y = baseY;
        for (int vi = 0; vi < voices.Length; vi++)
        {
            var voice = voices[vi];
            if (measureIndex >= voice.Measures.Length)
                continue;
            var items = voice.Measures[measureIndex].Items;
            if (itemIndex >= items.Length)
                continue;
            // In a multi-voice staff the stems are force-flipped (voice 1 up,
            // voice 2 down) regardless of the note's pitch-default StemUp, so a
            // low note in the lower voice still has a long DOWN stem to clear.
            bool? forcedStemUp = multiVoice ? VoiceDefaults.GetDefaultStemUp(vi + 1) : null;
            double lowest = GetLowestExtent(items[itemIndex], forcedStemUp);
            // Native Y-up: push the baseline BELOW (smaller Y-up than) the note's
            // lowest extent, clearing it by padding + ascent — the min, not the max.
            y = Math.Min(y, lowest - Padding - TextAscent);
        }
        return y;
    }

    // The dynamic/expressive label is drawn at FontSize*0.5 = 2.0 with
    // TextAnchor.Middle (see DrawDynamics), so it extends this far to each side of
    // the note. SerifTextMetrics counts a CJK glyph as a full em, so a "これ" label
    // reports its true (wide) extent.
    private const double DynamicFontSize = 2.0;

    private static double LabelHalfWidth(string text, bool expressive)
    {
        double w = expressive
            ? LilySharp.Core.Rendering.SerifTextMetrics.Measure(text, DynamicFontSize)
            : LilySharp.Core.Rendering.SerifTextMetrics.MeasureBold(text, DynamicFontSize);
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
    private static double ClearNeighbors(
        ImmutableArray<Measure> measures, int measureIndex,
        MeasureLayout measureLayout, double labelX, double halfWidth, double y)
    {
        if (measureIndex >= measures.Length)
            return y;
        var items = measures[measureIndex].Items;
        for (int j = 0; j < items.Length; j++)
        {
            double itemX = measureLayout.X + LayoutUtilities.GetItemXOffset(
                measures, measureIndex, j, measureLayout);
            // The note's head lies (even partly) under the label's horizontal span.
            if (Math.Abs(itemX - labelX) > halfWidth + EngravingDefaults.NoteheadHalfWidth)
                continue;
            // Native Y-up: push below the neighbour's lowest extent (the min).
            y = Math.Min(y, GetLowestExtent(items[j]) - Padding - TextAscent);
        }
        return y;
    }

    /// <summary>
    /// Gets the lowest Y extent of a music item in the native Y-up frame
    /// (staff-spaces above the staff middle, up-positive; lowest = smallest).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: stem.cc:461-468 calc_stem_end_position
    /// Accounts for note position and stem direction.
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
                if (!(forcedStemUp ?? note.StemUp))
                {
                    return noteY - EngravingDefaults.DefaultStemLength;
                }

                // Half a notehead height below center
                return noteY - EngravingDefaults.NoteheadHalfHeight;

            case ChordItem chord:
                // Find lowest note in chord (most negative StaffPosition = lowest on staff)
                int lowestPos = chord.Notes.Min(n => n.StaffPosition);
                double lowestNoteY = lowestPos * 0.5;

                // If stem down, subtract stem length from lowest note
                if (!(forcedStemUp ?? chord.StemUp))
                {
                    return lowestNoteY - EngravingDefaults.DefaultStemLength;
                }

                return lowestNoteY - EngravingDefaults.NoteheadHalfHeight;

            case RestItem:
                // Rest is typically around middle of staff (1 ss below the middle line)
                return -1.0;

            default:
                return -StaffMiddle;
        }
    }

    /// <summary>
    /// ABOVE-staff mirror of <see cref="CalculateYPositionAcrossVoices"/>: the dynamic Y
    /// that clears the HIGHEST note/stem of any voice at the column, so a forced-above
    /// dynamic sits above the up-stems rather than through them. Returns the most-above
    /// (smallest) Y.
    /// </summary>
    private static double CalculateYPositionAboveVoices(
        ImmutableArray<Voice> voices, int measureIndex, int itemIndex, double aboveBaseY)
    {
        bool multiVoice = voices.Length > 1;
        double y = aboveBaseY;
        for (int vi = 0; vi < voices.Length; vi++)
        {
            var voice = voices[vi];
            if (measureIndex >= voice.Measures.Length)
                continue;
            var items = voice.Measures[measureIndex].Items;
            if (itemIndex >= items.Length)
                continue;
            bool? forcedStemUp = multiVoice ? VoiceDefaults.GetDefaultStemUp(vi + 1) : null;
            double highest = GetHighestExtent(items[itemIndex], forcedStemUp);
            // Native Y-up: push the baseline ABOVE (larger Y-up than) the note's
            // highest extent. The glyph descender (baseline − descent) is what faces
            // the note, so the baseline sits a further TextDescent above it — the max.
            y = Math.Max(y, highest + Padding + TextDescent);
        }
        return y;
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
                if (forcedStemUp ?? note.StemUp)
                    return noteY + EngravingDefaults.DefaultStemLength;
                return noteY + EngravingDefaults.NoteheadHalfHeight;
            }
            case ChordItem chord:
            {
                int highestPos = chord.Notes.Max(n => n.StaffPosition);
                double highestNoteY = highestPos * 0.5;
                if (forcedStemUp ?? chord.StemUp)
                    return highestNoteY + EngravingDefaults.DefaultStemLength;
                return highestNoteY + EngravingDefaults.NoteheadHalfHeight;
            }
            case RestItem:
                return 1.0; // 1 ss above the middle line

            default:
                return StaffMiddle; // staff top
        }
    }
}
