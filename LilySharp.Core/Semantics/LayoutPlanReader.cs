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

using System.Globalization;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Turns a <c>layout</c> directive into the <see cref="LayoutPlan"/> it asks for, and
/// says what was wrong with it.
/// </summary>
/// <remarks>
/// ONE HOME FOR THE READING, the contract <see cref="PaperPlanReader"/> and
/// <see cref="FontPlanReader"/> keep: the collector wants the plan and no diagnostics,
/// the validator wants the diagnostics and no plan, and the twin wants the plan the page
/// resolved to — if each parsed the entries itself they would eventually disagree about
/// which directives are legal.
/// <para>
/// The vocabulary: <c>marks stacked|beside</c> (<see cref="MarkArrangement"/>) and
/// <c>barNumbers lines|none|every N</c> (<see cref="BarNumberPolicy"/>). What belongs
/// here and not in <c>paper</c> is the rule <see cref="LayoutPlan"/> states: a switch
/// among a few drawings, score-wide, with no unit.
/// </para>
/// </remarks>
internal static class LayoutPlanReader
{
    /// <summary>Something the directive got wrong, with the span to point at.</summary>
    /// <param name="Span">Where to underline.</param>
    /// <param name="Code">A <see cref="DiagnosticCodes"/> constant.</param>
    /// <param name="Message">The prose, ASCII punctuation only (these reach the CLI).</param>
    /// <param name="IsError">False for a warning.</param>
    internal readonly record struct Problem(TextSpan Span, string Code, string Message, bool IsError);

    /// <summary>Every key a <c>layout { }</c> entry can be spelled with — the syntax
    /// layer's list (<see cref="SyntaxFacts.LayoutKeyVocabulary"/>), which the entry walker
    /// cuts entries by.</summary>
    internal static IReadOnlyList<string> AllKeySpellings() => SyntaxFacts.LayoutKeyVocabulary;

    /// <summary>The words each key takes, for messages and for completion.</summary>
    internal static IReadOnlyList<string> ValueWords(string key) => Canonical(key) switch
    {
        MarkArrangement.Property => MarkArrangement.Modes,
        BarNumberPolicy.Key => BarNumberPolicy.Words,
        AccidentalStyles.Key => AccidentalStyles.Words,
        SectionLabels.Key => SectionLabels.Words,
        PartCombineTexts.Key => PartCombineTexts.Words,
        ChordQualityStyles.Key => ChordQualityStyles.Words,
        MinorChords.Key => MinorChords.Words,
        _ => [],
    };

    /// <summary>
    /// Reads <paramref name="layout"/> into the plan it asks for.
    /// </summary>
    /// <param name="layout">The directive.</param>
    /// <param name="problems">Everything wrong with it, in source order.</param>
    /// <returns>
    /// <see cref="LayoutPlan.Default"/> with the directive's entries overlaid. Entries that
    /// produced an ERROR are left out of it, so a block with one bad key still gets the
    /// switches it spelled correctly.
    /// </returns>
    internal static LayoutPlan Read(LayoutDeclarationSyntax layout, out IReadOnlyList<Problem> problems)
        => Read(layout, LayoutPlan.Default, out problems);

    /// <summary><see cref="Read(LayoutDeclarationSyntax, out IReadOnlyList{Problem})"/> over
    /// a base other than the default.</summary>
    internal static LayoutPlan Read(LayoutDeclarationSyntax layout, LayoutPlan @base,
        out IReadOnlyList<Problem> problems)
    {
        var found = new List<Problem>();
        problems = found;

        if (!layout.IsBlock)
        {
            // The blockless form is refused by the parser (LYS9104 and kin) and keeps its
            // tokens so no source position slides. It sets NOTHING here — a refused
            // directive has to be refused all the way through. A blockless NAMED node — a
            // score's pure reference — reads through ReadReference instead, never here.
            return @base;
        }

        return ReadEntriesInto(@base, layout, found);
    }

    /// <summary>Every named top-level layout declaration, in document order.</summary>
    internal static IReadOnlyList<LayoutDeclarationSyntax> NamedDeclarations(SyntaxNode root) =>
        [.. root.DescendantNodes().OfType<LayoutDeclarationSyntax>()
            .Where(l => l.NameToken != null && l.IsBlock && !FontPlanReader.IsInsideRender(l))];

    /// <summary>The file's UNNAMED top-level block, or null — the default every score
    /// that references nothing lays out by. The LAST when there are several, as the
    /// collector reads them in document order (the repeat is warned about).</summary>
    internal static LayoutDeclarationSyntax? FileDefault(SyntaxNode root) =>
        root.DescendantNodes().OfType<LayoutDeclarationSyntax>()
            .LastOrDefault(l => l.NameToken == null && l.IsBlock && !FontPlanReader.IsInsideRender(l));

    /// <summary>
    /// Resolves a score reference's name to its top-level declaration — ONE HOME for the
    /// unknown-name sentence, the same contract as the paper one.
    /// </summary>
    internal static bool TryResolve(SyntaxNode root, LayoutDeclarationSyntax reference,
        out LayoutDeclarationSyntax? declaration, out Problem? problem)
    {
        declaration = null;
        problem = null;
        if (reference.NameToken is not { } nameToken)
            return false;
        string name = nameToken.Text;
        var declarations = NamedDeclarations(root);
        declaration = declarations.FirstOrDefault(d => d.NameToken!.Text == name);
        if (declaration != null)
            return true;
        var declared = declarations.Select(d => d.NameToken!.Text)
            .Distinct(StringComparer.Ordinal).ToList();
        problem = new Problem(nameToken.Span, DiagnosticCodes.UnknownLayoutBlockName,
            $"No layout block is named '{name}'." + (declared.Count > 0
                ? " Declared: " + string.Join(", ", declared) + "."
                : $" Declare one at the top level: layout {name} {{ marks beside }}."),
            IsError: true);
        return false;
    }

    /// <summary>
    /// The plan a score's <c>layout NAME [{ … }]</c> reference asks for: the named block
    /// overlaid on the defaults, then the reference's own override entries overlaid on
    /// THAT — the same reading as one merged block. A reference that resolves to nothing
    /// keeps <paramref name="fallback"/> — refused all the way through.
    /// </summary>
    /// <remarks>
    /// ⚠️ Entry problems are NOT surfaced here — each block's entries are validated where
    /// the block stands — and a same-key repeat ACROSS the two blocks is deliberately not a
    /// warning: overriding a key is the override block's purpose.
    /// </remarks>
    internal static LayoutPlan ReadReference(SyntaxNode root, LayoutDeclarationSyntax reference,
        LayoutPlan fallback)
    {
        if (!TryResolve(root, reference, out var declaration, out _))
            return fallback;
        var discard = new List<Problem>();
        var plan = ReadEntriesInto(LayoutPlan.Default, declaration!, discard);
        if (reference.IsBlock)
            plan = ReadEntriesInto(plan, reference, discard);
        return plan;
    }

    /// <summary>
    /// The plan a score lays out by, read the way the collector reads it: the score's own
    /// reference when it writes one, else the file's unnamed default, else
    /// <see cref="LayoutPlan.Default"/>. The twin asks this so it and the page cannot read
    /// two plans.
    /// </summary>
    internal static LayoutPlan Resolve(SyntaxNode root, RenderDeclarationSyntax? render)
    {
        var file = FileDefault(root);
        var plan = file != null ? Read(file, out _) : LayoutPlan.Default;
        if (render == null)
            return plan;
        // The LAST reference wins, like every repeated single-value setting.
        LayoutDeclarationSyntax? reference = null;
        foreach (var node in render.DescendantNodes())
            if (node is LayoutDeclarationSyntax l)
                reference = l;
        return reference != null ? ReadReference(root, reference, plan) : plan;
    }

    /// <summary>Overlays one block's entries onto <paramref name="plan"/> — the loop the
    /// direct read and the merged reference share. Duplicate-key detection is scoped to
    /// the one block: a repeat across blocks is an override.</summary>
    private static LayoutPlan ReadEntriesInto(
        LayoutPlan plan, LayoutDeclarationSyntax layout, List<Problem> found)
    {
        var boundKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in layout.Entries)
        {
            var span = entry.KeyToken.Span;
            string? key = Canonical(entry.Key);
            if (key == null)
            {
                found.Add(new Problem(span, DiagnosticCodes.UnknownLayoutKey,
                    $"'{entry.Key}' is not a layout key. Known keys: "
                    + string.Join(", ", AllKeySpellings()) + ".",
                    IsError: true));
                continue;
            }

            if (!boundKeys.Add(key))
            {
                found.Add(new Problem(span, DiagnosticCodes.DuplicateLayoutKey,
                    $"'{key}' is set twice in this layout block; the last one wins.",
                    IsError: false));
            }

            plan = key switch
            {
                MarkArrangement.Property => ReadMarks(plan, entry, span, found),
                BarNumberPolicy.Key => ReadBarNumbers(plan, entry, span, found),
                AccidentalStyles.Key => ReadAccidentals(plan, entry, span, found),
                SectionLabels.Key => ReadOneWord(plan, entry, span, found, SectionLabels.Key,
                    SectionLabels.Words, w => SectionLabels.Find(w) is { } s
                        ? plan with { SectionLabels = s } : null),
                PartCombineTexts.Key => ReadOneWord(plan, entry, span, found, PartCombineTexts.Key,
                    PartCombineTexts.Words, w => PartCombineTexts.Find(w) is { } b
                        ? plan with { PartCombineText = b } : null),
                // The two halves of one chord symbol's spelling. Separate keys because they
                // are separate in LilyPond too (an exception table and a boolean property),
                // and because a chart can want either without the other.
                ChordQualityStyles.Key => ReadOneWord(plan, entry, span, found, ChordQualityStyles.Key,
                    ChordQualityStyles.Words, w => ChordQualityStyles.Find(w) is { } s
                        ? plan with { Chords = plan.Chords with { Qualities = s } } : null),
                MinorChords.Key => ReadOneWord(plan, entry, span, found, MinorChords.Key,
                    MinorChords.Words, w => MinorChords.Find(w) is { } b
                        ? plan with { Chords = plan.Chords with { LowercaseMinor = b } } : null),
                // ⚠️ A key published in SyntaxFacts.LayoutKeyVocabulary with no arm here
                // lands on the default below and binds NOTHING, in silence — "a switch
                // nobody reads looks exactly like one that works", the sentence the
                // unknown-key error exists for, one level in. LayoutBlockTests'
                // EveryPublishedKey_IsBoundByTheReader is the machine that says so.
                _ => plan,
            };
        }
        return plan;
    }

    // marks stacked | beside — exactly one word.
    private static LayoutPlan ReadMarks(
        LayoutPlan plan, LayoutDeclarationSyntax.Entry entry, TextSpan keySpan, List<Problem> found)
    {
        string takes = $"'{MarkArrangement.Property}' takes {string.Join(" or ", MarkArrangement.Modes)}";
        if (entry.Values.Count == 0)
        {
            found.Add(new Problem(keySpan, DiagnosticCodes.LayoutEntryBadValue,
                takes + $" — e.g. '{MarkArrangement.Property} {MarkArrangement.Beside}'.", IsError: true));
            return plan;
        }
        var word = entry.Values[0];
        bool? beside = word.Text switch
        {
            MarkArrangement.Beside => true,
            MarkArrangement.Stacked => false,
            _ => null,
        };
        if (beside == null)
        {
            found.Add(new Problem(word.Span, DiagnosticCodes.LayoutEntryBadValue,
                $"'{word.Text}' is not a marks arrangement. " + takes + ".", IsError: true));
            return plan;
        }
        if (entry.Values.Count > 1)
        {
            found.Add(new Problem(entry.Values[1].Span, DiagnosticCodes.LayoutEntryBadValue,
                takes + $" — one word; '{entry.Values[1].Text}' is extra.", IsError: true));
            return plan;
        }
        return plan with { MarksBeside = beside.Value };
    }

    // barNumbers lines | none | every N.
    private static LayoutPlan ReadBarNumbers(
        LayoutPlan plan, LayoutDeclarationSyntax.Entry entry, TextSpan keySpan, List<Problem> found)
    {
        string takes = $"'{BarNumberPolicy.Key}' takes {BarNumberPolicy.LinesWord}, "
            + $"{BarNumberPolicy.NoneWord} or {BarNumberPolicy.EveryWord} N";
        if (entry.Values.Count == 0)
        {
            found.Add(new Problem(keySpan, DiagnosticCodes.LayoutEntryBadValue,
                takes + $" — e.g. '{BarNumberPolicy.Key} {BarNumberPolicy.EveryWord} 4'.", IsError: true));
            return plan;
        }
        var word = entry.Values[0];
        BarNumberPolicy policy;
        int consumed = 1;
        switch (word.Text)
        {
            case BarNumberPolicy.LinesWord:
                policy = BarNumberPolicy.Lines;
                break;
            case BarNumberPolicy.NoneWord:
                policy = BarNumberPolicy.None;
                break;
            case BarNumberPolicy.EveryWord:
                // The count: an integer, at least 1 (every 1 numbers every bar).
                if (entry.Values.Count < 2 || entry.Values[1].Kind != SyntaxKind.IntegerLiteral
                    || !int.TryParse(entry.Values[1].Text, NumberStyles.None, CultureInfo.InvariantCulture, out int n)
                    || n < 1)
                {
                    var at = entry.Values.Count >= 2 ? entry.Values[1].Span : word.Span;
                    found.Add(new Problem(at, DiagnosticCodes.LayoutEntryBadValue,
                        $"'{BarNumberPolicy.EveryWord}' takes a whole number of bars, at least 1: "
                        + $"{BarNumberPolicy.Key} {BarNumberPolicy.EveryWord} 4.", IsError: true));
                    return plan;
                }
                policy = BarNumberPolicy.Every(n);
                consumed = 2;
                break;
            default:
                found.Add(new Problem(word.Span, DiagnosticCodes.LayoutEntryBadValue,
                    $"'{word.Text}' is not a bar-number policy. " + takes + ".", IsError: true));
                return plan;
        }
        if (entry.Values.Count > consumed)
        {
            found.Add(new Problem(entry.Values[consumed].Span, DiagnosticCodes.LayoutEntryBadValue,
                takes + $"; '{entry.Values[consumed].Text}' is extra.", IsError: true));
            return plan;
        }
        return plan with { BarNumbers = policy };
    }

    // accidentals default | modern | modernCautionary | forget | noReset — exactly one word.
    private static LayoutPlan ReadAccidentals(
        LayoutPlan plan, LayoutDeclarationSyntax.Entry entry, TextSpan keySpan, List<Problem> found)
    {
        string takes = $"'{AccidentalStyles.Key}' takes "
            + string.Join(", ", AccidentalStyles.Words.Take(AccidentalStyles.Words.Count - 1))
            + " or " + AccidentalStyles.Words[AccidentalStyles.Words.Count - 1];
        if (entry.Values.Count == 0)
        {
            found.Add(new Problem(keySpan, DiagnosticCodes.LayoutEntryBadValue,
                takes + $" — e.g. '{AccidentalStyles.Key} modern'.", IsError: true));
            return plan;
        }
        var word = entry.Values[0];
        if (AccidentalStyles.Find(word.Text) is not { } style)
        {
            found.Add(new Problem(word.Span, DiagnosticCodes.LayoutEntryBadValue,
                $"'{word.Text}' is not an accidental style. " + takes + ".", IsError: true));
            return plan;
        }
        if (entry.Values.Count > 1)
        {
            found.Add(new Problem(entry.Values[1].Span, DiagnosticCodes.LayoutEntryBadValue,
                takes + $" — one word; '{entry.Values[1].Text}' is extra.", IsError: true));
            return plan;
        }
        return plan with { Accidentals = style };
    }

    /// <summary>
    /// The shape a key with ONE word out of a closed list takes: exactly one word, from the
    /// list, and nothing after it. <paramref name="bind"/> answers the new plan for a word
    /// it knows and null for one it does not, so the vocabulary and the binding stay in the
    /// key's own home rather than being spelled twice here.
    /// </summary>
    private static LayoutPlan ReadOneWord(
        LayoutPlan plan, LayoutDeclarationSyntax.Entry entry, TextSpan keySpan, List<Problem> found,
        string key, IReadOnlyList<string> words, Func<string, LayoutPlan?> bind)
    {
        string takes = $"'{key}' takes "
            + string.Join(", ", words.Take(words.Count - 1)) + " or " + words[words.Count - 1];
        if (entry.Values.Count == 0)
        {
            found.Add(new Problem(keySpan, DiagnosticCodes.LayoutEntryBadValue,
                takes + $" — e.g. '{key} {words[1]}'.", IsError: true));
            return plan;
        }
        var word = entry.Values[0];
        if (bind(word.Text) is not { } bound)
        {
            found.Add(new Problem(word.Span, DiagnosticCodes.LayoutEntryBadValue,
                $"'{word.Text}' is not a value of '{key}'. " + takes + ".", IsError: true));
            return plan;
        }
        if (entry.Values.Count > 1)
        {
            found.Add(new Problem(entry.Values[1].Span, DiagnosticCodes.LayoutEntryBadValue,
                takes + $" — one word; '{entry.Values[1].Text}' is extra.", IsError: true));
            return plan;
        }
        return bound;
    }

    /// <summary>The canonical spelling <paramref name="word"/> matches among the keys, or
    /// null. Case-insensitive, like a paper key.</summary>
    private static string? Canonical(string word)
    {
        foreach (var candidate in AllKeySpellings())
            if (word.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                return candidate;
        return null;
    }
}
