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
/// Parameters for lyric layout calculation.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:3020-3060 LyricText grob
/// LILYPOND-REF: lily/lyric-engraver.cc:20-30 default parameters
/// </remarks>
public sealed record LyricParameters
{
    /// <summary>Distance below the staff in staff spaces.</summary>
    public double StaffPadding { get; init; } = 2.5;

    /// <summary>Minimum distance between syllables in staff spaces.</summary>
    public double MinSyllableSpacing { get; init; } = 0.5;

    /// <summary>Font size relative to staff space.</summary>
    public double FontSize { get; init; } = 1.2;

    /// <summary>Hyphen character width estimate (in staff spaces).</summary>
    public double HyphenWidth { get; init; } = 0.4;

    /// <summary>Minimum hyphen length before it's drawn (in staff spaces).</summary>
    public double MinHyphenLength { get; init; } = 0.3;

    /// <summary>Padding between syllable and hyphen (in staff spaces).</summary>
    public double HyphenPadding { get; init; } = 0.2;

    /// <summary>Extender line thickness (in staff spaces).</summary>
    public double ExtenderThickness { get; init; } = 0.04;

    /// <summary>Additional distance between lyric lines for multiple verses.</summary>
    public double VerseSpacing { get; init; } = 1.8;

    public static LyricParameters Default { get; } = new();
}

/// <summary>
/// Calculates lyric layout positions.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/lyric-engraver.cc:60-150 process_music, stop_translation_timestep
/// LILYPOND-REF: lily/lyric-combine-music-iterator.cc:100-200 note-lyric association
///
/// Lyrics are positioned:
/// - Horizontally: Centered under the associated note
/// - Vertically: Below the staff, with multiple verses stacked
///
/// Hyphens connect syllables of the same word.
/// Extenders indicate melisma (one syllable over multiple notes).
/// </remarks>
public sealed class LyricEngraver
{
    private readonly LyricParameters _params;

    public LyricEngraver(LyricParameters? parameters = null)
    {
        _params = parameters ?? LyricParameters.Default;
    }

    /// <summary>
    /// Distance from a lyric's anchor (the vertical MIDLINE — lyrics are drawn
    /// TextAnchor.Middle, see SharedRenderer.DrawLyrics) up to the TOP of the glyph.
    /// This is the lyric grob's real up-extent, used to build the lyric line's
    /// up-skyline box [anchor − topExtent, anchor] that the staff down-skyline must
    /// clear. The visible gap between text and note then equals
    /// <see cref="RelatedStaffPadding"/>, independent of this value.
    /// </summary>
    private const double LyricTextTopExtent = 0.76;

    /// <summary>
    /// LilyPond Lyrics relatedstaff-spacing padding (ly/engraver-init.ly:651): the
    /// gap left between the lyric up-skyline and the staff down-skyline when a note
    /// pokes far enough below that the skyline distance beats the basic-distance.
    /// </summary>
    private const double RelatedStaffPadding = 0.5;

    /// <summary>skyline-horizontal-padding for the lyric/staff skyline distance.</summary>
    private const double HorizonPadding = 0.1;

    /// <summary>Minimum X-width of a syllable's skyline box (narrow glyphs).</summary>
    private const double MinSyllableBoxWidth = 0.8;


    /// <summary>
    /// Calculate layouts for all lyrics in a score.
    /// </summary>
    /// <param name="lyrics">Collection of lyric items.</param>
    /// <param name="measureLayouts">Measure layout information for note positions.</param>
    /// <param name="staffBottom">Y position of the bottom staff line (in staff spaces).</param>
    /// <param name="systems">Systems, to map a lyric to its system's down-skyline.</param>
    /// <param name="systemSkylines">
    /// Per-system up/down skylines (1:1 with <paramref name="systems"/>). When given,
    /// each system's lyric line is lowered so the TEXT clears notes/ledger lines that
    /// poke below the staff — LilyPond places the Lyrics line at
    /// max(basic-distance, down-skyline + lyric-extent), the staff-affinity-UP
    /// VerticalAxisGroup spacing (engraver-init.ly:648-652).
    /// </param>
    public ImmutableArray<LyricLayout> CalculateLayouts(
        IReadOnlyList<LyricItem> lyrics,
        IReadOnlyList<MeasureLayout> measureLayouts,
        double staffBottom,
        ImmutableArray<SystemLayout> systems = default,
        IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)>? systemSkylines = null)
    {
        if (lyrics.Count == 0)
            return ImmutableArray<LyricLayout>.Empty;

        var layouts = new List<LyricLayout>();

        // Group lyrics by verse number
        var verseGroups = lyrics.GroupBy(l => l.VerseNumber).OrderBy(g => g.Key);

        foreach (var verseGroup in verseGroups)
        {
            int verseNumber = verseGroup.Key;
            var verseLyrics = verseGroup.ToList();

            // Calculate Y position for this verse
            // LILYPOND-REF: lily/lyric-engraver.cc:85-95 vertical positioning
            double verseY = staffBottom + _params.StaffPadding +
                           (verseNumber - 1) * _params.VerseSpacing;

            var verseLayouts = new List<LyricLayout>();
            for (int i = 0; i < verseLyrics.Count; i++)
            {
                var lyric = verseLyrics[i];
                var layout = CalculateSyllableLayout(
                    lyric,
                    measureLayouts,
                    verseY,
                    i + 1 < verseLyrics.Count ? verseLyrics[i + 1] : null);

                if (layout != null)
                    verseLayouts.Add(layout);
            }

            // Apply collision avoidance for this verse
            // LILYPOND-REF: lily/lyric-engraver.cc:120-140 collision handling
            verseLayouts = ResolveOverlaps(verseLayouts);
            layouts.AddRange(verseLayouts);
        }

        // Lower each system's lyric line so the TEXT clears notes/ledger lines
        // poking below the staff (LilyPond's max(basic-distance, skyline)).
        if (systemSkylines != null && !systems.IsDefaultOrEmpty)
            layouts = ApplySkylineDrop(layouts, systems, systemSkylines, staffBottom);

        return layouts.ToImmutableArray();
    }

    /// <summary>
    /// Places each system's lyric line below the staff at the LilyPond distance
    ///   realized = max(basic-distance, staffDownSkyline.distance(lyricUpSkyline) + padding)
    /// — Align_interface's per-pair spacing (align-interface.cc:222-275,
    /// page-layout-problem.cc:625-629). The lyric line's UP-skyline is built from
    /// the REAL text boxes of its (verse-1) syllables, so the skyline distance is
    /// the true glyph-to-note clearance, not a single-point estimate. basic-distance
    /// (the fixed floor) wins for ordinary music — so common snapshots are
    /// untouched and only notes poking far below drop the line, with the LP padding
    /// gap. The whole line (all verses) shifts together, preserving verse stacking.
    /// </summary>
    private List<LyricLayout> ApplySkylineDrop(
        List<LyricLayout> layouts, ImmutableArray<SystemLayout> systems,
        IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)> systemSkylines,
        double staffBottom)
    {
        var measureToSystem = new Dictionary<int, int>();
        for (int s = 0; s < systems.Length; s++)
            foreach (var m in systems[s].Measures)
                measureToSystem[m.MeasureIndex] = s;

        double basic = staffBottom + _params.StaffPadding;

        // Build each system's lyric UP-skyline from the verse-1 syllable boxes,
        // self-relative to the line's anchor (anchor at y=0; text top at -topExtent
        // above it, so the UP-skyline height there is +topExtent).
        var lyricUp = new Dictionary<int, VerticalSkyline>();
        foreach (var lay in layouts)
        {
            if (lay.Item.VerseNumber > 1) continue; // verse 1 is the line's top edge
            if (!measureToSystem.TryGetValue(lay.Item.MeasureIndex, out int s))
                continue;
            double halfW = Math.Max(lay.Width, MinSyllableBoxWidth) / 2.0;
            var box = VerticalSkyline.FromBox(
                lay.X - halfW, lay.X + halfW, 0, -LyricTextTopExtent, VerticalDirection.Up);
            if (lyricUp.TryGetValue(s, out var sky)) sky.Merge(box);
            else lyricUp[s] = box;
        }

        var systemDrop = new Dictionary<int, double>();
        foreach (var (s, up) in lyricUp)
        {
            if (s >= systemSkylines.Count) continue;
            var down = systemSkylines[s].down;
            if (down.IsEmpty || up.IsEmpty) continue;
            double dist = down.Distance(up, HorizonPadding);
            if (double.IsInfinity(dist) || double.IsNaN(dist)) continue;
            double realized = Math.Max(basic, dist + RelatedStaffPadding);
            double drop = realized - basic;
            if (drop > 1e-6) systemDrop[s] = drop;
        }

        if (systemDrop.Count == 0)
            return layouts;

        var shifted = new List<LyricLayout>(layouts.Count);
        foreach (var lay in layouts)
        {
            double drop = measureToSystem.TryGetValue(lay.Item.MeasureIndex, out int s)
                && systemDrop.TryGetValue(s, out var d) ? d : 0;
            shifted.Add(drop > 0 ? lay with { Y = lay.Y + drop } : lay);
        }
        return shifted;
    }

    /// <summary>
    /// Resolves overlapping syllables by shifting them apart.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/lyric-engraver.cc:150-180 horizontal spacing
    ///
    /// Strategy: Limit shifts to prevent lyrics from drifting too far from their notes.
    /// If a large shift would be needed, reduce the effective width estimate instead.
    /// </remarks>
    private List<LyricLayout> ResolveOverlaps(List<LyricLayout> layouts)
    {
        if (layouts.Count < 2)
            return layouts;

        // Maximum shift allowed (prevents lyrics from drifting too far from notes)
        const double maxShift = 2.0;

        var result = new List<LyricLayout>(layouts.Count);

        for (int i = 0; i < layouts.Count; i++)
        {
            var current = layouts[i];

            if (i == 0)
            {
                result.Add(current);
                continue;
            }

            var previous = result[i - 1];

            // Use reduced width for collision detection (allows some overlap)
            // This keeps lyrics closer to their notes while still readable
            double effectiveWidth = 0.6; // Use a smaller effective width for collision

            double prevRight = previous.X + effectiveWidth;
            double currLeft = current.X - effectiveWidth;
            double gap = currLeft - prevRight;

            // If there's not enough gap, shift current syllable to the right
            if (gap < _params.MinSyllableSpacing)
            {
                double neededShift = _params.MinSyllableSpacing - gap;
                double shift = Math.Min(neededShift, maxShift);
                current = current with { X = current.X + shift };
            }

            result.Add(current);
        }

        return result;
    }

    /// <summary>
    /// Calculate layout for a single syllable.
    /// </summary>
    private LyricLayout? CalculateSyllableLayout(
        LyricItem lyric,
        IReadOnlyList<MeasureLayout> measureLayouts,
        double y,
        LyricItem? nextLyric)
    {
        // Find the note position for this syllable
        if (lyric.MeasureIndex < 0 || lyric.MeasureIndex >= measureLayouts.Count)
            return null;

        var measureLayout = measureLayouts[lyric.MeasureIndex];
        if (lyric.ItemIndex < 0 || lyric.ItemIndex >= measureLayout.ItemPositions.Count)
            return null;

        // Get X position from the associated note
        // LILYPOND-REF: lily/lyric-engraver.cc:100-110 horizontal alignment
        double noteX = measureLayout.X + measureLayout.ItemPositions[lyric.ItemIndex];

        // Estimate text width (rough approximation: 0.5 staff spaces per character)
        double textWidth = EstimateTextWidth(lyric.Text);

        // Center the syllable under the note
        double syllableX = noteX;

        // Determine if we need a hyphen or extender
        bool drawHyphen = false;
        double hyphenX = 0;
        bool drawExtender = false;
        double extenderEndX = 0;

        if (lyric.ConnectorType == LyricConnectorType.Hyphen && nextLyric != null)
        {
            // Calculate hyphen position (midpoint between syllables)
            var nextNoteX = GetNoteX(nextLyric, measureLayouts);
            if (nextNoteX.HasValue)
            {
                double gap = nextNoteX.Value - (syllableX + textWidth / 2);
                if (gap > _params.MinHyphenLength + _params.HyphenPadding * 2)
                {
                    drawHyphen = true;
                    hyphenX = syllableX + textWidth / 2 + _params.HyphenPadding +
                             (gap - _params.HyphenPadding * 2) / 2;
                }
            }
        }
        else if (lyric.ConnectorType == LyricConnectorType.Extender && nextLyric != null)
        {
            // Extender line to next syllable
            var nextNoteX = GetNoteX(nextLyric, measureLayouts);
            if (nextNoteX.HasValue)
            {
                drawExtender = true;
                double nextTextWidth = EstimateTextWidth(nextLyric.Text);
                extenderEndX = nextNoteX.Value - nextTextWidth / 2 - _params.HyphenPadding;
            }
        }

        return new LyricLayout(
            lyric,
            syllableX,
            y,
            textWidth,
            drawHyphen,
            hyphenX,
            drawExtender,
            extenderEndX);
    }

    /// <summary>
    /// Get the X position of a note for a lyric item.
    /// </summary>
    private double? GetNoteX(LyricItem lyric, IReadOnlyList<MeasureLayout> measureLayouts)
    {
        if (lyric.MeasureIndex < 0 || lyric.MeasureIndex >= measureLayouts.Count)
            return null;

        var measureLayout = measureLayouts[lyric.MeasureIndex];
        if (lyric.ItemIndex < 0 || lyric.ItemIndex >= measureLayout.ItemPositions.Count)
            return null;

        return measureLayout.X + measureLayout.ItemPositions[lyric.ItemIndex];
    }

    /// <summary>
    /// Estimate text width in staff spaces using serif font character width ratios.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/font-metric.cc:100-120 text extent calculation
    ///
    /// The SVG renderer uses font-size = 4 * 0.8 = 3.2 staff spaces.
    /// Character widths are approximated using Times New Roman proportions (em fractions).
    /// Width classes are grouped by similar advance widths in standard serif fonts.
    /// </remarks>
    private double EstimateTextWidth(string text)
    {
        // Character width estimation at rendered font size (3.2 staff spaces)
        // Width ≈ fontSize * characterWidthRatio (em fraction)
        const double fontSize = 3.2;
        double width = 0;
        foreach (char c in text)
        {
            // Width ratios based on Times New Roman advance widths (em fractions)
            double ratio = c switch
            {
                ' ' => 0.25,
                '!' or '.' or ',' or ':' or ';' or '\'' or '|' => 0.25,
                'i' or 'l' or 'j' => 0.28,
                'f' or 't' or 'r' => 0.33,
                's' or 'z' => 0.39,
                'a' or 'c' or 'e' => 0.44,
                'b' or 'd' or 'g' or 'h' or 'k' or 'n' or 'o' or 'p' or 'q' or 'u' or 'v' or 'x' or 'y' => 0.50,
                'w' => 0.72,
                'm' => 0.78,
                'I' => 0.33,
                'J' => 0.39,
                'A' or 'B' or 'C' or 'D' or 'E' or 'F' or 'G' or 'H' or 'K' or 'L'
                    or 'N' or 'O' or 'P' or 'Q' or 'R' or 'S' or 'T' or 'U' or 'V'
                    or 'X' or 'Y' or 'Z' => 0.61,
                'M' or 'W' => 0.83,
                '-' => 0.33,
                _ => 0.50
            };
            width += fontSize * ratio;
        }
        return width;
    }
}
