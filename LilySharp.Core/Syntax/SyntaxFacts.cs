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

using System;
using System.Collections.Generic;
using System.Linq;

namespace LilySharp.Core.Syntax;

/// <summary>
/// Single source of truth for the token-set predicates (pitch, barline, clef,
/// dynamic) that the parser and the syntax layer classify tokens by. These sets
/// were previously re-spelled at ~10 sites in <c>Parser.cs</c> and independently
/// in <c>DynamicSyntax.Level</c>; centralizing them removes the drift risk (the
/// dynamic sets had already diverged from one another).
/// </summary>
internal static class SyntaxFacts
{
    /// <summary>
    /// Net octave shift from a node's own trailing marks: <c>'</c> counts +1, <c>,</c>
    /// counts -1, everything else nothing.
    /// </summary>
    /// <remarks>
    /// ONE SENTENCE, SEVEN READERS. A pitch (<c>c'</c>), a scale degree (<c>3,</c>), a chord
    /// (<c>&lt;c e g&gt;,</c>), an arpeggio (<c>&lt;&lt; c e g &gt;&gt;'</c>), a phrase
    /// reference (<c>Chorus'</c>) and — since 2026-08-31 — a SECTION reference (<c>~B'</c>)
    /// and a volta ending (<c>[1. B']</c>) all spell the same shift the same way, and each
    /// of them used to count it with its own copy of this loop. The copies differed only in
    /// where they started (slot 0 or slot 1), which was a distinction without a difference:
    /// a member pitch's own marks live inside that member's node, so for those six a node's
    /// marks are its only direct <c>'</c>/<c>,</c> TOKEN children and scanning every slot is
    /// the same answer. ⚠️ CHECKED, not reasoned — the claim is a claim about five green
    /// CONSTRUCTORS, and all five put a non-mark token at slot 0
    /// (<c>PitchGreen</c> pitchToken, <c>ScaleDegreeGreen</c> degree, <c>ChordGreen</c>
    /// openAngle, <c>ArpeggioGreen</c> openAngles, <c>VariableReferenceGreen</c> name), with
    /// their members held as NODES rather than tokens. A green whose slot 0 could be a mark
    /// would break the fold silently, so this is where the six are named. ⚠️ THE SEVENTH BROKE THAT: a volta ending's RANGE SEPARATOR
    /// (<c>[1,3. B]</c>) is a Comma token of its own, standing before the section name — so
    /// that one reader passes a starting slot (<see cref="NetOctaveMarksFrom(SyntaxNode, int)"/>) and the
    /// exception is written down here rather than discovered by whoever adds the eighth.
    /// </remarks>
    public static int NetOctaveMarks(SyntaxNode node) => NetOctaveMarksFrom(node, 0);

    /// <summary>
    /// <see cref="NetOctaveMarks"/> counted from <paramref name="firstSlot"/> onward, for
    /// the one node whose <c>,</c> tokens are not all marks: a volta ending's range
    /// separator (<c>[1,3. B]</c>) is a Comma standing before the section name, so the
    /// whole-node scan would read it as an octave down.
    /// </summary>
    /// <remarks>
    /// Counted on the GREEN slots: a mark is a token, and a token's kind is its green's,
    /// so no red token has to exist to be counted. Session 520 moved it here from the red
    /// children — the collector counts every note's marks once a keystroke, and a bass
    /// book spells most notes with <c>,</c> (409 Comma reds a keystroke over the owner's
    /// corpus, all built to be looked at once).
    /// </remarks>
    public static int NetOctaveMarksFrom(SyntaxNode node, int firstSlot)
        => NetOctaveMarksFrom(node.Green, firstSlot);

    /// <summary>The fold itself, on a green node — what a <see cref="PitchReading"/> asks
    /// with no red pitch at all (session 521).</summary>
    internal static int NetOctaveMarksFrom(InternalSyntax.GreenNode green, int firstSlot)
    {
        int offset = 0;
        for (int i = firstSlot; i < green.SlotCount; i++)
        {
            if (green.GetSlot(i) is not { IsToken: true } t)
                continue;
            if (t.Kind == SyntaxKind.Apostrophe)
                offset++;
            else if (t.Kind == SyntaxKind.Comma)
                offset--;
        }
        return offset;
    }

    /// <summary>
    /// The pass numbers a volta bracket names: <c>1.</c> is {1}, a range <c>1-3.</c> is
    /// {1, 2, 3}, a list <c>1,3.</c> is {1, 3}.
    /// </summary>
    /// <remarks>
    /// ONE SENTENCE, TWO READERS: the inline ending in the music (<c>[1-2. g4]</c>,
    /// <see cref="InlineVoltaSyntax.Numbers"/>) and the form ending (<c>[1-2. B]</c>,
    /// <see cref="FormAlternativeSyntax.Numbers"/>) are the same bracket with the same three
    /// spellings, held in the same three slots. Until 2026-09-29 only the inline one spelled
    /// this fold; the form's readers each read the slots for themselves (the MIDI: the first
    /// number only; Split Sections: ranges but not lists) — see <c>Semantics.RepeatPasses</c>.
    /// </remarks>
    /// <remarks>
    /// NEVER THROWS, because it is read on trees the parser has already complained about (the
    /// validators and the editor's preview run on every keystroke): a number the parser could
    /// not read — the empty token its recovery leaves for <c>[. B]</c> — names no pass, and
    /// the passes written with points, <c>[1.3.</c> (LYS0037, one decimal token), are read
    /// as the list they were meant to be, so the one parser error is not followed by a
    /// "no ending plays pass 1" it caused. Until session 715 both threw a FormatException out
    /// of the validators, and <c>lysc check</c> printed that instead of any diagnostic.
    /// </remarks>
    public static IEnumerable<int> VoltaPassNumbers(SyntaxTokenNode number, SyntaxTokenNode? separator, SyntaxTokenNode? endNumber,
        IReadOnlyList<SyntaxTokenNode>? morePasses = null)
    {
        if (!int.TryParse(number.Text, out int start))
        {
            if (number.Kind == SyntaxKind.DecimalLiteral)
                foreach (var part in number.Text.Split('.'))
                    if (int.TryParse(part, out int pass))
                        yield return pass;
            yield break;
        }
        if (separator != null && endNumber != null && int.TryParse(endNumber.Text, out int end))
        {
            if (separator.Kind == SyntaxKind.Minus)
            {
                for (int n = start; n <= end; n++)
                    yield return n;
            }
            else // comma list: [1,3. …], and on — [1,3,5. …]
            {
                yield return start;
                yield return end;
                if (morePasses != null)
                    foreach (var more in morePasses)
                        if (int.TryParse(more.Text, out int pass))
                            yield return pass;
            }
        }
        else
        {
            yield return start;
        }
    }

    /// <summary>
    /// A list ending's third and later numbers — <c>3</c>, <c>5</c> of <c>[1,2,3,5.</c> — the
    /// number tokens the parser holds between the end number and the point (the commas are
    /// skipped). Empty for every other ending.
    /// </summary>
    /// <remarks>Read by kind from slot 4 up to the point, because the list has no fixed length
    /// (Parser.ParseVoltaPasses); the slots after the point are the ending's body.</remarks>
    internal static IReadOnlyList<SyntaxTokenNode> VoltaMorePasses(SyntaxNode ending)
    {
        List<SyntaxTokenNode>? more = null;
        for (int i = 4; i < ending.SlotCount; i++)
        {
            if (ending.GetChild(i) is not SyntaxTokenNode token || token.Kind == SyntaxKind.Dot)
                break;
            if (token.Kind == SyntaxKind.IntegerLiteral)
                (more ??= []).Add(token);
        }
        return more ?? (IReadOnlyList<SyntaxTokenNode>)[];
    }

    /// <summary>An ending's passes as WRITTEN, with its point: <c>1.</c>, <c>1-3.</c>,
    /// <c>1,3,5.</c> (<c>PrintedText</c> is what the bracket prints).</summary>
    internal static string VoltaWrittenText(SyntaxTokenNode number, SyntaxTokenNode? separator,
        SyntaxTokenNode? endNumber, IReadOnlyList<SyntaxTokenNode> morePasses)
    {
        if (separator is null || endNumber is null)
            return $"{number.Text}.";
        var text = new System.Text.StringBuilder($"{number.Text}{separator.Text}{endNumber.Text}");
        foreach (var more in morePasses)
            text.Append(',').Append(more.Text);
        return text.Append('.').ToString();
    }

    /// <summary>
    /// The text a volta bracket PRINTS for the passes it names, as LilyPond prints it: the
    /// passes grouped into runs; a run of three or more is <c>1.–3.</c> (EN DASH), a shorter
    /// run each number with its point, <c>1. 2.</c>; the runs apart by a THIN SPACE — so
    /// <c>[1-2.</c> prints <c>1. 2.</c>, <c>[1-3.</c> <c>1.–3.</c>, <c>[1,3.</c> <c>1. 3.</c>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/output-lib.scm:2267-2290 volta-bracket-interface::calc-text —
    ///   group-into-ranges (scm/lily-library.scm:747-767), a range at or over
    ///   range-collapse-threshold (3, scm/define-grobs.scm:4299) as start "–" end, else each
    ///   number "N." joined by " ", the ranges joined by " ".
    /// Owner's decision 2026-09-30: the page printed the written spelling (<c>1-2.</c>,
    /// <c>1,3.</c>) until then. The diagnostics still QUOTE the written spelling
    /// (<see cref="FormAlternativeSyntax.VoltaText"/>).
    /// </remarks>
    public static string VoltaPrintedText(IEnumerable<int> passes)
    {
        const int RangeCollapseThreshold = 3;
        var sorted = passes.Distinct().Order().ToList();
        var runs = new List<string>();
        for (int i = 0; i < sorted.Count;)
        {
            int j = i;
            while (j + 1 < sorted.Count && sorted[j + 1] == sorted[j] + 1)
                j++;
            int start = sorted[i], end = sorted[j];
            runs.Add(end - start + 1 >= RangeCollapseThreshold
                ? $"{start}.–{end}."
                : string.Join(" ", Enumerable.Range(start, end - start + 1).Select(n => $"{n}.")));
            i = j + 1;
        }
        return string.Join(" ", runs);
    }

    /// <summary>
    /// The occurrence label written on a form item — the quoted string, unquoted — or null.
    /// </summary>
    /// <remarks>
    /// ONE SENTENCE, THREE SHAPES: a plain reference (<c>A "reprise"</c>), a silent one
    /// (<c>~A "reprise"</c>) and a volta ending (<c>[1. A "reprise"]</c>) all park the label
    /// the same way, and each used to find and unquote it for itself — at three different
    /// fixed indices, which is what made the silent one's label reachable only by the parser.
    /// A form item holds no OTHER string, so "the first StringLiteral child" is the whole
    /// rule and it needs no index to stay correct as slots move.
    /// </remarks>
    public static string? UnquotedLabel(SyntaxNode node)
    {
        for (int i = 0; i < node.SlotCount; i++)
        {
            if (node.GetChild(i) is not SyntaxTokenNode { Kind: SyntaxKind.StringLiteral } t)
                continue;
            return StringLiteral.Value(t.Text);
        }
        return null;
    }

    /// <summary>The seven diatonic pitch token kinds (<c>c d e f g a b</c>).</summary>
    public static bool IsPitchKind(SyntaxKind kind) => kind is
        SyntaxKind.PitchC or SyntaxKind.PitchD or SyntaxKind.PitchE or
        SyntaxKind.PitchF or SyntaxKind.PitchG or SyntaxKind.PitchA or
        SyntaxKind.PitchB;

    /// <summary>
    /// Any barline token, INCLUDING the dashed barline (<c>!</c>). A music stream
    /// accepts all of these. Use <see cref="IsMeasureBarlineKind"/> for the
    /// chord/lyric-row set, which excludes the dashed barline.
    /// </summary>
    public static bool IsBarlineKind(SyntaxKind kind) => kind is
        SyntaxKind.Bar or SyntaxKind.DoubleBar or SyntaxKind.FinalBar or
        SyntaxKind.DashedBar or SyntaxKind.RepeatStartBar or
        SyntaxKind.RepeatEndBar or SyntaxKind.RepeatBothBar;

    /// <summary>
    /// Barlines that delimit a chord-block or lyric measure. EXCLUDES the dashed
    /// barline — chord and lyric rows never treated <c>!</c> as a measure break,
    /// a behavior preserved verbatim from the original per-site predicates.
    /// </summary>
    public static bool IsMeasureBarlineKind(SyntaxKind kind) => kind is
        SyntaxKind.Bar or SyntaxKind.DoubleBar or SyntaxKind.FinalBar or
        SyntaxKind.RepeatStartBar or SyntaxKind.RepeatEndBar or
        SyntaxKind.RepeatBothBar;

    /// <summary>
    /// The barlines that REPEAT — <c>|:</c>, <c>:|</c> and the fused <c>:|:</c>. The subset of
    /// <see cref="IsBarlineKind"/> that changes the playing ORDER rather than drawing a
    /// division, which is the line the language draws: these may be written only inside a
    /// <c>form { … }</c> (<see cref="DiagnosticCodes.RepeatStructureOutsideForm"/>), while
    /// <c>|</c> <c>||</c> <c>|.</c> <c>!</c> stay free anywhere a barline is legal.
    /// </summary>
    public static bool IsRepeatBarlineKind(SyntaxKind kind) => kind is
        SyntaxKind.RepeatStartBar or SyntaxKind.RepeatEndBar or
        SyntaxKind.RepeatBothBar;

    /// <summary>
    /// The five clef-name keywords accepted by a <c>clef</c> declaration
    /// (treble, bass, alto, tenor, treble_8). NOTE: this is deliberately narrower
    /// than <c>PartReferenceFinder.IsClefKeyword</c>, which also accepts
    /// <see cref="SyntaxKind.Treble8UpKeyword"/>; the clef-declaration grammar
    /// does not, and that difference is preserved.
    /// </summary>
    public static bool IsClefKeyword(SyntaxKind kind) => kind is
        SyntaxKind.TrebleKeyword or SyntaxKind.BassKeyword or
        SyntaxKind.AltoKeyword or SyntaxKind.TenorKeyword or
        SyntaxKind.Treble8Keyword;

    /// <summary>
    /// The same five clefs as WORDS — the part header's eleven filtered by
    /// <see cref="IsClefKeyword"/>, so this can neither name a word the parser would reject
    /// nor miss one it accepts.
    /// </summary>
    /// <remarks>
    /// ⚠️ Derived rather than written down, and deliberately so: until 2026-08-19 these five
    /// words were spelled out at FOUR sites that nothing connected — this predicate, the
    /// "Expected clef name (…)" message in <c>Parser.ParseClefDeclaration</c>, GRAMMAR.md's
    /// <c>ClefName</c> production, and the editor's completion list. The editor's copy was the
    /// one that went wrong, and it went wrong by being RIGHT in the other position: it offered
    /// these five inside a part header, where eleven are legal.
    /// <c>treble^8</c> drops out on its own — it is stitched from three tokens rather than
    /// lexed as one keyword, so it never carries a clef kind.
    /// </remarks>
    public static IReadOnlyList<string> ClefNameVocabulary { get; } =
        [.. Semantics.SymbolCaseValidator.ClefValueVocabulary
            .Where(name => IsClefKeyword(Parser.Lexer.GetKeywordKind(name)))
            .OrderBy(name => name, StringComparer.Ordinal)];

    /// <summary>The six clef names a PART HEADER takes and a music block does not — the
    /// complement of <see cref="ClefNameVocabulary"/>. Named so that a diagnostic can tell a
    /// writer WHERE the word they used is legal without spelling the six a second time.</summary>
    public static IReadOnlyList<string> PartOnlyClefNameVocabulary { get; } =
        [.. Semantics.SymbolCaseValidator.ClefValueVocabulary
            .Except(ClefNameVocabulary, StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)];

    /// <summary>The three kinds a <c>repeat</c> takes (GRAMMAR.md: Repeat) — the words the
    /// parser's "Expected repeat type" message names and the editor offers after the keyword.
    /// The ORDER is the one both show. Published 2026-09-03 when the editor gained the value
    /// context; until then the message was the only enumeration and the completion snippet
    /// carried its own copy of the three in a Detail string.</summary>
    /// <remarks>⚠️ <c>volta</c> is deliberately absent: <c>repeat volta</c> is refused
    /// (LYS0006) in favour of the form's <c>|: … :|</c>.</remarks>
    public static IReadOnlyList<string> RepeatKindVocabulary { get; } = ["unfold", "percent", "tremolo"];

    /// <summary>The nine modes a <c>key</c> takes (GRAMMAR.md: Mode), in the order the
    /// parser's "Unknown mode" message names them and the editor offers them after the
    /// tonic. Published 2026-09-10: the parser tested the nine kinds one by one, the
    /// message spelled the nine words, and the editor's completion held a third copy.</summary>
    /// <remarks>
    /// ⚠️ Declared BEFORE <see cref="KeyModeKinds"/>, which is built from it — static
    /// initializers run in textual order.
    /// </remarks>
    public static IReadOnlyList<string> KeyModeVocabulary { get; } =
        ["major", "minor", "ionian", "dorian", "phrygian", "lydian", "mixolydian", "aeolian", "locrian"];

    /// <summary>The token kinds the nine mode words lex to — derived, so the predicate below
    /// can neither accept a kind the list has no word for nor refuse one it names.</summary>
    private static readonly HashSet<SyntaxKind> KeyModeKinds =
        [.. KeyModeVocabulary.Select(Parser.Lexer.GetKeywordKind)];

    /// <summary>True for the keyword kind of one of the nine modes — what
    /// <c>ParseKeySignature</c> accepts after the tonic.</summary>
    public static bool IsKeyModeKeyword(SyntaxKind kind) => KeyModeKinds.Contains(kind);

    /// <summary>
    /// The navigation marks (GRAMMAR.md: NavMark) as the writer spells them — the same bare
    /// words in a <c>form</c> and in a music stream: the two jump targets, the four
    /// instructions, and the four <c>al</c> forms. Published 2026-09-10 so the two
    /// completion lists that offer them read one list; until then the form's popup held
    /// the ten by hand and the music popup, whose grammar takes the same ten, offered none.
    /// </summary>
    /// <remarks>
    /// ⚠️ A LIST of spellings, not a derivation: <c>ParseNavigationMark</c> is structural
    /// (a first keyword, then optionally <c>al</c> and a target), so the ten are what that
    /// structure admits, written out once. NavigationMarkCompletionTests compiles every
    /// spelling in both positions.
    /// </remarks>
    public static IReadOnlyList<string> NavigationMarkVocabulary { get; } =
        ["segno", "coda", "to coda", "fine", "dc", "ds", "dc al fine", "dc al coda", "ds al fine", "ds al coda"];

    /// <summary>
    /// The keywords that open a render item in a <c>score { }</c> body (GRAMMAR.md:
    /// ScoreItem) — every branch of <c>ParseRenderItem</c> but the bare MIDI-only part name,
    /// which has no keyword. In the order the editor offers them: the staff and its groups,
    /// the other rows, then the score's own header and references.
    /// </summary>
    /// <remarks>
    /// ⚠️ A LIST of spellings, like <see cref="NavigationMarkVocabulary"/>: the parser
    /// dispatches on token kinds and cannot be asked for its branches. What holds the list to
    /// the parser is DocKeywordListTests, which asks every reserved word one at a time whether
    /// a score body gives it a branch and compares the answer with this list in both
    /// directions. Published 2026-09-10 (session 365): the editor's score-body popup was a
    /// hand-written copy of the fifteen, the shape that had drifted for the clefs and the part
    /// properties.
    /// </remarks>
    public static IReadOnlyList<string> ScoreItemKeywordVocabulary { get; } =
    [
        "staff", "grandStaff", "staffGroup", "choirStaff", "condensedStaff", "combinedStaff",
        "tab", "ossia", "chords", "lyrics",
        "title", "subtitle", "composer", "poet", "fonts", "paper", "layout",
    ];

    /// <summary>
    /// The keys a <c>layout { }</c> block takes (GRAMMAR.md: LayoutEntry) — <c>marks</c>
    /// and <c>barNumbers</c>. Spelled here, in Syntax, because the block's entry walker
    /// (<see cref="LayoutDeclarationSyntax.Entries"/>) needs to know a key from a value to
    /// cut the entries; the reader (<c>LayoutPlanReader</c>) reads the same list.
    /// </summary>
    public static IReadOnlyList<string> LayoutKeyVocabulary { get; } =
        ["markTempo", "barNumbers", "accidentals", "sectionLabels", "partCombineText",
         "chordQualities", "minorChords", "chordDiagrams", "chordNames", "chordList", "voltaBracket",
         // The engraving style (Svg.EngravingStyle, 2026-10-05): a number each, the dotted ones
         // a grob's property (`Stem.thickness 1.5`), cut by the entry walker as one key.
         "lineThickness", "StaffLine.thickness", "LedgerLine.thickness", "LedgerLine.lengthFraction",
         "Stem.thickness", "Stem.lengthFraction", "Beam.thickness", "Beam.damping", "Dots.padding", "Accidental.rightPadding",
         "BarLine.thinThickness", "BarLine.thickThickness"];

    /// <summary>True when <paramref name="word"/> is a layout key as written — keys are
    /// case-sensitive, like a paper key's (owner's decision 2026-09-27).</summary>
    public static bool IsLayoutKey(string word)
    {
        foreach (var key in LayoutKeyVocabulary)
            if (string.Equals(word, key, StringComparison.Ordinal))
                return true;
        return false;
    }

    /// <summary>The layout key <paramref name="word"/> differs from ONLY IN CASE, or null.
    /// The entry walker cuts an entry there too, so the reader can refuse the word as a key
    /// and name the spelling to write.</summary>
    public static string? LayoutKeyCaseOnlyMatch(string word)
    {
        foreach (var key in LayoutKeyVocabulary)
            if (!string.Equals(word, key, StringComparison.Ordinal)
                && string.Equals(word, key, StringComparison.OrdinalIgnoreCase))
                return key;
        return null;
    }

    /// <summary>
    /// The directives a <c>section { }</c> may carry beside (or instead of) its part cells
    /// (GRAMMAR.md: SectionSetting, plus the section-scoped OverrideDecl) — the words the
    /// parser's stray-item message names and the editor offers in a section header. In the
    /// order a writer reaches for them: the pickup, then the section-wide key / time / tempo,
    /// then the grob override.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE FIVE ARE THE WHOLE LIST (GRAMMAR.md §SectionItem): a setting that belongs to ONE
    /// part — clef, instrument, transpose, octave — is refused here (LYS1035 / LYS0030).
    /// <c>revert</c> and <c>once</c> parse in a section and are then refused by a validator
    /// (they belong in a music stream), so they are not in this list. Published 2026-09-10
    /// (session 365): the parser's message and the editor's section-header list each spelled
    /// the five by hand.
    /// </remarks>
    public static IReadOnlyList<string> SectionSettingVocabulary { get; } =
        ["partial", "key", "time", "tempo", "override"];

    /// <summary>
    /// The words that can NOT name a part or a section: the keywords that open a structure of
    /// their own where a part name may stand, and the section settings. Every other bare word
    /// — a clef word, a dynamic, a pitch letter, <c>q</c>, a drum name, <c>grace</c> — names a
    /// part (owner's decision 2026-10-03; until then only an identifier and the four clef
    /// words did, and <c>part p</c> / <c>phrase p</c> were "a reserved word — pick another
    /// name").
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY A DENY-LIST IS ENOUGH: every position a part name is written in has a keyword or a
    /// shape beside it — <c>part X</c>, <c>X { … }</c> in a section, <c>staff X</c>,
    /// <c>tab X</c>, <c>ossia X</c>, <c>condensedStaff { X Y }</c>, <c>sings X</c>, and the
    /// bare X of a score body — so a name never stands in a music stream where it could be
    /// read as a note. The two places the shape alone decides are the section body, where
    /// <c>X {</c> opens a part cell, and the score body, where a bare X is the MIDI-only part;
    /// the words below are the ones an <c>X {</c> or a bare X would otherwise mean there.
    /// </para>
    /// <para>
    /// The groups, in the order they are listed: the containers a section or score body
    /// dispatches on before it looks for a cell (<c>voice</c> … <c>fonts</c>); the words a
    /// music stream reads as a block opener when a <c>{</c> follows, which is exactly the
    /// shape of a part cell (<c>grace</c>, <c>acciaccatura</c>, <c>appoggiatura</c>,
    /// <c>cue</c>); the section settings and the directives a section or part body claims at
    /// the head of an item (<c>key</c> … <c>once</c>); the file-level words a score body
    /// claims before its bare-name arm (<c>using</c>, <c>title</c> … <c>poet</c>); and the
    /// words a FORM body reads as items of its own — the navigation marks and the break
    /// directives — since a section is named by the same rule and referenced bare in a form.
    /// </para>
    /// <para>
    /// ⚠️ WORDS, not kinds, and the kinds are DERIVED (<see cref="PartNameReservedKinds"/>)
    /// through the lexer's own table, so the list cannot name a word that is not a keyword and
    /// the parser's refusal message can print the same spellings a writer sees here.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> PartNameReservedVocabulary { get; } =
    [
        "voice", "lyrics", "chords", "section", "part", "phrase", "form", "score",
        "tab", "staff", "ossia", "grandStaff", "staffGroup", "choirStaff",
        "condensedStaff", "combinedStaff", "layout", "paper", "fonts",
        "grace", "acciaccatura", "appoggiatura", "cue",
        "key", "time", "tempo", "partial", "override", "revert", "once",
        "using", "title", "subtitle", "composer", "poet",
        "segno", "fine", "coda", "dc", "ds", "al", "to",
        "break", "noBreak", "pageBreak", "noPageBreak",
    ];

    /// <summary>The kinds of <see cref="PartNameReservedVocabulary"/>, read off the lexer.</summary>
    private static readonly HashSet<SyntaxKind> PartNameReservedKinds =
        [.. PartNameReservedVocabulary.Select(Parser.Lexer.GetKeywordKind)];

    /// <summary>
    /// Whether a token can spell a PART NAME (or a section's): any bare word — letters,
    /// digits and <c>_</c> (<see cref="IsBareWord"/>) — that does not start with a digit
    /// (a leading number is a duration or a scale degree; <c>ExpectPartName</c> names that
    /// mistake) and whose kind is not one of <see cref="PartNameReservedVocabulary"/>.
    /// </summary>
    /// <remarks>
    /// One home for the rule, shared by the parser (which decides what to consume) and the
    /// render nodes (which decide what counts as a member). They must agree: a container
    /// that KEEPS a rejected token so its width survives would otherwise hand that token
    /// back as a part name. Asked of the TEXT as well as the kind, for the reason
    /// <see cref="IsBareWord"/> gives: a list of admitted kinds has to be revisited every time
    /// a keyword is added, and the list this replaced admitted four clef words and refused
    /// the other seven for no reason anybody chose.
    /// </remarks>
    public static bool IsPartNameToken(SyntaxKind kind, string? text) =>
        IsBareWord(text) && !char.IsDigit(text![0]) && !PartNameReservedKinds.Contains(kind);

    /// <summary>
    /// A bare word: letters, digits and <c>_</c>, nothing else and not empty. Quoted strings,
    /// punctuation and synthetic zero-width tokens all fail it.
    /// </summary>
    /// <remarks>
    /// Asked of a token's TEXT rather than its kind, because "is this a word?" is a property of
    /// the spelling and a list of kinds answering it has to be revisited every time a keyword
    /// is added — which is how the tail of a hyphenated part-header value came to admit four
    /// clef words and refuse <c>soprano</c>.
    /// </remarks>
    public static bool IsBareWord(string? text) =>
        !string.IsNullOrEmpty(text) && text.All(c => char.IsLetterOrDigit(c) || c == '_');

    /// <summary>
    /// The eight dynamic token KINDS the lexer emits, mapped to their level.
    /// Returns <see cref="DynamicLevel.None"/> for any non-dynamic kind. The lexer
    /// remains the sole producer of these kinds; this is the single consumer-side
    /// mapping shared by the parse gate and the velocity lookup.
    /// </summary>
    public static DynamicLevel DynamicLevelForKind(SyntaxKind kind) => kind switch
    {
        SyntaxKind.DynamicPPP => DynamicLevel.PPP,
        SyntaxKind.DynamicPP => DynamicLevel.PP,
        SyntaxKind.DynamicP => DynamicLevel.P,
        SyntaxKind.DynamicMP => DynamicLevel.MP,
        SyntaxKind.DynamicMF => DynamicLevel.MF,
        SyntaxKind.DynamicF => DynamicLevel.F,
        SyntaxKind.DynamicFF => DynamicLevel.FF,
        SyntaxKind.DynamicFFF => DynamicLevel.FFF,
        _ => DynamicLevel.None
    };

    /// <summary>True if <paramref name="kind"/> is one of the eight dynamic token kinds.</summary>
    public static bool IsDynamicKind(SyntaxKind kind) => DynamicLevelForKind(kind) != DynamicLevel.None;

    /// <summary>
    /// Fixed-level dynamics recognized by TEXT rather than a dedicated token kind:
    /// the pitch-token <c>f</c>, the extended p*/f* families the lexer does not
    /// tokenize as Dynamic* kinds (<c>ppp…ppppp</c>, <c>fff…fffff</c>), and the
    /// accent dynamics (<c>fp sf sfz rf rfz fz sffz</c>) which lex as identifiers.
    /// This is the single source shared by both the parse gate
    /// (<see cref="IsDynamicText"/> reads the keys) and the velocity map
    /// (<c>DynamicSyntax.Level</c> reads the values).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, DynamicLevel> DynamicTextLevels =
        new Dictionary<string, DynamicLevel>
        {
            ["ppppp"] = DynamicLevel.PPPPP,
            ["pppp"] = DynamicLevel.PPPP,
            ["ppp"] = DynamicLevel.PPP,
            ["pp"] = DynamicLevel.PP,
            ["p"] = DynamicLevel.P,
            ["mp"] = DynamicLevel.MP,
            ["mf"] = DynamicLevel.MF,
            ["fp"] = DynamicLevel.FP,
            ["f"] = DynamicLevel.F,
            ["sf"] = DynamicLevel.SF,
            ["ff"] = DynamicLevel.FF,
            ["sfz"] = DynamicLevel.SFZ,
            ["rf"] = DynamicLevel.RF,
            ["rfz"] = DynamicLevel.RFZ,
            ["fz"] = DynamicLevel.FZ,
            ["sffz"] = DynamicLevel.SFFZ,
            ["fffff"] = DynamicLevel.FFFFF,
            ["ffff"] = DynamicLevel.FFFF,
            ["fff"] = DynamicLevel.FFF,
        };

    /// <summary>True if <paramref name="text"/> is a fixed-level dynamic name (see
    /// <see cref="DynamicTextLevels"/>).</summary>
    public static bool IsDynamicText(string text) => DynamicTextLevels.ContainsKey(text);

    /// <summary>
    /// The dynamic-spanner names (<c>cresc</c>, <c>decresc</c>, <c>dim</c>) the
    /// parser accepts as dynamics but which carry no fixed velocity level — they
    /// are resolved downstream as hairpins/text spanners.
    /// </summary>
    public static bool IsDynamicSpannerName(string text) => text is "cresc" or "decresc" or "dim";

    /// <summary>
    /// The annotations that read a parenthesised ARGUMENT — the closed vocabulary,
    /// one entry per family the collector actually consumes. Everything else that
    /// is a KNOWN annotation leaves the '(' alone, so it opens a slur.
    /// </summary>
    /// <remarks>
    /// Without a vocabulary the parser read '(' as an argument list after ANY
    /// identifier, because the name is resolved downstream, not here: so
    /// <c>c4@staccato (d4 e4 f4)</c> ate the whole slur group — three notes
    /// vanished from the bar and the only word about it was "Unknown annotation
    /// '@staccato(d 4 e 4 f 4)'". No book in the corpus writes that, so this is a
    /// trap rather than a live defect, but it is one a reader falls into by
    /// writing perfectly ordinary music.
    /// </remarks>
    // Case-sensitive, like every annotation name (owner's decision 2026-09-27).
    private static readonly HashSet<string> ArgumentTakingAnnotations =
        new(StringComparer.Ordinal)
        {
            "figuredBass", "chord", "finger", "bend", "notehead", "diagram", "text",
            "mark", "feather", "pluck", "arpeggio",
            // @textSpan("poco rit.") — the general text spanner; the argument is the text it
            // prints. The sugar spellings (@rit, @accel, @rall) take no argument and are
            // NOT here: each is this annotation with the argument already filled in.
            "textSpan",
            // Also plain marks on their own (@ottava, @ds): they are in the list so
            // the argument form keeps parsing, since the rule below would otherwise
            // hand their '(' to the slur.
            "ottava", "quindicesima", "ds", "dc", "to",
            // @todo / @todo("memo") — a plain mark too, for the same reason.
            "todo",
        };

    /// <summary>
    /// Whether <c>@name(</c> opens an argument list rather than a slur. Three
    /// classes: an argument-taking name reads the argument; a name known to take
    /// none (an articulation, a plain feature name, a bare mark) leaves the '('
    /// to the music, where it opens a slur; an UNKNOWN name reads the argument
    /// too — a typo like <c>@notehed(x)</c> is one mistake, and reading its
    /// argument keeps it one ("did you mean '@notehead(x)'?") instead of
    /// cascading into "Undefined variable or phrase: 'x'".
    /// </summary>
    public static bool AnnotationReadsAParenthesisedArgument(string name)
        => ArgumentTakingAnnotations.Contains(name)
            || !Semantics.AnnotationNameValidator.IsKnownPlainName(name);

    /// <summary>
    /// Whether <paramref name="name"/>'s argument is OPTIONAL — the name is complete on its
    /// own (<c>@ottava</c>, <c>@quindicesima</c>, <c>@arpeggio</c>, a bare <c>@chord</c>) and
    /// also takes one (<c>@ottava(bassa)</c>). For such a name a glued <c>(</c> is ambiguous:
    /// the argument, or the slur that starts on the same note (<c>e8@ottava( d c b)</c>).
    /// The parser tells them apart by what follows the <c>(</c> — an argument is written
    /// against it, a slur's next note after a space (user report 2026-09-26: the slur's
    /// whole run, <c>@!ottava</c> included, was read as the argument and dropped).
    /// </summary>
    public static bool AnnotationArgumentIsOptional(string name)
        => ArgumentTakingAnnotations.Contains(name)
           && (Semantics.AnnotationNameValidator.IsKnownPlainName(name)
               || name.Equals("chord", StringComparison.Ordinal));

    /// <summary>
    /// Whether <paramref name="name"/> is one of the names that CAN take a
    /// parenthesised argument — the vocabulary above, without the "unknown names read
    /// one too" arm. A diagnostic uses this to tell a reader who wrote the name bare
    /// (or dotted) what the spelling is, instead of reporting the name as unknown.
    /// </summary>
    public static bool IsArgumentTakingAnnotationName(string name)
        => ArgumentTakingAnnotations.Contains(name);

    /// <summary>The argument-taking names above, for <c>AnnotationNames</c>.</summary>
    public static IReadOnlyCollection<string> ArgumentTakingAnnotationNames => ArgumentTakingAnnotations;
}
