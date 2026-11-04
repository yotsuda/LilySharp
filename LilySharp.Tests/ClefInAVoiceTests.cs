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
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Rendering;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A clef is the staff's, whichever voice writes it and wherever (LilySharp-Omr REQUESTS #22):
/// every voice's heads stand in it from its moment, and it is drawn there. A middle c stands at
/// −6 in treble and at +6 in bass.
/// </summary>
[Trait("Category", "Unit")]
public class ClefInAVoiceTests
{
    private const int InTreble = -6, InBass = 6;

    private static string Book(string music)
        => "octave absolute\ntime 4/4\npart p { clef treble }\nsection A { p { " + music + " } }\nform { A }\nscore { staff p }\n";

    private static Staff Staff(string music)
    {
        var tree = SyntaxTree.Parse(Book(music));
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        return new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!).StaffGroups.Single().Staves.Single();
    }

    private static int[] Heads(Staff staff, int voice)
        => staff.Voices[voice].Measures.SelectMany(m => m.Items).OfType<NoteItem>().Select(n => n.StaffPosition).ToArray();

    [Fact]
    public void ALowerVoicesClef_MovesTheUpperVoice_FromThatMoment_ItsHeadThenIncluded()
    {
        var staff = Staff("voice { c'4 c' c' c' | } { s2 clef bass c'2 | }");
        Assert.Equal(new[] { InTreble, InTreble, InBass, InBass }, Heads(staff, 0));
        Assert.Equal(new[] { InBass }, Heads(staff, 1));
    }

    [Fact]
    public void AnUpperVoicesClef_MovesTheLowerVoice_FromThatMoment()
        => Assert.Equal(new[] { InTreble, InTreble, InBass, InBass },
            Heads(Staff("voice { c'2 clef bass c' | } { c'4 c' c' c' | }"), 1));

    [Fact]
    public void ALowerVoicesClef_AtABarHead_MovesTheUpperVoicesHeadThere()
        => Assert.Equal(new[] { InTreble, InBass }, Heads(Staff("voice { c'1 | c'1 | } { c'1 | clef bass c'1 | }"), 0));

    [Fact]
    public void ALowerVoicesClef_AtTheSpansHead_MovesTheUpperVoicesHeadThere()
        => Assert.Equal(new[] { InBass }, Heads(Staff("voice { c'1 | } { clef bass c'1 | }"), 0));

    /// <summary>A phrase may hold the clef: the span cannot tell from its own text.</summary>
    [Fact]
    public void AClefInAPhrase_AtABarHead_MovesTheUpperVoicesHeadThere()
    {
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\nphrase low { clef bass c'1 | }\npart p { clef treble }\n"
            + "section A { p { voice { c'1 | c'1 | } { c'1 | low } } }\nform { A }\nscore { staff p }\n");
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var staff = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!).StaffGroups.Single().Staves.Single();
        Assert.Equal(new[] { InTreble, InBass }, Heads(staff, 0));
    }

    /// <summary>The same wait serves the key: until 2026-11-04 the upper voice's head at the bar
    /// read the old key when the lower voice wrote the new one after its bar line.</summary>
    [Fact]
    public void ALowerVoicesKey_AtABarHead_GovernsTheUpperVoicesHeadThere()
    {
        var staff = Staff("voice { c'1 | fis'1 | } { c'1 | key d major c'1 | }");
        var head = staff.Voices[0].Measures[1].Items.OfType<NoteItem>().Single();
        Assert.Null(head.Accidental);
    }

    /// <summary>The ink and its place do not depend on which voice wrote the clef. Written in
    /// the upper voice this form was always right; the columns are the staff's.</summary>
    [Theory]
    [InlineData("voice { tuplet 3/2 { c'8 d' e' } clef bass r2. | } { s4 c'2. | }",
                "voice { tuplet 3/2 { c'8 d' e' } r2. | } { s4 clef bass c'2. | }")]
    [InlineData("voice { c'8 d' e' f' g' a' b' c'' | clef bass c'1 | } { c'1 | c'1 | }",
                "voice { c'8 d' e' f' g' a' b' c'' | c'1 | } { c'1 | clef bass c'1 | }")]
    public void TheClefIsPaidFor_WhicheverVoiceWritesIt(string upper, string lower)
    {
        // At many lengths: a break gate reading the wrong voice breaks some line elsewhere.
        static string Long(string music, int bars) => string.Concat(Enumerable.Repeat(music + " clef treble c'8 d' e' f' g' a' b' c'' |\n", bars));
        for (int bars = 1; bars <= 12; bars++)
            Assert.Equal(Ink(Book(Long(upper, bars))), Ink(Book(Long(lower, bars))));
    }

    private static int Ties(string music)
    {
        var tree = SyntaxTree.Parse(Book(music));
        Assert.DoesNotContain(SemanticValidation.Run(tree), d => d.Message.Contains("does not repeat the tied pitch"));
        return new TieDetector().DetectTies(new MeasureCollector().Collect(tree, "p")).Count();
    }

    /// <summary>A tie binds pitches, not staff positions: LilyPond draws it over a clef change.</summary>
    [Theory]
    [InlineData("c'1~ | clef bass c'1 |")]
    [InlineData("c'2~ clef bass c'2 |")]
    [InlineData("voice { c'1~ | c'1 | } { c1 | clef bass c1 | }")]
    public void ATie_CrossesAClefChange(string music)
        => Assert.True(Ties(music) >= 1);

    [Fact]
    public void AChordsTie_CrossesAClefChange()
        => Assert.Equal(2, Ties("<c' e'>1~ | clef bass <c' e'>1 |"));

    [Fact]
    public void ANotesTie_ReachesAChordOverAClefChange()
        => Assert.Equal(1, Ties("c'1~ | clef bass <c' e'>1 |"));

    [Fact]
    public void AChordsTie_ReachesANoteOverAClefChange()
        => Assert.Equal(1, Ties("<c' e'>1~ | clef bass c'1 |"));

    /// <summary>Another spelling is still no tie, clef or none.</summary>
    [Theory]
    [InlineData("cis'1~ | des'1 |")]
    [InlineData("cis'1~ | clef bass des'1 |")]
    public void AnotherSpelling_IsNoTie(string music)
    {
        var tree = SyntaxTree.Parse(Book(music));
        Assert.Contains(SemanticValidation.Run(tree), d => d.Message.Contains("does not repeat the tied pitch"));
        Assert.Empty(new TieDetector().DetectTies(new MeasureCollector().Collect(tree, "p")));
    }

    /// <summary>A tied head keeps no accidental over the clef, as under one (LilyPond 2.26.0, Lab
    /// sessions/p1099/clef/a.ly); the next bar's first head has one.</summary>
    [Fact]
    public void ATiedAccidental_IsNotRepeated_OverAClefChange()
    {
        var heads = Staff("cis'1~ | clef bass cis'1 | cis'1 |").PrimaryVoice.Measures.SelectMany(m => m.Items).OfType<NoteItem>().ToList();
        Assert.Equal("sharp", heads[0].Accidental);
        Assert.Null(heads[1].Accidental);
        Assert.Equal("sharp", heads[2].Accidental);
    }

    [Fact]
    public void ATieOverARepeat_InAnotherClef_HangsItsRepeatTie()
    {
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\npart p { clef treble }\n"
            + "section A { p { c'1 | } }\nsection B { p { clef bass c'1~ | } }\nform { |: A B :| }\nscore { staff p }\n");
        Assert.DoesNotContain(SemanticValidation.Run(tree), d => d.Message.Contains("does not repeat the tied pitch"));
        var heads = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!).StaffGroups.Single().Staves.Single()
            .PrimaryVoice.Measures.SelectMany(m => m.Items).OfType<NoteItem>().ToList();
        Assert.True(heads[0].HasRepeatTie);
    }

    private static string[] Ink(string source)
    {
        var svg = SvgGenerator.Generate(SyntaxTree.Parse(source), new SvgRenderOptions { EmbedFont = false });
        return System.Text.RegularExpressions.Regex.Matches(svg, "<[^>]+>")
            .Select(m => System.Text.RegularExpressions.Regex.Replace(m.Value, "data-pos=\"[^\"]*\"", ""))
            .Where(tag => !tag.StartsWith("</") && !tag.StartsWith("<g"))
            .Order(System.StringComparer.Ordinal).ToArray();
    }

    [Fact]
    public void AVoicesClef_HoldsAfterTheSpan()
    {
        var staff = Staff("voice { c'1 | } { s2 clef bass c'2 | } c'1 |");
        Assert.Equal(InBass, Heads(staff, 0)[^1]);
    }

    [Fact]
    public void TwoVoicesWritingOneClefAtOneMoment_DrawIt_Once()
    {
        var staff = Staff("voice { c'2 clef bass c' | } { c'2 clef bass c' | }");
        Assert.Single(staff.Voices.SelectMany(v => v.Measures).SelectMany(m => m.Items).OfType<ClefChangeItem>());
    }

    [Fact]
    public void AClefOpeningATuplet_IsReadAndDrawn_BeforeTheBracket()
    {
        var tree = SyntaxTree.Parse(Book("tuplet 3/2 { clef bass c'8 c' c' } r2. |"));
        var score = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        var staff = score.StaffGroups.Single().Staves.Single();
        Assert.Equal(new[] { InBass, InBass, InBass }, Heads(staff, 0));
        var items = staff.PrimaryVoice.Measures[0].Items;
        Assert.IsType<ClefChangeItem>(items[0]);
        Assert.IsType<NoteItem>(items[score.TupletBrackets.Single().StartNoteIndex]);
    }

    [Fact]
    public void AClefInsideATuplet_StandsAtItsMember()
        => Assert.Equal(new[] { InTreble, InBass, InBass }, Heads(Staff("tuplet 3/2 { c'8 clef bass c' c' } r2. |"), 0));

    [Fact]
    public void ALineOpens_InTheClefALowerVoiceLeft()
    {
        Assert.Equal(ClefType.Bass, SharedRenderer.ResolveClefAt(Staff("voice { c'1 | } { s2 clef bass c'2 | } c'1 |"), 1));
        Assert.Equal(ClefType.Bass, SharedRenderer.ResolveClefAt(Staff("voice { c'1 | c'1 | } { c'1 | clef bass c'1 | }"), 1));
    }

    [Fact]
    public void ALineOpens_InTheLatestClef_WhicheverVoiceWroteIt()
    {
        Assert.Equal(ClefType.Treble, SharedRenderer.ResolveClefAt(
            Staff("voice { c'4 clef bass c'2. | } { c'2 clef treble c'2 | } c'1 |"), 1));
        Assert.Equal(ClefType.Bass, SharedRenderer.ResolveClefAt(
            Staff("voice { c'2 clef bass c'2 | } { c'4 clef alto c'2. | } c'1 |"), 1));
    }

    [Fact]
    public void AClefLaterInTheBar_DoesNotOpenTheLine()
        => Assert.Equal(ClefType.Treble, SharedRenderer.ResolveClefAt(Staff("c'1 | voice { c'1 | } { s2 clef bass c'2 | }"), 1));

    /// <summary>The upper voice's note is the clef's neighbor, so the column stays in the springs
    /// and is paid for: read off the carrying voice alone, its spacer's onset left another column
    /// between them, and the clef stood on the upper voice's head.</summary>
    [Fact]
    public void AClefAfterASpacer_HasTheOtherVoicesNote_ForItsNeighbor()
    {
        var staff = Staff("voice { tuplet 3/2 { c'8 d' e' } r2. | } { s4 clef bass c'2. | }");
        var measures = staff.Voices.Select(v => v.Measures[0]).ToList();
        var clef = measures[1].Items.OfType<ClefChangeItem>().Single();
        var column = new ItemColumn(new List<MusicItem> { clef });
        Assert.Equal(Fraction.Zero, SpacingRules.LooseChangeLeftNeighborTiming(measures, column));
        Assert.Equal(new Fraction(1, 6), SpacingRules.LooseChangeLeftNeighborTiming(
            measures, column, new List<Staff> { staff, staff }));
    }

    /// <summary>On two staves the other staff's note is no neighbor.</summary>
    [Fact]
    public void AClefsNeighbor_IsOnItsOwnStaff()
    {
        var one = Staff("c'2 d' |");
        var other = Staff("s4 clef bass c'2. |");
        var measures = new List<Measure> { one.PrimaryVoice.Measures[0], other.PrimaryVoice.Measures[0] };
        var clef = measures[1].Items.OfType<ClefChangeItem>().Single();
        Assert.Equal(Fraction.Zero, SpacingRules.LooseChangeLeftNeighborTiming(
            measures, new ItemColumn(new List<MusicItem> { clef }), new List<Staff> { one, other }));
    }

    [Fact]
    public void TheBarBeforeALowerVoicesOpeningClef_MakesRoomForIt()
    {
        var src = Book("voice { c'1 | c'1 | } { c'1 | clef bass c'1 | }");
        var tree = SyntaxTree.Parse(src);
        var score = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        var voices = score.StaffGroups.Single().Staves.Single().Voices;
        Assert.Same(voices[1].Measures[1], MultiStaffLayouter.MeasureForTheClosingGap(score, voices[0].Measures[1], 1));
        Assert.Same(voices[0].Measures[0], MultiStaffLayouter.MeasureForTheClosingGap(score, voices[0].Measures[0], 0));
    }

    [Fact]
    public void AnEditAbove_RendersTheSameIncrementally()
    {
        var src = "title \"x\"\n" + Book("voice { c'4 c' c' c' | c'1 | } { s4 tuplet 3/2 { clef bass c'8 c' c' } c'2 | c'1 | } c'1 |");
        var options = new SvgRenderOptions { EmbedFont = false };
        var session = new IncrementalCompiler(SyntaxTree.Parse(src), options);
        session.Render();
        var edited = src.Replace("title \"x\"", "title \"y\"");
        Assert.Equal(SvgGenerator.Generate(SyntaxTree.Parse(edited), options).Replace("\r\n", "\n"),
            session.RenderIncremental(SyntaxTree.Parse(edited)).Replace("\r\n", "\n"));
    }

    [Fact]
    public void TheLilyPondFile_WritesTheClefWhereItStands()
    {
        var ly = new LilyPondExporter().Export(SyntaxTree.Parse(Book("tuplet 3/2 { clef bass c'8 c' c' } r2. |")));
        Assert.Matches(@"\\tuplet 3/2 \{\s*\\clef ""bass""", ly);
    }
}
