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
/// Music written into a score by place (<see cref="PartWriter"/>; owner's proposal 2026-11-11):
/// a part's section, some of its bars, its words, a new part — all placed, or every problem said.
/// </summary>
public class PartWriterTests
{
    private const string Song = """
        octave absolute
        part gtr "G" { instrument guitar }
        part voc "V" { lyrics w }
        phrase gF { r8 <f a c' f'>8 r8 q r8 q r8 q | }
        phrase two { c1 | d1 | }
        section A {
          gtr {
            R | R*2 |
            r8 <ees bes>8@mp r4 r2 |
            gF two
          }
          voc { c'1 | }
          lyrics w { la | }
        }
        section B {
          gtr { gF gF }
        }
        form { A B A }
        score { staff gtr  staff voc lyrics w }
        score big {
          staff gtr
          staff voc
          lyrics w
        }
        """;

    private static string Unix(string? text) => (text ?? "").Replace("\r\n", "\n");

    private static string Written(params PartWriter.Write[] writes)
    {
        var result = PartWriter.Apply(Song, writes);
        Assert.Empty(result.Problems);
        Assert.NotNull(result.Text);
        Assert.False(SyntaxTree.Parse(result.Text!).HasErrors, result.Text);
        return Unix(result.Text);
    }

    [Fact]
    public void APartsSectionIsReplacedWhole_OnOneLineOrALineABar()
    {
        string text = Written(new PartWriter.Write("gtr", "B", "c1 |\nd1 |"), new PartWriter.Write("voc", "A", "d'1 |"));
        Assert.Contains("section B {\n  gtr {\n    c1 |\n    d1 |\n  }\n}", text);
        Assert.Contains("  voc { d'1 | }\n  lyrics w { la | }", text);
        // The other part of the section, and the phrases, are as they were.
        Assert.Contains("    gF two\n  }", text);
        Assert.Contains("phrase gF { r8 <f a c' f'>8 r8 q r8 q r8 q | }", text);
    }

    // Bars are counted as the section is written: a run closed by a bar line, a counted rest's bars,
    // a phrase's. A phrase of one bar called there is replaced by the notes given.
    [Fact]
    public void SomeBarsAreReplaced_WhereTheyBeginAndEndWithWhatIsWritten()
    {
        Assert.Contains("    e1 | two\n", Written(new PartWriter.Write("gtr", "A", "e1 |", "5")));
        Assert.Contains("    gF e1 | f1 |\n", Written(new PartWriter.Write("gtr", "A", "e1 | f1 |", "6-7")));
        Assert.Contains("    R | e1 | f1 |\n    r8 <ees bes>8@mp", Written(new PartWriter.Write("gtr", "A", "e1 | f1 |", "2-3")));
        Assert.Contains("    e1 | R*2 |\n", Written(new PartWriter.Write("gtr", "A", "e1 |", "1")));
        Assert.Contains("  gtr { e1 | gF }", Written(new PartWriter.Write("gtr", "B", "e1 |", "1")));
    }

    [Fact]
    public void ASingingPartsWordsGoWithItsNotes()
    {
        var result = PartWriter.Apply(Song, [new PartWriter.Write("voc", "A", "d'2 e'2 |", null, "do re |")]);
        Assert.Contains("  voc { d'2 e'2 | }\n  lyrics w { do re | }", Unix(result.Text));
        Assert.Contains("voc, A: its words (`lyrics w`) written.", result.Done);
        // A part with no lyrics row has no words to write.
        var none = PartWriter.Apply(Song, [new PartWriter.Write("gtr", "B", "c1 | d1 |", null, "la la |")]);
        Assert.Null(none.Text);
        Assert.Contains(none.Problems, p => p.Contains("`gtr` names no lyrics row"));
    }

    // Every problem at once, and nothing changed: one more call mends them all.
    [Fact]
    public void WhatDoesNotFitIsSaid_AllOfItAtOnce_AndNothingIsWritten()
    {
        var result = PartWriter.Apply(Song,
        [
            new PartWriter.Write("gtr", "A", "e1 |", "2"),
            new PartWriter.Write("gtr", "A", "e1 |", "6"),
            new PartWriter.Write("gtr", "A", "e1 |", "9"),
            new PartWriter.Write("nope", "A", "c1 |"),
            new PartWriter.Write("gtr", "Z", "c1 |"),
            new PartWriter.Write("gtr", "B", " "),
            new PartWriter.Write("gtr", "B", "c1 |", "x"),
            // Placed, had the others been: not written either.
            new PartWriter.Write("voc", "A", "d'1 |"),
        ]);
        Assert.Null(result.Text);
        Assert.Empty(result.Done);
        Assert.Equal(7, result.Problems.Count);
        Assert.Contains("gtr, A, bars 2: they cut through a rest of 2 bars (bars 2-3). Take those bars whole, or write the section whole.", result.Problems);
        Assert.Contains("gtr, A, bars 6: they cut through the phrase `two`, which is bars 6-7 there. Take those bars whole, or write the section whole.", result.Problems);
        Assert.Contains("gtr, A, bars 9: it has 7 bars there.", result.Problems);
        Assert.Contains(result.Problems, p => p.StartsWith("nope, A: the score has no part `nope` (its parts are gtr, voc)."));
        Assert.Contains(result.Problems, p => p.StartsWith("gtr, Z: the score has no section `Z` (its sections are A, B)."));
        Assert.Contains(result.Problems, p => p.StartsWith("gtr, B: no `music` to write."));
        Assert.Contains(result.Problems, p => p.Contains("`bars` is a bar or a range of them"));
    }

    [Fact]
    public void TwoWritesIntoOnePlace_OrABlockWithARepeat_AreSaid()
    {
        var twice = PartWriter.Apply(Song, [new PartWriter.Write("gtr", "A", "e1 |", "1"), new PartWriter.Write("gtr", "A", "f1 |")]);
        Assert.Null(twice.Text);
        Assert.Contains("write over each other", Assert.Single(twice.Problems));
        string repeated = Song.Replace("gtr { gF gF }", "gtr { |: c1 | :| }");
        Assert.NotEqual(Song, repeated);
        var opaque = PartWriter.Apply(repeated, [new PartWriter.Write("gtr", "B", "e1 |", "1")]);
        Assert.Contains("its bars cannot be told apart here — it holds a repeat (`|:` … `:|`)", Assert.Single(opaque.Problems));
        Assert.Contains("an ending, a `%` repeat or a group with bar lines inside it",
            Assert.Single(PartWriter.Apply(Song.Replace("gtr { gF gF }", "gtr { c1 | % | }"), [new PartWriter.Write("gtr", "B", "e1 |", "1")]).Problems));
        // Whole, the block is written all the same.
        Assert.Contains("  gtr { e1 | f1 | }", Unix(PartWriter.Apply(repeated, [new PartWriter.Write("gtr", "B", "e1 | f1 |")]).Text));
    }

    // The answer says where each write went, and that a section the form plays twice changes both times.
    [Fact]
    public void TheAnswerSaysWhatWasWrittenWhere()
    {
        var result = PartWriter.Apply(Song, [new PartWriter.Write("gtr", "A", "e1 |", "5"), new PartWriter.Write("gtr", "B", "c1 | d1 |")]);
        Assert.Equal(
        [
            "gtr, A, bars 5: replaced (A is played 2 times in the form: each time changes).",
            "gtr, B: the whole of it replaced.",
        ], result.Done);
    }

    [Fact]
    public void ANewPartIsDeclared_GivenItsMusic_AndAStaffInEveryScoreThatShowsThePartItFollows()
    {
        var result = PartWriter.Apply(Song,
            [new PartWriter.Write("str", "A", "c''1 | d''1 | e''1 | f''1 | g''1 | a''1 | b''1 |"), new PartWriter.Write("str", "B", "c''1 | d''1 |")],
            [new PartWriter.NewPart("str", "Strings", "Str.", "instrument violin\nmidiInstrument \"string ensemble 1\"", "gtr")]);
        Assert.Empty(result.Problems);
        string text = Unix(result.Text);
        Assert.False(SyntaxTree.Parse(result.Text!).HasErrors, text);
        Assert.Contains("part gtr \"G\" { instrument guitar }\npart str \"Strings\" \"Str.\" {\n  instrument violin\n  midiInstrument \"string ensemble 1\"\n}\npart voc", text);
        Assert.Contains("    gF two\n  }\n  str { c''1 | d''1 | e''1 | f''1 | g''1 | a''1 | b''1 | }\n  voc { c'1 | }", text);
        Assert.Contains("  gtr { gF gF }\n  str { c''1 | d''1 | }\n}", text);
        Assert.Contains("score { staff gtr  staff str  staff voc lyrics w }", text);
        Assert.Contains("score big {\n  staff gtr\n  staff str\n  staff voc\n  lyrics w\n}", text);
        Assert.Contains("str: a new part, after gtr, with a staff in 2 scores.", result.Done);

        // After a singing part, its staff stands after that part's words.
        var last = PartWriter.Apply(Song, [new PartWriter.Write("str", "B", "c''1 | d''1 |")], [new PartWriter.NewPart("str", "Strings")]);
        Assert.Contains("score { staff gtr  staff voc lyrics w  staff str }", Unix(last.Text));
        Assert.Contains("  staff voc\n  lyrics w\n  staff str\n}", Unix(last.Text));
        Assert.Contains("part voc \"V\" { lyrics w }\npart str \"Strings\" {\n}", Unix(last.Text));
    }

    [Fact]
    public void ANewPartThatCannotBe_IsSaid()
    {
        PartWriter.Write[] music = [new("gtr", "B", "c1 | d1 |")];
        Assert.Contains("The score has a part `gtr` already", Assert.Single(PartWriter.Apply(Song, music, [new PartWriter.NewPart("gtr")]).Problems));
        Assert.Contains("is to stand after `drums`, and the score has no such part", PartWriter.Apply(Song, music, [new PartWriter.NewPart("str", After: "drums")]).Problems[0]);
        Assert.Contains("needs a name of letters and digits", PartWriter.Apply(Song, music, [new PartWriter.NewPart("my part")]).Problems[0]);
        Assert.Contains(PartWriter.Apply(Song, music, [new PartWriter.NewPart("str")]).Problems, p => p.StartsWith("str: a new part with no music."));
        Assert.Contains("Nothing to write", Assert.Single(PartWriter.Apply(Song, []).Problems));
        Assert.Contains("has errors", Assert.Single(PartWriter.Apply("part x {", music).Problems));
    }

    // A score that writes its parts' sections in the parts: read and written there.
    [Fact]
    public void AScoreWrittenByPart_IsWrittenInItsParts()
    {
        const string byPart = """
            octave absolute
            part gtr "G" {
              instrument guitar
              section A { c1 | d1 | }
              section B { e1 | f1 | }
            }
            part bass "B" {
              instrument bass
              section A { c,1 | d,1 | }
            }
            score { staff gtr  staff bass }
            """;
        var result = PartWriter.Apply(byPart,
            [new PartWriter.Write("gtr", "A", "g1 | a1 |"), new PartWriter.Write("bass", "B", "e,1 | f,1 |"), new PartWriter.Write("gtr", "B", "b1 |", "2"),
             new PartWriter.Write("keys", "A", "c'1 | d'1 |")],
            [new PartWriter.NewPart("keys", "Keys", null, "instrument piano-right", "gtr")]);
        Assert.Empty(result.Problems);
        string text = Unix(result.Text);
        Assert.False(SyntaxTree.Parse(result.Text!).HasErrors, text);
        Assert.Contains("  section A { g1 | a1 | }\n  section B { e1 | b1 | }\n}", text);
        Assert.Contains("  section A { c,1 | d,1 | }\n  section B { e,1 | f,1 | }\n}", text);
        Assert.Contains("part keys \"Keys\" {\n  instrument piano-right\n  section A { c'1 | d'1 | }\n}\npart bass", text);
        Assert.Contains("score { staff gtr  staff keys  staff bass }", text);
    }
}
