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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Inside a voice span every script follows its VOICE — voice 1 above, voice 2 below —
/// whatever its own default: \voiceOne / \voiceTwo set `direction` on every grob of
/// direction-polyphonic-grobs, Script among them (scm/music-functions.scm:617-634, :666-674).
/// Only an explicit .up / .down beats it. Until 2026-09-26 Lily# applied only the stem half:
/// voice 1's staccato sat between the voices, voice 2's fermata stood over voice 1, and a
/// drum kit's closed hi-hat '+' in the lower voice landed on the cymbal above (found writing
/// a big-band probe; every case below measured against LilyPond 2.26 in Lab
/// sessions/p641/issues/i9-*).
/// </summary>
[Trait("Category", "Unit")]
public class VoiceScriptDirectionTests
{
    private static bool[] SidesOf(string music, string clef = "treble")
    {
        string src = "octave absolute\npart m { clef " + clef + " }\nsection A { m { " + music
            + " } }\nform main { ~A }\nscore main { staff m }\n";
        var tree = SyntaxTree.Parse(src);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return new LayoutEngine().Layout(score).ArticulationLayouts
            .OrderBy(a => a.MeasureIndex).ThenBy(a => a.X)
            .Select(a => a.IsAbove).ToArray();
    }

    [Theory]
    // voice 1 (stems up): a staccato and an accent go ABOVE, not opposite the stem
    [InlineData("voice { e''4@staccato f''@accent r2 | } { c'1 | }", true)]
    // voice 2 (stems down): a fermata, a trill and an up-bow go BELOW, not their default UP
    [InlineData("voice { e''1 | } { c'4@fermata d'@trill e'@upBow r | }", false)]
    // voice 2: a staccato goes below — opposite-the-stem would have put it ABOVE
    [InlineData("voice { e''1 | } { c'2 e'2@staccato | }", false)]
    public void InAVoiceSpan_EveryScriptFollowsItsVoice(string music, bool above)
        => Assert.All(SidesOf(music), side => Assert.Equal(above, side));

    [Fact]
    public void AnExplicitSide_StillWins()
    {
        // voice 1's .down and voice 2's .up, as LilyPond's ^ / _ beat the voice's direction.
        var sides = SidesOf("voice { g''2@staccato.down r2 | } { r2 e'2@staccato.up | }");
        Assert.Equal(new[] { false, true }, sides);
    }

    [Fact]
    public void OutsideAVoiceSpan_TheOldRulesHold()
    {
        // One voice: a staccato opposite the stem (a high note, stem down → above), a
        // fermata UP whatever the stem.
        Assert.Equal(new[] { true, true }, SidesOf("c''4@staccato c'@fermata r2 |"));
    }

    [Fact]
    public void TheClosedHiHatsPlus_InTheLowerVoice_GoesBelow()
    {
        // hhc carries LilyPond's automatic "stopped" script (ly/drumpitch-init.ly:237).
        var sides = SidesOf(
            "voice { cymr4 cymr cymr cymr | } { bd4 hhc bd hhc | }", clef: "percussion");
        Assert.Equal(new[] { false, false }, sides);
    }
}
