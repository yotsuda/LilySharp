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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// The ways out of LYS4080 — verses that do not fit their section's repeat — as edits an
/// editor can offer (owner's instruction 2026-11-14: a book must not be left unplayable with no
/// way shown out; docs/inline-repeat-design.md §6). Read off the findings the report was made
/// from (<see cref="VerseRepeatValidator.Findings"/>).
/// </summary>
public static class VerseRepeatFixes
{
    /// <summary>One way out: its title, and the text to put in place of <c>[Start, End)</c>.</summary>
    public readonly record struct Fix(string Title, int Start, int End, string NewText);

    /// <summary>The fixes for the warning reported at <paramref name="reported"/>.</summary>
    public static IReadOnlyList<Fix> For(SyntaxTree tree, TextSpan reported)
    {
        var root = tree.GetRoot();
        string text = root.ToFullString();
        var fixes = new List<Fix>();
        string newLine = text.Contains("\r\n") ? "\r\n" : "\n";
        foreach (var f in VerseRepeatValidator.Findings(root).Where(f => f.Verse.Span.Start == reported.Start))
        {
            int end = f.Verse.Span.Start + f.Verse.Span.Length;
            switch (f.Fault)
            {
                case VerseRepeatValidator.Fault.MissingPass:
                    // After the last verse, on a line of its own at that verse's indent.
                    string indent = Indent(text, f.Verse.Span.Start);
                    string body = Body(f.Verse);
                    string bars = string.Join(" ", Enumerable.Repeat("|", System.Math.Max(1, body.Count(c => c == '|'))));
                    fixes.Add(new Fix($"Add 'verse {f.Pass}' with the bars empty", end, end, $"{newLine}{indent}verse {f.Pass} {{ {bars} }}"));
                    fixes.Add(new Fix($"Add 'verse {f.Pass}' with the words of the verse before it", end, end, $"{newLine}{indent}verse {f.Pass} {{{body}}}"));
                    break;
                case VerseRepeatValidator.Fault.NeverSung:
                    fixes.Add(new Fix($"Delete this verse - section '{f.Section}' plays {f.Passes} times", LineStart(text, f.Verse.Span.Start), end, ""));
                    break;
                case VerseRepeatValidator.Fault.PassNamedTwice:
                    var named = new HashSet<int>();
                    foreach (var verse in f.Verse.Parent!.ChildNodes().Where(n => n.Kind == SyntaxKind.LyricVerse))
                        named.UnionWith(LyricsCollector.PlaysOfVerseAt(root, verse.Span.Start));
                    int free = Enumerable.Range(1, f.Passes).FirstOrDefault(p => !named.Contains(p));
                    var header = VerseRepeatValidator.HeaderSpan(f.Verse);
                    if (free > 0)
                        fixes.Add(new Fix($"Make this 'verse {free}' - no verse is for pass {free}", header.Start, header.Start + header.Length, $"verse {free}"));
                    fixes.Add(new Fix("Delete this verse", LineStart(text, f.Verse.Span.Start), end, ""));
                    break;
            }
        }
        return fixes;
    }

    /// <summary>What stands between the verse's braces, as written.</summary>
    private static string Body(SyntaxNode verse)
    {
        string full = verse.ToFullString();
        int open = full.IndexOf('{'), close = full.LastIndexOf('}');
        return open >= 0 && close > open ? full.Substring(open + 1, close - open - 1) : " ";
    }

    /// <summary>The white space the line of <paramref name="offset"/> opens with.</summary>
    private static string Indent(string text, int offset)
    {
        int start = text.LastIndexOf('\n', System.Math.Max(0, offset - 1)) + 1;
        int end = start;
        while (end < offset && text[end] is ' ' or '\t')
            end++;
        return text.Substring(start, end - start);
    }

    /// <summary>Where to delete from so the verse's line goes with it: the end of the line
    /// before, when only white space stands before the verse on its own line.</summary>
    private static int LineStart(string text, int offset)
    {
        int start = text.LastIndexOf('\n', System.Math.Max(0, offset - 1));
        if (start < 0)
            return offset;
        for (int i = start + 1; i < offset; i++)
            if (text[i] is not (' ' or '\t'))
                return offset;
        return start > 0 && text[start - 1] == '\r' ? start - 1 : start;
    }
}