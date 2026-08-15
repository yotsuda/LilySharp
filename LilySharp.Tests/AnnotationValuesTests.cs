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
using LilySharp.Core.LilyPond;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The four annotation families whose argument is a VALUE, read once
/// (VALUE_SITE_AUDIT §9.5 ⑵ — the first families to move off the dotted MarkName).
/// </summary>
[Trait("Category", "Unit")]
public class AnnotationValuesTests
{
    private static MusicMarkSyntax Mark(string music)
        => Assert.Single(SyntaxTree.Parse("melody { " + music + " }")
            .GetRoot().DescendantNodes().OfType<MusicMarkSyntax>());

    // The same shape LilyPondExporterTests exports: a post-event only reaches the twin
    // attached to a note inside a scored part.
    private static string ExportedTwin(string music) =>
        new LilyPondExporter().Export(SyntaxTree.Parse($$"""
        octave absolute
        part bassline {
          clef bass
          section S { {{music}} }
        }
        form main { ~S }
        score main { staff bassline }
        """));

    [Theory]
    [InlineData("c4@finger(3) |", 3)]
    [InlineData("c4@finger(0) |", 0)]
    [InlineData("c4@finger(12) |", 12)]   // LilyPond's UNSIGNED, measured on 2.26.0
    [InlineData("c4@finger(03) |", 3)]
    public void AFingerArgument_IsItsNumber(string music, int finger)
        => Assert.Equal(finger, AnnotationValues.Finger(Mark(music)));

    [Theory]
    [InlineData("c4@finger(x) |")]
    [InlineData("c4@finger(3 5) |")]      // two arguments: no single fingering
    [InlineData("c4@bend(3) |")]          // a different family
    public void SomethingElse_IsNoFinger(string music)
        => Assert.Null(AnnotationValues.Finger(Mark(music)));

    [Theory]
    [InlineData("c4@pluck(p) |", "p")]
    [InlineData("c4@pluck(i) |", "i")]
    [InlineData("c4@pluck(M) |", "m")]    // the string form lower-cased first too
    public void APluckArgument_IsItsLetter(string music, string letter)
        => Assert.Equal(letter, AnnotationValues.Pluck(Mark(music)));

    [Theory]
    [InlineData("c4@pluck(z) |")]
    [InlineData("c4@pluck(pi) |")]
    public void SomethingElse_IsNoPluck(string music)
        => Assert.Null(AnnotationValues.Pluck(Mark(music)));

    [Theory]
    [InlineData("c4@bend(half) |", 1)]
    [InlineData("c4@bend(full) |", 2)]
    [InlineData("c4@bend(5) |", 5)]
    [InlineData("c4@bend(12) |", 12)]
    public void ABendArgument_IsItsSemitones(string music, int semitones)
        => Assert.Equal(semitones, AnnotationValues.Bend(Mark(music)));

    [Theory]
    [InlineData("c4@bend(0) |")]
    [InlineData("c4@bend(13) |")]
    [InlineData("c4@bend(quarter) |")]
    public void SomethingElse_IsNoBend(string music)
        => Assert.Null(AnnotationValues.Bend(Mark(music)));

    [Theory]
    [InlineData("c4@notehead(x) |", "x")]
    [InlineData("c4@notehead(cross) |", "cross")]
    [InlineData("c4@notehead(TRIANGLE) |", "triangle")]
    public void ANoteheadArgument_IsItsStyleWord(string music, string style)
        => Assert.Equal(style, AnnotationValues.Notehead(Mark(music)));

    [Fact]
    public void AnUnknownStyle_IsNoNotehead()
        => Assert.Null(AnnotationValues.Notehead(Mark("c4@notehead(square) |")));

    /// <summary>
    /// ★ The declared behaviour change. The two former copies of the fingering rule
    /// disagreed about a QUOTED digit: the collector rejected it (so Lily# drew
    /// nothing) while the LilyPond exporter trimmed the quotes and emitted a
    /// fingering anyway — a twin stating music the source does not. One reader, so
    /// one answer: neither produces one. The control below is the bare digit, which
    /// both always accepted and still do.
    /// </summary>
    [Fact]
    public void AQuotedFingerDigit_IsNoFingeringOnEitherSide()
    {
        Assert.Null(AnnotationValues.Finger(Mark("c,1@finger(\"3\")")));
        Assert.DoesNotContain("c,1-3", ExportedTwin("c,1@finger(\"3\")"));
    }

    [Fact]
    public void ABareFingerDigit_IsAFingeringOnBothSides()
    {
        Assert.Equal(3, AnnotationValues.Finger(Mark("c,1@finger(3)")));
        Assert.Contains("c,1-3", ExportedTwin("c,1@finger(3)"));
    }

    [Theory]
    [InlineData("c4@text(\"dolce\") |", "dolce")]
    [InlineData("c4@text(\"sul D\") |", "sul D")]
    [InlineData("c4@text(\"\") |", "")]
    [InlineData("c4@text(\"dolce\").up |", "dolce")]   // the qualifier is not the payload
    public void ATextArgument_IsItsStringContent(string music, string text)
        => Assert.Equal(text, AnnotationValues.Text(Mark(music)));

    /// <summary>
    /// An unquoted argument is not free text — it draws nothing, as before. The
    /// hand-walk this replaces scanned for a StringLiteral and found none.
    /// </summary>
    [Theory]
    [InlineData("c4@text(dolce) |")]
    [InlineData("c4@text() |")]
    public void AnUnquotedTextArgument_IsNoFreeText(string music)
        => Assert.Null(AnnotationValues.Text(Mark(music)));

    /// <summary>
    /// The side is read off the qualifier, not off "the last token happened to be
    /// 'up'" — so a text whose CONTENT is "up" is still placed below.
    /// </summary>
    [Fact]
    public void ATextReadingUp_IsNotAPlacement()
    {
        Assert.Equal("up", AnnotationValues.Text(Mark("c4@text(\"up\") |")));
        Assert.Null(Mark("c4@text(\"up\") |").ForcedAbove);
    }

    [Theory]
    [InlineData("c4@feather(right) |", 1)]
    [InlineData("c4@feather(accel) |", 1)]
    [InlineData("c4@feather(left) |", -1)]
    [InlineData("c4@feather(rit) |", -1)]
    [InlineData("c4@feather(sideways) |", 0)]
    [InlineData("c4@finger(3) |", 0)]
    public void AFeatherArgument_IsItsGrowDirection(string music, int direction)
        => Assert.Equal(direction, AnnotationValues.Feather(Mark(music)));

    [Theory]
    [InlineData("c4@arpeggio(bracket) |", true)]
    [InlineData("c4@arpeggio(BRACKET) |", true)]
    [InlineData("c4@arpeggio(arrow) |", false)]
    [InlineData("c4@notehead(x) |", false)]
    public void AnArpeggioBracket_IsRecognisedByItsArgument(string music, bool isBracket)
        => Assert.Equal(isBracket, AnnotationValues.IsArpeggioBracket(Mark(music)));

    /// <summary>
    /// The validator asks the same reader, so "is this consumed?" and "what does it
    /// mean?" cannot drift apart again for these families (§9.3's tenth restatement).
    /// </summary>
    [Theory]
    [InlineData("c4@finger(3) |")]
    [InlineData("c4@pluck(a) |")]
    [InlineData("c4@bend(half) |")]
    [InlineData("c4@notehead(diamond) |")]
    [InlineData("c4@text(\"dolce\") |")]
    [InlineData("c4@feather(right) |")]
    [InlineData("c4@arpeggio(bracket) |")]
    public void AValueFamilyAnnotation_IsNotWarnedAsUnknown(string music)
    {
        var tree = SyntaxTree.Parse("melody { " + music + " }");
        var validator = new AnnotationNameValidator();
        validator.Validate(tree);
        Assert.DoesNotContain(validator.Diagnostics,
            d => d.Code == DiagnosticCodes.UnknownAnnotation);
    }

    [Theory]
    [InlineData("c4@finger(x) |")]
    [InlineData("c4@pluck(z) |")]
    [InlineData("c4@bend(13) |")]
    [InlineData("c4@notehead(square) |")]
    [InlineData("c4@feather(sideways) |")]
    [InlineData("c4@arpeggio(arrow) |")]
    public void AnArgumentNoConsumerAccepts_IsStillWarned(string music)
    {
        var tree = SyntaxTree.Parse("melody { " + music + " }");
        var validator = new AnnotationNameValidator();
        validator.Validate(tree);
        Assert.Contains(validator.Diagnostics,
            d => d.Code == DiagnosticCodes.UnknownAnnotation);
    }
}
