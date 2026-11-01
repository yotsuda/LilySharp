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
    {
        var rows = new List<(string, List<List<SyntaxNode>>)>();
        foreach (var block in root.ChildNodes().OfType<ChordPartBlockSyntax>())
            if (!block.HasSections && block.PartName is { } row && placed.Contains(row))
                rows.Add((row, BarsOf(block.Items)));
        return new FlatChordRows(rows);
    }

    /// <summary>The next <paramref name="bars"/> bars of each track, as items of a row, and moves on.</summary>
    public IEnumerable<(string Row, List<SyntaxNode> Items)> Take(int bars)
    {
        int from = Cursor;
        Cursor += bars;
        return _rows.Select(r => (r.Row, r.Bars.Skip(from).Take(bars).SelectMany(b => b).ToList())).Where(r => r.Item2.Count > 0);
    }

    /// <summary>What is left of each track past the music.</summary>
    public IEnumerable<(string Row, List<SyntaxNode> Items)> Rest() => Take(int.MaxValue - Cursor);

    // Split as a row is read (MidiExporter.PlayChordRow): every bar line closes a bar, a `|` opening the
    // run closing an empty one, except `|:`, which closes only what is pending. A bar keeps its bar line.
    private static List<List<SyntaxNode>> BarsOf(IEnumerable<SyntaxNode> items)
    {
        var bars = new List<List<SyntaxNode>>();
        var bar = new List<SyntaxNode>();
        foreach (var item in items)
        {
            if (item is BarlineSyntax barline)
            {
                bar.Add(barline);
                if (Svg.Collector.MeasureCollector.ParseBarlineType(barline.BarText) != Svg.Model.BarlineType.RepeatStart || bar.Count > 1)
                {
                    bars.Add(bar);
                    bar = [];
                }
                continue;
            }
            if (item is ChordEntrySyntax or RestSyntax or ChordExtendSyntax)
                bar.Add(item);
        }
        if (bar.Count > 0)
            bars.Add(bar);
        return bars;
    }
}
