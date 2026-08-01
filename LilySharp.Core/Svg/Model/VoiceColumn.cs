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

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Represents multiple voices on a single staff at a specific time point.
/// </summary>
public sealed record VoiceColumn
{
    /// <summary>The voices present at this time point.</summary>
    public ImmutableArray<VoiceEntry> Entries { get; }

    /// <summary>Measure index.</summary>
    public int MeasureIndex { get; }

    /// <summary>Creates a voice column from its entries at a measure index.</summary>
    public VoiceColumn(ImmutableArray<VoiceEntry> entries, int measureIndex)
    {
        Entries = entries;
        MeasureIndex = measureIndex;
    }
}

/// <summary>
/// A single voice's contribution to a voice column.
/// </summary>
public sealed record VoiceEntry
{
    /// <summary>The voice this entry belongs to (1-based).</summary>
    public int VoiceId { get; }

    /// <summary>The music item at this position.</summary>
    public MusicItem Item { get; }

    /// <summary>Item index within the measure for this voice.</summary>
    public int ItemIndex { get; }

    /// <summary>Forced stem direction (null = use voice default or auto).</summary>
    public bool? ForcedStemUp { get; }

    /// <summary>Creates a voice entry for a single item within a voice column.</summary>
    public VoiceEntry(int voiceId, MusicItem item, int itemIndex, bool? forcedStemUp = null)
    {
        VoiceId = voiceId;
        Item = item;
        ItemIndex = itemIndex;
        ForcedStemUp = forcedStemUp;
    }
}

/// <summary>
/// Voice direction settings for multi-voice layout.
/// </summary>
public static class VoiceDefaults
{
    /// <summary>
    /// Gets default stem direction for a voice number.
    /// Voice 1 = up, Voice 2 = down, etc.
    /// </summary>
    public static bool? GetDefaultStemUp(int voiceNumber) => voiceNumber switch
    {
        1 => true,   // First voice: stems up
        2 => false,  // Second voice: stems down
        3 => true,   // Third voice: stems up
        4 => false,  // Fourth voice: stems down
        _ => null    // Other voices: auto
    };

    /// <summary>
    /// Whether measure <paramref name="measureIndex"/> lies inside a
    /// <c>voice { } voice { }</c> span — the only measures where a voice's stem
    /// direction is forced.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/music-functions.scm:1042-1057 voicify-sublist / make-voice-props-set
    ///   — each <c>\\</c>-separated sublist is wrapped in its OWN Voice context
    ///   (<c>context-spec-music … 'Bottom "1"</c>) with <c>make-voice-props-set</c>
    ///   at its head, so the forcing lives and dies with the span. The music
    ///   before and after it belongs to the surrounding (implicit) Voice context
    ///   and keeps the pitch-derived direction.
    /// LILYPOND-REF: scm/music-functions.scm:666-674 make-voice-props-set —
    ///   direction = <c>(if (odd? n) -1 1)</c> on direction-polyphonic-grobs.
    ///
    /// Asking <c>Voices.Length &gt; 1</c> instead — a PART-wide question — pinned
    /// every bar of voice 1 stem-up as soon as the part had ONE <c>voice { }</c>
    /// anywhere in it, flipping beams a whole section away from the span
    /// (showcase/grammar-tour, 2026-08-01).
    ///
    /// The span is read back off the model rather than carried alongside it:
    /// voices 2..N are built as full-length tracks that are EMPTY outside their
    /// span (MeasureCollector.BuildExtraVoiceTracks), so a measure is inside a
    /// span exactly where one of them has items.
    /// ⚠️ DIVERGENCE from the LILYPOND-REF above, not an own invention: reading the
    /// span back off the model gets its reach at MEASURE granularity and only as far
    /// as its later voices go. A span whose later voices run out before voice 1 does
    /// (<c>voice { a1 b1 } voice { c1 }</c>) stops forcing where they stop, where
    /// LilyPond keeps \voiceOne to the end of the span — its Voice context lives as
    /// long as its own music. Carrying the span's measure range on the model, which
    /// MeasureCollector._parallelSpans already knows at collect time, is what closing
    /// that would take; nothing in the corpus reaches it today.
    /// </remarks>
    public static bool IsPolyphonicAt(ImmutableArray<Voice> voices, int measureIndex)
    {
        for (int vi = 1; vi < voices.Length; vi++)
            if (measureIndex < voices[vi].Measures.Length
                && voices[vi].Measures[measureIndex].Items.Length > 0)
                return true;
        return false;
    }

    /// <summary>
    /// The stem direction voice <paramref name="voiceIndex"/> (0-based) is forced
    /// to in measure <paramref name="measureIndex"/>, or null where nothing forces
    /// it — <see cref="GetDefaultStemUp"/> narrowed to <see cref="IsPolyphonicAt"/>.
    /// </summary>
    public static bool? GetDefaultStemUpAt(
        ImmutableArray<Voice> voices, int voiceIndex, int measureIndex)
        => IsPolyphonicAt(voices, measureIndex) ? GetDefaultStemUp(voiceIndex + 1) : null;
}