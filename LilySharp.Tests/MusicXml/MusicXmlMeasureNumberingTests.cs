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
using LilySharp.Core.MusicXml;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// Measure numbers run without a gap across a block that closes on a bar line: the empty
/// measure that bar line opens is never written, and it hands its number back. Until
/// 2026-10-03 it kept it — the music after a <c>voice { } { }</c> span whose voices end on
/// <c>|</c> was numbered one past the gap (test/multi-voice: 1, 2, 4; HANDOFF §1.0 ⑹′, ten
/// books of the corpus). The page numbers the same bars 1, 2, 3.
/// </summary>
public class MusicXmlMeasureNumberingTests
{
    private static int[] Numbers(string source)
        => new MusicXmlExporter().Export(SyntaxTree.Parse(source))
            .Parts.Single().Measures.Select(x => x.Number).ToArray();

    /// <summary>The multi-voice shape: two bars of a span, then a bar after it — 1, 2, 3.</summary>
    [Fact]
    public void AVoiceSpanClosedOnABarLine_IsFollowedByTheNextNumber()
    {
        var numbers = Numbers("""
            octave absolute
            time 4/4
            part m
            section A { m { voice { c'2 d' | e'2 f' | } { e'2 f' | g'2 a' | }  g'1 | } }
            form main { A }
            score main { staff m }
            """);
        Assert.Equal(new[] { 1, 2, 3 }, numbers);
    }

    private static (int Number, bool Implicit)[] NumbersAndImplicit(string source)
        => new MusicXmlExporter().Export(SyntaxTree.Parse(source))
            .Parts.Single().Measures.Select(x => (x.Number, x.Implicit)).ToArray();

    /// <summary>
    /// The bar a section boundary splits is one bar: B opens with the half bar A left short,
    /// with no <c>partial</c>, so its first bar takes A's number 2, implicit, and the bar
    /// after it is 3 — the page's 1, 2, 2, 3 (MeasureCollector.SectionBoundaryContinuations).
    /// Until 2026-10-03 the MusicXML wrote 1, 2, 3, 4.
    /// </summary>
    [Fact]
    public void ASectionOpeningWithTheRestOfTheBarBefore_ContinuesItsNumber()
    {
        var m = NumbersAndImplicit("""
            octave absolute
            time 4/4
            part m { clef treble }
            section A { m { c'1 | c'2 | } }
            section B { m { d'2 | e'1 | } }
            form main { A B }
            score main { staff m }
            """);
        Assert.Equal(new[] { (1, false), (2, false), (2, true), (3, false) }, m);
    }

    /// <summary>The other control: a short first bar that does NOT complete the bar before
    /// it (a half and a quarter) is a short bar of its own — numbered on, not implicit — as
    /// the page numbers it (the two must add up to exactly one bar).</summary>
    [Fact]
    public void AShortFirstBarThatDoesNotCompleteTheBarBefore_IsNotContinued()
    {
        var m = NumbersAndImplicit("""
            octave absolute
            time 4/4
            part m { clef treble }
            section A { m { c'1 | c'2 | } }
            section B { m { d'4 | e'1 | } }
            form main { A B }
            score main { staff m }
            """);
        Assert.Equal(new[] { (1, false), (2, false), (3, false), (4, false) }, m);
    }

    /// <summary>The control: a DECLARED pickup is a pickup bar of its own and counts, on the
    /// page (1, 2, 3, 4) as here — only its number is hidden (implicit).</summary>
    [Fact]
    public void ADeclaredPickupAfterAShortBar_IsABarOfItsOwn()
    {
        var m = NumbersAndImplicit("""
            octave absolute
            time 4/4
            part m { clef treble }
            section A { m { c'1 | c'2 | } }
            section B { partial 2  m { d'2 | e'1 | } }
            form main { A B }
            score main { staff m }
            """);
        Assert.Equal(new[] { (1, false), (2, false), (3, true), (4, false) }, m);
    }

    /// <summary>The shape the rule was written for: the repeat sign mid-bar. The first
    /// ending continues the body's half bar (2, implicit); the second ending follows the
    /// first ending's full bar in the document and is numbered on (4), as the page numbers it
    /// ("bar numbers continue through alternatives") — 1, 2, 2, 3, 4, 5.</summary>
    [Fact]
    public void AVoltaOpeningMidBar_ContinuesTheBodysNumber_AndTheSecondEndingCountsOn()
    {
        var m = NumbersAndImplicit("""
            octave absolute
            time 4/4
            part m { clef treble }
            section A { m { c'1 | c'2 | } }
            section B { m { d'2 | e'1 | } }
            section C { m { f'2 | g'1 | } }
            form main { |: A [1. B] :| [2. C] }
            score main { staff m }
            """);
        Assert.Equal(new[] { (1, false), (2, false), (2, true), (3, false), (4, false), (5, false) }, m);
    }

    /// <summary>A span whose voices do NOT end on a bar line hands back no empty measure: the
    /// bar after it is the same bar continued — 1, 2 (the control, which never moved).</summary>
    [Fact]
    public void AVoiceSpanLeftOpen_ContinuesItsBar()
    {
        var numbers = Numbers("""
            octave absolute
            time 4/4
            part m
            section A { m { voice { c'2 d' } { e'2 f' } | g'1 | } }
            form main { A }
            score main { staff m }
            """);
        Assert.Equal(new[] { 1, 2 }, numbers);
    }
}
