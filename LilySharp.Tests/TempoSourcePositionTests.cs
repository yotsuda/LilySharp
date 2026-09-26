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
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Each piece of a tempo mark carries ITS token's offset (user request 2026-09-26): the
/// note and "= 120" click to <c>tempo |120</c> (the note to a written unit, <c>tempo |4 = 120</c>),
/// the swing equation to <c>tempo 120 |swing</c> with the swung value (<c>swing |16</c>) as an
/// alias, and the marking to its own string. Until then the whole mark — swing equation
/// included — carried the first value's offset, so a click on the swing jumped to the bpm
/// and a caret on the bpm lit the swing as well.
/// </summary>
/// <remarks>⚠️ INTERACTIVE, because the alias half only exists there (static export drops
/// <c>data-alt</c>).</remarks>
[Trait("Category", "Unit")]
public class TempoSourcePositionTests
{
    private static readonly SvgRenderOptions Interactive = new() { EmbedFont = false, Interactive = true };

    private static string Book(string header, string music = "c'4 d' e' f' |") =>
        header + "\npart m { clef treble }\nsection A { m { " + music + " } }\n"
        + "form main { ~A }\nscore main { staff m }\n";

    /// <summary>Every element that carries an address: its tag text, primary and aliases.</summary>
    private static List<(string Tag, int Pos, string Alt)> Addressed(string svg)
        => Regex.Matches(svg, "<[a-z]+ [^>]*data-pos=\"[0-9]+\"[^>]*>(?:[^<]*)")
            .Select(m => m.Value)
            .Select(tag => (
                Tag: tag,
                Pos: int.Parse(Regex.Match(tag, "data-pos=\"([0-9]+)\"").Groups[1].Value),
                Alt: Regex.Match(tag, "data-alt=\"([^\"]*)\"") is { Success: true } a ? a.Groups[1].Value : ""))
            .ToList();

    private static int At(string book, string needle) => book.IndexOf(needle, System.StringComparison.Ordinal);

    [Fact]
    public void TheCountAndTheNote_ClickToTheBpm_AndTheSwingToTheFeelWord()
    {
        string book = Book("tempo 120 swing");
        int bpm = At(book, "120"), feel = At(book, "swing");
        var els = Addressed(SvgGenerator.Generate(SyntaxTree.Parse(book), Interactive));

        // "= 120" is text; the note is a music glyph and a stem. All on the bpm.
        Assert.Contains(els, e => e.Tag.Contains("= 120") && e.Pos == bpm);
        Assert.Contains(els, e => e.Tag.Contains("class=\"music\"") && e.Pos == bpm);
        // The swing equation — heads, stems, beam, flag, bracket, "=", "3" — on the feel word,
        // and none of it on the bpm.
        var swing = els.Where(e => e.Pos == feel).ToList();
        Assert.True(swing.Count >= 12, $"the swing equation's pieces carry the feel word: {swing.Count}");
        Assert.Contains(swing, e => e.Tag.Contains(">3"));
        Assert.DoesNotContain(els, e => e.Pos == bpm && e.Tag.Contains(">3"));
    }

    /// <summary>
    /// The swing equation highlights WHOLE: the preview reads a <c>&lt;rect&gt;</c> among one
    /// offset's elements as a boxed label's frame and then leaves that offset's text
    /// uncoloured, so while the stems and beams were rects only they lit and the heads, the
    /// flag, the "=" and the "3" stayed dark (user report 2026-09-26). No rect, and every kind
    /// of piece present under the one address.
    /// </summary>
    [Fact]
    public void TheSwingEquation_HasNoRect_SoThePreviewLightsAllOfIt()
    {
        string book = Book("tempo 120 swing");
        int feel = At(book, "swing");
        var swing = Addressed(SvgGenerator.Generate(SyntaxTree.Parse(book), Interactive))
            .Where(e => e.Pos == feel).ToList();
        Assert.DoesNotContain(swing, e => e.Tag.StartsWith("<rect", System.StringComparison.Ordinal));
        Assert.Equal(5, swing.Count(e => e.Tag.Contains("class=\"music\"")));   // 4 heads + the flag
        Assert.Contains(swing, e => e.Tag.StartsWith("<text", System.StringComparison.Ordinal) && e.Tag.EndsWith(">="));
        Assert.Contains(swing, e => e.Tag.StartsWith("<text", System.StringComparison.Ordinal) && e.Tag.EndsWith(">3"));
        // The pair's 2 stems and beam, the triplet's 2 stems.
        Assert.Equal(5, swing.Count(e => e.Tag.StartsWith("<polygon", System.StringComparison.Ordinal)));
    }

    [Fact]
    public void TheSwungValue_IsTheSwingEquationsAlias()
    {
        string book = Book("tempo 120 swing 16");
        int feel = At(book, "swing"), value = At(book, "16");
        var swing = Addressed(SvgGenerator.Generate(SyntaxTree.Parse(book), Interactive))
            .Where(e => e.Pos == feel).ToList();
        Assert.NotEmpty(swing);
        Assert.All(swing, e => Assert.Equal(value.ToString(), e.Alt));
    }

    [Fact]
    public void AWrittenUnit_TakesTheNote_AndTheMarkingKeepsItsString()
    {
        string book = Book("tempo \"Allegro\" 4 = 132");
        int marking = At(book, "\"Allegro\""), unit = At(book, "4 = "), bpm = At(book, "132");
        var els = Addressed(SvgGenerator.Generate(SyntaxTree.Parse(book), Interactive));

        Assert.Contains(els, e => e.Tag.Contains(">Allegro") && e.Pos == marking);
        Assert.Contains(els, e => e.Tag.Contains("class=\"music\"") && e.Pos == unit);
        Assert.Contains(els, e => e.Tag.Contains("= 132)") && e.Pos == bpm);
        Assert.Contains(els, e => e.Tag.Contains(">(") && e.Pos == bpm);
    }

    [Fact]
    public void AMidPieceTempo_ItsPiecesToo()
    {
        string book = Book("", "c'4 d' tempo 96 swing e' f' |");
        int bpm = At(book, "96"), feel = At(book, "swing");
        var els = Addressed(SvgGenerator.Generate(SyntaxTree.Parse(book), Interactive));
        Assert.Contains(els, e => e.Tag.Contains("= 96") && e.Pos == bpm);
        Assert.True(els.Count(e => e.Pos == feel) >= 12);
    }

    /// <summary>An edit above the tempo shifts every piece: the incremental render equals
    /// a full one (the collect splice, the layout's re-derived data-pos and the overlay memo
    /// all carry the pieces).</summary>
    [Theory]
    [InlineData("title \"T\"\n", "title \"Title\"\n")]
    [InlineData("c'4 d' e' f' |", "c'4 d' e' f' | g'1 |")]
    public void AnEdit_ShiftsThePieces_AsAFullRenderDoes(string find, string replace)
    {
        string src = Book("title \"T\"\ntempo 120 swing 16");
        var tree = SyntaxTree.Parse(src);
        var session = new LilySharp.Core.Svg.IncrementalCompiler(tree, Interactive);
        session.Render();
        int at = src.IndexOf(find, System.StringComparison.Ordinal);
        Assert.True(at >= 0);
        var change = new TextChange(new TextSpan(at, find.Length), replace);
        string incremental = session.Edit(change).Replace("\r\n", "\n");
        string edited = tree.WithChange(change).Text;
        string full = SvgGenerator.Generate(SyntaxTree.Parse(edited), Interactive).Replace("\r\n", "\n");
        Assert.Equal(full, incremental);
        Assert.Contains($"data-pos=\"{edited.IndexOf("swing", System.StringComparison.Ordinal)}\"", incremental);
    }
}
