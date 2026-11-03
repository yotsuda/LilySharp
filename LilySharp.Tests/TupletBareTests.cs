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
using LilySharp.Core.Editing;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>tuplet 3/2 bare { … }</c> (the OMR's request 19, owner's decision 2026-11-03): a tuplet drawn
/// with no number and no bracket, as engravers leave them off where the music plainly runs in threes.
/// One word after the ratio, of that place alone. Durations, beams and sound stay.
/// </summary>
[Trait("Category", "Unit")]
public class TupletBareTests
{
    private static string Book(string music, string part = "clef treble") =>
        "time 4/4\npart m { " + part + " }\nsection A { m { " + music + " } }\nform { A }\nscore { staff m }\n";

    private const string Eighths = "tuplet 3/2 { g'8 c'' e'' } tuplet 3/2 { g' c'' e'' } tuplet 3/2 { g' c'' e'' } tuplet 3/2 { g' c'' e'' } |";
    private const string Quarters = "tuplet 3/2 { c''4 d'' e'' } tuplet 3/2 { f'' e'' d'' } |";

    private static string Bare(string music) => music.Replace("tuplet 3/2 {", "tuplet 3/2 bare {");

    private static ScoreLayout Laid(string book)
    {
        var tree = SyntaxTree.Parse(book);
        Assert.False(tree.HasErrors, book);
        return new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)!));
    }

    [Theory]
    [InlineData("tuplet 3/2 bare { c'8 d e } f4 g a |")]
    [InlineData("tuplet 3/2 bare{ c'8 d e } f4 g a |")]
    [InlineData("tuplet 3/2   bare   { c'8 d e } f4 g a |")]
    public void TheWord_ParsesAndKeepsItsPlace(string music)
    {
        var tree = SyntaxTree.Parse(Book(music));
        Assert.False(tree.HasErrors);
        var tuplet = Assert.Single(tree.GetNodes<TupletExpressionSyntax>());
        Assert.True(tuplet.IsBare);
        Assert.Equal(3, tuplet.TupletRatio);
        Assert.Equal(2, tuplet.BaseDivision);
        Assert.Equal(3, tuplet.Body.DescendantNodes<NoteSyntax>().Count());
        Assert.Equal(Book(music), tree.GetRoot().ToFullString());
    }

    [Fact]
    public void NoWord_IsAsItWas()
    {
        var tuplet = Assert.Single(SyntaxTree.Parse(Book("tuplet 3/2 { c'8 d e } f4 g a |")).GetNodes<TupletExpressionSyntax>());
        Assert.False(tuplet.IsBare);
        Assert.Null(tuplet.Appearance);
    }

    [Fact]
    public void AnotherWord_IsAnError()
        => Assert.True(SyntaxTree.Parse(Book("tuplet 3/2 slur { c'8 d e } f4 g a |")).HasErrors);

    [Fact]
    public void ItIsAWordOfThatPlaceAlone_APartMayBeCalledBare()
    {
        const string book = "time 4/4\npart bare { clef treble }\nsection A { bare { tuplet 3/2 bare { c'8 d e } f4 g a | } }\nform { A }\nscore { staff bare }\n";
        var tree = SyntaxTree.Parse(book);
        Assert.False(tree.HasErrors);
        Assert.Empty(LilySharp.Core.Semantics.SemanticValidation.Run(tree));
    }

    [Theory]
    [InlineData(Eighths)]
    [InlineData(Quarters)]
    public void ThePage_DrawsNoNumberAndNoBracket(string music)
    {
        Assert.NotEmpty(Laid(Book(music)).TupletBracketLayouts);
        Assert.Empty(Laid(Book(Bare(music))).TupletBracketLayouts);
    }

    /// <summary>The OMR's repro: bars 1-2 drawn, bars 3-4 bare, in one part.</summary>
    [Fact]
    public void FromTheMiddleOfAPiece_ItIsTupletByTuplet()
    {
        var laid = Laid(Book(Eighths + " " + Quarters + " " + Bare(Eighths) + " " + Bare(Quarters)));
        Assert.Equal(6, laid.TupletBracketLayouts.Length);
        Assert.All(laid.TupletBracketLayouts, b => Assert.InRange(b.MeasureIndex, 0, 1));
    }

    [Fact]
    public void OnTheSvg_TheDigitGoes()
    {
        string Svg(string music) => SvgGenerator.Generate(SyntaxTree.Parse(Book(music)));
        Assert.Contains(">3<", Svg(Eighths));
        Assert.DoesNotContain(">3<", Svg(Bare(Eighths)));
    }

    [Fact]
    public void TheBeams_AreTheSame()
    {
        Assert.Equal(Laid(Book(Eighths)).BeamLayouts.Length, Laid(Book(Bare(Eighths))).BeamLayouts.Length);
        Assert.Equal(4, Laid(Book(Bare(Eighths))).BeamLayouts.Length);
    }

    [Fact]
    public void Nested_OnlyTheBareOneGoes()
    {
        var laid = Laid(Book("tuplet 3/2 { c'8 tuplet 3/2 bare { d16 e f } g8 } f4 g a |"));
        Assert.Single(laid.TupletBracketLayouts);
    }

    [Fact]
    public void ADrumChart_TakesItToo()
    {
        const string chart = "part kit { clef percussion }\nsection A { kit { drums { { tuplet 3/2 bare { hh8 hh hh } hh4 hh hh } { bd1 } } | } }\nform { A }\nscore { staff kit }\n";
        Assert.Empty(Laid(chart).TupletBracketLayouts);
    }

    [Fact]
    public void TheSound_IsTheSame()
    {
        static string Heard(string book) => string.Join(" ", new MidiExporter().Export(SyntaxTree.Parse(book)).Tracks.SelectMany(t => t.Notes)
            .OrderBy(n => n.StartTick).Select(n => $"{n.Pitch}@{n.StartTick}+{n.DurationTicks}"));
        Assert.Equal(Heard(Book(Eighths)), Heard(Book(Bare(Eighths))));
    }

    [Fact]
    public void TheTwin_OmitsBoth_ForThatTupletAlone()
    {
        string twin = new LilyPondExporter().Export(SyntaxTree.Parse(Book("tuplet 3/2 bare { c'8 d e } tuplet 3/2 { f g a } b2 |")));
        Assert.Single(Regex.Matches(twin, @"\\once \\omit TupletNumber \\once \\omit TupletBracket \\tuplet 3/2"));
        Assert.Equal(2, Regex.Matches(twin, @"\\tuplet 3/2").Count);
    }

    private const string Mixed = "tuplet 3/2 bare { c'8 d e } tuplet 3/2 { f g a } b2 |";

    [Fact]
    public void TheMusicXml_SaysNeither_OnTheStart()
    {
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(Book(Mixed))).ToXml().ToString();
        var starts = Regex.Matches(xml, @"<tuplet [^>]*type=""start""[^>]*>").Select(m => m.Value).ToList();
        Assert.Equal(2, starts.Count);
        Assert.Contains("bracket=\"no\"", starts[0]);
        Assert.Contains("show-number=\"none\"", starts[0]);
        Assert.DoesNotContain("show-number", starts[1]);
        Assert.Equal(6, Regex.Matches(xml, "<time-modification>").Count);
    }

    [Fact]
    public void AndComesBackFromIt()
    {
        var tree = SyntaxTree.Parse(Book(Mixed));
        string xml = new MusicXmlExporter().Export(tree).ToXml().ToString();
        var (back, _) = new LilySharp.Core.MusicXmlImport.MusicXmlImporter().Import(xml);
        var tuplets = SyntaxTree.Parse(back).GetNodes<TupletExpressionSyntax>().ToList();
        Assert.Equal(2, tuplets.Count);
        Assert.True(tuplets[0].IsBare, back);
        Assert.False(tuplets[1].IsBare, back);
    }

    private static string Stepped(string text, int at, int dir, out string? said)
    {
        var tree = SyntaxTree.Parse(text);
        var r = NoteStepper.Step(text, tree, [(at, at)], dir, semitone: true);
        said = r.Message;
        foreach (var e in r.Edits.OrderByDescending(e => e.Start))
            text = text[..e.Start] + e.NewText + text[e.End..];
        return text;
    }

    [Fact]
    public void TheKeys_WriteAndTakeAwayTheWord()
    {
        string plain = Book("tuplet 3/2 { c'8 d e } f4 g a |"), bare = Book("tuplet 3/2 bare { c'8 d e } f4 g a |");
        int word = plain.IndexOf("tuplet", System.StringComparison.Ordinal) + 2;
        Assert.Equal(bare, Stepped(plain, word, +1, out var on));
        Assert.Contains("no number and no bracket", on);
        Assert.Equal(plain, Stepped(bare, word, -1, out _));
        Assert.Equal(plain, Stepped(bare, bare.IndexOf("bare", System.StringComparison.Ordinal) + 1, -1, out _));
        // At either end it stays, and says so.
        Assert.Equal(bare, Stepped(bare, word, +1, out var again));
        Assert.Contains("already", again);
        Assert.Equal(plain, Stepped(plain, word, -1, out var off));
        Assert.Contains("already", off);
    }

    [Fact]
    public void OnTheRatio_TheKeysStepTheRatio_TheWordStaying()
    {
        string bare = Book("tuplet 3/2 bare { c'8 d e } f4 g a |");
        string after = Stepped(bare, bare.IndexOf("3/2", System.StringComparison.Ordinal), +1, out _);
        Assert.Contains("tuplet 4/3 bare {", after);
    }
}
