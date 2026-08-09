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
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>combinedStaff { partA partB }</c> — two parts on one staff, MERGED where they agree.
/// </summary>
/// <remarks>
/// The music here is the music of <c>scratch/lpreg/pcombine-lp.ly</c>, four bars built to hit
/// each configuration once, and the expected counts are LilyPond's own, dumped grob by grob
/// from that file:
/// <code>
///   bar 1  unisono   4 note heads, 4 stems, all UP, "a2" on the first head
///   bar 2  solo1     2 heads — part two's whole-bar rest is NOT engraved — "Solo"
///   bar 3  solo2     2 heads — part one's is not engraved — "Solo II"
///   bar 4  chords    4 TWO-NOTE chords: 8 heads, 4 stems, no text
///   total            16 heads, 12 stems, 3 labels
/// </code>
/// ⚠️ The fourth bar is the one that surprises: LilyPond's chord-range is a NINTH, so two
/// parts a fourth to a seventh apart with the same rhythm come out as one voice of chords,
/// not as the two-voice picture <c>&lt;&lt; \\ &gt;&gt;</c> gives. The probe's own header
/// said otherwise until it was dumped.
/// </remarks>
[Trait("Category", "Unit")]
public class CombinedStaffTests
{
    private const string Defaults = "octave absolute\ntime 4/4\n";

    /// <summary>The four-bar probe, rendered by whatever score body is asked for.</summary>
    private static string FourBars(string render) => Defaults + """
        part fl1 { clef treble }
        part fl2 { clef treble }
        section A {
          fl1 { c4 d e f | g2 g | R1 | c4 e g e | }
          fl2 { c4 d e f | R1 | g,2 g, | g,4 g, g, g, | }
        }
        form main { ~A }
        """ + "\nscore main { " + render + " }\n";

    private static string Svg(string source) => SvgGenerator.Generate(
        SyntaxTree.Parse(source),
        new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });

    /// <summary>Everything the compiler says about this source — PARSE diagnostics as well
    /// as semantic ones, since the bad-member rule is the parser's.</summary>
    private static IReadOnlyList<Diagnostic> Diagnose(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var all = new List<Diagnostic>(tree.Diagnostics);
        foreach (var v in SemanticValidation.CreateAll())
        {
            v.Validate(tree);
            all.AddRange(v.Diagnostics);
        }
        return all;
    }

    /// <summary>Stem lines: the only 0.130-wide strokes on the page.</summary>
    private static List<(double Y1, double Y2)> Stems(string svg) =>
        Regex.Matches(svg, "<line x1=\"[-\\d.]+\" y1=\"([-\\d.]+)\" x2=\"[-\\d.]+\" y2=\"([-\\d.]+)\""
                           + " stroke=\"#000000\" stroke-width=\"0.130\"")
            .Select(m => (double.Parse(m.Groups[1].Value), double.Parse(m.Groups[2].Value)))
            .ToList();

    /// <summary>
    /// Note heads as (x, y), so a chord shows up as two heads sharing an x.
    /// </summary>
    /// <remarks>
    /// The clef and the time signature are music glyphs too, and they are the two leftmost
    /// on this page — the whole prefix, since none of these bars changes key or metre — so
    /// dropping the two smallest x values leaves the heads. Matching the head code points
    /// instead would tie the test to the bundled font's private-use assignments.
    /// </remarks>
    private static List<(double X, double Y)> Heads(string svg) =>
        Regex.Matches(svg, "<text class=\"music\"[^>]*x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"")
            .Select(m => (X: double.Parse(m.Groups[1].Value), Y: double.Parse(m.Groups[2].Value)))
            .OrderBy(h => h.X)
            .Skip(2)
            .ToList();

    private static List<string> Labels(string svg) =>
        Regex.Matches(svg, "<text (?![^>]*class=\"music\")[^>]*>([^<]+)</text>")
            .Select(m => m.Groups[1].Value)
            .Where(t => t is "a2" or "Solo" or "Solo II")
            .ToList();

    private static int StaffCount(string svg) =>
        Regex.Matches(svg, "<line x1=\"0.00\"[^>]*stroke-width=\"0.100\"").Count / 5;

    [Fact]
    public void TwoPartsShareOneStaff()
    {
        Assert.Equal(1, StaffCount(Svg(FourBars("combinedStaff { fl1 fl2 }"))));
    }

    [Fact]
    public void TheStemCountIsLilyPonds()
    {
        // 12, against the 20 the same music takes when the parts are merely condensed. This
        // one number is the point of the whole feature: a unison collapses to one stem, a
        // chord to one stem, and a solo drops the partner's bar entirely.
        Assert.Equal(12, Stems(Svg(FourBars("combinedStaff { fl1 fl2 }"))).Count);
        Assert.Equal(20, Stems(Svg(FourBars("condensedStaff { fl1 fl2 }"))).Count);
    }

    [Fact]
    public void EveryStemPointsUp()
    {
        // MEASURED: all twelve of LilyPond's stems are dir=1. The bars where only the LOWER
        // part plays are what makes this a test rather than a coincidence — a combiner that
        // left the surviving voice in "voice two" would point those down. LilyPond's shared
        // and solo contexts carry no voice settings, so the pitch rule decides, and every
        // pitch here is below the middle line.
        var stems = Stems(Svg(FourBars("combinedStaff { fl1 fl2 }")));
        Assert.All(stems, s => Assert.True(s.Y2 < s.Y1, "a stem points down"));
    }

    [Fact]
    public void TheNoteHeadsStandInLilyPondsColumns()
    {
        // Sixteen heads in twelve columns: eight columns of one (the unison bar and the two
        // solo bars) and four of two (the chord bar) — the shape of LilyPond's dump.
        var heads = Heads(Svg(FourBars("combinedStaff { fl1 fl2 }")));
        Assert.Equal(16, heads.Count);

        var columns = heads.GroupBy(h => h.X).OrderBy(g => g.Key).Select(g => g.Count()).ToList();
        Assert.Equal([1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2], columns);
    }

    [Fact]
    public void TheNoteHeadsSitAtLilyPondsStaffPositions()
    {
        // The staff positions LilyPond printed, in order, middle line = 0:
        //   bar 1  -6 -5 -4 -3           (c' d' e' f', one head each)
        //   bar 2  -2 -2                 (g')
        //   bar 3  -9 -9                 (g)
        //   bar 4  (-9,-6) (-9,-4) (-9,-2) (-9,-4)
        // This renderer draws position p at y = 11.69 - p/2 on this page, so the two are
        // compared through that one conversion rather than through a snapshot.
        int[] expected = [-6, -5, -4, -3, -2, -2, -9, -9, -9, -6, -9, -4, -9, -2, -9, -4];

        var positions = Heads(Svg(FourBars("combinedStaff { fl1 fl2 }")))
            .OrderBy(h => h.X).ThenByDescending(h => h.Y)
            .Select(h => (int)Math.Round((11.69 - h.Y) * 2))
            .ToArray();

        Assert.Equal(expected, positions);
    }

    [Fact]
    public void TheLabelsAreLilyPonds()
    {
        Assert.Equal(["a2", "Solo", "Solo II"], Labels(Svg(FourBars("combinedStaff { fl1 fl2 }"))));
    }

    [Fact]
    public void ACondensedStaffPrintsNoLabels()
    {
        // The control: the labels belong to combining, not to sharing a staff. LilyPond
        // prints nothing over plain simultaneous voices either.
        Assert.Empty(Labels(Svg(FourBars("condensedStaff { fl1 fl2 }"))));
    }

    [Fact]
    public void SourceOrderDecidesWhichPartIsWhich()
    {
        // Swapping them swaps "Solo" and "Solo II".
        Assert.Equal(["a2", "Solo II", "Solo"], Labels(Svg(FourBars("combinedStaff { fl2 fl1 }"))));
    }

    // ------------------------------------------------------------------ diagnostics

    [Fact]
    public void OnePartIsAnError()
    {
        var d = Diagnose(FourBars("combinedStaff { fl1 }"));
        Assert.Contains(d, x => x.Code == DiagnosticCodes.CombinedStaffNeedsTwoParts);
    }

    [Fact]
    public void ThreePartsAreAnError_AndTheMessagePointsAtTheContainerThatTakesThem()
    {
        var source = Defaults + """
            part fl1 { clef treble }
            part fl2 { clef treble }
            part fl3 { clef treble }
            section A {
              fl1 { c4 d e f | }
              fl2 { c4 d e f | }
              fl3 { c4 d e f | }
            }
            form main { ~A }
            score main { combinedStaff { fl1 fl2 fl3 } }
            """ + "\n";

        var d = Diagnose(source);
        var error = Assert.Single(d, x => x.Code == DiagnosticCodes.CombinedStaffNeedsTwoParts);
        Assert.Contains("condensedStaff", error.Message);
    }

    [Fact]
    public void AStaffInsideIsReportedAsItself_NotAsAMissingBrace()
    {
        // The first thing a grandStaff user tries. Reported by the PARSER, where the tokens
        // are, so the message names the mistake instead of "Expected 'CloseBrace'".
        var d = Diagnose(FourBars("combinedStaff { staff fl1 staff fl2 }"));
        Assert.Contains(d, x => x.Code == DiagnosticCodes.CombinedStaffBadMember);
    }

    [Fact]
    public void TheRestOfTheScoreStillRendersAfterABadMember()
    {
        // Error recovery: the offending item is skipped, not the block.
        var svg = Svg(FourBars("combinedStaff { staff fl1 staff fl2 }"));
        Assert.Contains("<line", svg);
    }
}
