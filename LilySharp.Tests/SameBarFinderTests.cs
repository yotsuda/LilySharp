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
using LilySharp.Core.Editing;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The bar under a caret, found in the other parts and chords rows that write its section
/// (<see cref="SameBarFinder"/>; owner's request 2026-11-14): the place each opens the same bar
/// at, counted from the bar lines it writes.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SameBarFinderTests
{
    private const string Book =
        "octave absolute\ntime 4/4\n"
        + "part m { section S { c''1 | d''1 | e''1 | } section T { f''1 | } }\n"
        + "part b { section S { c1 | d1 | e1 | } section T { f1 | } }\n"
        + "chords prog { section S { C | G | Am | } }\n"
        + "form { S T }\nscore { chords prog staff m staff b }\n";

    private static string After(int offset) => Book.Substring(offset).Split(' ')[0];

    [Fact]
    public void TheSameBarOfEveryOtherVoiceOfTheSection()
    {
        var answer = SameBarFinder.At(SyntaxTree.Parse(Book), Book.IndexOf("d''1"))!;
        Assert.Equal(("S", 2, "part 'm'"), (answer.Section, answer.Bar, answer.Voice));
        // The other voices, in document order from the caret's own: the bass, then the row.
        Assert.Equal(new[] { ("part 'b'", "d1"), ("chords 'prog'", "G") },
            answer.Others.Select(o => (o.Voice, After(o.Offset))).ToArray());
    }

    /// <summary>
    /// Every voice's bars as text (lilysharp/voiceBars, Orecchiante's request 23): the shape
    /// Orecchiante writes — by-section part blocks, a chords row, a kit of grooves and <c>%</c>,
    /// a bar of two drum voices — each bar its own text, its bar line left out.
    /// </summary>
    [Fact]
    public void AllBars_EveryVoicesBarsAsText()
    {
        const string chart = "octave absolute\ntime 4/4\n"
            + "part bass { instrument bass }\npart kit { clef percussion }\n"
            + "phrase hatsA { hh8 8 8 8 8 8 8 8 | }\nphrase kickA { bd4 4 4 4 | }\n"
            + "phrase beatA { drums { hatsA kickA } }\n"
            + "section A {\n  chords prog {\n    C | G7 | C | C | break\n  }\n"
            + "  bass {\n    c8 c g c c c g c | g,2 b, | c1 | c1~ |\n  }\n"
            + "  kit {\n    beatA | % | beatA | drums { { hh4 4 4 4 } { bd2 2 } } |\n  }\n}\n"
            + "section B {\n  chords prog { F | C | }\n  bass { f1 | c1 | }\n  kit { beatA | % | }\n}\n"
            + "form { A B A }\nscore { chords prog staff bass staff kit }\n";
        var tree = SyntaxTree.Parse(chart);
        Assert.False(tree.HasErrors);
        var voices = SameBarFinder.AllBars(tree);
        string[] Texts(string section, string voice)
            => voices.Single(v => v.Section == section && v.Voice == voice).Bars
                .Select(b => b.Start < 0 ? "?" : chart[b.Start..b.End].Trim()).ToArray();
        Assert.Equal(new[] { "C", "G7", "C", "C" }, Texts("A", "chords 'prog'"));
        Assert.Equal(new[] { "c8 c g c c c g c", "g,2 b,", "c1", "c1~" }, Texts("A", "part 'bass'"));
        Assert.Equal(new[] { "beatA", "%", "beatA", "drums { { hh4 4 4 4 } { bd2 2 } }" }, Texts("A", "part 'kit'"));
        Assert.Equal(new[] { "F", "C" }, Texts("B", "chords 'prog'"));
        Assert.Equal(new[] { "beatA", "%" }, Texts("B", "part 'kit'"));
    }

    [Fact]
    public void TheFirstBarOpensAfterTheVoicesBrace_AndTheSectionIsTheCaretsOwn()
    {
        var answer = SameBarFinder.At(SyntaxTree.Parse(Book), Book.IndexOf("f1"))!;
        Assert.Equal(("T", 1, "part 'b'"), (answer.Section, answer.Bar, answer.Voice));
        var other = Assert.Single(answer.Others);       // the row writes no section T
        Assert.Equal(("part 'm'", "f''1"), (other.Voice, After(other.Offset)));
    }

    [Fact]
    public void FromARowToTheParts()
    {
        var answer = SameBarFinder.At(SyntaxTree.Parse(Book), Book.IndexOf("Am"))!;
        Assert.Equal(("S", 3, "chords 'prog'"), (answer.Section, answer.Bar, answer.Voice));
        Assert.Equal(new[] { "e''1", "e1" }, answer.Others.Select(o => After(o.Offset)).ToArray());
    }

    [Fact]
    public void ACaretOutsideEverySectionHasNoBar()
        => Assert.Null(SameBarFinder.At(SyntaxTree.Parse(Book), Book.IndexOf("form")));

    /// <summary>The words' bars too (owner's request 2026-11-17): a lyrics cell, by part or by section,
    /// is a place to go to — and to come from.</summary>
    [Fact]
    public void TheWordsOfTheBar_ByPartAndBySection()
    {
        const string byPart = "octave absolute\ntime 4/4\n"
            + "part m { section S { c''1 | d''1 | e''1 | } }\n"
            + "lyrics w { section S { one | two | three | } }\n"
            + "form { S }\nscore { staff m lyrics w on m }\n";
        var answer = SameBarFinder.At(SyntaxTree.Parse(byPart), byPart.IndexOf("d''1"))!;
        var words = Assert.Single(answer.Others);
        Assert.Equal("two", byPart.Substring(words.Offset).Split(' ')[0]);
        // …and from the words, to the music.
        var back = SameBarFinder.At(SyntaxTree.Parse(byPart), byPart.IndexOf("three"))!;
        Assert.Equal(3, back.Bar);
        Assert.Equal("e''1", byPart.Substring(Assert.Single(back.Others).Offset).Split(' ')[0]);

        const string bySection = "octave absolute\ntime 4/4\npart m { clef treble }\n"
            + "section S { m { c''1 | d''1 | e''1 | } lyrics w { one | two | three | } }\n"
            + "form { S }\nscore { staff m lyrics w on m }\n";
        var again = SameBarFinder.At(SyntaxTree.Parse(bySection), bySection.IndexOf("e''1"))!;
        Assert.Equal("three", bySection.Substring(Assert.Single(again.Others).Offset).Split(' ')[0]);
    }
}