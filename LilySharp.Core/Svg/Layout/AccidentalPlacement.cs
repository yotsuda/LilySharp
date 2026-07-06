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
/// Information about a positioned accidental within a chord.
/// </summary>
public readonly record struct AccidentalLayout(
    // Staff position of the note.
    int StaffPosition,
    // The accidental type (sharp, flat, natural, etc.).
    string Accidental,
    // X offset from the note column in staff spaces (negative = left of note).
    double XOffset,
    // Whether this is a courtesy (cautionary) accidental.
    bool IsCourtesy = false
);

/// <summary>
/// Parameters for accidental placement. All dimensions in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/accidental-placement.cc:393-439 position_apes
/// LILYPOND-REF: scm/define-grobs.scm:84 AccidentalPlacement
/// </remarks>
internal sealed record AccidentalPlacementParameters
{
    public static AccidentalPlacementParameters Default { get; } = new();

    /// <summary>Padding between accidental columns in staff spaces.</summary>
    /// <remarks>LILYPOND-REF: accidental-placement.cc:398,505 (hardcoded 0.2)</remarks>
    public double Padding { get; init; } = 0.2;

    /// <summary>Extra padding from note head in staff spaces.</summary>
    /// <remarks>LILYPOND-REF: define-grobs.scm:84 AccidentalPlacement.right-padding</remarks>
    public double RightPadding { get; init; } = 0.15;

    /// <summary>Y-axis padding for overlap detection in staff spaces.</summary>
    /// <remarks>LILYPOND-REF: accidental-placement.cc:413 horizon_padding</remarks>
    public double HorizonPadding { get; init; } = 0.1;

}

/// <summary>
/// Calculates accidental positions for chords following LilyPond's algorithm.
/// Uses skyline-based collision detection for precise placement.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/accidental-placement.cc
///
/// Algorithm:
/// 1. Build entries with glyph Y-extents at each note's staff position
/// 2. Sort by alteration priority (naturals rightmost, flats leftmost)
///    LILYPOND-REF: accidental-placement.cc:164-184 acc_less
/// 3. Build reference LeftSkyline from noteheads
///    LILYPOND-REF: accidental-placement.cc:355-370
/// 4. Position right-to-left: each accidental placed as close to notes
///    as possible without colliding with the reference skyline
///    LILYPOND-REF: accidental-placement.cc:393-439 position_apes
///
/// IMPLEMENTED — skyline-based collision (accidental-placement.cc:338-390)
/// IMPLEMENTED — octave-first priority sorting (accidental-placement.cc:164-184)
/// IMPLEMENTED — stagger_apes (accidental-placement.cc:261-336):
///   group accidentals by vertical proximity, reorder by group size, zigzag within size groups
/// IMPLEMENTED — same-note-name overstrike (accidental-placement.cc set_ape_skylines):
///   overstrike when same note name + same octave + same alteration
/// NOTE — editorial (AccidentalSuggestion) accidentals do NOT pass through this
///   class: per LilyPond they are placed ABOVE the note (define-grobs.scm:96-123,
///   direction UP), handled by the collector + ArticulationEngraver; the
///   left-of-note IsEditorial path below is retained but never fed.
/// </remarks>
internal sealed class AccidentalPlacement
{
    private readonly AccidentalPlacementParameters _params;

    public AccidentalPlacement(AccidentalPlacementParameters? parameters = null)
    {
        _params = parameters ?? AccidentalPlacementParameters.Default;
    }

    /// <summary>Internal entry for positioning calculations.</summary>
    private readonly record struct PlacementEntry(
        int StaffPosition,
        string Accidental,
        double YBottom,     // Lower bound in staff spaces
        double YTop,        // Upper bound in staff spaces
        double Width,       // Glyph width in staff spaces (includes paren width if courtesy)
        int Priority,       // Sorting priority: lower = rightmost
        bool IsCourtesy     // Whether this is a courtesy accidental
    );

    /// <summary>
    /// Calculates accidental positions for a chord.
    /// </summary>
    /// <param name="notes">The chord's notes.</param>
    /// <param name="headOffsets">Optional per-note head X displacement
    /// (parallel to <paramref name="notes"/>) from
    /// <see cref="ChordHeadPositioning"/> — reversed heads of seconds shift
    /// the note-column ink the accidentals must clear.
    /// LILYPOND-REF: lily/accidental-placement.cc:355-370 — the reference
    /// skyline is built from the heads' real (shifted) extents.</param>
    /// <param name="scale">Font-size scale for cue chords (LP CueVoice fontSize
    /// = -4). All dimensional quantities — glyph widths, paddings and the glyphs'
    /// Y-extents — scale by this; the staff-position Y centers do not (cue heads
    /// still sit on the real staff lines). 1.0 = normal size.</param>
    public ImmutableArray<AccidentalLayout> CalculatePositions(
        IReadOnlyList<ChordNoteInfo> notes, IReadOnlyList<double>? headOffsets = null,
        double scale = 1.0)
    {
        var accidentals = new List<(ChordNoteInfo Note, double HeadOffset)>();
        double minHeadOffset = 0;
        for (int i = 0; i < notes.Count; i++)
        {
            double off = headOffsets != null && i < headOffsets.Count ? headOffsets[i] : 0;
            minHeadOffset = Math.Min(minHeadOffset, off);
            if (!string.IsNullOrEmpty(notes[i].Accidental))
                accidentals.Add((notes[i], off));
        }

        if (accidentals.Count == 0)
            return ImmutableArray<AccidentalLayout>.Empty;

        if (accidentals.Count == 1)
        {
            var (n, _) = accidentals[0];
            double totalWidth = ComputeRenderedWidth(n.Accidental!, n.IsCourtesy) * scale;
            // A head reversed to the LEFT of the stem extends the column ink;
            // the accidental clears the leftmost head edge.
            return ImmutableArray.Create(new AccidentalLayout(
                n.StaffPosition, n.Accidental!,
                minHeadOffset - (totalWidth + _params.RightPadding * scale),
                n.IsCourtesy));
        }

        return CalculateMultipleAccidentals(accidentals, notes, headOffsets, scale);
    }

    /// <summary>
    /// Calculates position for a single note's accidental.
    /// </summary>
    public AccidentalLayout? CalculateSinglePosition(NoteItem note)
    {
        if (string.IsNullOrEmpty(note.Accidental))
            return null;

        double totalWidth = ComputeRenderedWidth(note.Accidental, note.IsCourtesy);
        return new AccidentalLayout(
            note.StaffPosition,
            note.Accidental,
            -(totalWidth + _params.RightPadding),
            note.IsCourtesy);
    }

    /// <summary>
    /// Computes the rendered width of an accidental glyph, accounting for
    /// courtesy parentheses.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/accidental.cc:35-46 — parenthesize().
    /// (Editorial/AccidentalSuggestion accidentals never reach this class —
    /// they print ABOVE the note via the articulation channel.)
    /// </remarks>
    private double ComputeRenderedWidth(string accidental, bool isCourtesy)
    {
        double baseWidth = GetAccidentalWidth(accidental);
        if (isCourtesy)
            return baseWidth + GlyphMetrics.AccidentalParensInkWidth;
        return baseWidth;
    }

    private ImmutableArray<AccidentalLayout> CalculateMultipleAccidentals(
        List<(ChordNoteInfo Note, double HeadOffset)> accidentalsWithOffsets,
        IReadOnlyList<ChordNoteInfo> allNotes,
        IReadOnlyList<double>? headOffsets,
        double scale)
    {
        var accidentals = accidentalsWithOffsets;
        // Build entries with glyph Y-extents. For cue chords (scale < 1) the glyph
        // extents and widths shrink, but each stays centered on the note's real
        // (unscaled) staff position.
        var entries = new List<PlacementEntry>(accidentals.Count);
        foreach (var (n, _) in accidentals)
        {
            var bbox = GlyphMetrics.GetAccidentalBBox(n.Accidental!);
            // Staff position is in half-spaces; convert to staff spaces
            double yCenterSS = n.StaffPosition / 2.0;

            double yBottom = yCenterSS + bbox.Bottom * scale;
            double yTop = yCenterSS + bbox.Top * scale;

            int priority = GetAlterationPriority(n.Accidental!);
            // LILYPOND-REF: lily/accidental.cc:35-46 — parenthesize() adds a paren glyph each side.
            double width = (n.IsCourtesy
                ? bbox.Width + GlyphMetrics.AccidentalParensInkWidth
                : bbox.Width) * scale;
            entries.Add(new PlacementEntry(
                n.StaffPosition, n.Accidental!, yBottom, yTop, width, priority,
                n.IsCourtesy));
        }

        // Sort by octave first, then alteration priority: naturals rightmost, flats leftmost
        // LILYPOND-REF: accidental-placement.cc:164-184 acc_less
        // Octave grouping ensures same-octave accidentals are placed together
        entries.Sort((a, b) =>
        {
            int octaveA = OctaveOf(a.StaffPosition);
            int octaveB = OctaveOf(b.StaffPosition);
            if (octaveA != octaveB)
                return octaveA.CompareTo(octaveB);
            return a.Priority.CompareTo(b.Priority);
        });

        // LILYPOND-REF: accidental-placement.cc:261-336 stagger_apes
        // Reorder entries: group by vertical proximity, sort groups by size (larger first),
        // zigzag within same-size groups. This ensures dense clusters stay close to noteheads.
        if (entries.Count > 2)
            entries = StaggerEntries(entries);

        // LILYPOND-REF: accidental-placement.cc:341-385 build_heads_skyline —
        // the reference skyline is built from ALL noteheads of the column
        // (not only the accidental-carrying ones), at their real X extents:
        // heads reversed to the LEFT of a down-stem (seconds) shift their box.
        // (LilyPond also adds the stems; for the LEFT skyline they never
        // protrude beyond the head boxes, so they are omitted here.)
        var noteheadBoxes = new List<(double YBottom, double YTop, double XLeft, double XRight)>();
        for (int i = 0; i < allNotes.Count; i++)
        {
            double headOffset = headOffsets != null && i < headOffsets.Count ? headOffsets[i] : 0;
            double yCenterSS = allNotes[i].StaffPosition / 2.0;
            var nhBBox = GlyphMetrics.NoteheadBlack;
            // headOffset arrives already cue-scaled (ChordHeadPositioning); the head
            // box's Y-extent and the right-padding scale with the cue font here.
            noteheadBoxes.Add((
                yCenterSS + nhBBox.Bottom * scale,
                yCenterSS + nhBBox.Top * scale,
                headOffset - _params.RightPadding * scale,
                headOffset));
        }
        var referenceSkyline = Skyline.FromBoxes(noteheadBoxes, Skyline.Direction.Left);

        // Position right-to-left with skyline collision avoidance
        // LILYPOND-REF: accidental-placement.cc:393-439 position_apes
        // LILYPOND-REF: accidental-placement.cc:338-390 skyline collision
        var layouts = new List<AccidentalLayout>(entries.Count);

        // Track placed accidentals for flat-merge overlap (special case not in skyline)
        var placedEntries = new List<(double LeftEdge, double YBottom, double YTop, string Accidental, double Width)>();

        // LILYPOND-REF: accidental-placement.cc set_ape_skylines()
        // In LilyPond, overstrike only applies WITHIN the same note-name group (APE).
        // Accidentals on different note names (e.g. D♮ vs F♮) are never overstriked.
        int lastOctave = int.MinValue;
        string lastAlteration = "";
        double lastXLeft = 0;
        int lastNoteName = int.MinValue;

        foreach (var entry in entries)
        {
            int octave = OctaveOf(entry.StaffPosition);
            // Note name class: staff positions with same value mod 7 are the same note name
            // in different octaves (e.g. D4 and D5)
            int noteName = ((entry.StaffPosition % 7) + 7) % 7;
            bool sameOctave = (octave == lastOctave);
            bool sameNoteName = (noteName == lastNoteName);

            // LILYPOND-REF: accidental-placement.cc set_ape_skylines()
            // Same note name + same octave + same alteration → overstrike (share X position)
            // This only applies within the same note-name group, matching LilyPond's APE architecture.
            if (sameNoteName && sameOctave && entry.Accidental == lastAlteration)
            {
                // LILYPOND-REF: accidental-placement.cc:292-296 — glyph-shape skyline.
                var overSkyline = AccidentalGlyphSkyline.Build(
                    entry.Accidental, entry.YBottom, entry.YTop,
                    lastXLeft, lastXLeft + entry.Width, Skyline.Direction.Left);
                referenceSkyline = referenceSkyline.Merge(overSkyline);

                layouts.Add(new AccidentalLayout(
                    entry.StaffPosition, entry.Accidental, lastXLeft,
                    entry.IsCourtesy));
                // LILYPOND-REF: last_octave and last_alteration always updated unconditionally
                lastOctave = octave;
                lastAlteration = entry.Accidental;
                lastNoteName = noteName;
                continue;
            }

            // Query the reference skyline for the leftmost boundary in this Y range
            // (with horizon padding for proximity tolerance)
            double yQueryBottom = entry.YBottom - _params.HorizonPadding * scale;
            double yQueryTop = entry.YTop + _params.HorizonPadding * scale;
            double skylineLeft = referenceSkyline.QueryXInRange(yQueryBottom, yQueryTop);

            // Start with skyline boundary (or rightPadding if skyline is empty in this range)
            double xRight = double.IsPositiveInfinity(skylineLeft)
                ? -_params.RightPadding * scale
                : skylineLeft - _params.Padding * scale;

            // LILYPOND-REF: accidental-placement.cc:290-295
            // Check for flat-merge overlap with previously placed flats
            if (entry.Accidental == "flat")
            {
                foreach (var placed in placedEntries)
                {
                    if (placed.Accidental == "flat" &&
                        entry.YBottom - _params.HorizonPadding * scale < placed.YTop &&
                        placed.YBottom - _params.HorizonPadding * scale < entry.YTop)
                    {
                        double overlap = Math.Min(entry.Width, placed.Width) * 0.375;
                        double maxRight = placed.LeftEdge - _params.Padding * scale + overlap;
                        if (maxRight < xRight)
                            xRight = maxRight;
                    }
                }
            }

            double xLeft = xRight - entry.Width;

            // LILYPOND-REF: accidental-placement.cc:292-296 — glyph-shape skyline (vs naive BBox).
            var accSkyline = AccidentalGlyphSkyline.Build(
                entry.Accidental, entry.YBottom, entry.YTop, xLeft, xRight, Skyline.Direction.Left);
            referenceSkyline = referenceSkyline.Merge(accSkyline);

            placedEntries.Add((xLeft, entry.YBottom, entry.YTop, entry.Accidental, entry.Width));
            layouts.Add(new AccidentalLayout(
                entry.StaffPosition, entry.Accidental, xLeft,
                entry.IsCourtesy));

            lastOctave = octave;
            lastAlteration = entry.Accidental;
            lastXLeft = xLeft;
            lastNoteName = noteName;
        }

        return layouts.ToImmutableArray();
    }

    /// <summary>
    /// Reorders accidental entries using the stagger algorithm.
    /// Groups accidentals by vertical proximity, orders groups by size (larger first),
    /// and applies zigzag within same-size groups.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/accidental-placement.cc:261-336 stagger_apes
    ///
    /// The algorithm ensures that:
    /// - Clusters of close accidentals (e.g., seconds in a chord) are placed as one group
    /// - Larger groups are placed first (rightmost, closest to noteheads)
    /// - Within same-size groups, zigzag alternation avoids linear alignment
    /// </remarks>
    private static List<PlacementEntry> StaggerEntries(List<PlacementEntry> entries)
    {
        // Step 1: Group entries by vertical proximity
        // Two entries are in the same group if their Y extents overlap or are within 1 staff space
        const double proximityThreshold = 1.0; // staff spaces

        var groups = new List<List<PlacementEntry>>();
        var currentGroup = new List<PlacementEntry> { entries[0] };

        for (int i = 1; i < entries.Count; i++)
        {
            // Check if this entry is close to any entry in the current group
            bool closeToGroup = false;
            foreach (var member in currentGroup)
            {
                if (entries[i].YBottom - proximityThreshold <= member.YTop &&
                    member.YBottom - proximityThreshold <= entries[i].YTop)
                {
                    closeToGroup = true;
                    break;
                }
            }

            if (closeToGroup)
            {
                currentGroup.Add(entries[i]);
            }
            else
            {
                groups.Add(currentGroup);
                currentGroup = new List<PlacementEntry> { entries[i] };
            }
        }
        groups.Add(currentGroup);

        if (groups.Count <= 1)
            return entries; // Single group, no staggering needed

        // Step 2: Sort groups by size descending (larger groups first = closer to noteheads)
        // Within same size, maintain original order
        groups = groups
            .Select((g, idx) => (Group: g, OriginalIndex: idx))
            .OrderByDescending(x => x.Group.Count)
            .ThenBy(x => x.OriginalIndex)
            .Select(x => x.Group)
            .ToList();

        // Step 3: Zigzag within same-size categories
        var result = new List<PlacementEntry>();
        int gi = 0;
        while (gi < groups.Count)
        {
            int currentSize = groups[gi].Count;
            var sameSize = new List<List<PlacementEntry>>();

            while (gi < groups.Count && groups[gi].Count == currentSize)
            {
                sameSize.Add(groups[gi]);
                gi++;
            }

            // Zigzag: alternate between back and front of same-size groups
            bool parity = true;
            int front = 0;
            int back = sameSize.Count - 1;

            while (front <= back)
            {
                if (parity)
                {
                    result.AddRange(sameSize[back]);
                    back--;
                }
                else
                {
                    result.AddRange(sameSize[front]);
                    front++;
                }
                parity = !parity;
            }
        }

        return result;
    }

    /// <summary>
    /// Gets alteration sorting priority.
    /// Lower values are placed first (rightmost, closest to notes).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: accidental-placement.cc acc_less(), set_ape_skylines()
    /// In LilyPond, acc_less sorts within APEs (same note-name groups):
    ///   naturals sort LAST, processed FIRST (rightmost) via backward iteration.
    /// For cross-note-name ordering, LilyPond uses stagger_apes (group size +
    /// skyline position), not alteration priority.
    /// In Lily#'s forward iteration, lower priority = processed first = rightmost.
    /// Naturals get priority 0 (rightmost) to match LilyPond's effective placement.
    /// </remarks>
    /// <summary>
    /// The diatonic octave of a staff position (7 positions per octave), using FLOORED
    /// division so it agrees with the floored note-name modulo below the middle line.
    /// C# integer division truncates toward zero, which would put e.g. position 3 and
    /// position -4 (one octave apart) in the same octave and wrongly overstrike them.
    /// </summary>
    private static int OctaveOf(int staffPosition) =>
        (int)Math.Floor(staffPosition / 7.0);

    private static int GetAlterationPriority(string accidental) => accidental switch
    {
        "natural" => 0,
        "sharp" => 1,
        "doubleSharp" => 2,
        "flat" => 3,
        "doubleFlat" => 4,
        _ => 2
    };

    // Single source of truth for the accidental BBox lives in GlyphMetrics; the
    // former private copy here diverged only in an unreachable fallback.
    private static double GetAccidentalWidth(string accidental) =>
        GlyphMetrics.GetAccidentalBBox(accidental).Width;
}
