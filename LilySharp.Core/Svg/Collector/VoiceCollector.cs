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

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Collects voice columns from a score, aligning multiple voices at common time positions.
/// </summary>
/// <remarks>
/// VoiceCollector processes one or more voices and produces VoiceColumn objects
/// that represent all voices at each time point. This enables proper collision
/// detection and stem direction calculation for multi-voice layouts.
///
/// For single-voice scores, each item produces a VoiceColumn with one entry.
/// For multi-voice scores, items at the same time position are grouped together.
/// </remarks>
internal sealed class VoiceCollector
{
    /// <summary>
    /// Collects voice columns from a single-voice score.
    /// </summary>
    public ImmutableArray<VoiceColumn> Collect(Score score)
    {
        return Collect(score.Voices);
    }

    /// <summary>
    /// Collects voice columns from multiple voices.
    /// </summary>
    public ImmutableArray<VoiceColumn> Collect(ImmutableArray<Voice> voices)
    {
        if (voices.Length == 0)
            return ImmutableArray<VoiceColumn>.Empty;

        if (voices.Length == 1)
            return CollectSingleVoice(voices[0]);

        return CollectMultipleVoices(voices);
    }

    /// <summary>
    /// Optimized path for single-voice scores.
    /// </summary>
    private ImmutableArray<VoiceColumn> CollectSingleVoice(Voice voice)
    {
        var columns = ImmutableArray.CreateBuilder<VoiceColumn>();

        for (int measureIndex = 0; measureIndex < voice.Measures.Length; measureIndex++)
        {
            var measure = voice.Measures[measureIndex];

            for (int itemIndex = 0; itemIndex < measure.Items.Length; itemIndex++)
            {
                var item = measure.Items[itemIndex];

                // Skip rests for voice column generation (they don't collide)
                if (item is RestItem)
                    continue;

                var entry = new VoiceEntry(
                    voiceId: 1,
                    item: item,
                    itemIndex: itemIndex,
                    forcedStemUp: null);

                var column = new VoiceColumn(
                    ImmutableArray.Create(entry),
                    measureIndex);

                columns.Add(column);
            }
        }

        return columns.ToImmutable();
    }

    /// <summary>
    /// Collects voice columns from multiple voices, aligning by time position.
    /// </summary>
    private ImmutableArray<VoiceColumn> CollectMultipleVoices(ImmutableArray<Voice> voices)
    {
        // Build a timeline: map from (measureIndex, timeWithinMeasure) to entries
        var timeline = new SortedDictionary<TimelineKey, List<VoiceEntry>>();

        for (int voiceIndex = 0; voiceIndex < voices.Length; voiceIndex++)
        {
            var voice = voices[voiceIndex];
            int voiceId = voiceIndex + 1;

            for (int measureIndex = 0; measureIndex < voice.Measures.Length; measureIndex++)
            {
                var measure = voice.Measures[measureIndex];
                var timePosition = Fraction.Zero;

                // Forced stem direction for this voice, where the voice { } span
                // actually reaches.
                // LILYPOND-REF: scm/music-functions.scm:1042-1057 voicify-sublist / make-voice-props-set
                bool? defaultStemUp = VoiceDefaults.GetDefaultStemUpAt(
                    voices, voiceIndex, measureIndex);

                for (int itemIndex = 0; itemIndex < measure.Items.Length; itemIndex++)
                {
                    var item = measure.Items[itemIndex];

                    // Add non-rest items to timeline
                    if (item is not RestItem)
                    {
                        var key = new TimelineKey(measureIndex, timePosition);

                        if (!timeline.TryGetValue(key, out var entries))
                        {
                            entries = new List<VoiceEntry>();
                            timeline[key] = entries;
                        }

                        // Writer's @stemUp/@stemDown outranks the voice default
                        // (must match ResolveVoiceStemDirections, which skips
                        // these when baking).
                        bool? writerAsk = item switch
                        {
                            NoteItem n => n.ForcedStemUp,
                            ChordItem c => c.ForcedStemUp,
                            _ => null,
                        };
                        entries.Add(new VoiceEntry(voiceId, item, itemIndex, writerAsk ?? defaultStemUp));
                    }

                    // Advance time position
                    timePosition = timePosition + item.Duration;
                }
            }
        }

        // Convert timeline to voice columns
        var columns = ImmutableArray.CreateBuilder<VoiceColumn>();

        foreach (var kvp in timeline)
        {
            var column = new VoiceColumn(
                kvp.Value.ToImmutableArray(),
                kvp.Key.MeasureIndex);

            columns.Add(column);
        }

        return columns.ToImmutable();
    }

    /// <summary>
    /// Key for timeline indexing, ordered by measure, then time within measure.
    /// </summary>
    private readonly struct TimelineKey : IComparable<TimelineKey>
    {
        public int MeasureIndex { get; }
        public Fraction TimePosition { get; }

        public TimelineKey(int measureIndex, Fraction timePosition)
        {
            MeasureIndex = measureIndex;
            TimePosition = timePosition;
        }

        public int CompareTo(TimelineKey other)
        {
            // First compare by measure
            int measureCompare = MeasureIndex.CompareTo(other.MeasureIndex);
            if (measureCompare != 0)
                return measureCompare;

            // Then by time within measure
            return TimePosition.CompareTo(other.TimePosition);
        }

        public override bool Equals(object? obj) =>
            obj is TimelineKey other && CompareTo(other) == 0;

        public override int GetHashCode() =>
            HashCode.Combine(MeasureIndex, TimePosition.GetHashCode());
    }
}