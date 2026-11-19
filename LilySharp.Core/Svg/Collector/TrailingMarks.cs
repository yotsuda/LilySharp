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
using LilySharp.Core.Syntax;

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
/// them to stand on.
/// A hairpin's stop is left as it is: HairpinEngraver reads "the head of the bar after the
/// last" itself, as the final bar line (its endsAtFinalBar).
/// </para>
/// <para>
/// …AND A LEVEL AND A TEXT, where Lily# departs from LilyPond (owner's decision
/// 2026-11-19, session 1315: "最後の &lt;&gt; に書いた強弱・フェルマータ・文字もレンダできるようにしたい").
/// LILYSHARP-OWN: LilyPond prints none of them (the measurement above), for want of a column
/// and not by an engraving rule; what a writer wrote there is real notation — the level a
/// closing hairpin runs to (<c>c1@decresc &lt;&gt;@pp</c>), the fermata over the closing bar line,
/// a "fine" — and mid-piece the same empty chord prints them on the next bar's head. They
/// stand on the last bar's end like the lines' ends. audit/lp-regression/lys/empty-chord.lys
/// ends that way: its page has the pp LilyPond's has not, and its last hairpin ends before
/// the pp, as a hairpin running to a level does anywhere.
/// A FERMATA written there stands over the closing bar line (ArticulationEngraver's bar-end
/// arm — the one script with a place of its own; every other script is drawn on an item and
/// is left where it was, undrawn: an accent after the last note marks nothing).
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

        // A level or a text (the remarks' owner's decision).
        ImmutableArray<DynamicItem>.Builder? dynamics = null;
        for (int i = 0; i < content.Dynamics.Length; i++)
        {
            var d = content.Dynamics[i];
            if (d.ItemIndex != 0 || d.MeasureIndex <= 0)
                continue;
            var measures = measuresOf(d.StaffIndex, d.VoiceIndex);
            if (measures.IsDefaultOrEmpty || d.MeasureIndex != measures.Length)
                continue;
            int bar = d.MeasureIndex - 1;
            var items = measures[bar].Items;
            var at = Fraction.Zero;
            foreach (var item in items)
                at += item.Duration;
            (dynamics ??= content.Dynamics.ToBuilder())[i] = d with { MeasureIndex = bar, ItemIndex = items.Length, Moment = at };
        }

        // A fermata: the one script that has a place with no note (ArticulationEngraver's
        // bar-end arm). Any other script is drawn on an item and stays where it was.
        ImmutableArray<ArticulationItem>.Builder? scripts = null;
        for (int i = 0; i < content.Articulations.Length; i++)
        {
            var a = content.Articulations[i];
            if (a.ItemIndex != 0 || a.MeasureIndex <= 0
                || a.Type is not (ArticulationType.Fermata or ArticulationType.FermataShort or ArticulationType.FermataLong))
                continue;
            var measures = measuresOf(a.StaffIndex, a.VoiceIndex);
            if (measures.IsDefaultOrEmpty || a.MeasureIndex != measures.Length)
                continue;
            (scripts ??= content.Articulations.ToBuilder())[i] = a with { MeasureIndex = a.MeasureIndex - 1, ItemIndex = measures[a.MeasureIndex - 1].Items.Length };
        }

        if (marks == null && dynamics == null && scripts == null)
            return content;
        return content with
        {
            MusicMarks = marks?.ToImmutable() ?? content.MusicMarks,
            Dynamics = dynamics?.ToImmutable() ?? content.Dynamics,
            Articulations = scripts?.ToImmutable() ?? content.Articulations,
        };
    }
}
