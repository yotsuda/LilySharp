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
                // — and grace time, which collides in its OWN group's frame, not on the
                // main column grid. See CollectMultipleVoices for the measurement.
                if (item is RestItem || item.GraceTime)
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
    /// Collects voice columns from multiple voices, aligning by time position — measure by
    /// measure, in measure order (<see cref="CollectMeasure"/>): a column holds one measure's
    /// moment, so the whole staff's columns are its measures' columns laid end to end.
    /// </summary>
    private static ImmutableArray<VoiceColumn> CollectMultipleVoices(ImmutableArray<Voice> voices)
    {
        int measureCount = 0;
        foreach (var voice in voices)
            measureCount = Math.Max(measureCount, voice.Measures.Length);

        var columns = ImmutableArray.CreateBuilder<VoiceColumn>();
        for (int measureIndex = 0; measureIndex < measureCount; measureIndex++)
            columns.AddRange(CollectMeasure(voices, measureIndex));
        return columns.ToImmutable();
    }

    /// <summary>
    /// The columns of ONE measure across every voice, in time order — the unit the
    /// collision table solves a bar at a time (<see cref="Layout.VoiceCollisionTable"/>),
    /// and what <see cref="Collect(ImmutableArray{Voice})"/> concatenates for the staff.
    /// A voice shorter than the measure index contributes nothing.
    /// </summary>
    public static ImmutableArray<VoiceColumn> CollectMeasure(ImmutableArray<Voice> voices, int measureIndex)
    {
        // Build the measure's timeline: time within the measure → entries, in voice order.
        var timeline = new SortedDictionary<Fraction, List<VoiceEntry>>();

        for (int voiceIndex = 0; voiceIndex < voices.Length; voiceIndex++)
        {
            var voice = voices[voiceIndex];
            if (measureIndex >= voice.Measures.Length)
                continue;
            int voiceId = voiceIndex + 1;
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

                // Add non-rest items to timeline.
                // ⚠️ AND NOT GRACE TIME. A grace takes no measure time, so it lands on
                // the moment of the note it leads — putting a THIRD head into a column
                // that holds one note from each voice, and the collision solver then
                // shifts a head that is nowhere near it: MEASURED, in
                // `voice { c''4 c''4 c''2 } { g4 grace { d''16 } g4 g2 }` the UPPER
                // voice's second c'' moved 1.04 (a notehead) to the right, on account
                // of a grace in the lower one.
                // ⚠️ This is not a permanent answer, and HANDOFF §2 U8b is the open
                // ticket for the real one: LilyPond DOES collide two simultaneous
                // graces with each other (session 308 measured its two accidentals
                // stacked at 16.2208 / 17.0831), and Lily# still draws them on top of
                // each other. What the ticket needs is a column of grace time, beside
                // this one — not grace heads inside this one.
                if (item is not RestItem && !item.GraceTime)
                {
                    if (!timeline.TryGetValue(timePosition, out var entries))
                    {
                        entries = new List<VoiceEntry>();
                        timeline[timePosition] = entries;
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

        if (timeline.Count == 0)
            return ImmutableArray<VoiceColumn>.Empty;

        // Convert the timeline to voice columns
        var columns = ImmutableArray.CreateBuilder<VoiceColumn>(timeline.Count);
        foreach (var kvp in timeline)
            columns.Add(new VoiceColumn(kvp.Value.ToImmutableArray(), measureIndex));
        return columns.MoveToImmutable();
    }
}