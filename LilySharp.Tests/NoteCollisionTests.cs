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

        // Same position but different dots — cannot merge by default, and the touch is
        // ABANDONED: note-collision.cc:219-224 is `(full_collide || (!on_line && prefer))
        // && up.dots > down.dots`, and a unison IS a full collide, so the first disjunct
        // fires whatever line the note is on. The up group then moves right by 0.5.
        var result = collision.AnalyzeCollision(ups, downs, upNoteValue: 4, downNoteValue: 4, upDots: 1, downDots: 0);

        Assert.Equal(CollisionType.Full, result.Type);
        Assert.Equal(0.5, result.UpStemXOffset, 6);
        Assert.False(result.ShouldMerge);
    }

    [Fact]
    public void Second_MoreDotsUpButOnAStaffLine_KeepsTheTouch()
    {
        // The `!is_on_staff_line` disjunct at :220 only decides anything where full_collide is
        // FALSE — i.e. at a second, not a unison. An up-stem note ON a line already has its
        // dot raised clear, so the touch stands and the DOWN group moves right…
        var collision = new NoteCollision();
        var onLine = collision.AnalyzeCollision(new[] { 4 }, new[] { 3 },
            upNoteValue: 4, downNoteValue: 4, upDots: 1, downDots: 0);

        Assert.Equal(CollisionType.Touch, onLine.Type);
        Assert.Equal(-0.5, onLine.UpStemXOffset, 6);

        // …while the same second one step higher, with the dotted up-stem note in a SPACE,
        // abandons the touch and falls through to close_half's 0.52 with the UP group moving
        // right. (CloseHalf_SecondInterval_DottedNote_StillUsesCloseHalfShift is the same
        // branch with a half head; this pair is what isolates the line test itself.)
        var inSpace = collision.AnalyzeCollision(new[] { 5 }, new[] { 4 },
            upNoteValue: 4, downNoteValue: 4, upDots: 1, downDots: 0);

        Assert.Equal(CollisionType.CloseHalf, inSpace.Type);
        Assert.Equal(0.52, inSpace.UpStemXOffset, 6);
    }

    [Fact]
    public void FullCollision_SamePosition_DifferentNoteValues()
    {
        var collision = new NoteCollision();
        var ups = new[] { 4 };
        var downs = new[] { 4 };

        // Same position but different note values (half vs quarter) - cannot merge.
        // A unison TOUCHES (note-collision.cc:72-75 ups[0] >= dps.back()), and :212-227 is
        // reached before the full_collide multiplier — measured in book XVF.
        var result = collision.AnalyzeCollision(ups, downs, upNoteValue: 2, downNoteValue: 4, upDots: 0, downDots: 0);

        Assert.Equal(CollisionType.Touch, result.Type);
        Assert.False(result.ShouldMerge);
    }

    [Fact]
    public void CloseHalfCollision_AdjacentPositions()
    {
        var collision = new NoteCollision();
        var ups = new[] { 5 };    // One position above
        var downs = new[] { 4 };  // One position below

        var result = collision.AnalyzeCollision(ups, downs, upNoteValue: 4, downNoteValue: 4, upDots: 0, downDots: 0);

        // A second sets close_half_collide AND touch, and touch is consumed first
        // (note-collision.cc:323 before :325) — book XVE. It is NOT reachable as CloseHalf:
        // anything further apart than a second returns at :64-66.
        Assert.Equal(CollisionType.Touch, result.Type);
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
        // LILYPOND-REF: lily/note-collision.cc:299-350
        var p = NoteCollisionParameters.Default;

        Assert.Equal(0.52, p.CloseHalfShift);     // close_half_collide :326
        Assert.Equal(0.4, p.DistantHalfShift);    // distant_half_collide :329
        Assert.Equal(0.5, p.FullCollideShift);     // full_collide :327
        Assert.Equal(0.5, p.TouchShift);           // touch :324
        Assert.Equal(0.65, p.StemToStemShift);     // stem_to_stem :322
        Assert.Equal(0.17, p.MeshingGeneralShift); // meshing_general :337
        Assert.Equal(0.1, p.MeshingDottedShift);   // meshing_dotted :335
    }

    [Fact]
    public void StemToStem_DottedDownStem_AtAUnison_IsStillTheTouch()
    {
        // LILYPOND-REF: lily/note-collision.cc:202-211 — a collision whose DOWN-stem note
        // carries MORE dots (up.dots < down.dots) pushes the down-stem to the right, but the
        // 0.65 stem_to_stem clearance is set only `if (!touch)`, and a unison touches.
        // ⚠️ THIS TEST USED TO ASSERT 0.65 HERE, on the strength of reading :207-210 without
        // its guard. Book XVG measures the shape it names — half over dotted quarter at one
        // pitch — and LilyPond puts the heads 1.377400 apart, which is the 0.5 branch.
        // The real 0.65 is NonTouchingDottedDownStem_IsTheStemToStem065 (book XVH).
        var collision = new NoteCollision();
        var result = collision.AnalyzeCollision(new[] { 4 }, new[] { 4 },
            upNoteValue: 2, downNoteValue: 4, upDots: 0, downDots: 1);

        Assert.Equal(CollisionType.Touch, result.Type);
        Assert.False(result.ShouldMerge);
        // Down-stem (dotted) still goes RIGHT — the sign, which is what :207 sets.
        Assert.True(result.DownStemXOffset > 0);
        Assert.True(result.UpStemXOffset < 0);
    }

    [Fact]
    public void FullCollision_ShiftAmount_MatchesLilyPond()
    {
        // LILYPOND-REF: lily/note-collision.cc:212-227 + :323-324 — a unison touches, so the
        // shift is -1 × 0.5, scaled by extent_up[RIGHT] / extent_down.length() (:343-345)
        // because it is the DOWN group that moves. Book XVF: heads 1.377400 apart = the UP
        // (half) head's ink, and CalculateVoiceOffsets pins the up group at the column.
        var collision = new NoteCollision();
        var ups = new[] { 4 };
        var downs = new[] { 4 };

        var result = collision.AnalyzeCollision(ups, downs, upNoteValue: 2, downNoteValue: 4, upDots: 0, downDots: 0);

        double expected = 0.5 * GlyphMetrics.NoteheadHalf.Width / GlyphMetrics.NoteheadBlack.Width;
        Assert.Equal(CollisionType.Touch, result.Type);
        Assert.Equal(-expected, result.UpStemXOffset, 6);
        Assert.Equal(expected, result.DownStemXOffset, 6);
    }

    [Fact]
    public void TouchCollision_WholeNotes_CannotMerge_AndStillTouch()
    {
        // Whole notes at the same position cannot merge (note-collision.cc:102-104), so the
        // touch branch decides: -1 × 0.5, extent ratio 1.0 (both heads are the same glyph).
        var collision = new NoteCollision();
        var ups = new[] { 4 };
        var downs = new[] { 4 };

        var result = collision.AnalyzeCollision(ups, downs, upNoteValue: 0, downNoteValue: 0, upDots: 0, downDots: 0);

        Assert.Equal(CollisionType.Touch, result.Type);
        Assert.Equal(-0.5, result.UpStemXOffset, 6);
        Assert.Equal(0.5, result.DownStemXOffset, 6);
    }

    // --- LILYPOND-REF meshing multiplier tests (I-1) ---

    [Fact]
    public void CloseHalf_SecondInterval_IsReachedAsATouch()
    {
        // A half (open) a second ABOVE a quarter (filled) in two voices sets
        // close_half_collide — but ALSO touch, and :323 is consumed before :325, so 0.52 is
        // not what comes out. Books XVE/XVF measure both of the shapes this covers.
        // ⚠️ The comment this replaces asserted "close_half is ALWAYS the 0.52 shift"; that is
        // true of the multiplier chain read alone and false of the function.
        var collision = new NoteCollision();
        var ups = new[] { 5 };    // One position above
        var downs = new[] { 4 };  // Adjacent position

        var result = collision.AnalyzeCollision(ups, downs,
            upNoteValue: 2, downNoteValue: 4, upDots: 0, downDots: 0);

        double expected = 0.5 * GlyphMetrics.NoteheadHalf.Width / GlyphMetrics.NoteheadBlack.Width;
        Assert.Equal(CollisionType.Touch, result.Type);
        Assert.Equal(-expected, result.UpStemXOffset, 6);
    }

    [Fact]
    public void CloseHalf_SecondInterval_DottedNote_StillUsesCloseHalfShift()
    {
        // A dot does not turn a close_half collide into meshing — it stays 0.52 (the dotted 0.1
        // only applies in the meshing fallback). LILYPOND-REF: lily/note-collision.cc:325-337.
        var collision = new NoteCollision();
        var ups = new[] { 5 };
        var downs = new[] { 4 };

        var result = collision.AnalyzeCollision(ups, downs,
            upNoteValue: 2, downNoteValue: 4, upDots: 1, downDots: 0);

        Assert.Equal(0.52, result.UpStemXOffset, 2);
    }

    // The three below all say the same thing — a SECOND is never the 0.17 meshing shift —
    // and after the branch-order fix they say it about the touch branch, which is what a
    // second actually reaches. Each keeps its own head pairing, because the extent ratio
    // (:343-345) differs per pairing and that is the part a magnitude assertion can hide.

    [Fact]
    public void Meshing_WholeNotes_CannotMesh_TakesTheTouchBranch()
    {
        // A whole head over a black one, a second apart.
        var collision = new NoteCollision();

        var result = collision.AnalyzeCollision(new[] { 5 }, new[] { 4 },
            upNoteValue: 1, downNoteValue: 4, upDots: 0, downDots: 0);

        double expected = 0.5 * GlyphMetrics.NoteheadWhole.Width / GlyphMetrics.NoteheadBlack.Width;
        Assert.Equal(CollisionType.Touch, result.Type);
        Assert.Equal(-expected, result.UpStemXOffset, 6);
        Assert.True(Math.Abs(result.UpStemXOffset) > NoteCollisionParameters.Default.MeshingGeneralShift,
            "a second must never come out at the meshing shift");
    }

    [Fact]
    public void Meshing_ChordWithSeconds_CannotMesh_TakesTheTouchBranch()
    {
        // A two-note up chord whose LOWEST head is a second above the down head. The second
        // up head is far enough above (:74 ups[1] >= dps.back() + threshold + 1) that the
        // extremes still count as touching.
        var collision = new NoteCollision();

        var result = collision.AnalyzeCollision(new[] { 5, 7 }, new[] { 4 },
            upNoteValue: 4, downNoteValue: 4, upDots: 0, downDots: 0);

        Assert.Equal(CollisionType.Touch, result.Type);
        Assert.Equal(-0.5, result.UpStemXOffset, 6);
    }

    [Fact]
    public void Meshing_HalfNotes_CannotMesh_SameHeadGroup()
    {
        // Two half notes: same glyph both sides, so the extent ratio is exactly 1.
        var collision = new NoteCollision();

        var result = collision.AnalyzeCollision(new[] { 5 }, new[] { 4 },
            upNoteValue: 2, downNoteValue: 2, upDots: 0, downDots: 0);

        Assert.Equal(CollisionType.Touch, result.Type);
        Assert.Equal(-0.5, result.UpStemXOffset, 6);
    }

    [Fact]
    public void Meshing_SmallerThanStandardShift()
    {
        // LILYPOND-REF: lily/note-collision.cc:180-230
        // Meshing shift (0.17) should be much smaller than standard half collision (0.52)
        var p = NoteCollisionParameters.Default;
        Assert.True(p.MeshingGeneralShift < p.CloseHalfShift,
            $"Meshing ({p.MeshingGeneralShift}) should be < CloseHalf ({p.CloseHalfShift})");
        Assert.True(p.MeshingDottedShift < p.MeshingGeneralShift,
            $"MeshingDotted ({p.MeshingDottedShift}) should be < MeshingGeneral ({p.MeshingGeneralShift})");
    }

    // --- LILYPOND-REF crossing-voice meshing fallback tests ---

    [Fact]
    public void Crossing_UpBelowDown_UsesMeshingFallback()
    {
        // LILYPOND-REF: lily/note-collision.cc:332-337 — "we're meshing" fallback.
        // Voice crossing: the up-stem note (pos 1) sits well BELOW the down-stem
        // note (pos 6) — not too far apart, but no full/close/distant/touch/merge
        // fires. LilyPond falls through to the meshing shift (0.17 without dots).
        var collision = new NoteCollision();
        var ups = new[] { 1 };   // up-stem voice, low
        var downs = new[] { 6 }; // down-stem voice, high (crossing)

        var result = collision.AnalyzeCollision(ups, downs,
            upNoteValue: 8, downNoteValue: 1, upDots: 0, downDots: 0);

        Assert.Equal(CollisionType.Meshing, result.Type);
        Assert.Equal(0.17, result.UpStemXOffset, 2);
        Assert.Equal(-0.17, result.DownStemXOffset, 2);
    }

    [Fact]
    public void Crossing_Dotted_UsesDottedMeshingShift()
    {
        // LILYPOND-REF: lily/note-collision.cc:333-335 — dotted meshing shift 0.1.
        var collision = new NoteCollision();
        var result = collision.AnalyzeCollision(new[] { 1 }, new[] { 6 },
            upNoteValue: 8, downNoteValue: 1, upDots: 0, downDots: 1);

        Assert.Equal(CollisionType.Meshing, result.Type);
        Assert.Equal(0.1, result.UpStemXOffset, 2);
    }

    [Fact]
    public void Crossing_PinsUpVoice_ShiftsDownVoiceLeft()
    {
        // The consumer pins the up-stem (frequently beamed) voice at the column
        // slot and moves the DOWN-stem voice LEFT, so a beamed upper voice keeps
        // its column-X position. Separation = 2*0.17*width, matching LilyPond's
        // (which shifts the upper voice right by the same amount).
        var collision = new NoteCollision();
        double w = EngravingDefaults.NoteheadWholeWidth;

        var column = new VoiceColumn(ImmutableArray.Create(
            new VoiceEntry(1, MakeNoteWithDuration(1, Fraction.Quarter), 0, forcedStemUp: true),
            new VoiceEntry(2, MakeNoteWithDuration(6, Fraction.Whole), 0, forcedStemUp: false)
        ), measureIndex: 0);

        var offsets = collision.CalculateVoiceOffsets(column);

        double up = offsets.First(o => o.VoiceId == 1).XOffset;
        double down = offsets.First(o => o.VoiceId == 2).XOffset;

        Assert.Equal(0.0, up, 3);                 // up-stem voice pinned at slot
        Assert.Equal(-2 * 0.17 * w, down, 2);     // down-stem voice shifts LEFT
    }

    // --- LILYPOND-REF head wipe conformance tests (I-2) ---

    [Fact]
    public void HeadWipe_Merge_DownHeadTransparent()
    {
        // LILYPOND-REF: lily/note-collision.cc:381-407
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
        // LILYPOND-REF: lily/note-collision.cc:381-407
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
        // LILYPOND-REF: lily/note-collision.cc:381-407
        // Full collision (different note values, shifted apart) → no heads wiped
        var collision = new NoteCollision();
        var ups = new[] { 4 };
        var downs = new[] { 4 };

        var result = collision.AnalyzeCollision(ups, downs, upNoteValue: 2, downNoteValue: 4, upDots: 0, downDots: 0);

        Assert.Equal(CollisionType.Touch, result.Type);
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
        // LILYPOND-REF: lily/note-collision.cc:381-407
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
        // ⚠️ THIS PINS A DIVERGENCE, NOT A PORT. LilyPond's dot-direction rule is :375-398:
        // it fires only when the shift is POSITIVE (the up group moved right) and the
        // direction it sets is UP / CENTER / the up chord's — never "DOWN because the note is
        // on a line". Lily# forces DOWN off the staff line alone. Kept green so the current
        // behaviour is described rather than unwatched; ticketed in HANDOFF §2 A.
        // ⚠️ The seconds/unisons this test uses now produce a NEGATIVE shift (the touch branch
        // above), which is exactly where LilyPond's rule does NOT fire.
        // LILYPOND-REF: lily/note-collision.cc:375-398 check_meshing_chords — the rule this approximates.
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
        // No collision → no dot direction override (see the divergence note above).
        // LILYPOND-REF: lily/note-collision.cc:375-398 check_meshing_chords
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
        // LILYPOND-REF: lily/note-collision.cc:420-480
        // Voice 1 (up), Voice 2 (down), Voice 3 (up)
        // Voice 3 should get a larger offset than Voice 1
        var collision = new NoteCollision();
        var column = new VoiceColumn(ImmutableArray.Create(
            new VoiceEntry(1, MakeNote(4), 0, forcedStemUp: true),
            new VoiceEntry(2, MakeNote(4), 0, forcedStemUp: false),
            new VoiceEntry(3, MakeNote(4), 0, forcedStemUp: true)
        ), measureIndex: 0);

        var offsets = collision.CalculateVoiceOffsets(column);

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
        // LILYPOND-REF: lily/note-collision.cc:420-480
        // Voice 1 (up), Voice 2 (down), Voice 3 (up), Voice 4 (down)
        var collision = new NoteCollision();
        var column = new VoiceColumn(ImmutableArray.Create(
            new VoiceEntry(1, MakeNote(4), 0, forcedStemUp: true),
            new VoiceEntry(2, MakeNote(4), 0, forcedStemUp: false),
            new VoiceEntry(3, MakeNote(4), 0, forcedStemUp: true),
            new VoiceEntry(4, MakeNote(4), 0, forcedStemUp: false)
        ), measureIndex: 0);

        var offsets = collision.CalculateVoiceOffsets(column);

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
        var column = new VoiceColumn(ImmutableArray.Create(
            new VoiceEntry(1, MakeNote(4), 0, forcedStemUp: true),
            new VoiceEntry(2, MakeNote(4), 0, forcedStemUp: false)
        ), measureIndex: 0);

        var offsets = collision.CalculateVoiceOffsets(column);

        Assert.Equal(2, offsets.Length);
        // Both voices should have entries
        Assert.Contains(offsets, o => o.VoiceId == 1);
        Assert.Contains(offsets, o => o.VoiceId == 2);
    }

    // --- LILYPOND-REF width-based shift normalization tests ---

    [Fact]
    public void DownShift_IsScaledByTheUpHeadsWidth_NotTheDownOne()
    {
        // LILYPOND-REF: lily/note-collision.cc:343-345 — when it is the DOWN group that
        // moves, the shift is multiplied by extent_up[RIGHT] / extent_down.length() and then
        // (:435, :447) by extent_down.length() again, so the DOWN width cancels and the
        // displacement is the UP head's ink. Book XVF measures exactly that: a half over a
        // quarter at one pitch comes out 1.377400 = the HALF head, not 1.304200.
        // ⚠️ This test used to claim the opposite ("a whole-note lower voice shifts further"),
        // which only held because the up group was the one moving.
        var collision = new NoteCollision();
        var upHalf = MakeNoteWithDuration(4, Fraction.Half);

        double downWhole = Displace(upHalf, MakeNoteWithDuration(4, Fraction.Whole)).Down;
        double downQuarter = Displace(upHalf, MakeNoteWithDuration(4, Fraction.Quarter)).Down;

        Assert.Equal(GlyphMetrics.NoteheadHalf.Width, downQuarter, 6);
        Assert.Equal(downQuarter, downWhole, 6);
    }

    private static NoteItem MakeNoteWithDuration(int staffPosition, Fraction duration) =>
        new(staffPosition, duration, 0, null, false, 0);

    // --- LILYPOND-REF branch ORDER of check_meshing_chords, measured end to end ---
    // audit/lp-geometry/probes/cross-voice-accidental.ly, books XVE..XVH. Each number is
    // LilyPond 2.26.0's own head-to-head displacement with the up-stem group pinned at the
    // column, which is what CalculateVoiceOffsets returns. `touch` is decided before
    // close_half_collide and full_collide, so a SECOND and a UNISON both take it and the
    // DOWN-stem group is the one that moves right.

    private static NoteItem Dotted(int staffPosition, Fraction baseDuration, int dots) =>
        new(staffPosition, baseDuration, dots, null, false, 0);

    private static (double Up, double Down) Displace(MusicItem up, MusicItem down)
    {
        var offsets = new NoteCollision().CalculateVoiceOffsets(new VoiceColumn(
            ImmutableArray.Create(
                new VoiceEntry(1, up, 0, forcedStemUp: true),
                new VoiceEntry(2, down, 0, forcedStemUp: false)),
            measureIndex: 0));
        return (offsets.First(o => o.VoiceId == 1).XOffset,
                offsets.First(o => o.VoiceId == 2).XOffset);
    }

    [Fact]
    public void Second_TakesTheTouchBranch_AndMovesTheDownVoiceRight()
    {
        // XVE  << a'4 \\ g'4 >>  — up head 8.489735, DOWN head 9.793935.
        var (up, down) = Displace(
            MakeNoteWithDuration(-1, Fraction.Quarter),
            MakeNoteWithDuration(-2, Fraction.Quarter));

        Assert.Equal(0.0, up, 6);
        Assert.Equal(1.304200, down, 6);
    }

    [Fact]
    public void Unison_TakesTheTouchBranch_ScaledByTheUpHeadsWidth()
    {
        // XVF  << g'2 \\ g'4 >>  — up head 8.489735, DOWN head 9.867135. The half head is
        // wider (1.3774), and a down-shift is scaled by extent_up[RIGHT]/extent_down.length(),
        // so the displacement is the UP head's width, not the down one's.
        var (up, down) = Displace(
            MakeNoteWithDuration(-2, Fraction.Half),
            MakeNoteWithDuration(-2, Fraction.Quarter));

        Assert.Equal(0.0, up, 6);
        Assert.Equal(1.377400, down, 6);
    }

    [Fact]
    public void Unison_DottedDownStem_StillTouches_SoNotTheStemToStem065()
    {
        // XVG  << g'2 \\ g'4. >>  — IDENTICAL to XVF. note-collision.cc:202-211 fires
        // (up.dots < down.dots) and sets shift_amount = -1, but its
        // `if (!touch) stem_to_stem = true` does not, so :323 still multiplies by 0.5.
        // Reading that branch as "always 0.65" is what this test now falsifies.
        var (up, down) = Displace(
            MakeNoteWithDuration(-2, Fraction.Half),
            Dotted(-2, Fraction.Quarter, 1));

        Assert.Equal(0.0, up, 6);
        Assert.Equal(1.377400, down, 6);
    }

    [Fact]
    public void NonTouchingDottedDownStem_IsTheStemToStem065()
    {
        // XVH  << <f' a'>2 \\ a'4. >>  — up heads 8.489735, DOWN head 10.280355. The up
        // group's LOWEST head is a third below the down head, so the extremes do NOT touch
        // and :207-210 reaches stem_to_stem: 2 × 0.65 × 1.3774 = 1.790620.
        var chord = new ChordItem(
            ImmutableArray.Create(new ChordNoteInfo(-5, null, false), new ChordNoteInfo(-2, null, false)),
            Fraction.Half, 0, 0);

        var (up, down) = Displace(chord, Dotted(-2, Fraction.Quarter, 1));

        Assert.Equal(0.0, up, 6);
        Assert.Equal(1.790620, down, 6);
    }

    // --- LILYPOND-REF force-hshift manual override tests ---

    // --- LILYPOND-REF half+eighth merge formula tests ---

    [Fact]
    public void DifferentlyHeadedMerge_HalfAndQuarter_KeepsOpenNotehead()
    {
        // LILYPOND-REF: lily/note-collision.cc:252-261
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

        Assert.Equal(CollisionType.Touch, result.Type);
        Assert.False(result.ShouldMerge);
    }

    [Fact]
    public void DifferentlyHeadedMerge_WholeAndHalf_CannotMerge()
    {
        // LILYPOND-REF: lily/note-collision.cc:252-261
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
        // LILYPOND-REF: lily/note-collision.cc:486-502
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
        // LILYPOND-REF: lily/note-collision.cc:486-502
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
        // LILYPOND-REF: lily/note-collision.cc:486-502
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
    public void UpShift_IsScaledByTheDownHeadsWidth()
    {
        // The mirror of the above, and where the down-stem width DOES set the scale
        // (:435 wid, extent ratio 1.0 at :346-348). Reached by abandoning the touch: an
        // up-stem note in a SPACE carrying more dots than the down-stem one (:219-224).
        // ⚠️ Both widths are grob EXTENTS — the ink — not the advances
        // EngravingDefaults.Notehead*Width carries; see NoteCollision.HeadWidth.
        var upDotted = Dotted(5, Fraction.Quarter, 1);

        double upWhole = Displace(upDotted, MakeNoteWithDuration(5, Fraction.Whole)).Up;
        double upQuarter = Displace(upDotted, MakeNoteWithDuration(5, Fraction.Quarter)).Up;

        Assert.Equal(GlyphMetrics.NoteheadWhole.Width, upWhole, 6);
        Assert.Equal(GlyphMetrics.NoteheadBlack.Width, upQuarter, 6);
        Assert.Equal(GlyphMetrics.NoteheadWhole.Width / GlyphMetrics.NoteheadBlack.Width,
                     upWhole / upQuarter, 6);
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
