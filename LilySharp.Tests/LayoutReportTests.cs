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

using LilySharp.Core.Svg;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The 'layout' report exposes the engine's line-break / system / page decisions as
/// plain text, so an author (AI or human) can verify the layout without rendering.
/// </summary>
[Trait("Category", "Unit")]
public class LayoutReportTests
{
    private static string Report(string src) => LayoutReport.Generate(SyntaxTree.Parse(src));

    [Fact]
    public void ForcedBreaks_AreReportedAtTheirBars()
    {
        // 'break' after bar 2 and bar 4 → three systems, breaks at exactly 2 and 4.
        var report = Report(
            "part melody\n" +
            "section Main {\n" +
            "  melody {\n" +
            "    c4 d e f | g4 a b c' |\n" +
            "    break\n" +
            "    c4 d e f | g2. r4 |\n" +
            "    break\n" +
            "    e4 f g a | b2. r4 |\n" +
            "  }\n" +
            "}\n" +
            "structure { Main }\n" +
            "score \"brk\" { staff melody }\n");

        Assert.Contains("score \"brk\"", report);
        Assert.Contains("3 systems", report);
        Assert.Contains("line breaks after bar: 2, 4", report);
    }

    [Fact]
    public void SingleSystem_ReportsNoBreaks()
    {
        var report = Report(
            "part melody\n" +
            "section Main { melody { c4 d e f | } }\n" +
            "structure { Main }\n" +
            "score \"one\" { staff melody }\n");

        Assert.Contains("1 system,", report);
        Assert.Contains("line breaks after bar: (none", report);
    }

    [Fact]
    public void Header_ListsEveryStaffAndClef()
    {
        var report = Report(
            "part rh { clef treble }\n" +
            "part lh { clef bass }\n" +
            "section A { rh { c'4 d' e' f' | } lh { c2 g, | } }\n" +
            "structure { A }\n" +
            "score \"gs\" { grandStaff { staff rh staff lh } }\n");

        Assert.Contains("staves: treble, bass", report);
    }
}
