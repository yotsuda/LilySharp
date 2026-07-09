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
        // The bar edge still prices the symbol's width so it clears the barline
        // (this is what keeps an all-rest attached bar wide enough).
        Assert.True(result[0].MinDistance > 0.5,
            $"edge spring ({result[0].MinDistance:F2}) should reserve the symbol width");
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
