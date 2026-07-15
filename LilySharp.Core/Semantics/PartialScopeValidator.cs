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
/// Flags a <c>partial</c> (pickup) written outside a section. A pickup shortens the opening
/// bar for EVERY part of a section at once, so — unlike an ongoing default such as tempo — it
/// is neither a piece-wide global nor a per-voice directive; it belongs to the section. The
/// canonical place is a section directive, whose immediate parent is the section node:
/// <list type="bullet">
/// <item>Section-major header: <c>section A { partial 4  melody {…} bass {…} }</c> — applies
/// to all part blocks of the section.</item>
/// <item>Single-voice section body: <c>section A { partial 2. c d f }</c> — the lone voice's
/// pickup.</item>
/// </list>
/// The top level, a <c>part {}</c> header, or a <c>partial</c> nested inside one part block /
/// voice are all errors (the first two aren't tied to a section; the last would apply to only
/// one part). A bare-music file (no <c>part</c> / <c>section</c> / <c>form</c>) is a plain note
/// stream, so a leading <c>partial</c> there is just that music's pickup and is fine.
/// </summary>
internal sealed class PartialScopeValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        // Bare music (no structural nodes) is a plain note stream — a leading `partial` there
        // is the music's own pickup. The section rule only bites once the file is structured.
        bool structured = root.DescendantNodes().Any(n =>
            n is PartDeclarationSyntax or SectionDeclarationSyntax or FormDeclarationSyntax);
        if (!structured)
            return;

        foreach (var partial in root.DescendantNodes().OfType<PartialDeclarationSyntax>())
        {
            // A section directive (section-major header OR single-voice section body) has the
            // section as its immediate parent; anywhere else is wrong.
            if (partial.Parent is SectionDeclarationSyntax)
                continue;

            string where = partial.Parent switch
            {
                PartDeclarationSyntax => "a part header",
                _ when partial.Parent == root => "the top level",
                _ => "a part/voice",
            };
            _diagnostics.Error(partial.Span, DiagnosticCodes.PartialOutsideSection,
                $"'partial' cannot go in {where} — a pickup shortens the opening bar for every "
                + "part of a section at once. Write it as a section directive: section A { partial 4  … }.");
        }
    }
}
