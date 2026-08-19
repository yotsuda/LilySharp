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
/// Validates that note/rest/chord durations use valid note values.
/// </summary>
internal sealed class DurationValidator : ISemanticValidator
{
    private static readonly HashSet<int> ValidDurations = [0, 1, 2, 4, 8, 16, 32, 64, 128];

    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    /// <summary>
    /// Validates all durations in a syntax tree.
    /// </summary>
    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        // root is a CompilationUnit — it never matches a duration-bearing case,
        // so it is not checked separately (the old CheckNode(root) was a no-op).
        foreach (var node in root.DescendantNodes())
            CheckNode(node);
    }

    // Token nodes fall to the default case (no duration), so walking them via
    // DescendantNodes() instead of hand-rolled recursion is behavior-preserving.
    private void CheckNode(SyntaxNode node)
    {
        DurationSyntax? duration = node switch
        {
            NoteSyntax note => note.Duration,
            DrumNoteSyntax drum => drum.Duration,
            RestSyntax rest => rest.Duration,
            ChordSyntax chord => chord.Duration,
            ChordRepetitionSyntax rep => rep.Duration,
            SlashNoteSyntax slash => slash.Duration,
            BareDurationSyntax bare => bare.Duration,
            _ => null
        };

        if (duration != null && !ValidDurations.Contains(duration.Value))
        {
            _diagnostics.Error(
                duration.NumberToken.Span,
                DiagnosticCodes.InvalidDuration,
                $"Invalid duration '{duration.Value}'. Valid values are 0 (breve), 1, 2, 4, 8, 16, 32, 64, 128.");
        }
    }
}
