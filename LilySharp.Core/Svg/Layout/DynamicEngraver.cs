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
/// LILYPOND-REF: define-grobs.scm:1298-1327 DynamicText grob
/// LILYPOND-REF: define-grobs.scm:1270-1297 DynamicLineSpanner grob
/// </remarks>
public readonly record struct DynamicLayout(
    int MeasureIndex,       // Measure containing this dynamic
    int ItemIndex,          // Item index within measure (for X alignment)
    double X,               // Absolute X position (staff spaces from score start)
    double Y,               // Y position (staff spaces from staff top, positive = down)
    string Text,            // Dynamic text ("p", "ff", etc.)
    int SourcePosition      // For click-to-source mapping
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
public static class DynamicEngraver
{
    // LILYPOND-REF: define-grobs.scm:1274 direction = DOWN
    private const int Direction = 1;  // DOWN = 1 (positive Y = down in our coordinate system)

    // LILYPOND-REF: define-grobs.scm:1277 padding = 0.6
    private const double Padding = 0.6;

    // LILYPOND-REF: define-grobs.scm:1280 staff-padding = 0.1
    private const double StaffPadding = 0.1;

    // Staff geometry (5 lines = 4 staff spaces)
    private const double StaffBottom = 4.0;
    private const double StaffMiddle = EngravingDefaults.StaffMiddle;  // StaffBottom / 2

    // Text ascent above baseline for dynamic text (font-size 2.0, bold italic serif).
    // Approximate cap-height ratio ~0.6 × font-size.
    // LILYPOND-REF: define-grobs.scm:1317 Y-offset = (scale-by-font-size -0.6)
    private const double TextAscent = 1.2;

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
        Dictionary<int, double>? staffYByIndex = null)
    {
        if (dynamics.IsDefaultOrEmpty)
            return ImmutableArray<DynamicLayout>.Empty;

        var layouts = ImmutableArray.CreateBuilder<DynamicLayout>(dynamics.Length);

        // LILYPOND-REF: side-position-interface.cc:323-337 staff padding
        // Base Y position: dynamic text baseline must be low enough that the
        // visual top of the text (baseline - TextAscent) clears the staff bottom.
        double baseY = StaffBottom + StaffPadding + Padding + TextAscent;

        var fallbackVoices = voices.IsDefaultOrEmpty ? ImmutableArray.Create(score.Voice) : voices;

        // Two voices can carry a dynamic on the SAME note column (e.g. an upper
        // voice @f and a lower voice @p in a << \\ >>). They share (measure,
        // item) and would draw on top of each other; stack the 2nd+ downward so
        // both stay legible. Keyed by staff too: same-column dynamics on
        // DIFFERENT staves are independent and must not stack onto each other.
        var stackAt = new Dictionary<(int, int, int), int>();

        foreach (var dynamic in dynamics)
        {
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
            var dynMeasures = measuresByStaff != null
                && measuresByStaff.TryGetValue(dynamic.StaffIndex, out var mm) ? mm : score.Voice.Measures;
            double staffOffset = staffYByIndex != null
                && staffYByIndex.TryGetValue(dynamic.StaffIndex, out var so) ? so : 0;

            // Calculate X position (centered on the note)
            // LILYPOND-REF: define-grobs.scm:1311 self-alignment-X = CENTER
            double x = measureLayout.X + LayoutUtilities.GetItemXOffset(
                dynMeasures, dynamic.MeasureIndex, dynamic.ItemIndex, measureLayout);

            // Calculate Y position with collision avoidance against EVERY voice's
            // note column (a lower voice's down-stem must not be overlapped by a
            // dynamic positioned from the upper voice's stem-up note).
            // LILYPOND-REF: side-position-interface.cc:266-320 skyline-based positioning
            double y = CalculateYPositionAcrossVoices(
                dynVoices, dynamic.MeasureIndex, dynamic.ItemIndex, baseY);

            var key = (dynamic.MeasureIndex, dynamic.ItemIndex, dynamic.StaffIndex);
            int depth = stackAt.GetValueOrDefault(key, 0);
            stackAt[key] = depth + 1;
            y += depth * StackStep;

            // Bake the staff's within-system offset so the page-level renderer's
            // system-top + Y lands under THIS staff. (Offsets are uniform across
            // systems, so a single value is correct everywhere this measure falls.)
            y += staffOffset;

            layouts.Add(new DynamicLayout(
                dynamic.MeasureIndex,
                dynamic.ItemIndex,
                x,
                y,
                dynamic.Text,
                dynamic.SourcePosition
            ));
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// Baseline Y (staff spaces from staff top, positive = down) the dynamic at a
    /// given note column occupies, BEFORE same-column stacking. Exposed so the
    /// inter-staff skyline can widen the gap by the dynamic's downward reach
    /// (otherwise a low lower-voice's dynamic overlaps the staff below).
    /// </summary>
    internal static double ColumnBaselineY(
        ImmutableArray<Voice> voices, int measureIndex, int itemIndex)
    {
        var vs = voices.IsDefaultOrEmpty ? ImmutableArray<Voice>.Empty : voices;
        double baseY = StaffBottom + StaffPadding + Padding + TextAscent;
        if (vs.IsEmpty)
            return baseY;
        return CalculateYPositionAcrossVoices(vs, measureIndex, itemIndex, baseY);
    }

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
            y = Math.Max(y, lowest + Padding + TextAscent);
        }
        return y;
    }

    /// <summary>
    /// Gets the lowest Y extent of a music item (in staff spaces from top).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: stem.cc:876-920 calc_stem_end_position
    /// Accounts for note position and stem direction.
    /// </remarks>
    private static double GetLowestExtent(MusicItem item, bool? forcedStemUp = null)
    {
        switch (item)
        {
            case NoteItem note:
                // Convert StaffPosition to Y in staff spaces from top.
                // StaffPosition convention: 0 = middle line, positive = up, negative = down.
                // Canonical formula: Y = StaffMiddle - StaffPosition * 0.5
                // LILYPOND-REF: staff-symbol-referencer.cc:76-89 get_position
                double noteY = StaffMiddle - note.StaffPosition * 0.5;

                // If stem down, add stem length below the notehead. forcedStemUp
                // (multi-voice) overrides the note's pitch-default direction.
                if (!(forcedStemUp ?? note.StemUp))
                {
                    return noteY + EngravingDefaults.DefaultStemLength;
                }

                // Half a notehead height below center
                return noteY + EngravingDefaults.NoteheadHalfHeight;

            case ChordItem chord:
                // Find lowest note in chord (most negative StaffPosition = lowest on staff)
                int lowestPos = chord.Notes.Min(n => n.StaffPosition);
                double lowestNoteY = StaffMiddle - lowestPos * 0.5;

                // If stem down, add stem length from lowest note
                if (!(forcedStemUp ?? chord.StemUp))
                {
                    return lowestNoteY + EngravingDefaults.DefaultStemLength;
                }

                return lowestNoteY + EngravingDefaults.NoteheadHalfHeight;

            case RestItem:
                // Rest is typically around middle of staff
                return StaffMiddle + 1.0;

            default:
                return StaffBottom;
        }
    }
}
