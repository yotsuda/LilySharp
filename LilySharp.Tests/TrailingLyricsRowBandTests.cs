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
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A lyrics ROW that trails a system reserves the BAND it is drawn in, even when the loose
/// chain declines to place it.
/// </summary>
/// <remarks>
/// <para>
/// Two holes lined up. <c>SkylineBuilder.OuterStaff</c> returns nothing for a TEXT ROW, so a
/// system whose bottom element is one seeds neither its ink nor a staff symbol into the down
/// silhouette — the profile stops at the TOP staff. The row's own band stood in for that
/// through <c>LayoutEngine.LyricReservationBelowSystem</c> → <c>AddLyricBand</c> (2026-08-20)
/// — but that reservation returns null exactly when <c>SystemAlignment.UnmodelledRow</c> is
/// set, and a CHORDS row standing between the staff and the lyrics row sets it. Nothing
/// seeded the edge and nothing stood in for it, so the whole lower body of the system was
/// invisible to the page: on the reported book a body of 16.699 was priced at
/// <c>Distance()</c> 11.0 or less, the 12.000 basic distance won, and the second verse was
/// drawn 0.400 INSIDE the next system's top staff line (user report, session 243,
/// <c>scratch/ベースタブLy/Untitled-6.lys</c>).
/// </para>
/// <para>
/// ⚠️ THE ROW ORDER IS THE WHOLE QUESTION, and that is not arbitrary: a bound lyrics row
/// written directly BELOW its staff FOLDS into it (<c>RenderSpecParser.FoldAdjacentRows</c>,
/// user decision 2026-08-19), so it is note-bound and the reservation covers it. Put a chords
/// row between the two and the fold does not happen — the lyrics stay an independent row, and
/// that row is what nothing reserved. Both orders are written here so the test says which
/// half it is guarding.
/// </para>
/// <para>
/// ⚠️ The same defect reached a TRACKED book: <c>audit/lpreg/lyhygrace.lys</c>
/// (<c>staff / lyrics / staff / lyrics</c>, whose row between two staves also sets
/// <c>UnmodelledRow</c>) drew its two staves through each other's syllables. It is the one
/// book of the 572 tracked whose picture this fix moves, and it moved because it was wrong.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class TrailingLyricsRowBandTests
{
    /// <summary>The reported book's shape: two systems, a chords row, and a trailing lyrics
    /// row whose section B carries N verses.</summary>
    private static string Book(int verses, string rows)
    {
        string b = verses == 1
            ? "twelve thir- teen | four- teen fif- teen |"
            : """
              [~1. twelve thir- teen | four- teen fif- teen |]
                  [~2. six- teen sev- en | teen eight- een |]
              """;
        return $$"""
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
              section A { one two three four | five six seven | eight nine ten | e- le- ven | }
              section B { {{b}} }
            }
            form main { A B A }
            score main {
            {{rows}}
            }
            """;
    }

    // The score-block orders. CB is the reported one; LC is the control that folds.
    private const string ChordsBetween =
        "  staff melody\n  chords prog as names\n  lyrics verse sings melody";
    private const string LyricsThenChords =
        "  staff melody\n  lyrics verse sings melody\n  chords prog as names";

    private static string Render(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        return SvgGenerator.Generate(tree, new SvgRenderOptions { EmbedFont = false });
    }

    private static double Num(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>
    /// Every staff's line span on the page, as (top, bottom) pairs. A staff line is a
    /// horizontal rule that runs the width of the system, which is what separates it from a
    /// ledger line, a hyphen and a stem.
    /// </summary>
    private static List<(double Top, double Bottom)> StaffSpans(string svg)
    {
        var ys = new List<double>();
        foreach (Match m in Regex.Matches(svg,
            "<line[^>]*x1=\"([0-9.-]+)\"[^>]*y1=\"([0-9.-]+)\"[^>]*x2=\"([0-9.-]+)\"[^>]*y2=\"([0-9.-]+)\""))
        {
            double x1 = Num(m.Groups[1].Value), y1 = Num(m.Groups[2].Value);
            double x2 = Num(m.Groups[3].Value), y2 = Num(m.Groups[4].Value);
            // A staff line: horizontal, and long. A ledger line is ~2 staff spaces wide.
            if (Math.Abs(y1 - y2) < 1e-9 && x2 - x1 > 20) ys.Add(Math.Round(y1, 4));
        }
        ys = ys.Distinct().OrderBy(y => y).ToList();

        // Five lines one staff space apart make a staff. Walk them in order.
        var spans = new List<(double, double)>();
        for (int i = 0; i + 4 < ys.Count; i++)
        {
            bool five = true;
            for (int k = 0; k < 4 && five; k++)
                if (Math.Abs(ys[i + k + 1] - ys[i + k] - 1.0) > 1e-6) five = false;
            if (!five) continue;
            if (spans.Count > 0 && ys[i] - spans[^1].Item1 < 2) continue;  // same staff, shifted start
            spans.Add((ys[i], ys[i + 4]));
        }
        return spans;
    }

    /// <summary>A glyph of the music font, which lives in the Unicode private-use
    /// area — a notehead, a clef, a rest, a repeat dot.</summary>
    private static bool IsPrivateUse(char c) => c >= '\uE000' && c <= '\uF8FF';

    /// <summary>Every drawn text and the baseline it sits on.</summary>
    private static List<(double Y, string Text)> Texts(string svg)
        => Regex.Matches(svg, "<text[^>]*y=\"([0-9.-]+)\"[^>]*>([^<]*)</text>")
            .Select(m => (Y: Num(m.Groups[1].Value), Text: m.Groups[2].Value))
            .ToList();

    /// <summary>
    /// ⚠️ A STAFF LINE SPANS EVERY X, so "a syllable's baseline is inside a staff's line
    /// span" is an overlap whatever the syllable's X — which is why this assertion needs no
    /// X model of its own and cannot be talked out of a red by a horizontal argument.
    /// </summary>
    [Theory]
    [InlineData(ChordsBetween, 2)]   // the reported book
    [InlineData(ChordsBetween, 1)]   // …which is already wrong with one verse
    [InlineData(LyricsThenChords, 2)]  // the control that folds, and was always right
    [InlineData(LyricsThenChords, 1)]
    public void NoSyllableIsDrawnThroughAStaff(string rows, int verses)
    {
        string svg = Render(Book(verses, rows));
        var spans = StaffSpans(svg);
        Assert.True(spans.Count >= 2, $"expected two systems' staves, found {spans.Count}");

        foreach (var (y, text) in Texts(svg))
        {
            // Only real words: the music font's private-use glyphs (noteheads, clefs, rests)
            // are drawn ON the staff by design, and so is a boxed mark's own letter.
            if (!text.Any(char.IsLetterOrDigit) || text.Any(IsPrivateUse)) continue;
            foreach (var (top, bottom) in spans)
                Assert.False(y >= top && y <= bottom,
                    $"'{text}' sits at {y:F2}, inside a staff spanning {top:F2}‥{bottom:F2}");
        }
    }

    /// <summary>
    /// …and the room actually RESPONDS to the row: a second verse in the trailing row must
    /// push the next system down.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS THE SHARPEST RED. Before the fix the two books put their second system's
    /// top staff line at the IDENTICAL Y — the whole row band, verses and all, priced at
    /// zero — so the failure is not "a bit too tight" but "not measured at all". A test that
    /// only asserted clearance would go green again the day a font grew a shorter descender.
    /// </remarks>
    [Theory]
    [InlineData(ChordsBetween)]
    [InlineData(LyricsThenChords)]
    public void ASecondVerseInTheTrailingRow_PushesTheNextSystemDown(string rows)
    {
        double TopOfSecondSystem(int verses)
        {
            var spans = StaffSpans(Render(Book(verses, rows)));
            Assert.True(spans.Count >= 2);
            return spans[1].Top;
        }

        double one = TopOfSecondSystem(1);
        double two = TopOfSecondSystem(2);
        Assert.True(two > one + 0.5,
            $"the second verse bought no room: system 2 starts at {one:F3} with one verse "
            + $"and {two:F3} with two");
    }
}
