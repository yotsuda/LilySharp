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

using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Reports a bar line one part or chords track gives a <c>break</c> and another a
/// <c>noBreak</c> — or <c>pageBreak</c> against <c>noPageBreak</c> — (LYS4077): the break
/// wins, and the reader of the <c>noBreak</c> should know it did not hold.
/// </summary>
/// <remarks>
/// A break is the score's, taken from every part and chords track of the piece (owner's
/// decision 2026-11-13), so two places can now disagree about one bar line. Like
/// <see cref="MidBarBreakValidator"/> this reads back what the shared collect decided
/// (<c>MeasureCollector.BreakConflicts</c>): only the collect knows which bar of the played
/// form each word landed on, and the page and this report must give one answer.
/// </remarks>
internal sealed class BreakConflictValidator : ISharedCollectValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree) =>
        ValidateWith(tree, new System.Lazy<Svg.Collector.MeasureCollector?>(
            () => SemanticValidation.TryCollect(tree)));

    public void ValidateWith(SyntaxTree tree, System.Lazy<Svg.Collector.MeasureCollector?> sharedCollect)
    {
        // A malformed score (null collector) surfaces its real error elsewhere.
        var conflicts = sharedCollect.Value?.BreakConflicts;
        if (conflicts == null || conflicts.Count == 0)
            return;

        var seen = new HashSet<(int, string)>();
        foreach (var c in conflicts)
        {
            if (c.LoserPosition < 0 || !seen.Add((c.LoserPosition, c.LoserWord)))
                continue;
            string where = c.WinnerPosition >= 0
                ? $"{c.WinnerPlace} (line {LineOf(tree.Text, c.WinnerPosition)})"
                : c.WinnerPlace;
            // ASCII punctuation only: these strings reach legacy-codepage consoles via the CLI.
            _diagnostics.Warning(new TextSpan(c.LoserPosition, c.LoserWord.Length),
                DiagnosticCodes.BreakAgainstNoBreak,
                $"'{c.LoserWord}' in {c.LoserPlace} and '{c.WinnerWord}' in {where} stand at the same bar line: "
                + $"the {c.WinnerWord} wins. A break is the score's, read from every part and chords track - "
                + "take one of the two out.");
        }
    }

    /// <summary>The 1-based line <paramref name="position"/> stands on.</summary>
    private static int LineOf(string text, int position)
    {
        int line = 1;
        for (int i = 0; i < position && i < text.Length; i++)
            if (text[i] == '\n')
                line++;
        return line;
    }
}
