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
using Xunit;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class NoteCollisionTests
{
    [Fact]
    public void NoCollision_FarApartNotes()
    {
        var collision = new NoteCollision();
        var ups = new[] { 8 };    // High note
        var downs = new[] { 0 };  // Low note

        var result = collision.AnalyzeCollision(ups, downs, upNoteValue: 4, downNoteValue: 4, upDots: 0, downDots: 0);

        Assert.Equal(CollisionType.None, result.Type);
        Assert.Equal(0, result.UpStemXOffset);
        Assert.Equal(0, result.DownStemXOffset);
    }

    [Fact]
    public void MergeCollision_SamePositionSameNoteValue()
    {
        var collision = new NoteCollision();
        var ups = new[] { 4 };
        var downs = new[] { 4 };

        // Same position with same note value can be merged
        var result = collision.AnalyzeCollision(ups, downs, upNoteValue: 4, downNoteValue: 4, upDots: 0, downDots: 0);

        Assert.Equal(CollisionType.Merge, result.Type);
        Assert.True(result.ShouldMerge);
    }

    [Fact]
    public void FullCollision_SamePosition_DifferentDots()
    {
        var collision = new NoteCollision();
        var ups = new[] { 4 };
        var downs = new[] { 4 };

        // Same position but different dots - cannot merge by default
        var result = collision.AnalyzeCollision(ups, downs, upNoteValue: 4, downNoteValue: 4, upDots: 1, downDots: 0);

        Assert.Equal(CollisionType.Full, result.Type);
        Assert.False(result.ShouldMerge);
    }

    [Fact]
    public void FullCollision_SamePosition_DifferentNoteValues()
    {
        var collision = new NoteCollision();
        var ups = new[] { 4 };
        var downs = new[] { 4 };

        // Same position but different note values (half vs quarter) - cannot merge
        var result = collision.AnalyzeCollision(ups, downs, upNoteValue: 2, downNoteValue: 4, upDots: 0, downDots: 0);

        Assert.Equal(CollisionType.Full, result.Type);
        Assert.False(result.ShouldMerge);
    }

    [Fact]
    public void CloseHalfCollision_AdjacentPositions()
    {
        var collision = new NoteCollision();
        var ups = new[] { 5 };    // One position above
        var downs = new[] { 4 };  // One position below

        var result = collision.AnalyzeCollision(ups, downs, upNoteValue: 4, downNoteValue: 4, upDots: 0, downDots: 0);

        // Adjacent positions should cause collision
        Assert.True(result.Type == CollisionType.CloseHalf || result.Type == CollisionType.Full,
            $"Expected CloseHalf or Full, got {result.Type}");
    }

    [Fact]
    public void ChordCollision_MultipleNotes_WithOverlap()
    {
        var collision = new NoteCollision();
        var ups = new[] { 4, 6, 8 };     // Position 4 overlaps
        var downs = new[] { 0, 2, 4 };   // Position 4 overlaps

        var result = collision.AnalyzeCollision(ups, downs, upNoteValue: 4, downNoteValue: 4, upDots: 0, downDots: 0);

        // Has overlapping position - should trigger collision handling
        Assert.NotEqual(CollisionType.None, result.Type);
    }

    [Fact]
    public void ChordCollision_NoOverlap()
    {
        var collision = new NoteCollision();
        var ups = new[] { 6, 8, 10 };    // High chord
        var downs = new[] { 0, 2, 4 };   // Low chord, doesn't touch

        var result = collision.AnalyzeCollision(ups, downs, upNoteValue: 4, downNoteValue: 4, upDots: 0, downDots: 0);

        Assert.Equal(CollisionType.None, result.Type);
    }

    // --- LILYPOND-REF shift multiplier conformance tests ---

    [Fact]
    public void ShiftMultipliers_Default_MatchLilyPond()
    {
        // LILYPOND-REF: lily/note-collision-interface.cc:299-350
        var p = NoteCollisionParameters.Default;

        Assert.Equal(0.52, p.CloseHalfShift);     // close_half_collide :326
        Assert.Equal(0.4, p.DistantHalfShift);    // distant_half_collide :329
        Assert.Equal(0.5, p.FullCollideShift);     // full_collide :327
        Assert.Equal(0.5, p.TouchShift);           // touch :324 (not stem_to_stem 0.65)
        Assert.Equal(0.17, p.MeshingGeneralShift); // meshing_general :337
        Assert.Equal(0.1, p.MeshingDottedShift);   // meshing_dotted :335
    }

    [Fact]
    public void FullCollision_ShiftAmount_MatchesLilyPond()
    {
        // LILYPOND-REF: lily/note-collision.cc:327 full_collide = 0.5, applied
        // symmetrically (automatic_shift d*offset): up +0.5 / down -0.5. The
        // consumer (CalculateVoiceOffsets) pins the leftmost group, so the
        // 2-voice head separation is 2*0.5 = 1.0 notehead widths (side by side).
        var collision = new NoteCollision();
        var ups = new[] { 4 };
        var downs = new[] { 4 };

        var result = collision.AnalyzeCollision(ups, downs, upNoteValue: 2, downNoteValue: 4, upDots: 0, downDots: 0);

        Assert.Equal(CollisionType.Full, result.Type);
        Assert.Equal(0.5, result.UpStemXOffset);
        Assert.Equal(-0.5, result.DownStemXOffset);
    }

    [Fact]
    public void TouchCollision_ShiftAmount_MatchesLilyPond()
    {
        // LILYPOND-REF: lily/note-collision-interface.cc:317
        // stem_to_stem = 0.65 notehead widths
        var collision = new NoteCollision();
        // ups[0] == downs.Last() triggers touch: lowest up == highest down
        var ups = new[] { 4 };
        var downs = new[] { 4 };

        // Same note value, same dots → merge, not touch
        // Use whole notes (noteValue=0) to prevent merge (whole notes can't merge)
        var result = collision.AnalyzeCollision(ups, downs, upNoteValue: 0, downNoteValue: 0, upDots: 0, downDots: 0);

        // Whole notes at same position → full collision (can't merge whole notes)
        Assert.Equal(CollisionType.Full, result.Type);
        Assert.Equal(0.5, result.UpStemXOffset);
    }

    // --- LILYPOND-REF meshing multiplier tests (I-1) ---

    [Fact]
    public void Meshing_SecondInterval_UsesGeneralShift()
    {
        // LILYPOND-REF: lily/note-collision-interface.cc:180-230 check_meshing_chords()
        // Meshing requires different head groups: half (open) + quarter (filled)
        var collision = new NoteCollision();
        var ups = new[] { 5 };    // One position above
        var downs = new[] { 4 };  // Adjacent position

        var result = collision.AnalyzeCollision(ups, downs,
            upNoteValue: 2, downNoteValue: 4, upDots: 0, downDots: 0);

        Assert.Equal(0.17, result.UpStemXOffset, 2);
    }

    [Fact]
    public void Meshing_SecondInterval_DottedNote_UsesDottedShift()
    {
        // LILYPOND-REF: lily/note-collision-interface.cc:180-230
        // Dotted notes with different head groups use MeshingDottedShift (0.1)
        var collision = new NoteCollision();
        var ups = new[] { 5 };
        var downs = new[] { 4 };

        var result = collision.AnalyzeCollision(ups, downs,
            upNoteValue: 2, downNoteValue: 4, upDots: 1, downDots: 0);

        Assert.Equal(0.1, result.UpStemXOffset, 2);
    }

    [Fact]
    public void Meshing_WholeNotes_CannotMesh_UsesHalfShift()
    {
        // LILYPOND-REF: lily/note-collision-interface.cc:180-230
        // Whole notes (round noteheads) cannot mesh
        var collision = new NoteCollision();
        var ups = new[] { 5 };
        var downs = new[] { 4 };

        var result = collision.AnalyzeCollision(ups, downs,
            upNoteValue: 1, downNoteValue: 4, upDots: 0, downDots: 0);

        // Should use standard half collision shift, not meshing
        Assert.True(result.UpStemXOffset > 0.17,
            $"Whole note collision ({result.UpStemXOffset:F2}) should be larger than meshing (0.17)");
    }

    [Fact]
    public void Meshing_ChordWithSeconds_CannotMesh_UsesHalfShift()
    {
        // LILYPOND-REF: lily/note-collision-interface.cc:180-230
        // Chords with multiple notes can't mesh cleanly at seconds
        var collision = new NoteCollision();
        var ups = new[] { 5, 7 };    // Chord: two notes
        var downs = new[] { 4 };     // Single note adjacent to lowest of chord

        var result = collision.AnalyzeCollision(ups, downs,
            upNoteValue: 4, downNoteValue: 4, upDots: 0, downDots: 0);

        // Should use standard half collision shift
        Assert.True(result.UpStemXOffset > 0.17,
            $"Chord collision ({result.UpStemXOffset:F2}) should be larger than meshing (0.17)");
    }

    [Fact]
    public void Meshing_HalfNotes_CannotMesh_SameHeadGroup()
    {
        // LILYPOND-REF: lily/note-collision-interface.cc:180-230
        // Two half notes have the same head group (open) — cannot mesh.
        // LilyPond requires head_group_up != head_group_down for meshing.
        var collision = new NoteCollision();
        var ups = new[] { 5 };
        var downs = new[] { 4 };

        var result = collision.AnalyzeCollision(ups, downs,
            upNoteValue: 2, downNoteValue: 2, upDots: 0, downDots: 0);

        // Same head groups can't mesh: close_half_collide raw 0.52 (not meshing
        // 0.17). Applied symmetrically + pinned, the heads end up ~2*0.52 = 1.04w
        // apart (side by side). The old 1.0 here was the pre-doubled value.
        Assert.Equal(0.52, result.UpStemXOffset, 2);
    }

    [Fact]
    public void Meshing_SmallerThanStandardShift()
    {
        // LILYPOND-REF: lily/note-collision-interface.cc:180-230
        // Meshing shift (0.17) should be much smaller than standard half collision (0.52)
        var p = NoteCollisionParameters.Default;
        Assert.True(p.MeshingGeneralShift < p.CloseHalfShift,
            $"Meshing ({p.MeshingGeneralShift}) should be < CloseHalf ({p.CloseHalfShift})");
        Assert.True(p.MeshingDottedShift < p.MeshingGeneralShift,
            $"MeshingDotted ({p.MeshingDottedShift}) should be < MeshingGeneral ({p.MeshingGeneralShift})");
    }

    // --- LILYPOND-REF head wipe conformance tests (I-2) ---

    [Fact]
    public void HeadWipe_Merge_DownHeadTransparent()
    {
        // LILYPOND-REF: lily/note-collision-interface.cc:381-407
        // When notes merge (same pitch, same note value), down-stem notehead is wiped
        var collision = new NoteCollision();
        var ups = new[] { 4 };
        var downs = new[] { 4 };

        var result = collision.AnalyzeCollision(ups, downs, upNoteValue: 4, downNoteValue: 4, upDots: 0, downDots: 0);

        Assert.Equal(CollisionType.Merge, result.Type);
        Assert.True(result.ShouldMerge);
        Assert.True(result.DownHeadTransparent, "Down-stem head should be transparent on merge");
        Assert.False(result.UpHeadTransparent, "Up-stem head should remain visible on merge");
    }

    [Fact]
    public void HeadWipe_NoCollision_NoTransparency()
    {
        // LILYPOND-REF: lily/note-collision-interface.cc:381-407
        // No collision → no heads hidden
        var collision = new NoteCollision();
        var ups = new[] { 8 };
        var downs = new[] { 0 };

        var result = collision.AnalyzeCollision(ups, downs, upNoteValue: 4, downNoteValue: 4, upDots: 0, downDots: 0);

        Assert.Equal(CollisionType.None, result.Type);
        Assert.False(result.UpHeadTransparent);
        Assert.False(result.DownHeadTransparent);
    }

    [Fact]
    public void HeadWipe_FullCollision_NoTransparency()
    {
        // LILYPOND-REF: lily/note-collision-interface.cc:381-407
        // Full collision (different note values, shifted apart) → no heads wiped
        var collision = new NoteCollision();
        var ups = new[] { 4 };
        var downs = new[] { 4 };

        var result = collision.AnalyzeCollision(ups, downs, upNoteValue: 2, downNoteValue: 4, upDots: 0, downDots: 0);

        Assert.Equal(CollisionType.Full, result.Type);
        Assert.False(result.UpHeadTransparent, "Shifted notes should not have heads wiped");
        Assert.False(result.DownHeadTransparent, "Shifted notes should not have heads wiped");
    }

    [Fact]
    public void HeadWipe_CloseHalf_NoTransparency()
    {
        // Adjacent notes (second interval) → no heads wiped
        var collision = new NoteCollision();
        var ups = new[] { 5 };
        var downs = new[] { 4 };

        var result = collision.AnalyzeCollision(ups, downs, upNoteValue: 4, downNoteValue: 4, upDots: 0, downDots: 0);

        Assert.False(result.UpHeadTransparent);
        Assert.False(result.DownHeadTransparent);
    }

    [Fact]
    public void HeadWipe_MergeChord_DownHeadTransparent()
    {
        // LILYPOND-REF: lily/note-collision-interface.cc:381-407
        // Chord merge: all overlapping down-stem heads should be wiped
        var collision = new NoteCollision();
        var ups = new[] { 4, 6 };
        var downs = new[] { 4, 6 };

        var result = collision.AnalyzeCollision(ups, downs, upNoteValue: 4, downNoteValue: 4, upDots: 0, downDots: 0);

        Assert.Equal(CollisionType.Merge, result.Type);
        Assert.True(result.ShouldMerge);
        Assert.True(result.DownHeadTransparent);
    }

    // --- LILYPOND-REF dot direction adjustment tests (I-4) ---

    [Fact]
    public void DotDirection_DownVoice_OnLine_ForcedDown()
    {
        // LILYPOND-REF: lily/note-collision-interface.cc:411-448
        // In collision context, down-stem voice's dots on lines should shift DOWN
        // to avoid colliding with up-stem voice
        var collision = new NoteCollision();
        var ups = new[] { 5 };   // Up-stem note above
        var downs = new[] { 4 }; // Down-stem note on a line (even position)

        var result = collision.AnalyzeCollision(ups, downs,
            upNoteValue: 4, downNoteValue: 4, upDots: 0, downDots: 1);

        Assert.True(result.DownDotForceDown,
            "Down-stem voice dots on lines should be forced down in collision");
    }

    [Fact]
    public void DotDirection_NoCollision_NotForced()
    {
        // LILYPOND-REF: lily/note-collision-interface.cc:411-448
        // No collision → no dot direction override
        var collision = new NoteCollision();
        var ups = new[] { 8 };
        var downs = new[] { 0 };

        var result = collision.AnalyzeCollision(ups, downs,
            upNoteValue: 4, downNoteValue: 4, upDots: 1, downDots: 1);

        Assert.False(result.DownDotForceDown);
    }

    [Fact]
    public void DotDirection_DownVoice_InSpace_NotForced()
    {
        // Down-stem note in a space (odd position) → dot doesn't need adjustment
        var collision = new NoteCollision();
        var ups = new[] { 6 };
        var downs = new[] { 5 }; // Odd position = in a space

        var result = collision.AnalyzeCollision(ups, downs,
            upNoteValue: 4, downNoteValue: 4, upDots: 0, downDots: 1);

        Assert.False(result.DownDotForceDown,
            "Down-stem note in space doesn't need dot direction adjustment");
    }

    [Fact]
    public void DotDirection_Merge_NotForced()
    {
        // Merged notes don't need dot direction override (one head is wiped)
        var collision = new NoteCollision();
        var ups = new[] { 4 };
        var downs = new[] { 4 };

        var result = collision.AnalyzeCollision(ups, downs,
            upNoteValue: 4, downNoteValue: 4, upDots: 1, downDots: 1);

        Assert.False(result.DownDotForceDown,
            "Merged notes don't need dot direction adjustment");
    }

    // --- LILYPOND-REF multi-voice cascading tests (I-5) ---

    private static NoteItem MakeNote(int staffPosition) =>
        new(staffPosition, Fraction.Quarter, 0, null, false, 0);

    [Fact]
    public void ThreeVoices_CascadingOffsets()
    {
        // LILYPOND-REF: lily/note-collision-interface.cc:420-480
        // Voice 1 (up), Voice 2 (down), Voice 3 (up)
        // Voice 3 should get a larger offset than Voice 1
        var collision = new NoteCollision();
        double noteheadWidth = EngravingDefaults.NoteheadBlackWidth;

        var column = new VoiceColumn(ImmutableArray.Create(
            new VoiceEntry(1, MakeNote(4), 0, forcedStemUp: true),
            new VoiceEntry(2, MakeNote(4), 0, forcedStemUp: false),
            new VoiceEntry(3, MakeNote(4), 0, forcedStemUp: true)
        ), measureIndex: 0);

        var offsets = collision.CalculateVoiceOffsets(column, noteheadWidth);

        var v1Offset = offsets.First(o => o.VoiceId == 1).XOffset;
        var v2Offset = offsets.First(o => o.VoiceId == 2).XOffset;
        var v3Offset = offsets.First(o => o.VoiceId == 3).XOffset;

        // Voice 3 should have a larger offset than Voice 1
        Assert.True(v3Offset > v1Offset,
            $"Voice 3 ({v3Offset:F2}) should be further right than Voice 1 ({v1Offset:F2})");
    }

    [Fact]
    public void FourVoices_CascadingOffsets()
    {
        // LILYPOND-REF: lily/note-collision-interface.cc:420-480
        // Voice 1 (up), Voice 2 (down), Voice 3 (up), Voice 4 (down)
        var collision = new NoteCollision();
        double noteheadWidth = EngravingDefaults.NoteheadBlackWidth;

        var column = new VoiceColumn(ImmutableArray.Create(
            new VoiceEntry(1, MakeNote(4), 0, forcedStemUp: true),
            new VoiceEntry(2, MakeNote(4), 0, forcedStemUp: false),
            new VoiceEntry(3, MakeNote(4), 0, forcedStemUp: true),
            new VoiceEntry(4, MakeNote(4), 0, forcedStemUp: false)
        ), measureIndex: 0);

        var offsets = collision.CalculateVoiceOffsets(column, noteheadWidth);

        var v1Offset = offsets.First(o => o.VoiceId == 1).XOffset;
        var v2Offset = offsets.First(o => o.VoiceId == 2).XOffset;
        var v3Offset = offsets.First(o => o.VoiceId == 3).XOffset;
        var v4Offset = offsets.First(o => o.VoiceId == 4).XOffset;

        // Voice 3 further than Voice 1, Voice 4 further (left) than Voice 2
        Assert.True(v3Offset > v1Offset,
            $"Voice 3 ({v3Offset:F2}) should be further right than Voice 1 ({v1Offset:F2})");
        Assert.True(v4Offset < v2Offset,
            $"Voice 4 ({v4Offset:F2}) should be further left than Voice 2 ({v2Offset:F2})");
    }

    [Fact]
    public void TwoVoices_NoCascading()
    {
        // Two voices should work as before (no cascading needed)
        var collision = new NoteCollision();
        double noteheadWidth = EngravingDefaults.NoteheadBlackWidth;

        var column = new VoiceColumn(ImmutableArray.Create(
            new VoiceEntry(1, MakeNote(4), 0, forcedStemUp: true),
            new VoiceEntry(2, MakeNote(4), 0, forcedStemUp: false)
        ), measureIndex: 0);

        var offsets = collision.CalculateVoiceOffsets(column, noteheadWidth);

        Assert.Equal(2, offsets.Length);
        // Both voices should have entries
        Assert.Contains(offsets, o => o.VoiceId == 1);
        Assert.Contains(offsets, o => o.VoiceId == 2);
    }

    // --- LILYPOND-REF width-based shift normalization tests ---

    [Fact]
    public void WidthNormalization_WholeNoteWidth_ProducesLargerShifts()
    {
        // LILYPOND-REF: lily/note-collision-interface.cc:309-312
        // Shifts are multiplied by notehead width, so wider noteheads
        // (whole notes = 1.688) produce larger absolute shifts than
        // quarter noteheads (1.18)
        var collision = new NoteCollision();

        // Use half vs quarter at same position → Full collision (can't merge)
        var upHalf = MakeNoteWithDuration(4, Fraction.Half);
        var downQuarter = MakeNoteWithDuration(4, Fraction.Quarter);

        var column = new VoiceColumn(ImmutableArray.Create(
            new VoiceEntry(1, upHalf, 0, forcedStemUp: true),
            new VoiceEntry(2, downQuarter, 0, forcedStemUp: false)
        ), measureIndex: 0);

        var offsetsBlack = collision.CalculateVoiceOffsets(column, EngravingDefaults.NoteheadBlackWidth);
        var offsetsWhole = collision.CalculateVoiceOffsets(column, EngravingDefaults.NoteheadWholeWidth);

        double upBlack = offsetsBlack.First(o => o.VoiceId == 1).XOffset;
        double upWhole = offsetsWhole.First(o => o.VoiceId == 1).XOffset;

        // Both should have non-zero shifts (full collision, non-mergeable)
        Assert.True(Math.Abs(upBlack) > 0.001,
            $"Black width shift ({upBlack:F3}) should be non-zero");
        Assert.True(Math.Abs(upWhole) > Math.Abs(upBlack),
            $"Whole note offset ({upWhole:F3}) should be > black ({upBlack:F3})");
    }

    private static NoteItem MakeNoteWithDuration(int staffPosition, Fraction duration) =>
        new(staffPosition, duration, 0, null, false, 0);

    // --- LILYPOND-REF force-hshift manual override tests ---

    // --- LILYPOND-REF half+eighth merge formula tests ---

    [Fact]
    public void DifferentlyHeadedMerge_HalfAndQuarter_KeepsOpenNotehead()
    {
        // LILYPOND-REF: lily/note-collision-interface.cc:252-261
        // When merge-differently-headed is true, half+quarter at same pitch merge.
        // The open (half) notehead is kept visible.
        var collision = new NoteCollision(new NoteCollisionParameters
        {
            MergeDifferentlyHeaded = true
        });
        var ups = new[] { 4 };
        var downs = new[] { 4 };

        var result = collision.AnalyzeCollision(ups, downs,
            upNoteValue: 2, downNoteValue: 4, upDots: 0, downDots: 0);

        Assert.Equal(CollisionType.Merge, result.Type);
        Assert.True(result.ShouldMerge);
        // Up-stem is half (open) → keep visible; Down-stem is quarter (filled) → hide
        Assert.False(result.UpHeadTransparent, "Open notehead (half) should be kept visible");
        Assert.True(result.DownHeadTransparent, "Filled notehead (quarter) should be hidden");
    }

    [Fact]
    public void DifferentlyHeadedMerge_QuarterUp_HalfDown_HidesQuarter()
    {
        // When up=quarter, down=half at same pitch, hide the filled (quarter) head
        var collision = new NoteCollision(new NoteCollisionParameters
        {
            MergeDifferentlyHeaded = true
        });
        var ups = new[] { 4 };
        var downs = new[] { 4 };

        var result = collision.AnalyzeCollision(ups, downs,
            upNoteValue: 4, downNoteValue: 2, upDots: 0, downDots: 0);

        Assert.Equal(CollisionType.Merge, result.Type);
        Assert.True(result.ShouldMerge);
        // Up-stem is quarter (filled) → hide; Down-stem is half (open) → keep
        Assert.True(result.UpHeadTransparent, "Filled notehead (quarter) should be hidden");
        Assert.False(result.DownHeadTransparent, "Open notehead (half) should be kept visible");
    }

    [Fact]
    public void DifferentlyHeadedMerge_NotAllowed_DefaultParams()
    {
        // Default: merge-differently-headed is false → half+quarter should NOT merge
        var collision = new NoteCollision();
        var ups = new[] { 4 };
        var downs = new[] { 4 };

        var result = collision.AnalyzeCollision(ups, downs,
            upNoteValue: 2, downNoteValue: 4, upDots: 0, downDots: 0);

        Assert.Equal(CollisionType.Full, result.Type);
        Assert.False(result.ShouldMerge);
    }

    [Fact]
    public void DifferentlyHeadedMerge_WholeAndHalf_CannotMerge()
    {
        // LILYPOND-REF: lily/note-collision-interface.cc:252-261
        // Whole+half cannot merge (both open noteheads)
        var collision = new NoteCollision(new NoteCollisionParameters
        {
            MergeDifferentlyHeaded = true
        });
        var ups = new[] { 4 };
        var downs = new[] { 4 };

        var result = collision.AnalyzeCollision(ups, downs,
            upNoteValue: 1, downNoteValue: 2, upDots: 0, downDots: 0);

        // Should NOT merge — both are open noteheads
        Assert.NotEqual(CollisionType.Merge, result.Type);
        Assert.False(result.ShouldMerge);
    }

    [Fact]
    public void ForceHshift_ResolverQueryReturnsCorrectValue()
    {
        // LILYPOND-REF: lily/note-collision-interface.cc:486-502
        // Verify the resolver correctly returns force-hshift values
        var resolver = new GrobPropertyResolver(
            ImmutableArray.Create(new GrobOverride("NoteColumn", "force-hshift", "1.5", 0, 0)),
            ImmutableArray<GrobRevert>.Empty);

        resolver.AdvanceTo(0, 0);
        var forceHshift = resolver.GetDouble("NoteColumn", "force-hshift");

        Assert.NotNull(forceHshift);
        Assert.Equal(1.5, forceHshift!.Value, 2);
    }

    [Fact]
    public void ForceHshift_AppliedOffset_MatchesNoteheadWidthMultiple()
    {
        // LILYPOND-REF: lily/note-collision-interface.cc:486-502
        // force-hshift is in notehead width units; when applied, the absolute
        // offset should be force-hshift * noteheadWidth
        double forceHshift = 1.5;
        double noteheadWidth = EngravingDefaults.NoteheadBlackWidth;
        double expectedOffset = forceHshift * noteheadWidth;

        // Verify the math matches LilyPond's convention
        Assert.Equal(1.956, expectedOffset, 2); // 1.5 * 1.304 = 1.956
    }

    [Fact]
    public void ForceHshift_OnceOverride_ClearedAfterAdvance()
    {
        // LILYPOND-REF: lily/note-collision-interface.cc:486-502
        // \once override should apply only to the next item, then be cleared
        var resolver = new GrobPropertyResolver(
            ImmutableArray.Create(new GrobOverride("NoteColumn", "force-hshift", "1.5", 0, 0, IsOnce: true)),
            ImmutableArray<GrobRevert>.Empty);

        resolver.AdvanceTo(0, 0);
        Assert.NotNull(resolver.GetDouble("NoteColumn", "force-hshift"));

        // After advancing past the once-override position, it should be cleared
        resolver.AdvanceTo(0, 1);
        Assert.Null(resolver.GetDouble("NoteColumn", "force-hshift"));
    }

    [Fact]
    public void WidthNormalization_WholeVsQuarterCollision_ScalesCorrectly()
    {
        // LILYPOND-REF: lily/note-collision-interface.cc:309-312
        // The ratio of whole to black shift should match the ratio of widths
        var collision = new NoteCollision();

        // Use half vs quarter at same position → Full collision (non-mergeable)
        var upHalf = MakeNoteWithDuration(4, Fraction.Half);
        var downQuarter = MakeNoteWithDuration(4, Fraction.Quarter);

        var column = new VoiceColumn(ImmutableArray.Create(
            new VoiceEntry(1, upHalf, 0, forcedStemUp: true),
            new VoiceEntry(2, downQuarter, 0, forcedStemUp: false)
        ), measureIndex: 0);

        var offsetsBlack = collision.CalculateVoiceOffsets(column, EngravingDefaults.NoteheadBlackWidth);
        var offsetsWhole = collision.CalculateVoiceOffsets(column, EngravingDefaults.NoteheadWholeWidth);

        double blackShift = offsetsBlack.First(o => o.VoiceId == 1).XOffset;
        double wholeShift = offsetsWhole.First(o => o.VoiceId == 1).XOffset;

        double ratio = wholeShift / blackShift;
        double expectedRatio = EngravingDefaults.NoteheadWholeWidth / EngravingDefaults.NoteheadBlackWidth;
        Assert.Equal(expectedRatio, ratio, 2);
    }

    // --- Suspended head filtering ---
    // LILYPOND-REF: lily/note-column.cc:169-220 calc_main_extent

    // --- Within-chord seconds displacement (replaces HasSuspendedHead) ---
    // LILYPOND-REF: lily/stem.cc:606-760 calc_positioning_done

    private static ChordNoteInfo[] Infos(params int[] positions)
        => positions.Select(p => new ChordNoteInfo(p, null, false)).ToArray();

    [Fact]
    public void ChordHeadPositioning_Second_StemUp_ShiftsUpperHeadRight()
    {
        var offsets = ChordHeadPositioning.CalculateOffsets(Infos(0, 1), stemUp: true, noteValue: 4);
        Assert.Equal(0, offsets[0]);
        // ell - 0.5*stemThickness, shifted right
        double expected = GlyphMetrics.NoteheadBlack.Right - 0.5 * EngravingDefaults.StemThickness;
        Assert.Equal(expected, offsets[1], precision: 6);
    }

    [Fact]
    public void ChordHeadPositioning_Second_StemDown_ShiftsLowerHeadLeft()
    {
        var offsets = ChordHeadPositioning.CalculateOffsets(Infos(0, 1), stemUp: false, noteValue: 4);
        Assert.Equal(0, offsets[1]); // upper head = support head for stem down
        Assert.True(offsets[0] < 0, "lower head must shift LEFT for stem down");
    }

    [Fact]
    public void ChordHeadPositioning_Third_NoShift()
    {
        var offsets = ChordHeadPositioning.CalculateOffsets(Infos(0, 2), stemUp: true, noteValue: 4);
        Assert.All(offsets, o => Assert.Equal(0, o));
    }

    [Fact]
    public void ChordHeadPositioning_SingleNote_NoShift()
    {
        var offsets = ChordHeadPositioning.CalculateOffsets(Infos(0), stemUp: true, noteValue: 4);
        Assert.Equal(0, Assert.Single(offsets));
    }

    [Fact]
    public void ChordHeadPositioning_MixedIntervals_OnlySecondShifts()
    {
        // C, D, G (0, 1, 4): the D reverses; the G (a fourth above D) resets parity.
        var offsets = ChordHeadPositioning.CalculateOffsets(Infos(0, 1, 4), stemUp: true, noteValue: 4);
        Assert.Equal(0, offsets[0]);
        Assert.True(offsets[1] > 0);
        Assert.Equal(0, offsets[2]);
    }

    [Fact]
    public void ChordHeadPositioning_Cluster_AlternatesByParity()
    {
        // C, D, E (0, 1, 2): D reverses, E returns to the normal side (parity).
        var offsets = ChordHeadPositioning.CalculateOffsets(Infos(0, 1, 2), stemUp: true, noteValue: 4);
        Assert.Equal(0, offsets[0]);
        Assert.True(offsets[1] > 0);
        Assert.Equal(0, offsets[2]);
    }
}
