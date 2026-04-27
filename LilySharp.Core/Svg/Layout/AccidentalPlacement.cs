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
    /// <summary>Staff position of the note.</summary>
    int StaffPosition,
    /// <summary>The accidental type (sharp, flat, natural, etc.).</summary>
    string Accidental,
    /// <summary>X offset from the note column in staff spaces (negative = left of note).</summary>
    double XOffset,
    /// <summary>Whether this is a courtesy (cautionary) accidental.</summary>
    bool IsCourtesy = false,
    /// <summary>
    /// Whether this is an editorial (suggested) accidental, rendered in parentheses.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/accidental.cc:130-166
    /// Editorial accidentals are suggestions (e.g., musica ficta) and are
    /// rendered smaller or in parentheses to distinguish from required accidentals.
    /// </remarks>
    bool IsEditorial = false
);

/// <summary>
/// Parameters for accidental placement. All dimensions in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/accidental-placement.cc:393-439 position_apes
/// LILYPOND-REF: scm/define-grobs.scm:84 AccidentalPlacement
/// </remarks>
public sealed record AccidentalPlacementParameters
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

    /// <summary>
    /// Font-size factor applied to editorial (suggestion) accidentals. LP renders
    /// AccidentalSuggestion with font-size = -3 ⇒ magstep ≈ 2^(-3/6) ≈ 0.707, but
    /// the documented engraving convention uses 0.6 for clarity.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/accidental.cc:130-166 — AccidentalSuggestion print
    /// LILYPOND-REF: scm/define-grobs.scm AccidentalSuggestion (font-size . -3)
    /// </remarks>
    public double EditorialFontFactor { get; init; } = 0.6;
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
/// IMPLEMENTED — AccidentalSuggestion/editorial (accidental.cc:130-166): smaller glyph
///   (EditorialFontFactor) wrapped in parentheses, IsEditorial flag propagated to layout.
/// </remarks>
public sealed class AccidentalPlacement
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
        double Width,       // Glyph width in staff spaces (includes paren width if courtesy/editorial)
        int Priority,       // Sorting priority: lower = rightmost
        bool IsCourtesy,    // Whether this is a courtesy accidental
        bool IsEditorial    // Whether this is an editorial (suggestion) accidental
    );

    /// <summary>
    /// Calculates accidental positions for a chord.
    /// </summary>
    public ImmutableArray<AccidentalLayout> CalculatePositions(IReadOnlyList<ChordNoteInfo> notes)
    {
        var accidentals = notes
            .Where(n => !string.IsNullOrEmpty(n.Accidental))
            .ToList();

        if (accidentals.Count == 0)
            return ImmutableArray<AccidentalLayout>.Empty;

        if (accidentals.Count == 1)
        {
            var n = accidentals[0];
            double totalWidth = ComputeRenderedWidth(n.Accidental!, n.IsCourtesy, n.IsEditorial);
            return ImmutableArray.Create(new AccidentalLayout(
                n.StaffPosition, n.Accidental!, -(totalWidth + _params.RightPadding),
                n.IsCourtesy, n.IsEditorial));
        }

        return CalculateMultipleAccidentals(accidentals);
    }

    /// <summary>
    /// Calculates position for a single note's accidental.
    /// </summary>
    public AccidentalLayout? CalculateSinglePosition(NoteItem note)
    {
        if (string.IsNullOrEmpty(note.Accidental))
            return null;

        double totalWidth = ComputeRenderedWidth(note.Accidental, note.IsCourtesy, note.IsEditorial);
        return new AccidentalLayout(
            note.StaffPosition,
            note.Accidental,
            -(totalWidth + _params.RightPadding),
            note.IsCourtesy,
            note.IsEditorial);
    }

    /// <summary>
    /// Computes the rendered width of an accidental glyph, accounting for
    /// courtesy parentheses and editorial size reduction.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/accidental.cc:130-166 — parenthesize() + AccidentalSuggestion
    /// Editorial accidentals are rendered smaller (factor <see cref="AccidentalPlacementParameters.EditorialFontFactor"/>)
    /// AND wrapped in parentheses (just like courtesy). Courtesy alone keeps full size.
    /// </remarks>
    private double ComputeRenderedWidth(string accidental, bool isCourtesy, bool isEditorial)
    {
        double baseWidth = GetAccidentalWidth(accidental);
        if (isEditorial)
            baseWidth *= _params.EditorialFontFactor;
        // Both courtesy and editorial wrap in parens.
        if (isCourtesy || isEditorial)
            return baseWidth + 2 * GlyphMetrics.AccidentalParenWidth;
        return baseWidth;
    }

    private ImmutableArray<AccidentalLayout> CalculateMultipleAccidentals(
        List<ChordNoteInfo> accidentals)
    {
        // Build entries with glyph Y-extents
        var entries = new List<PlacementEntry>(accidentals.Count);
        foreach (var n in accidentals)
        {
            var bbox = GetAccidentalBBox(n.Accidental!);
            // Staff position is in half-spaces; convert to staff spaces
            double yCenterSS = n.StaffPosition / 2.0;

            // LILYPOND-REF: lily/accidental.cc:130-166 — editorial accidentals are scaled by font-size factor.
            double scale = n.IsEditorial ? _params.EditorialFontFactor : 1.0;
            double yBottom = yCenterSS + bbox.Bottom * scale;
            double yTop = yCenterSS + bbox.Top * scale;
            double scaledGlyphWidth = bbox.Width * scale;

            int priority = GetAlterationPriority(n.Accidental!);
            // LILYPOND-REF: lily/accidental.cc:35-46 — parenthesize() adds a paren glyph each side.
            double width = (n.IsCourtesy || n.IsEditorial)
                ? scaledGlyphWidth + 2 * GlyphMetrics.AccidentalParenWidth
                : scaledGlyphWidth;
            entries.Add(new PlacementEntry(
                n.StaffPosition, n.Accidental!, yBottom, yTop, width, priority,
                n.IsCourtesy, n.IsEditorial));
        }

        // Sort by octave first, then alteration priority: naturals rightmost, flats leftmost
        // LILYPOND-REF: accidental-placement.cc:164-184 acc_less
        // Octave grouping ensures same-octave accidentals are placed together
        entries.Sort((a, b) =>
        {
            int octaveA = a.StaffPosition / 7;
            int octaveB = b.StaffPosition / 7;
            if (octaveA != octaveB)
                return octaveA.CompareTo(octaveB);
            return a.Priority.CompareTo(b.Priority);
        });

        // LILYPOND-REF: accidental-placement.cc:261-336 stagger_apes
        // Reorder entries: group by vertical proximity, sort groups by size (larger first),
        // zigzag within same-size groups. This ensures dense clusters stay close to noteheads.
        if (entries.Count > 2)
            entries = StaggerEntries(entries);

        // LILYPOND-REF: accidental-placement.cc:355-370
        // Build reference LeftSkyline from notehead column boundary.
        // The reference skyline represents the left edge of the note column
        // that accidentals must not cross.
        var noteheadBoxes = new List<(double YBottom, double YTop, double XLeft, double XRight)>();
        foreach (var n in accidentals)
        {
            double yCenterSS = n.StaffPosition / 2.0;
            var nhBBox = GlyphMetrics.NoteheadBlack;
            noteheadBoxes.Add((
                yCenterSS + nhBBox.Bottom,
                yCenterSS + nhBBox.Top,
                -_params.RightPadding,
                0));
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
            int octave = entry.StaffPosition / 7;
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
                    entry.IsCourtesy, entry.IsEditorial));
                // LILYPOND-REF: last_octave and last_alteration always updated unconditionally
                lastOctave = octave;
                lastAlteration = entry.Accidental;
                lastNoteName = noteName;
                continue;
            }

            // Query the reference skyline for the leftmost boundary in this Y range
            // (with horizon padding for proximity tolerance)
            double yQueryBottom = entry.YBottom - _params.HorizonPadding;
            double yQueryTop = entry.YTop + _params.HorizonPadding;
            double skylineLeft = referenceSkyline.QueryXInRange(yQueryBottom, yQueryTop);

            // Start with skyline boundary (or rightPadding if skyline is empty in this range)
            double xRight = double.IsPositiveInfinity(skylineLeft)
                ? -_params.RightPadding
                : skylineLeft - _params.Padding;

            // LILYPOND-REF: accidental-placement.cc:290-295
            // Check for flat-merge overlap with previously placed flats
            if (entry.Accidental == "flat")
            {
                foreach (var placed in placedEntries)
                {
                    if (placed.Accidental == "flat" &&
                        entry.YBottom - _params.HorizonPadding < placed.YTop &&
                        placed.YBottom - _params.HorizonPadding < entry.YTop)
                    {
                        double overlap = Math.Min(entry.Width, placed.Width) * 0.375;
                        double maxRight = placed.LeftEdge - _params.Padding + overlap;
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
                entry.IsCourtesy, entry.IsEditorial));

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
    private static int GetAlterationPriority(string accidental) => accidental switch
    {
        "natural" => 0,
        "sharp" => 1,
        "doubleSharp" => 2,
        "flat" => 3,
        "doubleFlat" => 4,
        _ => 2
    };

    private static double GetAccidentalWidth(string accidental) =>
        GetAccidentalBBox(accidental).Width;

    private static GlyphMetrics.BBox GetAccidentalBBox(string accidental) => accidental switch
    {
        "sharp" => GlyphMetrics.AccidentalSharp,
        "flat" => GlyphMetrics.AccidentalFlat,
        "natural" => GlyphMetrics.AccidentalNatural,
        "doubleSharp" => GlyphMetrics.AccidentalDoubleSharp,
        "doubleFlat" => GlyphMetrics.AccidentalDoubleFlat,
        _ => GlyphMetrics.AccidentalSharp
    };
}
