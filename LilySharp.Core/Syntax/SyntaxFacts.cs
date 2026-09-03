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
    /// that one reader passes a starting slot (<see cref="NetOctaveMarksFrom"/>) and the
    /// exception is written down here rather than discovered by whoever adds the eighth.
    /// </remarks>
    public static int NetOctaveMarks(SyntaxNode node) => NetOctaveMarksFrom(node, 0);

    /// <summary>
    /// <see cref="NetOctaveMarks"/> counted from <paramref name="firstSlot"/> onward, for
    /// the one node whose <c>,</c> tokens are not all marks: a volta ending's range
    /// separator (<c>[1,3. B]</c>) is a Comma standing before the section name, so the
    /// whole-node scan would read it as an octave down.
    /// </summary>
    public static int NetOctaveMarksFrom(SyntaxNode node, int firstSlot)
    {
        int offset = 0;
        for (int i = firstSlot; i < node.SlotCount; i++)
        {
            if (node.GetChild(i) is not SyntaxTokenNode t)
                continue;
            if (t.Kind == SyntaxKind.Apostrophe)
                offset++;
            else if (t.Kind == SyntaxKind.Comma)
                offset--;
        }
        return offset;
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
            var text = t.Text;
            return text.Length >= 2 && text.StartsWith("\"") && text.EndsWith("\"")
                ? text.Substring(1, text.Length - 2)
                : text;
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

    /// <summary>
    /// The token kinds that can spell a PART NAME: a plain identifier, or one of the four
    /// clef words, which are legal part names (<c>part bass { … }</c>).
    /// </summary>
    /// <remarks>
    /// One home for the rule, shared by the parser (which decides what to consume) and the
    /// render nodes (which decide what counts as a member). They must agree: a container
    /// that KEEPS a rejected token so its width survives would otherwise hand that token
    /// back as a part name.
    /// </remarks>
    public static bool IsPartNameKind(SyntaxKind kind) => kind is
        SyntaxKind.Identifier or
        SyntaxKind.BassKeyword or SyntaxKind.TrebleKeyword or
        SyntaxKind.AltoKeyword or SyntaxKind.TenorKeyword;

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
    private static readonly HashSet<string> ArgumentTakingAnnotations =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "fig", "chord", "finger", "bend", "notehead", "frame", "text",
            "mark", "feather", "pluck", "arpeggio",
            // @textSpan("poco rit.") — the general text spanner; the argument is the text it
            // prints. The sugar spellings (@rit, @accel, @rall) take no argument and are
            // NOT here: each is this annotation with the argument already filled in.
            "textSpan",
            // Also plain marks on their own (@ottava, @ds): they are in the list so
            // the argument form keeps parsing, since the rule below would otherwise
            // hand their '(' to the slur.
            "ottava", "quindicesima", "ds", "dc", "to",
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
    /// Whether <paramref name="name"/> is one of the names that CAN take a
    /// parenthesised argument — the vocabulary above, without the "unknown names read
    /// one too" arm. A diagnostic uses this to tell a reader who wrote the name bare
    /// (or dotted) what the spelling is, instead of reporting the name as unknown.
    /// </summary>
    public static bool IsArgumentTakingAnnotationName(string name)
        => ArgumentTakingAnnotations.Contains(name);
}
