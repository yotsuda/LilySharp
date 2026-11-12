// This file is part of Lily#.
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Lily# is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// Lily# is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// The percent marks a shared staff cannot draw as a sign: those of one part of a
/// <c>condensedStaff</c> (or a <c>combinedStaff</c>) in a bar where another of its parts
/// writes music of its own.
/// </summary>
/// <remarks>
/// A percent sign stands for the whole bar of its STAFF: the page draws it at the bar's middle
/// and leaves out everything else the staff holds there. With one part's <c>%</c> beside the
/// other's notes on a condensed staff, the other part's notes were left out too — its slur and
/// its tuplet bracket drawn over an empty bar (grammar evaluation 3, 2026-11-13, a string
/// quartet's two violins). LILYSHARP-OWN: such a mark is written out — the bars it repeats,
/// drawn again as notes — and the sign is kept for the bars EVERY part of the staff that plays
/// the section marks.
/// <para>
/// Read off the syntax, by bar: a part's marks stand in the bars <see cref="MeasureModel.Split"/>
/// puts them in, counted from its section's start. A mark inside a phrase body is not looked
/// at (its text is no bar of one part) and stays a sign.
/// </para>
/// </remarks>
internal static class CondensedPercents
{
    /// <summary>The source starts of the marks to write out under <paramref name="spec"/>;
    /// empty when the score shares no staff or no shared part writes a mark.</summary>
    public static HashSet<int> WrittenOut(SyntaxNode root, RenderSpec spec)
    {
        var writtenOut = new HashSet<int>();
        var staves = new List<IReadOnlyList<string>>();
        void Gather(IEnumerable<RenderItemSpec> items)
        {
            foreach (var item in items)
                switch (item)
                {
                    case CondensedStaffSpec c when c.PartNames.Length > 1: staves.Add(c.PartNames); break;
                    case CombinedStaffSpec c when c.PartNames.Length > 1: staves.Add(c.PartNames); break;
                    case GrandStaffRenderSpec g: Gather(g.GrandStaff.Members); break;
                }
        }
        Gather(spec.Items);
        if (staves.Count == 0 || !root.DescendantNodesOfKinds(SyntaxKind.PercentMarkItem).Any())
            return writtenOut;

        // Each part's marks, by section: the bars of the section each mark covers.
        var phrases = SectionBarCounts.PhraseBodies(root);
        var marked = new Dictionary<(string Section, string Part), List<(int Start, int First, int Last)>>();
        foreach (var voice in SectionBarCounts.SemanticVoices(root, phrases))
        {
            string? part = voice.Container switch
            {
                PartBlockSyntax block => block.Name,
                SectionDeclarationSyntax { Parent: PartDeclarationSyntax owner } => owner.Name.Text,
                _ => null,
            };
            if (voice.IsChords || part == null || !staves.Any(s => s.Contains(part)))
                continue;
            if (!marked.TryGetValue((voice.SectionName, part), out var marks))
                marked[(voice.SectionName, part)] = marks = new();
            var bars = MeasureModel.Split(voice.Container, phrases);
            foreach (var mark in voice.Container.DescendantNodesOfKinds(SyntaxKind.PercentMarkItem))
            {
                int first = bars.FindIndex(b => b.Span.Contains(mark.SourceStart));
                int last = bars.FindLastIndex(b => b.Span.Contains(mark.SourceStart));
                if (first >= 0)
                    marks.Add((mark.SourceStart, first, last));
            }
        }

        foreach (var staff in staves)
            foreach (var ((section, part), marks) in marked)
            {
                if (!staff.Contains(part))
                    continue;
                foreach (string other in staff)
                {
                    if (other == part || !marked.TryGetValue((section, other), out var theirs))
                        continue;
                    foreach (var (start, first, last) in marks)
                        for (int bar = first; bar <= last; bar++)
                            if (!theirs.Any(t => bar >= t.First && bar <= t.Last))
                            {
                                writtenOut.Add(start);
                                break;
                            }
                }
            }
        return writtenOut;
    }
}
