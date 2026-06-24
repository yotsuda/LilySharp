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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout for a stanza number prefix (e.g., "1.") shown at the left edge of a verse.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/stanza-number-engraver.cc — StanzaNumber grob
/// LILYPOND-REF: scm/define-grobs.scm StanzaNumber:
///   font-size = -1, font-series = bold
/// The StanzaNumber sits to the left of each lyric verse line, anchored at
/// the system's left edge.
/// </remarks>
public readonly record struct StanzaNumberLayout(
    int VerseNumber,
    int SystemIndex,
    /// <summary>A measure in this system, used to resolve the system's Y when drawing.</summary>
    int MeasureIndex,
    /// <summary>X coordinate at the system's left edge (start of staff lines).</summary>
    double X,
    /// <summary>Y coordinate (relative to the system top, matching the verse's lyric baseline).</summary>
    double Y,
    /// <summary>Display text (e.g., "1.", "2.").</summary>
    string Text);

/// <summary>
/// Calculates StanzaNumber layouts for multi-verse lyrics.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/stanza-number-engraver.cc — Stanza_number_engraver.
/// Emits one entry per (system, verseNumber) combination so that a number
/// appears at the start of every verse line on every system.
/// </remarks>
public static class StanzaNumberEngraver
{
    /// <summary>
    /// Calculates stanza number layouts for verses present in the given lyric layouts.
    /// </summary>
    /// <param name="lyrics">All lyric layouts (post-engraver).</param>
    /// <param name="systems">System layouts; used to obtain each system's left edge X.</param>
    /// <param name="emitForFirstVerse">When false (LP default), single-verse scores
    /// don't get a "1." prefix. When multiple verses exist, all verses (including 1)
    /// are numbered.</param>
    public static ImmutableArray<StanzaNumberLayout> Calculate(
        ImmutableArray<LyricLayout> lyrics,
        ImmutableArray<SystemLayout> systems,
        bool emitForFirstVerse = false)
    {
        if (lyrics.IsDefaultOrEmpty || systems.IsDefaultOrEmpty)
            return ImmutableArray<StanzaNumberLayout>.Empty;

        // Find max verse number; LP only labels verses when there are multiple.
        int maxVerse = 0;
        foreach (var l in lyrics)
            if (l.Item.VerseNumber > maxVerse) maxVerse = l.Item.VerseNumber;
        if (maxVerse <= 1 && !emitForFirstVerse)
            return ImmutableArray<StanzaNumberLayout>.Empty;

        // Map measure → system index so we can match a lyric to its enclosing system.
        var measureToSystem = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);

        // Collect (system, verse) → first lyric in that system+verse (its Y is the verse baseline).
        var firstLyricBySystem = new Dictionary<(int sys, int verse), LyricLayout>();
        foreach (var l in lyrics)
        {
            if (!measureToSystem.TryGetValue(l.Item.MeasureIndex, out int sysIdx))
                continue;
            var key = (sysIdx, l.Item.VerseNumber);
            if (!firstLyricBySystem.ContainsKey(key))
                firstLyricBySystem[key] = l;
            else
            {
                // Keep the lyric whose X is leftmost in the system.
                if (l.X < firstLyricBySystem[key].X)
                    firstLyricBySystem[key] = l;
            }
        }

        var builder = ImmutableArray.CreateBuilder<StanzaNumberLayout>();
        foreach (var ((sysIdx, verseNumber), lyric) in firstLyricBySystem)
        {
            if (sysIdx >= systems.Length) continue;
            var system = systems[sysIdx];
            // Position at the system's left margin (where staff lines begin).
            // LP places StanzaNumber to the LEFT of the lyric, anchored to the system start.
            // Use (system.Y + first measure X) less a small offset.
            double x = system.Measures.IsDefaultOrEmpty
                ? lyric.X - 4.0
                : system.Measures[0].X - 4.0;
            builder.Add(new StanzaNumberLayout(
                VerseNumber: verseNumber,
                SystemIndex: sysIdx,
                MeasureIndex: lyric.Item.MeasureIndex,
                X: x,
                Y: lyric.Y,
                Text: $"{verseNumber}."));
        }

        return builder.ToImmutable();
    }
}
