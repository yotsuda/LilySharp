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

using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A drawn bow cites the CHARACTER THAT WROTE IT: a tie carries the offset of its
/// <c>~</c>, a slur the offset of its <c>(</c> with its <c>)</c> as an alias.
/// </summary>
/// <remarks>
/// Until 2026-08-30 no ordinary tie or slur carried a source offset at all — they are
/// drawn outside every <c>IDrawingContext.Source</c> scope, so of the 56 <c>&lt;path&gt;</c>
/// elements in the tracked snapshots exactly 6 had a <c>data-pos</c>, and all 6 were GRACE
/// slurs that happen to fall inside their note's scope. A caret on <c>~</c> therefore lit
/// the nearest PRECEDING address — the note — which is not a lie but is not the answer
/// either (HANDOFF §2 U10, from the reader's own question).
/// <para>
/// ⚠️ TWO ADDRESSES, ONE BOW, AND THE MECHANISM ALREADY EXISTED: a slur is written at two
/// places, so the bow takes <c>(</c> as its <c>data-pos</c> (the click target) and <c>)</c>
/// as a <c>data-alt</c> member — the same pair a barline uses when several written bars
/// collapse onto one drawn line, and the webview already matches
/// <c>[data-pos="p"], [data-alt~="p"]</c> for it. The issue that opened this said the
/// editor assumed one address per element; measured, it had not since the barline work.
/// </para>
/// <para>
/// ⚠️ THE OFFSET IS THE MARKER'S, NOT THE NOTE'S, and that distinction is the whole test:
/// reading the host note's own <c>SourcePosition</c> would pass every "the bow has an
/// address" assertion while lighting the wrong character. Every case below therefore
/// pins the marker's index and asserts the note's index is NOT it.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class BowSourcePositionTests
{
    private static string Book(string music) =>
        $"part bass\nsection A {{ bass {{ {music} }} }}\nform main {{ ~A }}\n"
        + "score main { staff bass }\n";

    /// <summary>⚠️ INTERACTIVE, because the alias half of the claim only exists there:
    /// <c>SvgDrawingContext.Source(int, aliases)</c> keeps <c>data-alt</c> for the preview
    /// and drops it for static export, so a non-interactive render would show the primary
    /// and say nothing at all about the second address.</summary>
    private static string Render(string book) => LilySharp.Core.Svg.SvgGenerator.Generate(
        SyntaxTree.Parse(book),
        new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false, Interactive = true });

    /// <summary>The offset of <paramref name="needle"/>'s <paramref name="nth"/>
    /// occurrence INSIDE the music, never in the surrounding book — the wrapper
    /// contains a <c>~</c> of its own (the phrase reference <c>~A</c>), and a
    /// whole-book IndexOf finds that one in half the cases here.</summary>
    private static int OffsetInMusic(string music, string needle, int nth = 0)
    {
        string book = Book(music);
        int bodyStart = book.IndexOf(music, System.StringComparison.Ordinal);
        Assert.True(bodyStart > 0, "the wrapper must contain the music verbatim");
        int at = -1;
        for (int i = 0; i <= nth; i++)
            at = music.IndexOf(needle, at + 1, System.StringComparison.Ordinal);
        Assert.True(at >= 0, $"the music must contain '{needle}' #{nth}");
        return bodyStart + at;
    }

    /// <summary>Every <c>&lt;path&gt;</c> that carries a primary address, with its
    /// alias list — the bows are the only paths in these books.</summary>
    private static System.Collections.Generic.List<(int Pos, string Alt)> BowAddresses(string svg)
        => Regex.Matches(svg, "<path[^>]*>")
            .Select(m => m.Value)
            .Select(tag => (
                Pos: Regex.Match(tag, "data-pos=\"([0-9]+)\"") is { Success: true } p
                    ? int.Parse(p.Groups[1].Value) : -1,
                Alt: Regex.Match(tag, "data-alt=\"([^\"]*)\"") is { Success: true } a
                    ? a.Groups[1].Value : ""))
            .Where(x => x.Pos >= 0)
            .ToList();

    [Theory]
    [InlineData("c4~ c4 d4 e4 |")]     // the plain tie
    [InlineData("c4~@accent c4 d4 e4 |")]  // marker BEFORE a post-event (the U9 spelling)
    [InlineData("c4@accent~ c4 d4 e4 |")]  // marker AFTER it — the same music, other tree
    public void ATie_CitesTheTildeThatWroteIt(string music)
    {
        int tilde = OffsetInMusic(music, "~");
        int note = OffsetInMusic(music, "c4");
        Assert.NotEqual(note, tilde);   // the two addresses must be distinguishable

        var bows = BowAddresses(Render(Book(music)));
        Assert.Contains(bows, b => b.Pos == tilde);
        Assert.DoesNotContain(bows, b => b.Pos == note);
    }

    [Theory]
    [InlineData("c4( d4 e4) f4 |")]
    [InlineData("c4(@accent d4 e4) f4 |")]
    public void ASlur_CitesItsOpenParen_AndAliasesItsClose(string music)
    {
        int open = OffsetInMusic(music, "(");
        int close = OffsetInMusic(music, ")");

        var bows = BowAddresses(Render(Book(music)));
        var bow = Assert.Single(bows, b => b.Pos == open);
        Assert.Equal(close.ToString(), bow.Alt);
    }

    /// <summary>
    /// The control the two theories above need: a book with no bow at all draws paths
    /// (stems are lines, but beams and the staff braces are paths) and NONE of them
    /// carries an address. Without this, "the bow has an address" could be satisfied by
    /// stamping every path in the document.
    /// </summary>
    [Fact]
    public void WithoutABow_NoPathCarriesAnAddress()
    {
        var bows = BowAddresses(Render(Book("c8 d8 e8 f8 g4 |")));
        Assert.Empty(bows);
    }

    /// <summary>
    /// One written slur drawn on TWO staves (a part rendered as notation and tablature)
    /// is two paths with ONE address — the same shape a chord's heads have, and what the
    /// webview's clusterInstances already expects.
    /// </summary>
    [Fact]
    public void OneSlurOnTwoStaves_IsTwoBowsWithOneAddress()
    {
        const string music = "c4( d4 e4) f4 |";
        string book = $"part bass\nsection A {{ bass {{ {music} }} }}\nform main {{ ~A }}\n"
            + "score main { staff bass tab bass }\n";
        int open = book.IndexOf(music, System.StringComparison.Ordinal) + music.IndexOf('(');

        var bows = BowAddresses(Render(book));
        Assert.Equal(2, bows.Count(b => b.Pos == open));
    }
}
