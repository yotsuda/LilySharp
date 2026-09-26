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

using LilySharp.Core.Rendering;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// The metronome mark's drawn geometry, in ONE home: the note glyph's scale and
/// stem, the equation text's em and face, and the whole markup's ink about its
/// baseline. The renderer draws from it, <see cref="MusicMarkEngraver"/> rests the
/// mark on it, and <see cref="OutsideStaffStacker"/> reserves it — the same
/// quantity is never priced twice (the old centered width estimate, the bold 1.8
/// equation and the flat 1.8 half-extent were three drifting homes).
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/translation-functions.scm:100-151 format-metronome-markup / metronome-markup —
/// the markup is (concat (general-align Y DOWN (smaller (note-by-number ...))) " = "
/// count), all in the mark's plain upright text font; only a textual marking is
/// \bold, printed as "text (♩ = N)".
/// LILYPOND-REF: scm/define-markup-commands.scm:5393-5650 note-by-number — the
/// head glyph at magstep(font-size), an up stem of size-factor * max(3, log-1)
/// staff spaces from the head, dots after the head.
/// The DOWN alignment puts the note's ink BOTTOM on the markup baseline, so the
/// mark's ink bottom is the equation digits' own overshoot below that baseline —
/// which is exactly what aligned_side lands padding 0.8 above the staff ink
/// (ledger tempo.quiet.staff-to-baseline = 2.05 + 0.8 + 0.033010).
/// </remarks>
internal static class MetronomeMarkGeometry
{
    /// <summary>
    /// The mark's text em for THIS score: <see cref="EngravingDefaults.MetronomeMarkFontSize"/>
    /// unless the score's <c>fonts { }</c> wrote a <c>step</c> or <c>size</c> for <c>tempo</c>.
    /// Every piece of the markup — the marking, the parentheses, the equation — and every
    /// reservation of it reads this one call.
    /// </summary>
    public static double Em(ScoreTextMetrics fonts)
        => fonts.Size(TextRole.Tempo, EngravingDefaults.MetronomeMarkFontSize);

    /// <summary>The textual marking's weight and slant: <c>\bold</c> (format-metronome-markup)
    /// unless the score wrote a style for <c>tempo</c>.</summary>
    public static FontStyle TextStyle(ScoreTextMetrics fonts)
        => fonts.Style(TextRole.Tempo, FontStyle.Bold);

    /// <summary>The parentheses' and the equation's weight and slant: the mark's plain upright
    /// text font unless the score wrote a style for <c>tempo</c>.</summary>
    public static FontStyle PlainStyle(ScoreTextMetrics fonts)
        => fonts.Style(TextRole.Tempo, FontStyle.Regular);

    /// <summary>
    /// The note glyphs' scale: \smaller = magstep(-1) of the mark's OWN font-size — so a
    /// <c>fonts { tempo step … }</c> steps the note with the text, as an
    /// <c>\override MetronomeMark.font-size</c> does in LilyPond, where the markup's
    /// <c>\smaller</c> is relative to the grob's size.
    /// </summary>
    public static double NoteScale(ScoreTextMetrics fonts)
        => EngravingDefaults.MetronomeMarkNoteMagstep
           * EmmentalerDesignSize.Magstep(fonts.StepOf(TextRole.Tempo, EngravingDefaults.MetronomeMarkFontSize));

    /// <summary>The note glyphs' font size in staff spaces (nominal 4.0 x magstep(-1)).</summary>
    public static double NoteSize(ScoreTextMetrics fonts) => SharedRenderer.FontSize * NoteScale(fonts);

    /// <summary>duration log of a beat unit (1 = whole ... 16 = sixteenth).</summary>
    // LILYPOND-REF: lily/duration.cc — log2 of the denominator.
    public static int Log(int beatUnit) => beatUnit switch
    {
        <= 1 => 0,
        2 => 1,
        4 => 2,
        8 => 3,
        _ => 4,
    };

    /// <summary>The head glyph's bbox (unscaled, origin at its ink left / centre line).</summary>
    public static GlyphMetrics.BBox HeadBox(int beatUnit) => Log(beatUnit) switch
    {
        0 => GlyphMetrics.NoteheadWhole,
        1 => GlyphMetrics.NoteheadHalf,
        _ => GlyphMetrics.NoteheadBlack,
    };

    /// <summary>The head glyph note-by-number engraves for a beat unit: whole (1) =
    /// stemless whole head; 2 = hollow half; 4 and shorter = black head.</summary>
    // LILYPOND-REF: scm/define-markup-commands.scm:5439-5448 get-glyph-name-candidates
    //   — "noteheads.~a~a" with min(log, 2), the "s" series for the default style.
    public static char HeadGlyph(int beatUnit) => Log(beatUnit) switch
    {
        0 => EmmentalerGlyphs.NoteheadWhole,
        1 => EmmentalerGlyphs.NoteheadHalf,
        _ => EmmentalerGlyphs.NoteheadBlack,
    };

    /// <summary>Stem top above the HEAD CENTRE, scaled; 0 for the stemless whole.</summary>
    // LILYPOND-REF: scm/define-markup-commands.scm:5566-5569 note-by-number,
    // stem-length = size-factor * max(3, log-1); :5575 stemy = dir * stem-length
    // (measured in the head's own frame, whose origin is the head centre line).
    public static double StemTopAboveCentre(ScoreTextMetrics fonts, int beatUnit)
        => Log(beatUnit) > 0 ? Math.Max(3, Log(beatUnit) - 1) * NoteScale(fonts) : 0.0;

    /// <summary>Stem thickness, scaled (note-by-number stem-thickness 0.13).</summary>
    // LILYPOND-REF: scm/define-markup-commands.scm:5571-5574 note-by-number stem-thickness.
    public static double StemThickness(ScoreTextMetrics fonts) => 0.13 * NoteScale(fonts);

    /// <summary>
    /// The up-stem attachment point on the head, UNSCALED (staff spaces about the head
    /// origin): the font's own LILC attachment. X is the head's designed right edge —
    /// the stem's lower-RIGHT corner sits on it — and Y is where above the centre line
    /// the stem's lower end starts (0.186 on the black head, 0.259 on the half).
    /// </summary>
    // LILYPOND-REF: scm/define-markup-commands.scm:5564-5565 note-by-number
    //   attach-indices = ly:note-head::stem-attachment; :5576-5579 attach-off =
    //   interval-index of the head extents at those indices (= the attachment point
    //   itself); :5591-5606 the stem box from attach-off to stemy, its right corner on
    //   attach-off for an up stem. lily/note-head.cc:164-196 get_stem_attachment.
    public static (double X, double Y) StemAttachment(int beatUnit) => Log(beatUnit) switch
    {
        1 => GlyphMetrics.NoteheadHalfStemAttachment,
        _ => GlyphMetrics.NoteheadBlackStemAttachment,
    };

    /// <summary>
    /// The note's ink TOP above the markup baseline. The markup DOWN-aligns the note,
    /// so its head bottom sits ON the baseline; a stemmed unit tops out at the stem
    /// (plus the 8th flag's small rise above it), the whole note at its own head.
    /// </summary>
    public static double NoteTop(ScoreTextMetrics fonts, int beatUnit)
    {
        double scale = NoteScale(fonts);
        var box = HeadBox(beatUnit);
        double centre = -box.Bottom * scale;   // head centre above the baseline
        if (Log(beatUnit) == 0)
            return centre + box.Top * scale;
        double top = StemTopAboveCentre(fonts, beatUnit);
        if (Log(beatUnit) >= 3)
            top += GlyphMetrics.Flag8thUp.Top * scale;
        return centre + top;
    }

    /// <summary>The dot glyph's ink width (note-by-number's <c>dotwid</c>), scaled.</summary>
    // LILYPOND-REF: scm/define-markup-commands.scm:5607-5608 note-by-number —
    //   dotwid = interval-length (ly:stencil-extent dot X).
    public static double DotWidth(ScoreTextMetrics fonts) => GlyphMetrics.AugmentationDot.Width * NoteScale(fonts);

    /// <summary>
    /// X of the k-th augmentation dot's origin from the note's origin: the dot run
    /// starts one dotwid past the head's ink right and steps 2 x dotwid; an up-stem
    /// flagged unit shifts the run +0.5 to clear the flag.
    /// </summary>
    // LILYPOND-REF: scm/define-markup-commands.scm:5609-5614 note-by-number dots
    //   (2 x dotwid apart); :5674-5682 translate to head extent right + dotwid;
    //   :5664-5668 the +0.5 X shift for a short up-stem flag (dir 1 < 1.15).
    public static double DotX(ScoreTextMetrics fonts, int beatUnit, int k)
        => HeadBox(beatUnit).Right * NoteScale(fonts) + DotWidth(fonts) + 2 * k * DotWidth(fonts)
           + (Log(beatUnit) > 2 ? 0.5 : 0.0);

    /// <summary>The note piece's ink RIGHT edge from its origin: the head's width —
    /// widened by an 8th flag, whose ink hangs off the stem past the head (the concat
    /// advances by the note STENCIL's extent, flag included) — and by the dot run.</summary>
    public static double NoteRight(ScoreTextMetrics fonts, int beatUnit, int dots)
    {
        double scale = NoteScale(fonts);
        double right = HeadBox(beatUnit).Right * scale;
        if (Log(beatUnit) >= 3)
            right = Math.Max(right,
                right - StemThickness(fonts) / 2.0 + GlyphMetrics.Flag8thUp.Right * scale);
        if (dots > 0)
            right = Math.Max(right, DotX(fonts, beatUnit, dots - 1) + DotWidth(fonts));
        return right;
    }

    /// <summary>The equation string as drawn: "= N", closed with ")" after a textual
    /// marking ("Grave (♩ = 120)").</summary>
    public static string EquationText(string count, bool parenthesised)
        => "= " + count + (parenthesised ? ")" : "");

    /// <summary>
    /// Pen advance from the piece BEFORE a text run to the run's first VISIBLE glyph,
    /// when the run begins with a leading space (the concat's " = N" / " ("): measured
    /// INSIDE the single run — advance(" " + rest) − advance(rest) — because the run is
    /// one stencil in LilyPond's concat and its extent is one measurement, while the
    /// draw must carry the space as an offset (SVG collapses a drawn leading space).
    /// </summary>
    public static double LeadingSpaceAdvance(ScoreTextMetrics fonts, string rest)
        => fonts.Advance(" " + rest, Em(fonts), TextRole.Tempo, PlainStyle(fonts))
           - fonts.Advance(rest, Em(fonts), TextRole.Tempo, PlainStyle(fonts));

    /// <summary>What one piece of the swing feel-equation is.</summary>
    public enum SwingPieceKind
    {
        /// <summary>A black notehead glyph: origin (X0, Y0) = (head left, head centre).</summary>
        Head,
        /// <summary>A filled rectangle X0..X1 × Y0..Y1 — a stem or a beam.</summary>
        Rule,
        /// <summary>An up-flag glyph (<see cref="SwingPiece.Glyph"/>) at its origin (X0, Y0).</summary>
        Flag,
        /// <summary>A round-capped line (X0, Y0)→(X1, Y1) of the bracket thickness.</summary>
        BracketLine,
        /// <summary>The tuplet number "3", italic, pen start X0 on baseline Y0.</summary>
        Number,
        /// <summary>The "=" between the two rhythms, pen start X0 on the markup baseline.</summary>
        EqualsSign,
    }

    /// <summary>One piece of the swing feel-equation: X from the END of the count's text,
    /// Y up from the markup baseline.</summary>
    public readonly record struct SwingPiece(
        SwingPieceKind Kind, double X0, double Y0, double X1 = 0, double Y1 = 0, char Glyph = default);

    /// <summary>The swing feel-equation's whole geometry: its pieces, the note glyphs' font
    /// size, the bracket's thickness, the number's em, and the reach/ink it adds to the mark.</summary>
    public readonly record struct SwingEquation(
        SwingPiece[] Pieces, double GlyphSize, double BracketThickness, double NumberEm,
        double Width, double Top, double Bottom);

    /// <summary>
    /// The swing feel-equation drawn after the count, as LilyPond's own swing idiom writes
    /// it — the <c>\rhythm</c> doc example and the user's transcriptions:
    /// <c>\markup { … \hspace #0.4 \rhythm { 8[ 8] } = \rhythm { \tuplet 3/2 { 4 8 } } }</c>
    /// (sixteenths: <c>\rhythm { 16[ 16] } = \rhythm { \tuplet 3/2 { 8 16 } }</c>).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-markup-commands.scm:1920-1990 define-markup-command (rhythm …) —
    /// a one-system <c>\score</c> at <c>font-size -2</c> (magnifyStaff magstep(-2)), aligned
    /// X LEFT; ly/engraver-init.ly:1721-1763 the StandaloneRhythm contexts (cadenza,
    /// common-shortest-duration 1/10, no staff lines, squashedPosition 1 = head centre half
    /// a staff space above the baseline, stems up). The markup line joins its words with word-space 0.6 (\hspace #0.4 between
    /// the count and the first rhythm; the "=" a word of the markup's own bold text).
    /// ⚠️ The horizontal spacing and stem/beam/bracket heights inside each \rhythm are the
    /// output of LilyPond's spacing and beam engines, which Lily# does not run on a markup:
    /// they are MEASURED from LilyPond 2.26 SVG output (Lab sessions/p641/swing/lp-geom.svg),
    /// in units of the rhythm's own staff space, and reproduce it to 1e-3.
    /// </remarks>
    public static SwingEquation Swing(ScoreTextMetrics fonts, int subdivision)
    {
        const double WordSpace = 0.6;          // markup line word-space
        const double HSpace = 0.4;             // the idiom's \hspace #0.4
        // The \rhythm stencil's left edge sits this far right of its first head's origin
        // (measured: "=" pen end + word-space − head origin = −0.0721 at magstep(-2)).
        const double LeftInsetU = 0.0908;
        bool sixteenths = subdivision >= 16;
        double k = EmmentalerDesignSize.Magstep(
            fonts.StepOf(TextRole.Tempo, EngravingDefaults.MetronomeMarkFontSize));
        double r = Math.Pow(2.0, -2.0 / 6.0) * k;          // \rhythm's font-size -2
        double th = 0.13 * k;                               // Stem.thickness 1.3 x line 0.1
        double bracketTh = 0.16 * k;                        // TupletBracket.thickness 1.6 x 0.1
        var att = GlyphMetrics.NoteheadBlackStemAttachment;
        double headCentre = 0.5 * r;                        // squashedPosition 1
        var pieces = new List<SwingPiece>(20);

        double StemCentre(double head) => head + att.X * r - th / 2;
        void Note(double head, double stemTopU)
        {
            pieces.Add(new SwingPiece(SwingPieceKind.Head, head, headCentre,
                Glyph: EmmentalerGlyphs.NoteheadBlack));
            double sc = StemCentre(head);
            pieces.Add(new SwingPiece(SwingPieceKind.Rule, sc - th / 2, headCentre + att.Y * r,
                sc + th / 2, stemTopU * r));
        }

        // ── The straight pair, beamed: 8[ 8] or 16[ 16]. ──
        double h1 = WordSpace + HSpace + WordSpace - LeftInsetU * r;
        double h2 = h1 + (sixteenths ? 2.0508 : 2.8872) * r;
        double beamTopU = sixteenths ? 3.1770 : 2.8230;     // stem top = upper beam's centre
        Note(h1, beamTopU);
        Note(h2, beamTopU);
        double beamL = StemCentre(h1) - th / 2, beamR = StemCentre(h2) + th / 2;
        double halfBeam = 0.48 * r / 2;
        pieces.Add(new SwingPiece(SwingPieceKind.Rule, beamL, beamTopU * r - halfBeam,
            beamR, beamTopU * r + halfBeam));
        if (sixteenths)
            pieces.Add(new SwingPiece(SwingPieceKind.Rule, beamL, 2.3541 * r - halfBeam,
                beamR, 2.3541 * r + halfBeam));

        // ── "=" — a word of the markup's bold text. ──
        double eqX = beamR + WordSpace;
        pieces.Add(new SwingPiece(SwingPieceKind.EqualsSign, eqX, 0));
        double eqEnd = eqX + fonts.Advance("=", Em(fonts), TextRole.Tempo, TextStyle(fonts));

        // ── The triplet: \tuplet 3/2 { 4 8 } or { 8 16 }, flagged, under a bracket. ──
        const double TupletStemTopU = 3.7501, FlagOriginU = 3.6997;
        double t1 = eqEnd + WordSpace - LeftInsetU * r;
        double t2 = t1 + (sixteenths ? 2.6774 : 3.3852) * r;
        Note(t1, sixteenths ? TupletStemTopU : 3.6666);
        Note(t2, TupletStemTopU);
        if (sixteenths)
            pieces.Add(new SwingPiece(SwingPieceKind.Flag, StemCentre(t1) + th / 2, FlagOriginU * r,
                Glyph: EmmentalerGlyphs.Flag8thUp));
        var lastFlag = sixteenths ? GlyphMetrics.Flag16thUp : GlyphMetrics.Flag8thUp;
        double flagX = StemCentre(t2) + th / 2;
        pieces.Add(new SwingPiece(SwingPieceKind.Flag, flagX, FlagOriginU * r,
            Glyph: sixteenths ? EmmentalerGlyphs.Flag16thUp : EmmentalerGlyphs.Flag8thUp));

        // The bracket spans the stems, its number centred in a gap of the line.
        double bL = StemCentre(t1) - 0.2818 * r, bR = StemCentre(t2) + 0.2818 * r;
        double bY = 5.0357 * r, hook = 0.7 * r;             // TupletBracket.edge-height 0.7
        double bC = (bL + bR) / 2;
        pieces.Add(new SwingPiece(SwingPieceKind.BracketLine, bL, bY - hook, bL, bY));
        pieces.Add(new SwingPiece(SwingPieceKind.BracketLine, bL, bY, bC - 0.9341 * r, bY));
        pieces.Add(new SwingPiece(SwingPieceKind.BracketLine, bC + 1.1861 * r, bY, bR, bY));
        pieces.Add(new SwingPiece(SwingPieceKind.BracketLine, bR, bY, bR, bY - hook));
        pieces.Add(new SwingPiece(SwingPieceKind.Number, bC - 0.4301 * r, 4.5818 * r));

        double width = Math.Max(bR + bracketTh / 2, flagX + lastFlag.Right * r);
        double top = Math.Max(bY + bracketTh / 2, FlagOriginU * r + lastFlag.Top * r);
        double bottom = Math.Min(0.0, headCentre + GlyphMetrics.NoteheadBlack.Bottom * r);
        return new SwingEquation(pieces.ToArray(), SharedRenderer.FontSize * r, bracketTh,
            Em(fonts) * Math.Pow(2.0, -4.0 / 6.0),           // TupletNumber font-size -2 inside -2
            width, top, bottom);
    }

    /// <summary>
    /// The whole mark's ink about its (left, baseline) origin: total advance width,
    /// ink top and ink bottom (negative below the baseline). Mirrors the draw's
    /// left-to-right concat: [bold text " ("] note " = N" [")"] [swing].
    /// </summary>
    public static (double Width, double Top, double Bottom) Ink(
        ScoreTextMetrics fonts,
        string count, string? tempoText, int beatUnit, int dots, int swingSubdivision)
    {
        double em = Em(fonts);
        var textStyle = TextStyle(fonts);
        var plainStyle = PlainStyle(fonts);
        double x = 0.0, top = 0.0, bottom = 0.0;
        bool hasMetronome = count.Length > 0;
        if (tempoText != null)
        {
            var tInk = fonts.Ink(tempoText, em, TextRole.Tempo, textStyle);
            top = Math.Max(top, tInk.Top);
            bottom = Math.Min(bottom, tInk.Bottom);
            x += fonts.Advance(tempoText, em, TextRole.Tempo, textStyle);
            if (!hasMetronome)
                return (x, top, bottom);
            var pInk = fonts.Ink("(", em, TextRole.Tempo, plainStyle);
            top = Math.Max(top, pInk.Top);
            bottom = Math.Min(bottom, pInk.Bottom);
            x += fonts.Advance(" (", em, TextRole.Tempo, plainStyle);
        }
        // The note: bottom ON the baseline (DOWN-aligned), top at its stem/head.
        top = Math.Max(top, NoteTop(fonts, beatUnit));
        x += NoteRight(fonts, beatUnit, dots);
        // " = N" — ONE text run whose leading space is the concat's separator, so its
        // advance is one measurement of the whole string, as one stencil's extent is.
        string eq = EquationText(count, tempoText != null);
        var eqInk = fonts.Ink(eq, em, TextRole.Tempo, plainStyle);
        top = Math.Max(top, eqInk.Top);
        bottom = Math.Min(bottom, eqInk.Bottom);
        x += fonts.Advance(" " + eq, em, TextRole.Tempo, plainStyle);
        if (swingSubdivision != 0)
        {
            var swing = Swing(fonts, swingSubdivision);
            x += swing.Width;
            top = Math.Max(top, swing.Top);
            bottom = Math.Min(bottom, swing.Bottom);
        }
        return (x, top, bottom);
    }

    /// <summary>
    /// The quiet resting BASELINE above the staff middle: aligned_side pays the mark's
    /// padding against its supports — and metronome-engraver.cc makes the STAVES the
    /// supports — so the stencil bottom lands at staff ink + 0.8 and the baseline rides
    /// the mark's own ink bottom above that (ledger tempo.quiet.staff-to-baseline
    /// = 2.05 + 0.8 + 0.033010, to the digit).
    /// </summary>
    // LILYPOND-REF: lily/side-position-interface.cc:361-370 aligned_side, padding paid
    // against the support extent — offset = support edge + padding − ext[DOWN], no
    // clamp (a positive ext[DOWN] cannot arise here anyway: the DOWN-aligned note
    // pins the ink bottom at ≤ 0);
    // lily/metronome-engraver.cc:136-139 stop_translation_timestep — side-support-elements = stavesFound.
    public static double QuietBaselineAboveMiddle(double inkBottom)
        => 2.0 + EngravingDefaults.StaffLineThickness / 2.0
           + EngravingDefaults.MetronomeMarkPadding - inkBottom;
}
