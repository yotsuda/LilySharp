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

using System.Collections.Generic;
using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Post-pass over already-collected voices that transposes the DISPLAYED staff
/// position of notes inside an ottava span by whole octaves — an 8va draws an
/// octave lower (fewer ledger lines) with the bracket telling the player to
/// sound an octave higher, matching real notation software and LilyPond.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/ottava-engraver.cc — Ottava_spanner_engraver sets the
/// Staff.middleCOffset context property while a spanner is active, which shifts
/// every note's displayed vertical position by the ottava's octaves. The
/// SOUNDING pitch is unchanged.
///
/// Only <see cref="NoteItem.StaffPosition"/> / <see cref="ChordNoteInfo.StaffPosition"/>
/// (the render position) is touched; <c>Midi</c> and both exporters read the
/// written pitch (MidiExporter/MusicXmlExporter walk the syntax tree), so sound
/// and exports are untouched. Stem direction and beams derive from StaffPosition
/// at layout time (after this pass), so they follow the shift automatically.
///
/// The transposition is measure-granular, matching the bracket geometry
/// (<see cref="Layout.OttavaBracketEngraver"/> spans whole measures). Authoring
/// an ottava with <c>@ottava</c>/<c>@loco</c> on measure boundaries — as real
/// scores do — lines the two up exactly. A grace note that lives outside the
/// measure item list is not shifted (rare; documented limitation).
/// </remarks>
internal static class OttavaTransposer
{
    /// <summary>Diatonic staff positions per octave. One octave = 7 steps; the
    /// device conversion halves this (7 positions = 3.5 staff spaces).</summary>
    private const int StepsPerOctave = 7;

    /// <summary>Display offset in staff positions: 8va shows an octave LOWER
    /// (negative = up the page is negative, lower pitch = larger device Y…);
    /// staff position decreases by 7 for 8va, increases for 8vb.</summary>
    private static int OffsetFor(OttavaType type) => type switch
    {
        OttavaType.Ottava8va => -StepsPerOctave,
        OttavaType.Ottava8vb => +StepsPerOctave,
        OttavaType.Quindicesima15ma => -2 * StepsPerOctave,
        OttavaType.Quindicesima15mb => +2 * StepsPerOctave,
        _ => 0
    };

    /// <summary>
    /// Shifts the displayed position of every note in the measures the given
    /// brackets cover. Pass only the brackets for THIS voice's staff. A voice
    /// with no covering bracket is returned unchanged (same reference), so
    /// non-ottava scores are byte-for-byte identical.
    /// </summary>
    public static Voice Transpose(Voice voice, IReadOnlyList<OttavaBracketItem> brackets)
    {
        if (brackets.Count == 0)
            return voice;

        // Per-measure display offset. Spans on one staff never overlap, so a
        // plain dictionary (last writer wins) is exact.
        var measureOffset = new Dictionary<int, int>();
        foreach (var b in brackets)
        {
            int off = OffsetFor(b.Type);
            if (off == 0) continue;
            for (int mi = b.StartMeasureIndex; mi <= b.EndMeasureIndex; mi++)
                measureOffset[mi] = off;
        }
        if (measureOffset.Count == 0)
            return voice;

        var rebuilt = ImmutableArray.CreateBuilder<Measure>(voice.Measures.Length);
        bool changed = false;
        for (int mi = 0; mi < voice.Measures.Length; mi++)
        {
            var measure = voice.Measures[mi];
            if (!measureOffset.TryGetValue(mi, out int off))
            {
                rebuilt.Add(measure);
                continue;
            }

            var items = measure.Items.ToArray();
            for (int ii = 0; ii < items.Length; ii++)
                items[ii] = Shift(items[ii], off);
            rebuilt.Add(measure with { Items = ImmutableArray.Create(items) });
            changed = true;
        }
        return changed ? voice with { Measures = rebuilt.MoveToImmutable() } : voice;
    }

    private static MusicItem Shift(MusicItem item, int off) => item switch
    {
        NoteItem n => n with { StaffPosition = n.StaffPosition + off },
        ChordItem c => c with { Notes = ImmutableArray.CreateRange(c.Notes, cn => cn with { StaffPosition = cn.StaffPosition + off }) },
        _ => item
    };
}
