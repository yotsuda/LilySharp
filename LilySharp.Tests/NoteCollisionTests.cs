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
using LilySharp.Core.Syntax;
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
        // ⚠️ The pin is the LEFTMOST group, not "the down group" — here the up group's offset
        // is the NEGATIVE one, so left_most is IT and the down group is what moves right.
        // The opposite-signed case is Crossing_PinsDownVoice_ShiftsUpVoiceRight_TheWayLeftMostDoes.
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
    public void Crossing_PinsDownVoice_ShiftsUpVoiceRight_TheWayLeftMostDoes()
    {
        // LILYPOND-REF: lily/note-collision.cc:441,462-463 — left_most starts at 0.0 and
        // only a NEGATIVE amount moves it, with no branch on collision type. The down
        // group's amount is the negative one, so the DOWN voice is the one that stays and
        // the up voice comes right to meet it, by twice the shift.
        //
        // MEASURED (LilyPond 2.26.0, twin of scratch/ベースタブLy/collision.lys and the same
        // book with the up voice's note replaced by a rest): the down head is drawn at
        // x=24.0881 in BOTH, so the collision does not move it; the up head appears at
        // 24.3489, i.e. 0.2608 = 2 x 0.1 x 1.3042 to its right.
        //
        // ⚠️ THIS TEST USED TO ASSERT THE OPPOSITE, and said so: the up voice was pinned
        // and the down voice moved left. That was not a reading of LilyPond — it kept the
        // (frequently beamed) upper voice on its column X because the beam frame did not
        // carry the collision shift, so a shifted beamed voice drew a skewed beam. The
        // frame carries it now, so the workaround is gone and this asserts the port.
        var collision = new NoteCollision();
        double w = EngravingDefaults.NoteheadWholeWidth;

        var column = new VoiceColumn(ImmutableArray.Create(
            new VoiceEntry(1, MakeNoteWithDuration(1, Fraction.Quarter), 0, forcedStemUp: true),
            new VoiceEntry(2, MakeNoteWithDuration(6, Fraction.Whole), 0, forcedStemUp: false)
        ), measureIndex: 0);

        var offsets = collision.CalculateVoiceOffsets(column);

        double up = offsets.First(o => o.VoiceId == 1).XOffset;
        double down = offsets.First(o => o.VoiceId == 2).XOffset;

        Assert.Equal(2 * 0.17 * w, up, 2);   // up-stem voice comes RIGHT
        Assert.Equal(0.0, down, 3);          // down-stem voice is the pin
        // The separation is the half this change does NOT touch: it was right before and
        // must be right after, or the pin has been confused with the shift.
        Assert.Equal(2 * 0.17 * w, up - down, 2);
    }

    // --- LILYPOND-REF head wipe conformance tests (I-2) ---

    [Fact]
    public void HeadWipe_EqualHeadedMerge_WipesNothing()
    {
        // LILYPOND-REF: lily/note-collision.cc:276-289 — equal ball types with equal
        // visible dot counts never assign wipe_ball: BOTH heads stay live and print
        // coincident (collision-seconds.ly, measured 2026-08-07: each merged unison is
        // two half-head paths at one coordinate in LilyPond's own SVG).
        // ⚠️ This test USED to pin the opposite ("down-stem notehead is wiped"), citing
        // :381-407 — a range inside calc_positioning_done with no wipe code. The wipe
        // it pinned dropped a whole down-stem chord in collision-seconds.ly m1p2.
        var collision = new NoteCollision();
        var ups = new[] { 4 };
        var downs = new[] { 4 };

        var result = collision.AnalyzeCollision(ups, downs, upNoteValue: 4, downNoteValue: 4, upDots: 0, downDots: 0);

        Assert.Equal(CollisionType.Merge, result.Type);
        Assert.True(result.ShouldMerge);
        Assert.False(result.DownHeadTransparent, "equal-headed merge leaves both heads live");
        Assert.False(result.UpHeadTransparent, "equal-headed merge leaves both heads live");
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
    public void HeadWipe_EqualHeadedChordMerge_WipesNothing()
    {
        // LILYPOND-REF: lily/note-collision.cc:276-289 — same as the single-note case:
        // an identical-chord merge keeps every head live (the coinciding pairs overlap).
        // ⚠️ Used to pin "all overlapping down-stem heads wiped" against :381-407, which
        // holds no wipe code.
        var collision = new NoteCollision();
        var ups = new[] { 4, 6 };
        var downs = new[] { 4, 6 };

        var result = collision.AnalyzeCollision(ups, downs, upNoteValue: 4, downNoteValue: 4, upDots: 0, downDots: 0);

        Assert.Equal(CollisionType.Merge, result.Type);
        Assert.True(result.ShouldMerge);
        Assert.False(result.DownHeadTransparent);
        Assert.False(result.UpHeadTransparent);
    }

    // --- collision-seconds.ly conformance: classification reads NORMAL-side heads only ---
    // LILYPOND-REF: lily/note-collision.cc:77-87 — check_meshing_chords re-reads the head
    // positions with filter=true before deciding merge and collision class; the heads a
    // within-chord second reverses to the far side of the stem drop out. AnalyzeCollision
    // derives those sets itself (from the positions, direction and head glyph), so each
    // case below exercises the derivation AND the classification. All staff positions
    // and expected shifts are LilyPond 2.26.0 measurements of
    // input/regression/collision-seconds.ly (2026-08-07, SVG head X to 4 decimals).

    [Fact]
    public void SuspendedHeads_TakeNoPartInClassification_UnisonPairMerges()
    {
        // m1p1  << <a' b>2 \\ <g a> >> : the full sets make phantom seconds (a-b, g-a),
        // but the normal-side sets are both {a} — a plain full collide, so the pair
        // MERGES: shift 0, and LilyPond prints both half heads at literally one
        // coordinate (x 17.6332). Classifying with the full sets sent this to the
        // 0.52 close-half branch and spread the columns a full head apart.
        var collision = new NoteCollision();
        var result = collision.AnalyzeCollision(
            new[] { -1, 0 }, new[] { -2, -1 },
            upNoteValue: 2, downNoteValue: 2, upDots: 0, downDots: 0);

        Assert.Equal(CollisionType.Merge, result.Type);
        Assert.Equal(0.0, result.UpStemXOffset, 6);
        Assert.False(result.UpHeadTransparent);
        Assert.False(result.DownHeadTransparent);
    }

    [Fact]
    public void SuspendedHeads_TakeNoPartInClassification_DistantHalf()
    {
        // m1p2  << <a b d e>2 \\ <a b> >> : full sets fully collide (a and b in both),
        // but the normal sides are {a, d} against {b} — a lone distant-half second, so
        // the up group moves right 0.4 (LP columns 1.1019 = 2 × 0.4 × 1.3774 apart).
        // The old whole-set classification called this a MERGE and the old merge wipe
        // then dropped both of the down chord's heads from the page (59 heads vs LP's 61).
        var collision = new NoteCollision();
        var result = collision.AnalyzeCollision(
            new[] { -1, 0, 2, 3 }, new[] { -1, 0 },
            upNoteValue: 2, downNoteValue: 2, upDots: 0, downDots: 0);

        Assert.False(result.ShouldMerge);
        Assert.Equal(0.4, result.UpStemXOffset, 6);
        Assert.Equal(-0.4, result.DownStemXOffset, 6);
    }

    [Fact]
    public void SuspendedHeads_TakeNoPartInClassification_Meshing()
    {
        // m4p1  << <f g c>2 \\ <g a e'> >> : the full sets share g (a full collide plus
        // a phantom second), the normal sides {f, c} vs {a, e} interleave without ever
        // coming within a second — the plain meshing case, 0.17
        // (LP columns 0.4683 = 2 × 0.17 × 1.3774 apart).
        var collision = new NoteCollision();
        var result = collision.AnalyzeCollision(
            new[] { -3, -2, 1 }, new[] { -2, -1, 3 },
            upNoteValue: 2, downNoteValue: 2, upDots: 0, downDots: 0);

        Assert.Equal(CollisionType.Meshing, result.Type);
        Assert.Equal(0.17, result.UpStemXOffset, 6);
    }

    [Fact]
    public void SuspendedHeads_TakeNoPartInClassification_CloseHalf()
    {
        // m5p2  << <f g c d>2 \\ <g a b> >> : normal sides {f, c} vs {g, b} — a distant
        // half (f under g) AND a close half (c over b), which :191 promotes to full,
        // and close-half wins the multiplier chain: 0.52
        // (LP columns 1.4325 = 2 × 0.52 × 1.3774 apart).
        var collision = new NoteCollision();
        var result = collision.AnalyzeCollision(
            new[] { -3, -2, 1, 2 }, new[] { -2, -1, 0 },
            upNoteValue: 2, downNoteValue: 2, upDots: 0, downDots: 0);

        Assert.Equal(CollisionType.CloseHalf, result.Type);
        Assert.Equal(0.52, result.UpStemXOffset, 6);
    }

    [Fact]
    public void VoiceOffsets_ChordEntries_DeriveTheNormalSideThemselves()
    {
        // End-to-end m1p1 and m1p2 through CalculateVoiceOffsets with real ChordItems:
        // the normal-side sets come out of ChordHeadPositioning (the one house that
        // knows which head a second reverses), not from a second spelling of the rule.
        var collision = new NoteCollision();

        // m1p1: merged — both columns pinned at the slot, nobody wiped.
        var m1p1 = new VoiceColumn(ImmutableArray.Create(
            new VoiceEntry(1, MakeChord(Fraction.Half, -1, 0), 0, forcedStemUp: true),
            new VoiceEntry(2, MakeChord(Fraction.Half, -2, -1), 0, forcedStemUp: false)
        ), measureIndex: 0);
        var offsets = collision.CalculateVoiceOffsets(m1p1);
        Assert.All(offsets, o => Assert.Equal(0.0, o.XOffset, 6));
        Assert.All(offsets, o => Assert.False(o.HeadTransparent));

        // m1p2: distant half — up group right by 2 × 0.4 × half-head width, and the
        // down chord's heads STAY (the old merge wipe deleted them).
        var m1p2 = new VoiceColumn(ImmutableArray.Create(
            new VoiceEntry(1, MakeChord(Fraction.Half, -1, 0, 2, 3), 0, forcedStemUp: true),
            new VoiceEntry(2, MakeChord(Fraction.Half, -1, 0), 0, forcedStemUp: false)
        ), measureIndex: 0);
        offsets = collision.CalculateVoiceOffsets(m1p2);
        double up = offsets.First(o => o.VoiceId == 1).XOffset;
        double down = offsets.First(o => o.VoiceId == 2).XOffset;
        Assert.Equal(2 * 0.4 * GlyphMetrics.NoteheadHalf.Width, up, 4);
        Assert.Equal(0.0, down, 6);
        Assert.All(offsets, o => Assert.False(o.HeadTransparent));
    }

    private static ChordItem MakeChord(Fraction baseDuration, params int[] staffPositions) =>
        new(staffPositions.Select(p => new ChordNoteInfo(p, null, false)).ToImmutableArray(),
            baseDuration, 0, 0);

    // --- LILYPOND-REF dot direction / dot side-support tests (I-4) ---
    // LILYPOND-REF: lily/note-collision.cc:352-397 check_meshing_chords — the dot
    // rules read the SIGN of the finished shift. Measured against
    // dot-column-note-collision.ly (audit/lp-regression/lys twin).

    [Fact]
    public void DotDirection_NegativeShift_NoUpForce_ButDownDotsTakeSupport()
    {
        // A second is a touch → the DOWN group moves right (negative shift), which is
        // exactly where the :374 direction rule does NOT fire; the :359 support arm
        // (its guard is the else of the up arm, not a shift test) still runs.
        var collision = new NoteCollision();
        var result = collision.AnalyzeCollision(new[] { 5 }, new[] { 4 },
            upNoteValue: 4, downNoteValue: 4, upDots: 0, downDots: 1);

        Assert.False(result.DownDotDirectionUp,
            "a negative shift must not set the down dots' direction");
        Assert.True(result.DownDotsAvoidUpHeads);
        Assert.False(result.UpDotsAvoidDownHeads);
    }

    [Fact]
    public void DotDirection_PositiveShift_SetsDownDotsUp()
    {
        // A voice crossing (up group BELOW the down group) is the meshing fallback:
        // the up group moves right (positive shift) and the down head is dotted —
        // dot-column-note-collision.ly m3: LilyPond prints b'4.'s dot in the space
        // ABOVE its line (direction UP), not below.
        var collision = new NoteCollision();
        var result = collision.AnalyzeCollision(new[] { -3, -2 }, new[] { 0, 1 },
            upNoteValue: 4, downNoteValue: 4, upDots: 0, downDots: 1);

        Assert.Equal(CollisionType.Meshing, result.Type);
        Assert.True(result.UpStemXOffset > 0);
        Assert.True(result.DownDotDirectionUp);
        Assert.True(result.DownDotsAvoidUpHeads);
    }

    [Fact]
    public void DotDirection_NegativeShift_UpDots_SupportAgainstDownHead()
    {
        // A second whose dotted up head sits ON a line keeps the touch branch (the
        // up-dots escape needs !upOnLine), so the DOWN group moves right — the up
        // group "ended up on the left" and its dots take support against the down
        // group's first head (:352-358); the down arm is the ELSE and must not run.
        var collision = new NoteCollision();
        var result = collision.AnalyzeCollision(new[] { 4 }, new[] { 3 },
            upNoteValue: 4, downNoteValue: 4, upDots: 1, downDots: 0);

        Assert.True(result.UpStemXOffset < -1e-6, "the touch branch moves the down group right");
        Assert.True(result.UpDotsAvoidDownHeads);
        Assert.False(result.DownDotsAvoidUpHeads);
        Assert.False(result.DownDotDirectionUp);
    }

    [Fact]
    public void DotDirection_NoCollision_NoAdjustments()
    {
        // Too far apart → NoCollision carries no dot adjustments at all.
        var collision = new NoteCollision();
        var result = collision.AnalyzeCollision(new[] { 8 }, new[] { 0 },
            upNoteValue: 4, downNoteValue: 4, upDots: 1, downDots: 1);

        Assert.False(result.DownDotDirectionUp);
        Assert.False(result.DownDotsAvoidUpHeads);
        Assert.False(result.UpDotsAvoidDownHeads);
    }

    [Fact]
    public void DotDirection_Merge_NoAdjustments()
    {
        // The merge early-return skips the dot blocks (see the note at the return):
        // a default-switch merge has EQUAL dots and coincident heads, so its dot
        // columns coincide too.
        var collision = new NoteCollision();
        var result = collision.AnalyzeCollision(new[] { 4 }, new[] { 4 },
            upNoteValue: 4, downNoteValue: 4, upDots: 1, downDots: 1);

        Assert.False(result.DownDotDirectionUp);
        Assert.False(result.DownDotsAvoidUpHeads);
        Assert.False(result.UpDotsAvoidDownHeads);
    }

    [Fact]
    public void VoiceOffsets_DotColumnMinX_ClearsTheUpVoiceHeads()
    {
        // dot-column-note-collision.ly m3 moment 1, end to end:
        // << <f' g'>4 \\ <b' c''>4. >> — the up voice crosses BELOW the dotted down
        // voice. LilyPond pushes the down chord's whole dot column right of the up
        // voice's suspended g' head: measured dot X − down-head X = 4.50
        // (= up shift 1.50 + reversed-head offset + head ink right + one dot width).
        var collision = new NoteCollision();
        var column = new VoiceColumn(ImmutableArray.Create(
            new VoiceEntry(1, MakeDottedChord(Fraction.Quarter, 1, -3, -2), 0, forcedStemUp: true),
            new VoiceEntry(2, MakeDottedChord(Fraction.Quarter, 1, 0, 1), 0, forcedStemUp: false)
        ), measureIndex: 0);

        var offsets = collision.CalculateVoiceOffsets(column);
        var up = offsets.First(o => o.VoiceId == 1);
        var down = offsets.First(o => o.VoiceId == 2);

        Assert.True(down.Dot.DirectionUp);
        Assert.NotNull(down.Dot.ColumnMinX);
        // The dot column starts one dot width right of the up voice's rightmost head
        // ink: upX + reversed-head offset + head ink right + dot width. Verified
        // against LilyPond: dots land 4.49/4.50 right of the down chord's head.
        var headBox = GlyphMetrics.GetNoteheadBBox(4);
        var upNotes = new[] { -3, -2 }
            .Select(p => new ChordNoteInfo(p, null, false)).ToImmutableArray();
        double flip = ChordHeadPositioning.CalculateOffsets(upNotes, true, 4).Max();
        double expected = up.XOffset + flip + headBox.Right + GlyphMetrics.AugmentationDot.Width;
        Assert.Equal(expected, down.Dot.ColumnMinX!.Value, 6);
        // Pinned against the render: dots land 2.99 right of the up voice's root head
        // (LilyPond 34.877 − 31.884 = 2.99), i.e. 4.49 right of the down chord's
        // REVERSED b' head, which sits a flip offset left of its column.
        Assert.Equal(2.99, down.Dot.ColumnMinX!.Value - up.XOffset, 1);
        // ONE dot column per staff moment — Dot_column_engraver lives in the Staff
        // context, so the up voice's dots share the down voice's column X.
        // LILYPOND-REF: ly/engraver-init.ly:73 Staff context — \consists Dot_column_engraver
        Assert.NotNull(up.Dot.ColumnMinX);
        Assert.Equal(down.Dot.ColumnMinX!.Value, up.Dot.ColumnMinX!.Value, 6);
    }

    [Fact]
    public void VoiceOffsets_DotColumns_ShareOneStaffColumn_WithoutAnyCollision()
    {
        // dots.ly m5: << <b c'>4. \\ <a, b,>4. >> — the voices are far apart (no
        // collision, no offsets), yet LilyPond prints ALL FOUR dots in ONE column:
        // the down voice's dots move right to clear the up chord's suspended c'
        // head, because the Dot_column_engraver lives in the STAFF context and
        // collects the whole moment. Measured (dots.ly): every dot 4.93 right of
        // the preceding bar line, in both voices.
        // LILYPOND-REF: ly/engraver-init.ly:73 Staff context — \consists Dot_column_engraver
        var collision = new NoteCollision();
        var column = new VoiceColumn(ImmutableArray.Create(
            new VoiceEntry(1, MakeDottedChord(Fraction.Quarter, 1, 0, 1), 0, forcedStemUp: true),
            new VoiceEntry(2, MakeDottedChord(Fraction.Quarter, 1, -8, -7), 0, forcedStemUp: false)
        ), measureIndex: 0);

        var offsets = collision.CalculateVoiceOffsets(column);
        var up = offsets.First(o => o.VoiceId == 1);
        var down = offsets.First(o => o.VoiceId == 2);

        // Far apart: nobody shifts.
        Assert.Equal(0.0, up.XOffset, 6);
        Assert.Equal(0.0, down.XOffset, 6);

        // The shared column X clears the up chord's reversed second (flip offset +
        // head ink right + one dot width) — for BOTH voices' dots.
        var headBox = GlyphMetrics.GetNoteheadBBox(4);
        double flip = ChordHeadPositioning.CalculateOffsets(
            new[] { 0, 1 }.Select(p => new ChordNoteInfo(p, null, false)).ToImmutableArray(),
            true, 4).Max();
        double expected = flip + headBox.Right + GlyphMetrics.AugmentationDot.Width;
        Assert.Equal(expected, up.Dot.ColumnMinX!.Value, 6);
        Assert.Equal(expected, down.Dot.ColumnMinX!.Value, 6);
    }

    private static ChordItem MakeDottedChord(Fraction baseDuration, int dots, params int[] staffPositions) =>
        new(staffPositions.Select(p => new ChordNoteInfo(p, null, false)).ToImmutableArray(),
            baseDuration, dots, 0);

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
            ImmutableArray.Create(new GrobOverride("NoteColumn", "force-hshift", new LysValue.Real(1.5), 0, 0)),
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
            ImmutableArray.Create(new GrobOverride("NoteColumn", "force-hshift", new LysValue.Real(1.5), 0, 0, IsOnce: true)),
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
