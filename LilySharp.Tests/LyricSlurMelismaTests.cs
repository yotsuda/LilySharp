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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Which note a syllable lands on: a slur and a tie make a melisma of their own, <c>__</c>
/// draws a line and takes no note, <c>_</c> takes one — LilyPond's <c>\lyricsto</c>.
/// </summary>
/// <remarks>
/// Owner's decision 2026-09-24 (session 569): "LP に合わせて". Every row is MEASURED on
/// 2.26.0 (LilySharp-Lab sessions/p569/melisma-assign.ly — the same music under
/// <c>\lyricsto</c>, each syllable's column <c>when</c> dumped). Before it Lily# gave every
/// note a syllable and let only the lyric-side markers hold one, so <c>c'4( d e) f</c> with
/// <c>la __ lb</c> put lb on e.
/// LILYPOND-REF: ly/engraver-init.ly melismaBusyProperties (slurMelismaBusy,
///   tieMelismaBusy) read by lily/lyric-combine-music-iterator.cc.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class LyricSlurMelismaTests
{
    private static (string Text, int Measure, Fraction Timing, bool Left)[] Syllables(
        string music, string words)
    {
        string src = $$"""
            time 4/4
            part m { section A { {{music}} } }
            lyrics w sings m { section A { {{words}} } }
            form main { A }
            score main { staff m  lyrics w }
            """;
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var score = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        return score.Lyrics
            .OrderBy(l => l.MeasureIndex).ThenBy(l => l.Timing)
            .Select(l => (l.Text, l.MeasureIndex, l.Timing, l.MelismaAlignLeft))
            .ToArray();
    }

    [Fact]
    public void ASlur_HoldsItsFirstSyllable_ToItsLastNote()
    {
        var s = Syllables("c4( d e) f |", "la __ lb |");
        Assert.Equal(new[] { "la", "lb" }, s.Select(x => x.Text));
        Assert.Equal(new Fraction(3, 4), s[1].Timing);           // LilyPond: 3/4
        Assert.True(s[0].Left, "the slurred syllable is a melisma — LEFT-aligned");
        Assert.False(s[1].Left);
    }

    [Fact]
    public void ASlur_WithNoExtender_StillHolds()
    {
        var s = Syllables("c4( d e) f |", "la lb |");
        Assert.Equal(new Fraction(3, 4), s[1].Timing);
    }

    [Fact]
    public void ATie_HoldsItsSyllable()
    {
        var s = Syllables("c2~ c4 d |", "la lb |");
        Assert.Equal(new Fraction(3, 4), s[1].Timing);            // LilyPond: 3/4
    }

    [Fact]
    public void ABareExtender_TakesNoNote()
    {
        var s = Syllables("c4 d e f |", "la __ lb lc ld |");
        Assert.Equal(new[] { Fraction.Zero, new Fraction(1, 4), new Fraction(1, 2), new Fraction(3, 4) },
            s.Select(x => x.Timing));                              // LilyPond: 0 1/4 1/2 3/4
    }

    [Fact]
    public void ASkip_TakesOneNote()
    {
        var s = Syllables("c4 d e f |", "la _ lb lc |");
        Assert.Equal(new[] { Fraction.Zero, new Fraction(1, 2), new Fraction(3, 4) },
            s.Select(x => x.Timing));                              // LilyPond: 0 1/2 3/4
    }

    [Fact]
    public void ASlurAcrossTheBar_HoldsIntoTheNextBar()
    {
        var s = Syllables("c2 d2( | e2) f2 |", "la lb | lc |");
        Assert.Equal(new[] { (0, Fraction.Zero), (0, new Fraction(1, 2)), (1, new Fraction(1, 2)) },
            s.Select(x => (x.Measure, x.Timing)));                 // LilyPond: 0 1/2 3/2
    }
}
