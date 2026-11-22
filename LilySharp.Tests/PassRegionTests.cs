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
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>pass 2 { … }</c> — music of some passes only (docs/eval4-features-design.md §3; grammar
/// evaluation 4's "3", owner's go-ahead 2026-11-15; evaluation 5's B4). Its notes sound on the
/// passes named and are a rest of their length on the others; the page prints them once, under
/// "2nd time only". MEASURED before (2026-11-22, Lab sessions/p1368/pass): the only way was
/// <c>@text("2nd time only")</c>, and the MIDI played the notes on every pass.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PassRegionTests
{
    private static string Book(string music) => $$"""
        octave absolute
        part m { section S { {{music}} } }
        form { S }
        score { staff m }
        """;

    /// <summary>The pitches one part plays, in order, as MIDI numbers.</summary>
    private static string Played(string src) =>
        string.Join(' ', new MidiExporter().Export(SyntaxTree.Parse(src)).Tracks.SelectMany(t => t.Notes)
            .Where(n => n.Part == "m").OrderBy(n => n.StartTick).Select(n => n.Pitch));

    private static List<Diagnostic> Said(string src)
    {
        var tree = SyntaxTree.Parse(src);
        return [.. tree.Diagnostics, .. SemanticValidation.Run(tree)];
    }

    [Theory]
    // c' d' e' f' g' a' = 60 62 64 65 67 69
    [InlineData("|: c'4 d' pass 2 { e' f' } :|", "60 62 60 62 64 65")]
    [InlineData("|: c'4 d' pass 1 { e' f' } :|", "60 62 64 65 60 62")]
    [InlineData("|: c'2 pass 1 3 { d'2 } :|*3", "60 62 60 60 62")]
    // A region across a bar line, and a pass a repeat never plays.
    [InlineData("|: c'2 pass 2 { d'2 | e'1 } :| g'1 |", "60 60 62 64 67")]
    // No repeat at all: the music is played once, on pass 1.
    [InlineData("c'4 d' pass 2 { e' f' } | g'1 |", "60 62 67")]
    [InlineData("c'4 d' pass 1 { e' f' } | g'1 |", "60 62 64 65 67")]
    public void TheNotesSoundOnTheNamedPassesOnly(string music, string played)
        => Assert.Equal(played, Played(Book(music)));

    /// <summary>A tie is cut at the region's braces: merged, a note of every pass took the
    /// region's passes (MEASURED: the first case lost its e'1 on the first pass). Inside the
    /// region a tie holds.</summary>
    [Theory]
    // start tick (in quarters) : length, as `lysc notes` reads them
    [InlineData("|: c'2 pass 2 { e'2~ } | e'1 :|", "0:2 4:4 8:2 10:2 12:4")]
    [InlineData("|: c'2 e'2~ | pass 2 { e'1 } :|", "0:2 2:2 8:2 10:2 12:4")]
    [InlineData("|: c'2 pass 2 { e'4~ e'4 } :|", "0:2 4:2 6:2")]
    public void ATieIsCutAtTheRegionsBraces(string music, string notes)
        => Assert.Equal(notes, string.Join(' ', new MidiExporter().Export(SyntaxTree.Parse(Book(music))).Tracks
            .SelectMany(t => t.Notes).Where(n => n.Part == "m").OrderBy(n => n.StartTick)
            .Select(n => $"{n.StartTick / 480}:{n.DurationTicks / 480}")));

    [Fact]
    public void ADroppedPassKeepsItsTime()
    {
        var notes = new MidiExporter().Export(SyntaxTree.Parse(Book("|: c'4 d' pass 2 { e' f' } :| g'1 |")))
            .Tracks.SelectMany(t => t.Notes).Where(n => n.Part == "m").OrderBy(n => n.StartTick).ToList();
        // c d (rest rest) c d e f g: the second pass starts a whole bar in, the g two bars in.
        Assert.Equal(new[] { 0, 480, 1920, 2400, 2880, 3360, 3840 }, notes.Select(n => n.StartTick));
    }

    [Theory]
    [InlineData("2", "2nd time only")]
    [InlineData("1", "1st time only")]
    [InlineData("3", "3rd time only")]
    [InlineData("4", "4th time only")]
    [InlineData("1 3", "1st &amp; 3rd time")]
    [InlineData("3 1", "1st &amp; 3rd time")]
    [InlineData("1 2 4", "1st, 2nd &amp; 4th time")]
    public void ThePagePrintsThePassesOverTheFirstNote(string passes, string text)
    {
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(Book($"|: c'4 d' pass {passes} {{ e' f' }} :|*4")));
        Assert.Contains(">" + text + "<", svg);
    }

    [Fact]
    public void ABareRegionPrintsNoText()
    {
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(Book("|: c'4 d' pass 2 bare { e' f' } :|")));
        Assert.DoesNotContain("time only", svg);
        Assert.Equal("60 62 60 62 64 65", Played(Book("|: c'4 d' pass 2 bare { e' f' } :|")));
    }

    [Fact]
    public void TheMarksMoveNothingOnThePage()
    {
        static string Ink(string svg) => Regex.Replace(Regex.Replace(svg, @"<text[^>]*>[^<]*time only</text>", ""),
            @"\s?data-(pos|span|start|alt)=""[^""]*""", "");
        string with = SvgGenerator.Generate(SyntaxTree.Parse(Book("|: c'4 d'[ pass 2 bare { e'] f'~ | f'1 } :|")));
        string without = SvgGenerator.Generate(SyntaxTree.Parse(Book("|: c'4 d'[ e'] f'~ | f'1 :|")));
        Assert.Equal(Ink(without), Ink(with));
    }

    [Fact]
    public void APhraseNamedPassIsStillCalled()
    {
        string src = """
            octave absolute
            phrase pass { e'4 f' }
            part m { section S { c'4 d' pass | } }
            form { S }
            score { staff m }
            """;
        Assert.DoesNotContain(Said(src), d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("60 62 64 65", Played(src));
    }

    [Fact]
    public void TheTwinPrintsTheText()
    {
        string ly = new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(Book("|: c'4 d' pass 2 { e' f' } :|")));
        Assert.Contains("<>^\\markup { \\italic \"2nd time only\" } e'", ly);
        Assert.DoesNotContain("PassStart", ly);
    }

    [Fact]
    public void MusicXmlWritesTheWordsAndTheTimeOnly()
    {
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(Book("|: c'4 d' pass 1 3 { e' f' } :|*3"))).ToXml().ToString();
        Assert.Contains("<words>1st &amp; 3rd time</words>", xml);
        Assert.Equal(2, Regex.Matches(xml, "<note time-only=\"1,3\"").Count);
        Assert.Equal(2, Regex.Matches(xml, "<note>").Count);
    }

    [Theory]
    [InlineData("|: c'4 d' pass 0 { e' f' } :|", DiagnosticCodes.PassNumberInvalid)]
    [InlineData("|: c'4 d' pass 2 2 { e' f' } :|", DiagnosticCodes.PassNumberInvalid)]
    [InlineData("|: c'4 d' pass 2 { e' pass 1 { f' } } :|", DiagnosticCodes.PassInsidePass)]
    public void AMisspelledRegionIsAnError(string music, string code)
    {
        var said = Said(Book(music));
        Assert.Contains(said, d => d.Code == code && d.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("c'4 d' pass 2 { e' f' } | g'1 |")]                   // no repeat: played once
    [InlineData("|: c'2 pass 3 { d'2 } :| g'1 |")]                   // a repeat played twice
    public void ARegionTheMusicNeverPlaysIsAWarning(string music)
        => Assert.Contains(Said(Book(music)), d => d.Code == DiagnosticCodes.PassNeverPlayed && d.Severity == DiagnosticSeverity.Warning);

    [Theory]
    [InlineData("|: c'2 pass 2 3 { d'2 } :| g'1 |")]                 // pass 2 is played
    [InlineData("c'4 d' pass 1 { e' f' } | g'1 |")]
    public void ARegionPlayedOnSomePassSaysNothingOfIt(string music)
        => Assert.DoesNotContain(Said(Book(music)), d => d.Code == DiagnosticCodes.PassNeverPlayed);

    [Fact]
    public void AWellWrittenRegionSaysNothing()
        => Assert.Empty(Said(Book("|: c'4 d' pass 1 3 { e' f' } :|*3")).Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
}
