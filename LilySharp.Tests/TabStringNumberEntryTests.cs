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
using System.Text.RegularExpressions;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// tablature.ly: string numbers enter as note articulations (inside the chord,
/// <c>&lt;e\5 dis'\4&gt;</c>) and as chord articulations (outside,
/// <c>&lt;e dis'&gt;\5\4</c>), pairing with the members in order — so all the
/// forced spellings fret identically. LP 2.26.0 oracle (tabnum twin): e → string
/// 5 fret 7, dis' → string 4 fret 13 in every forced form. Until 2026-08-09 BOTH
/// forms were silently ignored: Pitch/ChordSyntax.Articulations' type filter
/// swallowed StringNumberAnnotationSyntax, and the chord-level pairing did not
/// exist (the chord fretted as if unforced).
/// LILYPOND-REF: lily/articulations.cc:38-80 articulation_list.
/// </summary>
[Trait("Category", "Unit")]
public class TabStringNumberEntryTests
{
    private const string BookTwin = """
        octave absolute
        part m { clef treble_8 tuning guitar }
        section A {
          m {
            <e\5 dis'\4>4 <e dis'>4\5\4 <e dis'\4>4\5 r4 |
          }
        }
        form main { A }
        score main { tab m as numbers }
        """;

    [Fact]
    public void MemberAndChordLevelStringNumbers_FretIdentically()
    {
        var svg = LiveRender.SvgFromRenderSpec(BookTwin);

        // Fret digits with their string rows (row = the digit's string line).
        var digits = new List<(double X, double Y, string Text)>();
        foreach (Match m in Regex.Matches(svg,
            "<text x=\"([-\\d.]+)\" y=\"([-\\d.]+)\" font-size=\"3.00\" font-weight=\"bold\" text-anchor=\"middle\"[^>]*>(\\d+)</text>"))
            digits.Add((
                double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                m.Groups[3].Value));

        // Three chords, two digits each: "13" (dis' on string 4) above "7"
        // (e on string 5) — the LP frets for every forced entry form.
        Assert.Equal(3, digits.Count(d => d.Text == "13"));
        Assert.Equal(3, digits.Count(d => d.Text == "7"));
        Assert.Equal(6, digits.Count);

        // The 13s share one row (string 4) and sit above the 7s' row (string 5,
        // one tab space = 1.5 lower).
        double row13 = digits.Where(d => d.Text == "13").Select(d => d.Y).Distinct().Single();
        double row7 = digits.Where(d => d.Text == "7").Select(d => d.Y).Distinct().Single();
        Assert.Equal(1.5, row7 - row13, 2);
    }

    /// <summary>
    /// A string number may follow the note's slur, tie and beam marks — <c>g')\2</c> is
    /// the same event as <c>g'\2)</c>, because LilyPond's post-events are an unordered
    /// list. Both spellings must fret the note on the string that was written.
    /// LILYPOND-REF: lily/parser.yy post_events.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE ONE WRITTEN AFTER THE PAREN USED TO BE DROPPED IN SILENCE, and on a tab
    /// staff that is the worst place for silence: the page still prints a fret, because
    /// the automatic chooser answers instead, and its answer is a real fret on a real
    /// string. In the book this came from (a bass, `c( g')\2`), the reader saw an OPEN
    /// FIRST STRING where LilyPond prints the fifth fret of the second — measured on
    /// 2.26.0 through the twin of that book, 2026-08-16.
    /// <para>The two spellings are compared to each other rather than to one pinned
    /// row: what the fix is about is that the ORDER stops mattering. The fret text is
    /// pinned as well, since an ignored string number changes it (5 → 0) and that is
    /// what a reader would see.</para>
    /// </remarks>
    [Theory]
    [InlineData("c8 c c c( g'\\2) g g4")]   // string number BEFORE the slur close
    [InlineData("c8 c c c( g')\\2 g g4")]   // …and after it — the reported spelling
    public void AStringNumberAfterASlurClose_StillPlacesTheNote(string music)
    {
        var svg = LiveRender.SvgFromRenderSpec($$"""
            part melody {
              instrument bass
              section A { {{music}} }
            }
            form main { A }
            score main { tab melody }
            """);

        var digits = new List<(double X, double Y, string Text)>();
        foreach (Match m in Regex.Matches(svg,
            "<text x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*>(\\d+)</text>"))
            digits.Add((
                double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                m.Groups[3].Value));

        Assert.Equal(7, digits.Count);
        var ordered = digits.OrderBy(d => d.X).ToList();
        var g = ordered[4];                                 // the note that carries \2
        Assert.Equal("5", g.Text);                          // 5th fret, not the open string
        // …one tab space ABOVE the C's row: those are on the A string (3), this is on
        // the D string (2), which is what \2 asked for.
        double cRow = ordered.Take(4).Select(d => d.Y).Distinct().Single();
        Assert.Equal(1.5, cRow - g.Y, 2);
    }

    /// <summary>
    /// A <c>\N</c> that belongs to no note is refused rather than dropped: on a tab staff
    /// a dropped string number is invisible, because the chooser prints a fret anyway.
    /// </summary>
    /// <remarks>
    /// ⚠️ It has to be written where NO note precedes it. Whitespace does not end a note's
    /// post-events — <c>c8 \2</c> is the string number of that <c>c8</c>, exactly as in
    /// LilyPond — so only a leading one is a stray.
    /// </remarks>
    [Fact]
    public void AStringNumberOnItsOwn_IsReported()
    {
        var tree = SyntaxTree.Parse("""
            part melody {
              instrument bass
              section A { \2 c8 c c c }
            }
            form main { A }
            score main { tab melody }
            """);

        var error = Assert.Single(tree.Diagnostics,
            d => d.Code == DiagnosticCodes.StrayStringNumber);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("belongs to a note", error.Message);
    }
}
