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

using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The score's sign column (<see cref="RepeatStream"/>, docs/inline-repeat-design.md §4 stage 1):
/// the printed bars of a form with every repeat sign, ending and navigation mark — the form's
/// and the music's, whichever voice writes it — and the bars played right after each bar.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RepeatStreamTests
{
    private static RepeatStream Stream(string source)
    {
        var root = SyntaxTree.Parse(source).GetRoot();
        return RepeatStream.Of(root, ScoreForms.Primary(root));
    }

    /// <summary>The printed bars in played order, 0-based.</summary>
    private static string Played(RepeatStream stream)
    {
        var bars = new List<int>();
        foreach (var segment in stream.Played)
            for (int b = segment.From; b < segment.To; b++)
                bars.Add(b);
        return string.Join(" ", bars);
    }

    private static string Next(RepeatStream stream, int bar) => string.Join(",", stream.Next(bar));

    private const string TwoParts = """
        time 4/4
        part mel { section A { MEL } }
        part bas { clef bass section A { BAS } }
        chords ch { section A { ROW } }
        form { A }
        score { chords ch staff mel staff bas }
        """;

    private static string Book(string mel, string bas = "c1 | g | g | g | c |", string row = "C | G | G | G | C |")
        => TwoParts.Replace("MEL", mel).Replace("BAS", bas).Replace("ROW", row);

    /// <summary>A repeat with two endings, written in one voice: the bar before the endings is
    /// followed by each of them, the first ending by the head, the second by what is printed
    /// after it — whichever voice writes the signs (§3.2).</summary>
    [Theory]
    [InlineData("|: c'1 | d1 | [1. d1 :| [2. d1 ] | c1 |", "c1 | g | g | g | c |", "C | G | G | G | C |")]
    [InlineData("c'1 | d1 | d1 | d1 | c1 |", "|: c1 | g | [1. g :| [2. g ] | c |", "C | G | G | G | C |")]
    [InlineData("c'1 | d1 | d1 | d1 | c1 |", "c1 | g | g | g | c |", "|: C | G | [1. G :| [2. G ] | C |")]
    // …and written by two voices, which is one set of signs.
    [InlineData("|: c'1 | d1 | [1. d1 :| [2. d1 ] | c1 |", "|: c1 | g | [1. g :| [2. g ] | c |", "C | G | G | G | C |")]
    public void TheBarBeforeTheEndingsIsFollowedByEachOfThem(string mel, string bas, string row)
    {
        var stream = Stream(Book(mel, bas, row));
        Assert.Equal(5, stream.Bars);
        Assert.Equal("0 1 2 0 1 3 4", Played(stream));
        Assert.Equal("1", Next(stream, 0));
        Assert.Equal("2,3", Next(stream, 1));
        Assert.Equal("0", Next(stream, 2));
        Assert.Equal("4", Next(stream, 3));
        Assert.Equal("", Next(stream, 4));
        Assert.False(stream.PrintedNextIsPlayedNext(2));
        Assert.True(stream.PrintedNextIsPlayedNext(1));
    }

    /// <summary>A pair the section boundary cuts, and a pair the form closes: the signs are read
    /// on the form's column, not section by section (§3.3).</summary>
    [Theory]
    [InlineData("section A { |: c'1 | d1 | } section B { d'1 :| e1 | }", "form { A B }", "0 1 2 0 1 2 3")]
    // B before A: the ':|' has no '|:' before it — the piece so far again — and the '|:' closes nothing.
    [InlineData("section A { |: c'1 | d1 | } section B { d'1 :| e1 | }", "form { B A }", "0 0 1 2 3")]
    // The form's ':|' closes the music's '|:'.
    [InlineData("section A { c'1 |: d1 | } section B { d'1 | e1 | }", "form { A B :| }", "0 1 2 3 1 2 3")]
    // The form opens where the music opens: ONE sign.
    [InlineData("section A { |: c'1 | d1 :| } section B { d'1 | e1 | }", "form { |: A :| B }", "0 1 0 1 2 3")]
    // A count on the music's sign.
    [InlineData("section A { |: c'1 | d1 :|*3 } section B { d'1 | }", "form { A B }", "0 1 0 1 0 1 2")]
    // A ':|:' in the music.
    [InlineData("section A { |: c'1 :|: d1 :| } section B { d'1 | }", "form { A B }", "0 0 1 1 2")]
    public void TheSignsAreReadOnTheFormsColumn(string sections, string form, string played)
    {
        var stream = Stream($"time 4/4\npart mel {{ {sections} }}\n{form}\nscore {{ staff mel }}");
        Assert.Equal(played, Played(stream));
    }

    /// <summary>The navigation marks of the music: a sign opens the bar ahead, a text closes the
    /// bar it is written in, and the route follows them (§3.6).</summary>
    [Theory]
    // bars: 0 | 1 segno… | 2 to coda | 3 ds al coda | 4 coda
    [InlineData("c'1 | segno d1 | e1 to coda | f1 ds al coda | coda g1 |", "0 1 2 3 1 2 4")]
    [InlineData("c'1 | d1 fine | e1 | f1 dc al fine |", "0 1 2 3 0 1")]
    [InlineData("c'1 | d1 | e1 dc |", "0 1 2 0 1 2")]
    // On the replay a repeat is played once, on its last pass.
    [InlineData("|: c'1 | [1. d1 :| [2. e1 ] | f1 dc |", "0 1 0 2 3 0 2 3")]
    public void TheMusicsJumpsAreFollowed(string music, string played)
    {
        var stream = Stream($"time 4/4\npart mel {{ section A {{ {music} }} }}\nform {{ A }}\nscore {{ staff mel }}");
        Assert.Equal(played, Played(stream));
    }

    /// <summary>A file with no form: the sections in declaration order, on one column.</summary>
    [Fact]
    public void AFileWithNoFormIsReadInDeclarationOrder()
    {
        var stream = Stream("time 4/4\npart mel { section A { c'1 |: d1 | } section B { d'1 :| e1 | } }\nscore { staff mel }");
        Assert.Equal(["A", "B"], stream.Plays.Select(p => p.Section));
        Assert.Equal("0 1 2 1 2 3", Played(stream));
    }

    /// <summary>A section played twice stands twice on the column, its signs with it.</summary>
    [Fact]
    public void ASectionPlayedTwiceWritesItsSignsTwice()
    {
        var stream = Stream("time 4/4\npart mel { section A { |: c'1 | d1 :| } section B { e'1 | } }\nform { A B A }\nscore { staff mel }");
        Assert.Equal(5, stream.Bars);
        Assert.Equal("0 1 0 1 2 3 4 3 4", Played(stream));
        Assert.Equal(new RepeatStream.Play("A", 3, 2), stream.Plays[2]);
        Assert.Equal(2, stream.PlayOf(4));
    }

    /// <summary>
    /// A book that writes its repeats in the FORM alone is read as it always was: the plays in
    /// the order <see cref="PlayedOrder.Expand"/> gives — the reading the page's ties, the
    /// MusicXML tie stops, the twin's repeat ties and the split-bar rule go by — over every form
    /// of every net book. (A block inside a block is left out: PlayedOrder does not read one.)
    /// </summary>
    [Fact]
    public void AFormOnlyBookKeepsItsPlayedOrder_OnEveryNetBook()
    {
        int forms = 0, withRepeats = 0;
        var differ = new List<string>();
        foreach (var path in CollectResumeTests.NetBooks())
        {
            var root = SyntaxTree.Parse(File.ReadAllText(path)).GetRoot();
            if (WritesRepeatsInTheMusic(root))
                continue;
            foreach (var form in ScoreForms.All(root))
            {
                var items = FormWalk.Read(form);
                if (items.OfType<FormWalk.Repeat>().Any(r => r.Children.OfType<FormWalk.Repeat>().Any()))
                    continue;
                var names = new List<string>();
                var printed = PlayedOrder.PlaysOf(items, names);
                var stream = RepeatStream.Of(root, form);
                var bars = stream.Plays.GroupBy(p => p.Section).ToDictionary(g => g.Key, g => g.First().Bars);
                // A section of no bars takes no place on the column.
                var expected = PlayedOrder.Expand(printed).Select(i => names[i]).Where(n => bars.GetValueOrDefault(n) > 0).ToList();
                var actual = new List<string>();
                foreach (var segment in stream.Played)
                    for (int b = segment.From; b < segment.To; b++)
                        if (stream.PlayOf(b) is var p && p >= 0 && stream.Plays[p].FirstBar == b)
                            actual.Add(stream.Plays[p].Section);
                forms++;
                if (expected.Count != printed.Count)
                    withRepeats++;
                if (!expected.SequenceEqual(actual))
                    differ.Add($"{Path.GetFileName(path)} form '{form.NameText}': expected [{string.Join(" ", expected)}] got [{string.Join(" ", actual)}]");
            }
        }
        Assert.True(differ.Count == 0, string.Join("\n", differ));
        // The net is not vacuous: forms were read, and some of them repeat or jump.
        Assert.True(forms > 100, $"forms read: {forms}");
        Assert.True(withRepeats >= 10, $"forms whose played order is not the printed one: {withRepeats}");
    }

    private static bool WritesRepeatsInTheMusic(SyntaxNode root)
    {
        foreach (var node in root.DescendantNodesOfKinds(RepeatStructureScopeValidator.RepeatStructureKinds))
        {
            bool mark = node is InlineVoltaSyntax or NavigationMarkSyntax
                || node is BarlineSyntax bar && SyntaxFacts.IsRepeatBarlineKind(bar.BarToken.Kind);
            if (mark && !node.IsInside<FormDeclarationSyntax>())
                return true;
        }
        return false;
    }
}
