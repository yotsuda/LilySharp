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

using LilySharp.Core.Music;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Reports a <c>q</c> chord repetition that has no chord before it in its
/// top-level body (LYS4015): there is nothing to repeat, so the collector
/// renders it as a spacer of its written duration — silently, which is why
/// this validator says so.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/music-functions.scm:940-942 expand-repeat-chords! — a
/// repetition with no last-chord warns "Bad chord repetition" and stays an
/// empty (note-less) chord.
/// </remarks>
internal sealed class ChordRepetitionValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            if (node is ChordRepetitionSyntax rep && ChordRepetitions.OriginalOf(rep) is null)
                _diagnostics.Warning(
                    rep.Span,
                    DiagnosticCodes.BadChordRepetition,
                    "Bad chord repetition - no chord before this 'q' to repeat; "
                    + "it occupies its duration but plays nothing.");
        }
    }
}
