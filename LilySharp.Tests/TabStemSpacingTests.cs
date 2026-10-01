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

    /// <summary>
    /// The same on a NUMBERS-ONLY tab: its zero-length stub on string 1 still points down and
    /// still meets the bar, over (2.048, 2.5) page units — 0.452 / 7 · 0.4.
    /// </summary>
    /// <remarks>
    /// MEASURED (2.26.0, LilySharp-Lab sessions/p661/tabstem/ls.ly, the probe above without
    /// <c>\tabFullNotation</c>): first columns 7.265823 and 7.240000. Until session 661 the
    /// numbers-only line start read the notation stems of the tab's pitches.
    /// </remarks>
    [Fact]
    public void ALineStartRepeatBarReadsANumbersTabsStub()
    {
        const string book = """
            octave absolute
            time 4/4

            part gtr { instrument guitar }

            section A { gtr { c4\5 c\5 c\5 c\5 | break } }
            section B { gtr { e'4\1 e'\1 e'\1 e'\1 | break } }
            section C { gtr { c4\5 c\5 c\5 c\5 | } }

            form main { A |: B :| |: C :| }

            score main { tab gtr as numbers }
            """;
        var g = RenderedGeometry.Render(book);
        var firsts = g.Texts.Where(t => t.Role == TextRole.TabFret)
            .GroupBy(t => Math.Round(t.Y, 1))
            .OrderBy(grp => grp.Key)
            .Select(grp => grp.Min(t => t.X))
            .ToArray();
        Assert.Equal(3, firsts.Length);
        Assert.Equal(0.025823, firsts[1] - firsts[2], 4);
    }

    /// <summary>
    /// A lone flagged up-stem eighth on a low bass string is spaced as on any string: the TAB
    /// draws no ledger lines and no notation flag, so its column is not priced as a ledgered
    /// note under a staff.
    /// </summary>
    /// <remarks>
    /// MEASURED (2.26.0, Lab sessions/p736/dig, `lysc ly --pin-fonts` twins): the first gap is
    /// 2.2891 in both books, as on the D string (`ees8 ees4.`). Until session 736 Lily# read the
    /// notation skyline of B♭1 — two ledger lines below a bass staff, the flag-low regime — and
    /// gave 2.567. The digits are LilyPond's size so the gap reads the spacing alone.
    /// </remarks>
    [Theory]
    [InlineData("bes,,8 bes,,4. r2 |")]
    [InlineData("tuplet 3/2 { bes,,8 bes,,4 } bes,,4 r2 |")]
    public void ALowBassEighthIsNotSpacedAsALedgeredNote(string music)
    {
        string book = $$"""
            fonts { tab size 1.7218 }
            octave absolute
            part cb {
              instrument bass
              section A { {{music}} }
            }
            form main { A }
            score main { tab cb }
            """;
        var g = RenderedGeometry.Render(book);
        var xs = g.Texts.Where(t => t.Role == TextRole.TabFret).Select(t => t.X).OrderBy(x => x).ToArray();
        Assert.Equal(2.289171, xs[1] - xs[0], 3);
    }

    private static void AssertGaps(string music, double[] lilyPondGaps)
    {
        var g = RenderedGeometry.Render(Book.Replace("MUSIC", music));
        // One digit a column, every fret a single digit: the digit's X is its column's plus a
        // constant, so digit gaps are column gaps.
        var digits = g.Texts.Where(t => t.Role == TextRole.TabFret).OrderBy(t => t.X).ToArray();
        var xs = digits.Select(t => t.X).ToArray();
        Assert.Equal(lilyPondGaps.Length + 1, xs.Length);

        // ⚠️ A GUITAR'S COLUMNS ARE ALSO HELD APART BY LILY#'S OWN GAP (owner's decision
        // 2026-10-01, session 733 — TabConstants.ReducedFretColumnGap): two neighbouring digits'
        // half advances plus that clear air is the rod, and where LilyPond's gap is narrower the
        // rod is what Lily# draws. Everything else in the gap — the stem corrections this class
        // is about — is still LilyPond's.
        double Advance(int i) => LilySharp.Core.Rendering.TextFontMetrics.Advance(
            digits[i].Text, digits[i].FontSize, sans: false, LilySharp.Core.Svg.Layout.TabConstants.ReducedFretStyle);
        double Expected(int i) => Math.Max(lilyPondGaps[i],
            Advance(i) / 2 + Advance(i + 1) / 2 + LilySharp.Core.Svg.Layout.TabConstants.ColumnGap(6));
        var off = Enumerable.Range(0, lilyPondGaps.Length)
            .Select(i => (Gap: i + 1, LilyPond: Expected(i), LilySharp: xs[i + 1] - xs[i]))
            .Where(p => Math.Abs(p.LilySharp - p.LilyPond) > 0.001)
            .ToList();
        Assert.True(off.Count == 0,
            "tab column gaps off LilyPond's:\n"
            + string.Join("\n", off.Select(p => $"  gap {p.Gap}: LilyPond {p.LilyPond:F6}  Lily# {p.LilySharp:F6}")));
    }
}
