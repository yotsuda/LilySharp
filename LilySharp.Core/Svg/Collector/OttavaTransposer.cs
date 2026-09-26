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
using LilySharp.Core.Semantics;
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
/// The transposition is MOMENT-granular at the span's two ends: in the start measure only
/// the notes from <see cref="OttavaBracketItem.StartMoment"/> on move, in the end measure
/// only those before <see cref="OttavaBracketItem.EndMoment"/> (the closing note is back at
/// written pitch), in every voice of the staff alike, as Staff.middleCOffset is a Staff
/// property. Until 2026-09-26 it was measure-granular, so an ottava that began or ended
/// inside a bar moved that whole bar or none of it (Lab probes/complex-lys/07, bar 8: an
/// `@!ottava` on a bar's last note left the bar's first six notes at written pitch).
/// A grace note that lives outside the measure item list is not shifted (rare; documented
/// limitation).
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

        // Per-measure display offset and the moment window it holds over, [from, until).
        // Spans on one staff never overlap in time; two may share a measure (one ends inside
        // it, the next begins), so a measure keeps a short list of windows.
        var measureWindows = new Dictionary<int, List<(int Off, Fraction? From, Fraction? Until)>>();
        foreach (var b in brackets)
        {
            int off = OffsetFor(b.Type);
            if (off == 0) continue;
            for (int mi = b.StartMeasureIndex; mi <= b.EndMeasureIndex; mi++)
            {
                var from = mi == b.StartMeasureIndex ? b.StartMoment : null;
                var until = mi == b.EndMeasureIndex ? b.EndMoment : null;
                if (!measureWindows.TryGetValue(mi, out var list))
                    measureWindows[mi] = list = new();
                list.Add((off, from, until));
            }
        }
        if (measureWindows.Count == 0)
            return voice;

        var rebuilt = ImmutableArray.CreateBuilder<Measure>(voice.Measures.Length);
        bool changed = false;
        for (int mi = 0; mi < voice.Measures.Length; mi++)
        {
            var measure = voice.Measures[mi];
            if (!measureWindows.TryGetValue(mi, out var windows))
            {
                rebuilt.Add(measure);
                continue;
            }

            var items = measure.Items.ToArray();
            var onset = Fraction.Zero;
            for (int ii = 0; ii < items.Length; ii++)
            {
                foreach (var (off, from, until) in windows)
                    if ((from is null || onset >= from.Value) && (until is null || onset < until.Value))
                    {
                        items[ii] = Shift(items[ii], off);
                        break;
                    }
                onset += items[ii].Duration;
            }
            rebuilt.Add(measure with { Items = System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(items) });
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
