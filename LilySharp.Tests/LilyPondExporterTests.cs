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

using Xunit;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Syntax;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class LilyPondExporterTests
{
    private static string Export(string lys) =>
        new LilyPondExporter().Export(SyntaxTree.Parse(lys));

    // A part-major score with one bass section, the shape the corpus uses.
    private static string Score(string music, string headers = "octave absolute",
        string render = "staff bassline") => $$"""
        {{headers}}
        part bassline {
          clef bass
          tuning bass
          section S { {{music}} }
        }
        form main { ~S }
        score main { {{render}} }
        """;

    [Fact]
    public void AbsoluteOctave_WrapsInFixed_AndCopiesMarksVerbatim()
    {
        var ly = Export(Score("a,4 e,8 gis,8"));
        Assert.Contains("\\fixed c' {", ly);
        // The written octave marks survive untouched.
        Assert.Contains("a,4", ly);
        Assert.Contains("e,8", ly);
        Assert.Contains("gis,8", ly);
        Assert.DoesNotContain("\\relative", ly);
    }

    [Fact]
    public void RelativeIsTheDefault_WrapsInRelative()
    {
        // No `octave absolute` directive -> Lily#'s default relative mode.
        var ly = Export(Score("c d e", headers: ""));
        Assert.Contains("\\relative c' {", ly);
        Assert.DoesNotContain("\\fixed", ly);
    }

    [Fact]
    public void Header_EmitsTitleAndComposer()
    {
        var ly = Export(Score("c4", headers: "octave absolute\ntitle \"Song\"\ncomposer \"Writer\""));
        Assert.Contains("\\header {", ly);
        Assert.Contains("title = \"Song\"", ly);
        Assert.Contains("composer = \"Writer\"", ly);
    }

    [Fact]
    public void KeyTimeTempoClef_MapToBackslashForms()
    {
        var ly = Export(Score("c4",
            headers: "octave absolute\ntempo 120\nkey g major\ntime 3/4"));
        Assert.Contains("\\tempo 4 = 120", ly);
        Assert.Contains("\\key g \\major", ly);
        Assert.Contains("\\time 3/4", ly);
        Assert.Contains("\\clef bass", ly);
    }

    [Fact]
    public void StringNumbers_ArePreserved()
    {
        var ly = Export(Score("a,4\\2 e,8\\3"));
        Assert.Contains("a,4\\2", ly);
        Assert.Contains("e,8\\3", ly);
    }

    [Fact]
    public void InlineRepeat_BecomesRepeatVolta()
    {
        var ly = Export(Score("|: c,4 d,4 :|"));
        Assert.Contains("\\repeat volta 2 {", ly);
        Assert.DoesNotContain("|:", ly);
    }

    [Fact]
    public void InlineRepeatWithEndings_BecomesAlternative()
    {
        var ly = Export(Score("|: c,4 [1. d,4 ] :| [2. e,4 ]"));
        Assert.Contains("\\repeat volta 2 {", ly);
        Assert.Contains("\\alternative {", ly);
    }

    [Fact]
    public void RepeatPercent_PassesThrough()
    {
        var ly = Export(Score("repeat percent 4 { c,4 d,4 }"));
        Assert.Contains("\\repeat percent 4 {", ly);
    }

    [Fact]
    public void Mark_BecomesBoxedRehearsalMark()
    {
        var ly = Export(Score("c,4@mark(\"Intro\") d,4"));
        Assert.Contains("\\mark \\markup { \\box Intro }", ly);
    }

    [Fact]
    public void Tuplet_MapsToBackslashTuplet()
    {
        var ly = Export(Score("tuplet 3/2 { c,8 d,8 e,8 }"));
        Assert.Contains("\\tuplet 3/2 {", ly);
    }

    [Fact]
    public void Score_EmitsStaffAndTabWithBassTuning()
    {
        var ly = Export(Score("c,4", render: "staff bassline\n  tab bassline"));
        Assert.Contains("\\new Staff { \\clef bass", ly);
        Assert.Contains("\\new TabStaff", ly);
        Assert.Contains("stringTunings = #bass-four-string-tuning", ly);
    }

    [Fact]
    public void Ties_AndBreaks_ArePreserved()
    {
        var ly = Export(Score("c,4~ c,4 break d,4"));
        Assert.Contains("~", ly);
        Assert.Contains("\\break", ly);
    }

    [Fact]
    public void EmitsVersionHeader()
    {
        var ly = Export(Score("c,4"));
        Assert.StartsWith("\\version", ly);
    }
}
