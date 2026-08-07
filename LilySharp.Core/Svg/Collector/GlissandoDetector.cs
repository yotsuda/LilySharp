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
            // The start column contributes every note head as a line of its own — a
            // chord glissando is a FAN, one Glissando per member. A chord start used
            // to fall through this walk unread (the item pattern was NoteItem-only),
            // which swallowed eight of regression glissando-accidental.ly's ten lines
            // without a diagnostic.
            // LILYPOND-REF: scm/scheme-engravers.scm:2563-2579 Glissando_engraver —
            //   one grob per left-bound head (possible-left-bounds = the column's
            //   note heads, in array order).
            var startHeads = item switch
            {
                NoteItem { HasGlissando: true } n => new[] { (Pos: n.StaffPosition, Member: -1) },
                ChordItem { HasGlissando: true } c when c.Notes.Length > 0 =>
                    c.Notes.Select((cn, i) => (Pos: cn.StaffPosition, Member: i)).ToArray(),
                _ => null,
            };
            if (startHeads is null)
                continue;

            // Glissandos connect to the next note of any pitch — a note OR
            // a chord (a chord endpoint used to be skipped, dropping the gliss).
            var endItem = NoteScan.FindNextNoteOrChord(measures, measureIdx, itemIdx);
            if (endItem == null)
                continue;
            var (endMeasureIdx, endItemIdx, endNode) = endItem.Value;
            var endHeads = endNode switch
            {
                NoteItem n => new[] { (Pos: n.StaffPosition, Member: -1) },
                ChordItem c when c.Notes.Length > 0 =>
                    c.Notes.Select((cn, i) => (Pos: cn.StaffPosition, Member: i)).ToArray(),
                _ => startHeads,
            };

            // Pair heads by WRITTEN order, index for index. A start head with no
            // right-bound partner gets no line (LilyPond suicides the unbounded
            // grob); an unmatched end head is simply not a bound.
            // LILYPOND-REF: scm/scheme-engravers.scm:2543-2558 — for-each pairs
            //   possible-right-bounds with the open grobs, then suicides any grob
            //   whose RIGHT bound stayed unset.
            int lines = System.Math.Min(startHeads.Length, endHeads.Length);
            for (int k = 0; k < lines; k++)
            {
                glissandos.Add(new GlissandoItem(
                    StartMeasureIndex: measureIdx,
                    StartItemIndex: itemIdx,
                    StartStaffPosition: startHeads[k].Pos,
                    EndMeasureIndex: endMeasureIdx,
                    EndItemIndex: endItemIdx,
                    EndStaffPosition: endHeads[k].Pos,
                    Style: GlissandoStyle.Line,
                    SourcePosition: item.SourcePosition,
                    VoiceIndex: v,
                    StartMemberIndex: startHeads[k].Member,
                    EndMemberIndex: endHeads[k].Member));
            }
        }

        return glissandos.ToImmutableArray();
    }
}
