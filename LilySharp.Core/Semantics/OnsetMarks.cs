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
using System.Runtime.CompilerServices;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// The marks among the music's items that the page prints as a text over the NEXT onset — the
/// note (or rest) after them: <c>pass 2 { … }</c>'s "2nd time only" (docs/eval4-features-design.md
/// §3, <see cref="PassStartSyntax"/>) and an instrument change's name (§1,
/// <see cref="InstrumentSwitchSyntax"/>). A mark takes no time and has no column of its own, so
/// its text rides the onset that follows it in the music it is written in.
/// </summary>
/// <remarks>
/// ONE TABLE A TREE, built the first time it is asked and kept on the root. The marks are found
/// by the green finder (<see cref="SyntaxNode.SitesOfKinds"/>), never the red index: the page
/// asks on a keystroke, which must not build it (KeystrokeStaysRedFreeTests). A book that writes
/// neither mark has an empty table.
/// </remarks>
public static class OnsetMarks
{
    private static readonly SyntaxKind[] s_kinds = [SyntaxKind.PassStart, SyntaxKind.InstrumentSwitch];

    private static readonly ConditionalWeakTable<CompilationUnitSyntax, Dictionary<int, List<SyntaxNode>>> s_marks = new();

    /// <summary>
    /// The marks whose text rides <paramref name="onset"/>, in the order written — the onset is
    /// the first sounding item after each (a note, a chord, a rest but not a spacer, a bare
    /// duration, a <c>q</c>, a drum note; outside a grace group), in the same music; for a pass
    /// region, inside it.
    /// </summary>
    public static IReadOnlyList<SyntaxNode> At(SyntaxNode onset)
    {
        var root = RootOf(onset);
        if (root == null)
            return [];
        var marks = s_marks.GetValue(root, Build);
        return marks.Count != 0 && marks.TryGetValue(onset.SourceStart, out var list) ? list : [];
    }

    /// <summary>The <c>pass N {</c> whose region <paramref name="onset"/> opens, or null.</summary>
    public static PassStartSyntax? PassOpenedAt(SyntaxNode onset)
        => At(onset).OfType<PassStartSyntax>().FirstOrDefault();

    /// <summary>The texts the page prints over <paramref name="onset"/>, each with the source
    /// position of the mark that writes it: an instrument change's name, a pass region's
    /// "2nd time only" (none for a <c>bare</c> one).</summary>
    public static IEnumerable<(string Text, int Source)> TextsOver(SyntaxNode onset)
    {
        foreach (var mark in At(onset))
        {
            if (mark is InstrumentSwitchSyntax change && change.Preset.Length > 0)
                yield return (change.PrintedName, change.SourceStart);
            else if (mark is PassStartSyntax { PrintedText: { } text } start)
                yield return (text, start.SourceStart);
        }
    }

    private static CompilationUnitSyntax? RootOf(SyntaxNode node)
    {
        var cur = node;
        while (cur.Parent is { } parent)
            cur = parent;
        return cur as CompilationUnitSyntax;
    }

    private static Dictionary<int, List<SyntaxNode>> Build(CompilationUnitSyntax root)
    {
        var marks = new Dictionary<int, List<SyntaxNode>>();
        foreach (var site in root.SitesOfKinds(s_kinds))
            if (NextOnsetOf(site) is { } onset)
            {
                if (!marks.TryGetValue(onset.SourceStart, out var list))
                    marks[onset.SourceStart] = list = [];
                list.Add(site);
            }
        return marks;
    }

    /// <summary>The first onset among the items after <paramref name="mark"/> in the music it is
    /// written in. A pass region looks no further than its <see cref="PassEndSyntax"/> (the marks
    /// of a region nested in it are passed over); an instrument change looks to the end of the
    /// music, over pass regions.</summary>
    private static SyntaxNode? NextOnsetOf(SyntaxNode mark)
    {
        if (mark.Parent is not { } parent)
            return null;
        bool region = mark is PassStartSyntax;
        bool after = false;
        int depth = 0;
        for (int i = 0; i < parent.SlotCount; i++)
        {
            if (parent.GetChild(i) is not { } item)
                continue;
            if (!after)
            {
                after = item.Kind == mark.Kind && item.SourceStart == mark.SourceStart;
                continue;
            }
            if (item is PassStartSyntax)
            {
                depth++;
                continue;
            }
            if (item is PassEndSyntax)
            {
                if (region && depth == 0)
                    return null;
                depth--;
                continue;
            }
            if (IsOnset(item))
                return item;
            if (item is GraceExpressionSyntax)
                continue;
            foreach (var inner in item.DescendantNodes())
            {
                if (inner.Ancestors(item).Any(a => a is GraceExpressionSyntax))
                    continue;
                if (IsOnset(inner))
                    return inner;
            }
        }
        return null;
    }

    private static bool IsOnset(SyntaxNode node) => node switch
    {
        RestSyntax rest => rest.RestText != "s",
        NoteSyntax or ChordSyntax or BareDurationSyntax or ChordRepetitionSyntax or DrumNoteSyntax => true,
        _ => false,
    };

    // The nodes between `node` and `top` (both left out).
    private static IEnumerable<SyntaxNode> Ancestors(this SyntaxNode node, SyntaxNode top)
    {
        for (var cur = node.Parent; cur != null && cur != top; cur = cur.Parent)
            yield return cur;
    }
}
