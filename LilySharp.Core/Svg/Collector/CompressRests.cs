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
using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// <c>layout { compressRests true }</c>: whole-bar rests written one after another
/// (<c>R1 | R1 | R1</c>, typically one per section) are engraved as if they had been written
/// as ONE <c>R1*3</c> — one rest with its count (grammar feedback B5, owner's decision
/// 2026-10-13).
/// </summary>
/// <remarks>
/// <para>
/// LILYSHARP-OWN: LilyPond has no switch for this. MEASURED (2.26.0, Lab sessions/p903/b5):
/// <c>\compressMMRests</c> compresses one written <c>R1*N</c> but leaves separately written
/// <c>R1 | R1 | R1</c> as three one-bar rests — one Multi_measure_rest spanner per written
/// event. So the twin merges the events themselves (<c>LilyPondExporter</c>'s
/// <c>lysCompressRests</c> music function) and this pass does what that rewrite does: a bar's
/// written rest stops OPENING a run (<see cref="RestItem.OpensWrittenRun"/>), which is
/// exactly what the 2nd..Nth bar of an <c>R1*N</c> looks like, and
/// <c>MultiMeasureRestEngraver.FindRuns</c> takes it from there.
/// </para>
/// <para>
/// A rest continues the one before it in its voice only where nothing stands between them —
/// what the twin's rewrite finds between the two events in the part's music: a rehearsal mark,
/// a section label, a tempo, any other mark, a volta, a written bar line other than the plain
/// one, a written <c>break</c> / <c>noBreak</c>, or anything written ON the later rest (a
/// dynamic, a fermata, a text). The FIRST rest of a run may carry them, as <c>R1*4@fermata</c>
/// does. A key / time / clef change opens a new run already (FindRuns).
/// </para>
/// </remarks>
internal static class CompressRests
{
    /// <summary>The score with its consecutive written rests merged, or the score itself when
    /// its layout does not ask for it.</summary>
    public static MultiStaffScore Apply(MultiStaffScore score)
    {
        if (!score.LayoutPlan.CompressRests || score.StaffGroups.IsDefaultOrEmpty)
            return score;

        // The bars a merge stops at, score-wide: a mark at the bar (an end mark on the bar
        // before), a volta starting there, a text there.
        var stops = new HashSet<int>();
        foreach (var mark in score.MusicMarks)
            stops.Add(mark.Position == MusicMarkPosition.End ? mark.MeasureIndex + 1 : mark.MeasureIndex);
        foreach (var volta in score.VoltaBrackets)
            stops.Add(volta.StartMeasureIndex);
        foreach (var text in score.CustomTexts)
            stops.Add(text.MeasureIndex);
        // …and per staff: anything written on that staff's rest.
        var staffStops = new HashSet<(int Staff, int Measure)>();
        foreach (var d in score.Dynamics)
            staffStops.Add((d.StaffIndex, d.MeasureIndex));
        foreach (var a in score.Articulations)
            staffStops.Add((a.StaffIndex, a.MeasureIndex));
        bool labels = score.LayoutPlan.SectionLabels != SectionLabelStyle.None;

        int staffIndex = 0;
        return score with
        {
            StaffGroups = score.StaffGroups.Select(g => g with
            {
                Staves = g.Staves.Select(s =>
                {
                    int si = staffIndex++;
                    return s with
                    {
                        Voices = s.Voices.Select(v => v with { Measures = Merge(v.Measures, si) }).ToImmutableArray(),
                    };
                }).ToImmutableArray(),
            }).ToImmutableArray(),
        };

        ImmutableArray<Measure> Merge(ImmutableArray<Measure> measures, int staff)
        {
            ImmutableArray<Measure>.Builder? built = null;
            for (int m = 1; m < measures.Length; m++)
            {
                var bar = measures[m];
                var before = measures[m - 1];
                int i = MultiMeasureRestEngraver.BarRestIndex(bar);
                if (i != 0 || bar.Items[i] is not RestItem { OpensWrittenRun: true } rest
                    || MultiMeasureRestEngraver.BarRestIndex(before) < 0
                    || before.IsPickup || bar.IsPickup
                    || stops.Contains(m) || staffStops.Contains((staff, m))
                    || (labels && bar.SectionLabel != null)
                    || before.EndBarline is not (BarlineType.Single or BarlineType.None)
                    || bar.StartBarline is not (BarlineType.Single or BarlineType.None)
                    || before.LineBreakPermission != BreakPermission.Allow)
                    continue;
                built ??= measures.ToBuilder();
                built[m] = bar with { Items = bar.Items.SetItem(i, rest with { OpensWrittenRun = false }) };
            }
            return built?.MoveToImmutable() ?? measures;
        }
    }
}
