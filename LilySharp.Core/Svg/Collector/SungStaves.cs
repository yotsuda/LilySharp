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
/// Which parts a score SINGS and which it PLAYS, read off its rows (owner's decision 2026-11-08,
/// docs/singing-design.md §3): the words belong to the staff they are printed under. A staff row
/// with a lyrics row under it that sings its part is a singer's staff — the preview gives it the
/// voice; a staff row of the same part with no words under it is an instrument's — the part
/// sounds both ways, once each. A lyrics row belongs to the nearest staff of the part it sings
/// above it (the page draws it there), else the first such staff below it; a row that sings a
/// part no staff shows makes that part a singer the page does not draw (owner's decision
/// 2026-11-09: `staff m2  lyrics verse sings melody` sings the melody unseen — the page places
/// the row by the melody's rhythm already, <c>MeasureCollector.CollectMelodyFor</c>; the files
/// put the syllables on its notes, <c>SungSyllables</c>; the MIDI sounds it, <c>MidiExporter</c>).
/// Until then such a row gave no voice. LILYSHARP-OWN: LilyPond plays nothing.
/// </summary>
public static class SungStaves
{
    /// <summary>The parts with a sung staff and the parts with a played staff, each in row order.</summary>
    public sealed record Sounds(IReadOnlyList<string> Sung, IReadOnlyList<string> Played);

    /// <summary>
    /// The parts the score's lyrics rows sing that NO staff of the score shows, in row order: the
    /// unseen singers. Each is a declared part (a row may also name a voice of an engraved part,
    /// which is that staff's affair). What <see cref="Of"/> adds to <c>Sung</c> after the staves,
    /// and what the MIDI export sounds beside the parts the score engraves.
    /// </summary>
    public static List<string> UnseenSungParts(SyntaxNode root, RenderSpec spec)
    {
        var shown = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in Flat(spec.Items))
            switch (item)
            {
                case SingleStaffSpec s: shown.Add(s.Staff.VoiceName); break;
                case TabStaffSpec t: shown.Add(t.Staff.VoiceName); break;
                case CondensedStaffSpec c: shown.UnionWith(c.PartNames); break;
                case CombinedStaffSpec c: shown.UnionWith(c.PartNames); break;
            }
        HashSet<string>? declared = null;
        var unseen = new List<string>();
        foreach (var row in Flat(spec.Items).OfType<LyricsRowSpec>())
        {
            var target = LyricBindings.TargetOfRow(root, row.PartName, row.Sings);
            if (target == null || shown.Contains(target) || unseen.Contains(target))
                continue;
            declared ??= root.DescendantNodes<PartDeclarationSyntax>().Select(p => p.Name.Text).ToHashSet(StringComparer.Ordinal);
            if (declared.Contains(target))
                unseen.Add(target);
        }
        return unseen;
    }

    public static Sounds Of(SyntaxNode root, RenderSpec spec)
    {
        // The score's rows in page order: a performer's staff (its part), or a lyrics row. A
        // lyrics row written right under the staff it sings is already folded into that staff by
        // RenderSpecParser (StaffSpec.WithLyrics): such a staff is sung at once; the rows that
        // stayed rows (unbound, or under another part's staff) are placed below.
        var rows = new List<(string? Part, LyricsRowSpec? Lyrics)>();
        var sungRows = new HashSet<int>();
        foreach (var item in Flat(spec.Items))
        {
            switch (item)
            {
                case SingleStaffSpec s: AddStaff(s.Staff); break;
                case TabStaffSpec t: AddStaff(t.Staff); break;
                case CondensedStaffSpec c: foreach (var p in c.PartNames) rows.Add((p, null)); break;
                case CombinedStaffSpec c: foreach (var p in c.PartNames) rows.Add((p, null)); break;
                case LyricsRowSpec r: rows.Add((null, r)); break;
            }
        }
        void AddStaff(StaffSpec staff)
        {
            if (!staff.WithLyrics.IsDefaultOrEmpty)
                sungRows.Add(rows.Count);
            rows.Add((staff.VoiceName, null));
        }
        string? firstStaff = rows.FirstOrDefault(r => r.Part != null).Part;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Lyrics is not { } row)
                continue;
            // The part the row sings: its binding, else the staff above it (the page's rule for
            // an unbound row), else the score's first staff.
            string? above = null;
            for (int j = i - 1; j >= 0 && above == null; j--)
                above = rows[j].Part;
            var target = LyricBindings.TargetOfRow(root, row.PartName, row.Sings) ?? above ?? firstStaff;
            if (target == null)
                continue;
            int owner = -1;
            for (int j = i - 1; j >= 0 && owner < 0; j--)
                if (rows[j].Part == target) owner = j;
            for (int j = i + 1; j < rows.Count && owner < 0; j++)
                if (rows[j].Part == target) owner = j;
            if (owner >= 0)
                sungRows.Add(owner);
        }
        var sung = new List<string>();
        var played = new List<string>();
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Part is not { } part)
                continue;
            var list = sungRows.Contains(i) ? sung : played;
            if (!list.Contains(part))
                list.Add(part);
        }
        // The singers no staff shows, after the staves.
        foreach (var part in UnseenSungParts(root, spec))
            if (!sung.Contains(part))
                sung.Add(part);
        return new Sounds(sung, played);
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
