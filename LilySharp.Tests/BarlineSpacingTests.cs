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

using Xunit;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Tests;

/// <summary>
/// Tests for barline space-alist dynamic padding (H-3).
/// LILYPOND-REF: scm/define-grobs.scm BarLine.space-alist
/// LILYPOND-REF: lily/separation-item.cc:49-70 set_distance()
/// </summary>
[Trait("Category", "Unit")]
public class BarlineSpacingTests
{
    private static NoteItem CreateNote() =>
        new(staffPosition: 0, baseDuration: new Fraction(1, 4), dots: 0,
            accidental: null, needsLedgerLines: false, sourcePosition: 0);

    private static ClefChangeItem CreateClefChange() =>
        new(ClefType.Bass, sourcePosition: 0);

    private static KeySignatureChangeItem CreateKeyChange() =>
        new(new KeySignature(2), new KeySignature(0), sourcePosition: 0);

    [Fact]
    public void BarlineToFirstNote_SemiShrinkSpace()
    {
        // LILYPOND-REF: (first-note . (semi-shrink-space . 1.3))
        double space = SpacingRules.GetBarlineToItemSpace(CreateNote(), isFirstInMeasure: true);
        Assert.Equal(1.3, space, 2);
    }

    [Fact]
    public void BarlineToNextNote_SemiFixedSpace()
    {
        // LILYPOND-REF: (next-note . (semi-fixed-space . 0.9))
        double space = SpacingRules.GetBarlineToItemSpace(CreateNote(), isFirstInMeasure: false);
        Assert.Equal(0.9, space, 2);
    }

    [Fact]
    public void BarlineToClefChange_ExtraSpace()
    {
        // LILYPOND-REF: (clef . (extra-space . 1.0))
        double space = SpacingRules.GetBarlineToItemSpace(CreateClefChange());
        Assert.Equal(1.0, space, 2);
    }

    [Fact]
    public void BarlineToKeySignatureChange_ExtraSpace()
    {
        // LILYPOND-REF: (key-signature . (extra-space . 1.0))
        double space = SpacingRules.GetBarlineToItemSpace(CreateKeyChange());
        Assert.Equal(1.0, space, 2);
    }

    [Fact]
    public void ItemToBarline_NormalNote_UsesBarlinePadding()
    {
        double space = SpacingRules.GetItemToBarlineSpace(CreateNote());
        Assert.Equal(SpacingRules.BarlinePadding, space, 2);
    }

    [Fact]
    public void ItemToBarline_ClefChange_ExtraSpace()
    {
        double space = SpacingRules.GetItemToBarlineSpace(CreateClefChange());
        Assert.Equal(1.0, space, 2);
    }
}
