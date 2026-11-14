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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The bars a sign cuts (<see cref="SignCutBars"/>, docs/inline-repeat-design.md §7 stage 4): a
/// short bar closed by a bar line that carries a sign of the score is the first half of a bar
/// when every bar played after it opens with what it lacks, and a bar is the second half when
/// every bar played before it leaves that much open.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SignCutBarsTests
{
    private static SignCutBars? Cuts(string mel, string bas, string head = "section A { partial 4 }")
    {
        var root = SyntaxTree.Parse($$"""
            time 4/4
            {{head}}
            part mel { section A { {{mel}} } }
            part bas { clef bass section A { {{bas}} } }
            form { A }
            score { staff mel staff bas }
            """).GetRoot();
        return SignCutBars.For(root, ScoreForms.Primary(root));
    }

    /// <summary>"0:first 3:second<2" — every bar that is a half, and what a second half continues.</summary>
    private static string Shape(SignCutBars cuts)
    {
        var words = new List<string>();
        for (int b = 0; b < cuts.Stream.Bars; b++)
        {
            if (cuts.IsFirstHalf(b))
                words.Add($"{b}:first");
            if (cuts.IsSecondHalf(b))
                words.Add($"{b}:second<{cuts.ContinuedFrom(b)}");
        }
        return string.Join(" ", words);
    }

    /// <summary>The repeat that takes its pickup with it: the bar before the <c>:|</c> lacks a
    /// beat, and both bars played after it — the pickup and the bar after the sign — are that
    /// beat. Whichever part writes the signs.</summary>
    [Theory]
    [InlineData("|: g'4 | c d e f | g a b :| g4 | c1 |", "r4 | c1 | g2. | r4 | c1 |")]
    [InlineData("g'4 | c d e f | g a b | g4 | c1 |", "|: r4 | c1 | g2. :| r4 | c1 |")]
    public void TheBarARepeatSignStandsInIsOneBar(string mel, string bas)
    {
        var cuts = Cuts(mel, bas)!;
        Assert.False(cuts.IsEmpty);
        Assert.Equal("2:first 3:second<2", Shape(cuts));
        Assert.Equal(new Fraction(3, 4), cuts.HeadOf(3));
    }

    /// <summary>A short bar with a plain line after it is a short bar, signs elsewhere or not.</summary>
    [Fact]
    public void AShortBarAtAPlainLineIsNotCut()
    {
        Assert.Null(Cuts("g'4 | c d e f | g a b | g4 | c1 |", "r4 | c1 | g2. | r4 | c1 |"));
        var cuts = Cuts("|: g'4 | c d e f :| g a b | g4 | c1 |", "r4 | c1 | g2. | r4 | c1 |")!;
        Assert.True(cuts.IsEmpty);
        Assert.Equal("", Shape(cuts));
    }

    /// <summary>The repeat returns to a FULL bar: three beats and then four is a short bar on
    /// the first pass, so the bar before the sign is no first half — and the bar after the
    /// sign, though it is the missing beat, continues nothing.</summary>
    [Fact]
    public void ABarOnePassLeavesShortIsNotAFirstHalf()
    {
        var cuts = Cuts("|: c'4 d e f | g a b :| g4 | c1 |", "c1 | g2. | r4 | c1 |", head: "")!;
        Assert.True(cuts.IsEmpty);
        Assert.Equal("2:second<-1", Shape(cuts));
    }

    /// <summary>Endings that open inside a bar: the body's last bar is two beats, each ending
    /// opens with the other two — the second ending continues the BODY's bar, not the first
    /// ending's.</summary>
    [Fact]
    public void EveryEndingContinuesTheBodysLastBar()
    {
        var cuts = Cuts("|: c'4 d e f | g a | [1. b c :| [2. b g ] | c1 |", "c1 | g2 | g2 | g2 | c1 |", head: "")!;
        Assert.Equal("1:first 2:second<1 3:second<1", Shape(cuts));
    }

    /// <summary>One part's bars do not agree: the score has no cut bar there.</summary>
    [Fact]
    public void ThePartsDecideTogether()
    {
        var cuts = Cuts("|: g'4 | c d e f | g a b :| g4 | c1 |", "r4 | c1 | g1 | r4 | c1 |")!;
        Assert.True(cuts.IsEmpty);
    }

    /// <summary>What is written: the line that closes the first half, the line that opens the
    /// second, and the two bars the check does not call short.</summary>
    [Fact]
    public void TheWrittenLinesAndBarsAreNamed()
    {
        var root = SyntaxTree.Parse("""
            time 4/4
            section A { partial 4 }
            part mel { section A { |: g'4 | c d e f | g a b :| g4 | c1 | } }
            form { A }
            score { staff mel }
            """).GetRoot();
        var cuts = SignCutBars.For(root, ScoreForms.Primary(root))!;
        var lines = root.DescendantNodes<BarlineSyntax>().ToList();
        // |:  |  |  :|  |  |
        Assert.Equal([false, false, false, true, false, false], lines.Select(cuts.ClosesAFirstHalf));
        Assert.Equal(new Fraction(3, 4), cuts.HeadBehind(lines[3]));
        Assert.Null(cuts.HeadBehind(lines[2]));
        Assert.Equal(2, SignCutBars.HalvesInEveryForm(root).Count);
    }
}
