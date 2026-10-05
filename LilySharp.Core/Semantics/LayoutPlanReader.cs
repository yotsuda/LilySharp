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
using LilySharp.Core.Svg.Collector;
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
/// The vocabulary: <c>markTempo stacked|beside</c> (<see cref="MarkArrangement"/>) and
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

    /// <summary>
    /// The keys that take NUMBERS rather than words — the engraving style
    /// (<see cref="Svg.EngravingStyle"/>) — with the default each writes out and a value that
    /// is not the default, for messages, for completion and for the checks that every key binds.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, (string Default, string Example)> NumberKeys =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["lineThickness"] = ("0.1", "0.12"),
            ["StaffLine.thickness"] = ("1.0", "1.2"),
            ["LedgerLine.thickness"] = ("1.0 0.1", "1.0 0.15"),
            ["LedgerLine.lengthFraction"] = ("0.25", "0.3"),
            ["Stem.thickness"] = ("1.3", "1.5"),
            ["Stem.lengthFraction"] = ("1.0", "1.1"),
            ["Beam.thickness"] = ("0.48", "0.5"),
            ["Beam.damping"] = ("1.0", "2"),
            ["BarLine.thinThickness"] = ("1.9", "2.5"),
            ["BarLine.thickThickness"] = ("6.0", "7"),
        };

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
        ChordDiagramsKey.Key => ChordDiagramsKey.Words,
        ChordNamesKey.Key => ChordNamesKey.Words,
        ChordListKey.Key => ChordListKey.Words,
        VoltaBracketLength.Key => VoltaBracketLength.Words,
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
        [.. TopLevelNodes.OfRoot<LayoutDeclarationSyntax>(root)
            .Where(l => l.NameToken != null && l.IsBlock)];

    /// <summary>The file's UNNAMED top-level block, or null — the default every score
    /// that references nothing lays out by. The LAST when there are several, as the
    /// collector reads them in document order (the repeat is warned about).</summary>
    internal static LayoutDeclarationSyntax? FileDefault(SyntaxNode root) =>
        TopLevelNodes.OfRoot<LayoutDeclarationSyntax>(root)
            .LastOrDefault(l => l.NameToken == null && l.IsBlock);

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
                : $" Declare one at the top level: layout {name} {{ markTempo beside }}."),
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
        var plan = FilePlan(root);
        if (render == null)
            return plan;
        // The LAST reference wins, like every repeated single-value setting.
        LayoutDeclarationSyntax? reference = null;
        foreach (var node in render.DescendantNodes())
            if (node is LayoutDeclarationSyntax l)
                reference = l;
        return reference != null ? ReadReference(root, reference, plan) : plan;
    }

    /// <summary>
    /// <see cref="Resolve(SyntaxNode, RenderDeclarationSyntax?)"/> for a caller holding the
    /// PARSED score (<see cref="RenderSpec.LayoutRef"/> is that same last reference, read by
    /// <c>RenderSpecParser.Parse</c>) — the MIDI, which plays one score and reads its capo.
    /// </summary>
    internal static LayoutPlan ResolveFor(SyntaxNode root, RenderSpec? spec)
    {
        var plan = FilePlan(root);
        return spec?.LayoutRef is { } reference ? ReadReference(root, reference, plan) : plan;
    }

    /// <summary>The plan a score that references nothing lays out by: the file's unnamed
    /// default block, else <see cref="LayoutPlan.Default"/>.</summary>
    private static LayoutPlan FilePlan(SyntaxNode root)
    {
        var file = FileDefault(root);
        return file != null ? Read(file, out _) : LayoutPlan.Default;
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
                    SyntaxFacts.LayoutKeyCaseOnlyMatch(entry.Key) is { } canonicalKey
                        ? $"'{entry.Key}' is not a layout key. Keys are case-sensitive: "
                          + $"write '{canonicalKey}'."
                        : $"'{entry.Key}' is not a layout key. Known keys: "
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
                ChordDiagramsKey.Key => ReadChordDiagrams(plan, entry, span, found, layout),
                // What a name shows under a capo (ChordNameMode) — the third half of the
                // chord spelling, beside the quality's vocabulary and the minor root's case.
                ChordNamesKey.Key => ReadOneWord(plan, entry, span, found, ChordNamesKey.Key,
                    ChordNamesKey.Words, w => ChordNamesKey.Find(w) is { } mode
                        ? plan with { Chords = plan.Chords with { Names = mode } } : null),
                // The chord list at the score's head (ChordListKey).
                ChordListKey.Key => ReadOneWord(plan, entry, span, found, ChordListKey.Key,
                    ChordListKey.Words, w => ChordListKey.Find(w) is { } on
                        ? plan with { ChordList = on } : null),
                VoltaBracketLength.Key => ReadVoltaBracket(plan, entry, span, found),
                // The engraving style (Svg.EngravingStyle): a number each, LedgerLine two.
                "lineThickness" => ReadStyle(plan, entry, span, found, (s, v) => s with { LineThickness = v[0] }),
                "StaffLine.thickness" => ReadStyle(plan, entry, span, found, (s, v) => s with { StaffSymbolThickness = v[0] }),
                "LedgerLine.thickness" => ReadStyle(plan, entry, span, found,
                    (s, v) => s with { LedgerLineThicknessLines = v[0], LedgerLineThicknessSpaces = v[1] }),
                "LedgerLine.lengthFraction" => ReadStyle(plan, entry, span, found, (s, v) => s with { LedgerLengthFraction = v[0] }),
                "Stem.thickness" => ReadStyle(plan, entry, span, found, (s, v) => s with { StemThickness = v[0] }),
                "Stem.lengthFraction" => ReadStyle(plan, entry, span, found, (s, v) => s with { StemLengthFraction = v[0] }),
                "Beam.thickness" => ReadStyle(plan, entry, span, found, (s, v) => s with { BeamThickness = v[0] }),
                "Beam.damping" => ReadStyle(plan, entry, span, found, (s, v) => s with { BeamDamping = v[0] }),
                "BarLine.thinThickness" => ReadStyle(plan, entry, span, found, (s, v) => s with { BarLineHairThickness = v[0] }),
                "BarLine.thickThickness" => ReadStyle(plan, entry, span, found, (s, v) => s with { BarLineThickThickness = v[0] }),
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

    /// <summary>
    /// One engraving-style key (<c>Stem.thickness 1.5</c>): as many plain numbers as its
    /// <see cref="NumberKeys"/> default has, each positive — <c>LedgerLine.thickness</c>' two may
    /// be 0 but not both, and <c>Beam.damping</c> may be 0 (no damping, as LilyPond's). No unit:
    /// each is a multiple of a line thickness, staff spaces or a factor (Svg.EngravingStyle says
    /// which).
    /// </summary>
    private static LayoutPlan ReadStyle(LayoutPlan plan, LayoutDeclarationSyntax.Entry entry, TextSpan keySpan,
        List<Problem> found, Func<Svg.EngravingStyle, double[], Svg.EngravingStyle> set)
    {
        string example = NumberKeys[entry.Key].Example;
        int count = example.Split(' ').Length;
        bool zeroReads = entry.Key == "Beam.damping";
        string takes = count == 1
            ? $"'{entry.Key}' takes a {(zeroReads ? "number, 0 or more" : "positive number")}, no unit: {entry.Key} {example}."
            : $"'{entry.Key}' takes {count} numbers, not both 0 and none below 0: {entry.Key} {example}.";
        var values = new double[entry.Values.Count];
        bool numbers = entry.Values.Count == count;
        for (int i = 0; numbers && i < count; i++)
            numbers = entry.Values[i].Kind is SyntaxKind.IntegerLiteral or SyntaxKind.DecimalLiteral
                && double.TryParse(entry.Values[i].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]);
        if (numbers)
            numbers = count == 1 ? values[0] > 0 || (zeroReads && values[0] == 0) : values.All(v => v >= 0) && values.Any(v => v > 0);
        if (!numbers)
        {
            found.Add(new Problem(entry.Values.Count > 0 ? entry.Values[0].Span : keySpan,
                DiagnosticCodes.LayoutEntryBadValue, takes, IsError: true));
            return plan;
        }
        return plan with { Style = set(plan.EngravingStyle, values) };
    }

    // markTempo stacked | beside — exactly one word.
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

    // chordDiagrams none | [TUNING] [capo N] [all] [{ table }] (owner's decision 2026-09-28: the
    // tuning first, then the scope word; the capo between them, 2026-09-29, HANDOFF §2 K2), then
    // optionally the SHAPE TABLE in braces (owner's design HANDOFF §2 K5 ③, 2026-09-29:
    // `chordDiagrams guitar { Cm7 x35343  G  section B { … } }`, ReadShapeTable). Its own
    // reader for the grammar and the message: the tuning vocabulary is thirty words, so the
    // "takes" sentence names the commonest and points at the rest rather than listing them
    // (ReadOneWord would).
    private static LayoutPlan ReadChordDiagrams(
        LayoutPlan plan, LayoutDeclarationSyntax.Entry entry, TextSpan keySpan, List<Problem> found,
        LayoutDeclarationSyntax layout)
    {
        const string all = ChordDiagramsKey.AllWord, none = Music.ChordShapes.NoneWord,
            capoWord = ChordDiagramsKey.CapoWord;
        string key = ChordDiagramsKey.Key;
        string takes = $"'{key}' takes {none} or a tuning name (guitar, ukulele, mandolin, "
            + $"guitardropd, ... - the words a tab's 'tuning' takes), optionally followed by '{capoWord} N' "
            + $"(the fret the capo is on) and by '{all}' (every chord name draws a diagram), or "
            + $"'{capoWord} N' / '{all}' alone; then, optionally, a shape "
            + "table in braces ('{ Cm7 x35343  G }': the chords that draw a diagram, with the shape each draws)";
        void Refuse(TextSpan span, string message)
            => found.Add(new Problem(span, DiagnosticCodes.LayoutEntryBadValue, message, IsError: true));

        // The words before the table's '{' (the table is read after them).
        int open = -1;
        for (int i = 0; i < entry.Values.Count; i++)
            if (entry.Values[i].Kind == SyntaxKind.OpenBrace)
            {
                open = i;
                break;
            }
        IReadOnlyList<SyntaxTokenNode> head = open < 0 ? entry.Values : [.. entry.Values.Take(open)];

        if (head.Count == 0 && open < 0)
        {
            Refuse(keySpan, takes + $" - e.g. '{key} guitar' or '{key} guitar {all}'.");
            return plan;
        }
        string? tuningWord = null;
        bool drawsAll = false;
        int capo = 0;
        if (head.Count > 0)
        {
            var word = head[0];
            if (!ChordDiagramsKey.TryFind(word.Text))
            {
                Refuse(word.Span, $"'{word.Text}' is not a value of '{key}'." + CaseHint(word.Text) + " " + takes + ".");
                return plan;
            }
            if (word.Text == none)
            {
                if (head.Count > 1)
                {
                    string s = head[1].Text;
                    Refuse(head[1].Span, s == all
                        ? $"'{none}' draws no diagram, so it takes no '{all}' - write '{key} {all}' "
                          + $"(or '{key} guitar {all}') to draw every chord, or '{key} {none}' alone."
                        : s == capoWord
                            ? $"'{none}' draws no diagram, so it takes no '{capoWord}' - write '{key} guitar {capoWord} 3', "
                              + $"or '{key} {none}' alone."
                            : $"'{none}' takes nothing after it; '{s}' is extra.");
                    return plan;
                }
                tuningWord = none;
            }
            else
            {
                // [TUNING] [capo N] [all], in that order; anything else names what it should be.
                int i = 0;
                if (ChordDiagramsKey.IsTuningWord(word.Text))
                {
                    tuningWord = word.Text;
                    i = 1;
                }
                if (i < head.Count && head[i].Text == capoWord)
                {
                    if (i + 1 >= head.Count || head[i + 1].Kind != SyntaxKind.IntegerLiteral
                        || !int.TryParse(head[i + 1].Text, NumberStyles.None, CultureInfo.InvariantCulture, out capo)
                        || capo > ChordDiagramsKey.MaxCapo)
                    {
                        var at = i + 1 < head.Count ? head[i + 1].Span : head[i].Span;
                        Refuse(at, $"'{capoWord}' takes the fret the capo is on, 1 to {ChordDiagramsKey.MaxCapo}: "
                            + $"'{key} guitar {capoWord} 3'.");
                        return plan;
                    }
                    if (capo == 0)
                    {
                        Refuse(head[i + 1].Span, $"'{capoWord} 0' is no capo - leave the '{capoWord}' out.");
                        return plan;
                    }
                    i += 2;
                }
                if (i < head.Count && head[i].Text == all)
                {
                    drawsAll = true;
                    i++;
                }
                if (i < head.Count)
                {
                    var extra = head[i];
                    string s = extra.Text;
                    string written = tuningWord ?? "guitar";
                    Refuse(extra.Span,
                        s == all ? $"'{all}' is written twice - write '{key} {(tuningWord == null && capo == 0 ? all : $"{written}{(capo > 0 ? $" {capoWord} {capo}" : "")} {all}")}'."
                        : s == capoWord && capo > 0 ? $"'{capoWord}' is written twice - write '{key} {written} {capoWord} {capo}'."
                        : s == capoWord ? $"the capo comes before '{all}': write '{key} {written} {capoWord} N {all}'."
                        : ChordDiagramsKey.IsTuningWord(s) && tuningWord == null
                            ? $"the tuning comes first: write '{key} {s}{(capo > 0 ? $" {capoWord} {capo}" : "")}{(drawsAll ? $" {all}" : "")}'."
                        : s == tuningWord ? $"'{s}' is written twice - write '{key} {s}'."
                        : ChordDiagramsKey.IsTuningWord(s) || s == none
                            ? takes + $" - one word for the tuning, then optionally '{capoWord} N' and '{all}'; '{s}' is extra."
                        : drawsAll ? $"'{all}' takes nothing after it; '{s}' is extra."
                        : $"'{s}' is not a value of '{key}' here: after the tuning only '{capoWord} N' and '{all}' may follow."
                          + CaseHint(s));
                    return plan;
                }
            }
        }

        // The shape table, when one follows (never after `none`: it draws nothing).
        Music.ChordShapeTable? table = null;
        if (open >= 0)
        {
            if (tuningWord == none)
            {
                Refuse(entry.Values[open].Span, $"'{none}' draws no diagram, so it takes no shape table - "
                    + $"write '{key} guitar {{ ... }}' (or '{key} {{ ... }}') for the chords that draw, "
                    + $"or '{key} {none}' alone.");
                return plan;
            }
            table = ReadShapeTable(entry.Values, open, tuningWord, capo, layout, found);
            if (table == null)
                return plan;
        }

        // The tuning word as written, `none` included: an absent key — or `all` alone — is
        // null, and means "the part's instrument, else the guitar" (ChordDiagramsKey.Resolve).
        // All four parts are set, so an override block's `chordDiagrams guitar` drops a named
        // block's `all`, its capo and its table (a key written again is written whole). The
        // capo lives in the chord SPELLING, the value every namer is handed (ChordSpelling).
        return plan with
        {
            ChordDiagrams = tuningWord,
            ChordDiagramsAll = drawsAll,
            ChordDiagramTable = table,
            Chords = plan.Chords with { Capo = capo },
        };

        // Values are case-sensitive, like keys: `Guitar`, `ALL` name the spelling that works.
        static string CaseHint(string written)
            => ChordDiagramsKey.Words.FirstOrDefault(w => w.Equals(written, StringComparison.OrdinalIgnoreCase)
                    && !w.Equals(written, StringComparison.Ordinal)) is { } canonical
                ? $" Values are case-sensitive: write '{canonical}'."
                : "";
    }

    /// <summary>
    /// Reads the shape table after the <c>chordDiagrams</c> words — the tokens from the
    /// <c>{</c> at <paramref name="open"/> to its closer — into a <see cref="Music.ChordShapeTable"/>:
    /// entries <c>SYMBOL [TUNINGWORD] SHAPE …</c> (a name alone: the usual shape), and
    /// <c>section NAME { entries }</c> blocks. Null when the table is refused as a whole (a
    /// structural error: a stray brace, a <c>section</c> with no name or block, tokens after
    /// the closer); an entry with a problem is left out with a warning and the rest stand, the
    /// way a row's written shapes do (LYS1038).
    /// </summary>
    /// <remarks>
    /// The words are the tokens re-joined by ADJACENCY (a chords row's rule): <c>C#m7-5/G</c>
    /// and <c>8xx88-11</c> are one word each; a brace is always its own. A word is a chord
    /// symbol unless it starts a shape (<see cref="Music.ChordShapes.StartsShape"/>) or names a
    /// tuning — the words after a symbol are its shapes, read by
    /// <see cref="Music.ChordShapes.ParseWords"/>. The section names are wrapped in the
    /// keyword so a section named <c>A</c> or <c>C</c> cannot be read as a chord. A section no
    /// <c>section NAME { }</c> declares warns (its entries apply nowhere); a chord listed twice
    /// in one scope warns, and the last one wins (a key's rule). Each shape is checked against
    /// its chord (LYS1039) on the tunings it can draw on here: the layout's, else the guitar and
    /// every fretted instrument the file's parts play.
    /// </remarks>
    private static Music.ChordShapeTable? ReadShapeTable(IReadOnlyList<SyntaxTokenNode> values, int open,
        string? tuningWord, int capo, LayoutDeclarationSyntax layout, List<Problem> found)
    {
        const string sectionWord = "section";
        string key = ChordDiagramsKey.Key;
        bool refused = false;
        void Error(TextSpan span, string message)
        {
            found.Add(new Problem(span, DiagnosticCodes.LayoutEntryBadValue, message, IsError: true));
            refused = true;
        }
        void Warn(TextSpan span, string code, string message)
            => found.Add(new Problem(span, code, message, IsError: false));

        var words = TableWords(values, open + 1);
        var song = System.Collections.Immutable.ImmutableArray.CreateBuilder<Music.ChordShapeEntry>();
        var sections = System.Collections.Immutable.ImmutableArray
            .CreateBuilder<(string Section, System.Collections.Immutable.ImmutableArray<Music.ChordShapeEntry> Entries)>();
        var sectionSpans = new List<(string Name, TextSpan Span)>();
        var scope = song;
        string? sectionName = null;

        // The entry being read: its symbol (null between entries, or while the words of a
        // symbol that did not parse are skipped), and the words after it.
        string? symbol = null;
        TextSpan symbolSpan = default;
        Music.ChordStructure? chord = null;
        bool skipping = false;
        var entryWords = new List<(string Text, TextSpan Span)>();

        SyntaxNode root = layout;
        while (root.Parent != null)
            root = root.Parent;
        List<TuningType>? checkTunings = null;

        void Flush()
        {
            if (symbol == null || chord == null)
            {
                entryWords.Clear();
                return;
            }
            var problems = Music.ChordShapes.ParseWords([.. entryWords.Select(w => w.Text)], symbol, out var shapes);
            foreach (var p in problems)
                Warn(entryWords[p.WordIndex].Span, DiagnosticCodes.ChordDiagramNotDrawn, p.Message);
            checkTunings ??= CheckTunings(root, tuningWord);
            // A table shape is the PRESSED shape: under a capo it is raised by the capo
            // (ChordShapes.AtCapo) and checked against the sounding chord the entry names, so the
            // message speaks sounding names; the pressed chord (ChordStructure.Pressed) only
            // names the fix that keeps the symbol.
            var pressed = chord.Pressed(capo, 0);
            foreach (var tuning in checkTunings)
            {
                if (Music.ChordShapes.WrittenFor(tuning, shapes) is not { } shape
                    || Music.ChordShapes.Mismatch(Music.ChordShapes.AtCapo(Music.ChordShapes.Frets(shape), capo),
                        Tablature.Tunings.GetTuning(tuning), chord) is not { } mismatch)
                    continue;
                string? tuningName = shapes.First(s => s.Shape == shape).TuningName;
                string? pressedShape = capo > 0 ? Music.ChordShapes.Default(tuning, pressed)?.Spelled : null;
                string message = Music.ChordShapes.MismatchMessage(mismatch, shape, tuningName, symbol, inRow: false, inTable: true,
                    capo: capo, pressedShape: pressedShape);
                var at = entryWords.First(w => w.Text == shape).Span;
                if (!found.Any(f => f.Span.Start == at.Start && f.Message == message))
                    Warn(at, DiagnosticCodes.ChordShapeMismatch, message);
            }
            if (scope.Any(e => Music.ChordShapeTable.SameChord(e.Chord, chord)))
                Warn(symbolSpan, DiagnosticCodes.LayoutEntryBadValue,
                    $"'{symbol}' is listed twice in this table; the last one wins.");
            scope.Add(new Music.ChordShapeEntry(symbol, chord, shapes));
            symbol = null;
            chord = null;
            entryWords.Clear();
        }

        int i = 0, depth = 1;
        while (i < words.Count && depth > 0)
        {
            var w = words[i];
            if (w.Text == "}")
            {
                Flush();
                skipping = false;
                depth--;
                if (depth == 1 && sectionName != null)
                {
                    sections.Add((sectionName, scope.ToImmutable()));
                    sectionName = null;
                    scope = song;
                }
                i++;
                continue;
            }
            if (w.Text == "{")
            {
                Error(w.Span, "'{' here opens nothing - a shape table lists chord names with their shapes "
                    + "(Cm7 x35343), and only 'section NAME { ... }' opens a block inside it.");
                return null;
            }
            if (w.Text == sectionWord)
            {
                if (depth > 1)
                {
                    Error(w.Span, "a section's table holds no 'section' of its own - close this one "
                        + "with '}' first.");
                    return null;
                }
                Flush();
                skipping = false;
                if (i + 2 >= words.Count || words[i + 1].Text is "{" or "}" || words[i + 2].Text != "{")
                {
                    Error(w.Span, "'section' takes the section's name and a block of entries: "
                        + "section Chorus { C x35553 }.");
                    return null;
                }
                sectionName = words[i + 1].Text;
                sectionSpans.Add((sectionName, words[i + 1].Span));
                scope = System.Collections.Immutable.ImmutableArray.CreateBuilder<Music.ChordShapeEntry>();
                depth = 2;
                i += 3;
                continue;
            }
            bool shapeOrTuning = Music.ChordShapes.StartsShape(w.Text)
                || Tablature.Tunings.Names.Contains(w.Text)
                || Music.ChordShapes.CaseCorrected(w.Text) != null;
            if (shapeOrTuning)
            {
                if (symbol != null)
                    entryWords.Add((w.Text, w.Span));
                else if (!skipping)
                    Warn(w.Span, DiagnosticCodes.ChordDiagramNotDrawn,
                        $"'{w.Text}' comes before any chord name - a table entry is the chord's name, "
                        + "then its shapes: Cm7 x35343. It is not used.");
                i++;
                continue;
            }
            // A chord symbol: the entry before it is complete.
            Flush();
            if (Music.ChordStructure.TryParseChordEntry(w.Text, out var parsed))
            {
                symbol = w.Text;
                symbolSpan = w.Span;
                chord = parsed;
                skipping = false;
            }
            else
            {
                Warn(w.Span, DiagnosticCodes.ChordDiagramNotDrawn,
                    $"'{w.Text}' is not a chord symbol (Cm7, F#m7-5/A) - a table entry is the chord's "
                    + "name, then its shapes: Cm7 x35343. It is not used.");
                skipping = true;
            }
            i++;
        }
        Flush();
        if (depth > 0)
        {
            Error(values[open].Span, $"This '{key}' shape table has no closing '}}'.");
            return null;
        }
        if (i < words.Count)
        {
            Error(words[i].Span, $"'{words[i].Text}' comes after the shape table's closing '}}' - the table "
                + $"ends the '{key}' entry.");
            return null;
        }

        // A section no `section NAME { }` declares: its entries apply nowhere.
        if (sectionSpans.Count > 0)
        {
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in root.KindSites(SyntaxKind.SectionDeclaration))
                if (node is SectionDeclarationSyntax declared)
                    known.Add(declared.SectionName);
            foreach (var (name, span) in sectionSpans)
                if (!known.Contains(name))
                    Warn(span, DiagnosticCodes.LayoutEntryBadValue,
                        $"No section is named '{name}', so its entries apply nowhere."
                        + (known.Count > 0 ? " Sections: " + string.Join(", ", known.OrderBy(k => k, StringComparer.Ordinal)) + "." : ""));
        }
        return refused ? null : new Music.ChordShapeTable(song.ToImmutable(), sections.ToImmutable());
    }

    /// <summary>The tokens from <paramref name="from"/> on, re-joined by ADJACENCY into words
    /// (a chords row's rule: <c>C#m7-5/G</c>, <c>8xx88-11</c> are one word each); a brace is
    /// always a word of its own.</summary>
    private static List<(string Text, TextSpan Span)> TableWords(IReadOnlyList<SyntaxTokenNode> values, int from)
    {
        var words = new List<(string Text, TextSpan Span)>();
        var sb = new System.Text.StringBuilder();
        int start = -1, end = -1;
        void Flush()
        {
            if (sb.Length > 0)
                words.Add((sb.ToString(), new TextSpan(start, end - start)));
            sb.Clear();
            start = -1;
        }
        for (int i = from; i < values.Count; i++)
        {
            var t = values[i];
            if (t.Kind is SyntaxKind.OpenBrace or SyntaxKind.CloseBrace)
            {
                Flush();
                words.Add((t.Text, t.Span));
                continue;
            }
            if (t.Text.Length == 0)
                continue;
            if (sb.Length > 0 && t.Span.Start != end)
                Flush();
            if (start < 0)
                start = t.Span.Start;
            sb.Append(t.Text);
            end = t.Span.Start + t.Text.Length;
        }
        Flush();
        return words;
    }

    /// <summary>The tunings a table's shapes are checked on (LYS1039): the layout's own when it
    /// names one, else the guitar and every fretted instrument the file's parts play — the
    /// tunings a diagram of this score can draw on (<see cref="ChordDiagramsKey.Resolve"/>).</summary>
    private static List<TuningType> CheckTunings(SyntaxNode root, string? tuningWord)
    {
        var tunings = new List<TuningType>();
        if (tuningWord != null)
        {
            tunings.Add(Tablature.Tunings.Parse(tuningWord));
            return tunings;
        }
        tunings.Add(TuningType.Guitar);
        foreach (var part in TopLevelNodes.OfRoot<PartDeclarationSyntax>(root))
            if (PartHeaderDefaults.Read(part).FrettedTuning is { } fretted && !tunings.Contains(fretted))
                tunings.Add(fretted);
        return tunings;
    }

    // voltaBracket all | line | N — exactly one value (VoltaBracketLength, owner's design
    // 2026-09-28). Its own reader because the third spelling is a number, which ReadOneWord's
    // closed list cannot hold.
    private static LayoutPlan ReadVoltaBracket(
        LayoutPlan plan, LayoutDeclarationSyntax.Entry entry, TextSpan keySpan, List<Problem> found)
    {
        const string key = VoltaBracketLength.Key;
        string takes = $"'{key}' {VoltaBracketLength.Takes}";
        if (entry.Values.Count == 0)
        {
            found.Add(new Problem(keySpan, DiagnosticCodes.LayoutEntryBadValue,
                takes + $" — e.g. '{key} {VoltaBracketLength.LineWord}' or '{key} 2'.", IsError: true));
            return plan;
        }
        var word = entry.Values[0];
        if (VoltaBracketLength.Parse(word.Text) is not { } length)
        {
            found.Add(new Problem(word.Span, DiagnosticCodes.LayoutEntryBadValue,
                $"'{word.Text}' is not a value of '{key}'."
                + (VoltaBracketLength.CaseOnlyMatch(word.Text) is { } canonical
                    ? $" Values are case-sensitive: write '{canonical}'."
                    : " " + takes + "."), IsError: true));
            return plan;
        }
        if (entry.Values.Count > 1)
        {
            found.Add(new Problem(entry.Values[1].Span, DiagnosticCodes.LayoutEntryBadValue,
                takes + $" — one value; '{entry.Values[1].Text}' is extra.", IsError: true));
            return plan;
        }
        return plan with { VoltaBracket = length };
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

    /// <summary>The key <paramref name="word"/> spells, or null. Case-sensitive, like every
    /// key (owner's decision 2026-09-27).</summary>
    private static string? Canonical(string word)
    {
        foreach (var candidate in AllKeySpellings())
            if (word.Equals(candidate, StringComparison.Ordinal))
                return candidate;
        return null;
    }
}
