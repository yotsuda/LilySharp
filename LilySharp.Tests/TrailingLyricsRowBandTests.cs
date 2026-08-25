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
/// — but that reservation returned null exactly when <c>SystemAlignment.UnmodelledRow</c> was
/// set, and a CHORDS row standing between the staff and the lyrics row set it. Nothing
/// seeded the edge and nothing stood in for it, so the whole lower body of the system was
/// invisible to the page: on the reported book a body of 16.699 was priced at
/// <c>Distance()</c> 11.0 or less, the 12.000 basic distance won, and the second verse was
/// drawn 0.400 INSIDE the next system's top staff line (user report, session 243,
/// <c>scratch/ベースタブLy/Untitled-6.lys</c>).
/// </para>
/// <para>
/// ★ THE FLAG IS GONE (2026-08-26) AND SO IS THE BAND THAT STOOD IN FOR IT: a chords row is
/// an element of its run like any other non-spaceable line, so what the page reserves is the
/// run's walked INK — <c>LayoutEngine.RunBelowAnchor</c>, the same list the chain solves.
/// What this class guards is therefore no longer "the band is reserved for" but the property
/// underneath it, which outlives either model: the row's ink never reaches the next system,
/// and it is priced rather than free.
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
            ? VerseTexts[0]
            : string.Join(
                "\n    ",
                Enumerable.Range(0, verses).Select(i => $"[~{i + 1}. {VerseTexts[i]}]"));
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

    /// <summary>Section B's verses, one line each — enough of them that a book can be asked
    /// for a run DEEPER than the page spring's own ideal, which is the only regime in which
    /// the room is allowed to move.</summary>
    private static readonly string[] VerseTexts =
    {
        "twelve thir- teen | four- teen fif- teen |",
        "six- teen sev- en | teen eight- een |",
        "nine- teen twen- ty | twen- ty one |",
        "twen- ty two three | twen- ty four |",
        "twen- ty five six | twen- ty sev- en |",
    };

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
    /// …and the room actually RESPONDS to the row — the way LilyPond's does: flat while the
    /// page spring's own ideal covers the run, then growing by the verse step once the run
    /// outgrows it.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS THE SHARPEST RED, and it is the SECOND assertion that makes it sharp.
    /// Before the row was reserved for at all, every verse count put the next system's top
    /// staff line at the IDENTICAL Y — the whole row, verses and all, priced at zero — so
    /// the failure was not "a bit too tight" but "not measured at all". A test that only
    /// asserted clearance would go green again the day a font grew a shorter descender.
    /// <para>
    /// ★ IT USED TO ASSERT THE SECOND VERSE SPECIFICALLY (2026-08-26): "a second verse must
    /// push the next system down". That is a property of a BAND — Lily#'s own model, whose
    /// height grows by <c>MultiStaffLayouter.TextRowVerseSpacing</c> per verse whether
    /// anything needs the room or not — and it is not a property of a solved run. A loose
    /// line is absent from the page's spring chain, so what separates two systems is the
    /// SPRING, floored by whatever is deepest above it; a verse moves the gap only once the
    /// RUN is what is deepest. On the ChordsBetween book it is not, at one verse or at two:
    /// MEASURED, the deepest ink above system 2 is system 2's OWN bar number, and the gap
    /// reads 16.000 both times. Asserting on the second verse was asserting about that bar
    /// number.
    /// </para>
    /// <para>
    /// MEASURED, gap between the two systems' top staff lines at one to five verses —
    /// ChordsBetween 16.000 / 16.000 / 17.530 / 20.330 / 23.130, LyricsThenChords
    /// 13.620 / 15.870 / 18.310 / 24.180 / 26.980. The middle steps differ because line
    /// breaking does; the LAST step is 2.800000000 in BOTH, which is the regime where the run
    /// itself binds and the added verse's spring is rigid at
    /// <c>max(2.8, the two lines' ink + 0.2)</c>. That is the number pinned here.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(ChordsBetween)]
    [InlineData(LyricsThenChords)]
    public void TheTrailingRowBuysRoom_OnceItIsWhatBindsTheGap(string rows)
    {
        double SystemGap(int verses)
        {
            var spans = StaffSpans(Render(Book(verses, rows)));
            Assert.True(spans.Count >= 2, $"expected two systems' staves, found {spans.Count}");
            return spans[1].Top - spans[0].Top;
        }

        double one = SystemGap(1);
        double four = SystemGap(4);
        double five = SystemGap(5);

        // The run is PRICED: a row deep enough to be what floors the gap moves it. A run
        // priced at zero — the defect this class was opened for — cannot do this at any
        // verse count.
        Assert.True(five > one + 2 * VerseStep,
            $"a five-verse row bought no room: {one:F3} against {five:F3}");

        // …and it is priced by the SPRING rather than by a band: in the regime where the run
        // binds, one more verse is worth exactly its own spring's floor and nothing else.
        Assert.Equal(VerseStep, five - four, 6);
    }

    /// <summary>The <c>nonstaff-nonstaff-spacing</c> minimum-distance a verse step blocks at
    /// (ly/engraver-init.ly:653-656) — what the room grows by once the run's own minimum is
    /// what binds.</summary>
    private const double VerseStep = 2.8;
}
