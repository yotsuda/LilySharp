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

        var layouts = ImmutableArray.CreateBuilder<LyricLayout>(lyrics.Count);

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

            for (int i = 0; i < verseLyrics.Count; i++)
            {
                var lyric = verseLyrics[i];
                var layout = CalculateSyllableLayout(
                    lyric,
                    measureLayouts,
                    verseY,
                    i + 1 < verseLyrics.Count ? verseLyrics[i + 1] : null);

                if (layout != null)
                    layouts.Add(layout);
            }
        }

        return layouts.ToImmutable();
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
    /// Estimate text width in staff spaces.
    /// </summary>
    /// <remarks>
    /// This is a rough approximation. For accurate width calculation,
    /// we would need font metrics. Average character width is ~0.5 staff spaces.
    /// </remarks>
    private double EstimateTextWidth(string text)
    {
        // Simple estimation: ~0.5 staff spaces per character
        // Adjust for common narrow/wide characters
        double width = 0;
        foreach (char c in text)
        {
            width += c switch
            {
                'i' or 'l' or 'I' or '!' or '.' or '\'' => 0.3,
                'm' or 'w' or 'M' or 'W' => 0.7,
                _ => 0.5
            };
        }
        return width * _params.FontSize;
    }
}
