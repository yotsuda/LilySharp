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
/// Post-collect scan for a <c>|:</c> that never meets a <c>:|</c>.
/// </summary>
/// <remarks>
/// ⚠️ THIS SCAN CANNOT BE DONE ON THE WRITTEN TEXT, AND THAT IS THE WHOLE REASON IT LIVES
/// HERE. A section is not a piece of music on its own — it only becomes one when a
/// <c>form</c> lays it out — so a <c>|:</c> written in a section's music may be closed by a
/// <c>:|</c> the FORM writes, and vice versa. The two spellings sit in different layers of
/// the syntax tree and are only siblings after score expansion. Books in the wild are
/// written exactly that way: <c>form main { Intro |: A1 … :| … }</c> where section A1's own
/// music also opens with <c>|:</c>.
/// <para>
/// The collector has already done that expansion: <c>ProcessRepeatBlock</c> emits a form
/// block's own bars into the SAME flat stream as the sections' bars, so by the time there
/// are <see cref="Measure"/>s the two layers are indistinguishable — which is the correct
/// state, because they describe one score. So this reads the measures, exactly as
/// <see cref="SlurPairingScanner"/> does, and for the same reason: the pairing rules must be
/// the ones the page draws, not a second opinion about them.
/// </para>
/// <para>
/// ONE VOICE IS ENOUGH, and unlike a slur that is not an approximation: a repeat barline is
/// a SCORE-level object. <c>MeasureCollector.SynchronizeBarlines</c> propagates the
/// strongest start/end barline at each measure index to every voice ("score-level Timing
/// semantics"), and it is measurable — writing <c>|: … :|</c> in only one part of a
/// two-part score draws the repeat dots on both staves. Every voice therefore carries the
/// same answer.
/// </para>
/// <para>
/// A <c>:|</c> with nothing open is NOT reported: a one-sided end-repeat means "repeat from
/// the beginning of the piece", which is the ordinary reading of the sign and needs no
/// diagnostic. Only the other half is undefined — a <c>|:</c> with no <c>:|</c> anywhere
/// after it marks a span whose end nobody wrote.
/// </para>
/// </remarks>
internal static class RepeatPairingScanner
{
    public static void Scan(Voice voice, List<UnpairedRepeatWarning> sink)
    {
        // ⚠️ REPLACES rather than appends, unlike the slur and beam scans. Those record a
        // per-voice fact, so N voices contribute N findings; this one records a SCORE-level
        // fact that every voice already carries in full (see the remark above on
        // SynchronizeBarlines). Appending would report one dangling '|:' once per staff.
        sink.Clear();

        // Source positions of the '|:' bars still looking for a ':|'.
        var open = new Stack<int>();
        var measures = voice.Measures;
        for (int mi = 0; mi < measures.Length; mi++)
        {
            var m = measures[mi];

            // In WRITTEN order within one measure: its start bar, then its end bar. A
            // one-measure `|: c1 :|` carries both, and reading the end first would make a
            // perfectly ordinary repeat look like a surplus ':|'.
            //
            // A RepeatBoth is one written moment that CLOSES and then OPENS — the collector
            // fuses an adjacent ':|' + '|:' into it and clears the NEXT measure's start —
            // so it has to be read in that order, or a balanced ':| |:' chain would look
            // like a surplus open.
            if (m.StartBarline == BarlineType.RepeatBoth)
                Close(open);
            if (m.StartBarline is BarlineType.RepeatStart or BarlineType.RepeatBoth)
                open.Push(m.SourceStart);

            if (m.EndBarline is BarlineType.RepeatEnd or BarlineType.RepeatBoth)
                Close(open);
            if (m.EndBarline == BarlineType.RepeatBoth)
                open.Push(m.SourceEnd);
        }

        // Reported in the order the bars were WRITTEN (the stack pops innermost-first), so a
        // diagnostic list reads down the score rather than back up it.
        var dangling = new List<int>(open);
        dangling.Reverse();
        foreach (int position in dangling)
            sink.Add(new UnpairedRepeatWarning(position));
    }

    /// <summary>Closes the innermost open <c>|:</c>, or does nothing when none is open —
    /// a one-sided <c>:|</c> repeats from the beginning of the piece and is not a defect.</summary>
    private static void Close(Stack<int> open)
    {
        if (open.Count > 0)
            open.Pop();
    }
}
