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
/// LILYPOND-REF: scm/chord-name.scm:80-95 accidental->text-markup = make-accidental-markup
///   wrapped in make-smaller-markup and make-translate-scaled-markup; accidental->markup adds
///   the conditional kern. The three parts, and the LilyPond line each comes from:
/// <list type="bullet">
/// <item><c>\smaller</c> is <c>fontsize -1</c>
/// (scm/define-markup-commands.scm:3635-3655 smaller-markup), so the accidental is drawn at
/// the ChordName's own step minus one — <see cref="AccidentalFontSizeStep"/>.</item>
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

    /// <summary>The step the accidental is drawn at — one lower, which is <c>\smaller</c>.</summary>
    /// <remarks>LILYPOND-REF: scm/define-markup-commands.scm:3635-3655 — the define-markup-command
    ///   for smaller, whose body is `(fontsize-markup -1 arg)'.</remarks>
    internal const double AccidentalFontSizeStep = FontSizeStep - 1.0;

    /// <summary>The unscaled kern LilyPond puts before a narrow accidental glyph.</summary>
    /// <remarks>
    /// LILYPOND-REF: scm/chord-name.scm:89-95 accidental->markup — conditional-kern-before with
    /// 0.094725 when narrow-glyph? holds. It appears in LilyPond's own markup tree as a plain
    /// <c>hspace-markup</c>, i.e. it is NOT multiplied by magstep; measured on `C♭', where the
    /// symbol grows by the flat's scaled box plus this number exactly.
    /// </remarks>
    internal const double KernBeforeNarrowGlyph = 0.094725;

    /// <summary>
    /// One drawn piece of a chord symbol: a run of sans text, or one accidental glyph.
    /// </summary>
    /// <param name="Text">The text of a text piece; empty for a glyph piece.</param>
    /// <param name="Glyph">The Emmentaler codepoint of a glyph piece.</param>
    /// <param name="IsGlyph">True for an accidental glyph, false for text.</param>
    /// <param name="X">The piece's LEFT EDGE, relative to the symbol's origin.</param>
    /// <param name="Advance">How far the pen moves over this piece — for a glyph piece the
    /// kern plus the glyph's own box width, because a markup line concatenates by extent.</param>
    /// <param name="DrawX">Where the glyph's ORIGIN goes, relative to the symbol's origin. It
    /// is not <paramref name="X"/>: a flat's box starts 0.12 LEFT of its origin, so drawing at
    /// the piece's left edge would put the glyph that far too far right.</param>
    /// <param name="Raise">The glyph's baseline lift; 0 for text.</param>
    /// <param name="Bottom">The piece's ink bottom, already lifted.</param>
    /// <param name="Top">The piece's ink top, already lifted.</param>
    internal readonly record struct Piece(
        string Text, char Glyph, bool IsGlyph,
        double X, double Advance, double DrawX, double Raise,
        double Bottom, double Top);

    /// <summary>The em the glyph is drawn at — the music font's, at the smaller step.</summary>
    /// <remarks>
    /// LILYPOND-REF: lily/font-select.cc:115-186 select_font — the step picks the file and the
    /// size, which is what <see cref="EmmentalerDesignSize.Magstep"/> is the page-space factor
    /// for. <c>SharedRenderer.FontSize</c> is the full-size music em, so this is that em one
    /// step down, the same shape every other reduced music glyph in the tree is drawn at.
    /// </remarks>
    internal static double AccidentalGlyphEm(double staffFontSize)
        => staffFontSize * EmmentalerDesignSize.Magstep(AccidentalFontSizeStep);

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
    private static (char Glyph, GlyphMetrics.BBox Box) GlyphFor(int alteration)
    {
        var m = GlyphMetrics.AtFontSize(AccidentalFontSizeStep);
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

    /// <summary>The pieces of one chord symbol, left to right, with X from its origin.</summary>
    internal static ImmutableArray<Piece> Pieces(ScoreTextMetrics fonts, string text)
    {
        if (string.IsNullOrEmpty(text)) return ImmutableArray<Piece>.Empty;

        var pieces = ImmutableArray.CreateBuilder<Piece>();
        double em = EngravingDefaults.ChordNameFontSize;
        double magstep = EmmentalerDesignSize.Magstep(AccidentalFontSizeStep);
        double x = 0;
        int runStart = 0;

        void FlushText(int end)
        {
            if (end <= runStart) return;
            string run = text[runStart..end];
            double advance = fonts.Advance(
                run, em, TextRole.ChordName, EngravingDefaults.ChordNameFontStyle);
            var (bottom, top) = fonts.Ink(
                run, em, TextRole.ChordName, EngravingDefaults.ChordNameFontStyle);
            pieces.Add(new Piece(run, '\0', IsGlyph: false, x, advance, x, 0, bottom, top));
            x += advance;
        }

        for (int i = 0; i < text.Length;)
        {
            var accidental = AccidentalAt(text, i);
            if (accidental is null)
            {
                i++;
                continue;
            }
            var (length, alteration) = accidental.Value;
            FlushText(i);
            var (glyph, box) = GlyphFor(alteration);
            double kern = NarrowGlyph(alteration) ? KernBeforeNarrowGlyph : 0;
            double raise = (ShortGlyph(alteration) ? 0.3 : 0.6) * magstep;
            pieces.Add(new Piece(
                Text: "", glyph, IsGlyph: true,
                X: x, Advance: kern + box.Width,
                // The origin, not the left edge: the flat family's box reaches left of it.
                DrawX: x + kern - box.Left,
                raise, box.Bottom + raise, box.Top + raise));
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
    private static bool IsPlainText(string text) =>
        text.IndexOf('♯') < 0 && text.IndexOf('♭') < 0;

    /// <summary>The symbol's whole X extent, whose left edge is its reference point.</summary>
    internal static double Width(ScoreTextMetrics fonts, string text)
    {
        if (IsPlainText(text))
            return fonts.Advance(
                text, EngravingDefaults.ChordNameFontSize,
                TextRole.ChordName, EngravingDefaults.ChordNameFontStyle);
        double w = 0;
        foreach (var p in Pieces(fonts, text)) w += p.Advance;
        return w;
    }

    /// <summary>The symbol's ink about its baseline — the union of its pieces'.</summary>
    internal static (double Bottom, double Top) Ink(ScoreTextMetrics fonts, string text)
    {
        if (IsPlainText(text))
            return fonts.Ink(
                text, EngravingDefaults.ChordNameFontSize,
                TextRole.ChordName, EngravingDefaults.ChordNameFontStyle);
        double bottom = 0, top = 0;
        bool any = false;
        foreach (var p in Pieces(fonts, text))
        {
            bottom = any ? System.Math.Min(bottom, p.Bottom) : p.Bottom;
            top = any ? System.Math.Max(top, p.Top) : p.Top;
            any = true;
        }
        return any ? (bottom, top) : (0, 0);
    }
}
