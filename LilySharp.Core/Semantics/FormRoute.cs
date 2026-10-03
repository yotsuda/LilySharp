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
/// nowhere to go and stays a mark on the page (the route does not guess the beginning).</item>
/// <item>The replayed stretch ends at the jump text itself, or earlier: an <c>al fine</c>
/// stops at the first <c>fine</c> in it, an <c>al coda</c> at the first <c>to coda</c> in it
/// (neither found: the whole stretch, up to the jump).</item>
/// <item>After an <c>al fine</c> the piece ENDS — nothing written after the jump text
/// sounds. After an <c>al coda</c> the first pass resumes at the item after the first
/// <c>coda</c> sign that follows the jump text (none: at the item after the jump). After a
/// bare <c>dc</c> / <c>ds</c> it resumes at the item after the jump, as it does after a
/// one-sided <c>:|</c>.</item>
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
/// ⚠️ THREE OTHER READERS EXPAND A FORM'S PLAYED ORDER AND DO NOT FOLLOW THE JUMPS — the
/// page's tie carry (<c>SectionPlayCarry.PlayedOrder</c>, built from the stamped plays), the
/// bar-complement adjacency (<c>SectionBoundaryBars.Expand</c>) and the MusicXML export's
/// <c>_xmlRewind</c> (which writes the jump as a <c>&lt;sound&gt;</c> attribute and leaves
/// following it to the importer). They are named here so the next reader to need the route
/// takes this one rather than spelling a fourth; whether a tie carried over a jump text
/// should reach the segno's section is an open question for the owner, not an oversight.
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

    /// <summary>The route through <paramref name="items"/> (a <see cref="FormWalk.Read"/>).
    /// A form with no jump text is one first-pass stretch over everything.</summary>
    internal static IReadOnlyList<Stretch> Of(IReadOnlyList<FormWalk.Item> items)
    {
        var route = new List<Stretch>();
        int n = items.Count;
        int segno = -1; // the last segno the first pass met (its index)
        int start = 0;  // where the first-pass stretch being walked began
        int i = 0;
        while (i < n)
        {
            if (MarkOf(items[i]) == NavigationMarkType.Segno)
                segno = i;
            if (JumpOf(items[i]) is not { } jump)
            {
                i++;
                continue;
            }
            // A `ds` with no segno before it: nowhere to go — the mark stays visual.
            int from = jump.DaCapo ? 0 : segno >= 0 ? segno + 1 : -1;
            if (from < 0)
            {
                i++;
                continue;
            }

            Add(route, start, i, replay: false);

            int to = i;
            if (jump.AlFine && IndexOf(items, NavigationMarkType.Fine, from, i) is { } fine)
                to = fine;
            else if (jump.AlCoda && IndexOf(items, NavigationMarkType.ToCoda, from, i) is { } toCoda)
                to = toCoda;
            Add(route, from, to, replay: true);

            if (jump.AlFine)
                return route; // the piece ends at Fine (or at the jump, with no Fine to stop at)

            start = jump.AlCoda && IndexOf(items, NavigationMarkType.Coda, i + 1, n) is { } coda
                ? coda + 1
                : i + 1;
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

    private static int? IndexOf(IReadOnlyList<FormWalk.Item> items, NavigationMarkType type, int from, int to)
    {
        for (int k = from; k < to; k++)
            if (MarkOf(items[k]) == type)
                return k;
        return null;
    }

    private static NavigationMarkType? MarkOf(FormWalk.Item item)
        => item is FormWalk.Other { Node: NavigationMarkSyntax nav } ? nav.MarkType : null;

    private static Jump? JumpOf(FormWalk.Item item) => MarkOf(item) switch
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
