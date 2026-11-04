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

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Music;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Collector;

/// <summary>A syllable of the page and the note it stands on, for a file to write it on that
/// note: the page's item, which printing of that item's source it is (the k-th, counted on its
/// staff in written order), the part sung, and the track the words are written in.</summary>
internal readonly record struct SungSyllable(LyricItem Lyric, MusicItem Item, int Printing, string Part, string? Track);

/// <summary>
/// The one answer to "which note does this syllable stand on" for the files (MusicXML, MIDI):
/// the page's (<see cref="LyricCollector"/>). A file does not count syllables itself.
/// </summary>
/// <remarks>
/// A track the score does not place is not on the page, and is not in the files. An unbound row
/// (the even-spread lead-sheet row) names no note, and a file needs one: it is read as singing the
/// staff above it, so its words are kept. ⚠️ A row singing a part the score does not engrave has
/// no page note to stand on, and is left out.
/// </remarks>
internal static class SungSyllables
{
    /// <summary>Every sung syllable in page order (bar, moment, row). <paramref name="pageOfSpec"/>
    /// is the page of <paramref name="spec"/> if the caller has collected it.</summary>
    public static List<SungSyllable> Read(SyntaxTree tree, RenderSpec spec, MultiStaffScore? pageOfSpec = null)
    {
        var result = new List<SungSyllable>();
        var root = tree.GetRoot();
        var blocks = root.DescendantNodes<LyricsBlockSyntax>().ToList();
        if (blocks.Count == 0)
            return result;

        var bound = WithUnboundRowsBound(root, spec);
        var page = ReferenceEquals(bound, spec) && pageOfSpec != null
            ? pageOfSpec
            : new MeasureCollector
            {
                FontsOverride = spec.FontsRef,
                PaperOverride = spec.PaperRef,
                LayoutOverride = spec.LayoutRef,
            }.CollectMultiStaff(tree, bound);
        var staves = page.StaffGroups.SelectMany(g => g.Staves).ToList();

        var rowTargets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in Flat(bound.Items).OfType<LyricsRowSpec>())
            if (LyricBindings.TargetOfRow(root, row.PartName, row.Sings) is { } target)
                rowTargets.TryAdd(row.PartName, target);

        // Each staff's items' printings. Counted per staff: a phrase two parts play stands at one
        // source position, and is the k-th of it in each.
        var printing = new Dictionary<MusicItem, int>(ReferenceEqualityComparer.Instance);
        foreach (var staff in staves)
        {
            var seen = new Dictionary<int, int>();
            foreach (var voice in staff.Voices)
                foreach (var measure in voice.Measures)
                    foreach (var item in measure.Items)
                        if (item is NoteItem or ChordItem && item.SourcePosition >= 0)
                        {
                            seen.TryGetValue(item.SourcePosition, out int k);
                            printing[item] = k;
                            seen[item.SourcePosition] = k + 1;
                        }
        }

        foreach (var lyric in page.Lyrics.OrderBy(l => l.MeasureIndex).ThenBy(l => l.Timing).ThenBy(l => l.VerseNumber))
        {
            if (lyric.StaffIndex >= staves.Count)
                continue;
            var (item, part) = lyric.IsLyricsRow
                ? RowSungItem(staves, root, rowTargets, lyric)
                : (StaffSungItem(staves[lyric.StaffIndex], lyric), staves[lyric.StaffIndex].PrimaryVoice.Name);
            if (item == null || part == null || !printing.TryGetValue(item, out int k))
                continue;
            var track = blocks.FirstOrDefault(b => lyric.SourcePosition >= b.Span.Start && lyric.SourcePosition < b.Span.End)?.VoiceName;
            result.Add(new SungSyllable(lyric, item, k, part, track));
        }
        return result;
    }

    /// <summary>The note a staff's syllable is under: the primary voice's item it indexes, or — a
    /// bound voice's syllable — the note of another voice starting at its moment.</summary>
    private static MusicItem? StaffSungItem(Staff staff, LyricItem lyric)
    {
        if (lyric.VoiceId == 0)
        {
            var items = staff.PrimaryVoice.Measures is var ms && lyric.MeasureIndex < ms.Length
                ? ms[lyric.MeasureIndex].Items
                : default;
            return !items.IsDefault && lyric.ItemIndex >= 0 && lyric.ItemIndex < items.Length
                ? items[lyric.ItemIndex]
                : null;
        }
        foreach (var voice in staff.Voices.Skip(1))
        {
            if (lyric.MeasureIndex >= voice.Measures.Length)
                continue;
            var at = Semantics.Fraction.Zero;
            foreach (var item in voice.Measures[lyric.MeasureIndex].Items)
            {
                if (at == lyric.Timing && item is NoteItem or ChordItem && !item.GraceTime)
                    return item;
                at += item.Duration;
            }
        }
        return null;
    }

    /// <summary>The note a bound row's syllable is under: the item it indexes in the melody it
    /// sings, that part's first voice or a named voice of an engraved part.</summary>
    private static (MusicItem? Item, string? Part) RowSungItem(
        List<Staff> staves, SyntaxNode root, Dictionary<string, string> rowTargets, LyricItem lyric)
    {
        if (!rowTargets.TryGetValue(staves[lyric.StaffIndex].PrimaryVoice.Name, out var target))
            return (null, null);
        foreach (var staff in staves)
        {
            string part = staff.PrimaryVoice.Name;
            int slot = part == target ? 0
                : LyricBindings.VoiceSlotsOfPart(root, part).TryGetValue(target, out int named) ? named : -1;
            if (slot < 0 || slot >= staff.Voices.Length || lyric.MeasureIndex >= staff.Voices[slot].Measures.Length)
                continue;
            var items = staff.Voices[slot].Measures[lyric.MeasureIndex].Items;
            return (lyric.ItemIndex >= 0 && lyric.ItemIndex < items.Length ? items[lyric.ItemIndex] : null, part);
        }
        return (null, null);
    }

    /// <summary>The spec with each unbound lyrics row singing the staff above it (the score's first
    /// staff, when none stands above); the spec itself when no row is unbound.</summary>
    private static RenderSpec WithUnboundRowsBound(SyntaxNode root, RenderSpec spec)
    {
        static string? VoiceOf(RenderItemSpec item) => item switch
        {
            SingleStaffSpec s => s.Staff.VoiceName,
            TabStaffSpec t => t.Staff.VoiceName,
            _ => null,
        };
        var first = Flat(spec.Items).Select(VoiceOf).FirstOrDefault(v => v != null);
        if (first == null)
            return spec;
        bool any = false;
        string? above = null;
        var items = ImmutableArray.CreateBuilder<RenderItemSpec>(spec.Items.Length);
        foreach (var item in spec.Items)
        {
            above = VoiceOf(item) ?? above;
            if (item is LyricsRowSpec row && LyricBindings.TargetOfRow(root, row.PartName, row.Sings) == null)
            {
                items.Add(row with { Sings = above ?? first });
                any = true;
            }
            else
                items.Add(item);
        }
        return any ? spec with { Items = items.MoveToImmutable() } : spec;
    }

    private static IEnumerable<RenderItemSpec> Flat(IEnumerable<RenderItemSpec> items)
    {
        foreach (var item in items)
        {
            yield return item;
            if (item is GrandStaffRenderSpec g)
                foreach (var member in Flat(g.GrandStaff.Members))
                    yield return member;
        }
    }
}
