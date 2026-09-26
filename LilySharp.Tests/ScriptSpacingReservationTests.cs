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
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The room a fermata reserves in the note spacing (SpacingRules.ApplyArticulationSpacing →
/// ArticulationEngraver.SpacingInkBox) is LilyPond's separation box: the script's PURE
/// extent, on the script's own voice's note. Each pair below renders the same bar with and
/// without the fermata; LilyPond 2.26 spaces both alike (Lab sessions/p642, r0/r1 and
/// v0/v1), and until 2026-09-26 Lily# widened the fermata's column by 0.47.
/// </summary>
[Trait("Category", "Unit")]
public class ScriptSpacingReservationTests
{
    private static string[] NoteXs(string music)
    {
        string src = "octave absolute\npart m { clef treble }\nsection A { m { " + music
            + " } }\nform main { ~A }\nscore main { staff m }\n";
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(src),
            new SvgRenderOptions { EmbedFont = false });
        // Every music glyph but the fermata: heads, the accidental, the rest.
        return Regex.Matches(svg, "<text class=\"music\" x=\"([\\d.]+)\"[^>]*>([^<]*)<")
            .Where(m => m.Groups[2].Value != "" && m.Groups[2].Value != "")
            .Select(m => m.Groups[1].Value).ToArray();
    }

    [Fact]
    public void AFermataOnABeamedUpStem_ClearsTheBeamsPureTip_NotItsOwnStem()
    {
        // The beam runs up to the f#'' next door, so its pure extent (every up stem's tip
        // united, lily/stem.cc:387-447) keeps the fermata above the sharp. Reading the
        // fermata's own unbeamed stem put its box level with the sharp.
        Assert.Equal(NoteXs("e16 e16 fis'16 e16 e4 r2 |"),
                     NoteXs("e16 e16@fermata fis'16 e16 e4 r2 |"));
    }

    [Fact]
    public void ALowerVoicesFermata_IsReservedOnItsOwnNote_NotOnVoice1s()
    {
        // Voice 2's fermata hangs below its own a, (\voiceTwo). Matched by item index
        // alone it was reserved above voice 1's e — the note with the same index — and
        // collided there with the f#'' next door.
        Assert.Equal(NoteXs("voice { e16 e16 fis'16 e16 e4 r2 | } { a,16 a,16 a,16 a,16 a,4 r2 | }"),
                     NoteXs("voice { e16 e16 fis'16 e16 e4 r2 | } { a,16 a,16@fermata a,16 a,16 a,4 r2 | }"));
    }

    private static string GraceX(string music)
    {
        string src = "octave absolute\npart m { clef treble }\nsection A { m { " + music
            + " } }\nform main { ~A }\nscore main { staff m }\n";
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(src),
            new SvgRenderOptions { EmbedFont = false });
        // The grace head: the one music glyph at the grace size.
        return Regex.Match(svg, "<text class=\"music\"[^>]*? x=\"([\\d.]+)\" y=\"[\\d.]+\" font-size=\"2\\.83\"")
            .Groups[1].Value;
    }

    [Theory]
    // voice 1's fermata is on voice 1's note, not on the lower voice's grace's main note
    // (item 1 in both voices: the grace is the lower voice's item 0)
    [InlineData("voice { g'4 g'4@fermata g'2 | } { grace { e'16 } c4 c4 c2 | }")]
    // voice 2's own fermata hangs BELOW (\voiceTwo), out of the grace flag's band
    [InlineData("voice { g'4 g'4 g'2 | } { grace { e'16 } c4@fermata c4 c2 | }")]
    public void ALowerVoicesGrace_IsNotPushedByAFermataItsFlagCannotReach(string music)
    {
        // LilyPond 2.26 stands the grace at the same x in all three (Lab sessions/p642 w0-w2);
        // Lily# pushed it 0.37 left for either fermata.
        Assert.Equal(GraceX("voice { g'4 g'4 g'2 | } { grace { e'16 } c4 c4 c2 | }"), GraceX(music));
    }
}
