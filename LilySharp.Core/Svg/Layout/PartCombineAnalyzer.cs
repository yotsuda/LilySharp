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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Part combination state at a given time point.
/// LILYPOND-REF: scm/part-combiner.scm - determine-split-list
/// </summary>
public enum PartCombineState
{
    /// <summary>Both voices playing different notes (separate rendering).</summary>
    Apart,

    /// <summary>Both voices playing identical notes (render once, "a2" marking).</summary>
    Unison,

    /// <summary>Only voice 1 has notes ("Solo" marking).</summary>
    Solo1,

    /// <summary>Only voice 2 has notes ("Solo II" marking).</summary>
    Solo2,

    /// <summary>Both voices resting.</summary>
    Silence
}

/// <summary>
/// Part combination state at a specific position in the score.
/// </summary>
/// <param name="State">The combination state</param>
/// <param name="MeasureIndex">Measure where this state begins</param>
/// <param name="ItemIndex">Item index within the measure</param>
public sealed record PartCombineItem(
    PartCombineState State,
    int MeasureIndex,
    int ItemIndex);

/// <summary>
/// Layout for a part combination text annotation ("a2", "Solo", "Solo II").
/// </summary>
/// <param name="Text">Display text</param>
/// <param name="X">X position in staff spaces</param>
/// <param name="YUp">Y in the Y-up frame (frame B): staff-spaces ABOVE the system
/// top, up-positive. The renderer reflects it to device against the system top.</param>
/// <param name="MeasureIndex">Measure index</param>
public sealed record PartCombineLayout(
    string Text,
    double X,
    double YUp,
    int MeasureIndex);

/// <summary>
/// Analyzes two voices for part combination: unison, solo, apart detection.
/// LILYPOND-REF: scm/part-combiner.scm - determine-split-list, try-solo
/// </summary>
internal static class PartCombineAnalyzer
{
    /// <summary>
    /// Text displayed for "a due" (unison) passages.
    /// LILYPOND-REF: define-grobs.scm CombineTextScript.aDueText
    /// </summary>
    public const string ADueText = "a2";

    /// <summary>
    /// Text displayed for voice 1 solo passages.
    /// </summary>
    public const string Solo1Text = "Solo";

    /// <summary>
    /// Text displayed for voice 2 solo passages.
    /// </summary>
    public const string Solo2Text = "Solo II";

    /// <summary>
    /// Analyzes two voices and determines the combination state at each time position.
    /// </summary>
    /// <param name="voice1">First voice (stems up by default)</param>
    /// <param name="voice2">Second voice (stems down by default)</param>
    /// <param name="timeSignature">Time signature for time position calculation</param>
    /// <returns>Array of state changes (only emitted when state changes)</returns>
    public static ImmutableArray<PartCombineItem> Analyze(
        Voice voice1,
        Voice voice2,
        TimeSignature timeSignature)
    {
        var items = ImmutableArray.CreateBuilder<PartCombineItem>();
        var prevState = (PartCombineState?)null;

        int maxMeasures = Math.Max(voice1.Measures.Length, voice2.Measures.Length);

        for (int measureIndex = 0; measureIndex < maxMeasures; measureIndex++)
        {
            var m1 = measureIndex < voice1.Measures.Length ? voice1.Measures[measureIndex] : null;
            var m2 = measureIndex < voice2.Measures.Length ? voice2.Measures[measureIndex] : null;

            // Walk through time positions within the measure
            var timePos = Fraction.Zero;
            int idx1 = 0, idx2 = 0;

            while (idx1 < (m1?.Items.Length ?? 0) || idx2 < (m2?.Items.Length ?? 0))
            {
                var item1 = (m1 != null && idx1 < m1.Items.Length) ? m1.Items[idx1] : null;
                var item2 = (m2 != null && idx2 < m2.Items.Length) ? m2.Items[idx2] : null;

                var state = ClassifyTimePoint(item1, item2);
                int itemIndex = Math.Min(idx1, idx2);

                // Only emit state changes
                if (state != prevState)
                {
                    items.Add(new PartCombineItem(state, measureIndex, itemIndex));
                    prevState = state;
                }

                // Advance both indices
                if (item1 != null) idx1++;
                if (item2 != null) idx2++;
                if (item1 == null && item2 == null) break;
            }
        }

        return items.ToImmutable();
    }

    /// <summary>
    /// Classifies a time point based on what both voices have.
    /// LILYPOND-REF: scm/part-combiner.scm - analyse-time-step
    /// </summary>
    private static PartCombineState ClassifyTimePoint(MusicItem? item1, MusicItem? item2)
    {
        bool hasNote1 = item1 is NoteItem or ChordItem;
        bool hasNote2 = item2 is NoteItem or ChordItem;
        bool isRest1 = item1 is RestItem || item1 == null;
        bool isRest2 = item2 is RestItem || item2 == null;

        // Both resting
        if (isRest1 && isRest2)
            return PartCombineState.Silence;

        // Solo 1: voice 1 has notes, voice 2 resting
        if (hasNote1 && isRest2)
            return PartCombineState.Solo1;

        // Solo 2: voice 2 has notes, voice 1 resting
        if (isRest1 && hasNote2)
            return PartCombineState.Solo2;

        // Both have notes — check for unison
        if (hasNote1 && hasNote2 && AreUnison(item1!, item2!))
            return PartCombineState.Unison;

        // Both have notes but different — apart
        return PartCombineState.Apart;
    }

    /// <summary>
    /// Checks if two music items are in unison (same pitches and duration).
    /// LILYPOND-REF: scm/part-combiner.scm - unison check (equal? pitches and durations)
    /// </summary>
    private static bool AreUnison(MusicItem item1, MusicItem item2)
    {
        if (item1 is NoteItem n1 && item2 is NoteItem n2)
        {
            return n1.StaffPosition == n2.StaffPosition &&
                   n1.BaseDuration == n2.BaseDuration &&
                   n1.Dots == n2.Dots;
        }

        if (item1 is ChordItem c1 && item2 is ChordItem c2)
        {
            if (c1.Notes.Length != c2.Notes.Length)
                return false;
            if (c1.BaseDuration != c2.BaseDuration || c1.Dots != c2.Dots)
                return false;

            // Compare sorted staff positions
            var positions1 = c1.Notes.Select(n => n.StaffPosition).OrderBy(p => p).ToArray();
            var positions2 = c2.Notes.Select(n => n.StaffPosition).OrderBy(p => p).ToArray();
            return positions1.SequenceEqual(positions2);
        }

        return false;
    }

    /// <summary>
    /// Calculates layout positions for part combination text annotations.
    /// </summary>
    /// <param name="combineItems">State changes from Analyze()</param>
    /// <param name="measureLayouts">Measure layouts for X position lookup</param>
    /// <param name="measures">Measures for the combined part, for timing/context lookup.</param>
    /// <returns>Layout items for rendering</returns>
    public static ImmutableArray<PartCombineLayout> Calculate(
        ImmutableArray<PartCombineItem> combineItems,
        ImmutableArray<MeasureLayout> measureLayouts,
        ImmutableArray<Measure> measures = default)
    {
        if (combineItems.IsDefaultOrEmpty)
            return ImmutableArray<PartCombineLayout>.Empty;

        var layouts = ImmutableArray.CreateBuilder<PartCombineLayout>();
        // Y-up from the system top: 1.5 staff-spaces above it (the old device
        // offset -1.5 negated). The system top is resolved at draw time.
        const double aboveStaffYUp = 1.5;

        foreach (var item in combineItems)
        {
            string? text = item.State switch
            {
                PartCombineState.Unison => ADueText,
                PartCombineState.Solo1 => Solo1Text,
                PartCombineState.Solo2 => Solo2Text,
                _ => null // No text for Apart or Silence
            };

            if (text == null)
                continue;

            // Find X position from measure layout (Items/Columns-aware)
            double x = 0;
            if (item.MeasureIndex < measureLayouts.Length)
            {
                var ml = measureLayouts[item.MeasureIndex];
                x = ml.X + LayoutUtilities.GetItemXOffset(
                    measures, item.MeasureIndex, item.ItemIndex, ml);
            }

            layouts.Add(new PartCombineLayout(text, x, aboveStaffYUp, item.MeasureIndex));
        }

        return layouts.ToImmutable();
    }
}
