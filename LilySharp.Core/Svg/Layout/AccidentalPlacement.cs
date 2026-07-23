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
///    LILYPOND-REF: accidental-placement.cc:375-385
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
///   direction UP), handled by the collector + ArticulationEngraver.
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
    /// LILYPOND-REF: lily/accidental-placement.cc:375-385 — the reference
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
        for (int i = 0; i < notes.Count; i++)
        {
            double off = headOffsets != null && i < headOffsets.Count ? headOffsets[i] : 0;
            if (!string.IsNullOrEmpty(notes[i].Accidental))
                accidentals.Add((notes[i], off));
        }

        if (accidentals.Count == 0)
            return ImmutableArray<AccidentalLayout>.Empty;

        // Everything — a single accidental included — goes through the skyline packer:
        // LilyPond runs position_apes even for one accidental, so a lone accidental clears
        // the note by right-padding (0.15) PLUS padding (0.2) = 0.35, not 0.15 alone.
        return CalculateMultipleAccidentals(accidentals, notes, headOffsets, scale);
    }

    /// <summary>
    /// Calculates position for a single note's accidental.
    /// </summary>
    public AccidentalLayout? CalculateSinglePosition(NoteItem note)
    {
        if (string.IsNullOrEmpty(note.Accidental))
            return null;

        // One accidental against one note at the column origin — the single-ape case of
        // position_apes, so the glyph clears the head by right-padding + padding = 0.35.
        // LILYPOND-REF: accidental-placement.cc:391-438 position_apes.
        var nhBBox = GlyphMetrics.NoteheadBlack;
        double yCenterSS = note.StaffPosition / 2.0;
        var reference = HorizontalSkyline.FromBox(
            yCenterSS + nhBBox.Bottom, yCenterSS + nhBBox.Top, 0, nhBBox.Width,
            HorizontalDirection.Left);
        reference.Raise(-_params.RightPadding);

        var (_, glyphRight) = GlyphSkylinePair(note.Accidental, note.IsCourtesy, 1.0);
        glyphRight.Shift(yCenterSS);
        double offset = -glyphRight.Distance(reference, _params.HorizonPadding);
        if (double.IsInfinity(offset)) offset = 0; else offset -= _params.Padding;

        var bbox = GlyphMetrics.GetAccidentalBBox(note.Accidental);
        return new AccidentalLayout(
            note.StaffPosition, note.Accidental,
            InkLeft(offset, bbox.Left, note.IsCourtesy, 1.0), note.IsCourtesy);
    }

    /// <summary>
    /// The accidental's ink-left X (the value <see cref="AccidentalLayout.XOffset"/> carries):
    /// the glyph origin sits at <paramref name="offset"/>, so the LILC left edge is
    /// offset + bbox.Left; a courtesy accidental adds its left-parenthesis width, which draws
    /// (and is packed) that much further left again.
    /// </summary>
    private static double InkLeft(double offset, double bboxLeft, bool isCourtesy, double scale)
    {
        double inkLeft = offset + bboxLeft * scale;
        if (isCourtesy)
            inkLeft -= GlyphMetrics.AccidentalLeftParen.Width * scale;
        return inkLeft;
    }

    /// <summary>
    /// The accidental glyph's (LEFT, RIGHT) outline skyline pair, freshly cloned so the
    /// caller may mutate it, cue-scaled about the origin. Courtesy accidentals add the
    /// parenthesis ink as a box on each side, since LilyPond parenthesizes the stencil and
    /// the parens then join the skyline.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/accidental.cc:48 horizontal_skylines; :35-46 parenthesize.</remarks>
    private static (HorizontalSkyline Left, HorizontalSkyline Right) GlyphSkylinePair(
        string accidental, bool isCourtesy, double scale)
    {
        var (bakedLeft, bakedRight) = GlyphMetrics.AccidentalSkylinePair(accidental);
        var left = bakedLeft.Clone();
        var right = bakedRight.Clone();
        if (isCourtesy)
        {
            var bbox = GlyphMetrics.GetAccidentalBBox(accidental);
            double lpW = GlyphMetrics.AccidentalLeftParen.Width;
            double rpW = GlyphMetrics.AccidentalRightParen.Width;
            left.Merge(HorizontalSkyline.FromBox(
                bbox.Bottom, bbox.Top, bbox.Left - lpW, bbox.Left, HorizontalDirection.Left));
            right.Merge(HorizontalSkyline.FromBox(
                bbox.Bottom, bbox.Top, bbox.Right, bbox.Right + rpW, HorizontalDirection.Right));
        }
        if (scale != 1.0)
        {
            left.Scale(scale);
            right.Scale(scale);
        }
        return (left, right);
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

        // Processing order = rightmost (closest to the notes) placed FIRST. Naturals sort
        // rightmost (priority 0), so they are never mistaken for cancellation naturals; among
        // equal alterations the HIGHER accidental is placed first, so the pair interlocks into
        // LilyPond's C-shape (the lower one, placed to its left, tucks under). Processing the
        // lower one first instead leaves the skylines box-far apart — the whole point of the
        // real-outline nesting. LILYPOND-REF: accidental-placement.cc:130-146 ape_priority /
        // ape_less (higher skyline is placed nearer the notes); :164-181 acc_less (naturals
        // largest, i.e. rightmost).
        entries.Sort((a, b) =>
        {
            if (a.Priority != b.Priority)
                return a.Priority.CompareTo(b.Priority);
            return b.StaffPosition.CompareTo(a.StaffPosition);
        });

        // LILYPOND-REF: accidental-placement.cc:192-235 stagger_apes
        // Reorder entries: group by vertical proximity, sort groups by size (larger first),
        // zigzag within same-size groups. This ensures dense clusters stay close to noteheads.
        if (entries.Count > 2)
            entries = StaggerEntries(entries);

        // LILYPOND-REF: accidental-placement.cc:375-385 build_heads_skyline — the reference
        // LEFT skyline is built from ALL noteheads of the column (not only the
        // accidental-carrying ones), at their real X extents; heads reversed to the LEFT of a
        // down-stem (seconds) shift their box. (LilyPond also adds the stems; for the LEFT
        // skyline they never protrude beyond the head boxes, so they are omitted here.)
        var headBoxes = new List<(double YBottom, double YTop, double XLeft, double XRight)>();
        for (int i = 0; i < allNotes.Count; i++)
        {
            double headOffset = headOffsets != null && i < headOffsets.Count ? headOffsets[i] : 0;
            double yCenterSS = allNotes[i].StaffPosition / 2.0;
            var nhBBox = GlyphMetrics.NoteheadBlack;
            // headOffset arrives already cue-scaled (ChordHeadPositioning); the head box's
            // Y-extent scales with the cue font here.
            headBoxes.Add((
                yCenterSS + nhBBox.Bottom * scale,
                yCenterSS + nhBBox.Top * scale,
                headOffset,
                headOffset + nhBBox.Width * scale));
        }
        // LILYPOND-REF: accidental-placement.cc:398-400 — left_skyline = heads; raise by
        // -right-padding (0.15) so accidentals keep that much clear of the notes.
        var reference = HorizontalSkyline.FromBoxes(headBoxes, HorizontalDirection.Left);
        reference.Raise(-_params.RightPadding * scale);

        // Position right-to-left with skyline-to-skyline nesting.
        // LILYPOND-REF: accidental-placement.cc:391-438 position_apes.
        var layouts = new List<AccidentalLayout>(entries.Count);

        // LILYPOND-REF: accidental-placement.cc set_ape_skylines() — accidentals of the SAME
        // note name form one APE sharing a SINGLE column, whatever the octave. The first of
        // each note-name+alteration is positioned against the reference; later ones at that
        // note name snap to the SAME column (same octave overstrikes, different octaves align
        // vertically — C♯4 and C♯5 sit in one column, as LilyPond draws them).
        var apeColumn = new Dictionary<(int NoteName, string Accidental), double>();
        double lastOffset = 0.0;

        foreach (var entry in entries)
        {
            int noteName = ((entry.StaffPosition % 7) + 7) % 7;
            var apeKey = (noteName, entry.Accidental);
            var bbox = GlyphMetrics.GetAccidentalBBox(entry.Accidental);
            double yCenterSS = entry.StaffPosition / 2.0;

            // The glyph's own LEFT/RIGHT outline skylines, cue-scaled and lifted onto the
            // note's staff position (the Y centre does not scale — cue heads sit on the real
            // staff lines). LILYPOND-REF: accidental-placement.cc:292-295 set_ape_skylines.
            var (glyphLeft, glyphRight) = GlyphSkylinePair(entry.Accidental, entry.IsCourtesy, scale);
            glyphLeft.Shift(yCenterSS);
            glyphRight.Shift(yCenterSS);

            double offset;
            if (apeColumn.TryGetValue(apeKey, out double sharedOffset))
            {
                offset = sharedOffset;
            }
            else
            {
                // LILYPOND-REF: accidental-placement.cc:411-416 — nest the RIGHT skyline
                // against the accumulated LEFT skyline (horizon padding 0.1), then back off
                // by the inter-column padding (0.2).
                offset = -glyphRight.Distance(reference, _params.HorizonPadding * scale);
                if (double.IsInfinity(offset))
                    offset = lastOffset;
                else
                    offset -= _params.Padding * scale;
                apeColumn[apeKey] = offset;
                lastOffset = offset;
            }

            // LILYPOND-REF: accidental-placement.cc:418-421 — the new LEFT skyline is this
            // accidental's LEFT skyline shifted into place, merged over the old one.
            glyphLeft.Raise(offset);
            glyphLeft.Merge(reference);
            reference = glyphLeft;

            // XOffset is the whole accidental's ink-left (what DrawAccidentalAtInkLeft and the
            // reservation boxes anchor to): the glyph origin lands at `offset`, so its LILC
            // left edge is offset + bbox.Left. A courtesy accidental's left parenthesis draws
            // that much further left again (DrawAccidentalAtInkLeft: accInkLeft = inkLeftX +
            // leftParen.Width) and its box is already packed there, so the anchor must be the
            // group left, not the bare glyph's. All cue-scaled.
            layouts.Add(new AccidentalLayout(
                entry.StaffPosition, entry.Accidental,
                InkLeft(offset, bbox.Left, entry.IsCourtesy, scale), entry.IsCourtesy));
        }

        return layouts.ToImmutableArray();
    }

    /// <summary>
    /// Reorders accidental entries using the stagger algorithm.
    /// Groups accidentals by vertical proximity, orders groups by size (larger first),
    /// and applies zigzag within same-size groups.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/accidental-placement.cc:192-235 stagger_apes
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
    /// LILYPOND-REF: accidental-placement.cc acc_less() — naturals sort largest so they are
    /// never confused with cancellation naturals; they are placed rightmost (priority 0 here).
    /// </remarks>
    private static int GetAlterationPriority(string accidental) => accidental switch
    {
        "natural" => 0,
        "sharp" => 1,
        "doubleSharp" => 2,
        "flat" => 3,
        "doubleFlat" => 4,
        _ => 2
    };
}
