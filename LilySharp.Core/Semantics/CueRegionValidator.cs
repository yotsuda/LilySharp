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
/// Rejects the two shapes a <c>cue { … }</c> region is deliberately not opened for: a cue
/// nested in a cue, and a <c>voice { … } voice { … }</c> span inside one.
/// </summary>
/// <remarks>
/// Both are closed on purpose rather than because LilyPond cannot express them — it can.
/// A nested cue says nothing the outer one has not (LilyPond's cue is a context with ONE
/// <c>fontSize</c>), and a voice span inside a cue doubles a meaning the cue already
/// carries, with nothing in Lily# to decide which of the two owns the stem forcing.
/// <para>
/// ⚠️ The direction of the decision is the point: Lily# is pre-release, so OPENING one of
/// these later costs a grammar note and a test, while un-opening it after books exist costs
/// a migration. See docs/cue-context-design.md §4 and §8.
/// </para>
/// </remarks>
internal sealed class CueRegionValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        foreach (var cue in tree.GetRoot().DescendantNodes().OfType<CueExpressionSyntax>())
        {
            // Report at the INNER keyword: it is the one to delete, and squiggling the whole
            // outer region would bury it under bars of music.
            if (cue.IsInside<CueExpressionSyntax>())
            {
                _diagnostics.Error(cue.CueKeyword.Span, DiagnosticCodes.NestedCueBlock,
                    "a 'cue { … }' inside another one says nothing the outer region has not - "
                    + "a cue is a context with one size, so nesting cannot make it smaller "
                    + "again. Remove the inner 'cue'.");
                continue;
            }

            foreach (var span in cue.Body.DescendantNodes().OfType<ParallelExpressionSyntax>())
            {
                if (span.OpenAngle.Kind != SyntaxKind.VoiceKeyword)
                    continue;
                _diagnostics.Error(span.OpenAngle.Span, DiagnosticCodes.VoiceInsideCue,
                    "a 'voice { … } voice { … }' span inside a 'cue { … }' is not supported: "
                    + "a cue is already a voice of its own, and nothing decides which of the "
                    + "two the stem forcing belongs to. Put the cue inside one of the voices "
                    + "instead (voice { cue { … } } voice { … }).");
            }
        }
    }
}
