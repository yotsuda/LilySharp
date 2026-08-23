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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// An independent lyrics ROW standing below TWO staves clears the staff above it, on
/// EVERY system — measured on the drawn SVG, which is the frame the reader complains in.
/// </summary>
/// <remarks>
/// ⚠️ THIS SHAPE HAD NO OBSERVER AT ALL. Of the 575 <c>score</c> blocks in the tracked
/// corpus, 36 have two or more staves and NOT ONE of those also carries a <c>lyrics</c>
/// row — so 5807 tests, 222 snapshots and 566 ledger points were all green while the row
/// was engraved THROUGH the lower staff's noteheads. The census is the point: a shape the
/// corpus never spells is a shape every instrument agrees about for free.
/// <para>
/// WHAT IT CAUGHT: <c>LayoutEngine.LayoutLyrics</c> wired the per-staff down-skyline only
/// when some lyric line was note-bound (<c>lyrics.Any(l =&gt; !l.IsLyricsRow)</c>), which is
/// exactly false for a row-only score. <c>LyricEngraver.ResolveAnchor</c> then fell back to
/// the system silhouette and paid the <c>skylineToAnchor</c> frame step out of the chain's
/// FIRST gap, taking that gap's minimum NEGATIVE (−2.994200 here) — so
/// <c>nonstaff-relatedstaff-spacing</c> stopped binding and the row was drawn wherever the
/// spring solve left it. MEASURED: verse 1's baseline sat 0.550000 ABOVE the lower staff's
/// lowest notehead on the first system and 2.480000 below it on the last, because the last
/// system's chain runs to the page edge where the slack hides the missing floor.
/// </para>
/// <para>
/// ⚠️ IT IS THE PER-SYSTEM SWEEP THAT MATTERS, not one number: the defect was invisible on
/// the LAST system of every score it touched. A test that rendered one system, or read only
/// the first lyric baseline it found, would have been green on the broken build.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class LyricRowUnderMultipleStavesTests
{
    // Two staves and a lyrics ROW: the row sings the UPPER staff while standing below the
    // LOWER one, which is the arrangement `score { staff a  staff b  lyrics v }` builds
    // whenever the row does not sit directly under the staff it sings. Ten bars so the
    // book breaks into more than one system — the defect only ever showed on a system that
    // has another one after it.
    // ⚠️ THE REPEATED SECTION IS LOAD-BEARING, and the first draft of this test did not have
    // it: with ONE verse the chain is short enough that the solve lands the row on the
    // spring's NATURAL length (5.500000 below the anchor's reference point), which happens
    // to clear these noteheads — so the broken build passed. It is the SECOND verse that
    // spends the room and lets the missing floor show. A fixture that cannot go red is not
    // an observer, and this one was verified both ways (HANDOFF 5.2.1).
    private const string Source =
        "title \"row under two staves\"\n" +
        "time 4/4\n" +
        "key c major\n" +
        "part melody {\n" +
        "  clef treble\n" +
        "  section A { c4 c g' g | a a g2 | f4 f e e | d d c2 | }\n" +
        "  section B { g'4 g f f | e e d2 | }\n" +
        "}\n" +
        // Whole notes that hang BELOW their own staff: the ink the row has to clear is
        // under the bottom line, not on it, which is what makes the clearance measurable.
        "part lower {\n" +
        "  section A { c1 | b1 | a1 | d | }\n" +
        "  section B { c1 | d | }\n" +
        "}\n" +
        "lyrics verse sings melody {\n" +
        "  section A { Twin- kle twin- kle | lit- tle star | How I won- der | what you are | }\n" +
        "  section B {\n" +
        "    [~1. Up a- bove the | world so high |]\n" +
        "    [~2. Like a dia- mond | in the sky |]\n" +
        "  }\n" +
        "}\n" +
        "form main { A |: B :| A \"A2\" }\n" +
        "score main {\n  staff melody\n  staff lower\n  lyrics verse\n}\n";

    [Fact]
    public void RowBelowTwoStaves_ClearsTheLowerStaffsNotes_OnEverySystem()
    {
        var (staves, glyphs, lyrics) = Render(Source);

        Assert.True(staves.Count >= 4,
            $"the fixture must break into at least two systems; got {staves.Count / 2} staves-pairs");
        Assert.NotEmpty(lyrics);

        var checkedSystems = 0;
        foreach (var baseline in lyrics)
        {
            // The staff this syllable hangs under: the lowest one whose bottom line is
            // above the baseline.
            var above = staves.Where(s => s.Bottom < baseline)
                              .OrderByDescending(s => s.Bottom).FirstOrDefault();
            Assert.True(above.Bottom > 0, $"no staff above a lyric baseline at {baseline:F2}");

            // That staff's ink BELOW its own bottom line — the noteheads and their ledger
            // lines. Bounded above by the baseline so a following system's staff cannot be
            // read as this one's.
            var underStaff = glyphs.Where(g => g > above.Bottom && g < baseline).ToList();
            if (underStaff.Count == 0) continue;
            checkedSystems++;

            double lowestInk = underStaff.Max();
            // 1.0 ss of margin: a notehead's ink reaches about half a staff space below
            // its own baseline, so a syllable baseline within 1.0 of it is already touching.
            // The defect put the baseline 0.55 ABOVE this number; a correct build clears it
            // by 2.84 here, so nothing about the margin is load-bearing.
            Assert.True(baseline > lowestInk + 1.0,
                $"lyric baseline {baseline:F2} does not clear the staff ink at {lowestInk:F2} "
                + $"(staff lines {above.Top:F2}..{above.Bottom:F2})");
        }

        Assert.True(checkedSystems >= 2,
            $"the sweep must reach more than one system; it checked {checkedSystems}");
    }

    // The same book with the lower staff's notes put HIGH instead of low, so its ledger
    // lines rise and the page pushes that staff far down the system. The missing floor is
    // measured from the anchor staff, so the further that staff travels the further the row
    // is left behind: here it was drawn INSIDE the staff's own line span rather than merely
    // over its noteheads. Two fixtures because the defect has two visible ends, and the
    // milder one alone would let a partial repair look complete.
    private static readonly string SourceHighLowerStaff =
        Source.Replace(
            "  section A { c1 | b1 | a1 | d | }\n  section B { c1 | d | }\n",
            "  section A { c''1 | b'1 | a'1 | d'' | }\n  section B { c''1 | d'' | }\n");

    [Fact]
    public void RowBelowTwoStaves_NeverSitsInsideAStaff()
    {
        var (staves, _, lyrics) = Render(SourceHighLowerStaff);

        foreach (var baseline in lyrics)
            foreach (var st in staves)
                Assert.False(baseline > st.Top - 0.5 && baseline < st.Bottom + 0.5,
                    $"lyric baseline {baseline:F2} is inside the staff "
                    + $"{st.Top:F2}..{st.Bottom:F2}");
    }

    /// <summary>
    /// The drawn page, reduced to the three things this asks about: each staff's top and
    /// bottom line, every music glyph's baseline, and every lyric syllable's baseline.
    /// </summary>
    private static (List<(double Top, double Bottom)> Staves, List<double> Glyphs, List<double> Lyrics)
        Render(string src)
    {
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors,
            string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        string svg = LilySharp.Core.Svg.SvgGenerator.Generate(
            tree, new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });

        // Staff lines: long horizontals. A ledger line is short, so the width gate keeps
        // them out and the five-in-a-row grouping below would reject them anyway.
        var lineYs = Regex.Matches(svg, "<line x1=\"([-\\d.]+)\" y1=\"([-\\d.]+)\" x2=\"([-\\d.]+)\" y2=\"([-\\d.]+)\"")
            .Where(m => m.Groups[2].Value == m.Groups[4].Value
                        && double.Parse(m.Groups[3].Value) - double.Parse(m.Groups[1].Value) > 20)
            .Select(m => double.Parse(m.Groups[2].Value))
            .Distinct().OrderBy(v => v).ToList();

        var staves = new List<(double Top, double Bottom)>();
        for (int i = 0; i + 4 < lineYs.Count; )
        {
            bool five = true;
            for (int k = 1; k < 5; k++)
                if (Math.Abs(lineYs[i + k] - lineYs[i + k - 1] - 1.0) > 0.02) { five = false; break; }
            if (five) { staves.Add((lineYs[i], lineYs[i + 4])); i += 5; }
            else i++;
        }

        var glyphs = Regex.Matches(svg, "<text class=\"music\"[^>]*? y=\"([-\\d.]+)\"")
            .Select(m => double.Parse(m.Groups[1].Value)).ToList();

        // A syllable is a non-music text whose content is one of the words this fixture
        // sings — named rather than inferred from a font size, so a font change in the
        // options cannot quietly empty this list and make the sweep vacuous.
        var words = new HashSet<string>(StringComparer.Ordinal)
        {
            "Twin", "kle", "twin", "lit", "tle", "star", "How", "I", "won", "der",
            "what", "you", "are", "Up", "a", "bove", "the", "world", "so", "high",
            "Like", "dia", "mond", "in", "sky",
        };
        var lyrics = Regex.Matches(svg, "<text(?![^>]*class=\"music\")[^>]*?y=\"([-\\d.]+)\"[^>]*>([^<]*)</text>")
            .Where(m => words.Contains(m.Groups[2].Value))
            .Select(m => double.Parse(m.Groups[1].Value))
            .Distinct().OrderBy(v => v).ToList();

        return (staves, glyphs, lyrics);
    }
}
