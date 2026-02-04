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

    public NoteCollisionInfo(CollisionType type, double upStemXOffset, double downStemXOffset, bool shouldMerge = false)
    {
        Type = type;
        UpStemXOffset = upStemXOffset;
        DownStemXOffset = downStemXOffset;
        ShouldMerge = shouldMerge;
    }

    public static NoteCollisionInfo NoCollision { get; } = new(CollisionType.None, 0, 0);
}

/// <summary>
/// Parameters for note collision handling.
/// Based on Lilypond's note-collision.cc
/// </summary>
public sealed record NoteCollisionParameters
{
    public static NoteCollisionParameters Default { get; } = new();

    /// <summary>Threshold for considering notes as colliding (staff positions).</summary>
    public int CollisionThreshold { get; init; } = 1;

    /// <summary>Whether to merge differently dotted notes.</summary>
    public bool MergeDifferentlyDotted { get; init; } = false;

    /// <summary>Whether to merge differently headed notes (half vs quarter).</summary>
    public bool MergeDifferentlyHeaded { get; init; } = false;

    /// <summary>Whether to prefer dotted notes on the right side.</summary>
    public bool PreferDottedRight { get; init; } = true;

    /// <summary>Horizontal shift amount for close half collision (in notehead widths).</summary>
    public double CloseHalfShift { get; init; } = 1.0;

    /// <summary>Horizontal shift amount for distant half collision (in notehead widths).</summary>
    public double DistantHalfShift { get; init; } = 1.0;

    /// <summary>Horizontal shift amount for full collision (in notehead widths).</summary>
    public double FullCollideShift { get; init; } = 1.0;

    /// <summary>Horizontal shift amount for touch condition (in notehead widths).</summary>
    public double TouchShift { get; init; } = 0.5;
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
            // Can merge - no shift needed
            return new NoteCollisionInfo(CollisionType.Merge, 0, 0, shouldMerge: true);
        }

        if (touch && !fullCollide && !closeHalf && !distantHalf)
        {
            // Just touching - stems can align (with small shift per Lilypond line 295-296)
            double touchShift = _params.TouchShift;
            double upOffset = shiftUpRight ? touchShift : 0;
            double downOffset = shiftUpRight ? 0 : -touchShift;
            return new NoteCollisionInfo(CollisionType.Touch, upOffset, downOffset);
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
            else if (closeHalf)
            {
                shiftAmount = _params.CloseHalfShift;
                type = CollisionType.CloseHalf;
            }
            else // distantHalf
            {
                shiftAmount = _params.DistantHalfShift;
                type = CollisionType.CloseHalf; // Use CloseHalf type for distant too
            }

            // Apply shift direction
            double upOffset = shiftUpRight ? shiftAmount : 0;
            double downOffset = shiftUpRight ? 0 : -shiftAmount;

            return new NoteCollisionInfo(type, upOffset, downOffset);
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

        // Cannot merge quarter and half (indistinguishable)
        if ((upNoteValue == 1 && downNoteValue == 2) ||
            (upNoteValue == 2 && downNoteValue == 1))
            return false;

        // Check dot compatibility
        if (upDots != downDots && !_params.MergeDifferentlyDotted)
            return false;

        // Check note value compatibility
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
    /// Returns (VoiceId, ItemIndex, XOffset) for each entry in the column.
    /// </summary>
    public ImmutableArray<(int VoiceId, int ItemIndex, double XOffset)> CalculateVoiceOffsets(
        VoiceColumn column,
        double noteheadWidth)
    {
        var offsets = new List<(int VoiceId, int ItemIndex, double XOffset)>();

        // Group entries by stem direction
        var upEntries = column.Entries.Where(e => GetStemDirection(e) == true).ToList();
        var downEntries = column.Entries.Where(e => GetStemDirection(e) == false).ToList();

        if (upEntries.Count == 0 || downEntries.Count == 0)
        {
            // No collision possible - all voices get 0 offset
            foreach (var entry in column.Entries)
            {
                offsets.Add((entry.VoiceId, entry.ItemIndex, 0));
            }
            return offsets.ToImmutableArray();
        }

        // Get staff positions for each group
        var upPositions = GetStaffPositions(upEntries);
        var downPositions = GetStaffPositions(downEntries);

        // Get note values and dots (use first entry as representative)
        var (upNoteValue, upDots) = GetNoteInfo(upEntries[0]);
        var (downNoteValue, downDots) = GetNoteInfo(downEntries[0]);

        // Analyze collision
        var collision = AnalyzeCollision(upPositions, downPositions, upNoteValue, downNoteValue, upDots, downDots);

        // Apply offsets
        foreach (var entry in upEntries)
        {
            offsets.Add((entry.VoiceId, entry.ItemIndex, collision.UpStemXOffset * noteheadWidth));
        }
        foreach (var entry in downEntries)
        {
            offsets.Add((entry.VoiceId, entry.ItemIndex, collision.DownStemXOffset * noteheadWidth));
        }

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