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

using System.Collections.Generic;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Post-collect scan for ties that cannot bind. A tie joins two notes of the SAME
/// pitch and binds only to the IMMEDIATELY following timed item — the rule
/// <see cref="TieDetector"/> renders by (it never scans past an intervening item;
/// LILYPOND-REF: lily/tie-engraver.cc stop_translation_timestep). A pitch mismatch
/// or an audible rest there is almost always an authoring slip (a slur was meant,
/// or the target note was mistyped), so it is surfaced as a warning.
/// </summary>
/// <remarks>
/// Runs per voice on the collected measures BEFORE display-only transforms (the
/// ottava transposer moves staff positions), comparing both staff position and
/// sounding pitch so a same-position accidental change (<c>c~ cis</c>) is caught
/// too. A tie start with nothing after it, and a tie into an invisible spacer
/// (<c>s</c> — an absent parallel voice's padding), are left alone.
/// </remarks>
internal static class TieTargetScanner
{
    public static void Scan(Voice voice, List<TieTargetWarning> sink)
    {
        var measures = voice.Measures;
        for (int mi = 0; mi < measures.Length; mi++)
        {
            var items = measures[mi].Items;
            for (int ii = 0; ii < items.Length; ii++)
            {
                var item = items[ii];
                if (item is not (NoteItem { HasTieStart: true } or ChordItem { HasTieStart: true }))
                    continue;
                var next = NoteScan.FindNext(measures, mi, ii,
                    x => x is NoteItem or ChordItem or RestItem);
                if (next is not { } n)
                    continue; // dangling at the very end — nothing to compare
                if (n.Item is RestItem rest)
                {
                    if (!rest.IsSpacer)
                        sink.Add(new TieTargetWarning(rest.SourcePosition, IntoRest: true));
                }
                else if (!AnyPitchMatches(item, n.Item))
                {
                    sink.Add(new TieTargetWarning(n.Item.SourcePosition, IntoRest: false));
                }
            }
        }
    }

    /// <summary>True when at least one pitch of <paramref name="start"/> recurs in
    /// <paramref name="end"/>. A chord tie with SOME matching pitches is fine (the
    /// unmatched ones are dropped silently, the LilyPond chord-tie behavior); only
    /// a total mismatch — nothing gets tied at all — is reported.</summary>
    private static bool AnyPitchMatches(MusicItem start, MusicItem end)
    {
        foreach (var (pos, midi) in Pitches(start))
            foreach (var (endPos, endMidi) in Pitches(end))
                if (pos == endPos && midi == endMidi)
                    return true;
        return false;
    }

    private static IEnumerable<(int Pos, int Midi)> Pitches(MusicItem item)
    {
        switch (item)
        {
            case NoteItem n:
                yield return (n.StaffPosition, n.Midi);
                break;
            case ChordItem c:
                foreach (var p in c.Notes)
                    yield return (p.StaffPosition, p.Midi);
                break;
        }
    }
}
