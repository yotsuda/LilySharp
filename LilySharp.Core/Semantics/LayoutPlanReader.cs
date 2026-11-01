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
            ["Dots.padding"] = ("0.45", "0.6"),
            ["Accidental.rightPadding"] = ("0.15", "0.25"),
            ["NoteHead.scale"] = ("1.0", "1.1"),
            ["BarLine.thinThickness"] = ("1.9", "2.5"),
            ["BarLine.thickThickness"] = ("6.0", "7"),
            // LilyPond's font-size step, so the one key that takes a SIGN (ReadStyle joins it).
            ["cueSize"] = ("-4", "-2"),
        };

    /// <summary>The words each key takes, for messages and for completion.</summary>
    internal static IReadOnlyList<string> ValueWords(string key) => Canonical(key) switch
    {
        MarkArrangement.Property => MarkArrangement.Modes,
        BarNumberPolicy.Key => BarNumberPolicy.Words,
        AccidentalStyles.Key => AccidentalStyles.Words,
        SectionLabels.Key => SectionLabels.Words,
        SectionLabelAligns.Key => SectionLabelAligns.Words,
        PartCombineTexts.Key => PartCombineTexts.Words,
        ChordQualityStyles.Key => ChordQualityStyles.Words,
        MinorChords.Key => MinorChords.Words,
        ChordDiagramsKey.Key => ChordDiagramsKey.Words,
        ChordTuningKey.Key => ChordTuningKey.Words,
        ChordNamesKey.Key => ChordNamesKey.Words,
        ChordListKey.Key => ChordListKey.Words,
        CompressRestsKey.Key => CompressRestsKey.Words,
        VoltaBracketLength.Key => VoltaBracketLength.Words,
        OttavaLineKey.Key => OttavaLineKey.Words,
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
    /// switches it spelled correctly. ⚠️ The BLOCK's reading alone: the file's capo and shape
    /// table are not on it (<see cref="Resolve"/> lays them on, <see cref="ChordShapesDirective.Apply"/>).
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
        // A score's bare `layout { … }` (2026-10-09): the file's unnamed default with the
        // score's entries laid over it, read as one merged block like a named reference is.
        bool bare = reference.NameToken == null && reference.IsBlock;
        var declaration = bare
            ? FileDefault(root)
            : TryResolve(root, reference, out var named, out _) ? named : null;
        if (declaration == null && !bare)
            return fallback;
        var discard = new List<Problem>();
        var plan = declaration != null ? ReadEntriesInto(LayoutPlan.Default, declaration, discard) : LayoutPlan.Default;
        if (reference.IsBlock)
            plan = ReadEntriesInto(plan, reference, discard);
        return plan;
    }

    /// <summary>
    /// The plan a score lays out by, read the way the collector reads it: the score's own
    /// reference when it writes one, else the file's unnamed default, else
    /// <see cref="LayoutPlan.Default"/> — with the file's capo and shape table laid on it
    /// (<see cref="ChordShapesDirective.Apply"/>). The twin asks this so it and the page cannot
    /// read two plans.
    /// </summary>
    internal static LayoutPlan Resolve(SyntaxNode root, RenderDeclarationSyntax? render)
    {
        var plan = FilePlan(root);
        if (render != null)
        {
            // The LAST reference wins, like every repeated single-value setting.
            LayoutDeclarationSyntax? reference = null;
            foreach (var node in render.DescendantNodes())
                if (node is LayoutDeclarationSyntax l)
                    reference = l;
            if (reference != null)
                plan = ReadReference(root, reference, plan);
        }
        // The piece's data the plan carries for every reader — the file's capo and shape table
        // (ChordShapesDirective.Apply) — laid on LAST, over whatever block the score chose: no
        // layout block sets them (owner's grammar 2026-10-31).
        return ChordShapesDirective.Apply(root, plan);
    }

    /// <summary>
    /// <see cref="Resolve(SyntaxNode, RenderDeclarationSyntax?)"/> for a caller holding the
    /// PARSED score (<see cref="RenderSpec.LayoutRef"/> is that same last reference, read by
    /// <c>RenderSpecParser.Parse</c>) — the MIDI, which plays one score and reads its capo.
    /// </summary>
    internal static LayoutPlan ResolveFor(SyntaxNode root, RenderSpec? spec)
    {
        var plan = FilePlan(root);
        if (spec?.LayoutRef is { } reference)
            plan = ReadReference(root, reference, plan);
        return ChordShapesDirective.Apply(root, plan);
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
                SectionLabelAligns.Key => ReadOneWord(plan, entry, span, found, SectionLabelAligns.Key,
                    SectionLabelAligns.Words, w => SectionLabelAligns.Find(w) is { } a
                        ? plan with { SectionLabelAlign = a } : null),
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
                OttavaLineKey.Key => ReadOneWord(plan, entry, span, found, OttavaLineKey.Key,
                    OttavaLineKey.Words, w => OttavaLineKey.Find(w) is { } b
                        ? plan with { Style = plan.EngravingStyle with { OttavaWave = b } } : null),
                // Which chords draw a diagram, and the tuning a part-less chord draws on — two
                // keys, one question each (owner's grammar 2026-10-31). The old one-key form
                // (a tuning word, `capo N`, a table) is refused naming the new spelling.
                ChordDiagramsKey.Key => ReadChordDiagrams(plan, entry, span, found),
                ChordTuningKey.Key => ReadChordTuning(plan, entry, span, found),
                // What a name shows under a capo (ChordNameMode) — the third half of the
                // chord spelling, beside the quality's vocabulary and the minor root's case.
                ChordNamesKey.Key => ReadOneWord(plan, entry, span, found, ChordNamesKey.Key,
                    ChordNamesKey.Words, w => ChordNamesKey.Find(w) is { } mode
                        ? plan with { Chords = plan.Chords with { Names = mode } } : null),
                // The chord list at the score's head (ChordListKey).
                ChordListKey.Key => ReadOneWord(plan, entry, span, found, ChordListKey.Key,
                    ChordListKey.Words, w => ChordListKey.Find(w) is { } on
                        ? plan with { ChordList = on } : null),
                // Whole-bar rests written one after another, engraved as one (CompressRestsKey).
                CompressRestsKey.Key => ReadOneWord(plan, entry, span, found, CompressRestsKey.Key,
                    CompressRestsKey.Words, w => CompressRestsKey.Find(w) is { } on
                        ? plan with { CompressRests = on } : null),
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
                "Dots.padding" => ReadStyle(plan, entry, span, found, (s, v) => s with { DotPadding = v[0] }),
                "Accidental.rightPadding" => ReadStyle(plan, entry, span, found, (s, v) => s with { AccidentalRightPadding = v[0] }),
                "NoteHead.scale" => ReadStyle(plan, entry, span, found, (s, v) => s with { NoteHeadScale = v[0] }),
                "BarLine.thinThickness" => ReadStyle(plan, entry, span, found, (s, v) => s with { BarLineHairThickness = v[0] }),
                "BarLine.thickThickness" => ReadStyle(plan, entry, span, found, (s, v) => s with { BarLineThickThickness = v[0] }),
                // A cue's size, LilyPond's font-size step (owner's decision 2026-10-31, OMR #13).
                "cueSize" => ReadStyle(plan, entry, span, found, (s, v) => s with { CueFontSizeStep = v[0] }),
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
    /// be 0 but not both, and <c>Beam.damping</c> (no damping, as LilyPond's) and the two gaps
    /// <c>Dots.padding</c> and <c>Accidental.rightPadding</c> may be 0. No unit:
    /// each is a multiple of a line thickness, staff spaces or a factor (Svg.EngravingStyle says
    /// which).
    /// </summary>
    private static LayoutPlan ReadStyle(LayoutPlan plan, LayoutDeclarationSyntax.Entry entry, TextSpan keySpan,
        List<Problem> found, Func<Svg.EngravingStyle, double[], Svg.EngravingStyle> set)
    {
        string example = NumberKeys[entry.Key].Example;
        int count = example.Split(' ').Length;
        bool zeroReads = entry.Key is "Beam.damping" or "Dots.padding" or "Accidental.rightPadding";
        // cueSize is LilyPond's font-size step: a sign and a number (the block keeps the `-` as a
        // token of its own), −8 to 0 — nothing larger than the staff's own size, nothing smaller
        // than a quarter of it (magstep(−8) = 0.40).
        bool signedStep = entry.Key == "cueSize";
        string takes = signedStep
            ? $"'{entry.Key}' takes LilyPond's font-size step, -8 to 0 (-4 is the default, 0.63 of the staff's size; -2 is 0.79): {entry.Key} {example}."
            : count == 1
            ? $"'{entry.Key}' takes a {(zeroReads ? "number, 0 or more" : "positive number")}, no unit: {entry.Key} {example}."
            : $"'{entry.Key}' takes {count} numbers, not both 0 and none below 0: {entry.Key} {example}.";
        var values = new double[Math.Max(count, entry.Values.Count)];
        bool numbers;
        if (signedStep)
        {
            var tokens = entry.Values;
            numbers = tokens.Count is 1 or 2
                && tokens[^1].Kind is SyntaxKind.IntegerLiteral or SyntaxKind.DecimalLiteral
                && (tokens.Count == 1 || tokens[0].Kind == SyntaxKind.Minus)
                && double.TryParse(tokens[^1].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out values[0]);
            if (numbers && tokens.Count == 2)
                values[0] = -values[0];
            if (numbers)
                numbers = values[0] >= -8 && values[0] <= 0;
        }
        else
        {
            numbers = entry.Values.Count == count;
            for (int i = 0; numbers && i < count; i++)
                numbers = entry.Values[i].Kind is SyntaxKind.IntegerLiteral or SyntaxKind.DecimalLiteral
                    && double.TryParse(entry.Values[i].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]);
            if (numbers)
                numbers = count == 1 ? values[0] > 0 || (zeroReads && values[0] == 0) : values.All(v => v >= 0) && values.Any(v => v > 0);
        }
        if (!numbers)
        {
            found.Add(new Problem(entry.Values.Count > 0 ? entry.Values[0].Span : keySpan,
                DiagnosticCodes.LayoutEntryBadValue, takes, IsError: true));
            return plan;
        }
        return plan with { Style = set(plan.EngravingStyle, values).Wrote(WrittenKey(entry.Key)) };
    }

    /// <summary>The style property <paramref name="key"/> writes, among those a music font's
    /// <c>engravingDefaults</c> speaks for — what the font then leaves alone
    /// (<see cref="Svg.EngravingStyle.Written"/>).</summary>
    private static Svg.EngravingKeys WrittenKey(string key) => key switch
    {
        "StaffLine.thickness" => Svg.EngravingKeys.StaffLineThickness,
        "LedgerLine.thickness" => Svg.EngravingKeys.LedgerLineThickness,
        "LedgerLine.lengthFraction" => Svg.EngravingKeys.LedgerLengthFraction,
        "Stem.thickness" => Svg.EngravingKeys.StemThickness,
        "Beam.thickness" => Svg.EngravingKeys.BeamThickness,
        "BarLine.thinThickness" => Svg.EngravingKeys.BarLineThinThickness,
        "BarLine.thickThickness" => Svg.EngravingKeys.BarLineThickThickness,
        _ => Svg.EngravingKeys.None,
    };

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

    // chordDiagrams written | none | all — exactly one word (ChordDiagramScope; owner's grammar
    // 2026-10-31). Any other word — the tuning words, `capo`, a table the key took before that
    // day — is a wrong word like any other: no old form is recognized (owner's decision the same
    // day: no backward compatibility, and no machinery for it).
    private static LayoutPlan ReadChordDiagrams(
        LayoutPlan plan, LayoutDeclarationSyntax.Entry entry, TextSpan keySpan, List<Problem> found)
        => ReadOneWord(plan, entry, keySpan, found, ChordDiagramsKey.Key, ChordDiagramsKey.Words,
            w => ChordDiagramsKey.Find(w) is { } scope ? plan with { ChordDiagrams = scope } : null);

    // chordTuning guitar | ukulele | mandolin | … — exactly one word, the tuning vocabulary
    // (ChordTuningKey). Its own reader for the message: the vocabulary is thirty words, so the
    // "takes" sentence names the commonest and points at the rest rather than listing them
    // (ReadOneWord would).
    private static LayoutPlan ReadChordTuning(
        LayoutPlan plan, LayoutDeclarationSyntax.Entry entry, TextSpan keySpan, List<Problem> found)
    {
        string key = ChordTuningKey.Key;
        string takes = $"'{key}' takes a tuning name (guitar, ukulele, mandolin, guitarDropD, ... - the words a tab's 'tuning' takes): "
            + "the tuning a chord with no fretted part draws its diagram on";
        if (entry.Values.Count == 0)
        {
            found.Add(new Problem(keySpan, DiagnosticCodes.LayoutEntryBadValue,
                takes + $" - e.g. '{key} ukulele'.", IsError: true));
            return plan;
        }
        var word = entry.Values[0];
        if (!ChordTuningKey.IsTuningWord(word.Text))
        {
            string hint = Tablature.Tunings.Names.FirstOrDefault(n => n.Equals(word.Text, StringComparison.OrdinalIgnoreCase)) is { } canonical
                ? $" Values are case-sensitive: write '{canonical}'."
                : " " + takes + ".";
            found.Add(new Problem(word.Span, DiagnosticCodes.LayoutEntryBadValue,
                $"'{word.Text}' is not a value of '{key}'." + hint, IsError: true));
            return plan;
        }
        if (entry.Values.Count > 1)
        {
            found.Add(new Problem(entry.Values[1].Span, DiagnosticCodes.LayoutEntryBadValue,
                takes + $" - one word; '{entry.Values[1].Text}' is extra.", IsError: true));
            return plan;
        }
        return plan with { ChordTuning = word.Text };
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
