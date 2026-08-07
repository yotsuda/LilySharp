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
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Result of note collision analysis.
/// </summary>
public enum CollisionType
{
    /// <summary>Notes are far apart, no collision.</summary>
    None,

    /// <summary>Notes touch at extreme positions (can share stem line).</summary>
    Touch,

    /// <summary>Adjacent notes on neighboring staff positions.</summary>
    CloseHalf,

    /// <summary>Notes overlap but can be merged (same pitch).</summary>
    Merge,

    /// <summary>Full collision requiring horizontal shift.</summary>
    Full,

    /// <summary>
    /// Voice crossing: an up-stem note sits more than a threshold BELOW the
    /// down-stem note, so the up-stem's stem would pierce the down-stem head.
    /// LilyPond falls through to its "meshing" shift here.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/note-collision.cc:332-337</remarks>
    Meshing
}

/// <summary>
/// Information about a collision between two note columns.
/// </summary>
internal sealed record NoteCollisionInfo
{
    /// <summary>Type of collision detected.</summary>
    public CollisionType Type { get; }

    /// <summary>X offset for the up-stem column (positive = right).</summary>
    public double UpStemXOffset { get; }

    /// <summary>X offset for the down-stem column (positive = right).</summary>
    public double DownStemXOffset { get; }

    /// <summary>Whether notes should be merged (drawn as one).</summary>
    public bool ShouldMerge { get; }

    /// <summary>
    /// Whether the up-stem notehead should be hidden (head wipe).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-collision.cc:254-318
    /// Set only when a merge crosses a dot or head-type difference (the
    /// merge-differently-* switches); a plain equal-headed merge wipes nothing.
    /// </remarks>
    public bool UpHeadTransparent { get; }

    /// <summary>
    /// Whether the down-stem notehead should be hidden (head wipe).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-collision.cc:254-318
    /// Set only when a merge crosses a dot or head-type difference; a plain
    /// equal-headed merge draws BOTH heads coincident (wipe_ball stays null).
    /// </remarks>
    public bool DownHeadTransparent { get; }

    /// <summary>
    /// Whether the down-stem voice's dots take direction UP: fires only when the up
    /// group moved right (a POSITIVE shift) and the down head is dotted.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-collision.cc:374-397 check_meshing_chords — "In meshed
    ///   chords with dots on the left, adjust dot direction"; the direction set is UP.
    /// The CENTER arm (both voices' dots sharing one DotColumn) has no Lily# counterpart —
    /// dots resolve per item, there is no cross-voice column — and the conform arm reads
    /// the up dots' direction property, which the voice props set to UP for an up voice
    /// (direction-polyphonic-grobs), so it lands where the plain arm does.
    /// </remarks>
    public bool DownDotDirectionUp { get; }

    /// <summary>
    /// Whether the down-stem voice's dot column must clear EVERY head on the up stem
    /// (suspended right-side heads included).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-collision.cc:352-372 check_meshing_chords — "If any
    ///   dotted notes ended up on the left, tell the Dot_Columnn to avoid the note
    ///   heads on the right"; the down branch runs whenever the down head is dotted
    ///   (its guard is the else of the up branch, not a shift test).
    /// </remarks>
    public bool DownDotsAvoidUpHeads { get; }

    /// <summary>
    /// Whether the up-stem voice's dot column must clear the down group's first head —
    /// the up group moved LEFT (negative shift) and its head is dotted.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-collision.cc:352-358 check_meshing_chords — the
    ///   <c>shift_amount &lt; -1e-6</c> branch supports against <c>fh_down</c> alone.
    /// </remarks>
    public bool UpDotsAvoidDownHeads { get; }

    public NoteCollisionInfo(CollisionType type, double upStemXOffset, double downStemXOffset,
        bool shouldMerge = false, bool upHeadTransparent = false, bool downHeadTransparent = false,
        bool downDotDirectionUp = false, bool downDotsAvoidUpHeads = false,
        bool upDotsAvoidDownHeads = false)
    {
        Type = type;
        UpStemXOffset = upStemXOffset;
        DownStemXOffset = downStemXOffset;
        ShouldMerge = shouldMerge;
        UpHeadTransparent = upHeadTransparent;
        DownHeadTransparent = downHeadTransparent;
        DownDotDirectionUp = downDotDirectionUp;
        DownDotsAvoidUpHeads = downDotsAvoidUpHeads;
        UpDotsAvoidDownHeads = upDotsAvoidDownHeads;
    }

    public static NoteCollisionInfo NoCollision { get; } = new(CollisionType.None, 0, 0);
}

/// <summary>
/// The dot-column adjustments a two-voice collision imposes on ONE voice item: a
/// preferred direction for its dots (note-collision.cc:374-397) and/or a minimum X for
/// its dot column (:352-372), in the staff column's frame — the frame the collision
/// XOffsets live in. <c>default</c> means "no adjustment".
/// </summary>
internal readonly record struct DotAdjustment(bool DirectionUp, double? ColumnMinX);

/// <summary>
/// Parameters for note collision handling.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/note-collision.cc:40-401 check_meshing_chords()
/// LILYPOND-REF: scm/define-grobs.scm:2548 NoteCollision
///
/// (The class name is Note_collision_interface, but its implementation lives in
/// lily/note-collision.cc — there is no note-collision-interface.cc file.)
///
/// IMPLEMENTED — shift multipliers now match LilyPond:
///   stem_to_stem = 0.65, close_half = 0.52,
///   full_collide = 0.5, distant_half = 0.4
/// IMPLEMENTED — meshing multipliers:
///   meshing_dotted = 0.1, meshing_general = 0.17
/// IMPLEMENTED — merge head wipe (note-collision.cc:254-318). ⚠️ Equal ball types with
///   equal dots wipe NOTHING (:276-289 leaves wipe_ball null — both heads print
///   coincident); this line used to say "merge hides the overlapping notehead", and the
///   code wiped the whole down-stem ITEM, silently dropping a chord (collision-seconds.ly
///   m1p2). Wipes only fire across a dot/head-type difference, i.e. only under the
///   non-default merge-differently-* switches. dot_wipe_head is NOT ported (no channel).
/// IMPLEMENTED — suspended-head filter (note-collision.cc:77-87 + stem.cc:303-345
///   note_head_positions): merge and collision classification read only the heads on the
///   normal side of each stem; too-far (:64-66) and touch (:68-75) read the full set.
///   Classifying with the full set made every reversed low head a phantom second —
///   all six wrong shifts of collision-seconds.ly came from this one omission.
/// IMPLEMENTED — width-based shift normalization (note-collision.cc:427-437 and :447 in
///   calc_positioning_done — NOT automatic_shift, which this line used to name)
/// NOT YET IMPLEMENTED — the half+eighth merge shift (note-collision.cc:305-307: a merge whose
///   DOWN head is a half and UP head an eighth or shorter keeps a nonzero
///   <c>(1 - extent_up[RIGHT]/extent_down[RIGHT]) * 0.5</c>). ComputeMergeInfo returns 0 for
///   every merge. ⚠️ THIS LINE SAID "IMPLEMENTED", and cited :91-176, which is the collision
///   detection and holds no such formula — two errors in one claim. Unreachable at default
///   parameters (it needs merge-differently-headed), which is why nothing caught it.
/// NOT YET IMPLEMENTED — FA-shaped notehead handling (note-collision.cc:237-252 fa_styles)
/// IMPLEMENTED — dot direction (:374-397: fires on a POSITIVE shift, sets UP; the CENTER
///   arm needs a cross-voice DotColumn Lily# does not have) and the dot side supports
///   (:352-372: dotted heads' dot column clears the opposite group's heads; the push is
///   computed in CalculateVoiceOffsets and applied by the renderer with one-dot-width
///   padding, scm/define-grobs.scm DotColumn pad-by-one-dot-width). Measured against
///   dot-column-note-collision.ly. ⚠️ Both replaced a DIVERGENCE that forced DOWN off
///   the staff line alone; an earlier form of this line cited ":263-337 dot_wipe_head",
///   which is a local variable in the MERGE branch, not this mechanism.
///   ⚠️ The SPACING side does not know the push yet: a pushed dot column widens the
///   drawn ink but not the reserved column (no corpus book binds there — the pushed
///   dots of dot-column-note-collision.ly sit in slack; wire it when a book pins it).
/// IMPLEMENTED — force-hshift manual override (note-collision.cc:608-624 forced_shift)
/// IMPLEMENTED — within-chord seconds displacement (stem.cc:606-760) in ChordHeadPositioning
/// IMPLEMENTED — automatic_shift's group loop (note-collision.cc:504-599), ported clause
///   for clause in CalculateVoiceOffsets. ⚠️ It used to be a flat "+1 head width per later
///   same-direction voice" cascade wearing this same citation; the third voice of a
///   `voice { } { } { }` came out at double LilyPond's measured shift (+1.3042 vs +0.652,
///   scratch/ベースタブLy/Untitled-4.lys) until it was replaced with the literal clauses.
/// </remarks>
internal sealed record NoteCollisionParameters
{
    public static NoteCollisionParameters Default { get; } = new();

    /// <summary>Threshold for considering notes as colliding (staff positions).</summary>
    public int CollisionThreshold { get; init; } = 1;

    /// <summary>Whether to merge differently dotted notes.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grob-properties.scm:754 merge-differently-dotted</remarks>
    public bool MergeDifferentlyDotted { get; init; } = false;

    /// <summary>Whether to merge differently headed notes (half vs quarter).</summary>
    /// <remarks>LILYPOND-REF: scm/define-grob-properties.scm:760 merge-differently-headed</remarks>
    public bool MergeDifferentlyHeaded { get; init; } = false;

    /// <summary>Whether to prefer dotted notes on the right side.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:2554 prefer-dotted-right</remarks>
    public bool PreferDottedRight { get; init; } = true;

    /// <summary>
    /// Horizontal shift amount for close half collision (in notehead widths).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-collision.cc:325-326 close_half_collide
    /// </remarks>
    public double CloseHalfShift { get; init; } = 0.52;

    /// <summary>
    /// Horizontal shift amount for distant half collision (in notehead widths).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-collision.cc:329-330 distant_half_collide
    /// </remarks>
    public double DistantHalfShift { get; init; } = 0.4;

    /// <summary>
    /// Horizontal shift amount for full collision (in notehead widths).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-collision.cc:327-328 full_collide
    /// </remarks>
    public double FullCollideShift { get; init; } = 0.5;

    /// <summary>
    /// Horizontal shift amount for touch condition (in notehead widths).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-collision.cc:324 touch — multiplier 0.5.
    /// (0.65 is the separate stem_to_stem case at :322 — see StemToStemShift.)
    /// </remarks>
    public double TouchShift { get; init; } = 0.5;

    /// <summary>
    /// Horizontal shift when a dotted down-stem chord is forced to the right
    /// (prefer-dotted-right) and the stems must clear each other.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-collision.cc:321-322 stem_to_stem — multiplier 0.65.
    /// Set (note-collision.cc:207-210) when a full/half collide with up.dots &lt; down.dots
    /// pushes the down-stem right and it is not a touch; takes precedence over the
    /// full/close/distant multipliers.
    /// </remarks>
    public double StemToStemShift { get; init; } = 0.65;

    /// <summary>
    /// Horizontal shift for meshing seconds (general case, no dots involved).
    /// Much smaller than CloseHalfShift because noteheads interlock tightly.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-collision.cc:180-230 check_meshing_chords()
    /// Value 0.17 means noteheads overlap significantly when they mesh (interlock).
    /// </remarks>
    public double MeshingGeneralShift { get; init; } = 0.17;

    /// <summary>
    /// Horizontal shift for meshing seconds when dots are present.
    /// Slightly smaller than MeshingGeneralShift because the dot
    /// extends the notehead boundary.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-collision.cc:180-230 check_meshing_chords()
    /// </remarks>
    public double MeshingDottedShift { get; init; } = 0.1;
}

/// <summary>
/// Handles note collisions in multi-voice contexts.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/note-collision.cc:1-635 Note_collision_interface
/// </remarks>
internal sealed class NoteCollision
{
    private readonly NoteCollisionParameters _params;

    public NoteCollision(NoteCollisionParameters? parameters = null)
    {
        _params = parameters ?? NoteCollisionParameters.Default;
    }

    /// <summary>
    /// Analyzes collision between two note columns (up-stem and down-stem). The
    /// NORMAL-side head sets that merge/classification read (LP's filter=true re-read)
    /// are derived internally from the given positions — callers cannot forget them.
    /// </summary>
    /// <param name="sameHeadStyle">Whether the two columns' first heads share one
    /// notehead style (LP compares <c>fh_up</c>/<c>fh_down</c> "style"). Not derivable
    /// from positions, so it stays a parameter; the default models the common case.</param>
    public NoteCollisionInfo AnalyzeCollision(
        IReadOnlyList<int> upStaffPositions,
        IReadOnlyList<int> downStaffPositions,
        int upNoteValue,
        int downNoteValue,
        int upDots,
        int downDots,
        bool sameHeadStyle = true)
    {
        if (upStaffPositions.Count == 0 || downStaffPositions.Count == 0)
            return NoteCollisionInfo.NoCollision;

        // Sort positions ascending (like Lilypond)
        var ups = upStaffPositions.OrderBy(p => p).ToList();
        var downs = downStaffPositions.OrderBy(p => p).ToList();

        int threshold = _params.CollisionThreshold;

        // LILYPOND-REF: note-collision.cc:64-66 — too far apart to collide.
        // ups[0] is lowest up-stem note, downs.Last() is highest down-stem note
        if (ups[0] > downs.Last() + threshold)
            return NoteCollisionInfo.NoCollision;

        // LILYPOND-REF: note-collision.cc:68-75 — the extreme noteheads just meet, so the
        // two stems can share one vertical line.
        bool touch = CheckTouch(ups, downs, threshold);

        // LILYPOND-REF: note-collision.cc:77-87 — from here on LilyPond RE-READS the head
        // positions with filter=true: heads reversed to the far side of their stem (the
        // 'o's in its picture) take no part in the merge decision or the collision
        // classification, because they already sit a head-width away from the opposite
        // voice. Derived HERE, from the same inputs, exactly as check_meshing_chords
        // derives its own filtered sets — it used to be an optional caller-supplied
        // parameter whose null default silently fell back to the full set, i.e. a future
        // caller forgetting it would have quietly restored the bug this fixes:
        // classifying with the full set put every merged pair of collision-seconds.ly on
        // the 0.52 close-half shift (the down chord's reversed low head formed a phantom
        // second against the up chord's main head).
        ups = NormalSideStaffPositions(upStaffPositions, stemUp: true, upNoteValue);
        downs = NormalSideStaffPositions(downStaffPositions, stemUp: false, downNoteValue);
        // Unreachable at present — the first (support) head of a column is never
        // reversed, so a filtered set is never empty — but LP's merge check (:91) also
        // guards `!ups.empty () && !dps.empty ()`, and the reads below index [0].
        if (ups.Count == 0 || downs.Count == 0)
            return NoteCollisionInfo.NoCollision;

        // Check for merge possibility
        bool mergePossible = CheckMergePossible(ups, downs, upNoteValue, downNoteValue, upDots, downDots);

        // LILYPOND-REF: note-collision.cc:94-97 — do not merge notes typeset in
        // different style.
        if (!sameHeadStyle)
            mergePossible = false;

        // Detect collision types
        var (closeHalf, distantHalf, fullCollide) = DetectCollisionTypes(ups, downs, threshold, ref mergePossible);

        // LILYPOND-REF: note-collision.cc:191-193
        fullCollide = fullCollide || (closeHalf && distantHalf) ||
                     (distantHalf && (upNoteValue <= 0 || downNoteValue <= 0));

        // The dot rules (direction and side supports) read the SIGN of the finished
        // shift, so they are computed at the bottom of this method, after the width
        // normalization — the same order check_meshing_chords runs them in.

        // ⚠️ THE ORDER BELOW IS LILYPOND'S AND IT IS THE WHOLE POINT. `touch` is decided
        // above and consumed at :212 and :323 — BEFORE close_half_collide (:325) and
        // full_collide (:327) — so a SECOND and a UNISON both take the touch branch, and the
        // one that moves right is the DOWN-stem voice. Lily# used to gate the touch branch on
        // "no full/close/distant collide", which made it unreachable for exactly those two
        // shapes (a second is always touch AND close_half; anything further apart returns at
        // :64-66) and sent them to the 0.52 close_half branch with the UP voice moving right.
        // MEASURED (audit/lp-geometry/probes/cross-voice-accidental.ly, LilyPond 2.26.0):
        //   XVE  << a' \\ g' >>            up head 8.489735 · DOWN head 9.793935  (+1.304200)
        //   XVF  << g'2 \\ g'4 >>          up head 8.489735 · DOWN head 9.867135  (+1.377400)
        //   XVG  << g'2 \\ g'4. >>         identical to XVF — :202-211 fires but its
        //                                  `if (!touch) stem_to_stem = true` does NOT
        //   XVH  << <e' g'>2 \\ g'4. >>    up heads 8.489735 · DOWN head 10.280355 (+1.790620)
        //                                  = 2 × 0.65 × 1.3774, the stem_to_stem branch, which
        //                                  is reachable only because these do NOT touch
        // Each of the four is the branch that produces it, so the order is pinned by
        // measurement and not by a reading of the C++.

        // LILYPOND-REF: note-collision.cc:200-201 — the sign carries the direction: a
        // POSITIVE shift moves the up-stem group right, a negative one the down-stem group.
        double shiftAmount = 1;
        bool stemToStem = false;

        if ((fullCollide || ((closeHalf || distantHalf) && _params.PreferDottedRight))
            && upDots < downDots)
        {
            // LILYPOND-REF: note-collision.cc:202-211 — right-hand heads hide dots, so the
            // MORE dotted (here down-stem) group goes right. The stems only need the extra
            // stem_to_stem clearance when they are not already sharing one line.
            shiftAmount = -1;
            if (!touch)
                stemToStem = true;
        }
        else if (touch)
        {
            // LILYPOND-REF: note-collision.cc:212-227 — the down-stem group goes right so the
            // stems line up, UNLESS the up-stem group is the more dotted one and its dot is
            // not already raised off a staff line, in which case the touch is abandoned and
            // the ordinary collide multipliers below decide.
            bool upOnLine = ups[0] % 2 == 0;
            if ((fullCollide || (!upOnLine && _params.PreferDottedRight)) && upDots > downDots)
                touch = false;
            else
                shiftAmount = -1;
        }

        // LILYPOND-REF: note-collision.cc:254-318 — a merge replaces the shift entirely and
        // wipes one of the two heads.
        //
        // This used to read `mergePossible && fullCollide && !closeHalf && !distantHalf`. The
        // extra conjuncts are not LilyPond's, and they are also redundant, which is why
        // dropping them changed nothing. Proof, since "probably unreachable" is not a reason
        // to remove a guard:
        //   mergePossible requires ups[0] >= downs[0] and ups.back() >= downs.back();
        //   reaching here requires ups[0] <= downs.back() + threshold (else :64-66 returned).
        //   • ups[0] < downs[0] — mergePossible is false by definition.
        //   • downs[0] <= ups[0] < downs.back() — DetectCollisionTypes' interleave test
        //     (`up > downs[0] && up < downs.back()`) clears mergePossible.
        //   • ups[0] == downs.back() (or == downs[0]) — an EQUAL pair, so fullCollide.
        //   • ups[0] == downs.back() + 1 — |diff| == threshold, so closeHalf, which clears
        //     mergePossible in the same step.
        // So mergePossible surviving implies fullCollide and no half-collide: the conjuncts
        // could never have excluded anything.
        // ⚠️ The dot support/direction blocks (:352-397) also run on LilyPond's merge
        // path (check_meshing_chords falls through); the early return skips them, so a
        // MERGED pair's dots take no support. A default-switch merge requires EQUAL dot
        // counts and prints the heads coincident, so their dot columns coincide too —
        // no observer distinguishes the two until a merge-differently-* producer lands.
        if (mergePossible)
            return ComputeMergeInfo(upNoteValue, downNoteValue, upDots, downDots);

        // LILYPOND-REF: note-collision.cc:319-337 — "these numbers are magic", in this order.
        CollisionType type;
        if (stemToStem)
        {
            shiftAmount *= _params.StemToStemShift;
            type = CollisionType.Full;
        }
        else if (touch)
        {
            shiftAmount *= _params.TouchShift;
            type = CollisionType.Touch;
        }
        else if (closeHalf)
        {
            shiftAmount *= _params.CloseHalfShift;
            type = CollisionType.CloseHalf;
        }
        else if (fullCollide)
        {
            shiftAmount *= _params.FullCollideShift;
            type = CollisionType.Full;
        }
        else if (distantHalf)
        {
            shiftAmount *= _params.DistantHalfShift;
            type = CollisionType.CloseHalf;
        }
        else
        {
            // The "we're meshing" fallback, reached only for a voice CROSSING: the up-stem
            // note sits more than a threshold BELOW the down-stem note, so none of
            // merge/touch/full/close/distant fired, yet the notes are not too far apart —
            // the up-stem's stem would pierce the down-stem head.
            shiftAmount *= upDots > 0 || downDots > 0
                ? _params.MeshingDottedShift
                : _params.MeshingGeneralShift;
            type = CollisionType.Meshing;
        }

        // LILYPOND-REF: note-collision.cc:339-348 — the displacement to clear a collision
        // depends on the widths of the heads on the interfering sides, and calc_positioning_done
        // then multiplies by the DOWN-stem head's width:
        //   down-stem right (negative): (extent_up[RIGHT]   - extent_down[LEFT]) / extent_down.length()
        //   up-stem right   (positive): (extent_down[RIGHT] - extent_up[LEFT])   / extent_down.length()
        // Every Lily# head anchors at its own left edge, so extent_up = [0, upW] and
        // extent_down = [0, downW]: the factor is upW/downW going left and exactly 1.0 going
        // right. XVF is that first case — 2 × 0.5 × (1.3774/1.3042) × 1.3042 = 1.377400.
        // Written as LilyPond writes it, with the LEFT edges kept in the expression even
        // though every Lily# head anchors at 0: collapsing the up-shift arm to a bare 1.0
        // hides which extents the formula reads, and the next head style to arrive with a
        // non-zero left bearing would then be wrong silently.
        double upLeft = 0.0, downLeft = 0.0;
        double upRight = HeadWidth(upNoteValue);
        double downRight = HeadWidth(downNoteValue);
        double downLength = downRight - downLeft;
        if (downLength > 1e-6)
            shiftAmount *= shiftAmount < 0
                ? (upRight - downLeft) / downLength      // down-stem shifts right
                : (downRight - upLeft) / downLength;     // up-stem shifts right

        // LILYPOND-REF: lily/note-collision.cc:352-372 check_meshing_chords — "If any
        // dotted notes ended up on the left, tell the Dot_Columnn to avoid the note
        // heads on the right": a left-shifted dotted UP head supports against the down
        // group's first head; otherwise a dotted DOWN head supports against EVERY head
        // on the up stem (the down arm is the ELSE of the up arm — no shift test).
        bool upDotsAvoidDown = shiftAmount < -1e-6 && upDots > 0;
        bool downDotsAvoidUp = !upDotsAvoidDown && downDots > 0;

        // LILYPOND-REF: lily/note-collision.cc:374-397 check_meshing_chords — "In
        // meshed chords with dots on the left, adjust dot direction": when the up
        // group moved right and the down head is dotted, the down stem's dots take
        // direction UP (see DownDotDirectionUp for the CENTER/conform arms).
        bool downDotDirUp = shiftAmount > 1e-6 && downDots > 0;

        // LILYPOND-REF: note-collision.cc:539-579 automatic_shift — offsets[d] = d * offset,
        // i.e. the up group takes the amount and the down group its negation;
        // calc_positioning_done (:440-468) then pins the leftmost group with
        // `amount - left_most`, so the two groups end up 2 × |shiftAmount| apart.
        return new NoteCollisionInfo(type, shiftAmount, -shiftAmount,
            downDotDirectionUp: downDotDirUp,
            downDotsAvoidUpHeads: downDotsAvoidUp,
            upDotsAvoidDownHeads: upDotsAvoidDown);
    }

    /// <summary>
    /// Merge case: the two columns share one X (shift 0) and their coinciding heads
    /// print on top of each other. Equal ball types with equal dot counts wipe NO
    /// head — LilyPond leaves both alive and the identical glyphs overlap exactly
    /// (measured in collision-seconds.ly: the merged half-note unisons are two head
    /// paths at one coordinate). A head is wiped only when the two differ: equal
    /// types with different VISIBLE dot counts (reachable only under
    /// merge-differently-dotted) wipe the less-dotted head; a differently-headed
    /// merge (reachable only under merge-differently-headed) wipes the shorter head.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-collision.cc:254-318 — wipe_ball starts null and the
    ///   equal-type equal-dots arm (:276-289) never assigns it.
    /// ⚠️ THIS USED TO WIPE THE DOWN HEAD ON EVERY SAME-HEADED MERGE, which silently
    /// dropped the down voice's chord in collision-seconds.ly m1p2 (its citation
    /// named :381-407, a range inside calc_positioning_done with no wipe code).
    /// ⚠️ NOT PORTED — dot_wipe_head: LilyPond also kills the down head's DOT on
    /// every merge (:262-314); Lily# has no dot-wipe channel, so an equal-dots merge
    /// draws both voices' dots coincident (same ink, one glyph too many).
    /// </remarks>
    private static NoteCollisionInfo ComputeMergeInfo(int upNoteValue, int downNoteValue,
        int upDots, int downDots)
    {
        bool upIsOpen = upNoteValue <= 2; // whole(1) or half(2) = open notehead
        bool downIsOpen = downNoteValue <= 2;

        bool hideUp = false, hideDown = false;
        if (upNoteValue == downNoteValue)
        {
            // LILYPOND-REF: note-collision.cc:276-290 — equal heads: wipe only on a
            // visible-dot-count difference, the less-dotted side.
            if (downDots < upDots)
                hideDown = true;
            else if (downDots > upDots)
                hideUp = true;
        }
        else
        {
            // LILYPOND-REF: note-collision.cc:291-299 — different heads: hide the
            // filled (shorter duration) head, keep the open one.
            hideUp = !upIsOpen;   // hide up if it's filled (quarter/eighth)
            hideDown = !downIsOpen; // hide down if it's filled
        }

        return new NoteCollisionInfo(CollisionType.Merge, 0, 0,
            shouldMerge: true, upHeadTransparent: hideUp, downHeadTransparent: hideDown);
    }

    /// <summary>
    /// The notehead's X EXTENT in staff spaces for a note value — the quantity LilyPond
    /// measures a collision with.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-collision.cc:55-56 —
    /// <c>extent_up = sh_up-&gt;extent (sh_up, X_AXIS)</c>, a grob EXTENT; :339-348 scales the
    /// shift by those two, and :435 takes the down head's <c>extent(…).length()</c> as the
    /// unit the offsets are multiplied by.
    /// <para>
    /// ⚠️ AN EXTENT IS THE INK, NOT THE ADVANCE. This read
    /// <see cref="EngravingDefaults.NoteheadBlackWidth"/> and its neighbours, which that file
    /// says in as many words are "Emmentaler ADVANCE widths" — the same mix-up session 95
    /// pulled out of seven other sites, and the reason a plain second came out 1.304000 apart
    /// against LilyPond's 1.304200. It ALSO had no arm for the HALF head, so an open head
    /// (1.377400) was measured with the black one's number and the unison of book XVF came
    /// out 0.073400 short. Both are visible in the same measurement.
    /// </para>
    /// ⚠️ The breve keeps <see cref="EngravingDefaults.NoteheadDoubleWholeWidth"/>: it is the
    /// one width on that list with no font behind it (the extractor emits no brevis notehead),
    /// so there is no ink box to prefer — see the remark on that constant.
    /// </remarks>
    private static double HeadWidth(int noteValue) => noteValue switch
    {
        <= 0 => EngravingDefaults.NoteheadDoubleWholeWidth, // breve or longer
        _ => GlyphMetrics.GetNoteheadBBox(noteValue).Width, // whole / half / black ink
    };

    private bool CheckTouch(List<int> ups, List<int> downs, int threshold)
    {
        // Touch if extreme notes are adjacent but not overlapping
        // ups[0] = lowest up-stem, downs.Last() = highest down-stem
        if (ups[0] < downs.Last())
            return false;

        // Check if second notes also respect spacing
        if (downs.Count >= 2 && ups[0] < downs[downs.Count - 2] + threshold + 1)
            return false;
        if (ups.Count >= 2 && ups[1] < downs.Last() + threshold + 1)
            return false;

        return true;
    }

    private bool CheckMergePossible(
        List<int> ups, List<int> downs,
        int upNoteValue, int downNoteValue,
        int upDots, int downDots)
    {
        // Merge requires up notes to be at or above down notes (Lilypond line 89)
        if (ups[0] < downs[0] || ups.Last() < downs.Last())
            return false;

        // Cannot merge whole notes or longer
        if (upNoteValue <= 0 || downNoteValue <= 0)
            return false;

        // LILYPOND-REF: lily/note-collision.cc:116-126
        // Whole+half cannot merge (both open noteheads, only stem distinguishes)
        if ((upNoteValue == 1 && downNoteValue == 2) ||
            (upNoteValue == 2 && downNoteValue == 1))
            return false;

        // Check dot compatibility
        if (upDots != downDots && !_params.MergeDifferentlyDotted)
            return false;

        // LILYPOND-REF: lily/note-collision.cc:111-114
        // Half+quarter/eighth merges: when merge-differently-headed is true,
        // notes with different noteheads (open vs filled) can merge.
        // The open notehead (half) is kept visible.
        if (upNoteValue != downNoteValue && !_params.MergeDifferentlyHeaded)
            return false;

        return true;
    }

    private (bool closeHalf, bool distantHalf, bool fullCollide) DetectCollisionTypes(
        List<int> ups, List<int> downs, int threshold, ref bool mergePossible)
    {
        bool closeHalf = false;
        bool distantHalf = false;
        bool fullCollide = false;

        // Merge-sort like iteration (both lists ascending)
        int i = 0, j = 0;
        while (i < ups.Count && j < downs.Count)
        {
            int up = ups[i];
            int down = downs[j];

            if (up == down)
            {
                fullCollide = true;
                i++;
                j++;
            }
            else if (Math.Abs(up - down) <= threshold)
            {
                mergePossible = false;
                // Lilypond: ups[i] > dps[j] means up-stem note is higher
                if (up > down)
                    closeHalf = true;
                else
                    distantHalf = true;

                // Advance the smaller one
                if (up < down) i++;
                else j++;
            }
            else
            {
                // Check for interleaving (Lilypond line 158-161)
                if (up > downs[0] && up < downs.Last())
                    mergePossible = false;
                if (down > ups[0] && down < ups.Last())
                    mergePossible = false;

                // Advance the smaller one
                if (up < down) i++;
                else j++;
            }
        }

        return (closeHalf, distantHalf, fullCollide);
    }

    /// <summary>
    /// Calculates collision info for a voice column with multiple voices — the literal
    /// port of LilyPond's <c>automatic_shift</c>: within each stem direction the clash
    /// groups accumulate offsets clause for clause (match / clear-heads / cross / bare
    /// stem, then the opposite-direction clamps), the whole set is multiplied by the
    /// width of the down-stem group's first head, and the leftmost NEGATIVE amount is
    /// pinned to the column slot.
    /// Returns (VoiceId, ItemIndex, XOffset, HeadTransparent, Dot) for each entry, where
    /// Dot carries the item's dot-column adjustments (direction / minimum X).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-collision.cc:504-599 automatic_shift — the group loop
    ///   ported clause for clause below (each clause carries its line);
    /// LILYPOND-REF: lily/note-collision.cc:403-471 calc_positioning_done — the
    ///   <c>× wid</c> (whose loop over {UP, DOWN} OVERWRITES, so wid is the DOWN
    ///   group's first support head whenever a down group exists) and the
    ///   <c>amount - left_most</c> translation, where <c>left_most</c> starts at 0 and
    ///   only ever moves BELOW it;
    /// LILYPOND-REF: scm/music-functions.scm:666-674 make-voice-props-set —
    ///   <c>horizontal-shift = (quotient n 2)</c>, so voices 1,2 read 0 and voices
    ///   3,4 read 1: both the shift_less sort of get_clash_groups (:475-497) and the
    ///   "explicit shift matches previous" clause consume that number, and under
    ///   <c>voice { } { }</c> every voice is voicified so the shift is always explicit.
    /// <para>
    /// ⚠️ THE BODY THIS REPLACED WAS AN INVENTION, not a port, and its citation said
    /// otherwise: every later same-direction voice took a flat +1 head width
    /// ("cascade outward"), which put the third voice of
    /// scratch/ベースタブLy/Untitled-4.lys at +1.3042 where LilyPond MEASURES +0.652 —
    /// the <c>else if (Stem::is_valid_stem) offset += 0.5</c> clause times the down
    /// head's 1.3042. It also returned all zeros whenever one direction was EMPTY;
    /// LilyPond runs the loop regardless, so two same-direction voices spread even
    /// with no opposite voice on the moment.
    /// </para>
    /// <para>
    /// The Side_position_interface dot supports of :352-372 (dots-on-the-left clearing
    /// right-hand heads) are resolved here into <see cref="DotAdjustment.ColumnMinX"/> —
    /// see <see cref="DotColumnSupportMinX"/>. ⚠️ STILL NOT PORTED: :578-586 (an up
    /// group's dot column clearing LATER up stems — needs three-plus voices with a
    /// dotted first up voice; no corpus book reaches it).
    /// </para>
    /// <para>
    /// ⚠️ KEPT DIVERGENCE: a voice CROSSING (CollisionType.Meshing) pins the
    /// RIGHTMOST group where LilyPond pins the leftmost — the separation is LP's, but
    /// the (frequently beamed) upper voice keeps its column X so its beam, drawn at
    /// column X and not per note, is not skewed.
    /// </para>
    /// </remarks>
    public ImmutableArray<(int VoiceId, int ItemIndex, double XOffset, bool HeadTransparent, DotAdjustment Dot)> CalculateVoiceOffsets(
        VoiceColumn column)
    {
        var offsets = new List<(int VoiceId, int ItemIndex, double XOffset, bool HeadTransparent, DotAdjustment Dot)>();

        // get_clash_groups: one group per stem direction, sorted by horizontal-shift
        // (shift_less); VoiceId order IS that order under (id-1)/2, ties stable.
        var upEntries = column.Entries.Where(e => GetStemDirection(e) == true).ToList();
        var downEntries = column.Entries.Where(e => GetStemDirection(e) == false).ToList();
        upEntries.Sort((a, b) => a.VoiceId.CompareTo(b.VoiceId));
        downEntries.Sort((a, b) => a.VoiceId.CompareTo(b.VoiceId));

        if (upEntries.Count == 0 && downEntries.Count == 0)
        {
            foreach (var entry in column.Entries)
                offsets.Add((entry.VoiceId, entry.ItemIndex, 0, false, default));
            return offsets.ToImmutableArray();
        }

        // head_positions_interval, widened one position each way
        // (:517-520 — s[LEFT]--; s[RIGHT]++).
        var groups = new[] { upEntries, downEntries }; // g: 0 = UP (d=+1), 1 = DOWN (d=-1)
        var ext = new (int Lo, int Hi)[2][];
        var unionExt = new (int Lo, int Hi)[2];
        var validStem = new bool[2][];
        for (int g = 0; g < 2; g++)
        {
            var list = groups[g];
            ext[g] = new (int, int)[list.Count];
            validStem[g] = new bool[list.Count];
            int lo = int.MaxValue, hi = int.MinValue;
            for (int i = 0; i < list.Count; i++)
            {
                var ps = GetStaffPositions(new List<VoiceEntry> { list[i] });
                ext[g][i] = ps.Count > 0 ? (ps.Min() - 1, ps.Max() + 1) : (0, 0);
                lo = Math.Min(lo, ext[g][i].Lo);
                hi = Math.Max(hi, ext[g][i].Hi);
                // LILYPOND-REF: lily/stem.cc Stem::is_valid_stem — a stemless head
                // (whole, breve) contributes the 0.5 arm, a stemmed one the 1.0.
                validStem[g][i] = NoteColumnLayout.Of(list[i].Item) is { HasStem: true };
            }
            unionExt[g] = (lo, hi);
        }

        // inner_offset = check_meshing_chords over the FIRST group of each direction
        // (:526-529), 0.0 when either direction is empty. Dimensionless, in down-stem
        // head widths — the extent ratio is applied inside AnalyzeCollision, the width
        // below. A merge returns 0 with its head wipes. The normal-side sets for the
        // merge/classification half (LP's filter=true re-read) are derived inside
        // AnalyzeCollision from these same positions.
        var collision = upEntries.Count > 0 && downEntries.Count > 0
            ? AnalyzeCollision(
                GetStaffPositions(new List<VoiceEntry> { upEntries[0] }),
                GetStaffPositions(new List<VoiceEntry> { downEntries[0] }),
                GetNoteInfo(upEntries[0]).noteValue,
                GetNoteInfo(downEntries[0]).noteValue,
                GetNoteInfo(upEntries[0]).dots,
                GetNoteInfo(downEntries[0]).dots,
                sameHeadStyle: GetNoteheadStyle(upEntries[0]) == GetNoteheadStyle(downEntries[0]))
            : NoteCollisionInfo.NoCollision;
        // :200-201 — the sign carries the direction; UpStemXOffset IS +shift_amount.
        double inner = collision.UpStemXOffset;

        // The per-direction group loop, :539-581.
        var off = new double[2][];
        for (int g = 0; g < 2; g++)
        {
            int d = g == 0 ? 1 : -1;
            int o = 1 - g;
            var list = groups[g];
            off[g] = new double[list.Count];
            double offset = inner;
            int prevShift = 0;
            for (int i = 0; i < list.Count; i++)
            {
                int hs = (list[i].VoiceId - 1) / 2; // horizontal-shift
                if (i == 0)
                {
                    offset = inner;
                }
                else
                {
                    if (hs == prevShift)
                    {
                        // :559-560 — match the previous notecolumn offset.
                    }
                    else if (ext[g][i].Hi > ext[g][i - 1].Lo && ext[g][i].Lo < ext[g][i - 1].Hi)
                    {
                        // :561-563 — fully clear the previous-notecolumn heads.
                        offset += 1.0;
                    }
                    else if (d > 0 ? ext[g][i].Lo >= ext[g][i - 1].Hi
                                   : ext[g][i].Hi <= ext[g][i - 1].Lo)
                    {
                        // :564-567 — we cross the previous notecolumn
                        // (d * extents[d][i][-d] >= d * extents[d][i-1][d]).
                        offset += validStem[g][i - 1] ? 1.0 : 0.5;
                    }
                    else if (validStem[g][i])
                    {
                        // :568-569.
                        offset += 0.5;
                    }

                    // :571-577 — check if we cross the opposite-stemmed voices. An
                    // empty opposite union can satisfy neither condition (LP's empty
                    // Slice compares the same way), hence the single guard.
                    if (groups[o].Count > 0)
                    {
                        if (d > 0 ? ext[g][i].Lo < unionExt[o].Hi
                                  : ext[g][i].Hi > unionExt[o].Lo)
                            offset = Math.Max(offset, 0.5);
                        if (ext[g][i].Hi > ext[o][0].Lo && ext[g][i].Lo < ext[o][0].Hi)
                            offset = Math.Max(offset, 1.0);
                    }
                }
                off[g][i] = d * offset;
                prevShift = hs;
            }
        }

        // :427-437 — wid: the loop over {UP, DOWN} overwrites, so the DOWN group's
        // first support head wins whenever a down group exists.
        double wid = HeadWidth(GetNoteInfo(
            downEntries.Count > 0 ? downEntries[0] : upEntries[0]).noteValue);

        var raw = new List<(int VoiceId, int ItemIndex, double Offset, bool Hide, DotAdjustment Dot)>();
        for (int i = 0; i < upEntries.Count; i++)
            raw.Add((upEntries[i].VoiceId, upEntries[i].ItemIndex, off[0][i],
                i == 0 && collision.UpHeadTransparent, default));
        for (int i = 0; i < downEntries.Count; i++)
            raw.Add((downEntries[i].VoiceId, downEntries[i].ItemIndex, off[1][i],
                i == 0 && collision.DownHeadTransparent,
                i == 0 ? new DotAdjustment(collision.DownDotDirectionUp, null) : default));

        // :440-468 — translate by amount − left_most, where left_most starts 0.0 and
        // only amounts BELOW it move it: an all-positive set keeps the slot in place.
        // KEPT DIVERGENCE (see remarks): a voice crossing pins the rightmost group.
        double pin = collision.Type == CollisionType.Meshing
            ? raw.Max(r => r.Offset)
            : Math.Min(0.0, raw.Min(r => r.Offset));
        foreach (var r in raw)
            offsets.Add((r.VoiceId, r.ItemIndex, (r.Offset - pin) * wid, r.Hide, r.Dot));

        // The dot side supports (:352-372), resolved to a MINIMUM X for the dotted
        // item's dot column — the renderer draws one item at a time and cannot see the
        // opposite voice's heads, so the geometry is settled here, where both groups'
        // final offsets are in hand. The frame is the staff column's (same as XOffset).
        if (upEntries.Count > 0 && downEntries.Count > 0)
        {
            double FinalX(int voiceId, int itemIndex) =>
                offsets.First(o => o.VoiceId == voiceId && o.ItemIndex == itemIndex).XOffset;

            if (collision.DownDotsAvoidUpHeads)
            {
                // The down voice's dots carry direction DOWN from its voice props
                // unless the collision overrode them to UP — the renderer resolves
                // with the same two layers.
                double? minX = DotColumnSupportMinX(
                    dotted: downEntries[0],
                    dottedDir: collision.DownDotDirectionUp ? 1 : -1,
                    support: upEntries[0],
                    supportX: FinalX(upEntries[0].VoiceId, upEntries[0].ItemIndex),
                    allSupportHeads: true, supportStemUp: true);
                if (minX is { } m)
                    Amend(downEntries[0], m);
            }
            else if (collision.UpDotsAvoidDownHeads)
            {
                // An up voice's dots carry direction UP from its voice props.
                double? minX = DotColumnSupportMinX(
                    dotted: upEntries[0], dottedDir: 1,
                    support: downEntries[0],
                    supportX: FinalX(downEntries[0].VoiceId, downEntries[0].ItemIndex),
                    allSupportHeads: false, supportStemUp: false);
                if (minX is { } m)
                    Amend(upEntries[0], m);
            }

            void Amend(VoiceEntry entry, double columnMinX)
            {
                for (int i = 0; i < offsets.Count; i++)
                    if (offsets[i].VoiceId == entry.VoiceId && offsets[i].ItemIndex == entry.ItemIndex)
                    {
                        var o = offsets[i];
                        offsets[i] = (o.VoiceId, o.ItemIndex, o.XOffset, o.HeadTransparent,
                            o.Dot with { ColumnMinX = columnMinX });
                        break;
                    }
            }
        }

        // ONE dot column per staff moment: Dot_column_engraver lives in the STAFF
        // context, so every voice's dots at the moment share a single column X — the
        // right-facing skyline over every DOTTED head's box (X extent × position ±1.1),
        // floored by base_x (the union of the dotted first heads' extents, and the first
        // head is never the reversed one, so it sits at the column X), sampled at each
        // dot's resolved row; the max sample is the column X, padding one dot width.
        // The per-item draw keeps its own floor and takes the Max, so distributing the
        // common X to every dotted entry is enough. Undotted heads join the skyline only
        // through the collision supports settled above (:352-372).
        // LILYPOND-REF: ly/engraver-init.ly:73 Staff context — \consists Dot_column_engraver
        // LILYPOND-REF: lily/dot-column.cc:56-141,192-232 calc_positioning_done — add_head
        //   makes every dotted head a side support; base_x unites the first heads.
        // LILYPOND-REF: lily/dot-formatting-problem.cc — set_minimum_height (base_x[RIGHT])
        // LILYPOND-REF: lily/dot-configuration.cc:129-137 x_offset — a max of point samples
        // ⚠️ NOT PORTED: stem/flag boxes and rest supports (see DotColumnSupportMinX);
        //   rests never reach this column (VoiceCollector keeps them off the timeline).
        if (column.Entries.Length > 1)
        {
            var dotted = column.Entries.Where(e => GetNoteInfo(e).dots > 0).ToList();
            if (dotted.Count > 0)
            {
                double EntryX(VoiceEntry e) => offsets
                    .First(o => o.VoiceId == e.VoiceId && o.ItemIndex == e.ItemIndex).XOffset;

                const double headBandHalfPositions = 1.1;
                double baseXRight = double.NegativeInfinity;
                var boxes = new List<(int Pos, double Right)>();
                var rows = new List<int>();
                foreach (var e in dotted)
                {
                    var positions = GetStaffPositions(new List<VoiceEntry> { e });
                    if (positions.Count == 0)
                        continue;
                    var (noteValue, _) = GetNoteInfo(e);
                    double inkRight = GlyphMetrics.GetNoteheadBBox(noteValue).Right;
                    double ex = EntryX(e);
                    bool stemUp = GetStemDirection(e) ?? true;
                    double[] within = positions.Count >= 2
                        ? ChordHeadPositioning.CalculateOffsets(
                            positions.Select(p => new ChordNoteInfo(p, null, false)).ToImmutableArray(),
                            stemUp, noteValue)
                        : new double[positions.Count];

                    baseXRight = Math.Max(baseXRight, ex + inkRight);
                    for (int h = 0; h < positions.Count; h++)
                        boxes.Add((positions[h], ex + within[h] + inkRight));

                    // The rows the renderer will draw at — same inputs, same resolver:
                    // the two-layer preferred direction (voice props; a positive-shift
                    // collision overrides the down voice's dots to UP).
                    bool isDownFirst = upEntries.Count > 0 && downEntries.Count > 0
                        && e.VoiceId == downEntries[0].VoiceId
                        && e.ItemIndex == downEntries[0].ItemIndex;
                    int dir = isDownFirst && collision.DownDotDirectionUp ? 1
                        : VoiceDefaults.GetDefaultStemUp(e.VoiceId) switch
                          { true => 1, false => -1, null => 0 };
                    rows.AddRange(DotConfiguration.Resolve(
                        positions,
                        dir != 0 ? Enumerable.Repeat(dir, positions.Count).ToArray() : null));
                }

                if (rows.Count > 0)
                {
                    double columnX = double.NegativeInfinity;
                    foreach (int r in rows)
                    {
                        double sample = baseXRight;
                        foreach (var b in boxes)
                            if (Math.Abs(r - b.Pos) <= headBandHalfPositions)
                                sample = Math.Max(sample, b.Right);
                        columnX = Math.Max(columnX, sample);
                    }

                    double minX = columnX + GlyphMetrics.AugmentationDot.Width;
                    for (int i = 0; i < offsets.Count; i++)
                    {
                        var o = offsets[i];
                        if (!dotted.Any(e => e.VoiceId == o.VoiceId && e.ItemIndex == o.ItemIndex))
                            continue;
                        offsets[i] = (o.VoiceId, o.ItemIndex, o.XOffset, o.HeadTransparent,
                            o.Dot with
                            {
                                ColumnMinX = Math.Max(o.Dot.ColumnMinX ?? double.NegativeInfinity, minX),
                            });
                    }
                }
            }
        }

        return offsets.ToImmutableArray();
    }

    /// <summary>
    /// The minimum X (staff-column frame) that puts a dotted item's dot column clear of
    /// the opposite group's heads, plus one dot width of padding — or null when no
    /// support head's box covers any resolved dot's position (an unconstrained column
    /// stays where it is).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-collision.cc:352-372 check_meshing_chords — add_support
    ///   hands the heads to the DotColumn as side-support-elements;
    /// LILYPOND-REF: lily/dot-column.cc:79-141 calc_positioning_done — each Note_head
    ///   support becomes a box (its X extent × positions head±1.1); the column's X is
    ///   the head skyline sampled AT each dot's resolved position
    ///   (lily/dot-configuration.cc x_offset — a max over point samples), plus the
    ///   padding, applied ONCE to the whole column (:229-232);
    /// LILYPOND-REF: scm/define-grobs.scm DotColumn — (padding . dot-column-interface::pad-by-one-dot-width)
    /// ⚠️ NOT PORTED: the FLAG boxes of the support heads' stems (dot-column.cc:135-141
    ///   — an up-voice flag also constrains the column; needs a flagged unbeamed
    ///   support voice, no corpus book reaches it) and Rest/Stem-interface supports
    ///   (note-collision adds only heads).
    /// The dot positions resolved here mirror the renderer's own
    /// <see cref="DotConfiguration.Resolve"/> call — same positions, same effective
    /// direction (voice props overridden by the collision) — so the point samples land
    /// where the dots will be drawn.
    /// </remarks>
    private static double? DotColumnSupportMinX(
        VoiceEntry dotted, int dottedDir,
        VoiceEntry support, double supportX, bool allSupportHeads, bool supportStemUp)
    {
        var dotPositions = GetStaffPositions(new List<VoiceEntry> { dotted });
        if (dotPositions.Count == 0)
            return null;
        var dirs = Enumerable.Repeat(dottedDir, dotPositions.Count).ToArray();
        var resolved = DotConfiguration.Resolve(dotPositions, dirs);

        var (supportValue, _) = GetNoteInfo(support);
        var headBox = GlyphMetrics.GetNoteheadBBox(supportValue);
        // The head box spans positions head ± 1.1 and the skyline is sampled at the
        // dot's own (integer) position — the dot's extent plays no part.
        // LILYPOND-REF: lily/dot-column.cc:117-118 calc_positioning_done — Interval (-1.1, 1.1)
        const double headBandHalfPositions = 1.1;
        double pad = GlyphMetrics.AugmentationDot.Width;

        var supportPositions = GetStaffPositions(new List<VoiceEntry> { support });
        if (supportPositions.Count == 0)
            return null;
        double[] headOffsets;
        if (!allSupportHeads)
        {
            // fh_down alone: the down stem's FIRST head is its highest, and the first
            // (support) head is never the reversed one, so it sits at the column X.
            // LILYPOND-REF: lily/stem.cc:283-289 first_head — extremal_heads[-dir].
            supportPositions = new List<int> { supportPositions.Max() };
            headOffsets = new double[1];
        }
        else if (supportPositions.Count >= 2)
        {
            // Every head on the stem, each at its drawn X — reversed (suspended)
            // seconds included; the loop in :361-370 walks the stem's note-heads set.
            var notes = supportPositions
                .Select(p => new ChordNoteInfo(p, null, false))
                .ToImmutableArray();
            headOffsets = ChordHeadPositioning.CalculateOffsets(notes, supportStemUp, supportValue);
        }
        else
        {
            headOffsets = new double[1];
        }

        double? minX = null;
        foreach (int dotPos in resolved)
            for (int h = 0; h < supportPositions.Count; h++)
            {
                if (Math.Abs(dotPos - supportPositions[h]) <= headBandHalfPositions)
                {
                    double candidate = supportX + headOffsets[h] + headBox.Right + pad;
                    if (minX is not { } cur || candidate > cur)
                        minX = candidate;
                }
            }
        return minX;
    }

    private static bool? GetStemDirection(VoiceEntry entry)
    {
        if (entry.ForcedStemUp.HasValue)
            return entry.ForcedStemUp.Value;

        return entry.Item switch
        {
            NoteItem note => note.StemUp,
            ChordItem chord => chord.StemUp,
            _ => null
        };
    }

    /// <summary>
    /// Staff positions of the heads on the NORMAL side of the stem, sorted ascending —
    /// the set LilyPond's <c>Stem::note_head_positions (stem, true)</c> returns. A head
    /// that <see cref="ChordHeadPositioning"/> reverses to the far side of the stem
    /// (the second/unison displacement) is excluded; a single note or a chord without
    /// seconds keeps all of its heads. Reversal depends only on the position multiset,
    /// the stem direction and the head glyph, so the set is derivable right here —
    /// ChordHeadPositioning stays the one house that knows which head flips.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/stem.cc:303-345 note_head_positions — the filter keeps a
    /// head only when its X offset inside the column is 0 (<c>tolerated</c> is the
    /// point interval [0,0]); reversed heads sit a head-width off and drop out.
    /// The one widening of that interval — a harmonic head centered on a stemless
    /// whole note reads [0, 0.375] — is not ported: Lily# has no producer for a
    /// centered harmonic head yet (@notehead(diamond) keeps column X), and no
    /// corpus book observes it.
    /// </remarks>
    private static List<int> NormalSideStaffPositions(
        IReadOnlyList<int> positions, bool stemUp, int noteValue)
    {
        if (positions.Count < 2)
            return positions.OrderBy(p => p).ToList();

        var notes = positions
            .Select(p => new ChordNoteInfo(p, null, false))
            .ToImmutableArray();
        var offsets = ChordHeadPositioning.CalculateOffsets(notes, stemUp, noteValue);
        var kept = new List<int>();
        for (int i = 0; i < offsets.Length; i++)
            if (offsets[i] == 0.0)
                kept.Add(positions[i]);
        kept.Sort();
        return kept;
    }

    /// <summary>The notehead style LP's merge check compares (fh_up/fh_down "style").
    /// ⚠️ LP reads the FIRST HEAD's style; this reads the chord-level style, and
    /// <see cref="ChordNoteInfo.Notehead"/> can override per note (drum chords mix
    /// heads) — a mixed-head chord would compare the wrong head. No producer puts a
    /// mixed-head chord into a two-voice collision yet; refine when one arrives.</summary>
    private static NoteheadStyle GetNoteheadStyle(VoiceEntry entry) => entry.Item switch
    {
        NoteItem note => note.Notehead,
        ChordItem chord => chord.Notehead,
        _ => NoteheadStyle.Default
    };

    private static List<int> GetStaffPositions(List<VoiceEntry> entries)
    {
        var positions = new List<int>();
        foreach (var entry in entries)
        {
            switch (entry.Item)
            {
                case NoteItem note:
                    positions.Add(note.StaffPosition);
                    break;
                case ChordItem chord:
                    positions.AddRange(chord.Notes.Select(n => n.StaffPosition));
                    break;
            }
        }
        return positions;
    }

    private static (int noteValue, int dots) GetNoteInfo(VoiceEntry entry)
    {
        return entry.Item switch
        {
            NoteItem note => (GetNoteValue(note.BaseDuration), note.Dots),
            ChordItem chord => (GetNoteValue(chord.BaseDuration), chord.Dots),
            _ => (4, 0)
        };
    }

    private static int GetNoteValue(Core.Semantics.Fraction duration)
    {
        // Convert duration to note value (1 = whole, 2 = half, 4 = quarter, etc.)
        if (duration.Numerator == 0) return 4;
        return duration.Denominator / duration.Numerator;
    }
}