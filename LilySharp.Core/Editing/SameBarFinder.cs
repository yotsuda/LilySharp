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
/// its last. Lyrics are not voices here.
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
        var voices = SectionBarCounts.SemanticVoices(root, phrases);
        int own = voices.FindIndex(v => Contains(v.Container, offset));
        if (own < 0)
            return null;
        // The innermost voice the caret is in (a by-section part block inside its section).
        for (int i = own + 1; i < voices.Count; i++)
            if (Contains(voices[i].Container, offset) && voices[i].Container.Span.Length < voices[own].Container.Span.Length)
                own = i;

        var here = voices[own];
        int bar = 0;
        foreach (var (end, bars) in LinesOf(here, phrases))
            if (end <= offset)
                bar = bars;

        var others = new List<Place>();
        for (int step = 1; step < voices.Count; step++)
        {
            var other = voices[(own + step) % voices.Count];
            if (other.SectionName != here.SectionName || ReferenceEquals(other.Container, here.Container))
                continue;
            others.Add(new Place(other.Label, BarStart(other, LinesOf(other, phrases), bar, text)));
        }
        return new Answer(here.SectionName, bar + 1, here.Label, others);
    }

    private static bool Contains(SyntaxNode node, int offset)
        => offset >= node.Span.Start && offset <= node.Span.Start + node.Span.Length;

    /// <summary>The voice's own bar lines, each with the bars of the section standing before
    /// what follows it.</summary>
    private static List<(int End, int Bars)> LinesOf(SectionBarCounts.SemanticVoice voice,
        IReadOnlyDictionary<string, SyntaxNode> phrases)
    {
        var lines = new List<(int End, int Bars)>();
        if (!voice.IsChords)
        {
            MeasureModel.Split(voice.Container, phrases, longestVoice: true, writtenLines: lines);
            return lines;
        }
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
                lines.Add((line.Span.Start + line.Span.Length, bars));
            }
            else if (item is ChordEntrySyntax or RestSyntax or ChordExtendSyntax)
                holds = true;
        }
        return lines;
    }

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