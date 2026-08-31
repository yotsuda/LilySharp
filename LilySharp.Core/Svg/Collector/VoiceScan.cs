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
using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Shared per-voice item traversal used by the span detectors (tie / slur /
/// glissando). Each voice runs its own engraver, so every detector scans all
/// voices and keys its output by voice index; a single-voice score iterates once
/// with voice 0 (byte-identical).
/// LILYPOND-REF: ly/engraver-init.ly — Tie_/Slur_/Glissando_engraver per Voice.
/// </summary>
internal static class VoiceScan
{
    /// <summary>
    /// Enumerates every item of every voice in order, yielding the voice index,
    /// that voice's measures (for follow-on lookups), and the item's measure/item
    /// indices. A detector with per-voice state resets it when
    /// <c>VoiceIndex</c> changes.
    /// </summary>
    public static IEnumerable<(int VoiceIndex, ImmutableArray<Measure> Measures, int MeasureIndex, int ItemIndex, MusicItem Item)>
        WalkVoiceItems(Score score)
    {
        for (int v = 0; v < score.Voices.Length; v++)
        {
            var measures = score.Voices[v].Measures;
            for (int m = 0; m < measures.Length; m++)
            {
                var items = measures[m].Items;
                for (int i = 0; i < items.Length; i++)
                {
                    // GRACE TIME IS NOT YET IN THE SPAN DETECTORS' STREAM. All three pair an
                    // opening flag with THE NEXT item, and a grace takes no measure time, so
                    // it stands between a note and the note that note reaches to — MEASURED,
                    // `d4@glissando grace { d8 } c` drew its glissando to the GRACE (a
                    // horizontal line, both heads being d) instead of to the c.
                    // ⚠️ SCAFFOLDING, and the one whose removal is the PRIZE: this skip is
                    // the whole of "a grace note cannot carry a slur or a tie" (LYS4020).
                    // Deleting it is what HANDOFF §2 U8 ⒞ means, and it can only go once ⒝2
                    // lets the ordinary engravers draw grace time — until then the detectors
                    // would pair spans that nothing would draw.
                    if (items[i].GraceTime)
                        continue;
                    yield return (v, measures, m, i, items[i]);
                }
            }
        }
    }

    /// <summary>
    /// Curve direction for a tie/slur span. In polyphony the voice fixes it —
    /// the upper voice (index 0, 2, …) curves UP, the lower voice (1, 3, …) DOWN —
    /// so the voices' spans stay clear of each other; a single voice uses the
    /// given stem-based <paramref name="singleVoiceFallback"/>.
    /// </summary>
    /// <remarks>LILYPOND-REF: ly/engraver-init.ly \voiceOne/\voiceTwo set Tie/Slur.direction = UP/DOWN.</remarks>
    public static bool SpanCurvesUp(int voiceCount, int voiceIndex, bool singleVoiceFallback)
        => voiceCount > 1 ? (voiceIndex % 2 == 0) : singleVoiceFallback;
}
