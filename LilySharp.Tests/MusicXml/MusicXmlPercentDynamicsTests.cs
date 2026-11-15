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

using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// A percent repeat's repetition says none of its bars' dynamics (owner's decision 2026-11-15,
/// session 1264). The file writes the repeated bars' notes again under the sign; the page
/// prints no level and no hairpin there, and the document's wedges are the page's. Until then
/// the levels were written again and the hairpins were not: <c>c4@p d e@cresc f | % | c1@f</c>
/// said a p in the middle of the crescendo that runs through the repeated bar
/// (Lab sessions/p1262/w/w2, sessions/p1264/pc).
/// </summary>
[Trait("Category", "Unit")]
public class MusicXmlPercentDynamicsTests
{
    /// <summary>Each measure's levels and wedges, in document order.</summary>
    private static List<string> Said(string bars)
    {
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\npart m { clef treble }\nsection A { m {\n" + bars
            + "\n} }\nform { A }\nscore { staff m }\n");
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var doc = XDocument.Parse(new MusicXmlExporter().Export(tree).ToXml().ToString());
        return doc.Descendants("measure").Select(m => string.Join(" ", m.Elements("direction").Select(d =>
                d.Descendants("wedge").FirstOrDefault() is { } w ? "wedge:" + (string?)w.Attribute("type")
                : d.Descendants("dynamics").FirstOrDefault()?.Elements().First().Name.LocalName)
            .Where(s => s != null))).ToList();
    }

    /// <summary>The crescendo runs through the repeated bar to the f, and the bar says no p.
    /// The f after the run is written as ever.</summary>
    [Fact]
    public void TheRepeatedBar_SaysNoLevelInsideTheHairpinThatRunsThroughIt()
        => Assert.Equal(["p wedge:crescendo", "", "wedge:stop f"], Said("c''4@p d'' e''@cresc f'' | % | c''1@f |"));

    /// <summary>Every repetition, a niente too, and a two-bar sign's two bars.</summary>
    [Fact]
    public void EveryRepetition_IsSilentOfDynamics()
    {
        Assert.Equal(["f n", "", "", "p"], Said("c''4@f d'' e'' f''@niente | % % | c''1@p |"));
        Assert.Equal(["mf", "p", "", "", "f"], Said("c''4@mf d'' e'' f'' | d''4@p e'' f'' g'' | %% | c''1@f |"));
    }

    /// <summary>Both voices of the repeated bar.</summary>
    [Fact]
    public void ALaterVoicesLevels_AreNotSaidAgainEither()
        => Assert.Equal(["f p", "", ""], Said("voice { c''4@f d'' e'' f'' } { e'1@p } | % | c''1 |"));
}
