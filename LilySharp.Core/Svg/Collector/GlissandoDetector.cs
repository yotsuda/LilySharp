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
/// Detects glissandos between notes marked with HasGlissando.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/scheme-engravers.scm - Glissando_engraver
/// Unlike ties (same pitch), glissandos connect to the next note of any pitch.
/// </remarks>
public sealed class GlissandoDetector
{
    public ImmutableArray<GlissandoItem> DetectGlissandos(Score score)
    {
        var glissandos = new List<GlissandoItem>();
        var measures = score.Voice.Measures;

        for (int measureIdx = 0; measureIdx < measures.Length; measureIdx++)
        {
            var measure = measures[measureIdx];

            for (int itemIdx = 0; itemIdx < measure.Items.Length; itemIdx++)
            {
                if (measure.Items[itemIdx] is not NoteItem startNote)
                    continue;

                if (!startNote.HasGlissando)
                    continue;

                // Glissandos connect to the next note of any pitch.
                var endNote = NoteScan.FindNextNote(measures, measureIdx, itemIdx, _ => true);
                if (endNote != null)
                {
                    var (endMeasureIdx, endItemIdx, note) = endNote.Value;

                    glissandos.Add(new GlissandoItem(
                        StartMeasureIndex: measureIdx,
                        StartItemIndex: itemIdx,
                        StartStaffPosition: startNote.StaffPosition,
                        EndMeasureIndex: endMeasureIdx,
                        EndItemIndex: endItemIdx,
                        EndStaffPosition: note.StaffPosition,
                        Style: GlissandoStyle.Line,
                        SourcePosition: startNote.SourcePosition));
                }
            }
        }

        return glissandos.ToImmutableArray();
    }
}
