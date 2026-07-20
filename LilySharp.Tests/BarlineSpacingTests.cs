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
    public void BarlineToNote_UsesNextNote_NotFirstNote()
    {
        // LILYPOND-REF: (next-note . (semi-fixed-space . 0.9)) — scm/define-grobs.scm:301.
        // Every bar line inside a system has break_status_dir == CENTER, and
        // Staff_spacing::get_spacing reaches for `first-note` ONLY when that dir is not
        // CENTER (a system start). So a note after a bar line gets 0.9, never the
        // 1.3 of `first-note` — including the first note of a measure.
        // Verified on LilyPond 2.24.4: overriding BarLine's `first-note` from 0.0 to
        // 5.0 leaves every grob X in `c'1 c'1` bit-identical, because it is never read.
        // LILYPOND-REF: lily/staff-spacing.cc:147-153.
        double space = SpacingRules.GetBarlineToItemSpace(CreateNote());
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
