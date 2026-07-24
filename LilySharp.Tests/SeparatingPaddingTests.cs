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

using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The two distances LilyPond keeps between adjacent musical columns: the SPRING's
/// minimum and the ROD under it.
/// </summary>
/// <remarks>
/// These tests used to assert that <c>NoteSpacingParameters.MinItemGap</c> — a knob
/// LilyPond does not have — governed the note-to-note distance, i.e. they pinned the
/// invention in place. The expectations below are LilyPond's own measurements instead
/// (probe N2N in line-start-mindist.ly, and the saturation dump in
/// compressed-note-spacing.ly), which is what section 5.4 asks for: an implementation
/// constant compared with itself guards nothing.
/// LILYPOND-REF: lily/note-spacing.cc:78-83 — the spring minimum is the columns'
///   skyline distance taken with skyline-vertical-padding, clamped at 0.
/// LILYPOND-REF: lily/separation-item.cc:47-68 — the rod is that distance plus the
///   spacing spanner's padding; :166-179 folds each grob's extra-spacing-width into
///   its box, which is where the 0.2 between two heads comes from.
/// </remarks>
[Trait("Category", "Unit")]
public class SeparatingPaddingTests
{
    private static NoteItem MakeNote(int staffPos = 0) =>
        new(staffPosition: staffPos,
            baseDuration: new Fraction(1, 4),
            dots: 0,
            accidental: null,
            needsLedgerLines: false,
            sourcePosition: 0);

    /// <summary>
    /// LilyPond's spring minimum for two same-pitch quarter heads, measured on probe N2N:
    /// the head's 1.304200 of ink widened by each column's own extra-spacing-width.
    /// </summary>
    private const double LpSpringMinimum = 1.504200;

    /// <summary>
    /// …and the rod under it, LilyPond's spring minimum plus the spacing spanner's padding.
    /// Measured on compressed-note-spacing.ly, where every column's dumped rod reads this and
    /// the drawn gap saturates on it for every width from 19.916929 down to 12.519213.
    /// </summary>
    private const double LpRod = 1.604200;

    [Fact]
    public void SpringMinimum_IsLilyPondsSkylineDistance()
    {
        var prev = MakeNote(0);
        var next = MakeNote(0);
        double dist = SpacingRules.CalculateSkylineDistance(prev, next, staffY: 0);
        Assert.Equal(LpSpringMinimum, dist, precision: 6);
    }

    [Fact]
    public void Rod_IsTheSpringMinimumPlusTheSpacingSpannersPadding()
    {
        var prev = MakeNote(0);
        var next = MakeNote(0);
        double rod = SpacingRules.SeparationRodDistance(prev, next, staffY: 0);
        Assert.Equal(LpRod, rod, precision: 6);
        Assert.Equal(SpacingRules.CalculateSkylineDistance(prev, next, staffY: 0)
                     + SpacingRules.SeparationRodPadding, rod, precision: 9);
    }

    /// <summary>
    /// The knob must not come back. <c>MinItemGap</c> survives for lyric extents, which are
    /// their own unported quantity, but nothing it is set to may move a note-to-note
    /// distance — LilyPond reaches that number through extra-spacing-width and a rod padding,
    /// with no per-pair gap anywhere in the path.
    /// </summary>
    [Fact]
    public void NoteToNoteDistance_DoesNotDependOnMinItemGap()
    {
        var prev = MakeNote(0);
        var next = MakeNote(0);
        double withDefault = SpacingRules.CalculateSkylineDistance(
            prev, next, staffY: 0, NoteSpacingParameters.Default);
        double withTight = SpacingRules.CalculateSkylineDistance(
            prev, next, staffY: 0, NoteSpacingParameters.Default with { MinItemGap = 0.1 });
        double withWide = SpacingRules.CalculateSkylineDistance(
            prev, next, staffY: 0, NoteSpacingParameters.Default with { MinItemGap = 3.0 });

        Assert.Equal(withDefault, withTight, precision: 9);
        Assert.Equal(withDefault, withWide, precision: 9);
    }
}
