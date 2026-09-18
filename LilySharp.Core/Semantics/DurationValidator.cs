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
        foreach (var node in root.DescendantNodesOfKinds(DurationBearingKinds))
            CheckNode(node);
    }

    /// <summary>The kinds <see cref="CheckNode"/> finds a duration on, so the walk asks the
    /// tree's descendant index for those nodes instead of offering it every node of the
    /// book — a thousand-bar book is 3% duration-bearing nodes and 97% everything else
    /// (MEASURED, session 409), and this pass runs after every settled keystroke.</summary>
    /// <remarks>
    /// ⚠️ A SECOND SPELLING OF THE SWITCH BELOW, kept beside it on purpose (the shape
    /// <see cref="Editing.PartReferenceFinder.ReferenceKinds"/> has): an eighth
    /// duration-bearing spelling must be added to BOTH, or a bad duration on it is accepted
    /// in silence. Pinned by <c>TailValidatorKindsTests</c> over every node of the net books.
    /// </remarks>
    internal static readonly SyntaxKind[] DurationBearingKinds =
    [
        SyntaxKind.Note, SyntaxKind.DrumNote, SyntaxKind.Rest, SyntaxKind.Chord,
        SyntaxKind.ChordRepetition, SyntaxKind.SlashNote, SyntaxKind.BareDuration,
    ];

    /// <summary>The duration this node carries, or null — the half of
    /// <see cref="CheckNode"/> that <see cref="DurationBearingKinds"/> is the kind list of.
    /// </summary>
    internal static DurationSyntax? DurationOf(SyntaxNode node) => node switch
    {
        NoteSyntax note => note.Duration,
        DrumNoteSyntax drum => drum.Duration,
        RestSyntax rest => rest.Duration,
        ChordSyntax chord => chord.Duration,
        ChordRepetitionSyntax rep => rep.Duration,
        SlashNoteSyntax slash => slash.Duration,
        BareDurationSyntax bare => bare.Duration,
        _ => null,
    };
    private void CheckNode(SyntaxNode node)
    {
        DurationSyntax? duration = DurationOf(node);

        if (duration != null && !ValidDurations.Contains(duration.Value))
        {
            _diagnostics.Error(
                duration.NumberToken.Span,
                DiagnosticCodes.InvalidDuration,
                $"Invalid duration '{duration.Value}'. Valid values are 0 (breve), 1, 2, 4, 8, 16, 32, 64, 128.");
        }
    }
}
