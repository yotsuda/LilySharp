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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// Where the bar under a caret is written in the OTHER voices of its section — the parts and
/// chords rows that write the same section — for an editor's "go to the same bar in another
/// part" (owner's request 2026-11-14: the same bar of two parts may stand many lines apart).
/// </summary>
/// <remarks>
/// A bar's place is the bars of its section before it, counted from the bar lines each voice
/// writes, as its own reader counts them (<see cref="MeasureModel.Split"/>'s written lines for
/// a part, <see cref="ChordRowBarLines"/> for a row) — the count LYS1070 compares signs by.
/// The caret's bar is the one after the last bar line written before it; the place in another
/// voice is right after the bar line that closes the bar before. A bar with no bar line of the
/// voice's own before it — one inside a phrase the voice plays, or inside a <c>R1*4</c> — is
/// answered with the nearest written line before it, and a voice that writes fewer bars with
/// its last. A lyrics cell joins them (owner's request 2026-11-17): its bars are its lyric
/// measures (<see cref="LyricSyllableReader"/>'s, every parsed measure one bar),
/// each closed where its measure ends — a jump goes to its words, and from them to the music.
/// </remarks>
public static class SameBarFinder
{
    /// <summary>A voice's copy of the bar: the voice as a message names it
    /// (<c>part 'bass'</c>, <c>chords 'prog'</c>) and the offset its bar opens at.</summary>
    public readonly record struct Place(string Voice, int Offset);

    /// <summary>The answer for a caret: its section, its bar (1-based), the voice it is in, and
    /// the other voices' places, in document order starting after the caret's own.</summary>
    public sealed record Answer(string Section, int Bar, string Voice, IReadOnlyList<Place> Others);

    /// <summary>The same bar in the other voices for the caret at <paramref name="offset"/>, or
    /// null when the caret is in no section's music.</summary>
    public static Answer? At(SyntaxTree tree, int offset)
    {
        var root = tree.GetRoot();
        string text = root.ToFullString();
        var phrases = SectionBarCounts.PhraseBodies(root);
        // The voices and the lyrics cells, each with its bar lines, in document order.
        var voices = SectionBarCounts.SemanticVoices(root, phrases)
            .Select(v => (Voice: v, Lines: LinesOf(v, phrases)))
            .Concat(LyricsCellsOf(root).Select(c => (Voice: c, Lines: LyricLines(c.Container))))
            .DistinctBy(v => v.Voice.Container.Span.Start)
            .OrderBy(v => v.Voice.Container.Span.Start)
            .ToList();
        int own = voices.FindIndex(v => Contains(v.Voice.Container, offset));
        if (own < 0)
            return null;
        // The innermost voice the caret is in (a by-section part block inside its section).
        for (int i = own + 1; i < voices.Count; i++)
            if (Contains(voices[i].Voice.Container, offset) && voices[i].Voice.Container.Span.Length < voices[own].Voice.Container.Span.Length)
                own = i;

        var (here, hereLines) = voices[own];
        int bar = 0;
        foreach (var (end, bars) in hereLines)
            if (end <= offset)
                bar = bars;

        var others = new List<Place>();
        for (int step = 1; step < voices.Count; step++)
        {
            var (other, lines) = voices[(own + step) % voices.Count];
            if (other.SectionName != here.SectionName || ReferenceEquals(other.Container, here.Container))
                continue;
            others.Add(new Place(other.Label, BarStart(other, lines, bar, text)));
        }
        return new Answer(here.SectionName, bar + 1, here.Label, others);
    }

    /// <summary>Every lyrics cell: by part (<c>lyrics w { section A { … } }</c>) and by section
    /// (<c>section A { lyrics w { … } }</c>).</summary>
    private static IEnumerable<SectionBarCounts.SemanticVoice> LyricsCellsOf(SyntaxNode root)
    {
        foreach (var cell in SectionBarCounts.LyricsCells(root))
            yield return cell;
        foreach (var block in root.DescendantNodes<LyricsBlockSyntax>())
            if (!block.HasSections && block.Parent is SectionDeclarationSyntax section)
                yield return SectionBarCounts.LyricsCell(section.SectionName, block, block, SectionBarCounts.LyricsAnchor(block), groupedByPart: false);
    }

    /// <summary>A lyrics cell's bar lines: the end of each lyric measure, with the bars before what
    /// follows it — the plain run, else the first verse's (LyricSyllableReader.CountBars).</summary>
    internal static List<(int End, int Bars)> LyricLines(SyntaxNode container)
    {
        var measures = new List<SyntaxNode>();
        SyntaxNode? firstVerse = null;
        for (int i = 0; i < container.SlotCount; i++)
            if (container.GetChild(i) is SyntaxNode m)
            {
                if (m.Kind == SyntaxKind.LyricMeasure)
                    measures.Add(m);
                else if (m.Kind == SyntaxKind.LyricVerse)
                    firstVerse ??= m;
            }
        if (measures.Count == 0 && firstVerse != null)
            measures.AddRange(firstVerse.DescendantNodes().Where(n => n.Kind == SyntaxKind.LyricMeasure));
        // (every parsed measure is one bar, as LyricSyllableReader.CountRun counts them: the parser
        // has already folded a lone opening `|` into its run)
        return [.. measures.Select((m, i) => (m.Span.Start + m.Span.Length, i + 1))];
    }

    private static bool Contains(SyntaxNode node, int offset)
        => offset >= node.Span.Start && offset <= node.Span.Start + node.Span.Length;

    /// <summary>The voice's own bar lines, each with the bars of the section standing before
    /// what follows it (<see cref="VoiceBarLines"/>).</summary>
    private static List<(int End, int Bars)> LinesOf(SectionBarCounts.SemanticVoice voice,
        IReadOnlyDictionary<string, SyntaxNode> phrases)
        => [.. VoiceBarLines.Of(voice, phrases).Select(line => (line.End, line.Bars))];

    /// <summary>Where bar <paramref name="bar"/> (0-based) of the voice opens: after the line
    /// that closes the bar before it, else after the nearest line before that, else at the
    /// voice's first item.</summary>
    private static int BarStart(SectionBarCounts.SemanticVoice voice, List<(int End, int Bars)> lines, int bar, string text)
    {
        int at = -1, reached = -1;
        foreach (var (end, bars) in lines)
            if (bars <= bar && bars > reached)
                (at, reached) = (end, bars);
        if (at < 0)
        {
            // Before every bar line: the voice's opening brace.
            int open = text.IndexOf('{', voice.Container.Span.Start);
            int close = voice.Container.Span.Start + voice.Container.Span.Length;
            at = open >= 0 && open < close ? open + 1 : voice.Container.Span.Start;
        }
        while (at < text.Length && char.IsWhiteSpace(text[at]))
            at++;
        return at;
    }
}