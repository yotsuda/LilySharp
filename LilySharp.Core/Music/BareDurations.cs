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
    private readonly record struct Resolution(SyntaxNode Original, bool CrossesBarline);

    // One map per red tree root, built on first query and dropped with the tree.
    private static readonly ConditionalWeakTable<SyntaxNode, Dictionary<BareDurationSyntax, Resolution>> Maps = new();

    /// <summary>The event a bare duration repeats — a <see cref="NoteSyntax"/>,
    /// <see cref="ChordSyntax"/>, <see cref="SlashNoteSyntax"/> or
    /// <see cref="DrumNoteSyntax"/> — or null when nothing precedes it in its
    /// top-level body.</summary>
    public static SyntaxNode? OriginalOf(BareDurationSyntax bare)
        => MapFor(bare).TryGetValue(bare, out var r) ? r.Original : null;

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

    private static Dictionary<BareDurationSyntax, Resolution> BuildMap(SyntaxNode root)
    {
        var map = new Dictionary<BareDurationSyntax, Resolution>();
        SyntaxNode? last = null;
        bool crossed = false;
        Thread(root, map, ref last, ref crossed);
        return map;
    }

    /// <summary>Document-order threading, mirroring
    /// <see cref="ChordRepetitions"/>. Every repeatable event replaces the
    /// running one; a repetition spelling (<c>q</c>, a bare duration) threads
    /// as what IT resolves to, never as itself, so chains stay flat and a later
    /// bare duration copies the true original. <paramref name="crossed"/>
    /// tracks the OTHER distance — barlines since the last WRITTEN repeatable
    /// spelling (every written event clears it, a bare duration included, even
    /// though the map threads flat past it) — so the crossing warning fires on
    /// the bare duration that opens a measure's run, once.</summary>
    private static void Thread(SyntaxNode node, Dictionary<BareDurationSyntax, Resolution> map,
        ref SyntaxNode? last, ref bool crossed)
    {
        switch (node)
        {
            case BarlineSyntax:
                crossed = true;
                return;
            // A pitched rest (`a4@rest`) is a REST that borrows a pitch for its
            // height — transparent like `r4`, never a repeat target (nothing
            // records a spelling for it; nothing sounds).
            case NoteSyntax n:
                if (!Semantics.PitchedRest.Is(n))
                {
                    last = n;
                    crossed = false;
                }
                return;
            // The empty chord <> is a post-event carrier occupying no time —
            // transparent here for the same reason it is transparent to the bar.
            case ChordSyntax c:
                if (!c.IsEmpty)
                {
                    last = c;
                    crossed = false;
                }
                return;
            case SlashNoteSyntax or DrumNoteSyntax:
                last = node;
                crossed = false;
                return; // an event holds no further events
            case ChordRepetitionSyntax q:
                if (ChordRepetitions.OriginalOf(q) is { } chord)
                    last = chord;
                crossed = false; // written either way; an unresolved q errors on its own
                return;
            // An arpeggio BREAKS the run rather than becoming its target: its
            // members play in sequence and subdivide a group total, so "repeat
            // it with a new duration" has no single answer yet. Clearing the
            // running event makes a bare duration after one a LOUD error
            // (LYS0016) instead of a silent repeat of an inner member the
            // document-order descent would otherwise pick up.
            case ArpeggioSyntax:
                last = null;
                crossed = false;
                return;
            case BareDurationSyntax bare:
                if (last != null)
                    map[bare] = new Resolution(last, crossed);
                crossed = false;
                return;
        }
        for (int i = 0; i < node.SlotCount; i++)
        {
            if (node.GetChild(i) is not { } child || child is SyntaxTokenNode)
                continue;
            if (IsScopeBoundary(child))
            {
                SyntaxNode? inner = null;
                bool innerCrossed = false;
                Thread(child, map, ref inner, ref innerCrossed);
            }
            else
            {
                Thread(child, map, ref last, ref crossed);
            }
        }
    }

    private static bool IsScopeBoundary(SyntaxNode n) => n is
        PartDeclarationSyntax or SectionDeclarationSyntax or PhraseDeclarationSyntax
        or PartBlockSyntax or ChordPartBlockSyntax;
}
