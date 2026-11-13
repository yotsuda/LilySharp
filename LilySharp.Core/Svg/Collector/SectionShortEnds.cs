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

using System;
using System.Collections.Generic;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// The short last bar of a section: the first half of a bar the next play's pickup completes
/// (<c>… | r2. |</c> in 4/4, then a section under <c>partial 4</c>). A part that writes
/// nothing in the section rests through that bar too, and its rest is as long as the bar is —
/// not as long as the meter.
/// </summary>
/// <remarks>
/// ONE HOME for the two readers that pad a silent part: the page
/// (<c>MeasureCollector</c>'s section padding) and the LilyPond twin
/// (<c>LilyPondExporter.AppendSilentPlay</c>). The MusicXML export measures the bars it has
/// already written instead (<c>FitSilentPlayToWrittenBars</c>, 2026-11-12) and the MIDI needs
/// no silence. Until 2026-11-14 both readers here padded a full bar: on the page the staff of
/// a part that rests through such a section could no longer be told that the bar is split —
/// and when it was the score's first staff, nobody was (the conductor's score of a choral
/// piece counted 69 bars, its soprano's sheet 67) — and the twin ran the staff a beat late per
/// section, the Score's clock with it (grammar evaluation 4, Lab sessions/p1227/x/twin).
/// </remarks>
internal static class SectionShortEnds
{
    /// <summary>The meter a section with no header <c>time</c> opens in: the file's own
    /// (its first top-level metered <c>time</c>), else 4/4.</summary>
    public static Fraction HomeMeter(SyntaxNode root)
    {
        foreach (var child in root.ChildNodes())
            if (child is TimeSignatureSyntax { IsSenzaMisura: false } time)
                return new Fraction(time.Beats, time.BeatType);
        return new Fraction(4, 4);
    }

    /// <summary>How long <paramref name="voice"/>'s last bar of its section is when it is
    /// shorter than its meter (the bar model's own answer, <see cref="MeasureModel.Split"/>),
    /// or null: a full bar, an empty one, an unmetered one, or a section of one bar (a pickup
    /// alone is its reader's to measure).</summary>
    /// <param name="opening">The meter the section opens in: its header's, else the file's.</param>
    public static Fraction? OfVoice(SectionBarCounts.SemanticVoice voice,
        IReadOnlyDictionary<string, SyntaxNode> phrases, Fraction opening, SectionMeterPlan plan)
    {
        if (voice.IsChords || voice.IsLyrics)
            return null;
        Func<int, TimeSignatureSyntax?>? planned = plan.IsEmpty
            ? null
            : bar => plan.ForeignChangeAt(voice.SectionName, bar, voice.Container);
        var bars = MeasureModel.Split(voice.Container, phrases, opening, plannedMeterAt: planned);
        if (bars.Count < 2 || bars.Count != voice.Bars)
            return null;
        var last = bars[^1];
        return last is { IsEmpty: false, Meter: { } meter } && last.Duration > Fraction.Zero && last.Duration < meter
            ? last.Duration
            : null;
    }

    /// <summary>The length the last bar of <paramref name="section"/> has in every music voice
    /// that writes the section's full <paramref name="bars"/> bars, when all of them end it
    /// short of the meter (the longest of them); null when one fills it, or none writes it.</summary>
    public static Fraction? OfSection(IEnumerable<SectionBarCounts.SemanticVoice> voices,
        IReadOnlyDictionary<string, SyntaxNode> phrases, string section, int bars, Fraction opening,
        SectionMeterPlan plan)
    {
        Fraction? longest = null;
        foreach (var voice in voices)
        {
            if (voice.SectionName != section || voice.IsChords || voice.IsLyrics || voice.Bars != bars)
                continue;
            if (OfVoice(voice, phrases, opening, plan) is not { } length)
                return null;
            if (longest == null || length > longest.Value)
                longest = length;
        }
        return longest;
    }
}
