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
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>nobreak</c> (LilyPond's <c>\noBreak</c>) forbids a line break after the
/// measure it closes — the mirror of <c>break</c>, which forces one. The layout's
/// Forbid handling is covered elsewhere; these pin the keyword → permission wiring.
/// </summary>
[Trait("Category", "Unit")]
public sealed class NoBreakTests
{
    private static Measure[] Collect(string body)
    {
        string src = "time 4/4\nkey c major\npart m { section A { " + body + " } }\n"
                   + "form main { A }\nscore main { staff m }";
        return new MeasureCollector().Collect(SyntaxTree.Parse(src), "m").Voice.Measures.ToArray();
    }

    [Fact]
    public void NoBreak_ForbidsTheBreakAfterThePrecedingMeasure()
    {
        // `nobreak` at the second bar's start forbids the break between bars 1 and 2.
        var m = Collect("c4 d e f | nobreak g a b c |");
        Assert.Equal(BreakPermission.Forbid, m[0].LineBreakPermission);
        Assert.False(m[0].HasBreakAfter);
    }

    [Fact]
    public void Break_StillForcesABreak()
    {
        var m = Collect("c4 d e f | break g a b c |");
        Assert.Equal(BreakPermission.Force, m[0].LineBreakPermission);
        Assert.True(m[0].HasBreakAfter);
    }

    [Fact]
    public void MidMeasureNoBreak_AppliesToThatMeasure()
    {
        // A `nobreak` written inside a measure forbids the break after that measure.
        var m = Collect("c4 d nobreak e f | g a b c |");
        Assert.Equal(BreakPermission.Forbid, m[0].LineBreakPermission);
    }
}
