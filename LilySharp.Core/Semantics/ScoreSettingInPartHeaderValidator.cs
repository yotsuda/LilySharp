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
/// <c>tempo</c> and <c>time</c> are SCORE-LEVEL: every part plays at one tempo and in one
/// meter, so they cannot be a single part's property. Written as a part-header attribute
/// (<c>part melody { tempo 120 … }</c>, <c>part bass { time 3/4 … }</c>) they are silently
/// treated as a global default (the collector keeps only the last across parts), which is
/// misleading — so this rejects them there. Their valid homes are the top level (the piece's
/// opening value) and a section header (a change that applies to every part). A tempo/time
/// change INSIDE the music stream (a part's inner section, a section-major cell) is a mid-piece
/// change and is left alone — only the header attribute is flagged.
/// </summary>
internal sealed class ScoreSettingInPartHeaderValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            // Only a header attribute — a DIRECT child of the part declaration. A tempo/time
            // nested in the part's inner section is music (a mid-piece change), not a header.
            if (node.Parent is not PartDeclarationSyntax)
                continue;
            string? kind = node switch
            {
                TempoDeclarationSyntax => "tempo",
                TimeSignatureSyntax => "time",
                _ => null,
            };
            if (kind == null)
                continue;
            _diagnostics.Error(node.Span, DiagnosticCodes.ScoreSettingInPartHeader,
                $"'{kind}' is shared by every part, so it can't be a part property — put it at "
                + $"the top level (the piece's opening {kind}) or in a section header (a {kind} "
                + "change that applies to every part).");
        }
    }
}
