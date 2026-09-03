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
    private static readonly ConditionalWeakTable<SyntaxNode, Dictionary<ChordRepetitionSyntax, Resolved>> Maps = new();

    /// <summary>What a <c>q</c> resolves to: the chord it repeats and the octaves it
    /// is displaced by (0 for a plain <c>q</c>).</summary>
    private readonly record struct Resolved(ChordSyntax Chord, int Octave);

    // The map's value set, for O(1) IsOriginal membership — derived from the same
    // build, one per tree, dropped with it.
    private static readonly ConditionalWeakTable<SyntaxNode, HashSet<ChordSyntax>> Originals = new();

    /// <summary>The chord a <c>q</c> repeats, or null when no chord precedes it
    /// in its top-level body (LP: warning "Bad chord repetition").</summary>
    public static ChordSyntax? OriginalOf(ChordRepetitionSyntax repetition)
        => Lookup(repetition) is { } r ? r.Chord : null;

    /// <summary>
    /// The octaves this <c>q</c> is displaced by — the running total of the octave
    /// marks on it and on every <c>q</c> since the written chord, because each one
    /// repeats the chord as the previous <c>q</c> left it (user decision, 2026-09-03).
    /// So <c>&lt;c e g&gt;4 q' q</c> sounds the chord up an octave twice, and
    /// <c>q' q'</c> climbs by one and then two.
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN: LilyPond's <c>q</c> takes no marks, so there is nothing to port.
    /// A plain <c>q</c> answers 0, and so does one with no chord to repeat.
    /// </remarks>
    public static int DisplacementOf(ChordRepetitionSyntax repetition)
        => Lookup(repetition) is { } r ? r.Octave : 0;

    private static Resolved? Lookup(ChordRepetitionSyntax repetition)
    {
        var top = (SyntaxNode)repetition;
        while (top.Parent != null)
            top = top.Parent;
        var map = Maps.GetValue(top, BuildMap);
        return map.TryGetValue(repetition, out var resolved) ? resolved : null;
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
            root => new HashSet<ChordSyntax>(Maps.GetValue(root, BuildMap).Values.Select(v => v.Chord)));
        return set.Contains(chord);
    }

    private static Dictionary<ChordRepetitionSyntax, Resolved> BuildMap(SyntaxNode root)
    {
        var map = new Dictionary<ChordRepetitionSyntax, Resolved>();
        var running = new Running();
        Thread(root, map, running);
        return map;
    }

    /// <summary>The chord in force and how far the q chain has displaced it. One
    /// object rather than two refs, so a scope boundary resets both or neither.</summary>
    private sealed class Running
    {
        public ChordSyntax? Chord;
        public int Octave;
    }

    /// <summary>Document-order threading — the same order as LP's fold (element
    /// before elements), which the syntax tree mirrors. A body-owning
    /// declaration (part / section / phrase / part cell) opens its OWN scope:
    /// the running chord does not leak across bodies, so a structural replay
    /// (~Main entered twice) resolves the same way on every walk.</summary>
    private static void Thread(SyntaxNode node, Dictionary<ChordRepetitionSyntax, Resolved> map,
        Running running)
    {
        switch (node)
        {
            case ChordSyntax chord:
                // A written chord is the new origin, at its own octave.
                running.Chord = chord;
                running.Octave = 0;
                return; // a chord holds no chords
            case ChordRepetitionSyntax q:
                if (running.Chord != null)
                {
                    // Displacement ACCUMULATES: each q repeats the chord as the last q
                    // left it, so q' q sounds up an octave twice.
                    running.Octave += q.OctaveOffset;
                    map[q] = new Resolved(running.Chord, running.Octave);
                }
                return;
        }
        for (int i = 0; i < node.SlotCount; i++)
        {
            if (node.GetChild(i) is not { } child || child is SyntaxTokenNode)
                continue;
            if (IsScopeBoundary(child))
                Thread(child, map, new Running());
            else
                Thread(child, map, running);
        }
    }

    private static bool IsScopeBoundary(SyntaxNode n) => n is
        PartDeclarationSyntax or SectionDeclarationSyntax or PhraseDeclarationSyntax
        or PartBlockSyntax or ChordPartBlockSyntax;
}
