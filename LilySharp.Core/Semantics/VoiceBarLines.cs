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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// The bar lines a voice of a section writes ITSELF — a part's music, or a chords row — each
/// with the bars of the section standing before what follows it. The one count the readers
/// that compare voices bar line by bar line go by: LYS1070
/// (<see cref="RepeatSignAgreementValidator"/>), the editor's same-bar jump
/// (<c>Editing.SameBarFinder</c>) and its repeat-sign spreader (<c>Editing.RepeatSignSpreader</c>).
/// </summary>
/// <remarks>
/// A part's lines are counted by <see cref="MeasureModel.Split"/> (the written lines it hands
/// back — the model the validators and the page's own count share), a row's by
/// <see cref="ChordRowBarLines"/>. A bar line the voice does not write itself — one inside a
/// phrase it plays — is not here, and neither is one the count cannot place.
/// </remarks>
internal static class VoiceBarLines
{
    /// <summary>A bar line of the voice's own and the bars of its section before what follows it.</summary>
    internal readonly record struct Line(BarlineSyntax Node, int Bars)
    {
        /// <summary>Where the line's ink ends.</summary>
        public int End => Node.Span.Start + Node.Span.Length;
    }

    /// <summary>The voice's own bar lines, in document order.</summary>
    internal static List<Line> Of(SectionBarCounts.SemanticVoice voice, IReadOnlyDictionary<string, SyntaxNode> phrases)
    {
        var lines = new List<Line>();
        if (voice.IsChords)
        {
            int bars = 0;
            bool holds = false;
            var items = voice.Container is ChordPartBlockSyntax block
                ? block.Items : ChordNameCollector.SectionItems((SectionDeclarationSyntax)voice.Container);
            foreach (var item in items)
            {
                if (item is BarlineSyntax line)
                {
                    if (ChordRowBarLines.Closes(line, holds))
                        bars++;
                    holds = false;
                    lines.Add(new Line(line, bars));
                }
                else if (item is VoltaEndSyntax end && ChordRowBarLines.EndCloses(end, holds))
                    (bars, holds) = (bars + 1, false);
                else if (item is ChordEntrySyntax or RestSyntax or ChordExtendSyntax or PercentMarkSyntax)
                    holds = true;
            }
            return lines;
        }

        return PartLines(voice, phrases, lines);
    }

    /// <summary>The bars of a chords row's section (the count <see cref="Of"/> keeps) after
    /// which an ending's <c>]</c> closes a bar with no bar line written
    /// (<see cref="ChordRowBarLines.EndCloses"/>) — the places the LilyPond twin writes a plain
    /// line for. Empty for a part's music.</summary>
    internal static List<int> RowEndClosedBars(SectionBarCounts.SemanticVoice voice)
    {
        var places = new List<int>();
        if (!voice.IsChords)
            return places;
        int bars = 0;
        bool holds = false;
        var items = voice.Container is ChordPartBlockSyntax block
            ? block.Items : ChordNameCollector.SectionItems((SectionDeclarationSyntax)voice.Container);
        foreach (var item in items)
        {
            if (item is BarlineSyntax line)
            {
                if (ChordRowBarLines.Closes(line, holds))
                    bars++;
                holds = false;
            }
            else if (item is VoltaEndSyntax end && ChordRowBarLines.EndCloses(end, holds))
            {
                (bars, holds) = (bars + 1, false);
                places.Add(bars);
            }
            else if (item is ChordEntrySyntax or RestSyntax or ChordExtendSyntax or PercentMarkSyntax)
                holds = true;
        }
        return places;
    }

    private static List<Line> PartLines(SectionBarCounts.SemanticVoice voice,
        IReadOnlyDictionary<string, SyntaxNode> phrases, List<Line> lines)
    {
        var byEnd = new Dictionary<int, BarlineSyntax>();
        foreach (var node in voice.Container.DescendantNodesOfKinds(SyntaxKind.Barline))
            if (node is BarlineSyntax line)
                byEnd.TryAdd(line.Span.Start + line.Span.Length, line);
        if (byEnd.Count == 0)
            return lines;
        var written = new List<(int End, int Bars)>();
        MeasureModel.Split(voice.Container, phrases, longestVoice: true, writtenLines: written);
        var seen = new HashSet<int>();
        foreach (var (end, bars) in written)
            if (seen.Add(end) && byEnd.TryGetValue(end, out var line))
                lines.Add(new Line(line, bars));
        return lines;
    }
}