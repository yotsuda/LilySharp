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
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The tuplet bracket avoids scripts by default: every priority-less script of the
/// tuplet's notes joins the bracket's offset pass as the point (script X centre,
/// script ink edge on the bracket's side), and the bracket clears the winner by
/// padding 1.1. Scripts that DECLARE an outside-staff-priority (the fermata family)
/// are outside-staff movers and are skipped — they clear the bracket, not the other
/// way around.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/tuplet-bracket.cc:682-706 calc_position_and_height
///   (the avoid-scripts block, gated on the default avoid-scripts #t).
/// LP oracle (tuplet-bracket-avoid-scripts.ly twin, audit/lpreg/tupavsc*):
/// for <c>\tuplet 3/2 { a'8^\accent r a'^\accent }</c> LilyPond puts the flat
/// bracket 5.14 staff spaces above the middle line — accent ink top 4.04 + 1.10 —
/// while stem tips reach only 3.00 (stem-driven placement would give 4.10).
/// </remarks>
[Trait("Category", "Unit")]
public class TupletBracketAvoidScriptsTests
{
    private static ScoreLayout BuildLayout(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);
        var engine = new LayoutEngine(new LayoutOptions());
        return engine.Layout(score);
    }

    [Fact]
    public void ForcedUpScripts_RaiseTheBracket_LpExact()
    {
        // The regression book's music: both notes A4, scripts forced above,
        // bracket above. A bare fragment is relative-from-C4, so the SECOND a
        // is spelled bare (nearest = unison), exactly as the LP book spells it.
        var layout = BuildLayout("tuplet 3/2 { a'8@accent.up r8 a8@accent.up } |");
        var bracket = Assert.Single(layout.TupletBracketLayouts);

        // Flat bracket (both accents at the same height).
        Assert.Equal(bracket.StartYUp, bracket.EndYUp, precision: 6);

        // LP: bracket edge 5.14 above the staff middle (StartYUp is above the
        // staff TOP line, middle = top − 2.0).
        Assert.Equal(5.14, bracket.StartYUp + 2.0, precision: 2);
    }

    [Fact]
    public void BracketClearsScriptInkTop_ByPadding()
    {
        var layout = BuildLayout("tuplet 3/2 { a'8@accent.up r8 a8@accent.up } |");
        var bracket = Assert.Single(layout.TupletBracketLayouts);
        var accents = layout.ArticulationLayouts
            .Where(a => a.OutsideStaffPriority == null).ToArray();
        Assert.Equal(2, accents.Length);

        // The bracket sits exactly TupletBracket padding 1.1 above the accents'
        // ink top — the avoid-scripts point won the offset pass over the stems.
        double inkTop = accents.Max(a => a.YUp + a.Ink.Top);
        Assert.Equal(1.1, (bracket.StartYUp + 2.0) - inkTop, precision: 6);
    }

    [Fact]
    public void FermataFamily_DoesNotRaiseTheBracket()
    {
        // A fermata declares outside-staff-priority 75: LP skips it in the
        // avoid-scripts pass (it is a mover and clears the BRACKET instead).
        // LILYPOND-REF: lily/tuplet-bracket.cc:690-692 calc_position_and_height
        //   (the outside-staff-priority skip).
        var plain = BuildLayout("tuplet 3/2 { a8 r8 a8 } |");
        var fermata = BuildLayout("tuplet 3/2 { a8@fermata.up r8 a8@fermata.up } |");
        var plainBracket = Assert.Single(plain.TupletBracketLayouts);
        var fermataBracket = Assert.Single(fermata.TupletBracketLayouts);

        Assert.Equal(plainBracket.StartYUp, fermataBracket.StartYUp, precision: 6);
    }
}
