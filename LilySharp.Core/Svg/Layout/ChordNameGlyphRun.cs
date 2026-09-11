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

using System.Collections.Immutable;
using LilySharp.Core.Rendering;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// How a chord symbol is laid out: sans text, with every accidental in it drawn as the
/// Emmentaler ACCIDENTAL GLYPH one font step smaller and lifted off the baseline, the way
/// LilyPond builds a chord name.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/chord-name.scm accidental->text-markup (lines 80-95) = make-accidental-markup
///   wrapped in make-smaller-markup and make-translate-scaled-markup; accidental->markup adds
///   the conditional kern. The three parts, and the LilyPond line each comes from:
/// <list type="bullet">
/// <item><c>\smaller</c> is <c>fontsize -1</c>
/// (scm/define-markup-commands.scm:3635-3655 smaller-markup), so the accidental is drawn at
/// the ChordName's own font-size with that offset off it —
/// <see cref="SmallerFontSizeOffset"/>, applied by <see cref="AccidentalStep"/>.</item>
/// <item><c>\translate-scaled #'(0 . y)</c> lifts by <c>y * (magstep font-size)</c>
/// (scm/define-markup-commands.scm:6142-6174 translate-scaled-markup), and the font-size it
/// reads is the one <c>\smaller</c> already lowered. <c>y</c> is 0.3 for a
/// <c>short-glyph?</c> (alteration &lt; 0, i.e. the flat family) and 0.6 otherwise.</item>
/// <item>the kern is an unscaled <c>\hspace</c> of <see cref="KernBeforeNarrowGlyph"/> before
/// a <c>narrow-glyph?</c>, and that predicate lists 0 and −1/2 among the western alterations
/// — so the single FLAT gets it and the sharp, the double sharp and the DOUBLE FLAT do
/// not.</item>
/// </list>
/// <para>
/// ⚠️ NOT <see cref="FetaTextRun"/>, and the difference is a LilyPond one rather than a
/// convenience. That type models a PANGO TEXT RUN that happens to contain feta glyphs
/// (fetaText encoding — one shaper, per-glyph advances hinted to a device pixel). A chord
/// name is a MARKUP LINE of separate stencils: the letters are a text stencil and the
/// accidental is a <c>\musicglyph</c> stencil, and <c>\line</c> concatenates them by their
/// EXTENTS with no hinting between them. MEASURED, which is how the difference was settled
/// rather than argued: LilyPond's `C♭' is 1.069431046810552 wider than its `C', and that is
/// the flat's BBOX width 0.92 times magstep plus the kern 0.094725 unscaled — an advance
/// (0.8) or a hinted pair would give neither number.
/// </para>
/// <para>
/// ⚠️ THE BOX IS THE LILC BBOX, NOT THE OUTLINE, and one book decides it: the sharp's two
/// boxes are identical so `A♯m' cannot tell them apart, while the FLAT's are 1.830000 against
/// 1.860000. LilyPond's `C♭' measures 2.256656390985299 = magstep * (0.3 + 1.83). That is
/// what <see cref="GlyphMetrics.AtFontSize"/> returns, which is also the house that picks the
/// DESIGN LilyPond's select_font would pick — so nothing here multiplies a glyph box by a
/// font size (see that method's remarks).
/// </para>
/// <para>
/// ⚠️ WHAT IS NOT PORTED HERE, named so it is not mistaken for a defect in this file:
/// LilyPond's chord-name VOCABULARY is not Lily#'s. LilyPond prints Cm⁷ (superscript), Cø for
/// a half-diminished, C+ for an augmented and C° for a diminished, where Lily# prints Cm7,
/// Cm7♭5, Caug and Cdim (<c>ChordQualityRegistry</c>). That is the language's decision and
/// this file does not touch it — it takes whatever string the namer produced and renders the
/// ACCIDENTALS in it the way LilyPond renders accidentals. MEASURED 2026-08-25: LilyPond
/// applies exactly this markup to the root, to a slash bass (<c>c/gis</c>) AND to a step
/// alteration (<c>c:7.9-</c> shows the 0.094725 hspace and the 0.3 translate in its own markup
/// tree), so applying it to every accidental in the printed name is LilyPond's rule and not a
/// generalisation of it.
/// </para>
/// <para>
/// ⚠️ WHAT ELSE IS NOT PORTED, and it is ONE absence with one cause: LilyPond builds the
/// raised part as SEVERAL markup pieces — the main name, the alterations, the sus/add
/// suffixes, the additions — and joins them with <c>chordNameSeparator</c>, an
/// <c>\hspace</c> of 0.5. Lily#'s quality is one WORD, so there is nothing to join and the
/// separator has no site to live at. MEASURED on LilyPond 2.26.0 (scratch/p372/lptri.svg):
/// its <c>C△9</c> puts the <c>9</c> 1.6203 past the triangle's origin, which is this run's
/// 1.1703 extent plus exactly that 0.5, so a Lily# symbol whose quality would have been two
/// LilyPond pieces is 0.5 narrower per join. What reaches it is an ALTERED TENSION
/// (<c>m7♭5</c>, <c>7♭9</c>, <c>7♯11</c>) and, under <c>symbols</c>, <c>△9</c> / <c>△13</c>.
/// COUNTED rather than guessed (session 371's census over 27,085 files): 40 sites in 40
/// books, 30 of them one <c>Gm7♭5</c>, plus 2 major-ninth/thirteenth sites. Porting it means
/// giving the quality LilyPond's PIECE STRUCTURE instead of a word — the namer would have to
/// hand over where its pieces join — which is a different change from this one, so it is
/// named here rather than approximated.
/// LILYPOND-REF: ly/engraver-init.ly chordNameSeparator (line 949) — the ChordNames
///   property, <c>#(make-hspace-markup 0.5)</c>;
/// LILYPOND-REF: scm/chord-ignatzek-names.scm markup-join (lines 190-195) — where the
///   raised pieces are joined with it.
/// </para>
/// <para>
/// ⚠️ ⒝ (HANDOFF §7.6): LilyPond chooses the glyph from the ALTERATION NUMBER, which it still
/// has when it builds the markup. Lily#'s chord name has already been rendered to a STRING by
/// <c>ChordStructure.SpellPitch</c>, so the alteration is recovered from the spelling — and
/// that spelling doubles the character for ±1 (`♯♯', `♭♭') where LilyPond has one glyph. The
/// pair is therefore read as ONE double accidental, which is what LilyPond draws. Making this
/// literal means carrying the alteration to the printer instead of the character.
/// </para>
/// </remarks>
internal static class ChordNameGlyphRun
{
    /// <summary>The ChordName grob's own font-size step.</summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:837-846 ChordName's (font-size . 1.5), beside after-line-breaking
    ///   and extra-spacing-width in the same block. The EM that
    /// step works out to is <see cref="EngravingDefaults.ChordNameFontSize"/>, which is the
    /// one home for the text side; this is the same number in LilyPond's own units, needed
    /// because the accidental is sized and lifted by STEPS, not by the em.
    /// </remarks>
    internal const double FontSizeStep = 1.5;

    /// <summary>
    /// The markup property <c>font-size</c> in force inside this score's chord symbol — the
    /// grob's own <see cref="FontSizeStep"/> as the score's <c>fonts { chordName … }</c>
    /// left it.
    /// </summary>
    /// <remarks>
    /// ⚠️ ONE HOME, because LilyPond has one: <c>font-size</c> is a markup PROPERTY that
    /// every command below reads and offsets (<c>\super</c> takes 3 off it, <c>\smaller</c>
    /// 1, <c>whiteTriangleMarkup</c> 3 more), and <c>magstep</c> is applied to whatever it
    /// then is. Reconstructing "the default plus the score's delta" at each site instead is
    /// how two of them come to disagree about which font-size they mean.
    /// </remarks>
    internal static double FontSize(ScoreTextMetrics fonts)
        => FontSizeStep + fonts.StepOf(TextRole.ChordName, EngravingDefaults.ChordNameFontSize);

    /// <summary>What <c>\super</c> takes off <c>font-size</c> inside itself.</summary>
    /// <remarks>LILYPOND-REF: scm/define-markup-commands.scm super-markup (lines 6263-6283)
    ///   — the body wraps the argument in <c>(font-size . ,(- font-size 3))</c>.</remarks>
    internal const double SuperFontSizeOffset = -3.0;

    /// <summary>What <c>\smaller</c> takes off it — the accidental's.</summary>
    /// <remarks>LILYPOND-REF: scm/define-markup-commands.scm smaller-markup (lines 3635-3655)
    ///   — the body is <c>(fontsize-markup -1 arg)</c>.</remarks>
    internal const double SmallerFontSizeOffset = -1.0;

    /// <summary>…and what <c>whiteTriangleMarkup</c> takes off it, on top of the
    /// <c>\super</c> it stands in.</summary>
    /// <remarks>LILYPOND-REF: ly/chord-modifiers-init.ly whiteTriangleMarkup (lines 23-33) —
    ///   the markup is <c>\fontsize #-3 \triangle ##f</c>.</remarks>
    internal const double TriangleFontSizeOffset = -3.0;

    /// <summary>
    /// The TEXT em a piece interpreted at <paramref name="fontSize"/> is drawn with.
    /// </summary>
    /// <remarks>
    /// The em LilyPond's <c>select_font</c> lands on for that font-size, in the units the
    /// engraving default is quoted in: <see cref="EngravingDefaults.ChordNameFontSize"/> is
    /// the em at <see cref="FontSizeStep"/>, and every other font-size is that scaled by
    /// <c>magstep</c> of the difference. So a raised piece's em is this asked at
    /// <c>FontSize + SuperFontSizeOffset</c> — LilyPond's own two words — rather than the
    /// symbol's em times a hand-reduced factor.
    /// </remarks>
    internal static double EmAt(double fontSize)
        => EngravingDefaults.ChordNameFontSize
           * EmmentalerDesignSize.Magstep(fontSize - FontSizeStep);

    /// <summary>How far a superscripted piece is lifted, in staff spaces.</summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-markup-commands.scm super-markup (lines 6263-6283) — the lift
    ///   is <c>(* 1.0 (magstep font-size))</c>, and the comment on that line says which
    ///   font-size it means: <c>; original font-size</c>, i.e. before the three came off.
    /// Written as those letters: the 1.0 and <c>magstep</c> of <see cref="FontSize"/>.
    /// MEASURED on LilyPond 2.26.0 (scratch/p372/lp-words.svg): the root sits at y 9.2248
    /// and the raised <c>7</c> at 8.0356 — 1.1892 against magstep(1.5) = 1.189207.
    /// </remarks>
    internal static double SuperRaise(ScoreTextMetrics fonts)
        => 1.0 * EmmentalerDesignSize.Magstep(FontSize(fonts));

    /// <summary>
    /// The character that CARRIES the major-seventh triangle through the printed string —
    /// U+25B3 WHITE UP-POINTING TRIANGLE.
    /// </summary>
    /// <remarks>
    /// ⚠️ IT IS NEVER DRAWN AS A CHARACTER. LilyPond's <c>majorSevenSymbol</c> is a drawn
    /// polygon, not a glyph, and the bundled sans face is not asked for U+25B3 at all — the
    /// run turns it into a <see cref="ChordPieceKind.Triangle"/> piece the way it turns ♯
    /// and ♭ into glyph pieces. It rides in the string so that <c>ChordText</c> stays one
    /// value the whole tree can carry, compare and click on; LilyPond's own source names
    /// this codepoint as the symbol's spelling in the comment beside the markup.
    /// LILYPOND-REF: ly/chord-modifiers-init.ly whiteTriangleMarkup (lines 23-33) — the
    ///   markup is <c>\fontsize #-3 \triangle ##f</c>, with <c>%% U+25B3: up pointing
    ///   triangle</c> written under it as the character that would stand for it.
    /// </remarks>
    internal const char TriangleCarrier = '△';

    /// <summary>
    /// The triangle's BASE, in staff spaces, for a piece drawn at <paramref name="step"/>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-markup-commands.scm triangle-markup (lines 519-561) — the body is
    ///   <c>(let ((ex (* (magstep font-size) 1.8)))</c> around a polygon through
    ///   <c>(0,0) (ex,0) (0.5ex, 0.86ex)</c>; the 1.8 carries its own comment saying it was
    ///   found by trial and error.
    /// MEASURED on LilyPond 2.26.0 (scratch/p372/lptri.svg): a default ChordName's triangle
    /// comes out as <c>points="0.5351 -0.9204 1.0703 -0.0000 0.0000 -0.0000"</c> — a base of
    /// 1.0703 and a height of 0.9204 against this formula's 1.070287 and 0.920447, at the
    /// step <c>\super</c> and <c>\fontsize #-3</c> leave: 1.5 − 3 − 3 = −4.5.
    /// </remarks>
    internal static double TriangleBase(double step)
        => EmmentalerDesignSize.Magstep(step) * 1.8;

    /// <summary>The triangle's height as a fraction of its base.</summary>
    /// <remarks>LILYPOND-REF: scm/define-markup-commands.scm triangle-markup (lines 519-561) — the
    ///   apex is <c>(cons (* 0.5 ex) (* 0.86 ex))</c>.</remarks>
    internal const double TriangleHeightRatio = 0.86;

    /// <summary>The unscaled kern LilyPond puts before a narrow accidental glyph.</summary>
    /// <remarks>
    /// LILYPOND-REF: scm/chord-name.scm:89-95 accidental->markup — conditional-kern-before with
    /// 0.094725 when narrow-glyph? holds. It appears in LilyPond's own markup tree as a plain
    /// <c>hspace-markup</c>, i.e. it is NOT multiplied by magstep; measured on `C♭', where the
    /// symbol grows by the flat's scaled box plus this number exactly.
    /// </remarks>
    internal const double KernBeforeNarrowGlyph = 0.094725;

    /// <summary>
    /// One drawn piece of a chord symbol: a run of sans text, one accidental glyph, or the
    /// major-seventh triangle.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/chord-ignatzek-names.scm:103-209 ignatzek-format-chord-name — the
    ///   name is assembled as a LIST of markups and closed with <c>make-line-markup</c>
    ///   (:209), so the printed symbol is a line of separate stencils and this type is one
    ///   of them. The list is where the root, the prefix modifiers, the raised group and the
    ///   slash bass are put side by side, which is why a piece carries its own font-size and
    ///   its own baseline rather than inheriting one from the symbol.
    /// </remarks>
    /// <param name="Text">The text of a text piece; empty for a glyph or triangle piece.</param>
    /// <param name="Glyph">The Emmentaler codepoint of a glyph piece.</param>
    /// <param name="Kind">Which of the three this is (<see cref="ChordPieceKind"/>).</param>
    /// <param name="X">The piece's LEFT EDGE, relative to the symbol's origin.</param>
    /// <param name="Advance">How far the pen moves over this piece — for a glyph piece the
    /// kern plus the glyph's own box width, because a markup line concatenates by extent.</param>
    /// <param name="DrawX">Where the glyph's ORIGIN goes, relative to the symbol's origin. It
    /// is not <paramref name="X"/>: a flat's box starts 0.12 LEFT of its origin, so drawing at
    /// the piece's left edge would put the glyph that far too far right.</param>
    /// <param name="Raise">The piece's baseline lift — the accidental glyph's, or the
    /// superscript's (see <see cref="SuperRaise"/>).</param>
    /// <param name="Bottom">The piece's ink bottom, already lifted.</param>
    /// <param name="Top">The piece's ink top, already lifted.</param>
    /// <param name="FontSize">The markup property <c>font-size</c> THIS piece is interpreted
    /// at — the symbol's <see cref="ChordNameGlyphRun.FontSize"/> with the offsets the
    /// commands around it took off (<see cref="SuperFontSizeOffset"/>,
    /// <see cref="SmallerFontSizeOffset"/>, <see cref="TriangleFontSizeOffset"/>).
    /// ⚠️ A FONT-SIZE, NOT A FACTOR: LilyPond's markup carries font-size and each stencil
    /// kind turns it into a size its own way — a text run through the text face's em
    /// (<see cref="EmAt"/>), a <c>\musicglyph</c> through the music design
    /// (<see cref="AccidentalGlyphEm"/>), the triangle through <c>magstep × 1.8</c>
    /// (<see cref="TriangleBase"/>). One reduced factor for all three would be a number
    /// LilyPond does not have.</param>
    internal readonly record struct Piece(
        string Text, char Glyph, ChordPieceKind Kind,
        double X, double Advance, double DrawX, double Raise,
        double Bottom, double Top, double FontSize = FontSizeStep)
    {
        /// <summary>True for an Emmentaler accidental glyph.</summary>
        internal bool IsGlyph => Kind == ChordPieceKind.Accidental;

        /// <summary>True for the major-seventh triangle, which is a DRAWN polygon and not a
        /// character at all (<see cref="TriangleBase"/>).</summary>
        internal bool IsTriangle => Kind == ChordPieceKind.Triangle;
    }

    /// <summary>What one piece of a chord symbol is drawn with.</summary>
    /// <remarks>
    /// Three, because LilyPond builds the name from three kinds of stencil: a text stencil,
    /// a <c>\musicglyph</c> and — for the major seventh — a POLYGON. A kind rather than two
    /// booleans so the impossible combination cannot be written.
    /// LILYPOND-REF: scm/chord-ignatzek-names.scm name-step (lines 162-177) — the branch that emits
    ///   either the majorSevenSymbol markup (the polygon) or an accidental plus a digit, so
    ///   the three kinds are LilyPond's own three and not a convenience of this file;
    /// LILYPOND-REF: scm/chord-name.scm accidental->text-markup (lines 80-95) — where the accidental
    ///   becomes a <c>\musicglyph</c> stencil rather than a character in the text run.
    /// </remarks>
    internal enum ChordPieceKind
    {
        /// <summary>A run of sans text.</summary>
        Text,
        /// <summary>One Emmentaler accidental.</summary>
        Accidental,
        /// <summary>The major-seventh triangle.</summary>
        Triangle,
    }

    /// <summary>The em the glyph is drawn at — the music font's, at the smaller step.</summary>
    /// <remarks>
    /// LILYPOND-REF: lily/font-select.cc:115-186 select_font — the step picks the file and the
    /// size, which is what <see cref="EmmentalerDesignSize.Magstep"/> is the page-space factor
    /// for. <c>SharedRenderer.FontSize</c> is the full-size music em, so this is that em one
    /// step down, the same shape every other reduced music glyph in the tree is drawn at.
    /// </remarks>
    internal static double AccidentalGlyphEm(double staffFontSize, double fontSize)
        => staffFontSize * EmmentalerDesignSize.Magstep(fontSize);

    /// <summary>
    /// The symbol's text em for THIS score: <see cref="EngravingDefaults.ChordNameFontSize"/>
    /// unless the score's <c>fonts { }</c> wrote a <c>step</c> or <c>size</c> for
    /// <c>chordName</c> (or <c>chords</c>). Every reader of the em — the draw, the run's
    /// pieces, the row skyline — asks here.
    /// </summary>
    internal static double Em(ScoreTextMetrics fonts)
        => fonts.Size(TextRole.ChordName, EngravingDefaults.ChordNameFontSize);

    /// <summary>The symbol's weight and slant: <see cref="EngravingDefaults.ChordNameFontStyle"/>
    /// (regular; ChordName declares no series) unless the score wrote a style.</summary>
    internal static FontStyle Style(ScoreTextMetrics fonts)
        => fonts.Style(TextRole.ChordName, EngravingDefaults.ChordNameFontStyle);

    /// <summary>
    /// The accidental glyph's font-size for THIS score, on the BASELINE: <c>\smaller</c> off
    /// the symbol's own (<see cref="SmallerFontSizeOffset"/>). A RAISED accidental reads the
    /// <c>\super</c>'s font-size instead, which <see cref="Pieces"/> computes the same way
    /// and carries on the piece — this is the baseline case the callers outside the run ask
    /// for.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-markup-commands.scm smaller-markup (lines 3635-3655) — the
    ///   command <c>accidental->text-markup</c> wraps the glyph in, whose body is
    ///   <c>(fontsize-markup -1 arg)</c>: one word off the property, which is
    ///   <see cref="SmallerFontSizeOffset"/>. (The range is in prose because the name is a
    ///   two-part hyphen word — see <see cref="ShortGlyph"/>.)
    /// </remarks>
    internal static double AccidentalStep(ScoreTextMetrics fonts)
        => FontSize(fonts) + SmallerFontSizeOffset;

    /// <summary>
    /// The alteration a chord-name spelling puts at <paramref name="i"/>, in half steps, or
    /// null when the character there is not an accidental.
    /// </summary>
    /// <remarks>
    /// The doubled spellings are read first: <c>ChordStructure.SpellPitch</c> writes ±1 as two
    /// characters where LilyPond has one glyph, so `♯♯' must not lex as two sharps (see the
    /// type's ⒝ remark). Longest first, the same discipline
    /// <c>ChordStructure.RomanNumeralsLongestFirst</c> is written with.
    /// </remarks>
    private static (int Length, int Alteration)? AccidentalAt(string text, int i)
    {
        char c = text[i];
        if (c is not ('♯' or '♭')) return null;
        bool sharp = c == '♯';
        if (i + 1 < text.Length && text[i + 1] == c)
            return (2, sharp ? 2 : -2);
        return (1, sharp ? 1 : -1);
    }

    /// <summary>The glyph and its page-space box for an alteration in half steps.</summary>
    private static (char Glyph, GlyphMetrics.BBox Box) GlyphFor(int alteration, double step)
    {
        var m = GlyphMetrics.AtFontSize(step);
        return alteration switch
        {
            >= 2 => (EmmentalerGlyphs.AccidentalDoubleSharp, m.AccidentalDoubleSharp),
            1 => (EmmentalerGlyphs.AccidentalSharp, m.AccidentalSharp),
            -1 => (EmmentalerGlyphs.AccidentalFlat, m.AccidentalFlat),
            _ => (EmmentalerGlyphs.AccidentalDoubleFlat, m.AccidentalDoubleFlat),
        };
    }

    /// <summary>
    /// LilyPond's <c>short-glyph?</c> — the flat family sits lower, so it is lifted less.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/chord-name.scm — short-glyph?, whose whole body is `(&lt; alteration 0)'
    /// (:37-39). ⚠️ THE ADDRESS CARRIES NO LINE RANGE ON PURPOSE: those three lines hold one
    /// two-part hyphen name and nothing else, so <c>LpReferenceCitationTests</c> cannot tell it
    /// from English and would count a ranged citation as naming nothing whatever is written
    /// after it (the <c>misc.hh — intlog2</c> case in HANDOFF §5.2.1⑦).
    /// </remarks>
    private static bool ShortGlyph(int alteration) => alteration < 0;

    /// <summary>
    /// LilyPond's <c>narrow-glyph?</c> for the alterations a chord name can spell.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/chord-name.scm — narrow-glyph? (:41-53; the range is left off the
    /// address for the reason <see cref="ShortGlyph"/> gives), a membership test whose western
    /// entries are 0 and −1/2. ⚠️ THE DOUBLE FLAT IS NOT IN IT (−1 does not appear), which is
    /// why `C♭♭' grows by its box alone while `C♭' also pays the kern; measured on both.
    /// A natural never reaches here — <c>accidental->markup</c> returns before the kern when
    /// the alteration is 0, and a chord name spells no natural anyway.
    /// </remarks>
    private static bool NarrowGlyph(int alteration) => alteration == -1;

    /// <summary>
    /// The pieces of one chord symbol, left to right, with X from its origin.
    /// </summary>
    /// <param name="fonts">The score's text plan.</param>
    /// <param name="text">The printed symbol.</param>
    /// <param name="superFrom">Where the raised run begins
    /// (<see cref="Music.ChordSymbolText"/>), or <see cref="Music.ChordSymbolText.NoSuperscript"/>
    /// for a symbol that stands on one baseline.</param>
    /// <remarks>
    /// LILYPOND-REF: scm/chord-ignatzek-names.scm:179-209 ignatzek-format-chord-name — the
    ///   <c>let*</c> that builds the pieces and the <c>set!</c> that puts them in order:
    ///   <c>root-markup</c>, the kerned prefixes, <c>(make-super-markup to-be-raised-stuff)</c>
    ///   and then <c>base-stuff</c>'s slash separator and bass. This loop walks the same four
    ///   in the same order, which is why the raised span is a CONTIGUOUS range with the bass
    ///   after it.
    /// <para>
    /// ⚠️ THE RAISED RUN ENDS AT THE SLASH, and that is read off the string rather than
    /// carried: LilyPond puts nothing on the baseline between the quality and the bass, so
    /// the first <c>/</c> at or after <paramref name="superFrom"/> is the end by
    /// construction. A symbol has at most one (the namer writes it).
    /// </para>
    /// </remarks>
    internal static ImmutableArray<Piece> Pieces(
        ScoreTextMetrics fonts, string text, int superFrom = Music.ChordSymbolText.NoSuperscript)
    {
        if (string.IsNullOrEmpty(text)) return ImmutableArray<Piece>.Empty;

        var pieces = ImmutableArray.CreateBuilder<Piece>();
        var style = Style(fonts);
        // The symbol's own `font-size`, and the two the commands inside it leave. Each is
        // LilyPond's one word applied to the property, not a factor derived from it.
        double fontSize = FontSize(fonts);
        double superFontSize = fontSize + SuperFontSizeOffset;

        // The raised span, as a half-open character range. Everything outside it — the root,
        // the minor modifier, the `+` / `°`, the slash bass — is drawn at the symbol's own em
        // on its own baseline.
        int superStart = superFrom;
        int superEnd = text.Length;
        if (superStart >= 0 && superStart < text.Length)
        {
            int slash = text.IndexOf('/', superStart);
            if (slash >= 0) superEnd = slash;
        }
        else
        {
            superStart = int.MaxValue;   // nothing is raised
        }
        bool Raised(int i) => i >= superStart && i < superEnd;

        double superRaise = SuperRaise(fonts);
        double x = 0;
        int runStart = 0;

        // A text run is flushed whenever the drawing changes — at an accidental glyph, and
        // at either edge of the raised span, because a run carries ONE font-size and ONE
        // baseline.
        void FlushText(int end)
        {
            if (end <= runStart) return;
            string run = text[runStart..end];
            bool up = Raised(runStart);
            double runFontSize = up ? superFontSize : fontSize;
            double runEm = EmAt(runFontSize);
            double raise = up ? superRaise : 0;
            double advance = fonts.Advance(run, runEm, TextRole.ChordName, style);
            var (bottom, top) = fonts.Ink(run, runEm, TextRole.ChordName, style);
            pieces.Add(new Piece(run, '\0', ChordPieceKind.Text, x, advance, x, raise,
                bottom + raise, top + raise, runFontSize));
            x += advance;
        }

        for (int i = 0; i < text.Length;)
        {
            // An edge of the raised span cuts the run even when nothing else does.
            if ((i == superStart || i == superEnd) && i > runStart)
            {
                FlushText(i);
                runStart = i;
            }
            if (text[i] == TriangleCarrier)
            {
                FlushText(i);
                bool upTri = Raised(i);
                // whiteTriangleMarkup is `\fontsize #-3 \triangle ##f`, so it takes its own
                // offset off whatever font-size it stands in — the \super's inside the
                // superscript, the symbol's own outside it (the triangle only ever stands
                // inside today; the arm is written because the FONT-SIZE, not the place,
                // decides the size).
                double triFontSize =
                    (upTri ? superFontSize : fontSize) + TriangleFontSizeOffset;
                double triBase = TriangleBase(triFontSize);
                double triRaise = upTri ? superRaise : 0;
                double half = EngravingDefaults.LineThickness / 2;
                pieces.Add(new Piece(
                    Text: "", '\0', ChordPieceKind.Triangle,
                    X: x,
                    // The stencil's EXTENT, which is what a markup line concatenates by: the
                    // outline plus half the blot on each side.
                    // ⚠️ THAT IS THE EXTENT, NOT LILYPOND'S ADVANCE TO THE NEXT PIECE, and the
                    // difference is measured rather than glossed. On LilyPond's own `C△9`
                    // (scratch/p372/lptri.svg) the triangle's origin is 10.9296 and the `9`'s is
                    // 12.5499 — 1.6203 apart — which decomposes EXACTLY as this extent 1.1703
                    // plus 0.5: LilyPond joins the raised items with chordNameSeparator, an
                    // hspace of 0.5 (ly/engraver-init.ly chordNameSeparator, line 949).
                    // THAT SEPARATOR IS NOT PORTED, here or anywhere else in the symbol —
                    // Lily#'s quality is ONE text run, so `7♭9` carries only the accidental's
                    // kern where LilyPond joins `7` and `♭9` by 0.5 too. It is one absence with
                    // one cause (LilyPond's markup has pieces where Lily# has a word), not a
                    // triangle question, and it is named on the type's remarks.
                    Advance: triBase + 2 * half,
                    // The polygon's origin is its lower-LEFT corner, half a blot in from the
                    // extent's left edge.
                    DrawX: x + half,
                    triRaise,
                    Bottom: triRaise - half,
                    Top: triRaise + triBase * TriangleHeightRatio + half,
                    FontSize: triFontSize));
                // ⚠️ The BASE is not a field: it is `Advance - LineThickness` exactly, by the
                // line above, so a field for it would be a second spelling of one number and
                // the two could drift. `TriangleBaseOf` is the one reader.
                x += triBase + 2 * half;
                i++;
                runStart = i;
                continue;
            }
            var accidental = AccidentalAt(text, i);
            if (accidental is null)
            {
                i++;
                continue;
            }
            var (length, alteration) = accidental.Value;
            FlushText(i);
            bool up = Raised(i);
            // The accidental's own font-size: \smaller off whatever it stands in. A raised
            // one stands in the \super, so it reads that font-size — LilyPond's \super wraps
            // the whole markup, glyphs included — and its lift adds to the one the
            // accidental's own translate-scaled already gives it.
            double glyphFontSize =
                (up ? superFontSize : fontSize) + SmallerFontSizeOffset;
            var (glyph, box) = GlyphFor(alteration, glyphFontSize);
            double mag = EmmentalerDesignSize.Magstep(glyphFontSize);
            double kern = NarrowGlyph(alteration) ? KernBeforeNarrowGlyph : 0;
            double raise = (ShortGlyph(alteration) ? 0.3 : 0.6) * mag + (up ? superRaise : 0);
            pieces.Add(new Piece(
                Text: "", glyph, ChordPieceKind.Accidental,
                X: x, Advance: kern + box.Width,
                // The origin, not the left edge: the flat family's box reaches left of it.
                DrawX: x + kern - box.Left,
                raise, box.Bottom + raise, box.Top + raise, glyphFontSize));
            x += kern + box.Width;
            i += length;
            runStart = i;
        }
        FlushText(text.Length);
        return pieces.ToImmutable();
    }

    /// <summary>
    /// True when the name carries no accidental, so the run is ONE text piece and asking the
    /// face directly gives the same answer without building the run.
    /// </summary>
    /// <remarks>
    /// ⚠️ AN ALLOCATION SHORT CUT, NOT A RULE — HANDOFF §5.2 forbids branches LilyPond does
    /// not have, and this is not a branch in the ANSWER. <see cref="Pieces"/> emits exactly
    /// one text piece for such a name, and that piece's advance and ink ARE the face's for the
    /// whole string, so the two paths are equal by CONSTRUCTION rather than by measurement.
    /// It exists because the callers ask per symbol on every spacing pass while the code this
    /// replaced was a single face call: without it, a name with no accidental — nearly all of
    /// them — would begin allocating a builder per ask on the keystroke path (§5.6, §7.9).
    /// </remarks>
    private static bool IsPlainText(string text, int superFrom) =>
        superFrom == Music.ChordSymbolText.NoSuperscript
        && text.IndexOf('♯') < 0 && text.IndexOf('♭') < 0
        && text.IndexOf(TriangleCarrier) < 0;

    /// <summary>
    /// A triangle piece's BASE, read back off its advance — the one place that inverts the
    /// line <see cref="Pieces"/> builds it with, so the drawn triangle is the one the
    /// symbol's width was measured from.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-markup-commands.scm polygon-markup (lines 478-517) — the
    ///   stencil is <c>ly:round-polygon</c> at <c>thickness × line-thickness</c>, so its
    ///   extent is the outline grown by half that blot on each side and the inverse here is
    ///   that growth taken off again. (The range is in prose: the names are two-part hyphen
    ///   words.)
    /// </remarks>
    internal static double TriangleBaseOf(in Piece piece)
        => piece.Advance - EngravingDefaults.LineThickness;

    /// <summary>The symbol's whole X extent, whose left edge is its reference point.</summary>
    internal static double Width(
        ScoreTextMetrics fonts, string text, int superFrom = Music.ChordSymbolText.NoSuperscript)
    {
        if (IsPlainText(text, superFrom))
            return fonts.Advance(text, Em(fonts), TextRole.ChordName, Style(fonts));
        double w = 0;
        foreach (var p in Pieces(fonts, text, superFrom)) w += p.Advance;
        return w;
    }

    /// <summary>The symbol's ink about its baseline — the union of its pieces'.</summary>
    internal static (double Bottom, double Top) Ink(
        ScoreTextMetrics fonts, string text, int superFrom = Music.ChordSymbolText.NoSuperscript)
    {
        if (IsPlainText(text, superFrom))
            return fonts.Ink(text, Em(fonts), TextRole.ChordName, Style(fonts));
        double bottom = 0, top = 0;
        bool any = false;
        foreach (var p in Pieces(fonts, text, superFrom))
        {
            bottom = any ? System.Math.Min(bottom, p.Bottom) : p.Bottom;
            top = any ? System.Math.Max(top, p.Top) : p.Top;
            any = true;
        }
        return any ? (bottom, top) : (0, 0);
    }
}
