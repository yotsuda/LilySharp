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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Two voices' heads that merge keep one voice's dot: on a line the two would otherwise stand on
/// either side of it. Expected rows are LilyPond 2.26.0's (Lab sessions/p1094/dd).
/// LILYPOND-REF: lily/note-collision.cc:262-314 check_meshing_chords — dot_wipe_head.
/// </summary>
[Trait("Category", "Unit")]
public class MergedDotTests
{
    private static string Book(string voices) => $$"""
        octave absolute
        time 4/4
        part lh { clef bass }
        section A { lh { {{voices}} | } }
        form { ~A }
        score { staff lh }
        """;

    /// <summary>The rows the dots stand on, in spaces above the middle line, sorted.</summary>
    private static double[] DotRows(string voices)
    {
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(Book(voices)));
        double Y(string s) => double.Parse(s, CultureInfo.InvariantCulture);
        double middle = Regex.Matches(svg, "<line [^>]*y1=\"([\\d.]+)\"[^>]*y2=\"\\1\"")
            .Select(m => Y(m.Groups[1].Value)).Distinct().OrderBy(y => y).ElementAt(2);
        return Regex.Matches(svg, "<text class=\"music\" x=\"[-\\d.]+\" y=\"([-\\d.]+)\"[^>]*>\uE038</text>")
            .Select(m => middle - Y(m.Groups[1].Value)).OrderBy(r => r).ToArray();
    }

    private static void AssertRows(string voices, params double[] rows)
    {
        var got = DotRows(voices);
        Assert.Equal(rows.Length, got.Length);
        for (int i = 0; i < rows.Length; i++)
            Assert.Equal(rows[i], got[i], 1);
    }

    [Fact]
    public void AMergedNoteOnALineKeepsTheUpVoicesDot() =>
        AssertRows("voice { d4. r8 r2 } { d4. r8 r2 }", 0.5);

    [Fact]
    public void AMergedNoteInASpaceKeepsOneDot() =>
        AssertRows("voice { e4. r8 r2 } { e4. r8 r2 }", 0.5);

    [Fact]
    public void AMergedChordLosesOnlyTheDotOfItsFirstHead() =>
        // The down chord's stem starts from f: its dot goes, and d's stays below the line.
        AssertRows("voice { <d f>4. r8 r2 } { <d f>4. r8 r2 }", -0.5, 0.5, 1.5);

    [Fact]
    public void NotesThatDoNotMergeKeepBothDots() =>
        AssertRows("voice { f4. r8 r2 } { d4. r8 r2 }", -0.5, 1.5);

    [Fact]
    public void AnUndottedMergeHasNoDotToLose() =>
        Assert.Empty(DotRows("voice { d4 r4 r2 } { d4 r4 r2 }"));
}
