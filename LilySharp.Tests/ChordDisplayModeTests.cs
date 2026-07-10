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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The chord display selector: <c>staff X with chords Y as roman | both | names</c>
/// (and the same on a chord row). Absolute names by default, Roman-numeral degrees
/// for the key, or both stacked.
/// </summary>
public class ChordDisplayModeTests
{
    private const string Doc = """
        octave absolute
        time 4/4
        key c major
        part melody { clef treble }
        section A { melody { e'4 e' f' g' | a' g' e' d' | } }
        chords harmony { c1 | a1:m | }
        form main { A }
        score main "names" { staff melody with chords harmony }
        score main "roman" { staff melody with chords harmony as roman }
        score main "both"  { staff melody with chords harmony as both }
        score main "row"   { chords harmony as roman }
        """;

    private static ChordDisplayMode StaffMode(string score)
        => RenderSpecParser.FindByName(SyntaxTree.Parse(Doc), score)!
            .GetVoiceBindings().First().ChordDisplay;

    [Fact]
    public void AsSelector_ParsesEachMode()
    {
        // `as` doubles as the Dutch A-flat pitch, so it is matched by text — this
        // guards that the selector still reads correctly.
        Assert.Equal(ChordDisplayMode.Names, StaffMode("names"));
        Assert.Equal(ChordDisplayMode.Roman, StaffMode("roman"));
        Assert.Equal(ChordDisplayMode.Both, StaffMode("both"));
    }

    [Fact]
    public void ChordRow_AsRoman_Parses()
    {
        var rs = RenderSpecParser.FindByName(SyntaxTree.Parse(Doc), "row")!;
        var row = rs.Items.OfType<ChordRowSpec>().Single();
        Assert.Equal(ChordDisplayMode.Roman, row.DisplayMode);
    }

    [Fact]
    public void Collect_Roman_StampsDegreeAndMode()
    {
        var score = new MeasureCollector()
            .Collect(SyntaxTree.Parse(Doc), "melody", null, "harmony", ChordDisplayMode.Roman);
        // C major: c1 -> I, a1:m -> VIm.
        Assert.Equal(new[] { "I", "VIm" }, score.ChordNames.Select(c => c.RomanText).ToArray());
        Assert.All(score.ChordNames, c => Assert.Equal(ChordDisplayMode.Roman, c.DisplayMode));
        // The absolute name is still carried (Both mode shows it below the degree).
        Assert.Equal(new[] { "C", "Am" }, score.ChordNames.Select(c => c.ChordText).ToArray());
    }

    [Fact]
    public void InlineChord_FollowsTheStaffsRomanMode()
    {
        // A staff with `as roman` shows its inline @chord as a degree too, so it does
        // not clash with the track's Roman symbol (both render "Imaj7").
        var tree = SyntaxTree.Parse("""
            octave absolute
            time 4/4
            key c major
            part melody { clef treble }
            section A { melody { c'4@chord(c:maj7) c' g' g' | } }
            chords harmony { c:maj7 | }
            form main { A }
            score main { staff melody with chords harmony as roman }
            """);
        var score = new MeasureCollector()
            .Collect(tree, "melody", null, "harmony", ChordDisplayMode.Roman);
        Assert.Equal(2, score.ChordNames.Length); // inline + track, same slot
        Assert.All(score.ChordNames, c => Assert.Equal("Imaj7", c.RomanText));
        Assert.All(score.ChordNames, c => Assert.Equal(ChordDisplayMode.Roman, c.DisplayMode));
    }

    [Fact]
    public void MidPieceKeyChange_RebasesRomanDegrees()
    {
        // Modulate C -> G mid-section: a G chord is V in C but I in G.
        var tree = SyntaxTree.Parse("""
            octave absolute
            time 4/4
            key c major
            part melody { clef treble }
            section A { melody { c'4 c' c' c' | key g major d' d' d' d' | } }
            chords harmony { c1 | g1 | }
            form main { A }
            score main { staff melody with chords harmony as roman }
            """);
        var score = new MeasureCollector()
            .Collect(tree, "melody", null, "harmony", ChordDisplayMode.Roman);
        Assert.Equal(new[] { "I", "I" },
            score.ChordNames.OrderBy(c => c.MeasureIndex).Select(c => c.RomanText).ToArray());
    }

    [Fact]
    public void Collect_DefaultNames_HasNoModeButStillComputesDegree()
    {
        var score = new MeasureCollector()
            .Collect(SyntaxTree.Parse(Doc), "melody", null, "harmony");
        Assert.All(score.ChordNames, c => Assert.Equal(ChordDisplayMode.Names, c.DisplayMode));
    }
}
