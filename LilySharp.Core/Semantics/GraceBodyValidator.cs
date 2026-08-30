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
/// Reports everything written inside a <c>grace { }</c> body that does not reach the page
/// (LYS4020), at the span it was written at.
/// </summary>
/// <remarks>
/// ⚠️ THE FAMILY WAS SILENT, AND WIDER THAN ITS TICKET SAID. Until this existed the only
/// thing a grace body said when it dropped something was <see cref="DiagnosticCodes.
/// UnengravedRehearsalMark"/>, and only for <c>@mark</c>. MEASURED 2026-08-30 (session 298)
/// by rendering each spelling against a control and comparing the SVG with <c>data-pos</c>
/// masked: a chord, a rest or a tuplet in the body engraves NO GRACE AT ALL, and dots,
/// slurs, beams, ties, <c>@staccato</c>, <c>@text</c>, <c>@f</c>, <c>@finger</c>,
/// <c>@trill</c>, <c>@sustain</c>, <c>@rit</c> and <c>@cresc</c> are each dropped without a
/// word. LilyPond draws all of them.
/// <para>
/// ⚠️ IT DOES NOT RE-DECIDE THE NARROWING. What a grace body carries is stated once, in
/// <see cref="GraceBodySupport"/>, and the collector reads the same statement — a validator
/// that walked the body itself would be the second spelling HANDOFF §5.2.1② names, and it
/// would keep warning about the first thing the collector learns to engrave. The poison that
/// proves the link is in <c>GraceBodyValidatorTests</c>.
/// </para>
/// <para>
/// ⚠️ A WARNING, NOT AN ERROR. Every one of these spellings is valid LilyPond and every one
/// of them is what Lily# should eventually draw, so the report is "this is not drawn yet",
/// not "do not write this" — the direction <see cref="DiagnosticCodes.SpanCrossesCueBoundary"/>
/// argues about is the opposite one, and it turns on whether LilyPond can make the ink.
/// </para>
/// <para>
/// ⚠️ IT RUNS ON EVERY KEYSTROKE. <see cref="SemanticValidation.Run"/> is the LSP's
/// diagnostics pass, so this walk is paid by every book, and the books that write no
/// <c>grace</c> at all are nearly all of them (1697 on disk, a handful write one). That is
/// why it goes through <see cref="SyntaxNode.KindSites"/> rather than
/// <c>DescendantNodes().OfType&lt;T&gt;()</c>: the same pre-order over GREEN nodes, with a
/// red materialized only per match, so a book with no grace pays the walk and allocates
/// nothing.
/// </para>
/// </remarks>
internal sealed class GraceBodyValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        foreach (var grace in tree.GetRoot()
                     .KindSites(SyntaxKind.GraceExpression).OfType<GraceExpressionSyntax>())
        {
            // Asked ONCE per body, not once per drop: the sentence it adds is about the
            // group, and repeating it on every element of `grace { <c e> r8 }` would say
            // "no grace note is drawn at all" twice about one silence.
            bool engravesNothing = GraceBodySupport.EngravesNothing(grace);

            foreach (var drop in GraceBodySupport.Drops(grace))
            {
                // ASCII punctuation only: these strings reach legacy-codepage consoles
                // through the CLI.
                string message = drop.Kind switch
                {
                    GraceDropKind.Element =>
                        $"{drop.Written} inside 'grace {{ }}' is not engraved: a grace body "
                        + "carries bare notes only"
                        + (engravesNothing
                            ? ", and this body holds no bare note, so NO grace note is drawn at all"
                            : ""),
                    GraceDropKind.Span =>
                        $"{drop.Written} inside 'grace {{ }}' is not engraved: a grace note "
                        + "carries no slur, beam or tie",
                    _ =>
                        $"{drop.Written} on a grace note is not engraved: a grace note is not "
                        + "a measure item, so there is no column for it to hang off. A "
                        + "rehearsal mark is the one that still reaches the page - its grob "
                        + "belongs to the bar rather than to the note",
                };
                _diagnostics.Warning(drop.Span, DiagnosticCodes.UnengravedGraceContent, message);
            }
        }
    }
}
