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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

public static partial class NoteStepper
{
    /// <summary>
    /// The step keys on a length of a fenced text's <c>indent</c> (owner's request 2026-11-13:
    /// "数字を入力した後は、その数字を ctrl+shift+up/down で動かせるようにして"): Up is more white,
    /// Down less, by <see cref="LengthSteps"/>; the unit stays, and the number is written as a
    /// <c>size</c>'s is (<c>20mm</c> → <c>21mm</c>, <c>2.5cm</c> → <c>2.6cm</c>).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>The caret (or a selection) on a length, touching its end included, steps that
    /// one — with one length written, both sides. On the word <c>indent</c> it steps the first
    /// length, as the word <c>tempo</c> steps its number and <c>size</c> its own.</item>
    /// <item>Never under 0, and never into a width the validator refuses (LYS4057,
    /// <see cref="MarkdownTextValidator.LeavesTooLittle"/> on the paper the fence stands
    /// under): the large step stops at the last small step that fits, and then says so.</item>
    /// <item>⚠️ The fence is ONE token: only the opening line's words are read, so a number in
    /// the text between the fences is prose and the keys do there what they did (null here);
    /// the edit replaces the length's characters alone.</item>
    /// <item>A section's fence takes printings, not a shape: nothing steps there.</item>
    /// </list>
    /// Null when the selection is on none of these: the step goes on as before.
    /// </remarks>
    private static StepResult? FenceIndentStep(SyntaxTree tree, int start, int end, int dir, bool small)
    {
        foreach (var fence in tree.GetNodes<MarkdownTextSyntax>())
        {
            int at = fence.Fence.Span.Start;
            if (start <= at || end > fence.Fence.Span.End)
                continue;
            var value = fence.Value;
            if (!value.IsMarkdown || fence.Parent is SectionDeclarationSyntax)
                return null;
            bool On(int word) => at + value.Words[word].Offset <= start
                && end <= at + value.Words[word].Offset + value.Words[word].Text.Length;

            for (int i = 1; i < value.Words.Count; i++)
            {
                if (value.Words[i].Text != FencedText.IndentWord)
                    continue;
                // Its lengths: the one or two words after it that begin as a number does.
                var lengths = new List<int>();
                for (int j = i + 1; j < value.Words.Count && lengths.Count < 2 && BeginsALength(value.Words[j].Text); j++)
                    lengths.Add(j);
                int target = On(i) ? (lengths.Count > 0 ? lengths[0] : 0) : lengths.FirstOrDefault(On, -1);
                if (target < 0)
                    continue;
                if (target == 0)
                    return IndentSays("indent: write its length first (indent 20mm)");
                return FenceIndentEdit(tree, fence, lengths, target, dir, small);
            }
            return null;
        }
        return null;

        static bool BeginsALength(string word) => char.IsAsciiDigit(word[0]) || word[0] is '-' or '+' or '.';
    }

    private static StepResult IndentSays(string message) => new(false, [], message, [], 0);

    /// <summary>One step of the length <paramref name="target"/> (a word of the fence's opening
    /// line) among <c>indent</c>'s <paramref name="lengths"/>.</summary>
    private static StepResult FenceIndentEdit(SyntaxTree tree, MarkdownTextSyntax fence, List<int> lengths,
        int target, int dir, bool small)
    {
        var value = fence.Value;
        var (word, offset) = value.Words[target];
        if (!TextLength.TryRead(word, out var now, out string why))
            return IndentSays(why);
        string Spelled(double v) => SpelledLength(v, now.Unit);
        string Mm(double staffSpaces, Svg.Layout.LayoutOptions paper)
            => (staffSpaces * paper.StaffSpaceMm).ToString("0.#", CultureInfo.InvariantCulture);

        // The shape with this length at `to`: one length is both sides, two are left and right.
        TextShape With(double to)
        {
            var stepped = new TextLength(to, now.Unit);
            var other = stepped;
            if (lengths.Count == 2)
                TextLength.TryRead(value.Words[lengths[target == lengths[0] ? 1 : 0]].Text, out other, out _);
            return target == lengths[0]
                ? new TextShape(stepped, other, value.Shape.Center, value.Shape.Box)
                : new TextShape(other, stepped, value.Shape.Center, value.Shape.Box);
        }

        // Never under 0, and Up never into a width the validator refuses (SteppedLength).
        double? stepped = SteppedLength(now.Value, LengthSteps(now.Unit), dir, small, floor: 0,
            dir > 0 ? to => MarkdownTextValidator.LeavesTooLittle(tree.GetRoot(), fence, With(to), out _, out _) : null);
        if (stepped is not { } next)
        {
            if (dir < 0)
                return IndentSays($"indent {word}: already the least (0)");
            MarkdownTextValidator.LeavesTooLittle(tree.GetRoot(), fence, With(now.Value), out _, out var on);
            return IndentSays($"indent {word}: already the most (a text keeps at least "
                + $"{Mm(Svg.Layout.ProseBand.MinimumWidth, on)} mm of the line's {Mm(on.ContentWidth, on)} mm)");
        }

        MarkdownTextValidator.LeavesTooLittle(tree.GetRoot(), fence, With(next), out double width, out var paper);
        string written = string.Join(" ", lengths.Select(l => l == target ? Spelled(next) : value.Words[l].Text));
        string side = lengths.Count == 1 ? "both sides" : target == lengths[0] ? "the left" : "the right";
        int from = fence.Fence.Span.Start + offset;
        return new StepResult(false, [new Edit(from, from + word.Length, Spelled(next))],
            $"indent {written}: {side}, the text {Mm(Math.Max(0, width), paper)} mm wide", [], 0);
    }
}
