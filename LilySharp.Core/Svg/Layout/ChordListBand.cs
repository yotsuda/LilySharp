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
using System.Linq;
using LilySharp.Core.Music;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// The chords a score's chord list shows (<c>layout { chordList true }</c>; owner's
/// design 2026-09-29, HANDOFF §2 K5 ⑤): every chord the score names — its <c>chords</c> rows
/// and every <c>@chord</c> — each once, in order of first appearance, with the name the
/// score prints for it (the capo's pressed name, <c>chordNames</c> included) and the diagram
/// it draws at that first appearance; a chord that draws none in the score shows its usual
/// shape here (the list is where the shape is looked up), on the layout's tuning, else the
/// guitar — none under <c>chordDiagrams none</c> or where the tuning has no shape for it.
/// </summary>
/// <remarks>
/// LILYSHARP-OWN: LilyPond has no chord list. Read off the collected model
/// (<see cref="MultiStaffScore.ChordNames"/>) rather than the tree, so the names are the ones
/// the page prints (a Roman row's absolute name, a bare <c>@chord</c>'s derived one, a capo's
/// pressed one) and the shapes the ones it draws (<see cref="ChordNameItem.DrawnShape"/>).
/// "N.C." and quoted text name no chord and are not listed.
/// </remarks>
internal static class ChordListBand
{
    /// <summary>The list's entries for <paramref name="score"/>, in order of first appearance.</summary>
    internal static IReadOnlyList<ChordListEntry> EntriesOf(MultiStaffScore score)
    {
        var plan = score.LayoutPlan;
        if (!plan.ChordList || score.ChordNames.IsDefaultOrEmpty)
            return [];
        var tuning = Semantics.ChordDiagramsKey.Resolve(plan.ChordDiagrams, null);
        var seen = new List<ChordStructure>();
        var entries = new List<ChordListEntry>();
        foreach (var c in score.ChordNames.OrderBy(c => c.MeasureIndex).ThenBy(c => c.Timing.ToDouble()).ThenBy(c => c.StaffIndex))
        {
            if (c.Structure is not { } chord || seen.Any(s => ChordShapeTable.SameChord(s, chord)))
                continue;
            seen.Add(chord);
            string? spec = c.DrawnShape;
            if (spec == null && tuning is { } t)
                spec = ChordShapes.Drawn(t, [], all: true, chord, plan.ChordDiagramTable, null, plan.Chords.Capo)?.FrameSpec;
            entries.Add(new ChordListEntry(c.ChordText, c.SuperFrom, c.BracketSuperFrom, spec));
        }
        return entries;
    }
}
