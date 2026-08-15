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
/// Warns when the item after a tie (<c>~</c>) cannot receive it: a note (or chord)
/// repeating none of the tied pitches, or an audible rest. A tie joins two notes of
/// the SAME pitch — a mismatch is almost always an authoring slip (a slur was meant,
/// or the target note was mistyped), and nothing sensible gets tied.
/// </summary>
/// <remarks>
/// Resolving each tie's destination across barlines and voices is exactly what the
/// collector's render path already does, so — like <see cref="TabTieStringValidator"/> —
/// this validator runs the shared collector and reads back the mismatches it recorded
/// as a side effect (<see cref="Svg.Collector.MeasureCollector.TieTargetWarnings"/>).
/// </remarks>
internal sealed class TieTargetValidator : ISharedCollectValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree) =>
        ValidateWith(new System.Lazy<Svg.Collector.MeasureCollector?>(
            () => SemanticValidation.TryCollect(tree)));

    public void ValidateWith(System.Lazy<Svg.Collector.MeasureCollector?> sharedCollect)
    {
        // A malformed score (null collector) surfaces its real error elsewhere.
        var warnings = sharedCollect.Value?.TieTargetWarnings;
        if (warnings == null)
            return;

        foreach (var w in warnings)
        {
            // ASCII punctuation only: these strings reach legacy-codepage consoles via the CLI.
            _diagnostics.Warning(new TextSpan(w.SourcePosition, 1),
                DiagnosticCodes.TieTargetMismatch,
                w.Problem switch
                {
                    Svg.Collector.TieTargetProblem.IntoRest =>
                        "a tie '~' runs into a rest, so nothing is tied; remove the tie or fill the gap",
                    // Nothing follows at all. The score does not gain a hanging tie, it loses
                    // the mark outright — and Lily# already spells the hanging tie, so the
                    // complaint can name what was probably meant instead of only refusing.
                    Svg.Collector.TieTargetProblem.NoTarget =>
                        "a tie '~' has nothing after it, so no tie is drawn; remove it, or write "
                        + "'@laissezVibrer' for a tie that hangs into silence ('@repeatTie' for one "
                        + "resuming from a repeat) - note that a tie does not carry into another voice",
                    _ =>
                        "the note after a tie '~' does not repeat the tied pitch; a tie joins two "
                        + "notes of the same pitch - use a slur '( )' to connect different pitches",
                });
        }
    }
}
