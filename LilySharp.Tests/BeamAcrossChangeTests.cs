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

using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// An auto-beam runs across a clef or key change, and the beam clears the change's glyph.
/// LilyPond's Auto_beam_engraver acknowledges stems, beams, breathing signs and rests only
/// (lily/auto-beam-engraver.cc:519-523 ADD_ACKNOWLEDGER), so a Clef or a KeySignature between
/// two eighths does not end the beam; Beam_collision_engraver books both as covered grobs
/// (lily/beam-collision-engraver.cc:217-225 acknowledge_clef / acknowledge_key_signature), so
/// the beam is quanted clear of them. Until
/// 2026-09-26 Lily# split the beam at the change. MEASURED against LilyPond 2.26 (Lab
/// sessions/p643 cb, sessions/p642 kb): one Beam grob each, its centre 1.81 / 2.19 staff
/// spaces below the middle line.
/// </summary>
[Trait("Category", "Unit")]
public class BeamAcrossChangeTests
{
    private static (int Beams, double CentreBelowMiddle) Beam(string key, string music)
    {
        string src = "octave absolute\nkey " + key + "\npart m { clef treble }\nsection A { m { "
            + music + " } }\nform main { ~A }\nscore main { staff m }\n";
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(src), new SvgRenderOptions { EmbedFont = false });
        double middle = Regex.Matches(svg,
                "<line x1=\"[\\d.]+\" y1=\"([\\d.]+)\" x2=\"[\\d.]+\" y2=\"\\1\" stroke=\"#000000\" stroke-width=\"0.100\"")
            .Take(5).Average(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
        var beams = Regex.Matches(svg, "<polygon points=\"([^\"]+)\"");
        var ys = beams[0].Groups[1].Value.Split(' ')
            .Select(p => double.Parse(p.Split(',')[1], CultureInfo.InvariantCulture)).ToArray();
        return (beams.Count, (ys.Min() + ys.Max()) / 2 - middle);
    }

    [Theory]
    [InlineData("c major", "c''8 d'' clef bass e8 a c'4 a |", 1.81)]
    [InlineData("f major", "c''8 d'' key e major e'' a'' c''4 a' |", 2.19)]
    public void TheBeamRunsAcrossTheChange_AndClearsIt(string key, string music, double lpCentre)
    {
        var (beams, centre) = Beam(key, music);
        Assert.Equal(1, beams);
        Assert.Equal(lpCentre, centre, 2);
    }
}
