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
    Full
}

/// <summary>
/// Information about a collision between two note columns.
/// </summary>
public sealed record NoteCollisionInfo
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
    /// LILYPOND-REF: lily/note-collision.cc:381-407
    /// Head wipe hides overlapping noteheads in merged multi-voice contexts.
    /// </remarks>
    public bool UpHeadTransparent { get; }

    /// <summary>
    /// Whether the down-stem notehead should be hidden (head wipe).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-collision.cc:381-407
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
/// LILYPOND-REF: lily/note-collision.cc:299-350 check_meshing_chords()
/// LILYPOND-REF: scm/define-grobs.scm:2537 NoteCollision
///
/// IMPLEMENTED — shift multipliers now match LilyPond:
///   stem_to_stem = 0.65, close_half = 0.52,
///   full_collide = 0.5, distant_half = 0.4
/// IMPLEMENTED — meshing multipliers:
///   meshing_dotted = 0.1, meshing_general = 0.17
/// IMPLEMENTED — head wipe (note-collision-interface.cc:381-407): hide overlapping notehead in merge
/// IMPLEMENTED — width-based shift normalization (note-collision-interface.cc:309-312)
/// IMPLEMENTED — half+eighth merge formula (note-collision-interface.cc:252-261)
/// NOT YET IMPLEMENTED — FA-shaped notehead handling (note-collision-interface.cc:270-280)
/// IMPLEMENTED — dot direction adjustment (note-collision-interface.cc:411-448)
/// IMPLEMENTED — force-hshift manual override (note-collision-interface.cc:486-502)
/// IMPLEMENTED — within-chord seconds displacement (stem.cc:606-760) in ChordHeadPositioning
/// IMPLEMENTED — multi-voice cascading for 3+ voices (note-collision-interface.cc:420-480)
/// </remarks>
public sealed record NoteCollisionParameters
{
    public static NoteCollisionParameters Default { get; } = new();

    /// <summary>Threshold for considering notes as colliding (staff positions).</summary>
    public int CollisionThreshold { get; init; } = 1;

    /// <summary>Whether to merge differently dotted notes.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:2537 merge-differently-dotted</remarks>
    public bool MergeDifferentlyDotted { get; init; } = false;

    /// <summary>Whether to merge differently headed notes (half vs quarter).</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:2537 merge-differently-headed</remarks>
    public bool MergeDifferentlyHeaded { get; init; } = false;

    /// <summary>Whether to prefer dotted notes on the right side.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:2537 prefer-dotted-right</remarks>
    public bool PreferDottedRight { get; init; } = true;

    /// <summary>
    /// Horizontal shift amount for close half collision (in notehead widths).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-collision.cc:323 close_half_collide
    /// </remarks>
    public double CloseHalfShift { get; init; } = 0.52;

    /// <summary>
    /// Horizontal shift amount for distant half collision (in notehead widths).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-collision.cc:325 distant_half_collide
    /// </remarks>
    public double DistantHalfShift { get; init; } = 0.4;

    /// <summary>
    /// Horizontal shift amount for full collision (in notehead widths).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-collision.cc:321 full_collide
    /// </remarks>
    public double FullCollideShift { get; init; } = 0.5;

    /// <summary>
    /// Horizontal shift amount for touch condition (in notehead widths).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-collision.cc:324 touch — multiplier 0.5.
    /// (0.65 is the separate stem_to_stem case at :322, which Lily# does not
    /// detect; the old 0.65 here was that wrong value.)
    /// </remarks>
    public double TouchShift { get; init; } = 0.5;

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
public sealed class NoteCollision
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
        {
            // LILYPOND-REF: lily/note-collision.cc:252-261, 381-407
            // Can merge — determine which head to hide.
            // For same-headed merge: down-stem head is wiped.
            // For differently-headed merge (half+quarter/eighth): the filled
            // notehead is hidden, the open (half) notehead is kept visible.
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
            // LILYPOND-REF: lily/note-collision.cc:323-324 touch (0.5) and
            // automatic_shift (d*offset): the two columns shift symmetrically
            // (+inner up / -inner down); calc_positioning_done pins the leftmost
            // group to the slot, so the 2-voice separation is 2*inner.
            double inner = _params.TouchShift;
            double upOffset = shiftUpRight ? inner : -inner;
            double downOffset = shiftUpRight ? -inner : inner;
            return new NoteCollisionInfo(CollisionType.Touch, upOffset, downOffset,
                downDotForceDown: downDotForceDown);
        }

        if (fullCollide || closeHalf || distantHalf)
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
                // LILYPOND-REF: lily/note-collision.cc:180-230 check_meshing_chords()
                // Meshing (interlocking noteheads) requires ALL of:
                // - Notes are a second apart (already guaranteed by closeHalf/distantHalf)
                // - Neither note is a whole note (round noteheads can't mesh)
                // - Single notes in each voice (chords with multiple seconds don't mesh cleanly)
                // - Different head groups (one open, one filled)
                //   LilyPond checks: head_group_up != head_group_down
                //   Open (half=2) vs filled (quarter=4, eighth=8, etc.) can interlock;
                //   same group (half+half or quarter+quarter) cannot.
                bool upIsOpen = upNoteValue == 2;   // half note = open notehead
                bool downIsOpen = downNoteValue == 2;
                bool differentHeadGroups = upIsOpen != downIsOpen;

                bool canMesh = upNoteValue >= 2 && downNoteValue >= 2
                             && differentHeadGroups
                             && upStaffPositions.Count == 1 && downStaffPositions.Count == 1;

                if (canMesh)
                {
                    // Use meshing shift: noteheads interlock tightly
                    bool hasDots = upDots > 0 || downDots > 0;
                    shiftAmount = hasDots
                        ? _params.MeshingDottedShift
                        : _params.MeshingGeneralShift;
                    type = CollisionType.CloseHalf;
                }
                else if (differentHeadGroups)
                {
                    // LILYPOND-REF: lily/note-collision.cc:297-312
                    // Different head groups but chords (can't fully mesh):
                    // partial interlocking with standard half collision shift.
                    shiftAmount = closeHalf
                        ? _params.CloseHalfShift
                        : _params.DistantHalfShift;
                    type = CollisionType.CloseHalf;
                }
                else
                {
                    // LILYPOND-REF: lily/note-collision.cc:326 close_half_collide (0.52).
                    // Same head groups cannot interlock; the symmetric +/-inner shift
                    // (pinned by the consumer) yields a 2*0.52 ~= 1.0w separation, so the
                    // two heads sit side by side. (The old magic 1.0 here was the
                    // pre-doubled single-sided value for the left-edge frame.)
                    shiftAmount = _params.CloseHalfShift;
                    type = CollisionType.CloseHalf;
                }
            }
            else
            {
                shiftAmount = _params.DistantHalfShift;
                type = CollisionType.CloseHalf;
            }

            // LILYPOND-REF: lily/note-collision.cc automatic_shift (d*offset) +
            // calc_positioning_done (translate amount - left_most): the columns
            // shift symmetrically (+inner up / -inner down) and the consumer pins
            // the leftmost group, so the 2-voice separation is 2*inner. (Lily#
            // previously shifted only one side, ~half of LilyPond's separation.)
            double upOffset = shiftUpRight ? shiftAmount : -shiftAmount;
            double downOffset = shiftUpRight ? -shiftAmount : shiftAmount;

            return new NoteCollisionInfo(type, upOffset, downOffset,
                downDotForceDown: downDotForceDown);
        }

        return NoteCollisionInfo.NoCollision;
    }

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

        // LILYPOND-REF: lily/note-collision.cc:252-261
        // Whole+half cannot merge (both open noteheads, only stem distinguishes)
        if ((upNoteValue == 1 && downNoteValue == 2) ||
            (upNoteValue == 2 && downNoteValue == 1))
            return false;

        // Check dot compatibility
        if (upDots != downDots && !_params.MergeDifferentlyDotted)
            return false;

        // LILYPOND-REF: lily/note-collision.cc:252-261
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
    /// LILYPOND-REF: lily/note-collision.cc:381-407 — head wipe
    /// LILYPOND-REF: lily/note-collision.cc:420-480 — multi-voice cascading
    /// For 3+ voices, each additional voice in the same stem direction gets a
    /// cumulative shift of 1 notehead width beyond the base collision offset.
    /// </remarks>
    public ImmutableArray<(int VoiceId, int ItemIndex, double XOffset, bool HeadTransparent, bool DotForceDown)> CalculateVoiceOffsets(
        VoiceColumn column,
        double noteheadWidth)
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

        // LILYPOND-REF: lily/note-collision.cc:420-480
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

        double leftMost = raw.Count > 0 ? raw.Min(r => r.Offset) : 0.0;
        foreach (var r in raw)
            offsets.Add((r.VoiceId, r.ItemIndex,
                (r.Offset - leftMost) * noteheadWidth, r.Hide, r.Dot));

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