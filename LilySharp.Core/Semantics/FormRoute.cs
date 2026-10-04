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

using System;
using System.Collections.Generic;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// THE ONE READER of a form's navigation marks as a ROUTE: the stretches of the form's items
/// a performer plays, in order, once the jump texts — <c>dc</c>, <c>ds</c>, <c>dc al fine</c>,
/// <c>ds al coda</c> and the rest — are followed. <see cref="FormWalk"/> classifies the
/// items; this reader says which of them sound, and when, for the walk that plays the form
/// (<c>MidiExporter.PlayForm</c>). The page, the LilyPond twin and the MusicXML export keep
/// writing the marks where they stand; only a PLAYER needs the route.
/// </summary>
/// <remarks>
/// <para>
/// The route is the ordinary reading of the signs (owner's decision 2026-10-02: the arbiter
/// of what a form MEANS is musical validity, LilyPond's the arbiter of the drawing):
/// <list type="bullet">
/// <item><c>dc</c> goes back to the beginning of the form, <c>ds</c> to the item after the
/// last <c>segno</c> the first pass has met. A <c>ds</c> with no <c>segno</c> before it has
/// nowhere to go and stays a mark on the page (the route does not guess the beginning) —
/// and is reported, LYS4025 (session 790; every fallback in this list is).</item>
/// <item>The replayed stretch ends at the jump text itself, or earlier: an <c>al fine</c>
/// stops at the first <c>fine</c> in it, an <c>al coda</c> at the first <c>to coda</c> in it
/// (neither found: the whole stretch, up to the jump — reported).</item>
/// <item>After an <c>al fine</c> the piece ENDS — nothing written after the jump text
/// sounds. After an <c>al coda</c> the first pass resumes at the item after the first
/// <c>coda</c> sign that follows the jump text (none: at the item after the jump —
/// reported). After a bare <c>dc</c> / <c>ds</c> it resumes at the item after the jump, as
/// it does after a one-sided <c>:|</c>.</item>
/// <item>On the replayed stretch a repeat block plays ONCE, on its last pass (body, then the
/// ending that pass names) — the performer's convention that repeats are not taken on the
/// D.C./D.S. pass; a one-sided <c>:|</c> rewinds nothing (one rewind per written sign —
/// <c>MidiExporter.PlayFormItem</c>'s rule); jump texts met inside it are not followed again
/// (a second <c>dc</c> inside the replay would not terminate).</item>
/// <item>The stretch the first pass resumes with is a first pass again: its repeats are
/// taken, its <c>segno</c> is remembered, and a later jump text is followed. A form with two
/// routes (<c>segno A ds al coda coda B segno C ds al fine</c>) reads as two.</item>
/// </list>
/// </para>
/// <para>
/// Only FORM-LEVEL marks route. A mark written inside a <c>|: … :|</c> block is a child of
/// the block's item (FormWalk classifies it there), and a mark written in a section's MUSIC
/// (<c>c4 d e f | ds al fine</c>) is a landmark at a bar line the music walk draws — neither
/// is a point the route can address, since the route addresses form items. A book that wants
/// its jump followed writes it in the form. No book on disk writes a jump inside a block or in
/// the music (2026-10-03: 8 forms with jump texts, all at form level; the inline marks on disk
/// are signs).
/// </para>
/// <para>
/// THE OTHER READERS OF A FORM'S PLAYED ORDER FOLLOW THE JUMPS THROUGH THIS CLASS (session 792;
/// the owner's choice among session 775's candidates): <c>Svg.Collector.PlayedOrder.Expand</c>
/// rebuilds the form's mark sequence from the marks stamped on the plays and takes the route
/// from <see cref="Of(IReadOnlyList{NavigationMarkType?}, Action{int, JumpFault})"/>, and the
/// page's tie carry, the MusicXML tie stops, the twin's <c>\repeatTie</c>s and the bar-complement
/// adjacency (<c>SectionBoundaryBars</c>) all read that one expansion — so a tie at the end of
/// the section before a <c>ds al coda</c> now reaches the segno's section, as the MIDI sustains
/// it. Until then they stopped at the jump texts. MusicXML still writes the jump itself as a
/// <c>&lt;sound&gt;</c> attribute and leaves following it to the importer.
/// </para>
/// <para>
/// LILYSHARP-OWN: LilyPond's <c>\jump</c> (lily/jump-engraver.cc) is a mark only — its MIDI
/// does not follow a <c>\jump "D.S. al Coda"</c>, and the twin writes the form's texts as
/// <c>\jump</c>, so a book with a jump sounds longer here than its LilyPond twin's MIDI.
/// LilyPond's spelling with a meaning is <c>\repeat segno</c>, which the twin does not write
/// (a form is a sequence of references, not a nested repeat). Departs from: nothing in LP
/// computes this. Goes away: never on its own; it is the language's reading of its own
/// signs. Observed by: <c>FormJumpMidiTests</c> (the route) and the MIDI they export.
/// </para>
/// </remarks>
internal static class FormRoute
{
    /// <summary>The form items <c>[From, To)</c> played in order. <paramref name="Replay"/> is
    /// true for the stretch a <c>dc</c> / <c>ds</c> plays again (repeats once, rewinds and jump
    /// texts silent); false for a first-pass stretch.</summary>
    internal readonly record struct Stretch(int From, int To, bool Replay);

    /// <summary>A jump text's reading: where it goes back to, what it stops at, whether the
    /// piece ends or goes on to the coda after it.</summary>
    private readonly record struct Jump(bool DaCapo, bool AlFine, bool AlCoda);

    /// <summary>A landmark a jump text asked for and the form does not write where the route
    /// looks for it — the four fallbacks the class remarks spell out, named so the writer is
    /// told (LYS4025, <see cref="FormJumpTargetValidator"/>).</summary>
    internal enum JumpFault
    {
        /// <summary>A <c>ds</c> with no <c>segno</c> before it: the jump is not followed.</summary>
        NoSegno,
        /// <summary>An <c>al fine</c> with no <c>fine</c> on the replayed stretch: the replay
        /// runs to the jump and the piece ends there.</summary>
        NoFine,
        /// <summary>An <c>al coda</c> with no <c>to coda</c> on the replayed stretch: the replay
        /// runs to the jump before going to the coda.</summary>
        NoToCoda,
        /// <summary>An <c>al coda</c> with no <c>coda</c> after the jump: the first pass resumes
        /// right after the jump.</summary>
        NoCoda,
    }

    /// <summary>A jump text and the landmark it lacks.</summary>
    internal readonly record struct Fault(NavigationMarkSyntax Jump, JumpFault Kind);

    /// <summary>The route through <paramref name="items"/> (a <see cref="FormWalk.Read"/>).
    /// A form with no jump text is one first-pass stretch over everything. When
    /// <paramref name="faults"/> is given, every landmark a jump text asked for and did not
    /// find is appended to it — read off THIS walk, so a report can never disagree with the
    /// route the MIDI plays.</summary>
    internal static IReadOnlyList<Stretch> Of(IReadOnlyList<FormWalk.Item> items, List<Fault>? faults = null)
    {
        var marks = new List<NavigationMarkType?>(items.Count);
        foreach (var item in items)
            marks.Add(MarkOf(item));
        return Of(marks, faults is null ? null : (i, kind) =>
        {
            if (items[i] is FormWalk.Other { Node: NavigationMarkSyntax nav })
                faults.Add(new Fault(nav, kind));
        });
    }

    /// <summary>The route through a sequence of positions of which some are the form's
    /// navigation marks (<paramref name="marks"/>[i], null for anything else — a section play,
    /// a repeat run, a one-sided <c>:|</c>). The spelling the page's stamps rebuild
    /// (<c>Svg.Collector.PlayedOrder</c>) and <see cref="FormWalk"/>'s items share: the ONE
    /// reading of the signs. <paramref name="report"/>, when given, is told each landmark a jump
    /// text asked for and did not find (the jump's index and the fault).</summary>
    internal static IReadOnlyList<Stretch> Of(IReadOnlyList<NavigationMarkType?> marks,
        Action<int, JumpFault>? report = null)
    {
        var route = new List<Stretch>();
        int n = marks.Count;
        int segno = -1; // the last segno the first pass met (its index)
        int start = 0;  // where the first-pass stretch being walked began
        int i = 0;
        while (i < n)
        {
            if (marks[i] == NavigationMarkType.Segno)
                segno = i;
            if (JumpOf(marks[i]) is not { } jump)
            {
                i++;
                continue;
            }
            // A `ds` with no segno before it: nowhere to go — the mark stays visual (and is
            // reported; the landmarks the `al` half asks for are moot without the jump).
            int from = jump.DaCapo ? 0 : segno >= 0 ? segno + 1 : -1;
            if (from < 0)
            {
                report?.Invoke(i, JumpFault.NoSegno);
                i++;
                continue;
            }

            Add(route, start, i, replay: false);

            int to = i;
            if (jump.AlFine)
            {
                if (IndexOf(marks, NavigationMarkType.Fine, from, i) is { } fine)
                    to = fine;
                else
                    report?.Invoke(i, JumpFault.NoFine);
            }
            else if (jump.AlCoda)
            {
                if (IndexOf(marks, NavigationMarkType.ToCoda, from, i) is { } toCoda)
                    to = toCoda;
                else
                    report?.Invoke(i, JumpFault.NoToCoda);
            }
            Add(route, from, to, replay: true);

            if (jump.AlFine)
                return route; // the piece ends at Fine (or at the jump, with no Fine to stop at)

            if (jump.AlCoda && IndexOf(marks, NavigationMarkType.Coda, i + 1, n) is { } coda)
                start = coda + 1;
            else
            {
                if (jump.AlCoda)
                    report?.Invoke(i, JumpFault.NoCoda);
                start = i + 1;
            }
            i = start;
        }
        Add(route, start, n, replay: false);
        return route;
    }

    /// <summary>Whether <paramref name="item"/> is the form-level <c>segno</c> sign — the point a
    /// player snapshots the state a <c>ds</c> will come back to.</summary>
    internal static bool IsSegno(FormWalk.Item item) => MarkOf(item) == NavigationMarkType.Segno;

    private static void Add(List<Stretch> route, int from, int to, bool replay)
    {
        if (from < to)
            route.Add(new Stretch(from, to, replay));
    }

    private static int? IndexOf(IReadOnlyList<NavigationMarkType?> marks, NavigationMarkType type, int from, int to)
    {
        for (int k = from; k < to; k++)
            if (marks[k] == type)
                return k;
        return null;
    }

    private static NavigationMarkType? MarkOf(FormWalk.Item item)
        => item is FormWalk.Other { Node: NavigationMarkSyntax nav } ? nav.MarkType : null;

    private static Jump? JumpOf(NavigationMarkType? mark) => mark switch
    {
        NavigationMarkType.DaCapo => new Jump(DaCapo: true, AlFine: false, AlCoda: false),
        NavigationMarkType.DaCapoAlFine => new Jump(DaCapo: true, AlFine: true, AlCoda: false),
        NavigationMarkType.DaCapoAlCoda => new Jump(DaCapo: true, AlFine: false, AlCoda: true),
        NavigationMarkType.DalSegno => new Jump(DaCapo: false, AlFine: false, AlCoda: false),
        NavigationMarkType.DalSegnoAlFine => new Jump(DaCapo: false, AlFine: true, AlCoda: false),
        NavigationMarkType.DalSegnoAlCoda => new Jump(DaCapo: false, AlFine: false, AlCoda: true),
        _ => null,
    };
}
