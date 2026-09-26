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
using LilySharp.Core.LilyPond;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A note takes one dynamic: in <c>c4@f@sfz</c> the <c>f</c> is engraved and the <c>sfz</c>
/// is not, and LYS4022 says so at it — LilyPond's rule (lily/dynamic-engraver.cc:66-70,
/// assign_event_once), which the page broke by stacking both until 2026-09-26 (Lab
/// probes/complex-lys/05). Owner's decision, session 645.
/// </summary>
[Trait("Category", "Unit")]
public class DoubleDynamicTests
{
    private const string Book = """
        octave absolute
        part m { clef treble }
        section A { m { c'4@f@sfz d' e'@p@cresc f' | g'1@ff | } }
        form main { A }
        score main { staff m }
        """;

    [Fact]
    public void TheSecondDynamic_IsReported_AndAHairpinBesideADynamicIsNot()
    {
        var found = SemanticValidation.Run(SyntaxTree.Parse(Book))
            .Where(d => d.Code == DiagnosticCodes.DoubleDynamic).ToList();
        Assert.Single(found);
        Assert.Contains("'@sfz'", found[0].Message);
    }

    [Fact]
    public void ThePage_EngravesTheFirstDynamicOnly()
    {
        var tree = SyntaxTree.Parse(Book);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        Assert.Equal(new[] { "F", "P", "FF" },
            score.Dynamics.Select(d => d.Level.ToString().ToUpperInvariant()).ToArray());
    }

    [Fact]
    public void TheTwin_WritesTheFirstDynamicOnly()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(Book));
        Assert.Contains("\\f", ly);
        Assert.DoesNotContain("\\sfz", ly);
    }
}
