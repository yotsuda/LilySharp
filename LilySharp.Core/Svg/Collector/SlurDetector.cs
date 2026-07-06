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

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Detects slurs between notes in a score.
/// </summary>
internal sealed class SlurDetector
{
    public ImmutableArray<SlurItem> DetectSlurs(Score score)
    {
        var slurs = new List<SlurItem>();

        // Each voice runs its own slur engraver: a voice's open-slur stack must
        // not pair with another voice's close, so the stack is per voice. A
        // single-voice score iterates once with voiceIndex 0 (byte-identical).
        // LILYPOND-REF: ly/engraver-init.ly — Slur_engraver lives in the Voice context.
        for (int v = 0; v < score.Voices.Length; v++)
        {
            var measures = score.Voices[v].Measures;
            var openSlurs = new Stack<(int measureIdx, int itemIdx, MusicItem item)>();

            for (int measureIdx = 0; measureIdx < measures.Length; measureIdx++)
            {
                var measure = measures[measureIdx];

                for (int itemIdx = 0; itemIdx < measure.Items.Length; itemIdx++)
                {
                    var item = measure.Items[itemIdx];
                    // Slurs attach to a note OR a chord (`<c e>( <d f>)`).
                    if (!TryGetSlurFlags(item, out bool hasStart, out bool hasEnd))
                        continue;

                    if (hasStart)
                    {
                        openSlurs.Push((measureIdx, itemIdx, item));
                    }

                    if (hasEnd && openSlurs.Count > 0)
                    {
                        var (startMeasureIdx, startItemIdx, startItem) = openSlurs.Pop();

                        // Single voice: default DOWN, flipped UP when ANY covered
                        // stem points DOWN — not just the start note's (a slur from
                        // a stem-up note to a stem-down note goes UP, or it curves
                        // straight into the later note's stem side). Polyphony: the
                        // voice fixes the direction regardless of stems — the upper
                        // voice curves UP, the lower voice DOWN, so the two voices'
                        // slurs stay clear of each other.
                        // LILYPOND-REF: lily/slur.cc Slur::calc_direction — d = DOWN,
                        //   set UP if any non-rest note column has direction DOWN.
                        // LILYPOND-REF: ly/engraver-init.ly \voiceOne/\voiceTwo set
                        //   Slur.direction = UP / DOWN (voiceThree/Four alternate).
                        bool curveUp = score.Voices.Length > 1
                            ? (v % 2 == 0)
                            : AnyCoveredStemDown(measures,
                                startMeasureIdx, startItemIdx, measureIdx, itemIdx);

                        slurs.Add(new SlurItem(
                            // For a chord the slur anchors at the head on the curve side.
                            MusicItem.EdgeStaffPosition(startItem, curveUp) ?? 0,
                            MusicItem.EdgeStaffPosition(item, curveUp) ?? 0,
                            curveUp,
                            startMeasureIdx,
                            measureIdx,
                            startItemIdx,
                            itemIdx,
                            voiceIndex: v));
                    }
                }
            }
        }

        return slurs.ToImmutableArray();
    }

    private static bool TryGetSlurFlags(MusicItem item, out bool hasStart, out bool hasEnd)
    {
        switch (item)
        {
            case NoteItem n: hasStart = n.HasSlurStart; hasEnd = n.HasSlurEnd; return true;
            case ChordItem c: hasStart = c.HasSlurStart; hasEnd = c.HasSlurEnd; return true;
            default: hasStart = false; hasEnd = false; return false;
        }
    }

    // True when any note/chord covered by the slur (inclusive range) has a
    // DOWN stem — rests are transparent, like LP's !has_rests columns.
    // LILYPOND-REF: lily/slur.cc Slur::calc_direction.
    private static bool AnyCoveredStemDown(
        ImmutableArray<Measure> measures,
        int startMeasureIdx, int startItemIdx, int endMeasureIdx, int endItemIdx)
    {
        for (int mi = startMeasureIdx; mi <= endMeasureIdx && mi < measures.Length; mi++)
        {
            var items = measures[mi].Items;
            int from = mi == startMeasureIdx ? startItemIdx : 0;
            int to = mi == endMeasureIdx ? Math.Min(endItemIdx, items.Length - 1) : items.Length - 1;
            for (int ii = from; ii <= to; ii++)
            {
                switch (items[ii])
                {
                    case NoteItem n when !n.StemUp:
                        return true;
                    case ChordItem c when !c.StemUp:
                        return true;
                }
            }
        }
        return false;
    }
}