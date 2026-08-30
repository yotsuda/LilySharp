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
using System.IO;
using System.Linq;
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A <c>rit.</c> standing above the TOP staff of a system has to be reserved for by the
/// spring that spaces that system against the one above it.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS. The spanner's ink was already priced in the SCALAR per-system extents
/// (<c>LayoutEngine.EnrichExtentsWithAnnotationProtrusions</c>), which the page BREAKER
/// reads when it decides how many systems fit. Nothing more. The gap BETWEEN two systems is
/// <c>LayoutUtilities.InterSystemPairMinimum</c>, and when skylines are present that reads
/// the X-aware <c>Distance()</c> ALONE — <c>nextUpExtent</c> is its fallback for a rows-only
/// lead sheet. So the spanner was drawn where nothing had reserved room, and on a page whose
/// springs sat on their floor the next system's <c>rit.</c> printed straight through the
/// previous system's lyrics (reader report, 2026-08-30, <c>Untitled-6.lys</c>: the A2
/// system's rit. over verse 2's "Like").
/// </para>
/// <para>
/// ⚠️⚠️ THE FIRST FIX WAS TOO BROAD AND A LEDGER POINT SAID SO. Merging the top staff's
/// WHOLE skyline into the system silhouette (<c>SkylineBuilder.BuildSystemSkylines</c>) also
/// clears this collision — and drove <c>page.inline-chord.gap-first</c> 2.500000 AWAY from
/// LilyPond, along with six snapshots and three placement tests. That point's own recorded
/// cause names the reason: a band that floors the distance under EVERY x, for ink that
/// exists at a FEW, is a third charge on something two other arms already price. The narrow
/// fix registers the spanner as X-aware boxes beside the dynamics arm that carries the same
/// pair, and splits the label's box from the dashed rule's so the label's height is charged
/// only where the label is.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class TextSpannerSystemSpacingTests
{
    /// <summary>
    /// The reported shape, as a tracked book: a title, a chord row, a tab and two verses
    /// packing three systems onto one page, with a <c>rit.</c> opening the LAST system.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE PAGE MUST STAY FULL FOR THIS TO TEST ANYTHING — see the fixture's note in
    /// <c>SvgSnapshotTests.TestSamples</c>. Four synthetic books of two to seven systems
    /// were tried first and every one of them placed the spanner identically before and
    /// after the fix, because a page with slack is justified and the floor never binds.
    /// </remarks>
    private const string Fixture = "test/rit-across-systems";

    private static RecordingDrawingContext RenderFirstPage(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var spec = RenderSpecParser.FindFirst(tree);
        var score = SvgGenerator.CollectScore(tree, spec);
        var layout = new LayoutEngine().Layout(score);
        using var doc = new RecordingDocumentContext();
        SharedRenderer.RenderTo(score, layout, doc);
        return doc.Page;
    }

    private static string FixtureSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "LilySharp.Tests")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        string path = Path.Combine(dir!.FullName, "LilySharp.Tests", "Fixtures",
            Fixture.Replace('/', Path.DirectorySeparatorChar) + ".lys");
        Assert.True(File.Exists(path), $"fixture missing: {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void ARitOnAnInteriorSystemsTopStaff_ClearsTheLyricsOfTheSystemAbove()
    {
        var page = RenderFirstPage(FixtureSource());

        // Both systems that open with `rit.` draw one; the LOWER of the two is the interior
        // system's, and it is the one whose spring has a system above it to clear.
        var rits = page.Texts.Where(t => t.Text == "rit.").OrderBy(t => t.Y).ToList();
        Assert.Equal(2, rits.Count);
        var rit = rits[^1];

        // The verse-2 syllable directly above it. Verse 2 is the LOWER of the two lyric
        // rows, so its baseline is the deepest ink the system above puts over this spanner.
        var above = page.Texts
            .Where(t => t.Y < rit.Y && t.Text is "fif-" or "fif" or "six-" or "teen")
            .OrderByDescending(t => t.Y)
            .ToList();
        Assert.NotEmpty(above);
        var syllable = above[0];

        // Ink about each baseline, from the faces the renderer draws with — no constant of
        // this test's own, so a font or size change moves both sides together.
        // ⚠️ THE ROLE COMES OFF THE RECORDED DRAW, not from a guess here: the renderer
        // chose it, so a family that changes role stays measured against the face it is
        // actually set in.
        var fonts = ScoreTextMetrics.Bundled;
        var ritInk = fonts.Ink(rit.Text, rit.FontSize, rit.Role, FontStyle.Italic);
        var lyrInk = fonts.Ink(syllable.Text, syllable.FontSize, syllable.Role, FontStyle.Regular);

        double ritInkTop = rit.Y - ritInk.Top;
        double lyricInkBottom = syllable.Y - lyrInk.Bottom;   // Bottom is negative (Y-up)

        // POSITIVE CONTROL — the regime. If the two are not even in the same X band the
        // claim below is free, and this book has stopped posing the question.
        double ritRight = rit.X + fonts.Advance(rit.Text, rit.FontSize, rit.Role, FontStyle.Italic);
        double lyrRight = syllable.X
            + fonts.Advance(syllable.Text, syllable.FontSize, syllable.Role, FontStyle.Regular);
        Assert.True(rit.X < lyrRight && syllable.X < ritRight,
            $"the rit. (x {rit.X:F3}..{ritRight:F3}) and the syllable "
            + $"(x {syllable.X:F3}..{lyrRight:F3}) do not overlap in X — this book no longer "
            + "poses the question the fix answered.");

        // THE CLAIM. Before the fix this read 56.43 against a lyric bottom of 57.08.
        Assert.True(ritInkTop > lyricInkBottom,
            $"the interior system's rit. is drawn through the lyrics above it: "
            + $"rit ink top {ritInkTop:F6}, lyric ink bottom {lyricInkBottom:F6}.");
    }
}
