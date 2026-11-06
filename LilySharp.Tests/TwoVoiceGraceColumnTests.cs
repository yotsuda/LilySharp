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
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Two voices' graces before one moment stand in ONE column of the staff (HANDOFF §2 U8b): the
/// columns are paired by their grace moment, spaced by the union of their skylines, shifted
/// apart by Note_collision where they collide, and their accidentals packed as one column —
/// <c>MeasureCollector.StampGraceCompanions</c>, read by <c>SpacingRules.GraceColumns</c> and
/// <c>GraceColumnHeads</c>.
/// </summary>
/// <remarks>
/// MEASURED, LilyPond 2.26.0 (Lab sessions/p1115/v1–v6, NoteHead/Accidental X off a hook): the
/// numbers below are distances BEFORE the main note's head, in staff spaces, read off LilyPond's
/// grobs; Lily#'s are read the same way off its svg.
/// <para>⚠️ ASSERTED AS PAIRS (RULES §5.4): the lower voice's grace alone stands 1.418 before its
/// note (v5); under the upper voice's it stands at the shared column, 1.939 (v1) — a port that
/// never paired the columns passes neither.</para>
/// </remarks>
[Trait("Category", "Unit")]
public class TwoVoiceGraceColumnTests
{
    private static string Book(string voice1, string voice2) => $$"""
        octave absolute
        time 4/4
        part v { clef treble }
        section Main { v { voice { {{voice1}} } { {{voice2}} } } }
        form { ~Main }
        score { staff v }
        """;

    private sealed record Glyph(double X, double Y, double Size, char Char);

    /// <summary>Every music glyph of the page.</summary>
    private static Glyph[] Glyphs(string book)
    {
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(book));
        var inv = CultureInfo.InvariantCulture;
        return Regex.Matches(svg, "<text class=\"music\"[^>]*? x=\"([-\\d.]+)\" y=\"([-\\d.]+)\" font-size=\"([-\\d.]+)\"[^>]*>([^<]*)</text>")
            .Where(m => m.Groups[4].Value.Length > 0)
            .Select(m => new Glyph(double.Parse(m.Groups[1].Value, inv), double.Parse(m.Groups[2].Value, inv),
                double.Parse(m.Groups[3].Value, inv), m.Groups[4].Value[0]))
            .ToArray();
    }

    /// <summary>The page's heads: the BLACK head is the glyph the quarter notes repeat at full
    /// size; the grace heads are the same glyph at the grace's size (font-size −3).</summary>
    private static (Glyph[] Main, Glyph[] Grace, Glyph[] GraceAccidentals) Heads(string book)
    {
        var all = Glyphs(book);
        char head = all.Where(g => g.Size > 3.9).GroupBy(g => g.Char).OrderByDescending(g => g.Count()).First().Key;
        var main = all.Where(g => g.Size > 3.9 && g.Char == head).OrderBy(g => g.X).ToArray();
        var grace = all.Where(g => g.Size < 3.0 && g.Size > 2.7 && g.Char == head).OrderBy(g => g.X).ToArray();
        // The grace's accidental is the −4 font, smaller than its head's −3.
        var accidentals = all.Where(g => g.Size < 2.7 && g.Size > 2.3).OrderBy(g => g.X).ToArray();
        return (main, grace, accidentals);
    }

    private static void Before(Glyph main, Glyph g, double expected)
        => Assert.InRange(main.X - g.X, expected - 0.02, expected + 0.02);

    [Fact]
    public void TwoVoicesGracesAtOneMomentStandInOneColumn()
    {
        // v1: `grace { d'16 }` over `grace { g16 }` — one column, 1.939 before the main note.
        // The d' alone needs that much (its flag against the c' beside it); the g alone 1.418 (v5).
        var (main, grace, _) = Heads(Book("grace { d'16 } c'4 d' e' f' |", "grace { g16 } g4 g g g |"));
        Assert.Equal(2, grace.Length);
        Assert.InRange(grace[1].X - grace[0].X, -0.01, 0.01);
        Before(main[0], grace[0], 1.939);

        var alone = Heads(Book("c'4 d' e' f' |", "grace { g16 } g4 g g g |"));
        Assert.Single(alone.Grace);
        Before(alone.Main[0], alone.Grace[0], 1.418);
    }

    [Fact]
    public void ASecondBetweenTheVoicesGracesIsShiftedAndTheirAccidentalsPackedAsOneColumn()
    {
        // v2: cis' (voice 1, up) over bis (voice 2, down), a second: the cis' keeps the column
        // 3.482 before the main note, the bis stands 0.918 right of it — the grace head's width
        // — at 2.564; the sharps are one accidental column, cis''s 4.525 before, bis's 5.387.
        var (main, grace, accidentals) = Heads(Book("grace { cis'16 } c'4 d' e' f' |", "grace { bis16 } g4 g g g |"));
        Assert.Equal(2, grace.Length);
        Before(main[0], grace[0], 3.482);
        Before(main[0], grace[1], 2.564);
        Assert.Equal(2, accidentals.Length);
        Before(main[0], accidentals[0], 5.387);
        Before(main[0], accidentals[1], 4.525);
    }

    [Fact]
    public void RunsOfUnequalLengthAlignAtTheirEnd()
    {
        // v3: `grace { a16 b16 }` over `grace { g16 }` — the g shares the b's column (−1/16),
        // 1.418 before the main note; the a stands alone at −1/8, 2.836 before.
        var (main, grace, _) = Heads(Book("grace { a16 b16 } c'4 d' e' f' |", "grace { g16 } g4 g g g |"));
        Assert.Equal(3, grace.Length);
        Before(main[0], grace[0], 2.836);
        Before(main[0], grace[1], 1.418);
        Before(main[0], grace[2], 1.418);
    }

    [Fact]
    public void TheColumnsArePairedByTheirMomentNotTheirIndex()
    {
        // v7: `grace { a16 b16 }` over `grace { ais16 }` — the ais shares the b's column (−1/16),
        // not the a's: a second with b, so it stands 0.918 right of it (1.418 before the main note
        // against b's 2.336), its sharp 3.379 before; the a stands alone at 4.897. Paired by index
        // it would meet the a — a unison, merged, no sharp beside the b.
        var (main, grace, accidentals) = Heads(Book("grace { a16 b16 } c'4 d' e' f' |", "grace { ais16 } g4 g g g |"));
        Assert.Equal(3, grace.Length);
        Before(main[0], grace[0], 4.897);
        Before(main[0], grace[1], 2.336);
        Before(main[0], grace[2], 1.418);
        var sharp = Assert.Single(accidentals);
        Before(main[0], sharp, 3.379);
    }

    [Fact]
    public void AUnisonAcrossTheVoicesSharesOneX()
    {
        // v6: b in both voices — Note_collision merges the unison, both heads on one x, 1.939 before.
        var (main, grace, _) = Heads(Book("grace { b16 } c'4 d' e' f' |", "grace { b16 } g4 g g g |"));
        Assert.Equal(2, grace.Length);
        Assert.InRange(grace[1].X - grace[0].X, -0.01, 0.01);
        Before(main[0], grace[0], 1.939);
    }
}
