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
}
