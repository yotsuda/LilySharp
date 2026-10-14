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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A written bar with too much in it is still one bar on the page: what starts after its end
/// is not drawn, and the bars after it stay under the other parts' (Semantics.WrittenBar,
/// MeasureBuilder.OverflowsTheWrittenBar). A bar line left out — a span close to whole bars —
/// keeps every note.
/// </summary>
/// <remarks>
/// WHY IT EXISTS. The owner's sync.lys, 2026-10-14: `g4 g c,8 f1 c1 | e1` beside a part of full
/// bars. The page closed the bar where its beats ran out, made `c1` a bar of its own and `e1` a
/// fourth bar, and the score — laid out at the other part's three bars — never drew it. The
/// owner's decision: the written bar line wins, a left-out bar line is the one exception.
/// </remarks>
public class WrittenBarOverflowTests
{
    private static Measure[] Bars(string p2, string p1 = "c'1 | d | e | f")
    {
        var src = $$"""
            time 4/4
            section A {
              p1 { {{p1}} }
              p2 { {{p2}} }
            }
            form { A }
            score {
              staff p1
              staff p2
            }
            """;
        return new MeasureCollector().Collect(SyntaxTree.Parse(src), "p2").Voice.Measures.ToArray();
    }

    private static int Sounding(Measure m) => m.Items.Count(i => i is NoteItem or ChordItem);

    [Fact]
    public void TheOwnersBook_TheMusicPastTheBar_IsNotDrawn()
    {
        var bars = Bars("c'4 d e | g4 g c,8 f1 c1 | e1", "c'4 d e f | g2 g | c1");
        Assert.Equal(3, bars.Length);
        Assert.Equal(4, Sounding(bars[1])); // g g c f — the c1 after the bar's end is gone
        Assert.Equal(1, Sounding(bars[2])); // the e1, under the other part's third bar
    }

    [Fact]
    public void AnEighthTooMany_IsNotABarOfItsOwn()
    {
        var bars = Bars("c'1 | g2 g c,8 | e1 | f1");
        Assert.Equal(4, bars.Length);
        Assert.Equal(2, Sounding(bars[1]));
    }

    [Fact]
    public void ALeftOutBarLine_KeepsEveryNote()
    {
        // 15/8: two bars with the `|` between them missing (the owner's 銀河鉄道999).
        var bars = Bars("c'1 | d8 d d d d d d d e e e e e e e | f1");
        Assert.Equal(4, bars.Length);
        Assert.Equal(15, Sounding(bars[1]) + Sounding(bars[2]));
    }

    [Fact]
    public void TheMusicAfterTheLastBarLine_IsHeldToItsBarToo()
    {
        // No bar line after `g2 g f4.`: the span still ends where the music does, and 11/8 is
        // one bar with too much in it — the f is not drawn, as it would not be before a `|`.
        var bars = Bars("c'1 | g2 g f4.", "c'1 | d1");
        Assert.Equal(2, bars.Length);
        Assert.Equal(2, Sounding(bars[1]));
    }

    [Fact]
    public void ALeftOutBarLineBeforeAShortBar_KeepsEveryNote()
    {
        // 9/4: two bars and a beat with no bar line — within a quarter of two bars, so a bar
        // line left out; the beat is a short third bar, not music to drop.
        var bars = Bars("c'1 | d1 e1 g4 | f1", "c'1 | d | e | f | g");
        Assert.Equal(5, bars.Length);
        Assert.Equal(5, bars.Sum(Sounding));
    }

    [Fact]
    public void ALeftOutBarLine_IsAnError_AndAnOverfullBarAWarning()
    {
        static List<Diagnostic> Measure(string music) => SemanticValidation.Run(SyntaxTree.Parse(
            $"part m {{ section A {{ {music} }} }} form {{ A }} score {{ staff m }}"))
            .Where(d => d.Code is DiagnosticCodes.MissingBarLine or DiagnosticCodes.MeasureOverflow).ToList();

        var left = Assert.Single(Measure("c'1 | d8 d d d d d d d e e e e e e e | f1 |"));
        Assert.Equal(DiagnosticCodes.MissingBarLine, left.Code);
        Assert.Equal(DiagnosticSeverity.Error, left.Severity);

        var over = Assert.Single(Measure("c'1 | g2 g c,8 | e1 |"));
        Assert.Equal(DiagnosticCodes.MeasureOverflow, over.Code);
        Assert.Contains("not drawn or played", over.Message);
    }

    [Fact]
    public void ATimeWrittenInsideABar_IsALeftOutBarLine()
    {
        // `c1 time 3/4 c2.` — the bar line before the `time` is missing.
        var diags = SemanticValidation.Run(SyntaxTree.Parse(
            "part m { section A { c'1 time 3/4 c2. | } } form { A } score { staff m }"));
        var d = Assert.Single(diags, x => x.Code == DiagnosticCodes.MissingBarLine);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.DoesNotContain(SemanticValidation.Run(SyntaxTree.Parse(
                "part m { section A { c'1 | time 3/4 c2. | } } form { A } score { staff m }")),
            x => x.Code is DiagnosticCodes.MissingBarLine or DiagnosticCodes.MeasureOverflow);
    }

    [Fact]
    public void ABarLineInsideACue_ClosesItsBar()
    {
        // `cue { f'1( | gis'1) } |` is two bars, as the page draws it — the validator read the
        // cue as one item of 2/1 and called its bar line missing.
        Assert.DoesNotContain(SemanticValidation.Run(SyntaxTree.Parse(
                "part m { section A { c'1 | cue { f'1( | gis'1) } | } } form { A } score { staff m }")),
            x => x.Code is DiagnosticCodes.MissingBarLine or DiagnosticCodes.MeasureOverflow);
    }

    private static Measure[] PhraseBars(string phrases, string music)
        => new MeasureCollector().Collect(SyntaxTree.Parse(
                $"time 4/4\n{phrases}\npart m {{ section A {{ {music} }} }}\nform {{ A }}\nscore {{ staff m }}\n"),
            "m").Voice.Measures.ToArray();

    private static List<Diagnostic> PhraseDiagnostics(string phrases, string music)
        => SemanticValidation.Run(SyntaxTree.Parse(
                $"time 4/4\n{phrases}\npart m {{ section A {{ {music} }} }}\nform {{ A }}\nscore {{ staff m }}\n"))
            .Where(d => d.Code is DiagnosticCodes.MissingBarLine or DiagnosticCodes.MeasureOverflow).ToList();

    [Fact]
    public void APhrasesMusic_IsHeldToTheWrittenBarItPlaysIn()
    {
        // `riff | c1` with a five-beat riff: the bar is `c d e f`, the g past it is not drawn —
        // the phrase body is music like any other (it used to close a bar of its own).
        var bars = PhraseBars("phrase riff { c'4 d e f g }", "riff | c1 |");
        Assert.Equal(2, bars.Length);
        Assert.Equal(4, Sounding(bars[0]));
        // Reported in the phrase's body and where it is referenced, as every bar check of a
        // phrase is.
        var diags = PhraseDiagnostics("phrase riff { c'4 d e f g }", "riff | c1 |");
        Assert.NotEmpty(diags);
        Assert.All(diags, d => Assert.Equal(DiagnosticCodes.MeasureOverflow, d.Code));
    }

    [Fact]
    public void APhrasesOwnBarLines_AreBarLines()
    {
        var bars = PhraseBars("phrase riff { c'4 d e f | g a b c }", "riff | riff |");
        Assert.Equal(4, bars.Length);
        Assert.Equal(16, bars.Sum(Sounding));
        Assert.Empty(PhraseDiagnostics("phrase riff { c'4 d e f | g a b c }", "riff | riff |"));
    }

    [Fact]
    public void ABarLineLeftOutInAPhrase_IsAnError_AndKeepsEveryNote()
    {
        var bars = PhraseBars("phrase riff { c'4 d e f g a b c }", "riff | c1 |");
        Assert.Equal(3, bars.Length);
        Assert.Equal(9, bars.Sum(Sounding));
        var diags = PhraseDiagnostics("phrase riff { c'4 d e f g a b c }", "riff | c1 |");
        Assert.NotEmpty(diags);
        Assert.All(diags, d => Assert.Equal(DiagnosticCodes.MissingBarLine, d.Code));
    }

    [Fact]
    public void APhraseNestedInAPhrase_IsHeldToItsBarToo()
    {
        // `outer` is a full bar on its own (three beats of `inner` and an f); `outer g |` is one
        // bar with too much in it. The validator prices the nested body in place where `outer`
        // is played (MeasureValidator.PricePhraseBody) — as an opaque item `outer` was worth
        // nothing there and the bar read as the g alone — and the g is not drawn, the same
        // answer as the music written out.
        const string phrases = "phrase inner { c'4 d e }\nphrase outer { inner f }";
        var bars = PhraseBars(phrases, "outer g | c1 |");
        Assert.Equal(2, bars.Length);
        Assert.Equal(5, bars.Sum(Sounding));
        var diags = PhraseDiagnostics(phrases, "outer g | c1 |");
        Assert.NotEmpty(diags);
        Assert.All(diags, d => Assert.Equal(DiagnosticCodes.MeasureOverflow, d.Code));
    }

    [Fact]
    public void MusicWithNoBarLines_StillFillsBarAfterBar_AndIsAnError()
    {
        var bars = Bars("c'1 d e f");
        Assert.Equal(4, bars.Length);
        Assert.All(bars, b => Assert.Equal(1, Sounding(b)));
        Assert.Contains(SemanticValidation.Run(SyntaxTree.Parse(
                "part m { section A { c'1 d e f } } form { A } score { staff m }")),
            d => d.Code == DiagnosticCodes.MissingBarLine);
    }

    [Fact]
    public void TwoWholeBarsWithOneBarLine_AreTwoBars()
    {
        var bars = Bars("c'1 d1 | e1 | f1");
        Assert.Equal(4, bars.Length);
    }
}
