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
using System.Xml.Linq;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// The page's beams in the MusicXML (<c>&lt;beam number="N"&gt;</c>). Until 2026-09-30 the
/// file carried none, so a reader beamed by its own rules — MuseScore joined nine eighths of a
/// 7/4 bar under one beam where the page beams by the beat (LilySharp-Omr
/// docs/repro/musicxml-exporter-bugs.md #11). The groups are the page's own detection, so the
/// file beams what the page beams.
/// </summary>
[Trait("Category", "Unit")]
public class MusicXmlBeamTests
{
    private static XDocument Export(string time, string music)
    {
        var tree = SyntaxTree.Parse(
            $"octave absolute\ntime {time}\npart m {{ clef treble }}\nsection A {{ m {{ {music} }} }}\nform main {{ A }}\nscore main {{ staff m }}");
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        return new MusicXmlExporter().Export(tree).ToXml();
    }

    /// <summary>Per note (chord followers and rests included), its beams as "1b/2e" — b begin,
    /// c continue, e end, fh / bh forward / backward hook — or "-" for none.</summary>
    private static string[] Beams(XDocument doc) =>
        doc.Descendants("note")
            .Where(n => n.Element("chord") == null)
            .Select(n =>
            {
                var beams = n.Elements("beam").Select(b => b.Attribute("number")!.Value + b.Value switch
                {
                    "begin" => "b", "continue" => "c", "end" => "e",
                    "forward hook" => "fh", "backward hook" => "bh", var v => v,
                }).ToArray();
                return beams.Length == 0 ? "-" : string.Join("/", beams);
            })
            .ToArray();

    [Fact]
    public void EighthsInFourFour_AreBeamedByTheHalfBar_AsThePageBeamsThem()
        => Assert.Equal(new[] { "1b", "1c", "1c", "1e", "1b", "1c", "1c", "1e" },
            Beams(Export("4/4", "c'8 d' e' f' g' a' b' c'' |")));

    [Fact]
    public void ADottedEighthAndItsSixteenth_TheSixteenthHooksBack()
        => Assert.Equal(new[] { "1b", "1e/2bh", "-", "-" },
            Beams(Export("4/4", "c'8. d'16 e'4 f'2 |")));

    [Fact]
    public void AChordCarriesItsBeamOnItsFirstNote()
    {
        var doc = Export("2/4", "<c' e'>8 <d' f'> <e' g'> <f' a'> |");
        // 2/4 beams by the quarter, as the page (and LilyPond) does: two pairs.
        Assert.Equal(new[] { "1b", "1e", "1b", "1e" }, Beams(doc));
        Assert.All(doc.Descendants("note").Where(n => n.Element("chord") != null),
            n => Assert.Empty(n.Elements("beam")));
    }

    /// <summary>The report's 7/4 bar (beams.lys, bar 2): the page beams by the beat — pairs and
    /// short groups — where a reader's own rule ran nine notes together.</summary>
    [Fact]
    public void ASevenFourBar_IsBeamedByTheBeat()
        => Assert.Equal(
            new[] { "1b", "1e", "1b/2b", "1c/2e", "1e", "-", "-", "1b", "1e/2bh", "1b/2b", "1c/2e", "1e", "1b", "1e", "1b", "1e/2bh" },
            Beams(Export("7/4", "d'8 c'8 f'16 d'16 d'8 r8 c'8 aes8. bes16 c'16 f16 c'8 bes8 bes8 c'8. bes16 |")));
}
