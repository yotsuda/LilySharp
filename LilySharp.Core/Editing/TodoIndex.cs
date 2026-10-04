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
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// One <c>@todo</c> mark of a document and where the score plays it.
/// </summary>
/// <param name="Key">The mark's key, or null.</param>
/// <param name="Memo">The mark's memo, or null.</param>
/// <param name="Start">Where the <c>@todo</c> annotation starts (its <c>@</c>).</param>
/// <param name="End">Where the annotation ends.</param>
/// <param name="HostStart">Where the note, rest or chord carrying it starts.</param>
/// <param name="HostEnd">Where that host ends (its annotations included).</param>
/// <param name="Measure">The bar number the score prints for the bar holding the host, or
/// null when no score plays it (a phrase no part uses).</param>
/// <param name="Part">The part whose staff draws the host, or null with <paramref name="Measure"/>.</param>
public sealed record TodoEntry(
    string? Key, string? Memo, int Start, int End, int HostStart, int HostEnd, int? Measure, string? Part);

/// <summary>Where the score draws one item written at <see cref="Position"/>: the bar number the
/// page prints and the part whose staff it is on.</summary>
public readonly record struct ItemPlace(int Position, int Measure, string Part);

/// <summary>
/// Where the score draws what a document writes (LilySharp-Omr proposal C1: which bar of the
/// page a mark, or the caret, is in): every item's printed
/// bar and part (<see cref="Places"/>), the bar at a caret (<see cref="PlaceAt"/>), and the
/// <c>@todo</c> marks located (<see cref="Find"/>).
/// </summary>
/// <remarks>
/// The bar number is the one the page PRINTS — <see cref="BarNumberEngraver.NumberMeasures"/>
/// over the measures that drive the layout, with a leading pickup as bar 0 — so a reader of
/// the page and of this answer count alike. A phrase played twice is in two bars; the first
/// is answered. The scores are collected the way the render path collects them
/// (<c>SvgGenerator.CollectScore</c>), and the first score that plays an item decides.
/// </remarks>
public static class TodoIndex
{
    /// <summary>One place per written item position, ordered by position. Empty when the
    /// document cannot be collected.</summary>
    public static IReadOnlyList<ItemPlace> Places(SyntaxTree tree)
    {
        var byPosition = new Dictionary<int, ItemPlace>();
        try
        {
            // A file without a score block draws its first part alone (the render path's null spec).
            var all = RenderSpecParser.FindAll(tree);
            RenderSpec?[] specs = all.Count > 0 ? [.. all] : [null];
            foreach (var spec in specs)
                Collect(SvgGenerator.CollectScore(tree, spec), byPosition);
        }
        catch (Exception)
        {
            // A document the collector cannot walk has no places; its marks stay unlocated.
        }
        var places = byPosition.Values.ToList();
        places.Sort((a, b) => a.Position.CompareTo(b.Position));
        return places;
    }

    private static void Collect(MultiStaffScore score, Dictionary<int, ItemPlace> byPosition)
    {
        var primary = score.PrimaryContentStaff.PrimaryVoice.Measures;
        if (primary.IsDefaultOrEmpty)
            return;
        var numbers = BarNumberEngraver.NumberMeasures(primary, primary[0].IsPickup ? -1 : 0);
        foreach (var (_, staff, _) in score.EnumerateStaves())
            foreach (var voice in staff.Voices)
                for (int m = 0; m < voice.Measures.Length && m < numbers.Length; m++)
                    foreach (var item in voice.Measures[m].Items)
                        if (item.SourcePosition >= 0)
                            byPosition.TryAdd(item.SourcePosition,
                                new ItemPlace(item.SourcePosition, numbers[m], staff.PrimaryVoice.Name));
    }

    /// <summary>
    /// The place of the item at a caret: the last item written on the caret's line at or
    /// before it, else the first after it on that line; null when the line writes none.
    /// </summary>
    /// <remarks>
    /// The LINE bounds the answer because a part's music is written in its own block: the
    /// nearest item before a caret on an empty line, or at the head of a block, belongs to
    /// another part as often as not. A line is where a reader — and an OMR reader, which
    /// writes a bar per line — expects the bar to be read off.
    /// </remarks>
    public static ItemPlace? PlaceAt(IReadOnlyList<ItemPlace> places, string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        int lineStart = offset == 0 ? 0 : text.LastIndexOf('\n', offset - 1) + 1;
        int lineEnd = text.IndexOf('\n', offset);
        if (lineEnd < 0)
            lineEnd = text.Length;

        // The first place at or after the caret, then step back to the last one before it.
        int lo = 0, hi = places.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (places[mid].Position <= offset) lo = mid + 1; else hi = mid;
        }
        if (lo > 0 && places[lo - 1].Position >= lineStart)
            return places[lo - 1];
        if (lo < places.Count && places[lo].Position < lineEnd)
            return places[lo];
        return null;
    }

    /// <summary>Every <c>@todo</c> of <paramref name="tree"/>, located through
    /// <paramref name="places"/> (<see cref="Places"/> of the same tree; collected here when null).</summary>
    public static IReadOnlyList<TodoEntry> Find(SyntaxTree tree, IReadOnlyList<ItemPlace>? places = null)
    {
        var entries = new List<TodoEntry>();
        foreach (var node in tree.GetRoot().DescendantNodesOfKinds(AnnotationNameValidator.AnnotationKinds))
        {
            if (TodoAnnotation.Of(node) is not { } todo || node.Parent is not { } host)
                continue;
            places ??= Places(tree);
            ItemPlace? place = null;
            foreach (var p in places)
                if (p.Position >= host.Span.Start && p.Position < host.Span.End)
                {
                    place = p;
                    break;
                }
            entries.Add(new TodoEntry(todo.Key, todo.Memo, node.Span.Start, node.Span.End,
                host.Span.Start, host.Span.End, place?.Measure, place?.Part));
        }
        return entries;
    }
}
