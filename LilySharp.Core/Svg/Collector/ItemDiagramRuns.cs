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
/// Marks each chord item's diagram and name that repeat the item before it in its voice
/// (<see cref="ArticulationItem.RepeatsDiagramBack"/>, <see cref="ChordNameItem.RepeatsNameBack"/>):
/// they stand where the chord changes, and at the head of each system (owner's decisions
/// 2026-10-31, docs/chord-syntax-design.md Q2). The layout drops a repeat on its own system
/// (LayoutEngine.ItemDiagramsAtChanges); the twin asks LilyPond's breaks (LilyPondExporter.ItemDiagramMarkup).
/// </summary>
/// <remarks>
/// LILYSHARP-OWN: LilyPond draws a markup diagram wherever it is written; the twin asks it the
/// page's question in Scheme. ⚠️ A PASS OVER THE FINISHED TABLES, NOT WALK STATE: the collect resumes
/// mid-score after an edit (WalkCheckpoint), and "the voice's last diagram" kept in a collector
/// field would be lost at the resume point. Read here from the tables, it is the same however
/// they were filled.
/// <para>
/// What ends a run: any other chord in the voice — an <c>@chord</c>'s name, an <c>@diagram</c>,
/// an item that prints another shape or name, or none (a <c>diagram(…)</c> item ends a run of
/// names, a <c>chord(…)</c> item with no diagram one of diagrams).
/// </para>
/// </remarks>
internal static class ItemDiagramRuns
{
    /// <summary><paramref name="content"/> with its item diagrams and names marked — once its
    /// staves are final (after CombinedStaffAddressing), as ScoreAssembler builds the score.</summary>
    public static ScoreContent Mark(ScoreContent content)
    {
        var (articulations, chordNames) = Mark(content.Articulations, content.ChordNames);
        return content with { Articulations = articulations, ChordNames = chordNames };
    }

    public static (ImmutableArray<ArticulationItem> Articulations, ImmutableArray<ChordNameItem> ChordNames) Mark(
        ImmutableArray<ArticulationItem> articulations, ImmutableArray<ChordNameItem> chordNames)
    {
        bool anyDiagram = !articulations.IsDefaultOrEmpty && articulations.Any(a => a.IsItemDiagram);
        bool anyName = !chordNames.IsDefaultOrEmpty && chordNames.Any(c => c.IsItemName);
        if (!anyDiagram && !anyName)
            return (articulations, chordNames);

        // Each voice's chords by site (bar, item): the item's diagram and name there, and
        // whether anything else named or drew a chord there.
        var sites = new Dictionary<(int Staff, int Voice), SortedDictionary<(int Measure, int Item), Site>>();
        Site At(int staff, int voice, int measure, int item)
        {
            if (!sites.TryGetValue((staff, voice), out var voiceSites))
                sites[(staff, voice)] = voiceSites = new();
            if (!voiceSites.TryGetValue((measure, item), out var site))
                voiceSites[(measure, item)] = site = new Site();
            return site;
        }
        if (!articulations.IsDefaultOrEmpty)
            for (int i = 0; i < articulations.Length; i++)
                if (articulations[i] is { Type: ArticulationType.FretFrame } a)
                {
                    var site = At(a.StaffIndex, a.VoiceIndex, a.MeasureIndex, a.ItemIndex);
                    if (a.IsItemDiagram) site.Diagram = i; else site.Other = true;
                }
        if (!chordNames.IsDefaultOrEmpty)
            for (int i = 0; i < chordNames.Length; i++)
                if (chordNames[i] is { UseTiming: false, IsChordRow: false } c)
                {
                    var site = At(c.StaffIndex, c.VoiceIndex, c.MeasureIndex, c.ItemIndex);
                    if (c.IsItemName) site.Name = i; else site.Other = true;
                }

        var markedArticulations = anyDiagram ? articulations.ToBuilder() : null;
        var markedNames = anyName ? chordNames.ToBuilder() : null;
        foreach (var voiceSites in sites.Values)
        {
            ArticulationItem? lastDiagram = null;
            ChordNameItem? lastName = null;
            foreach (var site in voiceSites.Values)
            {
                if (site.Other)
                    (lastDiagram, lastName) = (null, null);
                if (site.Diagram is int d)
                {
                    var diagram = articulations[d];
                    if (lastDiagram != null && lastDiagram.FrameSpec == diagram.FrameSpec)
                        markedArticulations![d] = diagram with
                        {
                            RepeatsDiagramBack = diagram.MeasureIndex - lastDiagram.MeasureIndex,
                            RepeatsDiagramAt = lastDiagram.SourcePosition,
                        };
                    lastDiagram = diagram;
                }
                else
                    lastDiagram = null;
                if (site.Name is int n)
                {
                    var name = chordNames[n];
                    if (lastName != null && lastName.ChordText == name.ChordText && lastName.DrawnShape == name.DrawnShape)
                        markedNames![n] = name with
                        {
                            RepeatsNameBack = name.MeasureIndex - lastName.MeasureIndex,
                            RepeatsNameAt = lastName.SourcePosition,
                        };
                    lastName = name;
                }
                else
                    lastName = null;
            }
        }
        return (markedArticulations?.MoveToImmutable() ?? articulations, markedNames?.MoveToImmutable() ?? chordNames);
    }

    private sealed class Site
    {
        public int? Diagram, Name;
        public bool Other;
    }
}
