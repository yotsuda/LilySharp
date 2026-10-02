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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The twin writes <c>@figuredBass</c> as LilyPond's FiguredBass context under the staff —
/// a <c>\figuremode</c> stream read off the page, each group at its note's onset — and no
/// longer reports a <c>@chord</c> or <c>@figuredBass</c> written in a phrase as dropped
/// while it prints it (2026-10-02; until then 21 of the repository's 25 twin warnings).
/// </summary>
[Trait("Category", "Unit")]
public class LilyPondExporterFiguredBassTests
{
    private static (string Ly, string[] Warnings) Export(string lys)
    {
        var tree = SyntaxTree.Parse(lys);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var exporter = new LilyPondExporter();
        string ly = exporter.Export(tree);
        return (ly, exporter.Warnings.ToArray());
    }

    private static string Book(string music) => $$"""
        octave absolute
        time 4/4
        part bass { clef bass }
        section A { bass { {{music}} } }
        form main { ~A }
        score main { staff bass }
        """;

    [Fact]
    public void FiguresAreAFiguredBassStream_UnderTheStaff()
    {
        var (ly, warnings) = Export(Book("c4 d@figuredBass(6) e@figuredBass(6 4) f | g1@figuredBass(7) |"));
        Assert.Empty(warnings);
        Assert.Matches(@"bassFigures = \\figuremode \{\s*s4 <6>4 <6 4>2 \|\s*<7>1 \|\s*\}", ly);
        Assert.Matches(@"\\new Staff[^\n]*\\bass[^\n]*\n\s*\\new FiguredBass \\bassFigures", ly);
    }

    [Theory]
    [InlineData("@figuredBass(6 s)", "<6+>")]
    [InlineData("@figuredBass(4 f)", "<4->")]
    [InlineData("@figuredBass(7 n)", "<7!>")]
    [InlineData("@figuredBass(#)", "<_+>")]
    public void AnAccidental_IsLilyPondsSuffix(string mark, string expected)
    {
        var (ly, _) = Export(Book($"c1{mark} |"));
        Assert.Contains(expected + "1", ly);
    }

    [Fact]
    public void AHeldFigure_IsWrittenBlank_AndSaysSo()
    {
        var (ly, warnings) = Export(Book("c2@figuredBass(6) d@figuredBass(_) |"));
        Assert.Contains("<6>2 <_>2", ly);
        Assert.Contains(warnings, w => w.Contains("held figure"));
    }

    /// <summary>A phrase's music is written by a nested exporter outside any part: its marks
    /// are still the ones the streams carry.</summary>
    [Fact]
    public void MarksInAPhrase_AreNotReportedDropped()
    {
        var (ly, warnings) = Export("""
            octave absolute
            time 4/4
            part bass { clef bass }
            phrase cadence { c4@chord(C)@figuredBass(5 3) g,@chord(G7)@figuredBass(7) c2@chord(C) | }
            section A { bass { cadence } }
            form main { ~A }
            score main { staff bass }
            """);
        Assert.Empty(warnings);
        Assert.Contains("<5 3>4 <7>2. |", ly);
        Assert.Contains("\\new ChordNames", ly);
    }
}
