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

using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A staff column that two voices stand on has ONE accidental column, and the accidentals in
/// it do NOT ride the note-collision shift their heads take.
/// LILYPOND-REF: lily/accidental-placement.cc:479-518 calc_positioning_done — one
/// AccidentalPlacement grob per staff moment, whose reference skyline is built from EVERY
/// voice's heads (:303-355 extract_heads_and_stems, :375-385 build_heads_skyline) and which
/// translates the accidentals relative to itself, not to the shifted note column.
/// <para>
/// MEASURED (LilyPond 2.26.0, audit/lp-geometry/probes/cross-voice-accidental.ly): books XVA
/// and XVB put the same lone flat at the same 7.909735 whichever of the two voices carries it,
/// 0.35 left of the LEFTMOST head — never of its own. Lily# solved position_apes once per
/// ITEM and then let the collision shift carry the result, which put the displaced voice's
/// flat ON TOP of the other voice's notehead.
/// </para>
/// </summary>
[Trait("Category", "Unit")]
public class CrossVoiceAccidentalColumnTests
{
    // The flat's LILC box, which is what an accidental's packed X is the LEFT edge of.
    private static readonly double FlatLeft = GlyphMetrics.GetAccidentalBBox("flat").Left;
    private static readonly double FlatWidth = GlyphMetrics.GetAccidentalBBox("flat").Width;

    /// <summary>A lone accidental clears its head by right-padding 0.15 + padding 0.20, so
    /// its ink LEFT sits −(0.80 + 0.35) + (−0.12) from the column. LilyPond's XVB reads the
    /// same distance as 7.909735 against a head at 9.059735.</summary>
    private const double LoneFlatInkLeft = -1.27;

    private static NoteItem Note(int staffPosition, string? accidental) =>
        new(staffPosition, new Fraction(1, 4), 0, accidental,
            needsLedgerLines: false, sourcePosition: 0);

    /// <summary>Two voices, one quarter each, on one column of one measure.</summary>
    private static ImmutableArray<Voice> TwoVoices(MusicItem upper, MusicItem lower)
    {
        static Measure Bar(MusicItem item) => new(
            ImmutableArray.Create(item), BarlineType.None, BarlineType.Single,
            null, 0, 0);
        return ImmutableArray.Create(
            new Voice("v1", ImmutableArray.Create(Bar(upper))),
            new Voice("v2", ImmutableArray.Create(Bar(lower))));
    }

    private static double PackedX(ImmutableArray<Voice> resolved, int voiceIndex) =>
        Assert.IsType<NoteItem>(resolved[voiceIndex].Measures[0].Items[0]).AccidentalX
        ?? throw new Xunit.Sdk.XunitException("the column was not packed at all");

    [Fact]
    public void DisplacedVoicesAccidental_StaysLeftOfTheWholeColumn()
    {
        // XVA: aes' (upper, the voice Lily# displaces) against g' (lower). Only the
        // displaced voice carries an accidental.
        var resolved = StaffAccidentalColumns.Resolve(
            TwoVoices(Note(-1, "flat"), Note(-2, null)));

        double inkLeft = PackedX(resolved, 0);

        // THE CLAIM, and the positive control for it: the flat's ink ends left of the
        // column's reference point — i.e. left of the LEFTMOST head, which is the head of
        // the OTHER voice. Solving per item instead put this flat at +0.086 (the collision
        // shift 1.356 minus its own 1.27), so its ink ran to +1.006 — a full staff space
        // INSIDE the other voice's notehead. That is the reported defect.
        Assert.True(inkLeft + FlatWidth <= 0,
            $"the flat's ink right was {inkLeft + FlatWidth:F3}, i.e. over the column's heads");
    }

    [Fact]
    public void PinnedVoicesAccidental_KeepsTheLoneAccidentalDistance()
    {
        // XVB: the mirror — a' (upper) against ges' (lower, pinned and accidental-carrying).
        // This is the case that was ALREADY right, so it is the control that must not move:
        // the other voice's head sits a head-width to the right and cannot constrain the
        // flat, which therefore keeps the lone-accidental 0.35.
        var resolved = StaffAccidentalColumns.Resolve(
            TwoVoices(Note(-1, null), Note(-2, "flat")));

        Assert.Equal(LoneFlatInkLeft, PackedX(resolved, 1), 3);
    }

    [Fact]
    public void BothVoicesAccidentals_StackInsteadOfOverprinting()
    {
        // XVC: aes' over ges'. LilyPond reads 8.976351 and 7.909736 — two columns 1.066615
        // apart, nested (the gap is smaller than a flat's 0.92 width because the outer flat's
        // real outline tucks under the inner one's).
        var resolved = StaffAccidentalColumns.Resolve(
            TwoVoices(Note(-1, "flat"), Note(-2, "flat")));

        double upper = PackedX(resolved, 0);
        double lower = PackedX(resolved, 1);

        // Two columns, not one overprinted position…
        Assert.True(lower < upper, $"both flats landed at {upper:F3}/{lower:F3}");
        // …the lower one entirely left of the upper one's ink…
        Assert.True(lower + FlatWidth <= upper - FlatLeft + 1e-9,
            $"the flats overlap: {lower:F3}+{FlatWidth:F3} against {upper:F3}");
        // …and both still clear of the heads.
        Assert.True(upper + FlatWidth <= 0);
    }

    [Fact]
    public void OneVoiceOnTheColumn_IsLeftToThePerItemSolve()
    {
        // Nothing to pack against: the single-voice path must stay untouched, which is why
        // the whole corpus renders byte-identically with and without this pass.
        var single = ImmutableArray.Create(
            new Voice("v1", ImmutableArray.Create(new Measure(
                ImmutableArray.Create<MusicItem>(Note(-1, "flat")),
                BarlineType.None, BarlineType.Single, null, 0, 0))));

        var resolved = StaffAccidentalColumns.Resolve(single);

        Assert.Null(Assert.IsType<NoteItem>(resolved[0].Measures[0].Items[0]).AccidentalX);
    }
}
