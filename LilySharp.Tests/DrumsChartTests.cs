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
using LilySharp.Core.Midi;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>drums { hats  back  kick }</c> — a drum chart (owner's design 2026-10-11): lines
/// written as a drummer thinks, each a phrase or a <c>{ … }</c>, played together and drawn
/// in two voices, the hands' hits merged on stems up and the feet's on stems down.
/// </summary>
[Trait("Category", "Unit")]
public class DrumsChartTests
{
    private const string Phrases = """
        phrase hats  { hh8 8 8 8 8 8 8 8 | }
        phrase back  { r4 sn r sn | }
        phrase kick  { bd4 r bd r | }
        """;

    private static string Book(string music, string phrases = Phrases) => $$"""
        {{phrases}}
        part kit { clef percussion }
        section A { kit { {{music}} } }
        form { ~A }
        score { staff kit }
        """;

    private static MultiStaffScore Collect(string text)
    {
        var tree = SyntaxTree.Parse(text);
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        return SvgGenerator.CollectScore(tree, RenderSpecParser.Choose(RenderSpecParser.FindAll(tree), null));
    }

    [Fact]
    public void AChart_ParsesAsLines_EachWithItsSide()
    {
        var tree = SyntaxTree.Parse(Book("drums { hats  back  down kick  up { r2 sn4 sn } }"));
        Assert.Empty(tree.Diagnostics);
        var chart = tree.GetRoot().DescendantNodes().OfType<ParallelExpressionSyntax>().Single();
        Assert.True(chart.IsDrums);
        Assert.Equal(new bool?[] { null, null, true, false }, chart.DrumLines.Select(l => l.Down));
        Assert.Equal(4, chart.Voices.Count());
        // No line is a NAMED voice — the side words are not names.
        Assert.All(chart.NamedVoices, v => Assert.Null(v.Name));
        // The source round-trips, the zero-width braces adding nothing.
        Assert.Equal(Book("drums { hats  back  down kick  up { r2 sn4 sn } }"), tree.GetRoot().ToFullString());
    }

    [Fact]
    public void TheHandsMergeIntoChordsOnStemsUp_AndTheFeetGoDown()
    {
        var score = Collect(Book("drums { hats  back  kick }"));
        var voices = score.EnumerateStaves().Single().Staff.Voices;
        Assert.Equal(2, voices.Length);

        // Hands: eight hi-hat eighths, the snare joining the 3rd and 7th as a chord.
        var hands = voices[0].Measures[0].Items;
        Assert.Equal(8, hands.Length);
        Assert.Equal(new[] { 2, 6 }, hands.Select((it, i) => (it, i)).Where(p => p.it is ChordItem).Select(p => p.i));
        Assert.All(hands, it => Assert.Equal(new Fraction(1, 8), it.Duration));

        // Feet: the kick on 1 and 3, quarter rests between.
        var feet = voices[1].Measures[0].Items;
        Assert.Equal(new[] { "NoteItem", "RestItem", "NoteItem", "RestItem" }, feet.Select(i => i.GetType().Name));
    }

    [Fact]
    public void ATripletFill_KeepsItsBracket_InTheMergedVoice()
    {
        var score = Collect(Book(
            "drums { { hh4 hh r2 | }  { r2 tuplet 3/2 { sn8 sn sn  tomh tomml tomfh } | } }"));
        var hands = score.EnumerateStaves().Single().Staff.Voices.Single().Measures.Single().Items;
        Assert.Equal(8, hands.Length);   // hh hh, then the six triplet notes
        Assert.All(hands.Skip(2), it => Assert.Equal(new Fraction(1, 12), it.Duration));
        var bracket = Assert.Single(score.TupletBrackets);
        Assert.Equal((2, 7), (bracket.StartNoteIndex, bracket.EndNoteIndex));
    }

    [Fact]
    public void AChartInAPhrase_PlaysWhereverThePhraseDoes()
    {
        var score = Collect(Book("groove groove", Phrases + "\nphrase groove { drums { hats back kick } }"));
        var voices = score.EnumerateStaves().Single().Staff.Voices;
        Assert.Equal(2, voices.Length);
        Assert.Equal(2, voices[0].Measures.Length);
        Assert.Equal(4, voices[1].Measures[1].Items.Length);
    }

    [Fact]
    public void AFeetOnlyChart_IsOneVoice()
    {
        var score = Collect(Book("drums { kick  { r2 hhp4 r } }"));
        var only = score.EnumerateStaves().Single().Staff.Voices.Single().Measures[0].Items;
        Assert.Equal(new[] { "NoteItem", "RestItem", "ChordItem", "RestItem" }, only.Select(i => i.GetType().Name));
    }

    [Fact]
    public void TheMarksOfAMergedHit_RideItsChord()
    {
        // The open hi-hat's "○" and the snare's accent both land on the chord they joined.
        var score = Collect(Book("drums { { hh8 8 8 8 8 8 hho hh | }  { r4 sn r sn@accent | } }"));
        Assert.Contains(score.Articulations, a => a.ItemIndex == 6 && a.Type == ArticulationType.Accent);
        Assert.Contains(score.Articulations, a => a.ItemIndex == 6 && a.Type == ArticulationType.Flageolet);
    }

    [Fact]
    public void ADrumNotesOwnScript_IsDrawn()
    {
        // Until 2026-10-11 `sn4@accent` drew no accent at all — the script collector had no
        // arm for a drum note (found writing this chart). Plain and charted alike now.
        Assert.Contains(Collect(Book("r4 sn r sn4@accent |")).Articulations,
            a => a.Type == ArticulationType.Accent && a.ItemIndex == 3);
        Assert.Contains(Collect(Book("drums { { r4 sn r sn4@staccato | } }")).Articulations,
            a => a.Type == ArticulationType.Staccato && a.ItemIndex == 3);
    }

    [Fact]
    public void AFlam_DrawsItsGraceNote()
    {
        // Until 2026-10-11 a grace before a drum note was never walked: the drum arm did not
        // open its item, and LYS4020 called the body empty.
        var score = Collect(Book("r2. acciaccatura { sn16 } sn4 |"));
        var grace = Assert.Single(score.GraceNotes);
        Assert.Single(grace.Columns);
    }

    [Fact]
    public void AFlamInAChartLine_DrawsItsGrace_InFrontOfTheChordItJoined()
    {
        var score = Collect(Book("drums { hats  { r2. acciaccatura { sn16 } sn4 | }  kick }"));
        var grace = Assert.Single(score.GraceNotes);
        Assert.Single(grace.Columns);
        var hands = score.EnumerateStaves().Single().Staff.Voices[0].Measures[0].Items;
        Assert.IsType<ChordItem>(hands[grace.MainNoteItemIndex]);   // the sn with its hi-hat
    }

    [Fact]
    public void AFlam_PlaysItsGrace()
    {
        var notes = new MidiExporter().Export(SyntaxTree.Parse(Book("r2. acciaccatura { sn16 } sn4 |")))
            .Tracks.SelectMany(t => t.Notes).ToList();
        Assert.Equal(2, notes.Count);
        Assert.True(notes[0].StartTick < notes[1].StartTick);
        Assert.All(notes, n => Assert.Equal((9, 38), (n.Channel, n.Pitch)));
    }

    [Fact]
    public void ALineThatIsNotDrums_DrawsTheChartAsWritten()
    {
        // A pitched note is no kit piece: no merge, a voice a line, as `voice { } { }` draws.
        var score = Collect(Book("drums { hats  back  { c'1 | } }"));
        Assert.Equal(3, score.EnumerateStaves().Single().Staff.Voices.Length);
    }

    [Theory]
    [InlineData("drums { hats  back  kick }", 0)]                                  // a groove: fine
    [InlineData("drums { hats  back  { r4 tomh r tomh | } }", 2)]                  // three hands, twice
    [InlineData("drums { hats  { r2. hho4 | } }", 1)]                              // hh and hho at once
    [InlineData("drums { kick  { bda4 r bda r | }  { hhp4 r hhp r | } }", 2)]      // three feet, twice
    public void HitsNoDrummerCanPlay_AreWarned(string chart, int warnings)
    {
        var diagnostics = Core.Semantics.SemanticValidation.Run(SyntaxTree.Parse(Book(chart)));
        Assert.Equal(warnings, diagnostics.Count(d => d.Code == DiagnosticCodes.DrumsUnplayable));
    }

    [Fact]
    public void TheChart_PlaysEveryLine()
    {
        var notes = new MidiExporter().Export(SyntaxTree.Parse(Book("drums { hats  back  kick }")))
            .Tracks.SelectMany(t => t.Notes).ToList();
        Assert.Equal(8 + 2 + 2, notes.Count);
        Assert.All(notes, n => Assert.Equal(9, n.Channel));
    }

    [Fact]
    public void NotesWrittenStraightIntoAChart_AreAnError()
    {
        var tree = SyntaxTree.Parse(Book("drums { hh4 hh hh hh }"));
        Assert.Contains(tree.Diagnostics, d => d.Code == DiagnosticCodes.DrumsLineNotALine);
    }

    [Fact]
    public void APhraseNamedDrums_IsStillReachable()
    {
        var tree = SyntaxTree.Parse(Book("drums", Phrases + "\nphrase drums { hats }"));
        Assert.DoesNotContain(tree.GetRoot().DescendantNodes().OfType<ParallelExpressionSyntax>(), p => p.IsDrums);
        Assert.Contains(tree.GetRoot().DescendantNodes().OfType<VariableReferenceSyntax>(), r => r.Name.Text == "drums");
    }
}
