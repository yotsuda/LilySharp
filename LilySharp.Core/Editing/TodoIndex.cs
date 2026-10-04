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

/// <summary>
/// The <c>@todo</c> marks of a document in source order, each with the bar number and the
/// part the score draws it in (LilySharp-Omr proposal C1: an editor goes from a mark to the
/// bar of the scanned page, and from the caret to the mark).
/// </summary>
/// <remarks>
/// The bar number is the one the page PRINTS — <see cref="BarNumberEngraver.NumberMeasures"/>
/// over the measures that drive the layout, with a leading pickup as bar 0 — so a reader of
/// the page and of this answer count alike. A phrase played twice is in two bars; the first
/// is answered. The scores are collected the way the render path collects them
/// (<c>SvgGenerator.CollectScore</c>), and the
/// first score that plays the host decides.
/// </remarks>
public static class TodoIndex
{
    /// <summary>Every <c>@todo</c> of <paramref name="tree"/>, located.</summary>
    public static IReadOnlyList<TodoEntry> Find(SyntaxTree tree)
    {
        var marks = new List<(TodoAnnotation Todo, SyntaxNode Mark, SyntaxNode Host)>();
        foreach (var node in tree.GetRoot().DescendantNodesOfKinds(AnnotationNameValidator.AnnotationKinds))
            if (TodoAnnotation.Of(node) is { } todo && node.Parent is { } host)
                marks.Add((todo, node, host));
        if (marks.Count == 0)
            return [];

        var located = new (int Measure, string Part)?[marks.Count];
        try
        {
            // A file without a score block draws its first part alone (the render path's null spec).
            var all = RenderSpecParser.FindAll(tree);
            RenderSpec?[] specs = all.Count > 0 ? [.. all] : [null];
            foreach (var spec in specs)
            {
                if (located.All(l => l != null))
                    break;
                Locate(SvgGenerator.CollectScore(tree, spec), marks, located);
            }
        }
        catch (Exception)
        {
            // A document the collector cannot walk still lists its marks, unlocated.
        }

        var entries = new TodoEntry[marks.Count];
        for (int i = 0; i < marks.Count; i++)
        {
            var (todo, mark, host) = marks[i];
            entries[i] = new TodoEntry(todo.Key, todo.Memo, mark.Span.Start, mark.Span.End,
                host.Span.Start, host.Span.End, located[i]?.Measure, located[i]?.Part);
        }
        return entries;
    }

    private static void Locate(MultiStaffScore score,
        List<(TodoAnnotation Todo, SyntaxNode Mark, SyntaxNode Host)> marks, (int, string)?[] located)
    {
        var primary = score.PrimaryContentStaff.PrimaryVoice.Measures;
        if (primary.IsDefaultOrEmpty)
            return;
        var numbers = BarNumberEngraver.NumberMeasures(primary, primary[0].IsPickup ? -1 : 0);
        foreach (var (_, staff, _) in score.EnumerateStaves())
            foreach (var voice in staff.Voices)
                for (int m = 0; m < voice.Measures.Length && m < numbers.Length; m++)
                    foreach (var item in voice.Measures[m].Items)
                    {
                        if (item.TodoKey == null)
                            continue;
                        for (int i = 0; i < marks.Count; i++)
                            if (located[i] == null
                                && item.SourcePosition >= marks[i].Host.Span.Start
                                && item.SourcePosition < marks[i].Host.Span.End)
                                located[i] = (numbers[m], staff.PrimaryVoice.Name);
                    }
    }
}
