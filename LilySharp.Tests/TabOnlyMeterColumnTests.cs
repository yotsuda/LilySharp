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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A score no staff of which engraves a time signature stencil must not reserve a column for a
/// MID-PIECE meter change either — the other end of the defect
/// <see cref="TabOnlyKeyPrefixTests"/> covers at a system head.
/// </summary>
/// <remarks>
/// <para>
/// MEASURED (audit/lp-geometry/probes/tab-numbers-meter.ly, ledger points
/// mid-piece.tab-numbers.meter-identity, mid-piece.tab-numbers.change-bar-vs-plain-bar and
/// mid-measure.tab-numbers.meter-identity): on a bare TabStaff LilyPond renders
/// <c>\time 2/4</c> and <c>\time 16/32</c> byte-identically, and renders BOTH identically to
/// the same bar grid reached with <c>\set Timing.measureLength</c> and no meter command at
/// all. The column is ABSENT, not zero-wide.
/// </para>
/// <para>
/// ⚠️ WHAT THE LEDGER POINTS CANNOT SEE, and why these exist. They are geometry: they measure
/// where a fret digit lands, so they would be equally satisfied by a change column of width 0
/// that still spent its <c>(first-note . (semi-shrink-space . 2.0))</c> distance somewhere the
/// corpus does not look. The tests below assert the MECHANISM instead — that the walks return
/// "there is no change here" rather than "there is one and it is small" — which is the
/// distinction LilyPond's two <c>is_empty ()</c> skips make and the one the port had to get
/// right.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class TabOnlyMeterColumnTests
{
    private static MultiStaffScore Collect(string src)
    {
        var tree = SyntaxTree.Parse(src);
        var spec = RenderSpecParser.FindFirst(tree)!;
        return new MeasureCollector().CollectMultiStaff(tree, spec);
    }

    /// <summary>
    /// Two bars of 4/4, then a bar that OPENS with the meter change. The score block decides
    /// whether anything engraves a meter: <c>tab gt as numbers</c> alone is the bare TabStaff.
    /// </summary>
    private static string AtBarLine(string scoreBlock) => $$"""
        octave absolute
        time 4/4
        part gt { instrument guitar }
        section Main { gt { c4 e g e | c4 e g e | time 2/4 c4 e | c4 e | } }
        form main { ~Main }
        score main "x" { {{scoreBlock}} }
        """;

    /// <summary>The same, with the change INSIDE bar 2 — the other pricing function.</summary>
    private static string MidMeasure(string scoreBlock) => $$"""
        octave absolute
        time 4/4
        part gt { instrument guitar }
        section Main { gt { c4 e g e | c4 e time 2/4 g4 e | c4 e | } }
        form main { ~Main }
        score main "x" { {{scoreBlock}} }
        """;

    private static IReadOnlyList<MusicItem> MeasureItems(MultiStaffScore score, int measureIndex)
        => score.StaffGroups[0].Staves[0].Voices[0].Measures[measureIndex].Items;

    private static TimeSignatureChangeItem TheMeterChange(MultiStaffScore score) =>
        score.StaffGroups[0].Staves.SelectMany(s => s.Voices)
             .SelectMany(v => v.Measures)
             .SelectMany(m => m.Items)
             .OfType<TimeSignatureChangeItem>()
             .Single();

    [Fact]
    public void ANumbersOnlyTabScore_BlanksItsMidPieceMeter_AndAStaffScoreDoesNot()
    {
        // The pair. One score block apart, and the flag is the only thing that differs —
        // the change item is still there in both, because it still re-arms the measure
        // length and still stands in the non-musical column.
        Assert.True(TheMeterChange(Collect(AtBarLine("tab gt as numbers"))).Blanked);
        Assert.False(TheMeterChange(Collect(AtBarLine("staff gt  tab gt as numbers"))).Blanked);

        // ⚠️ And the DEFAULT `tab` is full notation, which engraves a meter — so a tab-only
        // score is not by itself a blanked one. This is the line session 198 had to draw for
        // the line-start half (SpacingRules.ContributesToTimeColumnWidth) and the same line
        // holds here.
        Assert.False(TheMeterChange(Collect(AtBarLine("tab gt"))).Blanked);
    }

    [Fact]
    public void ABlankedMeterOpeningABar_TakesNoBoundaryColumnAtAll()
    {
        // The barline half. The claim is not "the prefix is small" but "there is no prefix":
        // BoundaryChangePrefix returns null, so BarlineToFirstColumnSpring falls to the bar
        // line's own (next-note . (semi-fixed-space . 0.9)) exactly as it does in a bar that
        // carries nothing.
        // LILYPOND-REF: lily/spacing-interface.cc:217-220 extremal_break_aligned_grob — an
        //   empty extent never becomes the last_grob whose space-alist is read.
        var blanked = MeasureItems(Collect(AtBarLine("tab gt as numbers")), 2);
        var engraved = MeasureItems(Collect(AtBarLine("staff gt  tab gt as numbers")), 2);

        Assert.IsType<TimeSignatureChangeItem>(blanked[0]);   // the change IS in the measure
        Assert.Null(SpacingRules.BoundaryChangePrefix(blanked));
        Assert.NotNull(SpacingRules.BoundaryChangePrefix(engraved));

        // ⇒ and the spring the caller builds from it is the plain one. The falsifier is the
        // engraved twin, whose spring is wider by the column it does book.
        var plainBar = MeasureItems(Collect(AtBarLine("tab gt as numbers")), 1);
        Assert.Equal(SpacingRules.BarlineToFirstColumnSpring(plainBar, fillsMeasure: false).IdealDistance,
                     SpacingRules.BarlineToFirstColumnSpring(blanked, fillsMeasure: false).IdealDistance,
                     9);
        Assert.True(SpacingRules.BarlineToFirstColumnSpring(engraved, fillsMeasure: false).IdealDistance
                  > SpacingRules.BarlineToFirstColumnSpring(blanked, fillsMeasure: false).IdealDistance);
    }

    [Fact]
    public void ABlankedMeterInsideABar_IsNotAChangeColumn()
    {
        // The mid-measure half, through the other function. MidMeasureChangeGaps returns NULL
        // — the pair of columns is priced as an ordinary note-to-note spring — rather than a
        // gap of zero, which would still carry the change column's own rod and space-alist.
        // LILYPOND-REF: lily/break-alignment-interface.cc:144-156 calc_positioning_done — the
        //   alignment walk steps over an element whose extent is_empty ().
        var blanked = MeasureItems(Collect(MidMeasure("tab gt as numbers")), 1);
        var engraved = MeasureItems(Collect(MidMeasure("staff gt  tab gt as numbers")), 1);

        // The change shares the timing of the note after it; the column is that pair.
        var blankedColumn = blanked.Skip(2).Take(2).ToList();
        var engravedColumn = engraved.Skip(2).Take(2).ToList();
        Assert.IsType<TimeSignatureChangeItem>(blankedColumn[0]);
        Assert.IsType<TimeSignatureChangeItem>(engravedColumn[0]);

        var prev = new[] { blanked[1] };
        Assert.Null(SpacingRules.MidMeasureChangeGaps(blankedColumn, prev, durationIdeal: 3.0));
        Assert.NotNull(SpacingRules.MidMeasureChangeGaps(engravedColumn, prev, durationIdeal: 3.0));

        // The drawn-position home agrees with the priced one, which is the invariant that
        // keeps a glyph on the space paid for it.
        Assert.Equal(0.0, SpacingRules.MidMeasureChangeRightGap(blankedColumn), 9);
        Assert.True(SpacingRules.MidMeasureChangeRightGap(engravedColumn) > 0);
    }

    [Fact]
    public void ABlankedMeterIsStillNotAMusicalColumn()
    {
        // ⚠️ THE HALF THAT MUST NOT MOVE. A blanked grob is skipped by the walks that read an
        // EXTENT, not by the one that asks which column an item belongs to: in LilyPond it is
        // still an Item of the NonMusical column. Folding the two questions together would
        // have made MeasureLayouter.ItemStartingAt hand a zero-duration grob to the skyline as
        // the note sounding at that moment.
        var blanked = MeasureItems(Collect(AtBarLine("tab gt as numbers")), 2);
        Assert.True(SpacingRules.IsMidMeasureChangeColumn(blanked[0]));
        Assert.False(SpacingRules.IsMusicalColumn(blanked[0]));
    }

    [Fact]
    public void TheBlankingPassIsIdempotentAndLeavesAnEngravedScoreAlone()
    {
        // A score that engraves a meter is returned as it stands — the common path is the one
        // comparison and no allocation — and a blanked one is already blanked, so re-running
        // the pass is identity. Both matter because CollectMultiStaff applies it and
        // SvgGenerator applies it to the single-staff wrap; a pass that copied on every call
        // would double the model for a book that goes through both.
        var engraved = Collect(AtBarLine("staff gt  tab gt as numbers"));
        Assert.Same(engraved, MeterStencil.Blank(engraved));

        var blanked = Collect(AtBarLine("tab gt as numbers"));
        Assert.Same(blanked, MeterStencil.Blank(blanked));
    }

    [Fact]
    public void OnlyTheMeasureCarryingTheChangeIsRewritten()
    {
        // The rewrite keeps every untouched object, which is what lets it run on every score
        // without allocating for the books that have no mid-piece meter at all — and, on the
        // books that do, what keeps the two staves of one part sharing their measures.
        var score = Collect(AtBarLine("tab gt as numbers"));
        var measures = score.StaffGroups[0].Staves[0].Voices[0].Measures;

        // Bars 0, 1 and 3 carry no change; only bar 2 was rebuilt. Asserted through the flag
        // rather than by holding the pre-pass model, which the collector does not hand out.
        Assert.All(new[] { 0, 1, 3 },
            i => Assert.Empty(measures[i].Items.OfType<TimeSignatureChangeItem>()));
        Assert.Single(measures[2].Items.OfType<TimeSignatureChangeItem>());

        // A score with no meter change anywhere is returned unchanged even though nothing in
        // it engraves a meter — the pass has nothing to do and says so by identity.
        var noChange = Collect("""
            octave absolute
            time 4/4
            part gt { instrument guitar }
            section Main { gt { c4 e g e | c4 e g e | } }
            form main { ~Main }
            score main "x" { tab gt as numbers }
            """);
        Assert.Same(noChange, MeterStencil.Blank(noChange));
    }
}
