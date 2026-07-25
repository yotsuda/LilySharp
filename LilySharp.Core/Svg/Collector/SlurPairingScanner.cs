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
/// Post-collect scan for slur marks that pair with nothing. Whatever this reports,
/// <see cref="SlurDetector"/> silently drops: it draws a slur only when a <c>)</c> finds a
/// <c>(</c> on its stack, so a surplus <c>)</c> and a <c>(</c> still open at the end are
/// both discarded without a word, and one bar of music quietly loses its phrasing.
/// </summary>
/// <remarks>
/// THE PAIRING RULES ARE THE RENDERER'S, not a second opinion about them — a warning that
/// disagreed with what gets drawn would be worse than no warning:
/// <list type="bullet">
/// <item>marks pair as a STACK, innermost first, exactly as <c>SlurDetector</c> pops;</item>
/// <item>the scan is PER VOICE, because <c>SlurDetector</c> clears its stack at every
/// voice change (LILYPOND-REF: ly/engraver-init.ly — Slur_engraver lives in the Voice
/// context), so a <c>(</c> left open when a voice ends never pairs with anything;</item>
/// <item>a note carrying BOTH marks (<c>c4()</c>, or the middle of <c>c( d) e)</c>) opens
/// before it closes, which is the order <c>SlurDetector</c> reads them in.</item>
/// </list>
/// <para>
/// A <c>(</c> written where no note precedes it — <c>(e c4 d)</c> — never becomes a mark at
/// all: slur marks annotate the note BEFORE them
/// (<c>MeasureCollector.MusicWalk.PeekMarkers</c>). Nothing here can see that <c>(</c>; it
/// is reported through the <c>)</c> that is then left with nothing to pair with, which is
/// the one bar of music that goes missing in that spelling.
/// </para>
/// <para>
/// LilyPond warns on the same two shapes (its Slur_engraver reports an unterminated slur
/// and an unmatched close), so this is a fidelity fix as much as an editor one.
/// </para>
/// </remarks>
internal static class SlurPairingScanner
{
    public static void Scan(Voice voice, List<UnpairedSlurWarning> sink)
    {
        // Source positions of the '(' marks still looking for a ')'.
        var open = new Stack<int>();
        var measures = voice.Measures;
        for (int mi = 0; mi < measures.Length; mi++)
        {
            var items = measures[mi].Items;
            for (int ii = 0; ii < items.Length; ii++)
            {
                if (!TryGetSlurFlags(items[ii], out bool hasStart, out bool hasEnd))
                    continue;
                if (hasStart)
                    open.Push(items[ii].SourcePosition);
                if (hasEnd)
                {
                    if (open.Count > 0)
                        open.Pop();
                    else
                        sink.Add(new UnpairedSlurWarning(items[ii].SourcePosition, IsOpen: false));
                }
            }
        }

        // Whatever is still open when the voice ends is dropped by the renderer. Reported
        // in the order the marks were WRITTEN (the stack pops innermost-first), so a
        // diagnostic list reads down the score rather than back up it.
        var dangling = new List<int>(open);
        dangling.Reverse();
        foreach (int position in dangling)
            sink.Add(new UnpairedSlurWarning(position, IsOpen: true));
    }

    /// <summary>Slurs attach to a note OR a chord — the same two item kinds
    /// <see cref="SlurDetector.DetectSlurs"/> pairs.</summary>
    private static bool TryGetSlurFlags(MusicItem item, out bool hasStart, out bool hasEnd)
    {
        switch (item)
        {
            case NoteItem n: hasStart = n.HasSlurStart; hasEnd = n.HasSlurEnd; return true;
            case ChordItem c: hasStart = c.HasSlurStart; hasEnd = c.HasSlurEnd; return true;
            default: hasStart = false; hasEnd = false; return false;
        }
    }
}
