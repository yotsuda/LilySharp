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
/// Records the manual beam brackets <see cref="BeamDetector"/> discards: a <c>[</c> that is
/// never closed, and a <c>]</c> read with none open. Neither is a parse error and neither
/// leaves the score bare — the notes simply fall back to AUTOMATIC beaming — so until this
/// existed the engraved grouping could differ from the written one without a word.
/// </summary>
/// <remarks>
/// <para>
/// THE PAIRING RULE IS THE DETECTOR'S, not a second opinion about it. <see cref="BeamDetector"/>
/// collects every <c>HasBeamStart</c>/<c>HasBeamEnd</c> marker of a voice in order and matches
/// them with a STACK (DetectCrossMeasureManualBeams: "the i-th open <c>[</c> matches the i-th
/// close <c>]</c>"); a pair landing inside one measure is handed to the per-measure pass, and
/// a <c>]</c> arriving with an empty stack is dropped. This walk is that walk, so a bracket
/// reported here is exactly a bracket the detector built no group from.
/// </para>
/// <para>
/// The scan is PER VOICE for the same reason the slur scan is: the detector matches within one
/// voice's measures, so a <c>[</c> still open when a voice ends never pairs with anything.
/// </para>
/// <para>
/// MEASURED, on a bar where the manual grouping and the automatic one differ
/// (<c>c8[ d8 e8 f8 g8] a8 b8 c8</c> in 4/4 — five beamed, which automatic beaming never
/// produces): the closed form engraves its five-note beam; drop either bracket and the output
/// is byte-identical to the same notes with no bracket at all. So the loss is the grouping,
/// not the beam, and the message says that rather than borrowing the slur's "nothing is
/// drawn".
/// </para>
/// </remarks>
internal static class BeamPairingScanner
{
    public static void Scan(Voice voice, List<UnpairedBeamWarning> sink)
    {
        // Source positions of the '[' marks still looking for a ']'.
        var open = new Stack<int>();
        var measures = voice.Measures;
        for (int mi = 0; mi < measures.Length; mi++)
        {
            var items = measures[mi].Items;
            for (int ii = 0; ii < items.Length; ii++)
            {
                if (!TryGetBeamFlags(items[ii], out bool hasStart, out bool hasEnd))
                    continue;
                // A note carrying BOTH (`c8[]`) opens before it closes, the order the
                // detector reads its markers in.
                if (hasStart)
                    open.Push(items[ii].SourcePosition);
                if (hasEnd)
                {
                    if (open.Count > 0)
                        open.Pop();
                    else
                        sink.Add(new UnpairedBeamWarning(items[ii].SourcePosition, IsOpen: false));
                }
            }
        }

        // Whatever is still open when the voice ends builds no group. Reported in the order
        // the brackets were WRITTEN (the stack pops innermost-first), so a reader walking
        // down the file meets the complaints in the order of the marks.
        foreach (var position in ReverseOf(open))
            sink.Add(new UnpairedBeamWarning(position, IsOpen: true));
    }

    /// <summary>Only notes and chords carry beam brackets; rests and everything else are
    /// transparent to the scan, exactly as they are to the detector's marker collection.</summary>
    private static bool TryGetBeamFlags(MusicItem item, out bool hasStart, out bool hasEnd)
    {
        switch (item)
        {
            case NoteItem n:
                hasStart = n.HasBeamStart;
                hasEnd = n.HasBeamEnd;
                return true;
            case ChordItem c:
                hasStart = c.HasBeamStart;
                hasEnd = c.HasBeamEnd;
                return true;
            default:
                hasStart = hasEnd = false;
                return false;
        }
    }

    /// <summary>The stack's contents in WRITTEN order (a Stack enumerates newest-first).</summary>
    private static IEnumerable<int> ReverseOf(Stack<int> open)
    {
        var written = new List<int>(open);
        written.Reverse();
        return written;
    }
}
