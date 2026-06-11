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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Verifies the editorial (suggestion) accidental rendering pipeline:
/// the IsEditorial flag propagates from the model through layout, and the
/// resulting accidental is sized smaller and wrapped in parentheses.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/accidental.cc:130-166 — AccidentalSuggestion
/// LILYPOND-REF: scm/define-grobs.scm AccidentalSuggestion (font-size . -3)
/// </remarks>
[Trait("Category", "Unit")]
public class EditorialAccidentalTests
{
    private static readonly AccidentalPlacement Placement = new();
    private static readonly AccidentalPlacementParameters Params = AccidentalPlacementParameters.Default;

    private static NoteItem MakeNote(int staffPos, string acc, bool courtesy = false, bool editorial = false)
        => new(staffPosition: staffPos,
               baseDuration: new Fraction(1, 4),
               dots: 0,
               accidental: acc,
               needsLedgerLines: false,
               sourcePosition: 0,
               isCourtesy: courtesy,
               isEditorial: editorial);

    private static ChordNoteInfo MakeInfo(int staffPos, string acc, bool courtesy = false, bool editorial = false)
        => new(staffPos, acc, NeedsLedgerLines: false, IsCourtesy: courtesy, IsEditorial: editorial);

    [Fact]
    public void Single_PlainSharp_DoesNotMarkEditorial()
    {
        var layout = Placement.CalculateSinglePosition(MakeNote(0, "sharp"));
        Assert.NotNull(layout);
        Assert.False(layout!.Value.IsEditorial);
        Assert.False(layout.Value.IsCourtesy);
    }

    [Fact]
    public void Single_EditorialSharp_PropagatesFlagToLayout()
    {
        var layout = Placement.CalculateSinglePosition(MakeNote(0, "sharp", editorial: true));
        Assert.NotNull(layout);
        Assert.True(layout!.Value.IsEditorial);
    }

    [Fact]
    public void Single_EditorialSharp_IsNarrowerThanCourtesySharp()
    {
        // Both wrap in parens, but editorial scales the glyph by EditorialFontFactor (~0.6),
        // so its X offset is closer to the note than a courtesy paren-wrapped sharp.
        var courtesy = Placement.CalculateSinglePosition(MakeNote(0, "sharp", courtesy: true))!.Value;
        var editorial = Placement.CalculateSinglePosition(MakeNote(0, "sharp", editorial: true))!.Value;

        // XOffset is negative (left of note); editorial should be CLOSER to 0 (less negative).
        Assert.True(editorial.XOffset > courtesy.XOffset,
            $"Editorial should be narrower than courtesy. courtesy={courtesy.XOffset}, editorial={editorial.XOffset}");
    }

    [Fact]
    public void Single_EditorialSharp_IsNarrowerThanPlainSharp()
    {
        // Plain has no parens; editorial has parens but smaller glyph.
        // Width comparison depends on factor: factor * sharpWidth + 2*paren vs sharpWidth.
        // Actual values come from GlyphMetrics (extracted from Emmentaler-20):
        //   sharp width = AccidentalSharp.Right
        //   paren ink width = AccidentalParensInkWidth (both parens, zero padding)
        var plain = Placement.CalculateSinglePosition(MakeNote(0, "sharp"))!.Value;
        var editorial = Placement.CalculateSinglePosition(MakeNote(0, "sharp", editorial: true))!.Value;

        double plainWidth = -plain.XOffset - Params.RightPadding;
        double editorialWidth = -editorial.XOffset - Params.RightPadding;

        double sharpWidth = LilySharp.Core.Svg.Layout.GlyphMetrics.AccidentalSharp.Right;
        double parensInk = LilySharp.Core.Svg.Layout.GlyphMetrics.AccidentalParensInkWidth;
        double expectedEditorialWidth = sharpWidth * Params.EditorialFontFactor + parensInk;
        Assert.Equal(expectedEditorialWidth, editorialWidth, precision: 4);
        Assert.Equal(sharpWidth, plainWidth, precision: 4);
    }

    [Fact]
    public void Chord_EditorialFlagPropagatesThroughMultipleAccidentalsPath()
    {
        // Two-note chord with one editorial accidental.
        var notes = new[]
        {
            MakeInfo(0, "sharp", editorial: true),
            MakeInfo(4, "flat"),
        };

        var layouts = Placement.CalculatePositions(notes);
        Assert.Equal(2, layouts.Length);

        var editorialLayout = layouts.First(l => l.StaffPosition == 0);
        var plainLayout = layouts.First(l => l.StaffPosition == 4);

        Assert.True(editorialLayout.IsEditorial);
        Assert.False(plainLayout.IsEditorial);
        Assert.False(editorialLayout.IsCourtesy);
    }

    [Fact]
    public void EditorialAndCourtesy_CanCoexistOnSameNote_AsIndependentFlags()
    {
        // Both flags set: editorial sizing AND courtesy parens (LP allows this combination).
        var layout = Placement.CalculateSinglePosition(
            MakeNote(0, "natural", courtesy: true, editorial: true))!.Value;
        Assert.True(layout.IsCourtesy);
        Assert.True(layout.IsEditorial);
    }

    [Fact]
    public void EditorialFontFactor_DefaultsToLpDocumentedValue()
    {
        Assert.Equal(0.6, AccidentalPlacementParameters.Default.EditorialFontFactor);
    }
}
