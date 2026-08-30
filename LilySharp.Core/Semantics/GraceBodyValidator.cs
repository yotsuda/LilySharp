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
        var root = tree.GetRoot();
        Dictionary<string, SyntaxNode>? phrases = null;
        int budget = Svg.Collector.MeasureCollector.DefaultExpansionBudgetCap;

        foreach (var grace in root
                     .KindSites(SyntaxKind.GraceExpression).OfType<GraceExpressionSyntax>())
        {
            // The phrase table is built ONCE, and only for a book that writes a grace at
            // all: the KindSites walk above costs nothing for the books that write none,
            // and hoisting this DescendantNodes pass out of the loop would undo that.
            phrases ??= PhraseBodies(root);

            // ⚠️ THE BODY IS EXPANDED THROUGH THE STATEMENT THE COLLECTOR READS. A phrase
            // reference is a container, so what it NAMES is what has to be judged — a
            // validator that stopped at the reference would fall silent about a `<c e>`
            // written one level down and, worse, would call a body that engraves two grace
            // notes "no grace note at all".
            var elements = GraceBodySupport.BodyElements(
                grace, name => phrases.GetValueOrDefault(name), () => budget-- > 0);

            // Asked ONCE per body, not once per drop: the sentence it adds is about the
            // group, and repeating it on every element of `grace { <c e> r8 }` would say
            // "no grace note is drawn at all" twice about one silence.
            bool engravesNothing = GraceBodySupport.EngravesNothing(elements);

            foreach (var drop in GraceBodySupport.Drops(elements))
            {
                // ASCII punctuation only: these strings reach legacy-codepage consoles
                // through the CLI.
                //
                // ⚠️ THE BODY IS SPELLED WITH THE PHRASE IN IT when the drop was reached
                // through one, because the SPAN then points inside the phrase's declaration
                // — a line with no `grace` on it. "a chord inside 'grace { C }'" is the one
                // sentence that lets the reader walk from the underlined chord back to the
                // grace that silenced it.
                string body = drop.ViaPhrase is { } phrase
                    ? $"'grace {{ {phrase} }}'" : "'grace { }'";
                string message = drop.Kind switch
                {
                    GraceDropKind.Element =>
                        $"{drop.Written} inside {body} is not engraved: a grace body "
                        + "carries bare notes only"
                        + (engravesNothing
                            ? ", and this body holds no bare note, so NO grace note is drawn at all"
                            : ""),
                    GraceDropKind.Span =>
                        $"{drop.Written} inside {body} is not engraved: a grace note "
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

    /// <summary>
    /// Every phrase / variable name in the book, mapped to the body that defines it — the
    /// table a reference written in a grace body is resolved against.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE COLLECTOR'S TABLE IS BUILT THE SAME WAY (<c>MeasureCollector.Definitions</c>:
    /// a phrase declaration contributes its <c>Body</c>, a variable declaration its
    /// <c>Expression</c>), and <see cref="PhraseCycleValidator"/> already builds this exact
    /// pair on the diagnostics side. The two tables must agree or the collector and this
    /// validator would disagree about which references are containers.
    /// <para>
    /// ⚠️ IT IS A GREEN WALK, not <c>DescendantNodes()</c>, for the reason the grace walk
    /// above is one: this runs on every keystroke, and materializing a red for every node in
    /// the book to find the handful of declarations would cost more than everything else
    /// this validator does put together.
    /// </para>
    /// </remarks>
    private static Dictionary<string, SyntaxNode> PhraseBodies(SyntaxNode root)
    {
        var bodies = new Dictionary<string, SyntaxNode>();
        foreach (var n in root.GreenSites(g => (
                     g.Kind is SyntaxKind.PhraseDeclaration or SyntaxKind.VariableDeclaration,
                     true)))
        {
            if (n is PhraseDeclarationSyntax phrase)
                bodies[phrase.Name.Text] = phrase.Body;
            else if (n is VariableDeclarationSyntax variable)
                bodies[variable.Name.Text] = variable.Expression;
        }
        return bodies;
    }
}
