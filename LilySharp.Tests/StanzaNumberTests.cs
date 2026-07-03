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

using System.Collections.Immutable;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Tests for StanzaNumber detection at the start of multi-verse lyric lines.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/stanza-number-engraver.cc — StanzaNumber grob
/// </remarks>
[Trait("Category", "Unit")]
public class StanzaNumberTests
{
    private static SystemLayout MakeSystem(int idx, params (int idx, double x)[] measures)
    {
        var ms = measures
            .Select(m => new MeasureLayout(m.idx, x: m.x, width: 10, items: ImmutableArray<ItemLayout>.Empty))
            .ToImmutableArray();
        return new SystemLayout(idx, Y: 10, Width: 100, PrefixWidth: 5, Measures: ms);
    }

    private static LyricItem MakeLyric(string text, int measure, int verse) =>
        new(text, measure, ItemIndex: 0, VerseNumber: verse);

    private static LyricLayout MakeLyricLayout(string text, int measure, int verse, double x, double y) =>
        new(MakeLyric(text, measure, verse), X: x, Y: y, Width: 1.0,
            SourceIndex: -1);

    [Fact]
    public void SingleVerse_DefaultDoesNotEmitStanzaNumber()
    {
        // LP default: single-verse scores skip the "1." prefix.
        var systems = ImmutableArray.Create(MakeSystem(0, (0, 5)));
        var lyrics = ImmutableArray.Create(MakeLyricLayout("la", measure: 0, verse: 1, x: 5, y: 20));
        var stanza = StanzaNumberEngraver.Calculate(lyrics, systems);
        Assert.Empty(stanza);
    }

    [Fact]
    public void TwoVerses_EmitsOneStanzaNumberPerVersePerSystem()
    {
        var systems = ImmutableArray.Create(MakeSystem(0, (0, 5)));
        var lyrics = ImmutableArray.Create(
            MakeLyricLayout("la", measure: 0, verse: 1, x: 5, y: 20),
            MakeLyricLayout("li", measure: 0, verse: 2, x: 5, y: 25));

        var stanza = StanzaNumberEngraver.Calculate(lyrics, systems);
        Assert.Equal(2, stanza.Length);
        Assert.Contains(stanza, s => s.VerseNumber == 1 && s.Text == "1.");
        Assert.Contains(stanza, s => s.VerseNumber == 2 && s.Text == "2.");
    }

    [Fact]
    public void MultipleSystems_EmitsStanzaNumbersPerSystem()
    {
        var systems = ImmutableArray.Create(
            MakeSystem(0, (0, 5)),
            MakeSystem(1, (1, 5)));
        var lyrics = ImmutableArray.Create(
            MakeLyricLayout("la", measure: 0, verse: 1, x: 5, y: 20),
            MakeLyricLayout("li", measure: 0, verse: 2, x: 5, y: 25),
            MakeLyricLayout("lo", measure: 1, verse: 1, x: 5, y: 20),
            MakeLyricLayout("lu", measure: 1, verse: 2, x: 5, y: 25));

        var stanza = StanzaNumberEngraver.Calculate(lyrics, systems);
        Assert.Equal(4, stanza.Length);
    }

    [Fact]
    public void StanzaNumber_PositionedLeftOfFirstLyric()
    {
        var systems = ImmutableArray.Create(MakeSystem(0, (0, 10)));
        var lyrics = ImmutableArray.Create(
            MakeLyricLayout("la", measure: 0, verse: 1, x: 12, y: 20),
            MakeLyricLayout("li", measure: 0, verse: 2, x: 12, y: 25));

        var stanza = StanzaNumberEngraver.Calculate(lyrics, systems);
        // X anchored at system measure[0].X - 4
        Assert.All(stanza, s => Assert.Equal(10 - 4, s.X, precision: 4));
    }

    [Fact]
    public void StanzaNumber_YMatchesVerseLyricBaseline()
    {
        var systems = ImmutableArray.Create(MakeSystem(0, (0, 10)));
        var lyrics = ImmutableArray.Create(
            MakeLyricLayout("la", measure: 0, verse: 1, x: 12, y: 20),
            MakeLyricLayout("li", measure: 0, verse: 2, x: 12, y: 25));

        var stanza = StanzaNumberEngraver.Calculate(lyrics, systems);
        Assert.Equal(20, stanza.First(s => s.VerseNumber == 1).Y, precision: 4);
        Assert.Equal(25, stanza.First(s => s.VerseNumber == 2).Y, precision: 4);
    }
}
