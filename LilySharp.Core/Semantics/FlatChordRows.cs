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
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// The chord tracks written flat at the top level of a file (<c>chords prog { C | G7 | }</c>, valid where
/// the file is not grouped by part: <see cref="TrackNeedsSectionsValidator"/>). The page runs one from the
/// first measure, its bar N over the score's measure N (<c>ChordNameCollector.BlockStarts</c>). The readers
/// that play the form section by section take its bars the same way: each section the bars it prints,
/// under the meter, the pickup and the key that section opens with, as a row written in the section.
/// </summary>
internal sealed class FlatChordRows
{
    private readonly List<(string Row, List<List<SyntaxNode>> Bars)> _rows;

    /// <summary>No tracks: the state before a file is read.</summary>
    public static FlatChordRows None => new([]);

    private FlatChordRows(List<(string, List<List<SyntaxNode>>)> rows) => _rows = rows;

    /// <summary>The bar the next section takes. A form that plays a printed bar again sets it back: the
    /// bars of a repeat are printed once, and so are read once for every pass.</summary>
    public int Cursor { get; set; }

    /// <summary>The flat tracks of the file that <paramref name="placed"/> names.</summary>
    public static FlatChordRows Of(SyntaxNode root, IReadOnlySet<string> placed)
        => Of(root.ChildNodes().OfType<ChordPartBlockSyntax>().Where(b => !b.HasSections && b.PartName is { } row && placed.Contains(row)));

    /// <summary>These flat tracks.</summary>
    public static FlatChordRows Of(IEnumerable<ChordPartBlockSyntax> blocks)
        => new([.. blocks.Select(b => (b.PartName ?? "", BarsOf(b.Items)))]);

    /// <summary>How many bars the longest track has left.</summary>
    public int BarsLeft => _rows.Count == 0 ? 0 : System.Math.Max(0, _rows.Max(r => r.Bars.Count) - Cursor);

    /// <summary>The next <paramref name="bars"/> bars of each track, as items of a row, and moves on.</summary>
    public IEnumerable<(string Row, List<SyntaxNode> Items)> Take(int bars)
    {
        int from = Cursor;
        Cursor += bars;
        return _rows.Select(r => (r.Row, r.Bars.Skip(from).Take(bars).SelectMany(b => b).ToList())).Where(r => r.Item2.Count > 0);
    }

    /// <summary>What is left of each track past the music.</summary>
    public IEnumerable<(string Row, List<SyntaxNode> Items)> Rest() => Take(int.MaxValue - Cursor);

    // Split as a row is read (ChordRowBarLines): a bar keeps the bar line that closes it.
    private static List<List<SyntaxNode>> BarsOf(IEnumerable<SyntaxNode> items)
    {
        // A break directive (owner's decision 2026-11-13) rides with the bar it stands after and is
        // never a bar of its own: after the bar's chords it stays in that bar, before its bar line;
        // with nothing of its bar before it, it follows the bar line of the bar before.
        var bars = new List<List<SyntaxNode>>();
        var bar = new List<SyntaxNode>();
        int held = 0;       // what the open bar holds, its break directives aside
        foreach (var item in items)
        {
            if (item is BarlineSyntax barline)
            {
                bar.Add(barline);
                if (ChordRowBarLines.Closes(barline, held > 0))
                {
                    bars.Add(bar);
                    bar = [];
                    held = 0;
                }
                else
                    held++;
                continue;
            }
            if (item is ChordEntrySyntax or RestSyntax or ChordExtendSyntax or PercentMarkSyntax)
            {
                bar.Add(item);
                held++;
            }
            else if (item is BreakSyntax)
                (held == 0 && bars.Count > 0 ? bars[^1] : bar).Add(item);
            // An ending's marks and a navigation mark (docs/inline-repeat-design.md) ride with
            // their bar as a break does: an ending opens the bar ahead, anything else written
            // right after a bar line stays with the bar behind it — the same tick either way.
            else if (item is InlineVoltaSyntax)
                bar.Add(item);
            else if (item is VoltaEndSyntax end && ChordRowBarLines.EndCloses(end, held > 0))
            {
                bar.Add(end);
                bars.Add(bar);
                bar = [];
                held = 0;
            }
            else if (item is VoltaEndSyntax or NavigationMarkSyntax)
                (held == 0 && bars.Count > 0 ? bars[^1] : bar).Add(item);
        }
        if (held > 0)
            bars.Add(bar);
        return bars;
    }
}

/// <summary>
/// THE ONE READER of what a bar line of a CHORDS ROW closes — the rule the page's three walks
/// of a row (<c>ChordNameCollector</c>'s <c>CountBars</c>, <c>ProcessRun</c>, the diagram run and
/// the aligned row), the MIDI (<c>MidiExporter.PlayChordRow</c>), <see cref="FlatChordRows"/>,
/// the MusicXML (<c>RowBars</c>) and the LilyPond twin (<c>ChordBarsOf</c>) each spelled for
/// themselves until 2026-11-14.
/// </summary>
/// <remarks>
/// <para>
/// A written bar line closes a bar, an EMPTY one when nothing stands before it in its bar —
/// the bare-barline rule a part's music has (<c>MeasureBuilder.HandleBarline</c>). A <c>|:</c>
/// is no exception (owner's decision 2026-11-14: "<c>C | |: G</c> makes an empty bar, since
/// <c>C | | G</c> does"; the music's own rule since 2026-08-28), but for the one place it
/// differs from <c>|</c> in the music too: a <c>|:</c> that OPENS the row closes nothing — it
/// opens the bar in front of it and there is no span behind it to leave unowned
/// (<c>MeasureBuilder._atScopeStart</c>).
/// </para>
/// <para>
/// MEASURED before (Lab sessions/p1243): the spellings had drifted two ways. Five read a
/// <c>|:</c> as closing only a bar that held something (<c>C | |: G</c> two bars), the count,
/// the aligned row and the twin read every bar line as closing one (three bars — and
/// <c>|: C | G :|</c> three, an empty bar before the row began). A row with a repeat sign was
/// LYS1034 from 2026-08-31, so no book showed it.
/// </para>
/// <para>
/// The row's start is read off the TREE — no entry and no bar line before this one in the row
/// it is written in (a track's section, or a flat track's block) — so a reader that walks a
/// slice of a row (<see cref="FlatChordRows.Take"/>) answers as one that walks all of it.
/// </para>
/// </remarks>
internal static class ChordRowBarLines
{
    /// <summary>
    /// The endings of a row that follow another ending with no <c>:|</c> between them, in a
    /// repeat that has gone back already — <c>|: C | [1. G :| [2. Am ] | [3. F ] |</c>, where
    /// <c>[3.</c> is one: a further ending is a further pass (owner's decision 2026-11-15), so
    /// the ending before it goes back although no sign says so, and its last bar takes the
    /// repeat bar on the page and in MusicXML. The row's reading of
    /// <c>SyntaxFacts.EndingGoesBackUnsaid</c> (a part's endings hold their music; a row's
    /// stand flat among its chords).
    /// </summary>
    public static HashSet<InlineVoltaSyntax> EndingsAfterAnUnsaidRepeat(IReadOnlyList<SyntaxNode> items)
    {
        var found = new HashSet<InlineVoltaSyntax>(ReferenceEqualityComparer.Instance);
        bool goneBack = false;     // the run has met a ':|'
        bool inEnding = false;     // an ending is open
        bool closedPlain = false;  // …or one was closed by its ']' and nothing but bar lines followed
        foreach (var item in items)
        {
            switch (item)
            {
                case BarlineSyntax line:
                    var kind = line.BarToken.Kind;
                    if (kind == SyntaxKind.RepeatEndBar)
                        (goneBack, inEnding, closedPlain) = (true, false, false);
                    else if (kind is SyntaxKind.RepeatStartBar or SyntaxKind.RepeatBothBar)
                        (goneBack, inEnding, closedPlain) = (false, false, false);
                    break;
                case ChordEntrySyntax or RestSyntax or ChordExtendSyntax or PercentMarkSyntax:
                    // Chords after the endings: the run is over.
                    if (!inEnding)
                        (goneBack, closedPlain) = (false, false);
                    break;
                case InlineVoltaSyntax ending:
                    if (goneBack && (inEnding || closedPlain))
                        found.Add(ending);
                    (inEnding, closedPlain) = (true, false);
                    break;
                case VoltaEndSyntax:
                    if (inEnding)
                        (inEnding, closedPlain) = (false, true);
                    break;
            }
        }
        return found;
    }

    /// <summary>
    /// Whether an ending's <paramref name="end"/> (<c>]</c>) closes the bar it stands in, as a bar
    /// line would, given whether that bar <paramref name="holds"/> an entry already: when no bar
    /// line follows it — <c>| A | [1. A7] [2. ]</c> is A, A7, and an empty bar, where the bar line
    /// is left to the brackets — and the ending has a bar to close: one it holds, or none at all
    /// yet (<c>[2. ]</c>, an empty bar). <c>[2. F ] |</c> is the bar line's, as it always was.
    /// </summary>
    /// <remarks>Until 2026-11-15 a <c>]</c> closed nothing, so <c>[1. A7] [2. ]</c> stacked both
    /// brackets over A7's bar and the second ending had no bar (owner's report).</remarks>
    public static bool EndCloses(VoltaEndSyntax end, bool holds)
    {
        if (end.Parent is not { } row)
            return false;
        var items = new List<SyntaxNode>();
        for (int i = 0; i < row.SlotCount; i++)
            if (row.GetChild(i) is { } child and not SyntaxTokenNode)
                items.Add(child);
        int at = items.FindIndex(n => ReferenceEquals(n, end));
        if (at < 0)
            return false;
        for (int k = at + 1; k < items.Count; k++)
        {
            if (items[k] is BarlineSyntax)
                return false;
            if (items[k] is not BreakSyntax)
                break;
        }
        if (holds)
            return true;
        // An empty ending: nothing written since its `[N.` — no bar line closed a bar of it.
        for (int k = at - 1; k >= 0; k--)
        {
            if (items[k] is InlineVoltaSyntax)
                return true;
            if (items[k] is BarlineSyntax or ChordEntrySyntax or RestSyntax or ChordExtendSyntax or PercentMarkSyntax)
                return false;
        }
        return false;
    }

    /// <summary>
    /// The tonic step (0=C..6=B) and signature a row's <c>key</c> sets (owner's decision 2026-11-16:
    /// a row's <c>key</c> is that row's alone, as a part's is — the key its degrees are read in
    /// from that bar). Null for a key with no tonic (<c>key custom</c>, <c>key none</c>).
    /// </summary>
    public static (int TonicStep, int Sharps)? KeyOf(KeySignatureSyntax key)
    {
        if (key.IsCustom)
            return null;
        return (Math.Max(0, Music.KeySpelling.StepOf(key.Pitch.BaseName)),
            Music.KeySpelling.SharpsFor(key.Pitch.ToFullString().Trim().ToLowerInvariant(),
                key.Mode.Text.ToLowerInvariant()) ?? 0);
    }

    /// <summary>The <c>key</c>s a row writes at a bar's head, each with the bar of the row it
    /// opens — counted as every reader of a row's bars counts them.</summary>
    public static List<(int Bar, KeySignatureSyntax Key)> KeysAtBars(IEnumerable<SyntaxNode> items)
    {
        var found = new List<(int, KeySignatureSyntax)>();
        int bars = 0;
        bool holds = false;
        foreach (var item in items)
        {
            if (item is BarlineSyntax line)
            {
                if (Closes(line, holds))
                    bars++;
                holds = false;
            }
            else if (item is VoltaEndSyntax end && EndCloses(end, holds))
                (bars, holds) = (bars + 1, false);
            else if (item is ChordEntrySyntax or RestSyntax or ChordExtendSyntax or PercentMarkSyntax)
                holds = true;
            else if (item is KeySignatureSyntax key && !holds)
                found.Add((bars, key));
        }
        return found;
    }

    /// <summary>Whether <paramref name="barLine"/> closes a bar of its row, given whether the
    /// bar being read <paramref name="holds"/> an entry, a rest or a <c>.</c> already.</summary>
    public static bool Closes(BarlineSyntax barLine, bool holds)
    {
        if (holds || barLine.BarToken.Kind != SyntaxKind.RepeatStartBar)
            return true;
        if (barLine.Parent is not { } row)
            return true;
        for (int i = 0; i < row.SlotCount; i++)
        {
            var child = row.GetChild(i);
            if (child == null)
                continue; // an optional slot the row does not write
            if (ReferenceEquals(child, barLine) || child.Position >= barLine.Position)
                break;
            if (child is BarlineSyntax or ChordEntrySyntax or RestSyntax or ChordExtendSyntax or PercentMarkSyntax)
                return true;
        }
        return false;
    }
}
