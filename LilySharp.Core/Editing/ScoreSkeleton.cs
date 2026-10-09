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

using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// The <c>score { … }</c> a book with no score is offered (LYS6014's quick fix, and the
/// migration that gave the score-less books one): every chord track as a row on top, a staff
/// for every part in the order the book declares them, then every lyrics track — the plain
/// vertical stack, for the writer to rearrange.
/// </summary>
public static class ScoreSkeleton
{
    /// <summary>The score block's text, on one line (<c>score { staff melody  staff bass }</c>).</summary>
    public static string For(CompilationUnitSyntax root)
    {
        var parts = new List<string>();
        foreach (var node in root.DescendantNodesOfKinds(PartReferenceFinder.DeclaringKinds))
            if (PartReferenceFinder.DeclaredName(node) is { Text.Length: > 0 } name
                && name.Text != SyntaxFacts.UnnamedPartName && !parts.Contains(name.Text))
                parts.Add(name.Text);

        var chords = new List<string>();
        var lyrics = new List<string>();
        foreach (var node in root.DescendantNodes())
        {
            // A track's own sections (`chords prog { section A { … } }`) are inside it; its
            // name is the block's.
            if (node is ChordPartBlockSyntax c && c.Parent is not ChordPartBlockSyntax)
            {
                string row = c.NameToken is { } n ? "chords " + n.Text : "chords";
                if (!chords.Contains(row)) chords.Add(row);
            }
            else if (node is LyricsBlockSyntax l && l.Parent is not LyricsBlockSyntax)
            {
                string row = l.NameToken is { } n ? "lyrics " + n.Text : "lyrics";
                if (!lyrics.Contains(row)) lyrics.Add(row);
            }
        }

        var items = new List<string>(chords);
        if (parts.Count == 0)
            items.Add("staff");   // the file's unnamed part: its sections' own music
        else
            foreach (var p in parts)
                items.Add("staff " + p);
        items.AddRange(lyrics);
        return "score { " + string.Join("  ", items) + " }";
    }
}
