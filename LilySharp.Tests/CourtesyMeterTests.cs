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
        double staffRight = LineEdge(g, glyphY);
        return staffRight - (inkLeft + lastInkWidth);
    }

    /// <summary>
    /// The LINE EDGE the right-edge padding is measured from: the drawn staff line's end plus
    /// half its thickness, because the ink stops t/2 short of the span it belongs to
    /// (lily/staff-symbol.cc:84 Staff_symbol::print, <c>SharedRenderer.StaffLineInkRight</c>).
    /// Read against the drawn ink the same 0.5 would come out 0.45, in LilyPond as here —
    /// probes/courtesy-meter.ly measures both.
    /// </summary>
    private static double LineEdge(RenderedGeometry g, double glyphY)
    {
        double inkRight = g.Lines
            .Where(l => System.Math.Abs(l.Y1 - l.Y2) < 1e-9 && System.Math.Abs(l.Y1 - glyphY) < 6.0)
            .Max(l => System.Math.Max(l.X1, l.X2));
        return inkRight + LilySharp.Core.Svg.EngravingDefaults.StaffLineThickness / 2.0;
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
            LilySharp.Core.Svg.Layout.GlyphMetrics.GetTimeSigWidth(LilySharp.Core.Rendering.ScoreTextMetrics.Bundled, beats, beatType));

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
        double staffRight = LineEdge(g, glyphY);
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
        double staffRight = LineEdge(g, glyphY);
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

    // --- AND WHEN THE NEW KEY PRINTS NO SIGNATURE (session 804) ---
    //
    // A change into C major / A minor prints no KeySignature. LilyPond keeps the grob with an
    // EMPTY extent and steps over it
    // LILYPOND-REF: lily/break-alignment-interface.cc:144-156 Break_alignment_interface::calc_positioning_done
    // so the cancellation reads its own alist for what prints next, and a change that prints
    // nothing is no member of the group. The measured halves are the ledger's
    // (courtesy.key.cancellation-to-line-end and its three neighbours, probe courtesy-meter.ly
    // scores CANCONLY / CANCMETER / NOKEYMETER); these state the RULE in each shape.

    private static double NaturalWidth => LilySharp.Core.Svg.Layout.GlyphMetrics.AccidentalNatural.Width;

    /// <summary>
    /// E-flat → C major: three naturals and nothing after them. The cancellation is the
    /// group's last member and pays right-edge 0.5 once — the cancellation → key-signature 0.5
    /// has nobody to be paid to. This read 1.0 until session 804.
    /// </summary>
    [Fact]
    public void ACancellationWithNoSignatureAfterIt_PaysTheEdgeGapOnce()
    {
        var g = RenderedGeometry.Render(Book("4/4", "r1", "key c major"));
        double barRight = g.BarlineRight(0);
        var courtesy = g.Glyphs.Where(x => x.X > barRight + 1e-9).ToList();
        Assert.Equal(3, courtesy.Count);

        double margin = LineEdge(g, courtesy[0].Y) - (courtesy.Max(x => x.X) + NaturalWidth);
        Assert.Equal(0.5, margin, 3);
    }

    /// <summary>
    /// The same cancellation with a courtesy meter after it: the meter stands the
    /// CANCELLATION's time-signature entry off the last natural's ink — 1.25
    /// (scm/define-grobs.scm:1941) — not 0.5 to a signature that is not there plus the
    /// signature's 1.15, which is what it read (1.65) until session 804.
    /// </summary>
    [Fact]
    public void ACancellationBeforeAMeter_StandsTheMeterItsOwnGapAway()
    {
        var g = RenderedGeometry.Render(Book("2/4", "r2", "time 4/4 key c major"));
        double barRight = g.BarlineRight(0);
        var courtesy = g.Glyphs.Where(x => x.X > barRight + 1e-9).OrderBy(x => x.X).ToList();
        Assert.Equal(4, courtesy.Count);   // three naturals, then the C

        Assert.Equal(1.25, courtesy[3].X - (courtesy[2].X + NaturalWidth), 3);
    }

    /// <summary>
    /// C major → A minor prints neither cancellation nor signature, so the courtesy meter
    /// stands exactly where it stands when no key changes at all: BarLine's 0.75 off the bar,
    /// and the same distance from the line's end. Until session 804 a group was opened for
    /// the silent key and the meter stood 2.15 out — past the end of its own staff line.
    /// </summary>
    [Fact]
    public void AKeyChangeThatPrintsNothing_LeavesTheMeterWhereTheBarLinePutsIt()
    {
        string plain = Book("2/4", "r2", "time 4/4", openingKey: "c major");
        string silent = Book("2/4", "r2", "time 4/4 key a minor", openingKey: "c major");

        Assert.Equal(1, CourtesyGlyphCount(silent));
        Assert.Equal(0.75, RenderedGeometry.Render(silent).BarlineRightToNextGlyph(0), 3);
        Assert.Equal(MeterAnchorToLineEnd(plain), MeterAnchorToLineEnd(silent), 3);
    }

    /// <summary>
    /// RESERVE = DRAW, in each of the three shapes: the line that carries the courtesy ends
    /// where the next line ends. The staff line is drawn to the last bar plus the suffix
    /// (<c>SharedRenderer.StaffRightEdges</c>) and the music was laid out in the line width
    /// minus the suffix (<c>MultiStaffLayouter.LineEndCourtesyWidth</c>) — two readings of one
    /// width, and when they differ the first line runs past the margin or stops short of it.
    /// </summary>
    /// <remarks>
    /// The readings above cannot see the layout's half: they measure from the bar line, which
    /// moves WITH a wrong reservation. Poisoned (session 804: the layout told a silent key
    /// stands before the meter), every one of them stayed green and the first line ran 0.75
    /// past the second.
    /// </remarks>
    [Theory]
    [InlineData("4/4", "r1", "key c major", "ees major")]            // cancellation last
    [InlineData("2/4", "r2", "time 4/4 key c major", "ees major")]   // cancellation, then meter
    [InlineData("2/4", "r2", "time 4/4 key a minor", "c major")]     // a silent key, then meter
    public void TheLineCarryingTheCourtesy_EndsWhereTheNextLineEnds(
        string openingMeter, string restA, string header, string openingKey)
    {
        var g = RenderedGeometry.Render(Book(openingMeter, restA, header, openingKey));
        // Staff lines only: a ledger line is horizontal too, and a few staff spaces long.
        var ends = g.Lines
            .Where(l => System.Math.Abs(l.Y1 - l.Y2) < 1e-9 && System.Math.Abs(l.X2 - l.X1) > 20.0)
            .Select(l => System.Math.Max(l.X1, l.X2))
            .ToList();
        Assert.Equal(10, ends.Count);   // two systems of five lines
        Assert.Equal(ends.Min(), ends.Max(), 3);
    }

    /// <summary>
    /// The walk itself, in the two branches that can end on a natural — a standard key and a
    /// CUSTOM one, each cancelled to C major: the advance is the ink, ending at the last
    /// natural's right edge. No book in the corpus cancels a custom key to nothing, so that
    /// branch is stated here directly.
    /// </summary>
    [Fact]
    public void AChangeThatPrintsOnlyNaturals_EndsAtItsLastNatural()
    {
        var cMajor = new LilySharp.Core.Svg.Model.KeySignature(0);
        var standard = new LilySharp.Core.Svg.Model.KeySignatureChangeItem(
            cMajor, new LilySharp.Core.Svg.Model.KeySignature(-3), 0);
        var custom = new LilySharp.Core.Svg.Model.KeySignatureChangeItem(
            cMajor,
            new LilySharp.Core.Svg.Model.KeySignature(0,
                LilySharp.Core.Svg.Model.KeySignature.EncodeCustom(new[] { (3, 1), (6, -1) })),
            0);

        foreach (var change in new[] { standard, custom })
        {
            var (glyphs, width) = LilySharp.Core.Rendering.SharedRenderer.KeyChangeGeometry(change);
            Assert.NotEmpty(glyphs);
            Assert.All(glyphs, x => Assert.Equal("natural", x.Kind));
            Assert.Equal(glyphs[^1].Dx + NaturalWidth, width, 6);
            // The whole change is the cancellation column's: no signature part to align.
            var parts = LilySharp.Core.Svg.Layout.SpacingRules.KeyChangeParts(change);
            Assert.Equal(width, parts.CancellationWidth, 9);
            Assert.Equal(0, parts.KeyWidth);
        }
    }
}

