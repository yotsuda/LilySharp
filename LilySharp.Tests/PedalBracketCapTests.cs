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

using LilySharp.Core.Rendering.Boxes;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A pedal bracket's lines are round-capped, as LilyPond's are: each piece is a
/// <c>draw-line</c> whose ends reach half the thickness past its points (lily/bracket.cc
/// Bracket::make_bracket over lily/line-interface.cc make_line). MEASURED 2.26.0 (Lab
/// sessions/p1387/m b1, b2): the PianoPedalBracket's extent is −5.300 … −4.200 under a quarter
/// of c'' d'' e'' f'' — 1.100 high, the hook's 1.0 plus a cap at each end — and a change's notch
/// peaks 0.05 past its note. Butt-capped, the bracket stood 1.050 high and the notch peaked on
/// the note.
/// </summary>
public class PedalBracketCapTests
{
    private static BoxSymbol[] BracketLines(string music)
    {
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\npaper { raggedRight }\npart m { clef treble  pedal bracket }\n"
            + $"section A {{ m {{\n{music}\n}} }}\nform {{ A }}\nscore {{ staff m }}\n");
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        var page = Assert.Single(BoxesGenerator.GeneratePages(tree, RenderSpecParser.FindFirst(tree)));
        return [.. page.Symbols.Where(s => s.Kind == "line")];
    }

    private static double Head(string music, int nth)
    {
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\npaper { raggedRight }\npart m { clef treble  pedal bracket }\n"
            + $"section A {{ m {{\n{music}\n}} }}\nform {{ A }}\nscore {{ staff m }}\n");
        var page = Assert.Single(BoxesGenerator.GeneratePages(tree, RenderSpecParser.FindFirst(tree)));
        return page.Symbols.Where(s => s.Kind == "notehead").Select(s => s.Box[0]).OrderBy(x => x).ElementAt(nth);
    }

    [Fact]
    public void TheBracket_IsTheHookPlusACapAtEachEnd_High()
    {
        var lines = BracketLines("c''4@sustain d'' e'' f''@!sustain | c''1 |");
        Assert.NotEmpty(lines);
        Assert.Equal(1.100, lines.Max(s => s.Box[3]) - lines.Min(s => s.Box[1]), 3);
    }

    [Fact]
    public void AChangesNotch_PeaksHalfTheThicknessPastItsNote()
    {
        const string music = "c''4@sustain d'' e''@!sustain@sustain f''@!sustain | c''1 |";
        var lines = BracketLines(music);
        double change = Head(music, 2);   // the e'' the change is written on
        // The two flares meet over the note's left edge (its column): each one's box reaches a
        // half thickness past it, 0.05 — LilyPond's 6.054 over the column 6.004.
        var flares = lines.Where(s => s.Box[0] < change && s.Box[2] > change).ToArray();
        Assert.Equal(2, flares.Length);
        Assert.Equal(change + 0.05, flares.Min(s => s.Box[2]), 3);
        Assert.Equal(change - 0.05, flares.Max(s => s.Box[0]), 3);
    }
}
