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

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A bar that OPENS with a lower voice's note <c>@cross</c>ed up: that voice files no wish
/// toward the bar line before it (its last NoteSpacing is filed under the staff it left), so
/// the closing spring is the other voices' alone — and a lower staff whose notes are ALL moved
/// up is silent, its bar-line wish carrying no stem correction.
/// LILYPOND-REF: lily/note-spacing-engraver.cc:109-128 Note_spacing_engraver::stop_translation_timestep;
/// LILYPOND-REF: lily/spacing-spanner.cc:350-364 musical_column_spacing (found_matching_column).
/// </summary>
/// <remarks>
/// MEASURED, LilyPond 2.26.0 (Lab sessions/p1373/wish — w*, x* in probe.ps1): the distance from
/// the leftmost head of bar 2's last column to bar 3's first, on the upper staff. Until session
/// 1374 Lily# gave every voice its wish (4.69, 4.55, 4.51 over c, g, a rest; 4.24 under one
/// voice) and counted the moved notes on their home staff (4.82 where all are moved).
/// </remarks>
[Trait("Category", "Unit")]
public class CrossStaffBarlineWishTests
{
    private const string Two = "voice { e''4 e'' e'' e'' | e''4 e'' e'' e'' | e''4 e'' e'' e'' | } { d''4 d'' d'' d'' | d''4 d'' d'' d'' | d''4 d'' d'' d'' | }";
    private const string One = "e''4 e'' e'' e'' | e''4 e'' e'' e'' | e''4 e'' e'' e'' |";

    [Theory]
    // Two upper voices: the moved first note drops the lower voice's wish, whatever it ended on.
    [InlineData(Two, "c4 c c c | c4 c c c | a'4@cross c c c |", 4.815)]
    [InlineData(Two, "c4 c c c | c4 c c r | a'4@cross c c c |", 4.815)]
    [InlineData(Two, "c4 c c c | c4 c c g | a'4@cross c c c |", 4.816)]
    // …all of bar 3 moved: the lower staff is silent too (its share of the correction gone).
    [InlineData(Two, "c4 c c c | c4 c c c | a'4@cross a'@cross a'@cross a'@cross |", 4.735)]
    // Controls: nothing moved, or moved only from beat 2.
    [InlineData(Two, "c4 c c c | c4 c c c | c4 c c c |", 4.685)]
    [InlineData(Two, "c4 c c c | c4 c c r | c4 c c c |", 4.510)]
    [InlineData(Two, "c4 c c c | c4 c c c | c4 a'@cross a'@cross a'@cross |", 4.685)]
    // One down-stem upper voice: its own distance, narrower than with the lower voice's wish.
    [InlineData(One, "c4 c c c | c4 c c c | a'4@cross c c c |", 4.045)]
    [InlineData(One, "c4 c c c | c4 c c c | e'4@cross c c c |", 4.045)]
    [InlineData(One, "c4 c c c | c4 c c c | c4 c c c |", 4.248)]
    public void ABarOpeningWithAMovedNote_TheMovedVoiceFilesNoWishToTheBarLine(string upper, string lower, double crossing)
    {
        string src = "time 4/4\noctave absolute\npaper { raggedRight }\npart rh { }\npart lh { clef bass }"
            + "\nsection A {\n  rh { " + upper + " }\n  lh { " + lower
            + " }\n}\nform { A }\nscore { grandStaff { staff rh staff lh } }\n";
        var heads = Regex.Matches(TwinBowSweep.Dump(src), @"^HEAD \S+ 0 x=([-\d.]+)", RegexOptions.Multiline)
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .OrderBy(x => x).ToList();
        // The columns' leftmost heads (the heads are sorted): a head shifted off a second
        // (1.30) joins its column, the columns themselves stand 3 or more apart.
        var columns = new List<double>();
        foreach (double x in heads)
            if (columns.Count == 0 || x - columns[^1] > 2.0)
                columns.Add(x);
        Assert.Equal(12, columns.Count);
        // 0.006: the figures are LilyPond's to three places; Lily#'s rows read 4.82, 4.82,
        // 4.82, 4.74, 4.69, 4.51, 4.69, 4.04, 4.04, 4.24.
        Assert.Equal(crossing, columns[8] - columns[7], 0.006);
    }
}
