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
/// Reports a <c>break</c> / <c>pageBreak</c> written inside a bar that the page could not
/// break the bar at (LYS1037): the break fell to the next bar line, and the reader should
/// know why — a note sounding across it in some voice, a beam or tuplet or percent repeat
/// running across it, an unmetered bar, a second break in the same bar.
/// </summary>
/// <remarks>
/// Like <see cref="RepeatPairingValidator"/> and <see cref="ExpansionBudgetValidator"/> this
/// reads back what the shared collector decided rather than re-deciding it: only the collect
/// knows every voice's items at the break's moment (<see cref="Svg.Collector.MidBarBreakTable.Build"/>),
/// and the page and this report must give one answer.
/// </remarks>
internal sealed class MidBarBreakValidator : ISharedCollectValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree) =>
        ValidateWith(tree, new System.Lazy<Svg.Collector.MeasureCollector?>(
            () => SemanticValidation.TryCollect(tree)));

    public void ValidateWith(SyntaxTree tree, System.Lazy<Svg.Collector.MeasureCollector?> sharedCollect)
    {
        // A malformed score (null collector) surfaces its real error elsewhere.
        var conflicts = sharedCollect.Value?.MidBarBreakConflicts;
        if (conflicts == null || conflicts.Count == 0)
            return;

        var seen = new HashSet<int>();
        foreach (var c in conflicts)
        {
            if (c.SourcePosition < 0 || !seen.Add(c.SourcePosition))
                continue;
            // ASCII punctuation only: these strings reach legacy-codepage consoles via the CLI.
            _diagnostics.Warning(new TextSpan(c.SourcePosition, 5),
                DiagnosticCodes.MidBarBreakNotSplit,
                $"this break stands inside a bar, but the bar cannot be broken there: {c.Reason}. "
                + "The line breaks at the next bar line instead (the .ly twin breaks where written). "
                + "Tie the note across the break, or move the break to a beat no beam or tuplet crosses.");
        }
    }
}
