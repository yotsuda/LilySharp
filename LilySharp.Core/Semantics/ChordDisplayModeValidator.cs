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

using System.Linq;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// The word after <c>as</c> on a chord row names a DISPLAY: <c>roman</c> or <c>names</c>
/// (omitting the clause means names). Anything else is rejected here.
/// </summary>
/// <remarks>
/// <c>RenderSpecParser.ParseChordMode</c> ends in a <c>_ =&gt;</c> arm, so before this
/// validator existed an unrecognised word was read as <c>names</c> with nothing said —
/// `chords prog as romn` drew absolute names and reported no problem. That is the
/// "fallback swallows it" shape (HANDOFF §7.7). Every word that is not a display gets the
/// same message; a retired spelling has no message of its own (pre-release, no migration
/// hints — owner decision 2026-09-15).
/// </remarks>
internal sealed class ChordDisplayModeValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        foreach (var row in tree.GetRoot().DescendantNodes<ChordRowRenderSyntax>())
        {
            if (row.DisplayModeToken is not { } token || token.Text.Length == 0)
                continue;
            string text = token.Text;
            if (text is "roman" or "names")
                continue;

            _diagnostics.Error(token.Span, DiagnosticCodes.UnknownChordDisplayMode,
                $"'{text}' is not a chord display. Write 'as roman' or 'as names' "
                + "(omit 'as' for names).");
        }
    }
}
