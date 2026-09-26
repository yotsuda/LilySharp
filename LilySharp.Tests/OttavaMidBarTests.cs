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
/// An ottava that starts or stops inside a bar moves exactly the notes it spans, and its
/// bracket stops just after the last of them. LilyPond's ottava is a moment-granular span
/// (lily/ottava-engraver.cc:122-136, the right bound the last note column :168-191); until
/// 2026-09-26 Lily# moved whole bars, so an `@!ottava` on a bar's last note left the whole
/// bar at written pitch (Lab probes/complex-lys/07, bar 8). MEASURED against LilyPond 2.26
/// (Lab sessions/p644 o2): the bracket's right end 1.904 past the left of the last head it
/// covers.
/// </summary>
[Trait("Category", "Unit")]
public class OttavaMidBarTests
{
    private static string Svg(string music)
        => SvgGenerator.Generate(SyntaxTree.Parse(
                "octave absolute\npart m { clef bass octave 3 }\nsection A { m { " + music
                + " } }\nform main { ~A }\nscore main { staff m }\n"),
            new SvgRenderOptions { EmbedFont = false });

    private static (double X, double Y)[] Heads(string svg)
        => Regex.Matches(svg, "<text class=\"music\" x=\"([\\d.]+)\" y=\"([\\d.]+)\"[^>]*data-pos[^>]*>\uE0FE<")
            .Select(m => (double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                          double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)))
            .ToArray();

    /// <summary>
    /// A beam's stems are stamped in the direction the page DRAWS them. The stamp is baked at
    /// collect time, before the ottava moves the notes; 03-piano-nocturne (Lab probe) bar 9's
    /// `a gis fis` under an 8va were stamped DOWN off their written positions (above the
    /// middle line) while the beam was drawn UP, and the slur, which reads the stamp, ended on
    /// the last head and cut through the beam. Both collect roads: one staff, and a grand
    /// staff (the per-staff bake).
    /// </summary>
    [Theory]
    [InlineData("staff m")]
    [InlineData("grandStaff { staff m  staff l }")]
    public void AnOttavasBeam_IsStampedTheWayItIsDrawn(string staves)
    {
        var tree = SyntaxTree.Parse(
            "octave absolute\nkey e major\npart m { clef treble }\npart l { clef bass }\n"
            + "section A {\n  m { e''8@ottava( dis'' cis'' b' a' gis' fis' e'@!ottava) | }\n"
            + "  l { e,1 | }\n}\nform main { A }\nscore main { " + staves + " }\n");
        Assert.False(tree.HasErrors);
        var score = SvgGenerator.CollectScore(tree,
            LilySharp.Core.Svg.Collector.RenderSpecParser.FindAll(tree).First());
        var items = score.StaffGroups[0].Staves[0].Voices[0].Measures[0].Items
            .OfType<LilySharp.Core.Svg.Model.NoteItem>().ToArray();

        Assert.Equal(8, items.Length);
        // e d# c# b drawn high in the staff: down. a g# f# e drawn low: up, like the beam.
        Assert.All(items[..4], n => Assert.False(n.StemUpOverride));
        Assert.All(items[4..], n => Assert.True(n.StemUpOverride));
    }

    [Fact]
    public void AStopOnABarsLastNote_KeepsTheBarsOtherNotesUnderTheOttava()
    {
        var shifted = Heads(Svg("a,,4@ottava(bassa) e, a, e, | a,,4 e, a, e,@!ottava | a,,4 e, a, e, |"));
        var plain = Heads(Svg("a,,4 e, a, e, | a,,4 e, a, e, | a,,4 e, a, e, |"));
        // 8vb draws an octave higher: 3.5 staff spaces up — the first seven notes; the eighth
        // (the one the stop is written on) and the last bar are back at written pitch.
        for (int i = 0; i < 12; i++)
            Assert.Equal(i < 7 ? plain[i].Y - 3.5 : plain[i].Y, shifted[i].Y, 2);
    }

    [Fact]
    public void AStartInsideABar_LeavesTheNotesBeforeItAtWrittenPitch()
    {
        var shifted = Heads(Svg("a,,4 e,@ottava(bassa) a, e, | a,,4 e, a, e,@!ottava |"));
        var plain = Heads(Svg("a,,4 e, a, e, | a,,4 e, a, e, |"));
        for (int i = 0; i < 8; i++)
            Assert.Equal(i >= 1 && i < 7 ? plain[i].Y - 3.5 : plain[i].Y, shifted[i].Y, 2);
    }

    [Fact]
    public void TheBracket_StopsJustAfterTheLastNoteItCovers()
    {
        string svg = Svg("a,,4@ottava(bassa) e, a, e, | a,,4 e, a, e,@!ottava | a,,4 e, a, e, |");
        var hook = Regex.Match(svg,
            "<line x1=\"([\\d.]+)\" y1=\"([\\d.]+)\" x2=\"\\1\" y2=\"[\\d.]+\" stroke=\"#000000\" stroke-width=\"0.100\" data-pos");
        double end = double.Parse(hook.Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.Equal(1.904, end - Heads(svg)[6].X, 1);
    }
}
