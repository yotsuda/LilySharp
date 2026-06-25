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
    /// Calculate layouts for all lyrics in a score.
    /// </summary>
    /// <param name="lyrics">Collection of lyric items.</param>
    /// <param name="measureLayouts">Measure layout information for note positions.</param>
    /// <param name="staffBottom">Y position of the bottom staff line (in staff spaces).</param>
    /// <returns>Immutable array of lyric layouts.</returns>
    public ImmutableArray<LyricLayout> CalculateLayouts(
        IReadOnlyList<LyricItem> lyrics,
        IReadOnlyList<MeasureLayout> measureLayouts,
        double staffBottom)
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

        return layouts.ToImmutableArray();
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
