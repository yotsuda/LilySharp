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
using LilySharp.Core.Rendering;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A dynamic hangs below its line spanner by LilyPond's <c>(scale-by-font-size -0.6)</c>, so a
/// score that resizes its dynamics moves that gap with the letters (HANDOFF §2 R10⒣).
/// </summary>
/// <remarks>
/// MEASURED on 2.26.0 (LilySharp-Lab sessions/p569/dyn-yoffset.ly, -dbackend=null): a
/// DynamicText's Y relative to its DynamicLineSpanner is −0.6 at font-size 0,
/// −0.424264068711929 at −3 and −0.755952629936924 at +2. Until 2026-09-24 Lily# held 0.6
/// whatever the size.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class DynamicTextOffsetTests
{
    private static ScoreTextMetrics Fonts(string? fontsBlock)
    {
        if (fontsBlock == null)
            return ScoreTextMetrics.Bundled;
        var font = SyntaxTree.Parse(fontsBlock).GetRoot().DescendantNodes<FontDeclarationSyntax>().First();
        return new ScoreTextMetrics(FontPlanReader.Read(font, out _));
    }

    [Theory]
    [InlineData(null, 0.6)]
    [InlineData("fonts { dynamics step -3 }", 0.4242640687119285)]
    [InlineData("fonts { dynamics step +2 }", 0.7559526299369239)]
    public void TheTextHangsBelowItsSpanner_ByLilyPondsScaledOffset(string? fontsBlock, double lilypond)
        => Assert.Equal(lilypond, DynamicEngraver.TextOffsetInSpanner(Fonts(fontsBlock)), 9);
}
