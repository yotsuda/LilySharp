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
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

public static partial class NoteStepper
{
    /// <summary>A length written in a <c>paper { }</c> block: its key as the message says it
    /// (<c>leftMargin</c>, <c>systemSystemSpacing padding</c>), the key's own word, and its tokens.</summary>
    private readonly record struct PaperLength(string Says, string Key, SyntaxTokenNode KeyToken,
        SyntaxTokenNode? Minus, SyntaxTokenNode? Number, SyntaxTokenNode? Unit);

    /// <summary><c>staffSpace</c> is a staff space, not a margin: it steps a tenth of what the
    /// other lengths step (0.1 mm, and 0.5 mm on the page pair), and is written a place finer.
    /// LILYSHARP-OWN: a millimetre a press would double the staff.</summary>
    private const double StaffSpaceStepFraction = 0.1;

    /// <summary>
    /// The step keys on a length of a <c>paper { }</c> block (owner's request 2026-11-13:
    /// "paper { margin xx } も動くようにして"): the page's lengths, the margins, the indents,
    /// <c>spacingIncrement</c>, <c>staffSpace</c>, and a spacing block's <c>basicDistance</c>,
    /// <c>minimumDistance</c> and <c>padding</c> — Up is more, Down less, by the steps a fenced
    /// text's <c>indent</c> takes (<see cref="LengthSteps"/>); the unit stays, and a bare number
    /// is staff spaces (PaperPlanReader.TryReadLength).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>The caret (or a selection) on the length, touching its end included, or on its key:
    /// an entry has one length. On a spacing block's key its one length steps; with several the
    /// keys say to put the caret on one.</item>
    /// <item>Never under 0 — a length written negative (a spacing block's <c>padding -1</c>) is
    /// stepped where it is — and never into a book the validators refuse where they accepted this
    /// one (<see cref="PaperTrouble"/>): the large step stops at the last small step that reads,
    /// and then says so.</item>
    /// <item>Wherever the block stands: the file's, a named one, a score's own.</item>
    /// <item>NOT here: <c>size</c> (it walks the pages, <see cref="PaperSizeAt"/>), and the numbers
    /// that are no lengths — the counts, <c>shortestDurationSpace</c>, <c>stretchability</c>.</item>
    /// </list>
    /// Null when the selection is on none of these: the step goes on as before.
    /// </remarks>
    private static StepResult? PaperLengthStep(string text, SyntaxTree tree, int start, int end, int dir, bool small)
    {
        bool On(SyntaxTokenNode from, SyntaxTokenNode to) => from.Span.Start <= start && end <= to.Span.End;
        bool OnValue(PaperLength l) => l.Number != null && On(l.Minus ?? l.Number, l.Unit ?? l.Number);

        foreach (var paper in tree.GetNodes<PaperDeclarationSyntax>())
        {
            if (start < paper.Span.Start || end > paper.Span.End)
                continue;
            foreach (var entry in paper.Entries)
            {
                if (PaperPlanReader.IsLengthKey(entry.Key) && !entry.HasBlock)
                {
                    var length = new PaperLength(entry.Key, entry.Key, entry.KeyToken, entry.MinusToken, entry.NumberToken, entry.UnitToken);
                    if (On(entry.KeyToken, entry.KeyToken) || OnValue(length))
                        return PaperLengthEdit(text, tree, paper, length, dir, small);
                    continue;
                }
                if (!PaperPlanReader.IsSpacingKey(entry.Key) || !entry.HasBlock)
                    continue;
                var lengths = entry.SubEntries.Where(s => PaperPlanReader.IsLengthSubKey(s.Key))
                    .Select(s => new PaperLength($"{entry.Key} {s.Key}", s.Key, s.KeyToken, s.MinusToken, s.NumberToken, s.UnitToken))
                    .ToList();
                foreach (var length in lengths)
                    if (On(length.KeyToken, length.KeyToken) || OnValue(length))
                        return PaperLengthEdit(text, tree, paper, length, dir, small);
                if (!On(entry.KeyToken, entry.KeyToken))
                    continue;
                // On the block's key: its one length, as the word `tempo` steps its number.
                var written = lengths.Where(l => l.Number != null).ToList();
                return written.Count switch
                {
                    1 => PaperLengthEdit(text, tree, paper, written[0], dir, small),
                    0 => IndentSays($"{entry.Key}: write a length in it first ({entry.Key} {{ basicDistance 12 }})"),
                    _ => IndentSays($"{entry.Key}: {written.Count} lengths - put the caret on the one to step"),
                };
            }
            return null;
        }
        return null;
    }

    /// <summary>One step of <paramref name="length"/>, a length of <paramref name="paper"/>.</summary>
    private static StepResult PaperLengthEdit(string text, SyntaxTree tree, PaperDeclarationSyntax paper,
        PaperLength length, int dir, bool small)
    {
        bool staffSpace = length.Key == PaperPlanReader.StaffSpaceKey;
        if (length.Number is not { } number)
            return IndentSays($"{length.Says}: write its length first ({length.Key} {(staffSpace ? "1.5mm" : "15mm")})");
        string? unit = length.Unit?.Text;
        string was = (length.Minus != null ? "-" : "") + number.Text + unit;
        if (unit != null && !FencedText.LengthUnits.Contains(unit))
            return IndentSays($"{length.Says} {was}: '{unit}' is not a unit of this language (mm, cm, in)");
        if (staffSpace && unit == null)
            return IndentSays($"{length.Says} {was}: write its unit (staffSpace 1.5mm)");
        if (!double.TryParse(number.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double now))
            return IndentSays($"{length.Says} {was}: write a number");
        if (length.Minus != null)
            now = -now;

        var steps = LengthSteps(unit);
        int places = LengthPlaces;
        if (staffSpace)
        {
            steps = (steps.Small * StaffSpaceStepFraction, steps.Large * StaffSpaceStepFraction);
            places++;
        }
        // The sign is a token of its own, before the number; the unit stays where it is.
        int from = (length.Minus ?? number).Span.Start, to = number.Span.End;
        string Spelled(double v) => SpelledLength(v, null, places);

        var before = PaperState(tree, paper);
        var after = before;
        string? why = null;
        double? stepped = SteppedLength(now, steps, dir, small, floor: now < 0 ? double.NegativeInfinity : 0, tried =>
        {
            var state = PaperState(SyntaxTree.Parse(text[..from] + Spelled(tried) + text[to..]), paper);
            if (PaperTrouble(before, state) is { } trouble)
            {
                why = trouble;
                return true;
            }
            after = state;
            return false;
        }, places);
        if (stepped is not { } next)
            return IndentSays($"{length.Says} {was}: already the {(dir > 0 ? "most" : "least")} ({why ?? "0"})");

        string Mm(double staffSpaces) => (staffSpaces * after.Paper.StaffSpaceMm).ToString("0.#", CultureInfo.InvariantCulture);
        string said = $"{length.Says} {Spelled(next)}{unit}" + length.Key switch
        {
            "paperWidth" or "leftMargin" or "rightMargin" => $": the line {Mm(after.Paper.ContentWidth)} mm wide",
            PaperPlanReader.StaffSpaceKey => $": a staff {Mm(LayoutOptions.Default.StaffHeight)} mm high",
            _ => "",
        };
        return new StepResult(false, [new Edit(from, to, Spelled(next))], said, [], 0);
    }

    /// <summary>What a book's paper is to the step: the sentences of the errors the paper's and the
    /// fenced texts' validators raise, and the page the stepped block gives where it stands.</summary>
    private readonly record struct PaperStanding(IReadOnlyList<string> Errors, LayoutOptions Paper);

    /// <summary>The <see cref="PaperStanding"/> of <paramref name="tree"/> — the book as it is, or as
    /// a step would write it — for the block that starts where <paramref name="paper"/> does (a step
    /// edits inside the block, so its start stays).</summary>
    private static PaperStanding PaperState(SyntaxTree tree, PaperDeclarationSyntax paper)
    {
        var errors = new List<string>();
        foreach (var validator in new ISemanticValidator[] { new PaperValidator(), new MarkdownTextValidator() })
        {
            validator.Validate(tree);
            errors.AddRange(validator.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message));
        }
        var block = tree.GetNodes<PaperDeclarationSyntax>().FirstOrDefault(p => p.Span.Start == paper.Span.Start);
        return new PaperStanding(errors,
            block != null ? PaperPlanReader.WhereItStands(tree.GetRoot(), block) : LayoutOptions.Default);
    }

    /// <summary>
    /// Why a step from <paramref name="before"/> to <paramref name="after"/> is not taken, or null:
    /// the validators refuse the stepped book where they accepted this one (a <c>staffSpace</c> of
    /// 0; margins under which a fenced text's indents leave it less than its least width, LYS4057),
    /// or the page is left no line or no height at all.
    /// LILYSHARP-OWN: the second — no diagnostic refuses margins wider than their paper, and the
    /// keys stop before them all the same.
    /// </summary>
    private static string? PaperTrouble(PaperStanding before, PaperStanding after)
    {
        if (after.Errors.Count > before.Errors.Count)
        {
            string raised = after.Errors.FirstOrDefault(e => !before.Errors.Contains(e)) ?? after.Errors[^1];
            // The sentence that says what is wrong, without the advice after it.
            int stop = raised.IndexOf(". ", StringComparison.Ordinal);
            return (stop < 0 ? raised.TrimEnd('.') : raised[..stop]).Replace("These indents leave", "a text's indents would leave");
        }
        static double Height(LayoutOptions p) => p.PageHeight - p.MarginTop - p.MarginBottom;
        if (after.Paper.ContentWidth <= 0 && after.Paper.ContentWidth < before.Paper.ContentWidth)
            return "the margins would leave the page no line";
        if (after.Paper.PageHeight > 0 && Height(after.Paper) <= 0 && Height(after.Paper) < Height(before.Paper))
            return "the margins would leave the page no height";
        return null;
    }
}
