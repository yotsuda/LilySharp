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

using System.Runtime.CompilerServices;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Music;

/// <summary>
/// The shared chord-repetition resolver: ONE document-order walk per tree maps
/// every <c>q</c> to the chord it repeats, and every walker (collector,
/// exporters, validators) reads that map instead of tracking its own
/// last-chord. Only a <c>&lt;&gt;</c> chord updates the running chord — notes
/// and rests are transparent — and a <c>q</c> with no chord before it in its
/// top-level body resolves to nothing (the validator reports it).
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/music-functions.scm:923-946 expand-repeat-chords! — a fold
/// over the music tree in document order threading last-chord; only music of
/// type event-chord (a written <c>&lt;&gt;</c> chord) replaces it. The
/// expansion runs in toplevel-music-functions (ly/music-functions-init.ly:2143),
/// AFTER \relative has been resolved — which is why a <c>q</c> copies the
/// original chord's ABSOLUTE pitches and is transparent to the relative frame.
/// The map resets at each top-level declaration: a body is its own walk, so a
/// structural replay (~Main entered twice) sees the same mapping every time.
/// </remarks>
public static class ChordRepetitions
{
    // One map per red tree root, built on first query and dropped with the tree.
    // Red children are cached (SyntaxNode.GetChild), so node references are
    // stable keys within a tree.
    private static readonly ConditionalWeakTable<SyntaxNode, Dictionary<ChordRepetitionSyntax, ChordSyntax>> Maps = new();

    // The map's value set, for O(1) IsOriginal membership — derived from the same
    // build, one per tree, dropped with it.
    private static readonly ConditionalWeakTable<SyntaxNode, HashSet<ChordSyntax>> Originals = new();

    /// <summary>The chord a <c>q</c> repeats, or null when no chord precedes it
    /// in its top-level body (LP: warning "Bad chord repetition").</summary>
    public static ChordSyntax? OriginalOf(ChordRepetitionSyntax repetition)
    {
        var top = (SyntaxNode)repetition;
        while (top.Parent != null)
            top = top.Parent;
        var map = Maps.GetValue(top, BuildMap);
        return map.TryGetValue(repetition, out var chord) ? chord : null;
    }

    /// <summary>Whether <paramref name="chord"/> is the original some <c>q</c> in its
    /// tree copies — the collect-resume recorder's filter for which resolved
    /// spellings are worth logging (finding 3-4; a book without <c>q</c> logs
    /// nothing). The reverse set is the map's values, built once per tree.</summary>
    public static bool IsOriginal(ChordSyntax chord)
    {
        var top = (SyntaxNode)chord;
        while (top.Parent != null)
            top = top.Parent;
        var set = Originals.GetValue(top,
            root => new HashSet<ChordSyntax>(Maps.GetValue(root, BuildMap).Values));
        return set.Contains(chord);
    }

    private static Dictionary<ChordRepetitionSyntax, ChordSyntax> BuildMap(SyntaxNode root)
    {
        var map = new Dictionary<ChordRepetitionSyntax, ChordSyntax>();
        ChordSyntax? last = null;
        Thread(root, map, ref last);
        return map;
    }

    /// <summary>Document-order threading — the same order as LP's fold (element
    /// before elements), which the syntax tree mirrors. A body-owning
    /// declaration (part / section / phrase / part cell) opens its OWN scope:
    /// the running chord does not leak across bodies, so a structural replay
    /// (~Main entered twice) resolves the same way on every walk.</summary>
    private static void Thread(SyntaxNode node, Dictionary<ChordRepetitionSyntax, ChordSyntax> map,
        ref ChordSyntax? last)
    {
        switch (node)
        {
            case ChordSyntax chord:
                last = chord;
                return; // a chord holds no chords
            case ChordRepetitionSyntax q:
                if (last != null)
                    map[q] = last;
                return;
        }
        for (int i = 0; i < node.SlotCount; i++)
        {
            if (node.GetChild(i) is not { } child || child is SyntaxTokenNode)
                continue;
            if (IsScopeBoundary(child))
            {
                ChordSyntax? inner = null;
                Thread(child, map, ref inner);
            }
            else
            {
                Thread(child, map, ref last);
            }
        }
    }

    private static bool IsScopeBoundary(SyntaxNode n) => n is
        PartDeclarationSyntax or SectionDeclarationSyntax or PhraseDeclarationSyntax
        or PartBlockSyntax or ChordPartBlockSyntax;
}
