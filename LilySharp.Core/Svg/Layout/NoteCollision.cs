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
    /// Head wipe hides overlapping noteheads in merged multi-voice contexts.
    /// </remarks>
    public bool UpHeadTransparent { get; }

    /// <summary>
    /// Whether the down-stem notehead should be hidden (head wipe).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-collision.cc:254-318
    /// In standard merge cases, the down-stem notehead is wiped.
    /// </remarks>
    public bool DownHeadTransparent { get; }

    /// <summary>
    /// Whether the down-stem voice's dots should shift downward instead of upward
    /// for notes on staff lines.
    /// </summary>
    /// <remarks>
    /// ⚠️ A DIVERGENCE from LilyPond's rule, not a port of it — see the long note at the
    /// assignment in <see cref="NoteCollision.AnalyzeCollision"/>, which also records that the
    /// <c>:411-448</c> this family used to cite is the wrong range.
    /// LILYPOND-REF: lily/note-collision.cc:375-398 check_meshing_chords — the rule it approximates.
    /// </remarks>
    public bool DownDotForceDown { get; }

    public NoteCollisionInfo(CollisionType type, double upStemXOffset, double downStemXOffset,
        bool shouldMerge = false, bool upHeadTransparent = false, bool downHeadTransparent = false,
        bool downDotForceDown = false)
    {
        Type = type;
        UpStemXOffset = upStemXOffset;
        DownStemXOffset = downStemXOffset;
        ShouldMerge = shouldMerge;
        UpHeadTransparent = upHeadTransparent;
        DownHeadTransparent = downHeadTransparent;
        DownDotForceDown = downDotForceDown;
    }

    public static NoteCollisionInfo NoCollision { get; } = new(CollisionType.None, 0, 0);
}

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
/// IMPLEMENTED — head wipe (note-collision.cc:254-318: merge hides the overlapping notehead)
/// IMPLEMENTED — width-based shift normalization (note-collision.cc:427-437 and :447 in
///   calc_positioning_done — NOT automatic_shift, which this line used to name)
/// NOT YET IMPLEMENTED — the half+eighth merge shift (note-collision.cc:305-307: a merge whose
///   DOWN head is a half and UP head an eighth or shorter keeps a nonzero
///   <c>(1 - extent_up[RIGHT]/extent_down[RIGHT]) * 0.5</c>). ComputeMergeInfo returns 0 for
///   every merge. ⚠️ THIS LINE SAID "IMPLEMENTED", and cited :91-176, which is the collision
///   detection and holds no such formula — two errors in one claim. Unreachable at default
///   parameters (it needs merge-differently-headed), which is why nothing caught it.
/// NOT YET IMPLEMENTED — FA-shaped notehead handling (note-collision.cc:237-252 fa_styles)
/// DIVERGENCE — dot direction. LilyPond's rule is :375-398 (fires on a POSITIVE shift, sets
///   UP / CENTER / the up chord's direction); Lily# forces DOWN off the staff line alone. The
///   neighbouring :350-373 (dots on the left take Side_position support against the heads on
///   the right) is unported. ⚠️ This line said "IMPLEMENTED … :263-337 dot_wipe_head";
///   dot_wipe_head is a local variable in the MERGE branch, not this mechanism.
/// IMPLEMENTED — force-hshift manual override (note-collision.cc:608-624 forced_shift)
/// IMPLEMENTED — within-chord seconds displacement (stem.cc:606-760) in ChordHeadPositioning
/// IMPLEMENTED — multi-voice cascading for 3+ voices (note-collision.cc:505-605 automatic_shift)
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
    /// Analyzes collision between two note columns (up-stem and down-stem).
    /// </summary>
    public NoteCollisionInfo AnalyzeCollision(
        IReadOnlyList<int> upStaffPositions,
        IReadOnlyList<int> downStaffPositions,
        int upNoteValue,
        int downNoteValue,
        int upDots,
        int downDots)
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

        // Check for merge possibility
        bool mergePossible = CheckMergePossible(ups, downs, upNoteValue, downNoteValue, upDots, downDots);

        // Detect collision types
        var (closeHalf, distantHalf, fullCollide) = DetectCollisionTypes(ups, downs, threshold, ref mergePossible);

        // LILYPOND-REF: note-collision.cc:191-193
        fullCollide = fullCollide || (closeHalf && distantHalf) ||
                     (distantHalf && (upNoteValue <= 0 || downNoteValue <= 0));

        // Whether the down-stem voice's dots take a forced direction instead of the default.
        //
        // ⚠️⚠️ DIVERGENCE, NOT A PORT, AND THE CITATION USED TO SAY OTHERWISE. This carried
        // "LILYPOND-REF: lily/note-collision.cc:411-448" — a range that exists (which is why
        // audit/citation_drift.csv passes it) but holds get_clash_groups, the extent trigger
        // and the `wid` lookup inside calc_positioning_done. Nothing about dots.
        // LilyPond's dot direction rule is :375-398, and reading it shows a DIFFERENT rule:
        //   - it fires only when `shift_amount > 1e-6`, i.e. when the UP group moved right;
        //   - the direction it sets is UP by default, CENTER when both heads' dots share one
        //     DotColumn, and otherwise whatever the UP chord's dot already reads;
        //   - it sets that on every head of the down stem.
        // Lily# instead forces DOWN whenever any down-stem note sits on a staff line, with no
        // reading of the shift's sign at all. Both the trigger and the outcome differ.
        // ⚠️ The sign now matters more than it did: since the touch branch was restored above,
        // a second and a unison produce a NEGATIVE shift, which is precisely where LilyPond's
        // rule does not fire. Ticketed in HANDOFF §2 A; not changed here, because it moves
        // test/dot-force-down and wants its own measurement against LilyPond first.
        // LILYPOND-REF: lily/note-collision.cc:375-398 check_meshing_chords — the rule this approximates.
        // LILYPOND-REF: lily/note-collision.cc:350-373 check_meshing_chords — the neighbouring
        //   half (dots on the left take Side_position support against the heads on the
        //   right), also unported.
        bool downDotForceDown = false;
        if (downDots > 0)
        {
            downDotForceDown = downs.Any(pos => pos % 2 == 0);
        }

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
        if (mergePossible)
            return ComputeMergeInfo(upNoteValue, downNoteValue);

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

        // LILYPOND-REF: note-collision.cc:539-579 automatic_shift — offsets[d] = d * offset,
        // i.e. the up group takes the amount and the down group its negation;
        // calc_positioning_done (:440-468) then pins the leftmost group with
        // `amount - left_most`, so the two groups end up 2 × |shiftAmount| apart.
        return new NoteCollisionInfo(type, shiftAmount, -shiftAmount,
            downDotForceDown: downDotForceDown);
    }

    /// <summary>
    /// Merge case: two colliding heads at the same column combine into one.
    /// Same-headed merge wipes the down-stem head; a differently-headed merge
    /// (half + quarter/eighth) hides the FILLED head and keeps the open (half)
    /// head visible.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/note-collision.cc:254-318</remarks>
    private static NoteCollisionInfo ComputeMergeInfo(int upNoteValue, int downNoteValue)
    {
        bool upIsOpen = upNoteValue <= 2; // whole(1) or half(2) = open notehead
        bool downIsOpen = downNoteValue <= 2;

        bool hideUp, hideDown;
        if (upNoteValue == downNoteValue)
        {
            // Same heads: standard merge, hide down-stem
            hideUp = false;
            hideDown = true;
        }
        else
        {
            // Different heads: hide the filled (shorter duration) head
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
    /// Calculates collision info for a voice column with multiple voices.
    /// Returns (VoiceId, ItemIndex, XOffset, HeadTransparent, DotForceDown) for each entry.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-collision.cc:254-318 — head wipe
    /// LILYPOND-REF: lily/note-collision.cc:539-581 — multi-voice cascading
    /// For 3+ voices, each additional voice in the same stem direction gets a
    /// cumulative shift of 1 notehead width beyond the base collision offset.
    /// </remarks>
    public ImmutableArray<(int VoiceId, int ItemIndex, double XOffset, bool HeadTransparent, bool DotForceDown)> CalculateVoiceOffsets(
        VoiceColumn column)
    {
        var offsets = new List<(int VoiceId, int ItemIndex, double XOffset, bool HeadTransparent, bool DotForceDown)>();

        // Group entries by stem direction
        var upEntries = column.Entries.Where(e => GetStemDirection(e) == true).ToList();
        var downEntries = column.Entries.Where(e => GetStemDirection(e) == false).ToList();

        if (upEntries.Count == 0 || downEntries.Count == 0)
        {
            // No collision possible - all voices get 0 offset
            foreach (var entry in column.Entries)
            {
                offsets.Add((entry.VoiceId, entry.ItemIndex, 0, false, false));
            }
            return offsets.ToImmutableArray();
        }

        // Sort each group by VoiceId for consistent cascading order
        upEntries.Sort((a, b) => a.VoiceId.CompareTo(b.VoiceId));
        downEntries.Sort((a, b) => a.VoiceId.CompareTo(b.VoiceId));

        // Get staff positions for each group (using first entry = primary voice)
        var upPositions = GetStaffPositions(new List<VoiceEntry> { upEntries[0] });
        var downPositions = GetStaffPositions(new List<VoiceEntry> { downEntries[0] });

        // Get note values and dots (use first entry as representative)
        var (upNoteValue, upDots) = GetNoteInfo(upEntries[0]);
        var (downNoteValue, downDots) = GetNoteInfo(downEntries[0]);

        // Analyze collision between primary up and down voices
        var collision = AnalyzeCollision(upPositions, downPositions, upNoteValue, downNoteValue, upDots, downDots);

        // LILYPOND-REF: lily/note-collision.cc:539-581
        // Apply cascading offsets for 3+ voices.
        // Voice priority order within each direction:
        //   Up-stem:  Voice 1 (base), Voice 3 (+1 notehead), Voice 5 (+2 noteheads), ...
        //   Down-stem: Voice 2 (base), Voice 4 (-1 notehead), Voice 6 (-2 noteheads), ...
        // LILYPOND-REF: lily/note-collision.cc calc_positioning_done — each clash
        // group is translated by (amount - left_most), pinning the leftmost group
        // to the column's spacing slot. Up groups shift +, down groups shift -,
        // with the cascade extending outward; subtract the minimum so the leftmost
        // group lands at 0. For two voices this makes the separation 2*inner.
        var raw = new List<(int VoiceId, int ItemIndex, double Offset, bool Hide, bool Dot)>();
        for (int i = 0; i < upEntries.Count; i++)
        {
            var entry = upEntries[i];
            raw.Add((entry.VoiceId, entry.ItemIndex,
                collision.UpStemXOffset + i,   // up shifts right; cascade outward
                i == 0 && collision.UpHeadTransparent, false));
        }
        for (int i = 0; i < downEntries.Count; i++)
        {
            var entry = downEntries[i];
            raw.Add((entry.VoiceId, entry.ItemIndex,
                collision.DownStemXOffset - i, // down shifts left; cascade outward
                i == 0 && collision.DownHeadTransparent,
                i == 0 && collision.DownDotForceDown));
        }

        // LILYPOND-REF: lily/note-collision.cc calc_positioning_done — each clash
        // group translates by (amount - left_most), pinning the LEFTMOST group to
        // the column slot; the up-stem voice ends up shifted right.
        //
        // A voice CROSSING (CollisionType.Meshing) is the exception: pin the
        // rightmost group (the up-stem voice) instead and let the DOWN-stem voice
        // move LEFT. The separation is identical to LP's, but the — frequently
        // beamed — upper voice keeps its column-X position, so its beam (drawn at
        // column X, not per-note) is not skewed. This matches LP's appearance
        // (upper stem clears to the right of the lower head) without propagating a
        // shift through Lily#'s beam/spacing.
        double pin = raw.Count == 0
            ? 0.0
            : collision.Type == CollisionType.Meshing
                ? raw.Max(r => r.Offset)
                : raw.Min(r => r.Offset);
        // LILYPOND-REF: lily/note-collision.cc:339-340 — the offsets are multiplied by the
        // width of the DOWN-stem note (not the column's widest head); the per-collision
        // extent ratio (ComputeShiftInfo) already carries the up/down width difference.
        double downWidth = HeadWidth(downNoteValue);
        foreach (var r in raw)
            offsets.Add((r.VoiceId, r.ItemIndex,
                (r.Offset - pin) * downWidth, r.Hide, r.Dot));

        return offsets.ToImmutableArray();
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