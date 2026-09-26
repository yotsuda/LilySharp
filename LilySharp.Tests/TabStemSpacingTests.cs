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
using LilySharp.Core.Rendering;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A FULL tab's columns are spaced by the stems the TAB draws — the digits' strings and the
/// tab's own stem rule — not by the notation stems of the same notes.
/// </summary>
/// <remarks>
/// <para>
/// LILYPOND-REF: lily/note-spacing.cc:204-315 stem_dir_correction — each voice's wish is
///   corrected by that voice's own stems: head_positions and pure_y_extent · 2 / staff_space.
/// LILYPOND-REF: lily/staff-spacing.cc:43-67 Staff_spacing::optical_correction — the down stem
///   after a bar line, the stem's pure extent against the bar's extent over its staff space.
/// </para>
/// <para>
/// MEASURED against LilyPond 2.26.0 (audit/lp-geometry/probes/tab-stem-spacing.ly, a guitar
/// tab under <c>\tabFullNotation</c>): the 31 column gaps below. Until session 634 a tab
/// voice's wish read the NOTATION stems, and 11 of them were off — by the ±0.25 of a
/// same-direction correction the pitches earned and the strings did not (a run on ONE string
/// took one), by the missing one a string change earned (−0.25 / +0.25), by 0.05 on every
/// up/down quarter pair (the notation stems' overlap), and by 0.04 into every bar that opens
/// on a down stem.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class TabStemSpacingTests
{
    private const string Book = """
        octave absolute
        time 4/4

        part gtr { instrument guitar }

        section A {
          gtr { MUSIC }
        }

        form main { A }

        score main { tab gtr }
        """;

    /// <summary>Bars 1-5 of the probe: one system, so every gap is its natural length.</summary>
    private const string Bars1To5 =
        @"c4\5 e'\1 c\5 e'\1 | e'4\1 g\3 e'\1 c\5 | c8\5 d\5 e\4 f\4 g'\1 f'\1 e'\1 d'\2 | "
        + @"e'8\1 f'\1 g'\1 a'\1 e'\1 f'\1 g'\1 a'\1 | c8\5 d\5 e\4 f\4 c\5 d\5 e\4 f\4 |";

    /// <summary>Bars 6-7 of the probe, a book of their own for the same reason. Bar 6's second
    /// group stems DOWN as a group though its c's alone would stem up; bar 7 begins its up stems
    /// on a fret-4 digit, whose height sets how far the next, down stem overlaps it.</summary>
    private const string Bars6To7 =
        @"c8\5 d\5 e\4 fis\4 c\5 d\5 c\5 e'\1 | fis8\4 fis\4 fis\4 fis\4 g'\1 d'\3 g'\1 g'\1 |";

    /// <summary>LilyPond's column gaps for <see cref="Bars1To5"/>, in page units (the probe's
    /// header). A gap that crosses a bar line includes the bar.</summary>
    private static readonly double[] Bars1To5Gaps =
    [
        3.898514, 3.079829, 3.898514, 4.598264,
        3.239171, 3.739171, 3.079829, 4.788650,
        2.289171, 2.539171, 2.289171, 2.555657, 2.289171, 2.289171, 2.039171, 3.398264,
        2.289171, 2.289171, 2.289171, 2.289171, 2.289171, 2.289171, 2.289171, 3.169693,
        2.289171, 2.539171, 2.289171, 2.039171, 2.289171, 2.539171, 2.289171,
    ];

    /// <summary>The same for <see cref="Bars6To7"/> (the probe's gaps after bar 5's bar line).</summary>
    private static readonly double[] Bars6To7Gaps =
    [
        2.289171, 2.539171, 2.289171, 2.289171, 2.289171, 2.289171, 2.539171, 3.129171,
        2.289171, 2.289171, 2.289171, 2.557228, 2.039171, 2.539171, 2.289171,
    ];

    [Fact]
    public void AFullTabIsSpacedByItsOwnStems() => AssertGaps(Bars1To5, Bars1To5Gaps);

    [Fact]
    public void ABeamedTabStemIsItsGroups() => AssertGaps(Bars6To7, Bars6To7Gaps);

    /// <summary>
    /// A system that opens on <c>.|:</c> before a full tab's DOWN stem stands its first note
    /// further off by the optical correction, read by the tab's own stem.
    /// </summary>
    /// <remarks>
    /// MEASURED (audit/lp-geometry/probes/tab-stem-spacing-line-start.ly): system 2 (string 1,
    /// stem down) opens 0.228571 further right than system 3 (string 5, stem up). Until
    /// session 635 the line start read the notation stems and gave 0.1894.
    /// LILYPOND-REF: lily/staff-spacing.cc:43-67 Staff_spacing::optical_correction.
    /// </remarks>
    [Fact]
    public void ALineStartRepeatBarReadsTheTabsStem()
    {
        const string book = """
            octave absolute
            time 4/4

            part gtr { instrument guitar }

            section A { gtr { c4\5 c\5 c\5 c\5 | break } }
            section B { gtr { e'4\1 e'\1 e'\1 e'\1 | break } }
            section C { gtr { c4\5 c\5 c\5 c\5 | } }

            form main { A |: B :| |: C :| }

            score main { tab gtr }
            """;
        var g = RenderedGeometry.Render(book);
        // Each system's four digits share one string, so one Y a system; systems in page order.
        var firsts = g.Texts.Where(t => t.Role == TextRole.TabFret)
            .GroupBy(t => Math.Round(t.Y, 1))
            .OrderBy(grp => grp.Key)
            .Select(grp => grp.Min(t => t.X))
            .ToArray();
        Assert.Equal(3, firsts.Length);
        Assert.Equal(0.228571, firsts[1] - firsts[2], 3);
    }

    private static void AssertGaps(string music, double[] lilyPondGaps)
    {
        var g = RenderedGeometry.Render(Book.Replace("MUSIC", music));
        // One digit a column, every fret a single digit: the digit's X is its column's plus a
        // constant, so digit gaps are column gaps.
        var xs = g.Texts.Where(t => t.Role == TextRole.TabFret).Select(t => t.X).OrderBy(x => x).ToArray();
        Assert.Equal(lilyPondGaps.Length + 1, xs.Length);

        var off = Enumerable.Range(0, lilyPondGaps.Length)
            .Select(i => (Gap: i + 1, LilyPond: lilyPondGaps[i], LilySharp: xs[i + 1] - xs[i]))
            .Where(p => Math.Abs(p.LilySharp - p.LilyPond) > 0.001)
            .ToList();
        Assert.True(off.Count == 0,
            "tab column gaps off LilyPond's:\n"
            + string.Join("\n", off.Select(p => $"  gap {p.Gap}: LilyPond {p.LilyPond:F6}  Lily# {p.LilySharp:F6}")));
    }
}
