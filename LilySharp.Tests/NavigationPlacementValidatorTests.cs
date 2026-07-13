// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Licensed under the GNU General Public License v3 or later.

using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class NavigationPlacementValidatorTests
{
    private static bool WarnsMidMeasure(string src)
    {
        var v = new NavigationPlacementValidator();
        v.Validate(SyntaxTree.Parse(src));
        return v.Diagnostics.Any(d => d.Code == DiagnosticCodes.NavigationMarkMidMeasure);
    }

    private const string MidMeasure = """
        time 4/4
        part m { clef treble section A { c'4 segno d' e' f' | } }
        form main { A }
        score main { staff m }
        """;

    private const string AtBoundary = """
        time 4/4
        part m { clef treble section A { c'4 d' e' f' | segno g'4 a' b' c'' | } }
        form main { A }
        score main { staff m }
        """;

    [Fact]
    public void Collect_RecordsMidMeasureNavPlacement()
    {
        var c = new MeasureCollector();
        c.Collect(SyntaxTree.Parse(MidMeasure), "m");
        Assert.NotEmpty(c.NavigationPlacementWarnings);
    }

    [Fact]
    public void MidMeasureNav_Warns() => Assert.True(WarnsMidMeasure(MidMeasure));

    [Fact]
    public void BoundaryNav_IsClean() => Assert.False(WarnsMidMeasure(AtBoundary));
}
