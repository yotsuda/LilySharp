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
/// The regions <c>pass 2 { … }</c> writes (docs/eval4-features-design.md §3), read off the two
/// marks the parser leaves among the music's items (<see cref="PassStartSyntax"/>,
/// <see cref="PassEndSyntax"/>): which onset opens each one — the note (or rest) the page
/// prints "2nd time only" over.
/// </summary>
/// <remarks>
/// ONE TABLE A TREE, built the first time it is asked and kept on the root. The marks are found
/// by the green finder (<see cref="SyntaxNode.SitesOfKinds"/>), never the red index: the page
/// asks on a keystroke, which must not build it (KeystrokeStaysRedFreeTests).
/// </remarks>
public static class PassRegions
{
    private static readonly SyntaxKind[] s_passStart = [SyntaxKind.PassStart];

    private static readonly ConditionalWeakTable<CompilationUnitSyntax, Dictionary<int, PassStartSyntax>> s_openers = new();

    /// <summary>
    /// The <c>pass N {</c> whose region <paramref name="onset"/> is the first sounding item of —
    /// a note, a chord, a rest (not a spacer), a bare duration, a <c>q</c>, a drum note; the first
    /// one outside a grace group — or null.
    /// </summary>
    public static PassStartSyntax? OpenedAt(SyntaxNode onset)
    {
        var root = RootOf(onset);
        if (root == null)
            return null;
        var openers = s_openers.GetValue(root, Build);
        return openers.Count != 0 && openers.TryGetValue(onset.SourceStart, out var start) ? start : null;
    }

    /// <summary>What the page prints over <paramref name="onset"/> for the region it opens
    /// ("2nd time only"), or null — none opens there, or the region is <c>bare</c>.</summary>
    public static string? TextOver(SyntaxNode onset) => OpenedAt(onset)?.PrintedText;

    private static CompilationUnitSyntax? RootOf(SyntaxNode node)
    {
        var cur = node;
        while (cur.Parent is { } parent)
            cur = parent;
        return cur as CompilationUnitSyntax;
    }

    private static Dictionary<int, PassStartSyntax> Build(CompilationUnitSyntax root)
    {
        var openers = new Dictionary<int, PassStartSyntax>();
        foreach (var site in root.SitesOfKinds(s_passStart))
            if (site is PassStartSyntax start && FirstOnsetOf(start) is { } onset)
                openers.TryAdd(onset.SourceStart, start);
        return openers;
    }

    /// <summary>The first onset among the items after <paramref name="start"/> up to its
    /// <see cref="PassEndSyntax"/> (the marks of a region nested in it are passed over).</summary>
    private static SyntaxNode? FirstOnsetOf(PassStartSyntax start)
    {
        if (start.Parent is not { } parent)
            return null;
        bool after = false;
        int depth = 0;
        for (int i = 0; i < parent.SlotCount; i++)
        {
            if (parent.GetChild(i) is not { } item)
                continue;
            if (!after)
            {
                after = item.Kind == SyntaxKind.PassStart && item.SourceStart == start.SourceStart;
                continue;
            }
            if (item is PassStartSyntax)
            {
                depth++;
                continue;
            }
            if (item is PassEndSyntax)
            {
                if (depth-- == 0)
                    return null;
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
