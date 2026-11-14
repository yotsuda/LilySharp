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

using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A repeat sign, a volta ending or a navigation mark written in ONE part's music is the
/// score's, so typing one moves the bar lines of every staff — and the preview
/// (<see cref="IncrementalCompiler"/>), which keeps what an edit did not touch, has to draw
/// what the full render draws (docs/inline-repeat-design.md §4, the row "the preview's
/// increment": one keystroke in one part, every part's bar line).
/// </summary>
/// <remarks>⚠️ These books still report LYS1034 (the signs belong in the form); the page is
/// drawn all the same, and that is what is compared (the design's §4: the rule is lifted last).</remarks>
[Trait("Category", "Unit")]
public sealed class InlineRepeatIncrementalTests
{
    private static readonly SvgRenderOptions Opt = new() { EmbedFont = false };

    // Two parts and a chord row over two sections, long enough to break into systems.
    // (Line ends as the cases below spell them, whatever this file's are.)
    private static readonly string Book = """
        time 4/4
        part mel { section A { c'4 d e f | g a b c | d c b a | g f e d | c d e f | g a b c | d c b a | g1 | }
                   section B { e'4 f g a | b c d e | f e d c | b a g f | e1 | } }
        part bas { clef bass section A { c1 | g | d | g | c | g | d | g | }
                   section B { e1 | b | f | b | e | } }
        chords ch { section A { C | G | Dm | G | C | G | Dm | G | } section B { Em | Bm | F | Bm | Em | } }
        form { A B }
        score { chords ch staff mel staff bas }
        """.ReplaceLineEndings("\n");

    private static string Full(string src) => SvgGenerator.Generate(SyntaxTree.Parse(src), Opt).Replace("\r\n", "\n");

    /// <summary>Types the edit, and types it back: each preview is the full render of its text.</summary>
    [Theory]
    // A sign in the melody: the bass and the chord row's staff follow.
    [InlineData("b c | d c b a | g f e d | c d", "b c |: d c b a | g f e d :| c d")]
    [InlineData("c'4 d e f | g a b c |", "|: c'4 d e f | g a b c :|")]
    [InlineData("g f e d | c d e f", "g f e d :|*3 c d e f")]
    [InlineData("g f e d | c d e f", "g f e d :|: c d e f")]
    // …in the bass, which the melody follows.
    [InlineData("c1 | g | d | g | c | g | d | g | }", "c1 |: g | d | g :| c | g | d | g | }")]
    // …in the chord row.
    [InlineData("C | G | Dm | G | C |", "C |: G | Dm | G :| C |")]
    // An ending and its numbers.
    [InlineData("c'4 d e f | g a b c | d c b a | g f e d | c d", "|: c'4 d e f | g a b c | [1. d c b a :| [2. g f e d ] | c d")]
    // A pair the section boundary cuts.
    [InlineData("g1 | }\n           section B { e'4 f g a | b c d e |", "|: g1 | }\n           section B { e'4 f g a | b c d e :|")]
    // The navigation marks.
    [InlineData("g a b c | d c b a | g f e d | c d e f | g a b c | d c b a | g1 |", "g a b c | segno d c b a | g f e d | c d e f to coda | g a b c | d c b a | g1 ds al coda | coda")]
    public void ThePreviewDrawsWhatTheFullRenderDraws(string plain, string signed)
    {
        Assert.Contains(plain, Book);
        string with = Book.Replace(plain, signed);
        Assert.NotEqual(Full(Book), Full(with)); // the sign is drawn at all

        var typing = new IncrementalCompiler(SyntaxTree.Parse(Book), Opt);
        typing.Render();
        Assert.Equal(Full(with), typing.RenderIncremental(SyntaxTree.Parse(with)).Replace("\r\n", "\n"));
        Assert.Equal(Full(Book), typing.RenderIncremental(SyntaxTree.Parse(Book)).Replace("\r\n", "\n"));
    }

    /// <summary>One character at a time, as it is typed: <c>|</c> to <c>:|</c> to <c>:|:</c>, and
    /// an ending's number changed under a session that has drawn the other.</summary>
    [Fact]
    public void ASignTypedACharacterAtATimeIsDrawnEachTime()
    {
        string[] steps =
        [
            Book,
            Book.Replace("g f e d | c d e f", "g f e d :| c d e f"),
            Book.Replace("g f e d | c d e f", "g f e d :|: c d e f"),
            Book.Replace("g f e d | c d e f", "g f e d :|: c d e f").Replace("d c b a | g1 |", "d c b a :| g1 |"),
            Book.Replace("g f e d | c d e f", "g f e d :|: c d e f").Replace("d c b a | g1 |", "d c b a :|*3 g1 |"),
            Book.Replace("g f e d | c d e f", "g f e d | c d e f").Replace("d c b a | g1 |", "d c b a :|*3 g1 |"),
        ];
        var typing = new IncrementalCompiler(SyntaxTree.Parse(steps[0]), Opt);
        typing.Render();
        for (int i = 1; i < steps.Length; i++)
            Assert.Equal(Full(steps[i]), typing.RenderIncremental(SyntaxTree.Parse(steps[i])).Replace("\r\n", "\n"));
    }
}
