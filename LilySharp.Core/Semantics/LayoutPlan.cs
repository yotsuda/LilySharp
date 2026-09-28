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

using System.Collections.Generic;

namespace LilySharp.Core.Semantics;

/// <summary>
/// What a <c>layout { }</c> block asks for — the score-wide DISPLAY switches: closed
/// vocabularies that say how a class of symbol is drawn or arranged, with no unit and no
/// grob scope. The third block beside <c>fonts</c> (the faces) and <c>paper</c> (the
/// page's dimensions), and it takes the same two tiers: the unnamed top-level block is
/// the file's default, a named block is a per-score declaration a score references.
/// </summary>
/// <remarks>
/// <para>
/// The line against <c>paper</c> (user decision 2026-09-11): a quantity with a unit — a
/// length, a justification flag — is the page's and lives in <c>paper</c>; a switch that
/// picks one of a few drawings is the layout's and lives here. <c>indent</c>,
/// <c>raggedRight</c> and <c>spacingIncrement</c> stay in <c>paper</c> on that rule
/// (LilyPond itself accepts them in <c>\paper</c>). NOT an <c>override</c>: an override
/// reads a <c>once</c> / section scope that a whole-score switch would silently ignore.
/// </para>
/// <para>
/// Compared by value (a record of a bool and a small struct), so the incremental compiler
/// sheds its caches on a real change and not on a trivia edit — <c>LayoutOptions</c>'
/// contract.
/// </para>
/// </remarks>
public sealed record LayoutPlan(
    // `markTempo beside` — a boxed section label and the bar's tempo on one line (the chart's);
    // false is `markTempo stacked`, LilyPond's arrangement and the default (MarkArrangement).
    bool MarksBeside,
    // `barNumbers lines|none|every N` — where the bar numbers stand (BarNumberPolicy).
    BarNumberPolicy BarNumbers,
    // `accidentals default|modern|…` — which notes carry a printed accidental
    // (AccidentalStyles). Null is the default style, so LayoutPlan.Default compares equal
    // to a plan that states it.
    AccidentalStyleSpec? Accidentals = null,
    // `sectionLabels boxed|plain|none` — how a form section's name is drawn.
    SectionLabelStyle SectionLabels = SectionLabelStyle.Boxed,
    // `partCombineText true|false` — whether a combinedStaff prints "a2" / "Solo" / "Solo II".
    bool PartCombineText = true,
    // `chordQualities symbols|words` and `minorChords upper|lower` — how a chord SYMBOL is
    // spelled (ChordSpelling). The struct's own default is LilyPond's spelling, so
    // LayoutPlan.Default compares equal to a plan that writes both keys out.
    ChordSpelling Chords = default,
    // `chordDiagrams none|guitar|ukulele|…` — the TUNING word as written, or null when the key
    // is absent or writes no tuning (`chordDiagrams all`): then each diagram takes its part's
    // instrument, else the guitar (ChordDiagramsKey.Resolve, owner's decision 2026-09-28).
    string? ChordDiagrams = null,
    // `chordDiagrams [TUNING] all` — every chord name draws a diagram: its written shape, else
    // the default one (Music.ChordShapes.Drawn; owner's decision 2026-09-28). False without it:
    // only a written shape draws. Never true with `none` (the reader refuses `none all`).
    bool ChordDiagramsAll = false,
    // `voltaBracket all|line|N` — how far an ending's bracket reaches (VoltaBracketLength);
    // an ending's own `@voltaBracket(…)` overrides it. The struct's default is `all`.
    VoltaBracketLength VoltaBracket = default)
{
    /// <summary>The tuning a chord diagram of this score draws on, given the fretted tuning
    /// of the part it belongs to (<paramref name="partTuning"/>, null for none) — or null
    /// when the score writes <c>chordDiagrams none</c> (<see cref="ChordDiagramsKey.Resolve"/>).</summary>
    public Syntax.TuningType? ChordDiagramTuningFor(Syntax.TuningType? partTuning)
        => ChordDiagramsKey.Resolve(ChordDiagrams, partTuning);

    /// <summary>What a book with no <c>layout { }</c> gets: LilyPond's picture on every
    /// switch — labels stacked over the tempo, a number at the start of every line but the
    /// first, and the 18th-century accidental style.</summary>
    public static readonly LayoutPlan Default = new(MarksBeside: false, BarNumberPolicy.Lines);

    /// <summary>The accidental style this plan asks for, never null.</summary>
    public AccidentalStyleSpec AccidentalStyle => Accidentals ?? AccidentalStyles.Default;
}

/// <summary>
/// How a chord SYMBOL is spelled: the quality's vocabulary, and whether a chord with a
/// minor third writes an uppercase root with an <c>m</c> or a lowercase root without one.
/// The two axes are LilyPond's two, and they are independent there as well — the exception
/// table and <c>chordNameLowercaseMinor</c> are separate properties.
/// </summary>
/// <remarks>
/// ⚠️ ONE VALUE, PASSED AS A REQUIRED ARGUMENT to every namer, rather than read from a
/// context each namer finds for itself: the symbol is spelled in four places (a chord row,
/// an attached track, an inline <c>@chord</c>, the editor's completion) and a place that
/// missed the switch would print the OTHER vocabulary beside the one the score asked for,
/// in silence. Same discipline as <c>MusicMarkLayout.Boxed</c> (§5.2.1②).
/// <para>
/// ⚠️ NOT THE SAME QUESTION AS <c>chords NAME as names|roman</c>, which is why the two live
/// in different places and neither absorbs the other. That clause says WHICH QUANTITY the
/// row shows — the absolute chord, or its degree in the key — and it is written per ROW
/// because one score writes BOTH at once (<c>chords prog as roman</c> above
/// <c>chords prog as names</c> is how a track is shown two ways, since <c>as both</c> was
/// retired). A <c>layout { }</c> key is score-wide by admission, so it could not carry a
/// setting a single score needs two values of. This is the SPELLING of whatever that clause
/// chose — and the two compose: a roman row is byte-identical under either vocabulary,
/// because <see cref="Music.ChordStructure"/>'s roman table already spells the same four
/// qualities and overrides them.
/// </para>
/// <para>
/// ⚠️ IT DOES NOT REACH MusicXML. A <c>&lt;harmony&gt;</c> element carries the chord as
/// DATA — root, kind, degrees — and the exporter reads Lily#'s canonical spelling back to
/// build it, so the exporter asks for <see cref="Canonical"/> (a <c>C°</c> would parse as
/// nothing). The same rule <c>sectionLabels</c> and <c>partCombineText</c> keep: a display
/// switch moves the page, not the data.
/// </para>
/// </remarks>
/// <param name="Qualities">The quality's vocabulary.</param>
/// <param name="LowercaseMinor">LilyPond's <c>chordNameLowercaseMinor</c>: a minor-third
/// chord prints a lowercase root and drops the <c>m</c>.</param>
public readonly record struct ChordSpelling(ChordQualityStyle Qualities, bool LowercaseMinor)
{
    /// <summary>What a book with no <c>layout { }</c> gets, which is also the struct's
    /// <c>default</c>: LilyPond's own symbols, and an uppercase root with its <c>m</c>.</summary>
    public static readonly ChordSpelling Default = default;

    /// <summary>
    /// The spelling Lily#'s OWN PARSER reads back — words, uppercase root — for the readers
    /// that carry the chord as DATA rather than as a picture: the MusicXML
    /// <c>&lt;harmony&gt;</c> and the editor's completion detail.
    /// </summary>
    /// <remarks>
    /// ⚠️ IT WAS <see cref="Default"/> UNTIL 2026-09-12, and the two were the same value by
    /// accident: the display default was the words vocabulary, so a reader that meant "the
    /// spelling that parses" and a reader that meant "what a book with no switch prints"
    /// could share one name. The owner moved the default to LilyPond's symbols and the two
    /// meanings came apart — a <c>&lt;harmony&gt;</c> asking for the default would now be
    /// handed <c>C°</c>, which spells no quality the parser knows. One name, two quantities
    /// (§5.2.1②); this is the second one, named.
    /// </remarks>
    public static readonly ChordSpelling Canonical = new(ChordQualityStyle.Words, false);
}

/// <summary>How a chord's QUALITY is spelled after the root.</summary>
/// <remarks>
/// LILYPOND-REF: ly/chord-modifiers-init.ly ignatzekExceptionMusic (lines 47-59) — the table
/// whose entries give LilyPond its symbols: <c>&lt;c e gis&gt;</c> is "+",
/// <c>&lt;c es ges&gt;</c> is whiteCircleMarkup, <c>&lt;c es ges bes&gt;</c> is a superscript
/// U+00F8 and <c>&lt;c es ges beses&gt;</c> is the circle with a superscript 7.
/// ⚠️ THE ADDRESSES IN THIS FILE CARRY NO LINE RANGE ON PURPOSE: every name here is
/// camelCase or a two-part hyphen word, and <c>LpReferenceCitationTests</c>' symbol pattern
/// reads only underscored names and three-part hyphen ones — so a ranged citation would
/// count as naming nothing whatever is written after it. The line is given in prose instead,
/// the spelling <c>ChordNameGlyphRun.ShortGlyph</c> already uses.
/// <para>
/// ★ <c>symbols</c> IS LILYPOND'S PICTURE, and since 2026-09-12 it is the default. The two
/// halves LilyPond adds beyond the table are ported too: everything after the root is RAISED
/// (make-super-markup, at scm/chord-ignatzek-names.scm line 207 — <c>ChordNameGlyphRun</c>)
/// and a major seventh is a DRAWN TRIANGLE
/// (LILYPOND-REF: ly/chord-modifiers-init.ly whiteTriangleMarkup, lines 23-33 — a
/// <c>\fontsize #-3 \triangle ##f</c> polygon stencil), which
/// <c>ChordQualityRegistry.SymbolSuffix</c> carries for the four major-seventh qualities.
/// MEASURED (audit/lp-geometry, books CHL1/CHL2): with the triangle the chord row stands
/// 5.659653422 under the staff refpoint, which is LilyPond's own number; with the word
/// <c>maj7</c> it stands 0.337483977 lower, because the letters ink taller than the polygon.
/// </para>
/// <para>
/// ⚠️ <c>words</c> IS NOT A FALLBACK, it is the other convention — the lead-sheet spelling a
/// reader may want and LilyPond has no switch for. It was the default until 2026-09-12, and
/// what changed is which picture a book gets for free, not what either word means.
/// </para>
/// </remarks>
public enum ChordQualityStyle
{
    /// <summary>LilyPond's own symbols for the four qualities its exception table names —
    /// <c>C°</c>, <c>C+</c>, <c>Cø</c>, <c>C°7</c> — and its triangle for a major seventh.
    /// Every other quality keeps its word, because LilyPond spells those with digits too.
    /// THE DEFAULT, and the enum's zero so that <c>default(ChordSpelling)</c> is it.</summary>
    Symbols,

    /// <summary>Words — <c>Cdim</c>, <c>Caug</c>, <c>Cm7♭5</c>, <c>Cdim7</c>, <c>Cmaj7</c>:
    /// the lead-sheet convention, and what <see cref="ChordSpelling.Canonical"/> holds for
    /// the readers that carry a chord as data.</summary>
    Words,
}

/// <summary>The <c>chordQualities</c> key's words.</summary>
public static class ChordQualityStyles
{
    /// <summary>The key as written in the block.</summary>
    public const string Key = "chordQualities";

    /// <summary>The words, the default first.</summary>
    public static readonly IReadOnlyList<string> Words = ["symbols", "words"];

    /// <summary>The style <paramref name="word"/> names, or null.</summary>
    public static ChordQualityStyle? Find(string word) => word switch
    {
        "words" => ChordQualityStyle.Words,
        "symbols" => ChordQualityStyle.Symbols,
        _ => null,
    };
}

/// <summary>The <c>minorChords</c> key's words — LilyPond's
/// <c>chordNameLowercaseMinor</c>, which is a boolean there too.</summary>
/// <remarks>
/// LILYPOND-REF: ly/engraver-init.ly chordNameLowercaseMinor (line 948) — <c>##f</c> by
///   default;
/// LILYPOND-REF: scm/chord-ignatzek-names.scm chordNameLowercaseMinor (lines 229-232) — the
///   flag is read together with the chord's third and holds only when that third is FLAT,
///   which is why a diminished chord lowercases too and a <c>sus</c> chord (no third at all)
///   never does;
/// LILYPOND-REF: scm/chord-ignatzek-names.scm prefix-modifier->markup (lines 136-142) — the
///   minorChordModifier ("m") is replaced by empty-markup when the root is lowercased, so
///   the two always travel together.
/// ⚠️ The BASS keeps its capital.
/// LILYPOND-REF: scm/chord-name.scm chordNoteNamer (lines 147-153) — the callback is called
///   with <c>lowercase?</c> always <c>#f</c>, the <c>#f</c> the slash bass is named with.
/// (The ranges are in prose for the reason the enum above gives.)
/// </remarks>
public static class MinorChords
{
    /// <summary>The key as written in the block.</summary>
    public const string Key = "minorChords";

    /// <summary>The two words, the default first.</summary>
    public static readonly IReadOnlyList<string> Words = ["upper", "lower"];

    /// <summary>True for <c>lower</c>, false for <c>upper</c>, null for anything else.</summary>
    public static bool? Find(string word) => word switch
    {
        "upper" => false,
        "lower" => true,
        _ => null,
    };
}

/// <summary>How a form section's name is drawn above the staff.</summary>
/// <remarks>
/// ⚠️ The BOX is Lily#-own: LilyPond's SectionLabel grob draws the bare string, and the
/// twin reaches Lily#'s picture by writing <c>\mark \markup \box</c>. So <c>plain</c> is
/// the arrangement that agrees with LilyPond's own grob, and <c>boxed</c> — the default,
/// and what every book on disk prints — is the divergence Lily# chose (session 324 /
/// 368's ledger points say so in the same words).
/// </remarks>
public enum SectionLabelStyle
{
    /// <summary>A frame around the name (the default; Lily#-own).</summary>
    Boxed,
    /// <summary>The name alone, with no frame — LilyPond's own SectionLabel picture.</summary>
    /// <remarks>
    /// The frame's size is priced at nine sites, so this arm reaches them as a bit ON THE
    /// MARK (<c>MusicMarkLayout.Boxed</c>), handed to every one of them as a REQUIRED
    /// argument: a site that forgets does not compile. Half-threading it is how two places
    /// come to price one box differently, which this box has already taught once (§5.2.1②).
    /// </remarks>
    Plain,

    /// <summary>No label at all: the section's name is not engraved (the part sheet's
    /// answer). The form still plays it, and MIDI / MusicXML are untouched — this is a
    /// display switch.</summary>
    None,
}

/// <summary>The <c>sectionLabels</c> key's words.</summary>
public static class SectionLabels
{
    /// <summary>The key as written in the block.</summary>
    public const string Key = "sectionLabels";

    /// <summary>The words, the default first.</summary>
    public static readonly IReadOnlyList<string> Words = ["boxed", "plain", "none"];

    /// <summary>The style <paramref name="word"/> names, or null.</summary>
    public static SectionLabelStyle? Find(string word) => word switch
    {
        "boxed" => SectionLabelStyle.Boxed,
        "plain" => SectionLabelStyle.Plain,
        "none" => SectionLabelStyle.None,
        _ => null,
    };

    /// <summary>The word for <paramref name="style"/>.</summary>
    public static string WordOf(SectionLabelStyle style) => Words[(int)style];
}

/// <summary>The <c>partCombineText</c> key's words — LilyPond's
/// <c>printPartCombineTexts</c>, which is a boolean there too.</summary>
/// <remarks>LILYPOND-REF: ly/engraver-init.ly printPartCombineTexts — the Staff property
/// the part-combine engraver asks before it makes the text item.</remarks>
public static class PartCombineTexts
{
    /// <summary>The key as written in the block.</summary>
    public const string Key = "partCombineText";

    /// <summary>The two words, the default first — <c>true</c> / <c>false</c>, the language's
    /// one boolean spelling (as <c>removeEmpty</c>; owner decision 2026-09-15, which retired
    /// <c>on</c> / <c>off</c> before 0.7.0 shipped).</summary>
    public static readonly IReadOnlyList<string> Words = ["true", "false"];

    /// <summary>True for <c>true</c>, false for <c>false</c>, null for anything else.</summary>
    public static bool? Find(string word) => word switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };
}

/// <summary>
/// The <c>chordDiagrams</c> key's words: <c>none</c> or a tuning word — the same vocabulary a
/// tab staff's <c>tuning</c> takes (<see cref="Tablature.Tunings.Names"/>) — and the scope
/// word <c>all</c>, alone or after the tuning; and which tuning a diagram draws on
/// (<see cref="Resolve"/>).
/// </summary>
/// <remarks>
/// <para>
/// Owner's decisions (HANDOFF §2 K, 2026-09-28): a diagram draws only where a shape is
/// WRITTEN (<see cref="Music.ChordShapes.Drawn"/>) — unless the score writes <c>all</c>
/// (<c>chordDiagrams all</c>, <c>chordDiagrams ukulele all</c>: the tuning first), when EVERY
/// chord name draws one, its written shape else the default (<see cref="Music.ChordShapes.Default"/>).
/// <c>none all</c> is refused (<c>none</c> draws nothing). A diagram draws on ONE tuning,
/// strongest first:
/// the score's <c>chordDiagrams TUNING</c> (<c>none</c>: no diagram at all, written shapes
/// included — a piano score from the same source); else the fretted instrument of the part
/// the chord belongs to (<see cref="PartHeaderDefaults.FrettedTuningWord"/>); else the guitar.
/// A staff-level override (<c>staff melody with chords prog on ukulele</c>) was considered and
/// NOT added: the layout suffices.
/// </para>
/// <para>
/// LILYSHARP-OWN: LilyPond's FretBoards context carries its own <c>stringTunings</c>
/// (guitar by default) whatever the staves around it play, and draws a diagram for every chord
/// it is given — its predefined shape or one it computes.
/// LILYPOND-REF: ly/engraver-init.ly FretBoards context (lines 38-57) — predefinedDiagramTable,
///   and a stringTunings of its own.
/// </para>
/// </remarks>
public static class ChordDiagramsKey
{
    /// <summary>The key as written in the block.</summary>
    public const string Key = "chordDiagrams";

    /// <summary>The scope word: every chord name draws a diagram (<c>chordDiagrams all</c>,
    /// <c>chordDiagrams guitar all</c>).</summary>
    public const string AllWord = "all";

    /// <summary>The words the key's FIRST value takes: <c>none</c>, the tuning vocabulary, and
    /// <c>all</c> (which may also follow a tuning word).</summary>
    public static readonly IReadOnlyList<string> Words =
        [Music.ChordShapes.NoneWord, .. Tablature.Tunings.Names, AllWord];

    /// <summary>True for a word the key's first value takes (<c>none</c>, a tuning word or
    /// <c>all</c>).</summary>
    public static bool TryFind(string word)
        => word == Music.ChordShapes.NoneWord || word == AllWord || IsTuningWord(word);

    /// <summary>True for a tuning word (<see cref="Tablature.Tunings.Names"/>).</summary>
    public static bool IsTuningWord(string word) => Tablature.Tunings.Names.Contains(word);

    /// <summary>
    /// The tuning a diagram draws on: the layout's word (<paramref name="layoutWord"/>, null
    /// when the key is absent), else the part's fretted tuning (<paramref name="partTuning"/>),
    /// else the guitar — null for <c>none</c>, which draws no diagram.
    /// </summary>
    public static Syntax.TuningType? Resolve(string? layoutWord, Syntax.TuningType? partTuning)
        => layoutWord == Music.ChordShapes.NoneWord ? null
            : layoutWord != null ? Tablature.Tunings.Parse(layoutWord)
            : partTuning ?? Syntax.TuningType.Guitar;

    /// <summary>The same, as a tuning WORD (for the editor's messages): the layout's word, else
    /// the part's (<paramref name="partWord"/>), else <c>guitar</c>; null for <c>none</c>.</summary>
    public static string? ResolveWord(string? layoutWord, string? partWord)
        => layoutWord == Music.ChordShapes.NoneWord ? null : layoutWord ?? partWord ?? "guitar";
}

/// <summary>How far an ending's volta bracket reaches (<see cref="VoltaBracketLength"/>).</summary>
public enum VoltaBracketLengthMode
{
    /// <summary>Every bar of the ending (the default, and the enum's zero).</summary>
    All,
    /// <summary>Up to the end of the system the bracket starts in.</summary>
    Line,
    /// <summary>The ending's first <see cref="VoltaBracketLength.Bars"/> bars.</summary>
    Bars,
}

/// <summary>
/// The <c>voltaBracket</c> value — a layout key (<c>layout { voltaBracket line }</c>) and an
/// ending's own annotation (<c>[1. B C]@voltaBracket(3)</c>, which wins over the key):
/// <c>all</c> covers every bar of the ending, <c>line</c> stops at the end of the system the
/// bracket starts in, and a whole number N covers the ending's first N bars (all of it when
/// the ending is shorter). Owner's design, 2026-09-28.
/// </summary>
/// <remarks>
/// ⚠️ A bracket CUT SHORT always ends straight, whatever its <c>]</c> / <c>-]</c> says: the
/// down-hook means "the ending ends here", and a cut bracket's right end is not the ending's
/// end (<see cref="IsCut"/>).
/// LILYPOND-REF: lily/volta-engraver.cc:232-296 Volta_engraver::process_music — a
/// VoltaBracket ends early when its <c>musical-length</c> (or
/// <c>voltaBracketMusicalLength</c>) runs out;
/// LILYPOND-REF: lily/volta-engraver.cc:428-533 Volta_engraver::stop_translation_timestep —
/// zeroes the right <c>edge-height</c> unless the bar where it ends allows a hook.
/// </remarks>
/// <param name="Mode">Which reach.</param>
/// <param name="Bars">For <see cref="VoltaBracketLengthMode.Bars"/>, the N (at least 1); 0 otherwise.</param>
public readonly record struct VoltaBracketLength(VoltaBracketLengthMode Mode, int Bars = 0)
{
    /// <summary>The layout key, and the annotation's name.</summary>
    public const string Key = "voltaBracket";

    /// <summary>The value words (a whole number is the third spelling).</summary>
    public const string AllWord = "all";
    /// <inheritdoc cref="AllWord"/>
    public const string LineWord = "line";

    /// <summary>The words, the default first — for messages and completion.</summary>
    public static readonly IReadOnlyList<string> Words = [AllWord, LineWord];

    /// <summary>The sentence every refusal of a value ends with.</summary>
    public const string Takes = "takes all, line or a whole number of bars (at least 1)";

    /// <summary>Every bar of the ending — the default.</summary>
    public static readonly VoltaBracketLength All = default;

    /// <summary>To the end of the bracket's first system.</summary>
    public static readonly VoltaBracketLength Line = new(VoltaBracketLengthMode.Line);

    /// <summary>The ending's first <paramref name="bars"/> bars.</summary>
    public static VoltaBracketLength FirstBars(int bars) => new(VoltaBracketLengthMode.Bars, bars);

    /// <summary>The value <paramref name="word"/> spells — <c>all</c>, <c>line</c> or a whole
    /// number of at least 1 written in ASCII digits — or null. Case-sensitive, like every
    /// Lily# value word.</summary>
    public static VoltaBracketLength? Parse(string word)
    {
        if (word == AllWord) return All;
        if (word == LineWord) return Line;
        if (word.Length > 0 && word.All(char.IsAsciiDigit)
            && int.TryParse(word, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int n) && n >= 1)
            return FirstBars(n);
        return null;
    }

    /// <summary>The spelling of a value word that differs from <paramref name="word"/> only
    /// in case (<c>Line</c> → <c>line</c>), or null.</summary>
    public static string? CaseOnlyMatch(string word)
        => Words.FirstOrDefault(w => !w.Equals(word, StringComparison.Ordinal)
                                     && w.Equals(word, StringComparison.OrdinalIgnoreCase));

    /// <summary>The written spelling of this value.</summary>
    public override string ToString() => Mode switch
    {
        VoltaBracketLengthMode.Line => LineWord,
        VoltaBracketLengthMode.Bars => Bars.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => AllWord,
    };

    /// <summary>The last bar the bracket covers, for an ending over bars
    /// <paramref name="first"/>..<paramref name="last"/> (inclusive) — before any system
    /// break is known: a <c>line</c> bracket is cut by the layout, not here.</summary>
    public int LastBar(int first, int last)
        => Mode == VoltaBracketLengthMode.Bars ? Math.Min(last, first + Bars - 1) : last;

    /// <summary>True when N bars stop before the ending's last bar — the bracket is cut
    /// short and ends straight.</summary>
    public bool IsCut(int first, int last) => LastBar(first, last) < last;
}

/// <summary>Which bars carry a printed number.</summary>
public enum BarNumberMode
{
    /// <summary>LilyPond's default: the first bar of every line after the first
    /// (<c>first-bar-number-invisible-and-no-parenthesized-bar-numbers</c> with the grob's
    /// <c>begin-of-line-visible</c>).</summary>
    Lines,
    /// <summary>No bar numbers at all (LilyPond's <c>\remove Bar_number_engraver</c>).</summary>
    None,
    /// <summary>Every bar whose number is a multiple of <see cref="BarNumberPolicy.Period"/>,
    /// wherever it stands in the line (LilyPond's <c>every-nth-bar-number-visible</c> with
    /// <c>break-visibility = end-of-line-invisible</c>).</summary>
    Every,
}

/// <summary>
/// The <c>barNumbers</c> key's value: a mode, and for <see cref="BarNumberMode.Every"/> the
/// period. LilyPond's vocabulary, spelled as the chart writer says it.
/// </summary>
/// <param name="Mode">Which bars are numbered.</param>
/// <param name="Period">For <c>every N</c>, the N (≥ 1); 0 otherwise.</param>
public readonly record struct BarNumberPolicy(BarNumberMode Mode, int Period = 0)
{
    /// <summary>The key, as written in the block.</summary>
    public const string Key = "barNumbers";

    /// <summary>The three words the key takes, the default first.</summary>
    public const string LinesWord = "lines";
    /// <inheritdoc cref="LinesWord"/>
    public const string NoneWord = "none";
    /// <inheritdoc cref="LinesWord"/>
    public const string EveryWord = "every";

    /// <summary>The words, in the order the completion offers them (the default first).
    /// <c>every</c> takes an integer after it.</summary>
    public static readonly IReadOnlyList<string> Words = [LinesWord, NoneWord, EveryWord];

    /// <summary>The default: a number at the start of every line after the first.</summary>
    public static readonly BarNumberPolicy Lines = new(BarNumberMode.Lines);

    /// <summary>No numbers.</summary>
    public static readonly BarNumberPolicy None = new(BarNumberMode.None);

    /// <summary>Every <paramref name="period"/>th bar.</summary>
    public static BarNumberPolicy Every(int period) => new(BarNumberMode.Every, period);
}
