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
    // A measure in this system, used to resolve the system's Y when drawing.
    int MeasureIndex,
    // X coordinate at the system's left edge (start of staff lines).
    double X,
    // Y-up (frame B): staff-spaces above the system top, up-positive, matching the
    // verse's lyric baseline. The renderer reflects it to device against the
    // system top.
    double YUp,
    // Display text (e.g., "1.", "2.").
    string Text);

/// <summary>
/// Calculates StanzaNumber layouts for multi-verse lyrics.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/stanza-number-engraver.cc — Stanza_number_engraver.
/// Emits one entry per (system, verseNumber) combination so that a number
/// appears at the start of every verse line on every system.
/// </remarks>
internal static class StanzaNumberEngraver
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

        // Map measure → system index so we can match a lyric to its enclosing system.
        var measureToSystem = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);

        // Number verses PER SYSTEM: a stanza number is only useful where more than one
        // verse actually stacks. A song whose verses all stack everywhere numbers every
        // system (the usual case); a per-occurrence volta that puts a second verse on
        // only one system leaves the single-verse systems (a lone reprise line) clean.
        // A `~`-hidden verse still COUNTS toward a system's verse tally (so the visible
        // verse beside it keeps its number) but prints no number itself.
        var versesInSystem = new Dictionary<int, HashSet<int>>();
        foreach (var l in lyrics)
            if (measureToSystem.TryGetValue(l.Item.MeasureIndex, out int sys))
                (versesInSystem.TryGetValue(sys, out var set) ? set : versesInSystem[sys] = new())
                    .Add(l.Item.VerseNumber);

        // The stanza number is one label per (system, verse) at the left edge, so a
        // `~2` on ONE section's verse must suppress that whole baseline — otherwise
        // another (unhidden) section sharing the verse on the same system (a plain
        // reprise line) would keep re-printing the number the author asked to hide.
        var hiddenPairs = new HashSet<(int sys, int verse)>();
        foreach (var l in lyrics)
            if (l.Item.HideStanza && measureToSystem.TryGetValue(l.Item.MeasureIndex, out int hs))
                hiddenPairs.Add((hs, l.Item.VerseNumber));

        // Collect (system, verse) → first lyric in that system+verse (its Y is the verse baseline).
        var firstLyricBySystem = new Dictionary<(int sys, int verse), LyricLayout>();
        foreach (var l in lyrics)
        {
            if (l.Item.HideStanza)
                continue;
            if (!measureToSystem.TryGetValue(l.Item.MeasureIndex, out int sysIdx))
                continue;
            // Only a system that stacks 2+ verses gets numbers (unless forced).
            if (!emitForFirstVerse
                && (!versesInSystem.TryGetValue(sysIdx, out var vs) || vs.Count <= 1))
                continue;
            var key = (sysIdx, l.Item.VerseNumber);
            if (hiddenPairs.Contains(key))
                continue; // a ~-hidden verse suppresses this baseline's number outright
            // Keep the lyric whose X is leftmost in the system (the verse's start).
            if (!firstLyricBySystem.TryGetValue(key, out var cur) || l.X < cur.X)
                firstLyricBySystem[key] = l;
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
                YUp: lyric.YUp,
                Text: $"{verseNumber}."));
        }

        return builder.ToImmutable();
    }
}
