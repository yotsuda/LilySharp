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
using System.Collections.Immutable;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A STAFF-ATTACHED chord symbol overhangs the notes beside it (LilyPond
/// ChordName extra-spacing-width -0.5 . 0.5) rather than reserving its full
/// text width on the note column — otherwise a wide symbol (e.g. "Cmaj7") over
/// the first note pushes the following note right and the note spacing looks
/// uneven. A chords ROW/grid keeps the full reservation (its symbols are the
/// content, with no notes to overhang).
/// </summary>
[Trait("Category", "Unit")]
public sealed class ChordAttachedSpacingTests
{
    private static ImmutableArray<Spring> Springs(int count, double min = 0.5)
    {
        var b = ImmutableArray.CreateBuilder<Spring>(count);
        for (int i = 0; i < count; i++)
            b.Add(new Spring(min, min, 1.0));
        return b.ToImmutable();
    }

    // Two note columns (timings 0 and 1/4) => three springs.
    private static readonly List<Fraction> TwoColumns =
        new() { Fraction.Zero, new Fraction(1, 4) };

    private static ChordNameItem Attached(string text, Fraction timing) =>
        new(text, measureIndex: 0, itemIndex: 0, sourcePosition: 0,
            useTiming: true, timing: timing);

    [Fact]
    public void AttachedChord_OverBareNoteColumn_DoesNotWidenTheNoteSpring()
    {
        // A wide symbol at timing 0; timing 1/4 has no symbol (a bare note).
        var chords = ImmutableArray.Create(Attached("Cmaj7", Fraction.Zero));

        var result = SpacingRules.ApplyChordRowSpacing(
            Springs(3), TwoColumns, measureIndex: 0, chords, includeAttached: true);

        // The interior spring between the symbol column and the bare note column
        // is untouched — the symbol overhangs the note, keeping it evenly spaced.
        Assert.Equal(0.5, result[1].MinDistance, precision: 6);
        // The bar's LEFT edge holds only extra-spacing-width's 0.5 — no part of the ink
        // lies left of the column — plus the rod's own padding 0.1
        // (lily/spacing-spanner.cc:315-316 set_column_rods).
        Assert.Equal(0.6, result[0].MinDistance, precision: 6);
    }

    /// <summary>
    /// The all-rest bar, which is why staff-attached symbols price at all: its only column
    /// is the rest, so nothing but the symbol can hold the bar open. The width is reserved
    /// on the bar's RIGHT edge now, because that is the side the ink runs to
    /// (scm/define-grobs.scm:837-855 — ChordName's extent is <c>(0 . w)</c>).
    /// </summary>
    [Fact]
    public void AttachedChord_OverAnAllRestBar_HoldsTheBarOpenOnTheRight()
    {
        var chords = ImmutableArray.Create(Attached("Cmaj7", Fraction.Zero));
        double w = ChordNameEngraver.SymbolInkWidth("Cmaj7");

        // One column (the whole-bar rest) => two springs.
        var result = SpacingRules.ApplyChordRowSpacing(
            Springs(2), new List<Fraction> { Fraction.Zero },
            measureIndex: 0, chords, includeAttached: true);

        // Each edge carries its box reach plus the rod's padding 0.1
        // (lily/spacing-spanner.cc:315-316 set_column_rods).
        Assert.Equal(0.6, result[0].MinDistance, precision: 6);
        Assert.Equal(w + 0.6, result[1].MinDistance, precision: 6);
    }

    /// <summary>
    /// The reservation is asymmetric because the symbol is. LilyPond's ChordName declares no
    /// <c>X-offset</c> and no <c>self-alignment-interface</c> (scm/define-grobs.scm:837-855),
    /// so its ink runs <c>(0 . w)</c> from its column and <c>extra-spacing-width (-0.5 . 0.5)</c>
    /// makes the spacing extent <c>(-0.5 . w + 0.5)</c>. Before this was ported, Lily# centred
    /// the symbol and both edges reserved <c>w/2 + 0.5</c>, which stood a staff-less line's
    /// first column 0.438600 too far right (ledger staffless.line-start.chords-vs-staff).
    /// </summary>
    [Fact]
    public void ChordName_ReservesItsWholeWidthToTheRightOnly()
    {
        var chords = ImmutableArray.Create(
            new ChordNameItem("Cmaj7", measureIndex: 0, itemIndex: 0, sourcePosition: 0,
                useTiming: true, timing: Fraction.Zero, isChordRow: true));
        double w = ChordNameEngraver.SymbolInkWidth("Cmaj7");

        // One column => two springs: bar line -> column, column -> bar line.
        var result = SpacingRules.ApplyChordRowSpacing(
            Springs(2, min: 0.0), new List<Fraction> { Fraction.Zero },
            measureIndex: 0, chords, includeAttached: false);

        // LEFT of the column: the 0.5 of extra-spacing-width alone (no part of the ink),
        // plus the rod's padding 0.1 (lily/spacing-spanner.cc:315-316 set_column_rods).
        Assert.Equal(0.6, result[0].MinDistance, precision: 6);
        // RIGHT of it: the whole width plus that same 0.5 and the same padding.
        Assert.Equal(w + 0.6, result[1].MinDistance, precision: 6);
        // And the symbol is wide enough that halving it would have shown: this test is not
        // passing on a coincidence between w and w/2.
        Assert.True(w > 1.0, $"the probe symbol must be wider than 1.0 ss (was {w:F3})");
    }

    /// <summary>
    /// The grid-cell floor (a Lily#-own 10 ss, LilyPond has no such thing) puts its
    /// artificial room in the bar's LAST spring only — trailing room after the music.
    /// Never in front of beat 1: a whole-note cell used to share the deficit equally
    /// across both springs, standing the symbol and its syllable ~3.5 ss into the bar
    /// (reported on test/lead-sheet, 2026-07-29). And never into the INNER springs: those
    /// are the duration springs the <c>chord.symbol-width.*spring-control</c> ledger
    /// points hold against LilyPond, and a floor share folded into them is fitting the
    /// corpus cannot see past.
    /// </summary>
    [Fact]
    public void LeadSheetBarFloor_IsTrailingRoomOnly()
    {
        // Two columns: opening spring + one duration spring + closing spring.
        var springs = Springs(3, min: 0.6);

        var result = SpacingRules.EnsureLeadSheetBarWidth(springs);

        // Beat 1 stays by its bar line, the duration spring stays the duration's...
        Assert.Equal(0.6, result[0].MinDistance, precision: 9);
        Assert.Equal(0.6, result[0].IdealDistance, precision: 9);
        Assert.Equal(0.6, result[1].MinDistance, precision: 9);
        Assert.Equal(0.6, result[1].IdealDistance, precision: 9);
        // ...and the whole deficit is trailing room after the last chord.
        Assert.Equal(10.0 - 1.2, result[2].MinDistance, precision: 9);
        Assert.Equal(10.0, result.Sum(s => s.MinDistance), precision: 9);
    }

    [Fact]
    public void TwoAdjacentAttachedChords_StillReserveWidthBetweenThem()
    {
        // Symbols on BOTH columns must not overprint each other.
        var chords = ImmutableArray.Create(
            Attached("Cmaj7", Fraction.Zero),
            Attached("Gmaj7", new Fraction(1, 4)));

        var result = SpacingRules.ApplyChordRowSpacing(
            Springs(3), TwoColumns, measureIndex: 0, chords, includeAttached: true);

        Assert.True(result[1].MinDistance > 0.5,
            $"between two symbols ({result[1].MinDistance:F2}) must reserve width");
    }

    [Fact]
    public void ChordRow_OverBareCell_KeepsFullReservation()
    {
        // A chords ROW/grid (includeAttached == false) has no notes to overhang,
        // so a symbol prices its width even where the neighbouring cell is empty.
        var chords = ImmutableArray.Create(
            new ChordNameItem("Cmaj7", measureIndex: 0, itemIndex: 0, sourcePosition: 0,
                useTiming: true, timing: Fraction.Zero, isChordRow: true));

        var result = SpacingRules.ApplyChordRowSpacing(
            Springs(3), TwoColumns, measureIndex: 0, chords, includeAttached: false);

        Assert.True(result[1].MinDistance > 0.5,
            $"grid cell ({result[1].MinDistance:F2}) keeps the full reservation");
    }
}
