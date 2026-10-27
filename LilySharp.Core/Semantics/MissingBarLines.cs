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
/// Where the bar lines a span left out belong (<see cref="DiagnosticCodes.MissingBarLine"/>): after
/// each item that ends a whole bar of the meter, counted from the span's start — the places the
/// page and the playback already divide it at (<see cref="WrittenBar"/>). For the quick fix that
/// writes them (owner's request 2026-10-27: an OMR reader missed a barline, and Lily# knew where
/// it went). Null when the span cannot be cut there: a note held across a bar's end, a voice
/// construct the bar is in, or no music found.
/// </summary>
public static class MissingBarLines
{
    /// <summary>The meter a missing-bar-line report names ("… is about 2 bars of 6/8: …"), as the
    /// length of its bar; null for any other report (the pickup's, or a `time` inside the bar).</summary>
    public static Fraction? MeterOf(string message)
    {
        var m = System.Text.RegularExpressions.Regex.Match(message, @"^Measure duration \S+ is about \d+ bars of (\d+)/(\d+):");
        return m.Success && int.TryParse(m.Groups[1].Value, out int beats) && int.TryParse(m.Groups[2].Value, out int unit)
            && beats > 0 && unit > 0 ? new Fraction(beats, unit) : null;
    }

    /// <summary>
    /// <paramref name="text"/>'s <paramref name="start"/>..<paramref name="end"/> with the bar lines
    /// its missing-bar-line reports name written in (` |` after each bar's last item), or null when
    /// it has none to write — for a line edited beside the scan (the panel's button).
    /// </summary>
    public static string? WrittenIn(string text, int start, int end)
    {
        var tree = SyntaxTree.Parse(text);
        var at = new List<int>();
        foreach (var d in SemanticValidation.Run(tree))
            if (d.Code == DiagnosticCodes.MissingBarLine && d.Span.Start < end && d.Span.End > start
                && MeterOf(d.Message) is { } bar && Offsets(tree, d.Span, bar) is { } offsets)
                at.AddRange(offsets.Where(o => o > start && o < end));
        if (at.Count == 0)
            return null;
        var sb = new System.Text.StringBuilder(text[start..end]);
        foreach (int o in at.Distinct().OrderByDescending(o => o))
            sb.Insert(o - start, " |");
        return sb.ToString();
    }

    /// <summary>The offsets in the source to write a bar line at, in order, for the bar
    /// <paramref name="span"/> covers in a meter of <paramref name="barLength"/> whole notes.</summary>
    public static IReadOnlyList<int>? Offsets(SyntaxTree tree, TextSpan span, Fraction barLength)
    {
        if (barLength <= Fraction.Zero)
            return null;
        // The block the bar's items stand in: the innermost node holding the span whose own
        // children start inside it.
        var block = tree.GetRoot().DescendantNodes()
            .Where(n => n.Span.Start <= span.Start && n.Span.End >= span.End
                && n.ChildNodes().Any(c => c.Span.Start >= span.Start && c.Span.Start < span.End))
            .LastOrDefault();
        if (block == null)
            return null;
        var running = Fraction.Quarter;
        var total = Fraction.Zero;
        var bars = Fraction.Zero;
        var offsets = new List<int>();
        bool any = false;
        foreach (var item in block.ChildNodes())
        {
            if (item.Span.Start < span.Start)
            {
                // The default note value the bar starts with is the one the music before it leaves.
                MeasureDurations.ItemDuration(item, ref running);
                continue;
            }
            if (item.Span.Start >= span.End)
                break;
            if (item is BarlineSyntax)
                return null;   // not one span: nothing to write
            var length = MeasureDurations.ItemDuration(item, ref running);
            if (length > Fraction.Zero)
                any = true;
            total += length;
            // Past a bar's end inside this item: it is held across, and no bar line fits there.
            if (total > bars + barLength && item.Span.End < span.End)
                return null;
            // Ending a bar exactly: a bar line after it — not after the span's last item, which
            // the bar line already written follows.
            if (total == bars + barLength)
            {
                bars += barLength;
                if (item.Span.End < span.End)
                    offsets.Add(item.Span.End);
            }
        }
        return any && offsets.Count > 0 ? offsets : null;
    }
}
