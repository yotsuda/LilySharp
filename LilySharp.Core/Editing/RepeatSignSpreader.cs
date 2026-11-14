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
/// Writes a section's repeat signs in EVERY part that writes the section — or takes them back
/// to one — as edits for an editor's command (owner's design 2026-11-14,
/// docs/inline-repeat-design.md §8). A sign written in one part is the whole score's, so the
/// other parts need none; but a part read on its own then shows no repeat, and the command
/// makes the text say what the page draws.
/// </summary>
/// <remarks>
/// <para>
/// A sign goes where the other voice writes a plain <c>|</c> on the same bar line
/// (<see cref="VoiceBarLines"/>, the count LYS1070 compares signs by), and a <c>|:</c> that
/// opens the section is written at the head of a voice that opens with music. Where a voice
/// has no bar line of its own there — the bar is inside a phrase it plays or a <c>R1*4</c>, or
/// the voice is shorter — or writes another typed line (<c>||</c>, <c>|.</c>), nothing is
/// written and the place is named in <see cref="Result.Skipped"/>. Bar lines whose signs
/// DISAGREE between voices (LYS1070) are left as they are: which one is meant is the writer's.
/// </para>
/// <para>
/// Bar lines only: a volta ending and a navigation mark are not spread (yet).
/// </para>
/// </remarks>
public static class RepeatSignSpreader
{
    /// <summary>Put <paramref name="NewText"/> in place of <c>[Start, End)</c>.</summary>
    public readonly record struct Edit(int Start, int End, string NewText);

    /// <summary>The edits, how many signs they write (or take away), and the places that could
    /// not be written, each as a sentence.</summary>
    public sealed record Result(IReadOnlyList<Edit> Edits, int Count, IReadOnlyList<string> Skipped);

    /// <summary>Every repeat sign, written in every voice of its section that can take it.</summary>
    public static Result WriteInEveryPart(SyntaxTree tree) => Run(tree, spread: true);

    /// <summary>Every repeat sign, kept in the first voice that writes it and taken out of the
    /// others (a plain <c>|</c> in its place; a <c>|:</c> that opens the voice is deleted).</summary>
    public static Result KeepInOnePart(SyntaxTree tree) => Run(tree, spread: false);

    private static Result Run(SyntaxTree tree, bool spread)
    {
        var root = tree.GetRoot();
        string text = root.ToFullString();
        var phrases = SectionBarCounts.PhraseBodies(root);
        var voices = SectionBarCounts.SemanticVoices(root, phrases);
        var lines = voices.Select(v => VoiceBarLines.Of(v, phrases)).ToList();
        var edits = new List<Edit>();
        var skipped = new List<string>();
        int count = 0;

        foreach (var section in voices.Select(v => v.SectionName).Distinct())
        {
            var members = Enumerable.Range(0, voices.Count).Where(i => voices[i].SectionName == section).ToList();
            if (members.Count < 2)
                continue;
            // The signs of the section, by the bars before them: who writes which.
            var places = new SortedDictionary<int, List<(int Voice, VoiceBarLines.Line Line)>>();
            foreach (int i in members)
                foreach (var line in lines[i])
                    if (SyntaxFacts.IsRepeatBarlineKind(line.Node.BarToken.Kind))
                    {
                        if (!places.TryGetValue(line.Bars, out var at))
                            places[line.Bars] = at = [];
                        at.Add((i, line));
                    }

            foreach (var (bars, signs) in places)
            {
                string sign = Text(signs[0].Line.Node);
                if (signs.Any(s => Text(s.Line.Node) != sign))
                    continue; // LYS1070: the writer says which
                if (!spread)
                {
                    foreach (var (voice, line) in signs.Skip(1))
                    {
                        if (voice == signs[0].Voice)
                            continue;
                        edits.Add(TakenOut(line, lines[voice], text));
                        count++;
                    }
                    continue;
                }
                foreach (int i in members)
                {
                    if (signs.Any(s => s.Voice == i))
                        continue;
                    var own = lines[i].Where(l => l.Bars == bars).ToList();
                    if (own.Count > 0 && own.All(l => l.Node.BarToken.Kind == SyntaxKind.Bar))
                    {
                        edits.Add(new Edit(own[0].Node.Span.Start, own[0].End, sign));
                        count++;
                    }
                    else if (own.Count == 0 && bars == 0 && sign == "|:" && HeadOf(voices[i], lines[i], text) is int head)
                    {
                        edits.Add(new Edit(head, head, "|: "));
                        count++;
                    }
                    else
                        skipped.Add(own.Count > 0
                            ? $"{voices[i].Label}, section '{section}', after bar {bars}: it writes '{Text(own[0].Node)}' there"
                            : $"{voices[i].Label}, section '{section}', after bar {bars}: it writes no bar line of its own there");
                }
            }
        }
        edits.Sort((a, b) => a.Start.CompareTo(b.Start));
        return new Result(edits, count, skipped);
    }

    private static string Text(BarlineSyntax line)
        => line.BarToken.Text + (line.HasExplicitRepeatCount ? "*" + line.RepeatCount : "");

    /// <summary>A sign taken out: a plain <c>|</c> in its place — but a <c>|:</c> that closes
    /// nothing (it opens the voice) goes with the space after it, since a <c>|</c> there would
    /// be an empty bar.</summary>
    private static Edit TakenOut(VoiceBarLines.Line line, List<VoiceBarLines.Line> all, string text)
    {
        bool opensTheVoice = line.Bars == 0 && line.Node.BarToken.Kind == SyntaxKind.RepeatStartBar
            && ReferenceEquals(all[0].Node, line.Node);
        if (!opensTheVoice)
            return new Edit(line.Node.Span.Start, line.End, "|");
        int end = line.End;
        while (end < text.Length && text[end] is ' ' or '\t')
            end++;
        return new Edit(line.Node.Span.Start, end, "");
    }

    /// <summary>Where a voice that opens with music begins — after its brace — or null when it
    /// opens with a bar line (an empty bar the sign would not stand before).</summary>
    private static int? HeadOf(SectionBarCounts.SemanticVoice voice, List<VoiceBarLines.Line> lines, string text)
    {
        if (lines.Count > 0 && lines[0].Bars == 0)
            return null;
        int open = text.IndexOf('{', voice.Container.Span.Start);
        if (open < 0 || open >= voice.Container.Span.Start + voice.Container.Span.Length)
            return null;
        int at = open + 1;
        while (at < text.Length && char.IsWhiteSpace(text[at]))
            at++;
        return at;
    }
}