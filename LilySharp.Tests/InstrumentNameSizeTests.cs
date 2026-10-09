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
/// An instrument name is set at the paper's text size, as LilyPond sets it (InstrumentName
/// declares no font-size), so a name that fits LilyPond's fixed 15mm indent fits Lily#'s.
/// </summary>
/// <remarks>
/// Until session 733 the name was 3.0 staff spaces, and "Clarinet in B♭" in a chamber score
/// ran off the page's left edge (Lab sessions/p733/iname n1) where LilyPond's 2.2 keeps it on
/// the page. Owner's decision, 2026-10-01.
/// </remarks>
[Trait("Category", "Unit")]
public class InstrumentNameSizeTests
{
    [Fact]
    public void ALongName_IsSetAtTheTextSize_AndStaysOnThePage()
    {
        string svg = SvgGenerator.Generate(SyntaxTree.Parse("""
            part cl { clef treble }
            part hn { clef treble }
            section S { cl { c'1 | } hn { c'1 | } }
            form { ~S }
            score { staffGroup { staff cl "Clarinet in B♭"  staff hn "Horn" } }
            """), new SvgRenderOptions { EmbedFont = false });

        var name = Regex.Match(svg,
            "<text x=\"([\\d.]+)\" y=\"[\\d.]+\" font-size=\"([\\d.]+)\" text-anchor=\"end\"[^>]*>Clarinet in B♭</text>");
        Assert.True(name.Success, "no right-anchored instrument name drawn");
        Assert.Equal(EngravingDefaults.TextScriptFontSize.ToString("F2", CultureInfo.InvariantCulture),
            name.Groups[2].Value);

        // The name's left edge, in page units: its right-anchored x, less its advance, plus the
        // translate of the group it is drawn in (the left margin).
        double right = double.Parse(name.Groups[1].Value, CultureInfo.InvariantCulture);
        double width = LilySharp.Core.Rendering.TextFontMetrics.Advance("Clarinet in B♭",
            EngravingDefaults.TextScriptFontSize, sans: false, LilySharp.Core.Rendering.FontStyle.Regular);
        var group = Regex.Match(svg, "<g transform=\"translate\\(([\\d.]+),");
        double origin = group.Success ? double.Parse(group.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        Assert.True(origin + right - width >= 0,
            $"the name's left edge stands at {origin + right - width:F3}, off the page");
    }
}
