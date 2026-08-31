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
            ReportLabelsThatWillNotPrint(form, tree);
        }

        // Every score must reference a form that exists.
        ValidateScoreBindings(tree, declared);
    }

    /// <summary>
    /// LYS0012 — a quoted occurrence label on a play that will not print one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ THIS USED TO BE A PARSE-TIME WARNING, and it said "hidden by '~'". That was true
    /// while a tilde on a reference always meant HIDE. Since a section can declare its own
    /// label default (<c>section ~A { … }</c>, owner's decision 2026-08-31) the tilde asks for
    /// the OTHER default, so <c>form { ~A "shown" }</c> against a <c>section ~A</c> prints
    /// that label — and the parser, which cannot see declarations, would have gone on calling
    /// it hidden. An instrument that reports the surface (is there a tilde?) instead of the
    /// question (does this play show a label?) is the kind this repository keeps having to
    /// repair, so the check moved to where the declarations are.
    /// </para>
    /// <para>
    /// The condition is the label RULE's, asked once: a label is written, and
    /// <see cref="SectionLabelRule.IsShown"/> says nothing prints. Both spellings reach it —
    /// a plain reference against a <c>section ~A</c> is now just as silent as a tilde one
    /// against an ordinary section, and it was the second case that had no diagnostic at all.
    /// </para>
    /// <para>
    /// ⚠️ PERF: same shape as ReportEndingsNoRepeatOpens above — the FORM's own subtree, a
    /// handful of items, once per form declaration on the keystroke path. The declaration
    /// lookup is built once per form rather than per item.
    /// </para>
    /// </remarks>
    private void ReportLabelsThatWillNotPrint(FormDeclarationSyntax form, SyntaxTree tree)
    {
        Dictionary<string, SectionDeclarationSyntax>? sections = null;

        foreach (var node in form.DescendantNodes())
        {
            var (name, silent, span) = node switch
            {
                SectionReferenceSyntax r => (r.SectionName, false, r.Identifier.Span),
                FormAlternativeSyntax a => (a.SectionName.Text, a.IsSilent, a.SectionName.Span),
                { Kind: SyntaxKind.SilentSectionReference } s
                    when s.GetChild(1) is SyntaxTokenNode n => (n.Text, true, n.Span),
                _ => (null, false, default(TextSpan)),
            };
            if (name == null || SyntaxFacts.UnquotedLabel(node) is not { } label)
                continue;

            sections ??= BuildSectionIndex(tree);
            sections.TryGetValue(name, out var declaration);
            if (SectionLabelRule.IsShown(declaration, silent))
                continue;

            _diagnostics.Warning(span, DiagnosticCodes.HiddenSectionLabel,
                $"The section label \"{label}\" is not printed: "
                + (declaration?.LabelHiddenByDefault == true
                    ? (silent
                        ? $"'section ~{name}' prints no label by default and this reference asks for the default."
                        : $"'section ~{name}' prints no label by default; write '~{name}' here to show it.")
                    : $"the '~' on this reference hides it; drop the '~' to show it (or remove the label)."));
        }
    }

    /// <summary>Every section declaration by name — the first wins, matching the collector's
    /// own one-node-per-name map (MeasureCollector's remark on a second same-named source).</summary>
    private static Dictionary<string, SectionDeclarationSyntax> BuildSectionIndex(SyntaxTree tree)
    {
        var byName = new Dictionary<string, SectionDeclarationSyntax>(StringComparer.Ordinal);
        foreach (var s in tree.GetNodes<SectionDeclarationSyntax>())
            // ⚠️ ANY declaration with the tilde wins, not the first: part-major layout declares
            // one `section A` PER PART, and "this section is structure" is a property of the
            // section, not of one part's copy of it.
            if (!byName.TryGetValue(s.SectionName, out var existing) || s.LabelHiddenByDefault)
                if (existing?.LabelHiddenByDefault != true)
                    byName[s.SectionName] = s;
        return byName;
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
