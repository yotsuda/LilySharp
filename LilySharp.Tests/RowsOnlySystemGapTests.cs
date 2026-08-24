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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// What stands ABOVE a system — its section label, its bar number — must clear the system
/// above it, on a score with NO STAFF as much as on one with a staff.
/// </summary>
/// <remarks>
/// <para>
/// A rows-only lead sheet (chords row + lyrics row, no staff) had no down silhouette to be
/// spaced by: <c>SkylineBuilder.BuildSystemSkylines</c> seeds the down side from the bottom
/// staff's INK, and such a system has none — its content is text the lyric and chord
/// engravers draw. The lyric reservation does not cover it either, because that profile is
/// the rows hanging BELOW the last spaceable staff and here there is no such staff. So
/// <c>Distance()</c> answered 6.395 on the reported book where the true need was 14.900,
/// the 12.000 basic distance won, and the second system's "A2" and its bar number printed
/// 1.8 into the system above (user report, session 240).
/// </para>
/// <para>
/// ⚠️ The same defect was in a SHIPPED SAMPLE — <c>samples/drunken-sailor.lys</c>, whose
/// continuation bar number "5" sat at 27.25 with its ink reaching to about 25.35, inside
/// the first system's band of 19.42‥26.62. It is the one book of the 572 tracked whose
/// picture this fix moves, and it moved because it was wrong.
/// </para>
/// <para>
/// ⚠️ THE OTHER HALF OF THE SAME HOLE — a system that DOES have a staff but whose bottom
/// element is a lyrics row — was reported one session later and is guarded by
/// <see cref="TrailingLyricsRowBandTests"/>. See the comment on the theory below for why it
/// could not be an arm of this one.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class RowsOnlySystemGapTests
{
    private const string Head = """
        title "T"
        time 4/4
        part melody {
          clef treble
          section A { c4 c g' g | a a g2 | f4 f e e | d d c2 | }
          section B { g'4 g f f | e e d2 | }
        }
        chords prog {
          section A { C | F | G | C }
          section B { Am | G }
        }
        lyrics verse {
          section A { one two | three four | five six | sev- en | }
          section B { [~1. eight nine | ten e- ]
            [~2. le- ven | twelve thir- ] }
        }
        form main { A |: B :| A "A2" }
        """;

    private static string Render(string scoreBody)
    {
        var tree = SyntaxTree.Parse($"{Head}\nscore main {{\n{scoreBody}\n}}\n");
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        return SvgGenerator.Generate(tree, new SvgRenderOptions { EmbedFont = false });
    }

    /// <summary>Each system's band: the vertical span of its barlines (a narrow, tall rect),
    /// top-to-bottom in page order.</summary>
    private static List<(double Top, double Bottom)> Bands(string svg)
        => Regex.Matches(svg,
                "<rect[^>]*x=\"([0-9.-]+)\"[^>]*y=\"([0-9.-]+)\"[^>]*width=\"([0-9.-]+)\"[^>]*height=\"([0-9.-]+)\"")
            .Select(m => (Y: double.Parse(m.Groups[2].Value),
                          W: double.Parse(m.Groups[3].Value),
                          H: double.Parse(m.Groups[4].Value)))
            .Where(r => r.W < 1 && r.H > 2)
            .Select(r => (Top: r.Y, Bottom: r.Y + r.H))
            .Distinct().OrderBy(b => b.Top).ToList();

    private static double? BaselineOf(string svg, string text)
    {
        var m = Regex.Match(svg, $"<text[^>]*y=\"([0-9.-]+)\"[^>]*>{Regex.Escape(text)}</text>");
        return m.Success ? double.Parse(m.Groups[1].Value) : null;
    }

    // The cap height a boxed mark and a bar number rise above their baseline. Deliberately
    // a floor rather than the exact ink: the assertion is "it clears", and a value that
    // under-states the ascent can only make this test WEAKER, never wrong.
    private const double MarkAscent = 1.9;

    // ⚠️ THE FIXTURE'S SECOND VERSE IS LOAD-BEARING. The first version of this file wrote
    // section B with ONE verse, which makes the row band four staff spaces tall — the same
    // height a plain staff has — and at that height nothing overlaps even with the fix
    // reverted: the test could not fail. The reported book has two `[~N. …]` verses, so its
    // band is 7.2, and it is that extra depth the under-measured silhouette lost. Verified
    // by reverting the guard: with one verse the theory stayed green, with two it goes red.

    // ⚠️⚠️ ★★★ A SECOND ARM STOOD HERE AND ASSERTED NOTHING (removed session 243). It read
    // `staff melody / chords prog as names / lyrics verse sings melody` and its own comment
    // called it "the control that was always right" — while that exact arrangement was the
    // NEXT user report, and this file stayed green all the way through it. The INSTRUMENT is
    // why: `Bands()` measures BARLINES, and on a score WITH a staff the barlines span the
    // staff only, never the chord and lyric rows hanging under it. `firstBottom` was
    // therefore the staff's bottom, some six staff spaces above the deepest syllable, and
    // every label below it cleared trivially. The arm could not go red.
    // ⇒ ★★ A CONTROL THAT CANNOT GO RED IS NOT A CONTROL — it is a claim wearing a test's
    // clothes, and this one bought a session of false confidence.
    // ⚠️ AND IT CANNOT BE REPAIRED IN PLACE. On a staff score the label and the deepest
    // syllable are X-DISJOINT — the mark leads system 2 while the last verse ends system 1 —
    // so they legitimately share a Y, and a vertical-only assertion is WRONG there rather
    // than merely weak (measured: it goes red on correct output). That arrangement is
    // guarded by TrailingLyricsRowBandTests instead, whose instrument is X-free for a
    // different reason: a staff LINE spans every X, so "a syllable inside a staff's line
    // span" is an overlap without needing an X model at all.
    [Theory]
    // The reported shape: no staff at all.
    [InlineData("  chords prog as names\n  lyrics verse sings melody")]
    public void WhatStandsAboveTheSecondSystem_ClearsTheFirst(string scoreBody)
    {
        var svg = Render(scoreBody);
        var bands = Bands(svg);
        Assert.True(bands.Count >= 2, $"expected two systems, found {bands.Count}");

        double firstBottom = bands[0].Bottom;
        foreach (var label in new[] { "A2", "6" })
        {
            double? baseline = BaselineOf(svg, label);
            Assert.True(baseline.HasValue, $"'{label}' is not drawn");
            Assert.True(baseline!.Value - MarkAscent > firstBottom,
                $"'{label}' ink top {baseline.Value - MarkAscent:F2} is inside the first "
                + $"system, which ends at {firstBottom:F2}");
        }
    }

    /// <summary>
    /// …and the fix did not simply push every system apart: a rows-only score with nothing
    /// standing above its second system keeps a gap decided by its own content.
    /// </summary>
    [Fact]
    public void ARowsOnlyScoreIsStillSpacedByItsOwnContent()
    {
        var withLabel = Bands(Render("  chords prog as names\n  lyrics verse sings melody"));
        // The same book with the reprise silent, so no label stands above system 2.
        var tree = SyntaxTree.Parse(
            Head.Replace("form main { A |: B :| A \"A2\" }", "form main { A |: B :| ~A }")
            + "\nscore main {\n  chords prog as names\n  lyrics verse sings melody\n}\n");
        Assert.False(tree.HasErrors);
        var without = Bands(SvgGenerator.Generate(tree, new SvgRenderOptions { EmbedFont = false }));

        Assert.True(withLabel.Count >= 2 && without.Count >= 2);
        // A label above system 2 costs room; without it the systems sit closer.
        Assert.True(withLabel[1].Top > without[1].Top,
            $"the label should push system 2 down ({without[1].Top:F2} -> {withLabel[1].Top:F2})");
    }
}
