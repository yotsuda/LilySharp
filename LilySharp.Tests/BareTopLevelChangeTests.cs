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

using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A `key` / `time` / `tempo` written AFTER bare top-level music is that stream's
/// mid-music change, not the file default — the same rule the bare-stream `clef`
/// already follows (ClefChangeTests, the topLevelMusicSeen guard). Before the guard
/// covered these three, CollectDefinitions made the directive the file default AND
/// the music walk engraved the change: the piece opened in the changed key/time/tempo
/// and the change printed a second time at its own position.
/// </summary>
public class BareTopLevelChangeTests
{
    private static List<(double X, char Glyph)> MusicGlyphs(string source)
    {
        string svg = SvgGenerator.Generate(
            SyntaxTree.Parse(source),
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });
        return Regex.Matches(svg,
                "<text class=\"music\" x=\"([-\\d.]+)\"[^>]*>(&#x([0-9A-Fa-f]+);|.)</text>")
            .Select(m => (
                X: double.Parse(m.Groups[1].Value),
                Glyph: m.Groups[3].Success
                    ? (char)Convert.ToInt32(m.Groups[3].Value, 16)
                    : m.Groups[2].Value[0]))
            .ToList();
    }

    [Fact]
    public void BareStreamKey_AfterMusic_IsAChangeNotTheFileDefault()
    {
        // `g'1 key g major g'1`: the piece OPENS in C major (no sharp in the opening
        // signature) and ONE sharp prints at the change point after bar 1. Before the
        // guard, the sharp printed twice: once in the opening signature (file default
        // wrongly G) and once as the walked change.
        var glyphs = MusicGlyphs("g'1 key g major g'1");

        var sharps = glyphs.Where(g => g.Glyph == LilySharp.Core.Svg.EmmentalerGlyphs.AccidentalSharp)
            .Select(g => g.X).ToList();
        var heads = glyphs.Where(g => g.Glyph == LilySharp.Core.Svg.EmmentalerGlyphs.NoteheadWhole)
            .Select(g => g.X).OrderBy(x => x).ToList();

        Assert.Equal(2, heads.Count);
        var sharp = Assert.Single(sharps);
        // The single sharp is the mid-music change: AFTER bar 1's head, BEFORE bar 2's.
        Assert.True(sharp > heads[0] && sharp < heads[1],
            $"change sharp at {sharp} should sit between the heads ({heads[0]} .. {heads[1]})");
    }

    [Fact]
    public void BareStreamTime_AfterMusic_IsAChangeNotTheFileDefault()
    {
        // `g'1 time 3/4 g'2.`: the piece opens in the DEFAULT 4/4 (common-time C
        // glyph), and 3/4 prints once at the change point. Before the guard, 3/4 was
        // both the opening signature (bar 1 overfull) and a printed change.
        var glyphs = MusicGlyphs("g'1 time 3/4 g'2.");

        Assert.Single(glyphs.Where(g => g.Glyph == LilySharp.Core.Svg.EmmentalerGlyphs.TimeSigCommon));
        Assert.Single(glyphs.Where(g => g.Glyph == LilySharp.Core.Svg.EmmentalerGlyphs.TimeSig3));
        var head = glyphs.First(g => g.Glyph == LilySharp.Core.Svg.EmmentalerGlyphs.NoteheadWhole).X;
        var three = glyphs.First(g => g.Glyph == LilySharp.Core.Svg.EmmentalerGlyphs.TimeSig3).X;
        Assert.True(three > head, "the 3/4 change prints after bar 1, not in the opening signature");
    }

    [Fact]
    public void BareStreamTempo_AfterMusic_IsAChangeNotTheScoreDefault()
    {
        // `g'1 tempo 100 g'1`: ♩=100 prints ONCE, as a mid-music metronome mark.
        // Before the guard it was also collected as the score's opening tempo.
        string svg = SvgGenerator.Generate(
            SyntaxTree.Parse("g'1 tempo 100 g'1"),
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });

        Assert.Single(Regex.Matches(svg, "= 100"));
    }
}
