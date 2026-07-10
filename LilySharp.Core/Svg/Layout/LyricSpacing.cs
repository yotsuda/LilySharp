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
/// Lyric-driven horizontal spacing: syllable text-width estimation and the spring
/// adjustments that keep adjacent lyric syllables from colliding. Extracted from
/// <see cref="SpacingRules"/>; the spring-core builder
/// <see cref="SpacingRules.CreateSpringsForMeasureWithLyrics"/> stays there and
/// calls into these helpers.
/// </summary>
internal static class LyricSpacing
{
    /// <summary>
    /// Widens an EXISTING spring chain so adjacent syllables don't collide.
    /// Unlike <see cref="SpacingRules.CreateSpringsForMeasureWithLyrics"/> (which builds item
    /// springs from scratch for the single-staff path), this post-processes the
    /// timing-column springs used by the multi-staff layouter, so a promoted
    /// single-staff score gets the same lyric-driven spacing.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-spacing.cc:80-85 skyline-based min_distance.
    /// The spring chain is [start→col0, col0→col1, …, colLast→end]; for a
    /// single-voice measure the timing columns coincide with the note items, so
    /// spring i+1 spans item i → item i+1. When the column count does not match
    /// the item count (extra voices), the mapping breaks down and the chain is
    /// returned unchanged — lyrics are only engraved on single-voice staves.
    /// </remarks>
    public static ImmutableArray<Spring> ApplyLyricSpacing(
        ImmutableArray<Spring> springs,
        Measure measure,
        int measureIndex,
        IReadOnlyList<LyricItem> lyrics)
    {
        if (measure.Items.Length == 0 || springs.Length != measure.Items.Length + 1)
            return springs;

        var lyricsByItem = new Dictionary<int, List<LyricItem>>();
        foreach (var lyric in lyrics)
        {
            if (lyric.MeasureIndex != measureIndex)
                continue;
            if (!lyricsByItem.TryGetValue(lyric.ItemIndex, out var list))
                lyricsByItem[lyric.ItemIndex] = list = new List<LyricItem>();
            list.Add(lyric);
        }
        if (lyricsByItem.Count == 0)
            return springs;

        var result = springs.ToBuilder();

        // First spring (start barline → item 0): reserve item 0's left extent.
        if (lyricsByItem.TryGetValue(0, out var firstLyrics))
        {
            var s0 = result[0];
            double adjustedMin = Math.Max(s0.MinDistance, GetLyricLeftExtent(firstLyrics) + GlyphMetrics.MinItemGap);
            result[0] = new Spring(Math.Max(s0.IdealDistance, adjustedMin), adjustedMin, s0.InverseStretchStrength);
        }

        // Between items: spring i+1 spans item i → item i+1.
        for (int i = 0; i < measure.Items.Length - 1; i++)
        {
            double lyricDistance = CalculateLyricDistance(
                lyricsByItem.GetValueOrDefault(i),
                lyricsByItem.GetValueOrDefault(i + 1));
            var spring = result[i + 1];
            if (lyricDistance > spring.MinDistance)
                result[i + 1] = new Spring(
                    Math.Max(spring.IdealDistance, lyricDistance),
                    lyricDistance, spring.InverseStretchStrength);
        }

        // Last spring (item last → end barline): reserve last item's right extent.
        int lastIndex = measure.Items.Length - 1;
        if (lyricsByItem.TryGetValue(lastIndex, out var lastLyrics))
        {
            var sl = result[^1];
            double adjustedMin = Math.Max(sl.MinDistance, GetLyricRightExtent(lastLyrics) + GlyphMetrics.MinItemGap);
            result[^1] = new Spring(Math.Max(sl.IdealDistance, adjustedMin), adjustedMin, sl.InverseStretchStrength);
        }

        return result.ToImmutable();
    }

    /// <summary>
    /// Calculates the minimum distance between two notes based on their lyrics.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/separation-item.cc:49-70 set_distance()
    ///
    /// The distance is: prevLyricRightExtent + nextLyricLeftExtent + padding
    /// where each extent is half the lyric text width (centered under note).
    /// </remarks>
    internal static double CalculateLyricDistance(List<LyricItem>? prevLyrics, List<LyricItem>? nextLyrics)
    {
        if (prevLyrics == null && nextLyrics == null)
            return 0;

        double prevRight = GetLyricRightExtent(prevLyrics);
        double nextLeft = GetLyricLeftExtent(nextLyrics);

        // Minimum INK gap between syllables: a word-space at the lyric font
        // (~0.31 em at 3.2 ss), which is also what LP's lyric spacing yields
        // between words. It doubles as headroom for the renderer's actual
        // serif face, whose advances differ from the Times table by a few
        // percent either way (the face behind generic "serif" is the
        // viewer's choice; we cannot measure it at layout time).
        const double lyricPadding = 1.0;  // staff spaces

        return prevRight + nextLeft + lyricPadding;
    }

    /// <summary>
    /// Gets the right extent of lyrics (from note center to right edge of text).
    /// </summary>
    internal static double GetLyricRightExtent(List<LyricItem>? lyrics)
    {
        if (lyrics == null || lyrics.Count == 0)
            return 0;

        // Find the widest lyric (for multiple verses)
        double maxExtent = 0;
        foreach (var lyric in lyrics)
        {
            double width = EstimateLyricTextWidth(lyric.Text);
            // Right extent is half the width (text is centered under note)
            maxExtent = Math.Max(maxExtent, width / 2);
        }
        return maxExtent;
    }

    /// <summary>
    /// Gets the left extent of lyrics (from note center to left edge of text).
    /// </summary>
    internal static double GetLyricLeftExtent(List<LyricItem>? lyrics)
    {
        if (lyrics == null || lyrics.Count == 0)
            return 0;

        // Find the widest lyric (for multiple verses)
        double maxExtent = 0;
        foreach (var lyric in lyrics)
        {
            double width = EstimateLyricTextWidth(lyric.Text);
            // Left extent is half the width (text is centered under note)
            maxExtent = Math.Max(maxExtent, width / 2);
        }
        return maxExtent;
    }

    // Real serif-regular advances (SerifTextMetrics) at the 3.2 ss lyric font —
    // this used to be a crude 3-bucket table that under-measured capitals
    // ("Up" by ~0.7 ss), so the springs reserved too little and wide syllables
    // overlapped their neighbours in lyric rows.
    private static double EstimateLyricTextWidth(string text)
        => Rendering.SerifTextMetrics.Measure(text, 3.2);
}
