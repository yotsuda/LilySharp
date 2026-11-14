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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// The repeat signs, the endings and the navigation marks the voices of ONE section write in
/// their music — parts and chord rows — each by the bars of the section standing before it
/// (<see cref="VoiceBarLines"/>, the count LYS1070 compares signs by). A sign written in a
/// section's music is the score's, whichever voice writes it (docs/inline-repeat-design.md
/// §3.2), so the readers that need the section's signs as one set ask here: the score's sign
/// column (<see cref="RepeatStream"/>) and the LilyPond twin, which writes them into every voice.
/// </summary>
/// <remarks>
/// A part's lines are placed by <see cref="VoiceBarLines"/>; an ending runs from the bars
/// standing where it opens to the bars standing where it closes, a bar still open there
/// counted; a navigation mark stands at the bar line it belongs to — a sign (<c>segno</c>,
/// <c>coda</c>) at the head of the bar ahead, a text at the end of the bar behind, which is one
/// place: the bars closed before it, and one more when the bar it is written in already holds
/// music. Signs inside a phrase the voice plays are not here (LYS1034 keeps them out of phrases).
/// </remarks>
internal sealed class SectionSigns
{
    /// <summary>An ending a voice writes over the bars <c>[From, To)</c> of its section.
    /// <paramref name="Nested"/>: the ending holds its music (a part's); a row's stands flat
    /// among its chords.</summary>
    internal sealed record Ending(int From, int To, InlineVoltaSyntax Node, SyntaxNode Container, bool Nested);

    /// <summary>A navigation mark a voice writes, with the bars of its section before the bar
    /// line it stands at.</summary>
    internal readonly record struct Mark(int Bars, NavigationMarkSyntax Node, SyntaxNode Container);

    /// <summary>The first repeat sign written at each place, and the voice that wrote it.</summary>
    public readonly SortedDictionary<int, (BarlineSyntax Node, SyntaxNode Container)> Lines = new();

    /// <summary>Every ending the voices write, voice by voice in document order.</summary>
    public readonly List<Ending> Endings = [];

    /// <summary>Every navigation mark the voices write, voice by voice in document order.</summary>
    public readonly List<Mark> Marks = [];

    /// <summary>No repeat sign and no ending (navigation marks aside).</summary>
    public bool HasNoRepeats => Lines.Count == 0 && Endings.Count == 0;

    /// <summary>What the voices of section <paramref name="name"/> among
    /// <paramref name="voices"/> write of the score's repeats.</summary>
    public static SectionSigns Of(string name, IEnumerable<SectionBarCounts.SemanticVoice> voices,
        IReadOnlyDictionary<string, SyntaxNode> phrases)
    {
        var signs = new SectionSigns();
        foreach (var voice in voices)
        {
            if (voice.SectionName != name || voice.IsLyrics)
                continue;
            var lines = VoiceBarLines.Of(voice, phrases);
            foreach (var line in lines)
                if (SyntaxFacts.IsRepeatBarlineKind(line.Node.BarToken.Kind))
                    signs.Lines.TryAdd(line.Bars, (line.Node, voice.Container));
            if (voice.IsChords)
                ReadRow(voice, signs);
            else
                ReadMusic(voice, lines, signs);
        }
        return signs;
    }

    /// <summary>A part's endings and marks: each <c>[N. … ]</c> and each navigation mark of
    /// its own music (not a phrase's).</summary>
    private static void ReadMusic(SectionBarCounts.SemanticVoice voice, List<VoiceBarLines.Line> lines, SectionSigns signs)
    {
        foreach (var ending in voice.Container.DescendantNodes<InlineVoltaSyntax>())
        {
            int start = ending.Span.Start, end = ending.Span.Start + ending.Span.Length;
            int from = 0;
            VoiceBarLines.Line? lastInside = null, firstAfter = null;
            foreach (var line in lines)
            {
                if (line.End <= start)
                    from = line.Bars;
                else if (line.End <= end)
                    lastInside = line;
                else if (firstAfter == null)
                    firstAfter = line;
            }
            // The ending's last bar is closed by a line of its own, or by the next line the
            // voice writes (`[1. g a b c :|`, `[2. g f e d ] |`).
            var last = ending.Items.LastOrDefault(item => item is BarlineSyntax || TakesTime(item));
            int to = last is BarlineSyntax && lastInside is { } inside ? inside.Bars
                : firstAfter is { } after ? after.Bars
                : voice.Bars;
            if (to > from)
                signs.Endings.Add(new Ending(from, to, ending, voice.Container, Nested: true));
        }

        foreach (var mark in voice.Container.DescendantNodes<NavigationMarkSyntax>())
        {
            int at = mark.Span.Start;
            int bars = 0, lineEnd = -1;
            foreach (var line in lines)
            {
                if (line.End > at)
                    break;
                (bars, lineEnd) = (line.Bars, line.End);
            }
            // The bar the mark is written in already holds music: the mark is at its far line.
            // Music is what stands after the block's brace: until session 1254 the block's own
            // header (the section's name) was read as music too, so a sign that opens the
            // section — `section A { segno c1 | … }` — stood one bar late on the column.
            bool open = false;
            if (mark.Parent is { } parent)
            {
                int body = lineEnd;
                for (int i = 0; i < parent.SlotCount; i++)
                    if (parent.GetChild(i) is SyntaxTokenNode { Kind: SyntaxKind.OpenBrace } brace)
                    {
                        body = System.Math.Max(body, brace.Span.Start + brace.Span.Length);
                        break;
                    }
                foreach (var sibling in parent.ChildNodes())
                {
                    if (sibling.Span.Start >= at)
                        break;
                    if (sibling.Span.Start >= body && sibling is not BarlineSyntax && TakesTime(sibling))
                        open = true;
                }
            }
            signs.Marks.Add(new Mark(bars + (open ? 1 : 0), mark, voice.Container));
        }
    }

    /// <summary>A row's endings and marks: its <c>[N.</c>, <c>]</c> and marks stand flat among
    /// its chords, and an ending with no <c>]</c> closes at the <c>:|</c> or at the next ending.</summary>
    private static void ReadRow(SectionBarCounts.SemanticVoice voice, SectionSigns signs)
    {
        var items = voice.Container is ChordPartBlockSyntax block
            ? block.Items : ChordNameCollector.SectionItems((SectionDeclarationSyntax)voice.Container);
        int bars = 0;
        bool holds = false;
        (InlineVoltaSyntax Node, int From)? open = null;
        void Close()
        {
            int to = bars + (holds ? 1 : 0);
            if (open is { } o && to > o.From)
                signs.Endings.Add(new Ending(o.From, to, o.Node, voice.Container, Nested: false));
            open = null;
        }
        foreach (var item in items)
        {
            switch (item)
            {
                case BarlineSyntax line:
                    if (ChordRowBarLines.Closes(line, holds))
                        bars++;
                    holds = false;
                    if (line.BarToken.Kind is SyntaxKind.RepeatEndBar or SyntaxKind.RepeatBothBar)
                        Close();
                    break;
                case ChordEntrySyntax or RestSyntax or ChordExtendSyntax:
                    holds = true;
                    break;
                case InlineVoltaSyntax ending:
                    Close();
                    open = (ending, bars + (holds ? 1 : 0));
                    break;
                case VoltaEndSyntax:
                    Close();
                    break;
                case NavigationMarkSyntax mark:
                    signs.Marks.Add(new Mark(bars + (holds ? 1 : 0), mark, voice.Container));
                    break;
            }
        }
        Close();
    }

    /// <summary>Whether a music item takes time in its bar — everything but the marks, the
    /// declarations and the lines that stand at a moment.</summary>
    internal static bool TakesTime(SyntaxNode item) => item is not (
        BarlineSyntax or BreakSyntax or TieSyntax or SlurSyntax or BeamMarkerSyntax
        or DynamicSyntax or KeySignatureSyntax or TimeSignatureSyntax
        or TempoDeclarationSyntax or ClefDeclarationSyntax or PartialDeclarationSyntax
        or MusicMarkSyntax or NavigationMarkSyntax or GraceExpressionSyntax
        or StringNumberAnnotationSyntax or ArticulationSyntax
        or OctaveDirectiveSyntax or MetadataDeclarationSyntax
        or SectionDeclarationSyntax or FormDeclarationSyntax
        or PartDeclarationSyntax or RenderDeclarationSyntax);
}
