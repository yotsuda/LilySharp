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
using System.Linq;
using LilySharp.Core.Music;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// "Name these words on the part" (owner's decision 2026-11-12: the part's own
/// <c>lyrics NAME</c> is the standard way to say whose words they are): on a <c>singer</c> line,
/// or on a <c>lyrics NAME { … }</c> block, the edit that writes <c>lyrics NAME</c> into the header
/// of the part the words are sung or printed on — where the file says that only by a singer
/// line's <c>on</c> or by where a score stacks the row.
/// </summary>
/// <remarks>
/// The relation can be written in three places (docs/lyrics-singer-design.md §10): a score's
/// row under a staff, a singer line's <c>on PART</c>, the part's header. Only the last is
/// read off the part itself; the other two say it for one score or one voice.
/// </remarks>
public static class PartWordsNamer
{
    /// <summary>The words and the part at <paramref name="offset"/>, and the edit that names
    /// the one on the other; null where there is nothing to name (no singer line or lyrics
    /// block there, words made from the notes, a part that names them already, a part not found).</summary>
    public static (string Title, int Start, int End, string NewText)? EditAt(SyntaxTree tree, string text, int offset)
    {
        var root = tree.GetRoot();
        string? track = null, part = null;
        foreach (var line in LyricSinger.LinesOf(root))
        {
            if (line.Node.Span.Start <= offset && offset <= line.Node.Span.End)
            {
                (track, part) = (line.Track, line.Part);
                break;
            }
        }
        if (track == null)
        {
            // On the head of a named lyrics block: the part its words follow, by a score's row.
            foreach (var block in root.DescendantNodes<LyricsBlockSyntax>())
            {
                int brace = text.IndexOf('{', Math.Clamp(block.Span.Start, 0, text.Length));
                if (block.Span.Start <= offset && offset <= (brace < 0 ? block.Span.End : brace))
                {
                    track = block.VoiceName;
                    part = LyricBindings.TargetOf(root, track);
                    break;
                }
            }
        }
        if (string.IsNullOrEmpty(track) || part == null
            || track == SyntaxFacts.UnnamedLyricsName || GeneratedLyrics.IsGenerated(track)
            || LyricBindings.TracksOfPart(root, part).Contains(track))
            return null;
        var declaration = root.ChildNodes().OfType<PartDeclarationSyntax>().FirstOrDefault(p => p.Name.Text == part);
        if (declaration == null)
            return null;

        string title = $"Name these words on the part: part {part} {{ lyrics {track} }}";
        string newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        // A part that holds its own sections (the by-part writing): the words are named with
        // the header's properties, before the first section.
        if (declaration.ChildNodes().OfType<SectionDeclarationSyntax>().FirstOrDefault() is { } firstSection)
        {
            int at = firstSection.Span.Start;
            int from = at > 0 ? text.LastIndexOf('\n', at - 1) + 1 : 0;
            string indent = text[from..at];
            return indent.Trim().Length == 0 && from > 0
                ? (title, from, from, indent + "lyrics " + track + newLine)
                : (title, at, at, "lyrics " + track + "  ");
        }
        int close = -1;
        for (int i = declaration.SlotCount - 1; i >= 0; i--)
        {
            if (declaration.GetChild(i) is SyntaxTokenNode { Kind: SyntaxKind.CloseBrace } brace)
            {
                close = brace.Span.Start;
                break;
            }
        }
        if (close < 0)
            return (title, declaration.Span.End, declaration.Span.End, $" {{ lyrics {track} }}");
        int lineStart = close > 0 ? text.LastIndexOf('\n', close - 1) + 1 : 0;
        string before = text[lineStart..close];
        if (before.Trim().Length == 0 && lineStart > 0)
        {
            // The header's brace closes on a line of its own: a line of its own for the words.
            return (title, lineStart, lineStart, before + "  lyrics " + track + newLine);
        }
        bool spaced = close > 0 && char.IsWhiteSpace(text[close - 1]);
        return (title, close, close, (spaced ? " " : "  ") + "lyrics " + track + " ");
    }
}
