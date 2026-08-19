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

using System.Linq;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A meter CHANGE at a line break prints a courtesy signature at the end of the previous
/// line; the meter merely being in force does not.
/// </summary>
/// <remarks>
/// The measured halves live in the ledger (<c>courtesy.meter.barline-to-meter</c> and
/// <c>courtesy.meter.barline-to-cancellation</c>, probe courtesy-meter.ly). This file holds
/// the half the ledger is STRUCTURALLY BLIND TO: the case where nothing is drawn. A corpus of
/// distances between drawn things cannot state "and here there is no glyph" — it has nothing
/// to measure — so an implementation that printed the meter at EVERY line end would keep both
/// ledger points exact and still be badly wrong.
/// LILYPOND-REF: lily/time-signature-engraver.cc:114-118 Time_signature_engraver::process_music
///   — initialTimeSignatureVisibility (end-of-line-invisible) is stamped on the FIRST
///   signature only, guarded by <c>scm_is_null (last_spec_)</c>; every later one keeps the
///   grob's all-visible default.
/// </remarks>
public sealed class CourtesyMeterTests
{
    /// <summary>
    /// Two systems: section A fills the first, section B opens the second with whatever
    /// <paramref name="sectionBHeader"/> declares. Section A's rest is written to fill
    /// <paramref name="openingMeter"/>.
    /// </summary>
    private static string Book(string openingMeter, string restA, string sectionBHeader,
                               string openingKey = "ees major") => $$"""
        octave absolute
        time {{openingMeter}}
        key {{openingKey}}

        part m { clef bass }

        section A { m { {{restA}} | } }
        section B { {{sectionBHeader}} m { d,2 e, | } }

        form main { A break B }

        score main { staff m }
        """;

    /// <summary>
    /// Glyphs drawn to the right of the first system's final bar line — i.e. the end-of-line
    /// courtesy group and nothing else, since the next system's glyphs start back at the left
    /// margin and so sit at much smaller x.
    /// </summary>
    private static int CourtesyGlyphCount(string source)
    {
        var g = RenderedGeometry.Render(source);
        double barRight = g.BarlineRight(0);
        return g.Glyphs.Count(x => x.X > barRight + 1e-9);
    }

    [Fact]
    public void MeterChangeAtABreak_PrintsACourtesyMeter()
    {
        // 2/4 → 4/4 across the break: exactly one glyph after the line's bar line, the C.
        Assert.Equal(1, CourtesyGlyphCount(Book("2/4", "r2", "time 4/4")));
    }

    [Fact]
    public void NoMeterChange_LeavesTheLineEndBare()
    {
        // ⚠️ THE POINT OF THIS FILE. One meter throughout, so the INITIAL signature is
        // end-of-line-invisible and nothing at all follows the bar line.
        Assert.Equal(0, CourtesyGlyphCount(Book("4/4", "r1", "")));
    }

    [Fact]
    public void KeyChangeWithoutMeterChange_PrintsTheKeyAndNoMeter()
    {
        // E-flat → A major, meter unchanged: 3 cancellation naturals + 3 sharps = 6 glyphs,
        // and NO seventh. The pairing matters — a fix that keyed the courtesy meter off "the
        // next line has a prefix" rather than off a meter CHANGE would print seven here.
        Assert.Equal(6, CourtesyGlyphCount(Book("4/4", "r1", "key a major")));
    }

    [Fact]
    public void KeyAndMeterChange_PrintsBoth()
    {
        // Cancellation + new key + the meter: the shape a real book has.
        Assert.Equal(7, CourtesyGlyphCount(Book("2/4", "r2", "time 4/4 key a major")));
    }

    // --- AND THE GAP AFTER THE GROUP (session 206) ---

    /// <summary>
    /// The white left between the end-of-line courtesy group's last ink and the end of the
    /// staff line, measured off the DRAWN staff line the courtesy stands on.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE INK RIGHT EDGE NEEDS A WIDTH, and there is no way around it: the quantity has a
    /// glyph inside it. The width is taken from the same metrics the reservation uses, which
    /// would be circular if the width were what this asserted — it is not. The width is pinned
    /// independently by the <c>line-start.time-to-first-note.*</c> ledger family; what this
    /// file's readings are about is the 0.5 AFTER it, which no ledger point reaches.
    /// <para>
    /// The staff line is selected by the Y band the courtesy itself is drawn in, so it is the
    /// line the group actually stands on and not "whichever staff line was longest".
    /// </para>
    /// </remarks>
    private static double LineEndMargin(string source, double lastInkWidth)
    {
        var g = RenderedGeometry.Render(source);
        double barRight = g.BarlineRight(0);
        // Everything right of system 0's final bar line IS the courtesy group: the next
        // system's glyphs start back at the left margin (the count tests above rely on the
        // same fact).
        var courtesy = g.Glyphs.Where(x => x.X > barRight + 1e-9).ToList();
        Assert.NotEmpty(courtesy);
        // A two-row meter centres the narrower row inside the wider one, so the group's ink
        // opens at the LEFTMOST row and runs the WIDER row's width — taking the last glyph's
        // own x would measure from the centred row and come out short.
        double inkLeft = courtesy.Min(x => x.X);
        double glyphY = courtesy[0].Y;
        double staffRight = g.Lines
            .Where(l => System.Math.Abs(l.Y1 - l.Y2) < 1e-9 && System.Math.Abs(l.Y1 - glyphY) < 6.0)
            .Max(l => System.Math.Max(l.X1, l.X2));
        return staffRight - (inkLeft + lastInkWidth);
    }

    /// <summary>
    /// ⚠️ A break-align group has one more member than the grobs in it — <c>right-edge</c> —
    /// and the meter declares 0.5 for it (scm/define-grobs.scm:3951). Lily# ended the staff
    /// line at the meter's advance edge instead, leaving 0.07 ss: the line runs into the
    /// signature, which is what the owner reported on time-break.lys.
    /// <para>
    /// MEASURED IN LILYPOND at 0.500000 across three ink widths, three line widths, ragged
    /// and justified, and both the key and meter paths — probes/courtesy-meter.ly, the
    /// PROBELE section. Nine readings, one number.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("2/4", "r2", "time 4/4", "4", "4")]      // the C
    [InlineData("4/4", "r1", "time 3/4", "3", "4")]      // numerals
    [InlineData("4/4", "r1", "time 12/8", "12", "8")]    // a two-digit numerator
    public void TheCourtesyMeter_LeavesLilyPondsGapBeforeTheLineEnds(
        string openingMeter, string restA, string header, string beats, string beatType)
    {
        double margin = LineEndMargin(
            Book(openingMeter, restA, header),
            LilySharp.Core.Svg.Layout.GlyphMetrics.GetTimeSigWidth(beats, beatType));

        Assert.Equal(0.5, margin, 3);
    }

    /// <summary>
    /// The KEY pays the same gap when IT is last (scm/define-grobs.scm:1995), and a bare
    /// <c>+ 0.4</c> used to stand in for it — the fifth unnamed 0.4 in this group. A-major's
    /// signature inks 3.300030 = 3 x 1.100010 with nothing after it, so the 0.4 was pure
    /// surplus and this reading is what says so.
    /// </summary>
    /// <summary>
    /// ⚠️ FROM C MAJOR, so the change prints a signature and NO cancellation — the half of
    /// the pair below that never depended on the kerning. Both halves read 0.5 now that the
    /// reservation consumes the drawn walk; keeping both keeps the cancel-free case pinned
    /// independently of the kerning arithmetic.
    /// </summary>
    [Fact]
    public void ACourtesyKeyWithNoMeter_LeavesTheSameGap()
        => Assert.Equal(0.5, KeyOnlyMargin(Book("4/4", "r1", "key a major", openingKey: "c major")), 3);

    /// <summary>
    /// ⚠️ AND ONLY THE LAST MEMBER PAYS IT. With a key AND a meter the meter is last, so the
    /// gap is charged once: if the key paid too, this would read 1.0. LilyPond's BOTH score
    /// measures 0.500000 here, the same as the meter alone.
    /// </summary>
    /// <remarks>
    /// ⚠️ NO WIDTH ENTERS THIS ONE. Both books end in the SAME arriving meter (the C of
    /// <c>time 4/4</c>, one glyph), so the distance from that glyph's anchor to the line end
    /// is the same number in both IF AND ONLY IF the gap is charged once. Were the key paying
    /// it as well, the second book would stand 0.5 further from the edge — and the assertion
    /// would say so without needing to know how wide a C is.
    /// </remarks>
    [Fact]
    public void AKeyFollowedByAMeter_IsNotChargedTheGapTwice()
    {
        double meterAlone = MeterAnchorToLineEnd(Book("2/4", "r2", "time 4/4"));
        double afterAKey = MeterAnchorToLineEnd(
            Book("2/4", "r2", "time 4/4 key a major", openingKey: "c major"));

        Assert.Equal(meterAlone, afterAKey, 3);
    }

    /// <summary>
    /// The RIGHTMOST courtesy glyph's anchor → the end of the staff line. Used only where
    /// that glyph is a one-glyph meter (the C), which is what makes it width-free.
    /// </summary>
    private static double MeterAnchorToLineEnd(string source)
    {
        var g = RenderedGeometry.Render(source);
        double barRight = g.BarlineRight(0);
        var courtesy = g.Glyphs.Where(x => x.X > barRight + 1e-9).ToList();
        double glyphY = courtesy[0].Y;
        double staffRight = g.Lines
            .Where(l => System.Math.Abs(l.Y1 - l.Y2) < 1e-9 && System.Math.Abs(l.Y1 - glyphY) < 6.0)
            .Max(l => System.Math.Max(l.X1, l.X2));
        return staffRight - courtesy.Max(x => x.X);
    }

    /// <summary>
    /// The margin from the last SHARP of a courtesy key signature to the end of the staff
    /// line. Measuring off the last sharp's anchor plus one sharp lands on the same edge as
    /// the whole group's width and needs no cancellation arithmetic.
    /// </summary>
    private static double KeyOnlyMargin(string source)
    {
        var g = RenderedGeometry.Render(source);
        double barRight = g.BarlineRight(0);
        var courtesy = g.Glyphs.Where(x => x.X > barRight + 1e-9).ToList();
        double glyphY = courtesy[0].Y;
        double staffRight = g.Lines
            .Where(l => System.Math.Abs(l.Y1 - l.Y2) < 1e-9 && System.Math.Abs(l.Y1 - glyphY) < 6.0)
            .Max(l => System.Math.Max(l.X1, l.X2));
        return staffRight
             - (courtesy.Max(x => x.X)
                + LilySharp.Core.Svg.Layout.GlyphMetrics.GetKeySignatureAccidentalWidth(true));
    }

    /// <summary>
    /// ⚠️ A CANCELLING CHANGE READS THE SAME 0.5 AS A CANCEL-FREE ONE — E-flat → A major
    /// (three naturals, then three sharps) — because the reservation and the draw consume
    /// the SAME walk (<c>SharedRenderer.KeyChangeGeometry</c>), kerning included.
    /// </summary>
    /// <remarks>
    /// This read 0.650000 until 2026-08-19: <c>SpacingRules.KeyCourtesySuffixWidth</c>
    /// modelled the cancellation as an UPPER BOUND on LilyPond's natural kerning (0.3 per
    /// pair, where the drawn walk kerns 0.3 / 0.15 / 0 by vertical overlap), and the bound's
    /// slack landed in the only place left for it — after the group. Ledger
    /// <c>courtesy.key.key-to-line-end</c> opened on exactly that 0.15 and closed EXACT with
    /// the one-model fix. The inequality below is the rule ("never tighter than LilyPond's
    /// gap"); the equality is what says the reserve no longer exceeds the draw — a
    /// reintroduced bound reads wider than 0.5 here and this is the guard that says so.
    /// </remarks>
    [Fact]
    public void ACancellingKeyReservesAtLeastTheGap_AndTheSurplusIsTheKerningBound()
    {
        double margin = KeyOnlyMargin(Book("4/4", "r1", "key a major"));

        Assert.True(margin >= 0.5 - 1e-6,
            $"the courtesy key's margin ({margin:F6}) is TIGHTER than LilyPond's 0.5 — the "
            + "right-edge entry is not being charged");
        Assert.Equal(0.5, margin, 3);
    }
}

