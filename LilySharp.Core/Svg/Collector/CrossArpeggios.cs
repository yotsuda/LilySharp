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
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>Why an <c>@arpeggio(cross)</c> stayed on its own chord.</summary>
internal enum CrossArpeggioGap
{
    /// <summary>Its staff stands in no grand staff.</summary>
    NotInGrandStaff,
    /// <summary>It is the lowest staff of its grand staff.</summary>
    NoStaffBelow,
    /// <summary>The staff below begins nothing at that moment (a rest, a held note, another rhythm).</summary>
    NothingBeginsBelow,
}

/// <summary>
/// Connects each <c>@arpeggio(cross)</c> (owner's decision 2026-11-12, grammar evaluation 2)
/// to what begins at the same moment on the staff directly below it in the same grand staff:
/// the arpeggio is told how far down its one line runs, and the columns below are told a line
/// runs through them, so their spacing keeps the room. The lower staff writes nothing.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/span-arpeggio-engraver.cc:94-168 process_acknowledged,
///   stop_translation_timestep — with <c>connectArpeggios</c> the PianoStaff makes ONE span
///   Arpeggio over the arpeggios of a moment, takes their stems and side supports, and keeps
///   each of them, transparent, in its own column.
/// LILYSHARP-OWN: the pairing. LilyPond connects whatever arpeggios the staves WROTE at a
/// moment, on as many staves as wrote one; here the upper chord alone writes the mark and
/// the partner is found — every note or chord that begins at that moment in any voice of
/// the staff below. A lower chord that also wrote an arpeggio of its own is taken into the
/// line, as LilyPond's would be.
/// <para>
/// ⚠️ RUN ON THE FINISHED STAVES, EVERY COLLECT (<c>MeasureCollector.CollectMultiStaff</c>'s
/// tail, where <c>@cross</c>'s notes are moved too): what one staff draws depends on the
/// OTHER staff's music, which no walk's checkpoint has read, so nothing here is recorded
/// with a walk or adopted by a resume — the walk records only that the mark was written
/// (<see cref="ArpeggioItem.Cross"/>, a function of its own text). The inputs are never
/// changed in place: a walk's recorded measures are the next keystroke's baseline.
/// </para>
/// </remarks>
internal static class CrossArpeggios
{
    /// <summary>Whether any arpeggio writes <c>cross</c> — the gate that keeps every other
    /// score's collect exactly as it was.</summary>
    internal static bool Any(ImmutableArray<ArpeggioItem> arpeggios)
    {
        foreach (var a in arpeggios)
            if (a.Cross)
                return true;
        return false;
    }

    /// <summary>
    /// The global index of the staff a line from <paramref name="staffIndex"/> runs down to —
    /// the next notation staff after it in the same grand staff (a lyrics or chords row
    /// between the two hands is stepped over) — or the reason there is none.
    /// </summary>
    internal static (int Below, CrossArpeggioGap Gap) StaffBelow(ImmutableArray<StaffGroup> groups, int staffIndex)
    {
        int first = 0;
        foreach (var group in groups)
        {
            int end = first + group.Staves.Length;
            if (staffIndex >= first && staffIndex < end)
            {
                if (!group.IsGrandStaff)
                    return (-1, CrossArpeggioGap.NotInGrandStaff);
                for (int i = staffIndex + 1; i < end; i++)
                    if (IsNotation(group.Staves[i - first]))
                        return (i, CrossArpeggioGap.NothingBeginsBelow);
                return (-1, CrossArpeggioGap.NoStaffBelow);
            }
            first = end;
        }
        return (-1, CrossArpeggioGap.NotInGrandStaff);
    }

    private static bool IsNotation(Staff staff) => !staff.IsTab && !staff.IsTextRow && !staff.IsOssia;

    /// <summary>Why this unconnected cross arpeggio of <paramref name="score"/> is alone.</summary>
    internal static CrossArpeggioGap WhyAlone(MultiStaffScore score, ArpeggioItem arpeggio)
        => StaffBelow(score.StaffGroups, arpeggio.StaffIndex).Gap;

    /// <summary>
    /// The staves and the arpeggios with every cross arpeggio connected that can be: its
    /// <see cref="ArpeggioItem.PartnerStaffIndex"/> and lowest head set, the items it runs
    /// down to stamped (<see cref="MusicItem.SpanArpeggio"/>), and an arpeggio of their own
    /// those items wrote dropped from the list (the one line is theirs too).
    /// </summary>
    internal static (ImmutableArray<StaffGroup> Groups, ImmutableArray<ArpeggioItem> Arpeggios) Connect(
        ImmutableArray<StaffGroup> groups, ImmutableArray<ArpeggioItem> arpeggios)
    {
        var staves = groups.SelectMany(g => g.Staves).ToArray();
        // (staff, voice, measure, item) of every column a line runs down to, and its kind.
        var reached = new Dictionary<(int Staff, int Voice, int Measure, int Item), SpanArpeggioPart>();
        ImmutableArray<ArpeggioItem>.Builder? connected = null;

        for (int ai = 0; ai < arpeggios.Length; ai++)
        {
            var a = arpeggios[ai];
            if (!a.Cross || (uint)a.StaffIndex >= (uint)staves.Length)
                continue;
            int below = StaffBelow(groups, a.StaffIndex).Below;
            if (below < 0 || ItemAt(staves[a.StaffIndex], a.VoiceIndex, a.MeasureIndex, a.ItemIndex) is not { } upper
                || upper.Item.GraceTime)
                continue;

            // The moment is the bar's and the clock is each voice's own: the bars of a score
            // are one grid (SynchronizeBarlines), so the same index is the same bar below.
            var onset = LayoutUtilities.ItemOnset(upper.Measure, a.ItemIndex);
            var part = a.Bracket ? SpanArpeggioPart.Bracket : SpanArpeggioPart.Wiggle;
            int lowest = int.MaxValue;
            var voices = staves[below].Voices;
            for (int vi = 0; vi < voices.Length; vi++)
            {
                if (a.MeasureIndex >= voices[vi].Measures.Length)
                    continue;
                var items = voices[vi].Measures[a.MeasureIndex].Items;
                var at = Fraction.Zero;
                for (int i = 0; i < items.Length && at <= onset; i++)
                {
                    var item = items[i];
                    // A grace note stands before the moment, a rest sounds nothing, and a note
                    // @cross moved away is not on this staff: none of them is rolled into it.
                    if (at == onset && !item.GraceTime && item.CrossStaffIndex < 0 && LowestHead(item) is { } low)
                    {
                        lowest = Math.Min(lowest, low);
                        reached[(below, vi, a.MeasureIndex, i)] = part;
                    }
                    at += item.Duration;
                }
            }
            if (lowest == int.MaxValue)
                continue;
            (connected ??= arpeggios.ToBuilder())[ai] = a with
            {
                PartnerStaffIndex = below,
                PartnerLowestHead = lowest,
            };
        }

        if (connected is null)
            return (groups, arpeggios);

        // An arpeggio a reached chord wrote for itself is this line now.
        for (int ai = connected.Count - 1; ai >= 0; ai--)
        {
            var a = connected[ai];
            if (a.PartnerStaffIndex < 0 && reached.ContainsKey((a.StaffIndex, a.VoiceIndex, a.MeasureIndex, a.ItemIndex)))
                connected.RemoveAt(ai);
        }

        return (Stamp(groups, staves, reached), connected.ToImmutable());
    }

    /// <summary>
    /// For the exports, which write from the source: the offsets of the notes and chords whose
    /// <c>@arpeggio(cross)</c> the page connected (<c>Uppers</c>), and of the ones those lines
    /// run down to with the kind of each (<c>Reached</c>) — the lower staff wrote nothing, and
    /// LilyPond and MusicXML both want the arpeggio said on every chord of it.
    /// </summary>
    /// <remarks>
    /// By written source position: a phrase's chord played twice is one written chord, and is
    /// written connected when ANY of its plays is.
    /// </remarks>
    internal static (HashSet<int> Uppers, Dictionary<int, SpanArpeggioPart> Reached) Sources(MultiStaffScore score)
    {
        var uppers = new HashSet<int>();
        var reached = new Dictionary<int, SpanArpeggioPart>();
        var staves = score.StaffGroups.SelectMany(g => g.Staves).ToArray();
        foreach (var a in score.Arpeggios)
        {
            if (a.PartnerStaffIndex < 0 || (uint)a.PartnerStaffIndex >= (uint)staves.Length
                || (uint)a.StaffIndex >= (uint)staves.Length
                || ItemAt(staves[a.StaffIndex], a.VoiceIndex, a.MeasureIndex, a.ItemIndex) is not { } upper)
                continue;
            uppers.Add(a.SourcePosition);
            var onset = LayoutUtilities.ItemOnset(upper.Measure, a.ItemIndex);
            foreach (var voice in staves[a.PartnerStaffIndex].Voices)
            {
                if (a.MeasureIndex >= voice.Measures.Length)
                    continue;
                var at = Fraction.Zero;
                foreach (var item in voice.Measures[a.MeasureIndex].Items)
                {
                    if (at > onset)
                        break;
                    // A stamped item — one that wrote an arpeggio of its own says it itself.
                    if (at == onset && !item.GraceTime && item.SpanArpeggio != SpanArpeggioPart.None)
                        reached[item.SourcePosition] = item.SpanArpeggio;
                    at += item.Duration;
                }
            }
        }
        return (uppers, reached);
    }

    /// <summary>The lowest head of a note or chord in its staff's positions, or null for
    /// anything that has none (a rest, a change, an empty chord).</summary>
    private static int? LowestHead(MusicItem item) => item switch
    {
        NoteItem note => note.StaffPosition,
        ChordItem { Notes.Length: > 0 } chord => chord.Notes.Min(n => n.StaffPosition),
        _ => null,
    };

    private static (Measure Measure, MusicItem Item)? ItemAt(Staff staff, int voice, int measure, int item)
        => (uint)voice < (uint)staff.Voices.Length
           && (uint)measure < (uint)staff.Voices[voice].Measures.Length
           && (uint)item < (uint)staff.Voices[voice].Measures[measure].Items.Length
            ? (staff.Voices[voice].Measures[measure], staff.Voices[voice].Measures[measure].Items[item])
            : null;

    /// <summary>The groups with the reached items stamped, by copy. A chord that rolls or is
    /// bracketed on its own account already keeps that room and is left as it is.</summary>
    private static ImmutableArray<StaffGroup> Stamp(ImmutableArray<StaffGroup> groups, Staff[] staves,
        Dictionary<(int Staff, int Voice, int Measure, int Item), SpanArpeggioPart> reached)
    {
        var stamped = new Staff?[staves.Length];
        foreach (var byStaff in reached.GroupBy(r => r.Key.Staff))
        {
            var staff = staves[byStaff.Key];
            var voices = staff.Voices.ToArray();
            foreach (var byVoice in byStaff.GroupBy(r => r.Key.Voice))
            {
                var measures = voices[byVoice.Key].Measures.ToArray();
                foreach (var byMeasure in byVoice.GroupBy(r => r.Key.Measure))
                {
                    var items = measures[byMeasure.Key].Items.ToArray();
                    foreach (var (address, part) in byMeasure)
                        if (items[address.Item] is not ChordItem { HasArpeggio: true }
                            and not ChordItem { HasArpeggioBracket: true })
                            items[address.Item] = items[address.Item] with { SpanArpeggio = part };
                    measures[byMeasure.Key] = measures[byMeasure.Key] with { Items = ImmutableArray.Create(items) };
                }
                voices[byVoice.Key] = voices[byVoice.Key] with { Measures = ImmutableArray.Create(measures) };
            }
            stamped[byStaff.Key] = staff with { Voices = ImmutableArray.Create(voices) };
        }

        int walk = 0;
        return groups.Select(g => g with
        {
            Staves = g.Staves.Select(st => stamped[walk++] ?? st).ToImmutableArray(),
        }).ToImmutableArray();
    }
}
