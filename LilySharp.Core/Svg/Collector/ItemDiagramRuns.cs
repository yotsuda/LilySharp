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
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Marks each chord item's diagram that repeats the one before it in its voice
/// (<see cref="ArticulationItem.RepeatsDiagramBack"/>): a diagram stands where the chord changes,
/// and at the head of each system (owner's decision 2026-10-31, docs/chord-syntax-design.md Q2).
/// The layout drops a repeat on its own system (LayoutEngine.ItemDiagramsAtChanges); the twin asks
/// LilyPond's breaks (LilyPondExporter.ItemDiagramMarkup).
/// </summary>
/// <remarks>
/// LILYSHARP-OWN: LilyPond draws a markup diagram wherever it is written; the twin asks it the
/// page's question in Scheme. ⚠️ A PASS OVER THE FINISHED TABLES, NOT WALK STATE: the collect resumes mid-score
/// after an edit (WalkCheckpoint), and "the voice's last diagram" kept in a collector field would
/// be lost at the resume point. Read here from the tables, it is the same however they were filled.
/// <para>
/// What ends a run: any other chord in the voice — a name with no item diagram under it (an
/// <c>@chord</c>), a diagram that is not an item's (an <c>@diagram</c>), an item drawing another shape.
/// </para>
/// </remarks>
internal static class ItemDiagramRuns
{
    public static ImmutableArray<ArticulationItem> Mark(
        ImmutableArray<ArticulationItem> articulations, ImmutableArray<ChordNameItem> chordNames)
    {
        if (articulations.IsDefaultOrEmpty || !articulations.Any(a => a.IsItemDiagram))
            return articulations;

        // Each voice's chords in order: (bar, item), the item diagram first at one item, as it is
        // the chord there (a chord(…) item's name stands over its own diagram).
        var events = new List<(int Staff, int Voice, int Measure, int Item, int Kind, int Index)>();
        for (int i = 0; i < articulations.Length; i++)
            if (articulations[i] is { Type: ArticulationType.FretFrame } a)
                events.Add((a.StaffIndex, a.VoiceIndex, a.MeasureIndex, a.ItemIndex, a.IsItemDiagram ? 0 : 1, i));
        if (!chordNames.IsDefaultOrEmpty)
            foreach (var c in chordNames)
                if (!c.UseTiming && !c.IsChordRow)
                    events.Add((c.StaffIndex, c.VoiceIndex, c.MeasureIndex, c.ItemIndex, 2, -1));

        var marked = articulations.ToBuilder();
        foreach (var voice in events.GroupBy(e => (e.Staff, e.Voice)))
        {
            ArticulationItem? last = null;
            (int Measure, int Item) lastAt = (-1, -1);
            foreach (var e in voice.OrderBy(e => e.Measure).ThenBy(e => e.Item).ThenBy(e => e.Kind))
            {
                if (e.Kind != 0)
                {
                    // The name over the item diagram just taken is that chord's own.
                    if (!(e.Kind == 2 && last != null && (e.Measure, e.Item) == lastAt))
                        last = null;
                    continue;
                }
                var diagram = articulations[e.Index];
                if (last != null && last.FrameSpec == diagram.FrameSpec)
                    marked[e.Index] = diagram with
                    {
                        RepeatsDiagramBack = diagram.MeasureIndex - last.MeasureIndex,
                        RepeatsDiagramAt = last.SourcePosition,
                    };
                last = diagram;
                lastAt = (e.Measure, e.Item);
            }
        }
        return marked.MoveToImmutable();
    }
}
