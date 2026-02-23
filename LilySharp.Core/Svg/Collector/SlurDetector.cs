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
public sealed class SlurDetector
{
    public ImmutableArray<SlurItem> DetectSlurs(Score score)
    {
        var slurs = new List<SlurItem>();
        var measures = score.Voice.Measures;
        var openSlurs = new Stack<(int measureIdx, int itemIdx, NoteItem note)>();

        for (int measureIdx = 0; measureIdx < measures.Length; measureIdx++)
        {
            var measure = measures[measureIdx];

            for (int itemIdx = 0; itemIdx < measure.Items.Length; itemIdx++)
            {
                if (measure.Items[itemIdx] is not NoteItem note)
                    continue;

                if (note.HasSlurStart)
                {
                    openSlurs.Push((measureIdx, itemIdx, note));
                }

                if (note.HasSlurEnd && openSlurs.Count > 0)
                {
                    var (startMeasureIdx, startItemIdx, startNote) = openSlurs.Pop();

                    // Slur curves opposite to stem direction
                    // NoteItem.StemUp: true = stem visually UP, false = stem visually DOWN
                    bool curveUp = !startNote.StemUp;

                    slurs.Add(new SlurItem(
                        startNote,
                        note,
                        startNote.StaffPosition,
                        note.StaffPosition,
                        curveUp,
                        startMeasureIdx,
                        measureIdx,
                        startItemIdx,
                        itemIdx));
                }
            }
        }

        return slurs.ToImmutableArray();
    }
}