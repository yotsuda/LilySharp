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
using LilySharp.Core.LilyPond;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A repeat sign or an ending written in one voice of a section is the score's, and the
/// LilyPond twin writes it into EVERY voice's music — each part and each chord track is its own
/// expression, and one with no <c>\repeat volta</c> is played straight through by
/// <c>\unfoldRepeats</c> and the MIDI (docs/inline-repeat-design.md §3.2). MEASURED before
/// (2026-11-14, Lab sessions/p1243/stage0.md): only the voice that wrote the sign carried it.
/// The expected texts were read off Lab sessions/p1244/probe.
/// </summary>
[Trait("Category", "Unit")]
public sealed class InlineRepeatLilyPondTests
{
    private const string Head = "octave absolute\ntime 4/4\n";

    private static (string Ly, LilyPondExporter Exporter) Export(string book)
    {
        var exporter = new LilyPondExporter();
        return (exporter.Export(SyntaxTree.Parse(Head + book)), exporter);
    }

    private static string TwoParts(string m, string b, string rest = "form { S }\nscore { staff mel staff bas }")
        => $"part mel {{ section S {{ {m} }} }}\npart bas {{ section S {{ {b} }} }}\n{rest}";

    /// <summary>The body of one variable of the twin, on one line, without the section marks.</summary>
    private static string Var(string ly, string name)
    {
        var match = Regex.Match(ly, @"(?ms)^" + Regex.Escape(name) + @" = [^{]*\{\n(.*?)^\}");
        Assert.True(match.Success, $"no variable '{name}' in:\n{ly}");
        string body = Regex.Replace(match.Groups[1].Value, @"\\mark \\markup \\box ""[^""]*"" ?|\\time 4/4", "");
        return Regex.Replace(body, @"\s+", " ").Trim();
    }

    [Fact]
    public void EveryPartCarriesTheRepeatOnePartWrites()
    {
        var (ly, _) = Export(TwoParts("c''1 |: d''1 | e''1 :| f''1 |", "c1 | d1 | e1 | f1 |"));
        Assert.Equal(@"c''1 \repeat volta 2 { d''1 | e''1 } f''1 |", Var(ly, "mel"));
        Assert.Equal(@"c1 \repeat volta 2 { d1 | e1 | } f1 |", Var(ly, "bas"));
    }

    [Fact]
    public void TheEndingsAndTheCountAreWritten_InEveryPart()
    {
        var (ly, _) = Export(TwoParts("|: c''1 | [1. d''1 :| [2. e''1 ] | f''1 |", "c1 | d1 | e1 | f1 |"));
        Assert.Equal(@"\repeat volta 2 { c1 | } \alternative { { d1 | } { e1 | } } f1 |", Var(ly, "bas"));

        var (counted, _) = Export(TwoParts("|: c''1 :|*3 d''1 |", "c1 | d1 |"));
        Assert.Equal(@"\repeat volta 3 { c1 | } d1 |", Var(counted, "bas"));

        var (both, _) = Export(TwoParts("c''1 :|: d''1 :|*3 e''1 |", "c1 | d1 | e1 |"));
        Assert.Equal(@"\repeat volta 2 { c1 | } \repeat volta 3 { d1 | } e1 |", Var(both, "bas"));
    }

    /// <summary>The bar line after a last ending closes that ending's bar: it is not an empty
    /// bar (the twin wrote <c>s1 |</c> there until 2026-11-14, a bar the page does not draw).</summary>
    [Fact]
    public void TheLineAfterTheLastEnding_ClosesItsBar()
    {
        var (ly, _) = Export(TwoParts("|: c''1 | [1. d''1 :| [2. e''1 ] | f''1 |", "c1 | d1 | e1 | f1 |"));
        Assert.Equal(@"\repeat volta 2 { c''1 | } \alternative { { d''1 } { e''1 } } | f''1 |", Var(ly, "mel"));
    }

    [Fact]
    public void APairWrittenAcrossTwoSections_IsWrittenInEveryPart()
    {
        var (ly, _) = Export("""
            part mel { section A { c''1 |: d''1 | } section B { e''1 :| f''1 | } }
            part bas { section A { c1 | d1 | } section B { e1 | f1 | } }
            form { A B }
            score { staff mel staff bas }
            """);
        Assert.Equal(@"c1 \repeat volta 2 { d1 | e1 | } f1 |", Var(ly, "bas"));
    }

    [Fact]
    public void WithNoForm_TheSectionsStillCarryTheSigns()
    {
        var (ly, _) = Export(TwoParts("c''1 |: d''1 :|", "c1 | d1 |", "score { staff mel staff bas }"));
        Assert.Equal(@"c1 \repeat volta 2 { d1 | }", Var(ly, "bas"));
    }

    /// <summary>A part shorter than its section is padded, and its padding bars carry the signs;
    /// a part that does not write the section at all is all padding.</summary>
    [Fact]
    public void ThePaddingBarsCarryTheSigns()
    {
        var (shorter, _) = Export(TwoParts("|: c''1 | d''1 | e''1 :| f''1 |", "c1 | d1 |"));
        Assert.Equal(@"\repeat volta 2 { c1 | d1 | s1 | } s1 |", Var(shorter, "bas"));

        var (silent, _) = Export("""
            part mel { section A { c''1 | } section B { |: c''1 | [1. d''1 :| [2. e''1 ] | } }
            part bas { section A { c1 | } }
            form { A B }
            score { staff mel staff bas }
            """);
        Assert.Equal(@"c1 | \repeat volta 2 { s1 | } \alternative { { s1 | } { s1 | } }", Var(silent, "bas"));
    }

    [Fact]
    public void ARowsSignsAreTheParts_AndAPartsSignsTheRows()
    {
        var (fromRow, _) = Export("""
            part mel { section S { c''1 | d''1 | e''1 | f''1 | } }
            chords prog { section S { |: C | [1. G | Dm :| [2. G ] | } }
            form { S }
            score { chords prog staff mel }
            """);
        Assert.Equal(@"\repeat volta 2 { c''1 | } \alternative { { d''1 | e''1 | } { f''1 | } }", Var(fromRow, "mel"));
        // The row's own endings stand flat among its chords: written by the same pass.
        Assert.Equal(@"\repeat volta 2 { c1 | } \alternative { { g1 | d1:m } { g1 | } }", Var(fromRow, "progChords"));

        var (fromPart, _) = Export("""
            part mel { section S { c''1 |: d''1 | e''1 :| f''1 | } }
            chords prog { section S { C | G | Dm | G | } }
            form { S }
            score { chords prog staff mel }
            """);
        Assert.Equal(@"c1 \repeat volta 2 { g1 | d1:m | } g1 |", Var(fromPart, "progChords"));
    }

    [Fact]
    public void TheBySectionSpellingCarriesThemToo()
    {
        var (ly, _) = Export("""
            part mel { }
            part bas { }
            section S { mel { c''1 |: d''1 | e''1 :| f''1 | } bas { c1 | d1 | e1 | f1 | } }
            form { S }
            score { staff mel staff bas }
            """);
        Assert.Equal(@"c1 \repeat volta 2 { d1 | e1 | } f1 |", Var(ly, "bas"));
    }

    /// <summary>All or none: a part with no bar line of its own under one of the signs takes
    /// none of them — the <c>:|</c> alone would rewind to its beginning — and the twin says so.</summary>
    [Fact]
    public void APartThatCannotCarryOneSign_CarriesNone_AndTheTwinSaysSo()
    {
        var (ly, exporter) = Export("""
            phrase p { c1 | d1 | }
            part mel { section S { c''1 |: d''1 | e''1 :| f''1 | } }
            part bas { section S { p e1 | f1 | } }
            form { S }
            score { staff mel staff bas }
            """);
        Assert.DoesNotContain(@"\repeat", Var(ly, "bas"));
        Assert.Contains(exporter.Warnings, w => w.Contains("part 'bas' does not carry the repeat signs"));
    }

    /// <summary>A tie that ends a bar reaches the first note of every bar played after it
    /// (docs/inline-repeat-design.md §7 stage 4): LilyPond draws the arc to the bar printed
    /// next, and every other such bar's first note takes <c>\repeatTie</c> — the second ending
    /// a tie out of the body also enters, the head a first ending returns to. MEASURED before
    /// (2026-11-15, Lab sessions/p1247/probe/t-b-endings, t-c-first): neither carried one,
    /// where the form's spelling of the same music did.</summary>
    [Fact]
    public void ATieOverARepeatTheMusicWrites_IsARepeatTieOnEveryOtherBarPlayedNext()
    {
        var (endings, _) = Export(TwoParts("|: c''1 | d''1~ | [1. d''1 :| [2. d''1 ] | c''1 |", "c1 | d1 | e1 | f1 | g1 |"));
        Assert.Equal(@"\repeat volta 2 { c''1 | d''1 ~ | } \alternative { { d''1 } { d''1\repeatTie } } | c''1 |", Var(endings, "mel"));
        Assert.DoesNotContain("repeatTie", Var(endings, "bas"));

        var (head, _) = Export(TwoParts("|: c''1 | d''1 | [1. c''1~ :| [2. e''1 ] | c''1 |", "c1 | d1 | e1 | f1 | g1 |"));
        Assert.Equal(@"\repeat volta 2 { c''1\repeatTie | d''1 | } \alternative { { c''1 ~ } { e''1 } } | c''1 |", Var(head, "mel"));

        // The signs are another part's: the tied part's own bars take the marks.
        var (other, _) = Export(TwoParts("c''1 | d''1~ | d''1 | d''1 | c''1 |", "|: c1 | d1 | [1. e1 :| [2. f1 ] | g1 |"));
        Assert.Equal(@"\repeat volta 2 { c''1 | d''1 ~ | } \alternative { { d''1 | } { d''1\repeatTie | } } c''1 |", Var(other, "mel"));

        // Three endings: the ':|' between the second and the third still divides them, the
        // mark before the third notwithstanding.
        var (three, _) = Export(TwoParts("|: c''1 | d''1~ | [1. d''1 :| [2. d''1 :| [3. d''1 ] | c''1 |", "c1 | d1 | e1 | f1 | g1 | a1 |"));
        Assert.Equal(@"\repeat volta 3 { c''1 | d''1 ~ | } \alternative { { d''1 } { d''1\repeatTie } { d''1\repeatTie } } | c''1 |", Var(three, "mel"));

        // A section that ends on the tie with no bar line after it, under the form's ':|'.
        var (open, _) = Export("part mel { section A { c''1 |: d''1 | d''1~ } section B { d''1 | } }\n"
            + "form { A :| B }\nscore { staff mel }");
        Assert.Contains(@"\repeat volta 2 { d''1\repeatTie | d''1 ~ }", Var(open, "mel"));

        // No form: the sections in declaration order, the same column.
        var (noForm, _) = Export(TwoParts("|: c''1 | d''1~ | [1. d''1 :| [2. d''1 ] | c''1 |", "c1 | d1 | e1 | f1 | g1 |",
            "score { staff mel staff bas }"));
        Assert.Equal(Var(endings, "mel"), Var(noForm, "mel"));
    }

    /// <summary>The control: a book whose repeats are the form's writes what it always wrote.</summary>
    [Fact]
    public void ABookWithNoSignInItsMusic_IsUntouched()
    {
        var (ly, exporter) = Export(TwoParts("c''1 | d''1 |", "c1 | d1 |", "form { |: S :| }\nscore { staff mel staff bas }"));
        Assert.Equal(@"\repeat volta 2 { c''1 | d''1 | }", Var(ly, "mel"));
        Assert.Equal(@"\repeat volta 2 { c1 | d1 | }", Var(ly, "bas"));
        Assert.DoesNotContain(exporter.Warnings, w => w.Contains("repeat signs"));
    }
}
