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
/// The shared bare-duration resolver: ONE document-order walk per tree maps
/// every bare duration (<c>c4 4</c>) to the event it repeats — a note, chord,
/// slash or drum note — and every walker (collector, exporters, validators)
/// reads that map instead of tracking its own last-event. Rests are
/// transparent; a <c>q</c> threads as the chord it itself resolves to, so
/// <c>&lt;c e g&gt;4 q 4</c> repeats the chord; a bare duration threads as its
/// own target, so <c>c4 4 4</c> chains flat. A bare duration with no event
/// before it in its top-level body resolves to nothing (the validator reports
/// it, LYS0016).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/parser.yy music_embedded — "duration post_events" builds
/// a NoteEvent with a duration and NO pitch; the pitch used when typeset is
/// the preceding note's or chord's. Behaviour pinned against 2.26.0
/// (2026-08-19): <c>{ c'4 4 &lt;d' f'&gt;4 4 q4 }</c> and
/// <c>{ c'4 r4 4 8 8 }</c> are byte-identical to their explicit spellings —
/// a bare duration repeats the pitch of a note, the FULL chord of a chord,
/// and reads through intervening rests. Same scoping as
/// <see cref="ChordRepetitions"/>: each body is its own walk, so a structural
/// replay resolves the same way every time.
/// </remarks>
public static class BareDurations
{
    // The resolved target, plus whether a barline stands between the bare
    // duration and its lexically nearest WRITTEN event (which for a chain is
    // the previous bare duration, not the flat-threaded original).
    // Octave is the displacement the running event carried when this bare duration
    // copied it: 0 after a written note or chord, and the q chain's running total
    // after a `q'`. Without it `<c e g>4 q' 4` would repeat the WRITTEN chord while
    // `<c e g>4 q' q` repeats the displaced one — the same question, two answers.
    private readonly record struct Resolution(SyntaxNode Original, bool CrossesBarline, int Octave);

    // One map per red tree root, built on first query and dropped with the tree.
    private static readonly ConditionalWeakTable<SyntaxNode, Dictionary<BareDurationSyntax, Resolution>> Maps = new();

    /// <summary>The event a bare duration repeats — a <see cref="NoteSyntax"/>,
    /// <see cref="ChordSyntax"/>, <see cref="SlashNoteSyntax"/> or
    /// <see cref="DrumNoteSyntax"/> — or null when nothing precedes it in its
    /// top-level body.</summary>
    public static SyntaxNode? OriginalOf(BareDurationSyntax bare)
        => MapFor(bare).TryGetValue(bare, out var r) ? r.Original : null;

    /// <summary>
    /// The octaves the repeated event was displaced by — 0 unless the run reached
    /// this bare duration through a displaced <c>q</c>. <c>&lt;c e g&gt;4 q' 4</c>
    /// repeats the chord an octave up, the same as <c>q' q</c> does.
    /// </summary>
    public static int DisplacementOf(BareDurationSyntax bare)
        => MapFor(bare).TryGetValue(bare, out var r) ? r.Octave : 0;

    // The map's original set, for O(1) IsOriginal membership — derived from the
    // same build, one per tree, dropped with it.
    private static readonly ConditionalWeakTable<SyntaxNode, HashSet<SyntaxNode>> Originals = new();

    /// <summary>Whether <paramref name="node"/> is the original some bare duration
    /// in its tree copies — the collect-resume recorder's filter for which resolved
    /// spellings are worth logging (finding 3-4; a book without bare durations logs
    /// nothing). The reverse set is the map's originals, built once per tree.</summary>
    public static bool IsOriginal(SyntaxNode node)
    {
        var top = node;
        while (top.Parent != null)
            top = top.Parent;
        return OriginalsOf(top).Contains(node);
    }

    /// <summary>Every event some bare duration of the tree rooted at
    /// <paramref name="root"/> copies — the set <see cref="IsOriginal"/> answers from. The
    /// collect-resume planner compares two trees' sets (CollectResumePlanner.NewOriginalFloor).</summary>
    internal static IReadOnlySet<SyntaxNode> OriginalsOf(SyntaxNode root)
        => Originals.GetValue(root, r =>
        {
            var s = new HashSet<SyntaxNode>();
            foreach (var res in Maps.GetValue(r, BuildMap).Values)
                s.Add(res.Original);
            return s;
        });

    /// <summary>True when a barline stands between this bare duration and the
    /// nearest WRITTEN repeatable spelling before it — the shape a dropped
    /// pitch letter takes (<c>4 g f e</c> meant as <c>a4 g f e</c>), which the
    /// validator warns about (LYS1031). Within a measure (<c>bes8 8 8 8</c>)
    /// this is false, and a chain after a crossing is anchored by the first
    /// bare duration, so the crossing is reported once.</summary>
    public static bool CrossesBarline(BareDurationSyntax bare)
        => MapFor(bare).TryGetValue(bare, out var r) && r.CrossesBarline;

    private static Dictionary<BareDurationSyntax, Resolution> MapFor(BareDurationSyntax bare)
    {
        var top = (SyntaxNode)bare;
        while (top.Parent != null)
            top = top.Parent;
        return Maps.GetValue(top, BuildMap);
    }

    /// <summary>
    /// The map, from ONE walk of the tree's GREEN nodes: a red is materialised only for a
    /// bare duration that is found and for the event it repeats — by position, from the
    /// root, the same parent-cached instances every other reader of the tree holds.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS WALKED THE RED TREE UNTIL SESSION 519, and it is asked on every keystroke
    /// (<see cref="Maps"/> is keyed by the root, and the preview's root is new every
    /// keystroke — RULES §5.3's per-tree-is-per-keystroke shape), for every book, by
    /// <see cref="IsOriginal"/> from the collector's first note. MEASURED (Release, the
    /// owner's corpus, 232 books × eight forward keystrokes, allocated bytes around this
    /// build): 1.00 builds a keystroke, 43,399 B a keystroke, 3.0% of the render — and the
    /// map came out EMPTY, because the corpus writes no bare duration: a whole-tree red
    /// materialisation (every note, its duration, every articulation
    /// <see cref="Semantics.PitchedRest"/> looked at, every token skipped) for nothing. The
    /// resumed collect never touches the adopted prefix's reds, so nobody read them after.
    /// <para>
    /// The threading is the same fold in the same order — a green slot carries its kind, its
    /// full width and its children, which is everything the fold reads except the identity
    /// of the resolved nodes, and those are recovered by <see cref="RedOf"/>.
    /// <c>BareDurationsGreenWalkTests</c> holds this walk to the former red one, node for
    /// node, on every net book and on a book that spells every arm.
    /// </para>
    /// </remarks>
    private static Dictionary<BareDurationSyntax, Resolution> BuildMap(SyntaxNode root)
    {
        var map = new Dictionary<BareDurationSyntax, Resolution>();
        var run = new Running();
        Thread(root.Green, root.Position, root, map, ref run);
        return map;
    }

    /// <summary>The fold's running state: the event in force (as its green node and its
    /// full start, so its red can be found only when a bare duration needs it), the
    /// barline-crossed flag and the q chain's displacement.</summary>
    private struct Running
    {
        public Syntax.InternalSyntax.GreenNode? Last;
        public int LastPosition;
        public bool Crossed;
        public int Octave;
    }

    /// <summary>Document-order threading, mirroring
    /// <see cref="ChordRepetitions"/>. Every repeatable event replaces the
    /// running one; a repetition spelling (<c>q</c>, a bare duration) threads
    /// as what IT resolves to, never as itself, so chains stay flat and a later
    /// bare duration copies the true original. <c>Crossed</c>
    /// tracks the OTHER distance — barlines since the last WRITTEN repeatable
    /// spelling (every written event clears it, a bare duration included, even
    /// though the map threads flat past it) — so the crossing warning fires on
    /// the bare duration that opens a measure's run, once.</summary>
    private static void Thread(Syntax.InternalSyntax.GreenNode node, int position, SyntaxNode root,
        Dictionary<BareDurationSyntax, Resolution> map, ref Running run)
    {
        switch (node.Kind)
        {
            case SyntaxKind.Barline:
                run.Crossed = true;
                return;
            // A pitched rest (`a4@rest`) is a REST that borrows a pitch for its
            // height — transparent like `r4`, never a repeat target (nothing
            // records a spelling for it; nothing sounds).
            case SyntaxKind.Note:
                if (!Semantics.PitchedRest.Is(node))
                {
                    run.Last = node;
                    run.LastPosition = position;
                    run.Crossed = false;
                    run.Octave = 0;   // a WRITTEN event is at its own octave
                }
                return;
            // The empty chord <> is a post-event carrier occupying no time —
            // transparent here for the same reason it is transparent to the bar.
            case SyntaxKind.Chord:
                if (!ChordIsEmpty(node))
                {
                    run.Last = node;
                    run.LastPosition = position;
                    run.Crossed = false;
                    run.Octave = 0;
                }
                return;
            case SyntaxKind.SlashNote or SyntaxKind.DrumNote:
                run.Last = node;
                run.LastPosition = position;
                run.Crossed = false;
                run.Octave = 0;
                return; // an event holds no further events
            case SyntaxKind.ChordRepetition:
                if (ChordRepetitions.OriginalOf((ChordRepetitionSyntax)RedOf(root, node, position))
                    is { } chord)
                {
                    run.Last = chord.Green;
                    run.LastPosition = chord.Position;
                    // The q threads flat to the written chord, so the displacement it
                    // resolved to has to travel with it or a following bare duration
                    // would repeat the chord back at its written octave.
                    run.Octave = ChordRepetitions.DisplacementOf(
                        (ChordRepetitionSyntax)RedOf(root, node, position));
                }
                run.Crossed = false; // written either way; an unresolved q errors on its own
                return;
            // An arpeggio BREAKS the run rather than becoming its target: its
            // members play in sequence and subdivide a group total, so "repeat
            // it with a new duration" has no single answer yet. Clearing the
            // running event makes a bare duration after one a LOUD error
            // (LYS0016) instead of a silent repeat of an inner member the
            // document-order descent would otherwise pick up.
            case SyntaxKind.Arpeggio:
                run.Last = null;
                run.Crossed = false;
                run.Octave = 0;
                return;
            case SyntaxKind.BareDuration:
                if (run.Last != null)
                    map[(BareDurationSyntax)RedOf(root, node, position)] = new Resolution(
                        RedOf(root, run.Last, run.LastPosition), run.Crossed, run.Octave);
                run.Crossed = false;
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
            {
                Thread(child, childPosition, root, map, ref run);
            }
        }
    }

    /// <summary><see cref="ChordSyntax.IsEmpty"/> on the green: no pitch, degree or drum
    /// member among the slots.</summary>
    private static bool ChordIsEmpty(Syntax.InternalSyntax.GreenNode chord)
    {
        for (int i = 0; i < chord.SlotCount; i++)
            if (chord.GetSlot(i) is { Kind: SyntaxKind.Pitch or SyntaxKind.ChordDegree or SyntaxKind.DrumNote })
                return false;
        return true;
    }

    /// <summary>
    /// The red node of <paramref name="green"/>, which starts at full position
    /// <paramref name="position"/> under <paramref name="root"/>: the descent from the root
    /// through the one child slot whose full span holds the position, materialising only
    /// that spine — the parent-cached instances, so the answer is the same object every
    /// other reader of the tree holds.
    /// </summary>
    private static SyntaxNode RedOf(SyntaxNode root, Syntax.InternalSyntax.GreenNode green, int position)
    {
        var node = root;
        int nodePosition = root.Position;
        while (!ReferenceEquals(node.Green, green))
        {
            SyntaxNode? next = null;
            int at = nodePosition;
            var parent = node.Green;
            for (int i = 0; i < parent.SlotCount; i++)
            {
                var slot = parent.GetSlot(i);
                if (slot is null)
                    continue;
                if (position >= at && position < at + slot.FullWidth)
                {
                    next = node.GetChild(i);
                    nodePosition = at;
                    break;
                }
                at += slot.FullWidth;
            }
            if (next is null)
                throw new InvalidOperationException(
                    "BareDurations: a green site of the walk is not under the root it was walked from");
            node = next;
        }
        return node;
    }

    private static bool IsScopeBoundary(SyntaxKind kind) => kind is
        SyntaxKind.PartDeclaration or SyntaxKind.SectionDeclaration or SyntaxKind.PhraseDeclaration
        or SyntaxKind.PartBlock or SyntaxKind.ChordPartBlock;
}
