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
using System.Linq;
using LilySharp.Core.Music;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// How each part of a score sounds, read off its <c>staff</c> items and the file's <c>singer</c>
/// lines (docs/lyrics-singer-design.md §5): a staff with no <c>sounds</c> is a voice where a
/// <c>singer</c> line sings its part and an instrument where none does; <c>sounds</c> says so
/// outright. Printed words have no say. Two staves of one part add up.
/// </summary>
public static class SungStaves
{
    /// <summary>The parts with a sung staff and the parts with a played staff, each in row order.</summary>
    /// <param name="Sung">The parts with a staff a voice sings.</param>
    /// <param name="Played">The parts with a staff that plays its instrument, words printed under it or not.</param>
    /// <param name="PrintedWords">The parts that have words in this score and no voice asked for them
    /// (<see cref="LyricSinger.SungReaderOf"/>), for the editor to say how to have them sung.</param>
    public sealed record Sounds(IReadOnlyList<string> Sung, IReadOnlyList<string> Played, IReadOnlyList<string>? PrintedWords = null);

    /// <summary>The parts the score sounds and does not draw (<c>staff PART hidden</c>): what the
    /// MIDI export sounds beside the parts the score engraves.</summary>
    public static List<string> UnseenSungParts(SyntaxNode root, RenderSpec spec)
        => spec.HiddenStaves.IsDefault ? [] : spec.HiddenStaves.Select(h => h.Part).Distinct(StringComparer.Ordinal).ToList();

    public static Sounds Of(SyntaxNode root, RenderSpec spec)
    {
        var lines = LyricSinger.LinesOf(root);
        var singers = lines.Select(l => l.Part).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var sung = new List<string>();
        var played = new List<string>();
        void Add(string part, string? word)
        {
            bool hasSinger = singers.Contains(part);
            if (hasSinger && word is null or "voice" or "both" && !sung.Contains(part))
                sung.Add(part);
            if ((word is "instrument" or "both" || (word == null && !hasSinger)) && !played.Contains(part))
                played.Add(part);
        }
        var printed = new List<string>();
        foreach (var item in Flat(spec.Items))
        {
            switch (item)
            {
                case SingleStaffSpec s:
                    Add(s.Staff.VoiceName, s.Staff.Sounds);
                    if (!s.Staff.WithLyrics.IsDefaultOrEmpty && !printed.Contains(s.Staff.VoiceName))
                        printed.Add(s.Staff.VoiceName);
                    break;
                case TabStaffSpec t: Add(t.Staff.VoiceName, t.Staff.Sounds); break;
                case CondensedStaffSpec c: foreach (var p in c.PartNames) Add(p, null); break;
                case CombinedStaffSpec c: foreach (var p in c.PartNames) Add(p, null); break;
                case LyricsRowSpec { Sings: { } on } when !printed.Contains(on): printed.Add(on); break;
            }
        }
        if (!spec.HiddenStaves.IsDefault)
            foreach (var hidden in spec.HiddenStaves)
                Add(hidden.Part, hidden.Sounds);
        return new Sounds(sung, played, lines.Count == 0 ? printed : []);
    }

    private static IEnumerable<RenderItemSpec> Flat(IEnumerable<RenderItemSpec> items)
    {
        foreach (var item in items)
        {
            if (item is GrandStaffRenderSpec g)
                foreach (var member in Flat(g.GrandStaff.Members))
                    yield return member;
            else
                yield return item;
        }
    }
}
