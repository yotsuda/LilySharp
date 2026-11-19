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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// The END of a line written on an empty chord after a voice's last note, when that note
/// fills its bar: <c>fis4.@sustain b4. &lt;&gt;@!sustain |</c> as a piece's last bar, or the
/// <c>repeat tremolo 12 { … }@!sustain</c> that reads the same.
/// </summary>
/// <remarks>
/// The empty chord takes no time and adds no item: its marks stand at the current moment,
/// addressed as "the next item" (MeasureCollector's ChordSyntax arm). When the bar before it
/// is full the builder has already moved on, and the address is the head of the NEXT bar —
/// which is where LilyPond's timestep puts them when music follows. When nothing follows,
/// that bar is never made: the address names a measure the voice does not have, and every
/// engraver passes over a span whose end it cannot find. The pedal bracket closed by such a
/// release was not drawn at all (Calamo request 30, 2026-11-19), nor a text spanner ended
/// there; an ottava bracket collapsed onto its start.
/// <para>
/// Here such an end is given the same moment under the address that exists: the END of the
/// voice's last bar — one past its last item, the bar's length in. That is the address an
/// empty chord closing a bar that is NOT full has always had (<c>c2@sustain e4 &lt;&gt;@!sustain</c>
/// in 4/4), which the engravers draw to the bar's right end.
/// </para>
/// <para>
/// ONLY THE ENDS OF LINES — a pedal's release, a text spanner's stop, an ottava's. MEASURED
/// (LilyPond 2.26.0, Lab sessions/p1314/lp/end.ly, each as the last thing before <c>\bar "|."</c>):
/// a pedal bracket, a text spanner, an ottava bracket and a trill's line ended on the final
/// empty chord are drawn to the closing bar line ("programming error: bounds of this piece
/// aren't breakable", and drawn); a LEVEL (<c>&lt;&gt;\f</c>), a fermata, a text and the text-style
/// pedal's release sign written there are NOT drawn — the final moment has no note column for
/// them to stand on. So a level and a script keep the address no engraver finds, as before:
/// audit/lp-regression/lys/empty-chord.lys ends <c>c1@decresc &lt;&gt;@pp</c>, and its page is
/// LilyPond's, the hairpin to the bar line and no pp.
/// A hairpin's stop is left too: HairpinEngraver reads "the head of the bar after the last"
/// itself, as the final bar line (its endsAtFinalBar).
/// </para>
/// <para>
/// LILYSHARP-OWN in its form (an address is Lily#'s), LilyPond's in its result. Observed by
/// TrailingEmptyChordTests.
/// </para>
/// <para>
/// Done on the captured content, not on the collector's lists: a resumed collect adopts those
/// lists, and after an edit that adds a bar the same mark belongs to that bar's head again.
/// </para>
/// </remarks>
internal static class TrailingMarks
{
    /// <summary>The ends LilyPond draws to the final bar line (the remarks' measurement).</summary>
    private static bool IsALinesEnd(MusicMarkType type) => type is MusicMarkType.SustainOff
        or MusicMarkType.SostenutoOff or MusicMarkType.UnaCordaOff
        or MusicMarkType.TextSpanStop or MusicMarkType.OttavaStop;

    /// <summary>
    /// <paramref name="content"/> with every line's end that stands on the head of the bar
    /// after its voice's last one moved to that last bar's end.
    /// <paramref name="measuresOf"/> gives a staff's voice its measures (default: no such voice).
    /// </summary>
    internal static ScoreContent OntoTheLastBar(ScoreContent content,
        Func<int, int, ImmutableArray<Measure>> measuresOf)
    {
        ImmutableArray<MusicMarkItem>.Builder? marks = null;
        for (int i = 0; i < content.MusicMarks.Length; i++)
        {
            var m = content.MusicMarks[i];
            if (m.AnchorItemIndex != 0 || m.MeasureIndex <= 0 || !IsALinesEnd(m.Type))
                continue;
            var measures = measuresOf(m.StaffIndex, m.VoiceIndex);
            if (measures.IsDefaultOrEmpty || m.MeasureIndex != measures.Length)
                continue;
            int bar = m.MeasureIndex - 1;
            var items = measures[bar].Items;
            var at = Fraction.Zero;
            foreach (var item in items)
                at += item.Duration;
            (marks ??= content.MusicMarks.ToBuilder())[i] = m with { MeasureIndex = bar, AnchorItemIndex = items.Length, AnchorTiming = at };
        }
        return marks == null ? content : content with { MusicMarks = marks.ToImmutable() };
    }
}
