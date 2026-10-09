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
        form { ~S }
        score { staff bassline }
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
    [InlineData("c4@notehead(triangle) |", "triangle")]
    public void ANoteheadArgument_IsItsStyleWord(string music, string style)
        => Assert.Equal(style, AnnotationValues.Notehead(Mark(music)));

    /// <summary>
    /// The VALUE words are case-sensitive like the names (owner's decision 2026-09-27):
    /// until then each of these was lower-cased and read. Every value vocabulary is lower
    /// case; the validator names the spelling (<c>AnnotationNameValidatorTests</c>).
    /// </summary>
    [Fact]
    public void AValueWordInTheWrongCase_IsNoValue()
    {
        Assert.Null(AnnotationValues.Pluck(Mark("c4@pluck(M) |")));
        Assert.Null(AnnotationValues.Notehead(Mark("c4@notehead(TRIANGLE) |")));
        Assert.Null(AnnotationValues.Bend(Mark("c4@bend(Full) |")));
        Assert.Equal(0, AnnotationValues.Feather(Mark("c4@feather(Right) |")));
        Assert.False(AnnotationValues.IsArpeggioBracket(Mark("c4@arpeggio(BRACKET) |")));
        Assert.Null(AnnotationValues.Frame(Mark("c4@diagram(X32010) |")));
        Assert.Null(AnnotationValues.Frame(Mark("c4@diagram(x32O10) |")));   // a capital O
        Assert.Null(AnnotationValues.Figures(Mark("c4@figuredBass(6 S) |")));
    }

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
    [InlineData("c4@feather(left) |", -1)]
    [InlineData("c4@feather(accel) |", 0)]   // retired synonym of right (2026-09-15)
    [InlineData("c4@feather(rit) |", 0)]     // retired synonym of left
    [InlineData("c4@feather(sideways) |", 0)]
    [InlineData("c4@finger(3) |", 0)]
    public void AFeatherArgument_IsItsGrowDirection(string music, int direction)
        => Assert.Equal(direction, AnnotationValues.Feather(Mark(music)));

    [Theory]
    [InlineData("c4@arpeggio(bracket) |", true)]
    [InlineData("c4@arpeggio(arrow) |", false)]
    [InlineData("c4@notehead(x) |", false)]
    public void AnArpeggioBracket_IsRecognisedByItsArgument(string music, bool isBracket)
        => Assert.Equal(isBracket, AnnotationValues.IsArpeggioBracket(Mark(music)));

    /// <summary>
    /// ★ The family the argument node was shaped for. The spec is a POSITION STRING,
    /// one character per string, so its leading zero is the sixth string — it is read
    /// from the argument's text, never from the <c>Int(32010)</c> its value would be.
    /// </summary>
    [Theory]
    [InlineData("c4@diagram(032010) |", "032010")]
    [InlineData("c4@diagram(x32010) |", "x32010")]
    [InlineData("c4@diagram(xx0232) |", "xx0232")]
    [InlineData("c4@diagram(o32010) |", "o32010")]
    public void AFrameArgument_IsItsPositionString(string music, string spec)
        => Assert.Equal(spec, AnnotationValues.Frame(Mark(music)));

    /// <summary>
    /// Owner's decision 2026-09-28: a DASH-SEPARATED shape, one item per string, writes frets
    /// 10–15 — and the lexer's tokens (<c>x</c>, <c>-</c>, <c>10</c> …) glue into one argument.
    /// The reader hands the page its spec: frets 10–15 as <c>a</c>–<c>f</c>, which every
    /// consumer reads through <c>FretFrameGeometry.FretAt</c>.
    /// </summary>
    [Theory]
    [InlineData("c4@diagram(x-x-10-12-13-11) |", "xxacdb")]
    [InlineData("c4@diagram(8-10-10-8-8-8) |", "8aa888")]
    [InlineData("c4@diagram(x-3-5-5-4-3) |", "x35543")]
    [InlineData("c4@diagram(x-15-13-12-13-x) |", "xfdcdx")]
    [InlineData("c4@diagram(o-o-o-3) |", "0003")]
    [InlineData("c4@diagram(x-x-10-12-13-11).down |", "xxacdb")]
    // The compact form (owner's decision 2026-09-28): '-' only around the two-digit frets.
    [InlineData("c4@diagram(xx-10-12-13-11) |", "xxacdb")]
    [InlineData("c4@diagram(8xx88-11) |", "8xx88b")]
    [InlineData("c4@diagram(8-10-10-888) |", "8aa888")]
    [InlineData("c4@diagram(10-9-9-988) |", "a99988")]
    public void ADashSeparatedFrameArgument_IsItsSpec(string music, string spec)
        => Assert.Equal(spec, AnnotationValues.Frame(Mark(music)));

    [Theory]
    [InlineData("c4@diagram(032) |")]          // too few strings
    [InlineData("c4@diagram(032010789) |")]    // too many
    [InlineData("c4@diagram(zzzz) |")]         // not fret / open / muted
    [InlineData("c4@diagram(x-x-16-12-13-11) |")]   // beyond fret 15
    [InlineData("c4@diagram(x--3-5-5-4-3) |")]      // an empty item
    [InlineData("c4@diagram(x-3-5-5-4-3-) |")]      // a trailing dash
    [InlineData("c4@diagram(x-3-5) |")]             // three items
    [InlineData("c4@diagram(X-3-5-5-4-3) |")]       // upper case
    [InlineData("c4@diagram(10-99-88) |")]          // '99' is one fret, 99 (10-9-9-88 meant)
    [InlineData("c4@diagram(xx-10-12-13-01) |")]    // '01' is one fret, and none
    public void SomethingElse_IsNoFrame(string music)
        => Assert.Null(AnnotationValues.Frame(Mark(music)));

    /// <summary>
    /// ★ The declared behaviour change, the same shape as the fingering one. The
    /// MusicXML exporter had no gate at all, so a spec Lily# refuses to draw was
    /// written into the chord diagram anyway. The control is the valid spec beside it.
    /// </summary>
    /// <remarks>
    /// ⚠️ TWO things this probe had to be told, both found by the control failing
    /// rather than by reading the code: the chord symbol is not decoration (MusicXML's
    /// &lt;frame&gt; is a &lt;harmony&gt; CHILD, so with no chord symbol the spec
    /// reaches no element at all), and the spec is not written as a string (BuildFrame
    /// decomposes it into &lt;frame-note&gt; children, so "032010" never appears).
    /// Asserting on the element is the only honest form.
    /// </remarks>
    [Fact]
    public void AFrameSpecLilySharpRefuses_NoLongerReachesTheXml()
    {
        Assert.DoesNotContain("<frame>", Xml("c4@chord(C)@diagram(zzzz) |"));
        Assert.Contains("<frame>", Xml("c4@chord(C)@diagram(032010) |"));
    }

    private static string Xml(string music) =>
        new LilySharp.Core.MusicXml.MusicXmlExporter()
            .Export(SyntaxTree.Parse($$"""
            part gtr { clef treble section S { {{music}} } }
            form { ~S }
            score { staff gtr }
            """)).ToXml().ToString();

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
    [InlineData("c4@diagram(032010) |")]
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
    [InlineData("c4@diagram(zzzz) |")]
    public void AnArgumentNoConsumerAccepts_IsStillWarned(string music)
    {
        var tree = SyntaxTree.Parse("melody { " + music + " }");
        var validator = new AnnotationNameValidator();
        validator.Validate(tree);
        Assert.Contains(validator.Diagnostics,
            d => d.Code == DiagnosticCodes.UnknownAnnotation);
    }
}
