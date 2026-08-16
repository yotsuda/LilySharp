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

using LilySharp.Core.Editing;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Validates the <c>form</c> / <c>score</c> binding: every <c>form</c> is named
/// (<c>form Main { ... }</c>), form names are unique (case-sensitive), every <c>form</c>
/// names at least one section (LYS6007), and every
/// <c>score</c> references an existing form by name (<c>score Main { ... }</c>).
/// A form is the piece's arrangement — the order sections play in, with repeats
/// and navigation. The reserved form name <c>main</c> writes to the input file's
/// stem; any other name becomes the output file name unless a <c>"basename"</c>
/// overrides it.
/// </summary>
internal sealed class FormDeclarationValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var forms = tree.GetNodes<FormDeclarationSyntax>().ToList();

        // Every form must be named; names are unique and case-sensitive.
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var form in forms)
        {
            string name = form.NameText;
            if (string.IsNullOrEmpty(name))
            {
                _diagnostics.Error(form.FormKeyword.Span, DiagnosticCodes.UnnamedForm,
                    "A 'form' must be named, e.g. 'form main { ... }'.");
                continue;
            }
            if (!declared.Add(name))
                _diagnostics.Error(form.Name!.Span, DiagnosticCodes.DuplicateFormName,
                    $"Duplicate form name '{name}'. Each form name must be unique.");

            // A form that names no section arranges nothing, and the page that comes out of
            // it is not blank — there is no page at all (LYS6007's remark has the bytes).
            // "Names a section" is asked of SectionReferenceFinder rather than re-listed
            // here: it already knows all three spellings, and this validator would be the
            // fourth place to keep in step.
            if (SectionReferenceFinder.AllSectionNameTokens(form).Count == 0)
                _diagnostics.Error(form.BodySpan ?? form.FormKeyword.Span,
                    DiagnosticCodes.EmptyForm,
                    $"Form '{name}' has nothing to arrange — it names no section. "
                    + "Add a section reference, e.g. 'form " + name + " { A }' "
                    + "('~A' plays it without printing a rehearsal label).");

            ReportEndingsNoRepeatOpens(form);
        }

        // Every score must reference a form that exists.
        ValidateScoreBindings(tree, declared);
    }

    /// <summary>
    /// LYS6008 — every volta ending in <paramref name="form"/> that no repeat block opened.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The predicate is the ENGRAVER's, one ancestor walk, and it is deliberately not "does
    /// this form contain a repeat block": that weaker rule misses <c>|: A :| B [1. B]</c>,
    /// whose ending is a child of the form and is dropped exactly like one in a form with no
    /// repeat at all. Asking the same question the collector and the MIDI exporter ask keeps
    /// the diagnostic and the behaviour from drifting apart — there is no second spelling of
    /// the rule to keep in step (HANDOFF §5.2.1②).
    /// </para>
    /// <para>
    /// A legitimate <c>|: A [1. D] :| [2. O]</c> reaches neither arm: both endings, including
    /// the one written after the <c>:|</c>, are children of the repeat block.
    /// </para>
    /// <para>
    /// ⚠️ PERF (HANDOFF §7 ⑼): semantic validation is the KEYSTROKE path — the LSP's
    /// PublishDiagnostics runs it on every edit — so what this walks matters. It walks the
    /// FORM's own subtree, never <c>tree.DescendantNodes()</c>: a form body is a handful of
    /// items, and the LYS6007 check one line above already walks exactly this subtree
    /// (SectionReferenceFinder.AllSectionNameTokens). The added cost is a second pass over
    /// that same handful, once per form declaration — no whole-tree scan, no allocation per
    /// item, and nothing at all for the 1025 books that contain no such ending.
    /// </para>
    /// </remarks>
    private void ReportEndingsNoRepeatOpens(FormDeclarationSyntax form)
    {
        foreach (var node in form.DescendantNodes())
        {
            if (node is not FormAlternativeSyntax ending
                || ending.IsInside<FormRepeatBlockSyntax>())
                continue;

            string section = ending.SectionName.Text;
            _diagnostics.Warning(InkSpan(ending), DiagnosticCodes.VoltaEndingWithoutRepeat,
                // ⚠️ Every quoted spelling here is either lifted from the source or is a
                // SUGGESTION. HANDOFF §5.0: "what you report, you quote — you do not
                // rebuild it", and the first draft of this message broke that by offering
                // "drop the '[1.]'" — a bracket-plus-number with the section cut out of the
                // middle, which is not a string the author ever typed and not one the
                // language accepts. VoltaText IS written ("1.", "1-3."), and the "|: … :|"
                // clause is a candidate, which is the one place rebuilding is the right job.
                $"No repeat opens this ending, so '{ending.VoltaText}' prints nothing and "
                + $"'{section}' is engraved as an ordinary section reference. Open a repeat "
                + $"('|: … [{ending.VoltaText} {section}] :| …'), or remove the brackets and "
                + $"write '{section}' on its own.");
        }
    }

    /// <summary>
    /// The written characters of <paramref name="node"/>, with no trailing whitespace.
    /// </summary>
    /// <remarks>
    /// ⚠️ <see cref="SyntaxNode.Span"/> is NOT this: it drops the leading trivia but keeps the
    /// last token's TRAILING trivia, so <c>[1. B]</c> in <c>{ A [1. B] }</c> spans
    /// <c>"[1. B] "</c> — one character too many, and the squiggle would reach into the space
    /// after the ending. A TOKEN's span is ink (measured: the <c>]</c> is 104..105, not
    /// 104..106), so the ink end is the last present child's end. Children are walked
    /// backwards because the optional slots — separator, end number, tilde, display label,
    /// closing bracket — are null when unwritten, and an ending with no <c>]</c> ends on its
    /// section name.
    /// </remarks>
    private static TextSpan InkSpan(SyntaxNode node)
    {
        for (int i = node.SlotCount - 1; i >= 0; i--)
            if (node.GetChild(i) is { } last)
                return new TextSpan(node.Span.Start, last.Span.End - node.Span.Start);
        return node.Span;
    }

    private void ValidateScoreBindings(SyntaxTree tree, HashSet<string> declared)
    {
        foreach (var score in tree.GetNodes<RenderDeclarationSyntax>())
        {
            string reference = score.FormNameText;
            if (string.IsNullOrEmpty(reference))
                _diagnostics.Error(score.RenderKeyword.Span, DiagnosticCodes.UnknownFormReference,
                    "A 'score' must name the form it renders, e.g. 'score main { ... }'.");
            else if (!declared.Contains(reference))
                _diagnostics.Error(score.FormName!.Span, DiagnosticCodes.UnknownFormReference,
                    $"Unknown form '{reference}'. Declare it with 'form {reference} {{ ... }}'.");
        }
    }
}
