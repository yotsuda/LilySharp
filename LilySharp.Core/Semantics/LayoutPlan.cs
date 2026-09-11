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
    // `marks beside` — a boxed section label and the bar's tempo on one line (the chart's);
    // false is `marks stacked`, LilyPond's arrangement and the default (MarkArrangement).
    bool MarksBeside,
    // `barNumbers lines|none|every N` — where the bar numbers stand (BarNumberPolicy).
    BarNumberPolicy BarNumbers,
    // `accidentals default|modern|…` — which notes carry a printed accidental
    // (AccidentalStyles). Null is the default style, so LayoutPlan.Default compares equal
    // to a plan that states it.
    AccidentalStyleSpec? Accidentals = null,
    // `sectionLabels boxed|plain|none` — how a form section's name is drawn.
    SectionLabelStyle SectionLabels = SectionLabelStyle.Boxed,
    // `partCombineText on|off` — whether a combinedStaff prints "a2" / "Solo" / "Solo II".
    bool PartCombineText = true,
    // `chordQualities words|symbols` and `minorChords upper|lower` — how a chord SYMBOL is
    // spelled (ChordSpelling). The struct's own default is today's spelling, so
    // LayoutPlan.Default compares equal to a plan that writes both keys out.
    ChordSpelling Chords = default)
{
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
/// build it, so the exporter asks for <see cref="Default"/> on purpose (a <c>C°</c> would
/// parse as nothing). The same rule <c>sectionLabels</c> and <c>partCombineText</c> keep:
/// a display switch moves the page, not the data.
/// </para>
/// </remarks>
/// <param name="Qualities">The quality's vocabulary.</param>
/// <param name="LowercaseMinor">LilyPond's <c>chordNameLowercaseMinor</c>: a minor-third
/// chord prints a lowercase root and drops the <c>m</c>.</param>
public readonly record struct ChordSpelling(ChordQualityStyle Qualities, bool LowercaseMinor)
{
    /// <summary>Today's spelling, which is also the struct's <c>default</c>: words, and an
    /// uppercase root with its <c>m</c>.</summary>
    public static readonly ChordSpelling Default = default;
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
/// ⚠️ WHAT <c>symbols</c> DOES NOT REACH, so it is not read as a full port of that table:
/// LilyPond RAISES everything after the root (make-super-markup, at
/// scm/chord-ignatzek-names.scm line 207) and spells a major seventh with a DRAWN TRIANGLE
/// (LILYPOND-REF: ly/chord-modifiers-init.ly whiteTriangleMarkup, lines 23-33 — a
/// <c>\fontsize #-3 \triangle ##f</c> polygon stencil). Lily# draws a chord name as one
/// baseline text run with the accidentals as glyphs (<c>ChordNameGlyphRun</c>), so neither
/// the superscript nor the triangle has a home yet, and <c>maj7</c> stays <c>maj7</c> under
/// both words. That is the Phase-1 simplification this enum switches INSIDE, not the one it
/// closes.
/// </para>
/// </remarks>
public enum ChordQualityStyle
{
    /// <summary>Words — <c>Cdim</c>, <c>Caug</c>, <c>Cm7♭5</c>, <c>Cdim7</c>. The default,
    /// and what every book on disk prints.</summary>
    Words,

    /// <summary>LilyPond's own symbols for the four qualities its exception table names —
    /// <c>C°</c>, <c>C+</c>, <c>Cø</c>, <c>C°7</c>. Every other quality keeps its word,
    /// because LilyPond spells those with digits too.</summary>
    Symbols,
}

/// <summary>The <c>chordQualities</c> key's words.</summary>
public static class ChordQualityStyles
{
    /// <summary>The key as written in the block.</summary>
    public const string Key = "chordQualities";

    /// <summary>The words, the default first.</summary>
    public static readonly IReadOnlyList<string> Words = ["words", "symbols"];

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

    /// <summary>The two words, the default first.</summary>
    public static readonly IReadOnlyList<string> Words = ["on", "off"];

    /// <summary>True for <c>on</c>, false for <c>off</c>, null for anything else.</summary>
    public static bool? Find(string word) => word switch
    {
        "on" => true,
        "off" => false,
        _ => null,
    };
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
