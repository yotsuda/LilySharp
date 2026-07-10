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
using System.Linq;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Detects glissandos between notes marked with HasGlissando.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/scheme-engravers.scm - Glissando_engraver
/// Unlike ties (same pitch), glissandos connect to the next note of any pitch.
/// </remarks>
internal sealed class GlissandoDetector
{
    public ImmutableArray<GlissandoItem> DetectGlissandos(Score score)
    {
        var glissandos = new List<GlissandoItem>();

        // Each voice runs its own glissando engraver; VoiceScan walks them all so a
        // second voice's glissando is not lost.
        // LILYPOND-REF: scm/scheme-engravers.scm — Glissando_engraver per Voice.
        foreach (var (v, measures, measureIdx, itemIdx, item) in VoiceScan.WalkVoiceItems(score))
        {
            if (item is not NoteItem startNote || !startNote.HasGlissando)
                continue;

            // Glissandos connect to the next note of any pitch — a note OR
            // a chord (a chord endpoint used to be skipped, dropping the gliss).
            var endItem = NoteScan.FindNextNoteOrChord(measures, measureIdx, itemIdx);
            if (endItem != null)
            {
                var (endMeasureIdx, endItemIdx, endNode) = endItem.Value;
                // Into a chord: connect to the nearest chord tone (single line;
                // LilyPond fans to every tone, a future refinement).
                int endPos = endNode switch
                {
                    NoteItem n => n.StaffPosition,
                    ChordItem c when c.Notes.Length > 0 =>
                        c.Notes.MinBy(cn => System.Math.Abs(cn.StaffPosition - startNote.StaffPosition)).StaffPosition,
                    _ => startNote.StaffPosition,
                };

                glissandos.Add(new GlissandoItem(
                    StartMeasureIndex: measureIdx,
                    StartItemIndex: itemIdx,
                    StartStaffPosition: startNote.StaffPosition,
                    EndMeasureIndex: endMeasureIdx,
                    EndItemIndex: endItemIdx,
                    EndStaffPosition: endPos,
                    Style: GlissandoStyle.Line,
                    SourcePosition: startNote.SourcePosition,
                    VoiceIndex: v));
            }
        }

        return glissandos.ToImmutableArray();
    }
}
