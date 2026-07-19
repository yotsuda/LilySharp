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
    /// LILYPOND-REF: lily/note-collision.cc:411-448
    /// In collision contexts, down-stem dots shift below the line to avoid
    /// colliding with up-stem voice notes/dots.
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
/// IMPLEMENTED — head wipe (note-collision.cc:254-312: merge hides the overlapping notehead)
/// IMPLEMENTED — width-based shift normalization (note-collision.cc:505-605 automatic_shift)
/// IMPLEMENTED — half+eighth merge formula (note-collision.cc:91-176 check_meshing_chords)
/// NOT YET IMPLEMENTED — FA-shaped notehead handling (note-collision.cc:237-252 fa_styles)
/// IMPLEMENTED — dot direction adjustment (note-collision.cc:263-337 dot_wipe_head)
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

        // Too far apart to collide
        // ups[0] is lowest up-stem note, downs.Last() is highest down-stem note
        if (ups[0] > downs.Last() + threshold)
            return NoteCollisionInfo.NoCollision;

        // Check for touch condition (extreme noteheads just meet)
        bool touch = CheckTouch(ups, downs, threshold);

        // Check for merge possibility
        bool mergePossible = CheckMergePossible(ups, downs, upNoteValue, downNoteValue, upDots, downDots);

        // Detect collision types
        var (closeHalf, distantHalf, fullCollide) = DetectCollisionTypes(ups, downs, threshold, ref mergePossible);

        // Full collide includes combined cases (Lilypond line 174-176)
        fullCollide = fullCollide || (closeHalf && distantHalf) ||
                     (distantHalf && (upNoteValue <= 0 || downNoteValue <= 0));

        // Determine shift direction (Lilypond line 178-208)
        // Default: up-stem shifts right
        bool shiftUpRight = true;

        if ((fullCollide || ((closeHalf || distantHalf) && _params.PreferDottedRight))
            && upDots < downDots)
        {
            // Dotted down-stem: down-stem shifts right instead
            shiftUpRight = false;
        }

        // Calculate offsets
        if (mergePossible && fullCollide && !closeHalf && !distantHalf)
            return ComputeMergeInfo(upNoteValue, downNoteValue);

        // LILYPOND-REF: lily/note-collision.cc:411-448
        // Detect whether down-stem dots should shift down instead of up.
        // When two voices collide, down-stem dots on staff lines must shift
        // below the line to avoid colliding with up-stem notes/dots.
        bool downDotForceDown = false;
        if (downDots > 0)
        {
            downDotForceDown = downs.Any(pos => pos % 2 == 0);
        }

        if (touch && !fullCollide && !closeHalf && !distantHalf)
        {
            // LILYPOND-REF: lily/note-collision.cc:212-227 — for a pure touch LP shifts the
            // DOWN-stem right (shift_amount = -1). The one exception (touch = false, up-stem
            // right) is an up-stem note OFF a staff line carrying MORE dots than the down-stem
            // under prefer-dotted-right. Symmetric offsets; calc_positioning_done pins the
            // leftmost group, so the 2-voice separation is 2*inner. (:323-324 multiplier 0.5.)
            bool upOnLine = ups[0] % 2 == 0;
            bool touchUpRight = !upOnLine && _params.PreferDottedRight && upDots > downDots;
            double inner = _params.TouchShift;
            double upOffset = touchUpRight ? inner : -inner;
            double downOffset = touchUpRight ? -inner : inner;
            return new NoteCollisionInfo(CollisionType.Touch, upOffset, downOffset,
                downDotForceDown: downDotForceDown);
        }

        if (fullCollide || closeHalf || distantHalf)
            return ComputeShiftInfo(upStaffPositions, downStaffPositions,
                upNoteValue, downNoteValue, upDots, downDots,
                fullCollide, closeHalf, distantHalf, shiftUpRight, downDotForceDown);

        // LILYPOND-REF: lily/note-collision.cc:332-337 — the "we're meshing" fallback.
        // Reached only for a voice CROSSING: the up-stem note sits more than a
        // threshold BELOW the down-stem note, so none of merge/touch/full/close/
        // distant fired, yet the notes are not too far apart — the up-stem's stem
        // would pierce the down-stem head. LP applies a small meshing shift
        // (0.1 with dots, else 0.17). The consumer (CalculateVoiceOffsets) pins
        // the UP-stem voice for CollisionType.Meshing and shifts the DOWN-stem
        // voice LEFT, giving geometry identical to LP's (which shifts the upper
        // voice right) while leaving a beamed upper voice at its column X so its
        // beam — drawn at column X, not per-note — stays intact.
        //
        // Extent scaling (note-collision.cc:343-348): for a positive (up-shifts-
        // right) shift the factor is (extent_down[RIGHT] - extent_up[LEFT]) /
        // extent_down.length(). Every Lily# notehead has its left extent at 0, so
        // this factor is exactly 1.0 here; the base width is applied by the
        // consumer (× notehead width).
        bool meshHasDots = upDots > 0 || downDots > 0;
        double meshShift = meshHasDots
            ? _params.MeshingDottedShift
            : _params.MeshingGeneralShift;
        double meshUpOffset = shiftUpRight ? meshShift : -meshShift;
        double meshDownOffset = shiftUpRight ? -meshShift : meshShift;
        return new NoteCollisionInfo(CollisionType.Meshing, meshUpOffset, meshDownOffset,
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
    /// Full / close-half / distant-half collision: selects the shift amount and
    /// type (meshing when heads can interlock, else the standard half/full shift),
    /// then applies the symmetric +inner-up / -inner-down offsets.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-collision.cc:180-230 check_meshing_chords();
    ///   :297-312, :326 close_half_collide; automatic_shift + calc_positioning_done —
    ///   symmetric shift pinned by the consumer, so the 2-voice separation is 2*inner.
    /// </remarks>
    private NoteCollisionInfo ComputeShiftInfo(
        IReadOnlyList<int> upStaffPositions, IReadOnlyList<int> downStaffPositions,
        int upNoteValue, int downNoteValue, int upDots, int downDots,
        bool fullCollide, bool closeHalf, bool distantHalf,
        bool shiftUpRight, bool downDotForceDown)
    {
        // Select shift amount based on collision type (Lilypond line 297-302)
        double shiftAmount;
        CollisionType type;

        if (fullCollide)
        {
            shiftAmount = _params.FullCollideShift;
            type = CollisionType.Full;
        }
        else if (closeHalf || distantHalf)
        {
            // A half-collide is always the half shift — LilyPond's meshing shift (0.17) is NOT an
            // option here: it is only the fallback when NONE of touch / full / close / distant
            // fired (a voice crossing), handled separately below. Applying it in the close-half
            // branch (formerly gated on "different head groups") shrank the offset so a half note
            // and an eighth a second apart in two voices overlapped instead of sitting side by side.
            // LILYPOND-REF: lily/note-collision.cc:325-330 — close_half → 0.52, distant_half → 0.4,
            // both unconditional; meshing (0.17) is the else-branch that these preclude.
            shiftAmount = closeHalf ? _params.CloseHalfShift : _params.DistantHalfShift;
            type = CollisionType.CloseHalf;
        }
        else
        {
            shiftAmount = _params.DistantHalfShift;
            type = CollisionType.CloseHalf;
        }

        // LILYPOND-REF: lily/note-collision.cc:321-322 — when a dotted down-stem is forced
        // to the right (here shiftUpRight==false ⟺ LP's stem_to_stem: Branch A fired and it
        // is not a touch), the 0.65 stem-to-stem clearance replaces the per-collision value.
        if (!shiftUpRight)
            shiftAmount = _params.StemToStemShift;

        // LILYPOND-REF: lily/note-collision.cc:339-348 — LP scales the shift by the signed
        // head-extent ratio, because the displacement to clear a collision depends on the
        // widths of the heads on the interfering sides:
        //   up-stem shifts right:   (extent_down[RIGHT] - extent_up[LEFT]) / extent_down.length()
        //   down-stem shifts right: (extent_up[RIGHT]   - extent_down[LEFT]) / extent_down.length()
        // Our heads anchor at their own left edge, so extent_up = [0, upW], extent_down =
        // [0, downW]. The ratio is therefore 1.0 when the up-head shifts right and upW/downW
        // when the down-head shifts right. Together with the down-note-width scaling in
        // CalculateVoiceOffsets this reproduces LP's "displacement = shift × cleared-head width".
        double upW = HeadWidth(upNoteValue);
        double downW = HeadWidth(downNoteValue);
        double extentFactor = shiftUpRight ? 1.0 : (downW > 1e-6 ? upW / downW : 1.0);
        shiftAmount *= extentFactor;

        // Symmetric shift (+inner up / -inner down); the consumer pins the
        // leftmost group, so the 2-voice separation is 2*inner.
        double upOffset = shiftUpRight ? shiftAmount : -shiftAmount;
        double downOffset = shiftUpRight ? -shiftAmount : shiftAmount;

        return new NoteCollisionInfo(type, upOffset, downOffset,
            downDotForceDown: downDotForceDown);
    }

    /// <summary>
    /// Notehead X-width in staff-spaces for a note value (whole/breve are wider
    /// than half/quarter). Mirrors LilyPond's per-duration notehead extents.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/note-collision.cc:339-340 — the shift is scaled by
    /// the down-stem note's width; the extent ratio above supplies the up/down difference.</remarks>
    private static double HeadWidth(int noteValue) => noteValue switch
    {
        <= 0 => EngravingDefaults.NoteheadDoubleWholeWidth, // breve or longer
        1 => EngravingDefaults.NoteheadWholeWidth,          // whole note
        _ => EngravingDefaults.NoteheadBlackWidth           // half, quarter, ...
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