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
/// expansion runs in toplevel-music-functions (scm/music-functions.scm:1608-1613;
/// ly/music-functions-init.ly:2143 is the SAME call inside <c>retrograde</c>, not the hook),
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
        return OriginalsOf(top).Contains(chord);
    }

    /// <summary>Every chord some <c>q</c> of the tree rooted at <paramref name="root"/>
    /// copies — the set <see cref="IsOriginal"/> answers from. The collect-resume planner
    /// compares two trees' sets (CollectResumePlanner.NewOriginalFloor).</summary>
    internal static IReadOnlySet<ChordSyntax> OriginalsOf(SyntaxNode root)
        => Originals.GetValue(root,
            r => new HashSet<ChordSyntax>(Maps.GetValue(r, BuildMap).Values.Select(v => v.Chord)));

    /// <summary>
    /// The map, from ONE walk of the tree's GREEN nodes — the shape
    /// <c>BareDurations.BuildMap</c> took in session 519: a red is materialised only for a
    /// <c>q</c> that has a chord to repeat and for that chord, by position from the root
    /// (<c>BareDurations.RedOf</c>), the same parent-cached instances every other reader holds.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS WALKED THE RED TREE UNTIL SESSION 603, and it is asked of every new tree —
    /// the preview's root is new on every keystroke — by the collect resume's planner
    /// (<c>CollectResumePlanner.NewOriginalFloor</c>) and by the collector's first chord.
    /// MEASURED (Release, the reader's corpus, 3,760 pitch keystrokes): 4.7% of the render,
    /// and the map came out empty on all but 16 of 3,680 asks — a whole-tree red
    /// materialisation for a book that writes no <c>q</c>.
    /// <c>ChordRepetitionsGreenWalkTests</c> holds this walk to the former red one.
    /// </remarks>
    private static Dictionary<ChordRepetitionSyntax, Resolved> BuildMap(SyntaxNode root)
    {
        var map = new Dictionary<ChordRepetitionSyntax, Resolved>();
        var running = new Running();
        Thread(root.Green, root.Position, root, map, ref running);
        return map;
    }

    /// <summary>The chord in force — as its green node and its full start, so its red is
    /// found only when a <c>q</c> repeats it — and how far the q chain has displaced it. A
    /// scope boundary resets both.</summary>
    private struct Running
    {
        public Syntax.InternalSyntax.GreenNode? Chord;
        public int ChordPosition;
        public int Octave;
    }

    /// <summary>Document-order threading — the same order as LP's fold (element
    /// before elements), which the syntax tree mirrors. A body-owning
    /// declaration (part / section / phrase / part cell) opens its OWN scope:
    /// the running chord does not leak across bodies, so a structural replay
    /// (~Main entered twice) resolves the same way on every walk.</summary>
    private static void Thread(Syntax.InternalSyntax.GreenNode node, int position, SyntaxNode root,
        Dictionary<ChordRepetitionSyntax, Resolved> map, ref Running running)
    {
        switch (node.Kind)
        {
            case SyntaxKind.Chord:
                // A written chord is the new origin, at its own octave.
                running.Chord = node;
                running.ChordPosition = position;
                running.Octave = 0;
                return; // a chord holds no chords
            case SyntaxKind.ChordRepetition:
                if (running.Chord != null)
                {
                    var q = (ChordRepetitionSyntax)BareDurations.RedOf(root, node, position);
                    // Displacement ACCUMULATES: each q repeats the chord as the last q
                    // left it, so q' q sounds up an octave twice.
                    running.Octave += q.OctaveOffset;
                    map[q] = new Resolved(
                        (ChordSyntax)BareDurations.RedOf(root, running.Chord, running.ChordPosition),
                        running.Octave);
                }
                return;
        }
        int at = position;
        for (int i = 0; i < node.SlotCount; i++)
        {
            var child = node.GetSlot(i);
            if (child is null)
                continue;
            int childPosition = at;
            at += child.FullWidth;
            if (child.IsToken)
                continue;
            if (IsScopeBoundary(child.Kind))
            {
                var inner = new Running();
                Thread(child, childPosition, root, map, ref inner);
            }
            else
                Thread(child, childPosition, root, map, ref running);
        }
    }

    private static bool IsScopeBoundary(SyntaxKind kind) => kind is
        SyntaxKind.PartDeclaration or SyntaxKind.SectionDeclaration or SyntaxKind.PhraseDeclaration
        or SyntaxKind.PartBlock or SyntaxKind.ChordPartBlock;
}
