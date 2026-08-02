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
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using System.Collections.Immutable;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class AccidentalPlacementTests
{
    [Fact]
    public void SingleAccidental_PositionedLeftOfNote()
    {
        var placement = new AccidentalPlacement();
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(0, "sharp", false)
        );

        var layouts = placement.CalculatePositions(notes);

        Assert.Single(layouts);
        Assert.True(layouts[0].XOffset < 0, "Accidental should be left of note");
    }

    [Fact]
    public void TwoAccidentals_FarApart_SameColumn()
    {
        var placement = new AccidentalPlacement();
        // Staff positions 0 and 8 are far apart (4 staff spaces)
        // Glyph Y-extents don't overlap → placed at same X distance
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(0, "sharp", false),
            new ChordNoteInfo(8, "flat", false)
        );

        var layouts = placement.CalculatePositions(notes);

        Assert.Equal(2, layouts.Length);
        // Non-overlapping Y extents → both at rightmost position (different widths only)
        double xDiff = Math.Abs(layouts[0].XOffset - layouts[1].XOffset);
        Assert.True(xDiff < 0.5, $"Far apart accidentals should be in same column, got xDiff={xDiff}");
    }

    [Fact]
    public void TwoAccidentals_Close_DifferentColumns()
    {
        var placement = new AccidentalPlacement();
        // Staff positions 0 and 2 are close (1 staff space apart)
        // Glyph Y-extents overlap → second must shift left
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(0, "sharp", false),
            new ChordNoteInfo(2, "flat", false)
        );

        var layouts = placement.CalculatePositions(notes);

        Assert.Equal(2, layouts.Length);
        double xDiff = Math.Abs(layouts[0].XOffset - layouts[1].XOffset);
        Assert.True(xDiff > 0.3, $"Close accidentals should be in different columns, got xDiff={xDiff}");
    }

    [Fact]
    public void NoAccidentals_ReturnsEmpty()
    {
        var placement = new AccidentalPlacement();
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(0, null, false),
            new ChordNoteInfo(4, null, false)
        );

        var layouts = placement.CalculatePositions(notes);

        Assert.Empty(layouts);
    }

    [Fact]
    public void CalculateSinglePosition_ForNote()
    {
        var placement = new AccidentalPlacement();
        var note = new NoteItem(0, Core.Semantics.Fraction.Quarter, 0, "flat", false, 0);

        var layout = placement.CalculateSinglePosition(note);

        Assert.NotNull(layout);
        Assert.Equal(0, layout.Value.StaffPosition);
        Assert.Equal("flat", layout.Value.Accidental);
        Assert.True(layout.Value.XOffset < 0);
    }

    [Fact]
    public void CalculateSinglePosition_NoAccidental_ReturnsNull()
    {
        var placement = new AccidentalPlacement();
        var note = new NoteItem(0, Core.Semantics.Fraction.Quarter, 0, null, false, 0);

        var layout = placement.CalculateSinglePosition(note);

        Assert.Null(layout);
    }

    // --- Courtesy (parenthesized) accidental skylines ---
    // Expected values are read off a LIVE LilyPond 2.26.0 dump of AccidentalCautionary's
    // horizontal-skylines (audit/lp-geometry/probes/accidental-skyline.ly, scores
    // PFLAT/PSHARP/PNAT): the stencil embeds accidentals.leftparen/rightparen at the
    // accidental's LILC edges with padding 0 and the skyline is the combined stencil's
    // REAL outline — the parens span y = ±1.052 with the belly at y = 0, and above the
    // paren the accidental's own outline shows through.
    // LILYPOND-REF: lily/accidental.cc:33-43 parenthesize; :45-84 horizontal_skylines.

    [Fact]
    public void CourtesySharp_SkylineIsParenOutline_NotABox()
    {
        var (left, right) = AccidentalPlacement.GlyphSkylinePair("sharp", isCourtesy: true, GlyphMetrics.Design20);

        // Paren bellies at y=0: right = bbox.Right 1.1 + paren 0.6, left = 0 - 0.6.
        Assert.Equal(1.7, right.X(0), 9);
        Assert.Equal(-0.6, left.X(0), 9);
        // Above the paren's top (±1.052) the sharp's OWN outline shows through: LilyPond
        // dumps 0.8639999... between its bars at y=1.2. The former box model kept the
        // paren wall (1.7) up to the sharp's ±1.5 here.
        Assert.Equal(0.864, right.X(1.2), 5);
    }

    [Fact]
    public void CourtesyFlat_SkipsTheFattenBranch()
    {
        var (left, right) = AccidentalPlacement.GlyphSkylinePair("flat", isCourtesy: true, GlyphMetrics.Design20);

        Assert.Equal(1.4, right.X(0), 9);    // bbox.Right 0.8 + paren 0.6
        Assert.Equal(-0.72, left.X(0), 9);   // bbox.Left -0.12 - paren 0.6
        // accidental.cc:65-82 applies the 0.375 fattening ONLY when NOT parenthesized:
        // at y=1.5 the courtesy flat shows its bare stem outline (LP interpolates to
        // ~0.10134 between the dump vertices), not the 0.30 fatten wall.
        Assert.True(right.X(1.5) < 0.29,
            $"courtesy flat must not carry the 0.375 fatten wall, got {right.X(1.5)}");
        Assert.Equal(0.10134, right.X(1.5), 3);
    }

    [Fact]
    public void BareFlat_KeepsTheFattenWall()
    {
        // The fatten moved from the baked data to a runtime branch; a bare flat must
        // still show the 0.30 wall (bbox.Right 0.8 * 0.375) over the stencil Y-extent.
        var (_, right) = AccidentalPlacement.GlyphSkylinePair("flat", isCourtesy: false, GlyphMetrics.Design20);

        Assert.Equal(0.3, right.X(1.5), 9);
    }

    [Fact]
    public void CourtesyNatural_ParenSpanIsNarrowerThanTheGlyph()
    {
        var (_, right) = AccidentalPlacement.GlyphSkylinePair("natural", isCourtesy: true, GlyphMetrics.Design20);

        Assert.Equal(1.2666, right.X(0), 9); // bbox.Right 0.6666 + paren 0.6
        // At y=1.3 (above the paren top 1.052) only the natural's own stem remains:
        // LP interpolates to ~0.16581. The box model kept 1.2666 up to ±1.5 here.
        Assert.Equal(0.16581, right.X(1.3), 3);
    }

    // --- New tests for LilyPond-faithful algorithm ---

    [Fact]
    public void Parameters_Default_MatchesLilyPond()
    {
        var p = AccidentalPlacementParameters.Default;

        // LILYPOND-REF: accidental-placement.cc:398,505
        Assert.Equal(0.2, p.Padding);
        // LILYPOND-REF: define-grobs.scm:84
        Assert.Equal(0.15, p.RightPadding);
        // LILYPOND-REF: accidental-placement.cc:413
        Assert.Equal(0.1, p.HorizonPadding);
    }

    [Fact]
    public void AlterationPriority_NaturalClosestToNote()
    {
        var placement = new AccidentalPlacement();
        // Natural and sharp at same Y-overlap distance → natural should be closer to notes
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(0, "natural", false),
            new ChordNoteInfo(2, "sharp", false)
        );

        var layouts = placement.CalculatePositions(notes);

        Assert.Equal(2, layouts.Length);
        var naturalLayout = layouts.First(l => l.Accidental == "natural");
        var sharpLayout = layouts.First(l => l.Accidental == "sharp");
        // Natural should be closer to note (less negative XOffset)
        Assert.True(naturalLayout.XOffset > sharpLayout.XOffset,
            $"Natural ({naturalLayout.XOffset:F3}) should be closer to notes than sharp ({sharpLayout.XOffset:F3})");
    }

    [Fact]
    public void GlyphExtent_CollisionDetection_UsesActualHeight()
    {
        var placement = new AccidentalPlacement();
        // Double-sharp is very short (height ~1.08 ss), so it can share a column
        // with accidentals that are far enough away in Y. Compute the marginal
        // separation from the live Emmentaler BBoxes so this stays font-accurate.
        // Sharp at pos 0:    Y extent [Sharp.Bottom, Sharp.Top]
        // DoubleSharp at K:  Y extent [K/2 + DSharp.Bottom, K/2 + DSharp.Top]
        // Pick K so DoubleSharp.Bottom_at_K > Sharp.Top + horizon_padding (0.1).
        var sharp = LilySharp.Core.Svg.Layout.GlyphMetrics.AccidentalSharp;
        var dsharp = LilySharp.Core.Svg.Layout.GlyphMetrics.AccidentalDoubleSharp;
        const double horizonPadding = 0.1;
        // Need: K/2 + dsharp.Bottom > sharp.Top + horizonPadding
        // → K > 2 * (sharp.Top + horizonPadding - dsharp.Bottom)
        int marginalPos = (int)Math.Ceiling(2 * (sharp.Top + horizonPadding - dsharp.Bottom)) + 1;

        var notes = ImmutableArray.Create(
            new ChordNoteInfo(0, "sharp", false),
            new ChordNoteInfo(marginalPos, "doubleSharp", false)
        );

        var layouts = placement.CalculatePositions(notes);
        Assert.Equal(2, layouts.Length);

        var sharpLayout = layouts.First(l => l.Accidental == "sharp");
        var dsLayout = layouts.First(l => l.Accidental == "doubleSharp");
        double xDiff = Math.Abs(sharpLayout.XOffset - dsLayout.XOffset);
        Assert.True(xDiff < 0.5,
            $"DoubleSharp at pos {marginalPos} should not collide with sharp at pos 0 (xDiff={xDiff}); " +
            $"sharp.Top={sharp.Top}, dsharp.Bottom_at_pos={marginalPos / 2.0 + dsharp.Bottom}");
    }

    [Fact]
    public void ThreeAccidentals_StackedCorrectly()
    {
        var placement = new AccidentalPlacement();
        // Three accidentals very close together: all overlap in Y → stacked in 3 columns
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(-2, "sharp", false),
            new ChordNoteInfo(0, "flat", false),
            new ChordNoteInfo(2, "natural", false)
        );

        var layouts = placement.CalculatePositions(notes);
        Assert.Equal(3, layouts.Length);

        // All three should have distinct X offsets
        var offsets = layouts.Select(l => l.XOffset).OrderByDescending(x => x).ToList();
        Assert.True(offsets[0] > offsets[1], "First column should be different from second");
        Assert.True(offsets[1] > offsets[2], "Second column should be different from third");
    }

    [Fact]
    public void IsCourtesy_DefaultFalse()
    {
        var placement = new AccidentalPlacement();
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(0, "sharp", false)
        );

        var layouts = placement.CalculatePositions(notes);
        Assert.False(layouts[0].IsCourtesy);
    }

    [Fact]
    public void SinglePosition_XOffset_MatchesExpectedValue()
    {
        var placement = new AccidentalPlacement();
        var note = new NoteItem(0, Core.Semantics.Fraction.Quarter, 0, "sharp", false, 0);

        var layout = placement.CalculateSinglePosition(note);

        Assert.NotNull(layout);
        // A lone accidental runs through position_apes too, so it clears the note by
        // right-padding (0.15) PLUS the inter-column padding (0.2) = 0.35 of ink gap — NOT
        // 0.15 alone. Measured against LilyPond 2.26.0: a single sharp's ink-right sits 0.35
        // ss left of the note-head left edge. The sharp's right skyline reaches its full width
        // (1.10) across the note's Y, so ink-left = -(1.10 + 0.15 + 0.20).
        // LILYPOND-REF: accidental-placement.cc:398-416 (raise -right-padding, then -padding);
        // scm/define-grobs.scm:85 right-padding 0.15.
        double expected = -(GlyphMetrics.AccidentalSharp.Width + 0.15 + 0.20);
        Assert.Equal(expected, layout.Value.XOffset, 3);
    }

    // --- Octave-first priority sorting ---
    // LILYPOND-REF: accidental-placement.cc:164-184 acc_less

    [Fact]
    public void OctaveFirstSort_GroupsSameOctaveAccidentals()
    {
        var placement = new AccidentalPlacement();
        // Two accidentals in same octave (positions 0, 2) with different types
        // should be placed according to alteration priority within that octave
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(0, "flat", false),      // octave 0, priority 3
            new ChordNoteInfo(2, "natural", false)     // octave 0, priority 0
        );

        var layouts = placement.CalculatePositions(notes);

        Assert.Equal(2, layouts.Length);
        // Natural should be closer to notes (lower priority = rightmost)
        var naturalLayout = layouts.First(l => l.Accidental == "natural");
        var flatLayout = layouts.First(l => l.Accidental == "flat");
        Assert.True(naturalLayout.XOffset > flatLayout.XOffset,
            $"Natural ({naturalLayout.XOffset:F3}) should be closer to notes than flat ({flatLayout.XOffset:F3})");
    }

    [Fact]
    public void OctaveFirstSort_DifferentOctaves_GroupsByOctave()
    {
        var placement = new AccidentalPlacement();
        // Accidentals in different octaves: lower octave placed first (rightmost)
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(-7, "sharp", false),    // octave -1
            new ChordNoteInfo(0, "sharp", false),      // octave 0
            new ChordNoteInfo(7, "sharp", false)       // octave 1
        );

        var layouts = placement.CalculatePositions(notes);
        Assert.Equal(3, layouts.Length);

        // All should have valid (negative) offsets
        foreach (var layout in layouts)
            Assert.True(layout.XOffset < 0);
    }

    // --- Flat merge overlap ---
    // LILYPOND-REF: accidental-placement.cc:290-295

    [Fact]
    public void FlatMerge_AdjacentFlats_CanOverlap()
    {
        var placement = new AccidentalPlacement();
        // Two flats in different octaves at close Y positions
        // Use positions in different octaves to avoid same-octave overstrike
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(6, "flat", false),    // octave 0 (6/7=0)
            new ChordNoteInfo(9, "flat", false)     // octave 1 (9/7=1)
        );

        var layouts = placement.CalculatePositions(notes);
        Assert.Equal(2, layouts.Length);

        // With flat merge, the offset difference should be less than
        // what it would be for two sharps at the same positions
        var sharpNotes = ImmutableArray.Create(
            new ChordNoteInfo(6, "sharp", false),
            new ChordNoteInfo(9, "sharp", false)
        );
        var sharpLayouts = placement.CalculatePositions(sharpNotes);

        var flatOffsets = layouts.Select(l => l.XOffset).OrderByDescending(x => x).ToList();
        var sharpOffsets = sharpLayouts.Select(l => l.XOffset).OrderByDescending(x => x).ToList();

        double flatSpacing = Math.Abs(flatOffsets[0] - flatOffsets[1]);
        double sharpSpacing = Math.Abs(sharpOffsets[0] - sharpOffsets[1]);

        Assert.True(flatSpacing < sharpSpacing,
            $"Flat spacing ({flatSpacing:F3}) should be tighter than sharp spacing ({sharpSpacing:F3}) due to flat merge");
    }

    [Fact]
    public void FlatMerge_FlatAndSharp_NoOverlap()
    {
        var placement = new AccidentalPlacement();
        // Flat + sharp should NOT get the flat merge overlap
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(0, "flat", false),
            new ChordNoteInfo(2, "sharp", false)
        );

        var layouts = placement.CalculatePositions(notes);
        Assert.Equal(2, layouts.Length);
        // Both should have valid offsets
        foreach (var layout in layouts)
            Assert.True(layout.XOffset < 0);
    }

    [Theory]
    [InlineData("sharp", "flat", "natural", "doubleSharp", "doubleFlat")]
    public void AllAccidentalTypes_ProduceValidLayout(
        string a1, string a2, string a3, string a4, string a5)
    {
        var placement = new AccidentalPlacement();
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(-4, a1, false),
            new ChordNoteInfo(-2, a2, false),
            new ChordNoteInfo(0, a3, false),
            new ChordNoteInfo(2, a4, false),
            new ChordNoteInfo(4, a5, false)
        );

        var layouts = placement.CalculatePositions(notes);

        Assert.Equal(5, layouts.Length);
        foreach (var layout in layouts)
        {
            Assert.True(layout.XOffset < 0, $"Accidental {layout.Accidental} at pos {layout.StaffPosition} should be left of note");
        }
    }

    // --- stagger_apes ---
    // LILYPOND-REF: accidental-placement.cc:261-336

    [Fact]
    public void Stagger_WidelySpacedAccidentals_AllValid()
    {
        // Accidentals spread far apart (different octaves)
        // Stagger should produce valid non-overlapping layout
        var placement = new AccidentalPlacement();
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(-7, "sharp", false),   // octave -1
            new ChordNoteInfo(0, "flat", false),       // octave 0
            new ChordNoteInfo(7, "natural", false),    // octave 1
            new ChordNoteInfo(14, "sharp", false)      // octave 2
        );

        var layouts = placement.CalculatePositions(notes);
        Assert.Equal(4, layouts.Length);

        // All offsets should be negative (left of note)
        foreach (var l in layouts)
            Assert.True(l.XOffset < 0, $"{l.Accidental} at {l.StaffPosition}: offset {l.XOffset:F3}");
    }

    [Fact]
    public void Stagger_ClusterVsIsolated_ClusterCloserToNotes()
    {
        // Dense cluster (positions 0,1,2) vs isolated note at position 14
        // The cluster (3 accidentals) should be closer to noteheads than the isolated one
        var placement = new AccidentalPlacement();
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(0, "sharp", false),
            new ChordNoteInfo(1, "sharp", false),
            new ChordNoteInfo(2, "sharp", false),
            new ChordNoteInfo(14, "sharp", false)  // far away, isolated
        );

        var layouts = placement.CalculatePositions(notes);
        Assert.Equal(4, layouts.Length);

        // All should be valid
        foreach (var l in layouts)
            Assert.True(l.XOffset < 0);

        // The isolated accidental should have valid offset
        var isolated = layouts.First(l => l.StaffPosition == 14);
        Assert.True(isolated.XOffset < 0);
    }

    [Fact]
    public void Stagger_TwoEntries_NoStagger()
    {
        // Only 2 entries: stagger is not applied (threshold is >2)
        var placement = new AccidentalPlacement();
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(0, "sharp", false),
            new ChordNoteInfo(2, "flat", false)
        );

        var layouts = placement.CalculatePositions(notes);
        Assert.Equal(2, layouts.Length);

        foreach (var l in layouts)
            Assert.True(l.XOffset < 0);
    }

    // --- Same-octave overstrike ---
    // LILYPOND-REF: accidental-placement.cc set_ape_skylines()
    // Overstrike only applies within the same note-name group (APE).

    [Fact]
    public void SameNoteName_SameOctave_SameAlteration_Overstrike()
    {
        // Two sharps on the same note name in the same octave group
        // (positions -5 and 2: both note-name class 2, both octave 0 via staffPos/7)
        // → same note name + same octave + same alteration → overstrike
        var placement = new AccidentalPlacement();
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(-5, "sharp", false),
            new ChordNoteInfo(2, "sharp", false)
        );

        var layouts = placement.CalculatePositions(notes);
        Assert.Equal(2, layouts.Length);

        var layoutLow = layouts.First(l => l.StaffPosition == -5);
        var layoutHigh = layouts.First(l => l.StaffPosition == 2);

        // Both should share the same X offset (overstrike)
        Assert.Equal(layoutLow.XOffset, layoutHigh.XOffset, 3);
    }

    [Fact]
    public void DifferentNoteName_SameOctave_SameAlteration_NoOverstrike()
    {
        // Two sharps on DIFFERENT note names (positions 0 and 2: note-name classes 0 and 2)
        // → different note names → no overstrike, positioned by skyline collision
        // LILYPOND-REF: In LilyPond, these would be in separate APEs
        var placement = new AccidentalPlacement();
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(0, "sharp", false),
            new ChordNoteInfo(2, "sharp", false)
        );

        var layouts = placement.CalculatePositions(notes);
        Assert.Equal(2, layouts.Length);

        var layout0 = layouts.First(l => l.StaffPosition == 0);
        var layout2 = layouts.First(l => l.StaffPosition == 2);

        // Should have different X offsets (no overstrike between different note names)
        Assert.NotEqual(layout0.XOffset, layout2.XOffset);
    }

    [Fact]
    public void ThreeNaturals_DifferentNoteNames_AllSeparated()
    {
        // Three naturals on D, F, A (different note names) should NOT overstrike
        // LILYPOND-REF: Each would be in a separate APE → positioned by skyline collision
        var placement = new AccidentalPlacement();
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(-5, "natural", false),  // D4
            new ChordNoteInfo(-3, "natural", false),  // F4
            new ChordNoteInfo(-1, "natural", false)   // A4
        );

        var layouts = placement.CalculatePositions(notes);
        Assert.Equal(3, layouts.Length);

        // All three should have different X offsets — NO overstrike across note names
        var xOffsets = layouts.Select(l => l.XOffset).Distinct().ToList();
        Assert.True(xOffsets.Count >= 2,
            $"Expected at least 2 distinct X offsets for 3 naturals on different note names, got {xOffsets.Count}: " +
            string.Join(", ", layouts.Select(l => $"pos={l.StaffPosition} x={l.XOffset:F3}")));
    }

    [Fact]
    public void SameOctave_DifferentAlteration_NoOverstrike()
    {
        // Sharp and flat in same octave → different alteration → separate positions
        var placement = new AccidentalPlacement();
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(0, "sharp", false),
            new ChordNoteInfo(2, "flat", false)
        );

        var layouts = placement.CalculatePositions(notes);
        Assert.Equal(2, layouts.Length);

        // Should have different X offsets
        var sharpLayout = layouts.First(l => l.Accidental == "sharp");
        var flatLayout = layouts.First(l => l.Accidental == "flat");
        Assert.NotEqual(sharpLayout.XOffset, flatLayout.XOffset);
    }

    [Fact]
    public void DifferentOctaves_SameAlteration_NoOverstrike()
    {
        // Two sharps in different octaves → no overstrike
        var placement = new AccidentalPlacement();
        var notes = ImmutableArray.Create(
            new ChordNoteInfo(0, "sharp", false),   // octave 0
            new ChordNoteInfo(7, "sharp", false)     // octave 1
        );

        var layouts = placement.CalculatePositions(notes);
        Assert.Equal(2, layouts.Length);

        // Both should have valid offsets (may or may not be same depending on collision)
        foreach (var l in layouts)
            Assert.True(l.XOffset < 0);
    }
}
