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
/// "fallback swallows it" shape (HANDOFF §7.7), and it is also what made RETIRING a
/// display unsafe: `as both` would have kept parsing and quietly become `as names`.
/// So the silence had to close before <c>both</c> could go, and the message names the
/// replacement for it rather than leaving the writer to guess.
/// </remarks>
internal sealed class ChordDisplayModeValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        foreach (var row in tree.GetRoot().DescendantNodes().OfType<ChordRowRenderSyntax>())
        {
            if (row.DisplayModeToken is not { } token || token.Text.Length == 0)
                continue;
            string text = token.Text;
            if (text is "roman" or "names")
                continue;

            // `both` retired 2026-08-23 (user decision): it stacked the degree above the
            // name as ONE symbol, and placing the track twice says the same thing with the
            // rows the writer can see and order. The two are not identical — a symbol with
            // no degree (an `r` slot's N.C.) prints once under `both` and once per row when
            // stacked — which is why the retirement message says what to write instead
            // rather than pretending the spellings were interchangeable.
            string hint = text == "both"
                ? " 'both' was removed: place the track twice instead — 'chords "
                    + row.PartName + " as roman' above 'chords " + row.PartName + " as names'."
                : "";
            _diagnostics.Error(token.Span, DiagnosticCodes.UnknownChordDisplayMode,
                $"'{text}' is not a chord display. Write 'as roman' or 'as names' "
                + "(omit 'as' for names)." + hint);
        }
    }
}
