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
            ReportEndingPasses(FormWalk.Read(form));
            ReportLabelsThatWillNotPrint(form);
        }

        // Every score must reference a form that exists.
        ValidateScoreBindings(tree, declared);
    }

    /// <summary>
    /// LYS0012 — a quoted occurrence label on a play that will not print one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The condition is the label RULE's, asked once: a label is written, and
    /// <see cref="SectionLabelRule.IsShown"/> says nothing prints. It moved here from the
    /// parser on 2026-08-31, when a <c>section ~A</c> declaration could flip the answer; the
    /// declaration lost that say on 2026-09-24 (LYS0033), so the answer is the reference's
    /// own again, but asking the rule rather than the surface keeps the two from drifting.
    /// </para>
    /// <para>
    /// ⚠️ PERF: same shape as ReportEndingsNoRepeatOpens above — the FORM's own subtree, a
    /// handful of items, once per form declaration on the keystroke path.
    /// </para>
    /// </remarks>
    private void ReportLabelsThatWillNotPrint(FormDeclarationSyntax form)
    {
        foreach (var node in form.DescendantNodes())
        {
            var (name, silent, span) = node switch
            {
                // An ending's sections ([1. ~B "x"]) are these same reference nodes.
                SectionReferenceSyntax r => (r.SectionName, false, r.Identifier.Span),
                { Kind: SyntaxKind.SilentSectionReference } s
                    when s.GetChild(1) is SyntaxTokenNode n => (n.Text, true, n.Span),
                _ => (null, false, default(TextSpan)),
            };
            if (name == null || SyntaxFacts.UnquotedLabel(node) is not { } label)
                continue;

            if (SectionLabelRule.IsShown(silent))
                continue;

            _diagnostics.Warning(span, DiagnosticCodes.HiddenSectionLabel,
                $"The section label \"{label}\" is not printed: "
                + "the '~' on this reference hides it; drop the '~' to show it (or remove the label).");
        }
    }

    /// <summary>
    /// LYS6008 — every volta ending in <paramref name="form"/> that no repeat block opened.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The predicate is the one reader of a form's spellings, <see cref="FormWalk"/>: an
    /// ending a repeat opened is a child of a <c>FormWalk.Repeat</c> — written inside a
    /// <c>|: … :|</c> block, or trailing the run a form-level <c>:|:</c> opened
    /// (<c>A :|: B [1. C] :| [2. D]</c>, <c>FormWalk.GroupDividerRepeats</c>) — and a loose one
    /// is an item of the form itself. It is deliberately not "does this form contain a repeat
    /// block": that weaker rule misses <c>|: A :| B [1. B]</c>, whose ending is dropped exactly
    /// like one in a form with no repeat at all. Asking the reader the MIDI, MusicXML and the
    /// twin ask keeps the diagnostic and the behaviour from drifting apart (HANDOFF §5.2.1②).
    /// ⚠️ Until 2026-09-29 (第663 ⒁) this asked the TREE instead — an ancestor
    /// <c>FormRepeatBlockSyntax</c> — and so accused the divider run's endings, which every
    /// reader played (and the page, from that day, brackets: <c>MeasureCollector.ProcessForm</c>).
    /// </para>
    /// <para>
    /// A legitimate <c>|: A [1. D] :| [2. O]</c> reaches neither arm: both endings, including
    /// the one written after the <c>:|</c>, are children of the repeat block.
    /// </para>
    /// <para>
    /// ⚠️ PERF (HANDOFF §7 ⑼): semantic validation is the KEYSTROKE path — the LSP's
    /// PublishDiagnostics runs it on every edit — so what this walks matters. It walks the
    /// FORM's own items, never <c>tree.DescendantNodes()</c>: a form body is a handful of
    /// items, and the LYS6007 check one line above already walks exactly this subtree
    /// (SectionReferenceFinder.AllSectionNameTokens). The added cost is one
    /// <see cref="FormWalk.Read"/> of that handful (a list of its items), once per form
    /// declaration — no whole-tree scan, and nothing more for the 1025 books that contain no
    /// such ending.
    /// </para>
    /// </remarks>
    private void ReportEndingsNoRepeatOpens(FormDeclarationSyntax form)
    {
        foreach (var item in FormWalk.Read(form))
        {
            if (item is not FormWalk.Ending { Node: var ending })
                continue;

            var names = ending.Sections
                .Select(s => Editing.SectionSymbols.ReferencedName(s)?.Text ?? "")
                .ToList();
            string section = string.Join(" ", names);
            string reference = names.Count == 1 ? "an ordinary section reference" : "ordinary section references";
            _diagnostics.Warning(InkSpan(ending), DiagnosticCodes.VoltaEndingWithoutRepeat,
                // ⚠️ Every quoted spelling here is either lifted from the source or is a
                // SUGGESTION. HANDOFF §5.0: "what you report, you quote — you do not
                // rebuild it", and the first draft of this message broke that by offering
                // "drop the '[1.]'" — a bracket-plus-number with the section cut out of the
                // middle, which is not a string the author ever typed and not one the
                // language accepts. VoltaText IS written ("1.", "1-3."), and the "|: … :|"
                // clause is a candidate, which is the one place rebuilding is the right job.
                $"No repeat opens this ending, so '{ending.VoltaText}' prints nothing and "
                + $"'{section}' is engraved as {reference}. Open a repeat "
                + $"('|: … [{ending.VoltaText} {section}] :| …'), or remove the brackets and "
                + $"write '{section}' on its own.");
        }
    }

    /// <summary>
    /// LYS1042 and LYS1043 — each repeat with endings: no <c>:|*N</c> beside the numbers, and
    /// every pass from 1 to the highest number named by exactly one ending (owner's decision
    /// 2026-09-30). The numbers are then the whole story the page prints and every reader plays
    /// (<see cref="RepeatPasses"/>), as LilyPond's <c>\volta</c> numbers are.
    /// </summary>
    /// <remarks>⚠️ PERF: the form's own items, as <see cref="ReportEndingsNoRepeatOpens"/>.</remarks>
    private void ReportEndingPasses(IReadOnlyList<FormWalk.Item> items)
    {
        foreach (var repeat in items.OfType<FormWalk.Repeat>())
        {
            ReportEndingPasses(repeat.Children);
            var endings = repeat.Children.OfType<FormWalk.Ending>().Select(e => e.Node).ToList();
            if (endings.Count == 0)
                continue;

            if (repeat.ExplicitPlayCount is { } count && repeat.Node is { } block && CountSpan(block) is { } countSpan)
                _diagnostics.Error(countSpan, DiagnosticCodes.RepeatCountWithEndings,
                    $"A repeat with endings takes its passes from the endings' numbers: remove "
                    + $"'*{count}' and number every pass instead — the pass an ending plays on "
                    + "is the number it prints: three passes, the first two on B, are "
                    + "'|: A [1-2. B] :| [3. C]'.");

            var namedBy = new Dictionary<int, FormAlternativeSyntax>();
            foreach (var ending in endings)
            {
                foreach (int pass in ending.Numbers)
                {
                    if (namedBy.TryGetValue(pass, out var first))
                    {
                        _diagnostics.Error(InkSpan(ending), DiagnosticCodes.EndingPassNotNamedOnce,
                            $"Pass {pass} is already '[{first.VoltaText}': each pass plays one ending "
                            + $"— take {pass} out of one of the two numbers.");
                        break;
                    }
                    namedBy[pass] = ending;
                }
            }
            int highest = namedBy.Keys.DefaultIfEmpty(0).Max();
            var missing = Enumerable.Range(1, highest).Where(p => !namedBy.ContainsKey(p)).ToList();
            if (missing.Count == 0)
                continue;
            // At the ending after the first gap: the one whose lowest number passes it.
            var after = endings.First(e => e.Numbers.Any() && e.Numbers.Min() > missing[0]);
            string passes = missing.Count == 1 ? $"pass {missing[0]}" : $"passes {string.Join(", ", missing)}";
            _diagnostics.Error(InkSpan(after), DiagnosticCodes.EndingPassNotNamedOnce,
                $"No ending plays {passes} of this repeat: every pass up to the highest number "
                + $"({highest}) plays one ending — give {passes} to an ending's number "
                + $"('[{missing[0]}. …]', or a range such as '[1-{missing[0]}. …]').");
        }
    }

    /// <summary>The written <c>*N</c> of a repeat block, or null.</summary>
    private static TextSpan? CountSpan(FormRepeatBlockSyntax block)
    {
        for (int i = 0; i + 1 < block.SlotCount; i++)
            if (block.GetChild(i) is SyntaxTokenNode { Kind: SyntaxKind.Asterisk } star
                && block.GetChild(i + 1) is SyntaxTokenNode count)
                return new TextSpan(star.Span.Start, count.Span.End - star.Span.Start);
        return null;
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
    /// backwards because the optional slots — separator, end number, closing bracket — are
    /// null when unwritten, and an ending with no <c>]</c> ends on its last section, a NODE
    /// whose own span keeps the trailing trivia again — so the walk descends into it.
    /// </remarks>
    private static TextSpan InkSpan(SyntaxNode node)
    {
        for (int i = node.SlotCount - 1; i >= 0; i--)
            if (node.GetChild(i) is { } last)
                return new TextSpan(node.Span.Start,
                    (last is SyntaxTokenNode ? last.Span.End : InkSpan(last).End) - node.Span.Start);
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
