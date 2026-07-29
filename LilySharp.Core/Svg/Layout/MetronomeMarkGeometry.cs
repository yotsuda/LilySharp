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
    /// <summary>The note glyphs' scale: \smaller = magstep(-1).</summary>
    public static double NoteScale => EngravingDefaults.MetronomeMarkNoteMagstep;

    /// <summary>The note glyphs' font size in staff spaces (nominal 4.0 x magstep(-1)).</summary>
    public static double NoteSize => SharedRenderer.FontSize * NoteScale;

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
    public static double StemTopAboveCentre(int beatUnit)
        => Log(beatUnit) > 0 ? Math.Max(3, Log(beatUnit) - 1) * NoteScale : 0.0;

    /// <summary>Stem thickness, scaled (note-by-number stem-thickness 0.13).</summary>
    // LILYPOND-REF: scm/define-markup-commands.scm:5571-5574 note-by-number stem-thickness.
    public static double StemThickness => 0.13 * NoteScale;

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
    public static double NoteTop(int beatUnit)
    {
        var box = HeadBox(beatUnit);
        double centre = -box.Bottom * NoteScale;   // head centre above the baseline
        if (Log(beatUnit) == 0)
            return centre + box.Top * NoteScale;
        double top = StemTopAboveCentre(beatUnit);
        if (Log(beatUnit) >= 3)
            top += GlyphMetrics.Flag8thUp.Top * NoteScale;
        return centre + top;
    }

    /// <summary>The dot glyph's ink width (note-by-number's <c>dotwid</c>), scaled.</summary>
    // LILYPOND-REF: scm/define-markup-commands.scm:5607-5608 note-by-number —
    //   dotwid = interval-length (ly:stencil-extent dot X).
    public static double DotWidth => GlyphMetrics.AugmentationDot.Width * NoteScale;

    /// <summary>
    /// X of the k-th augmentation dot's origin from the note's origin: the dot run
    /// starts one dotwid past the head's ink right and steps 2 x dotwid; an up-stem
    /// flagged unit shifts the run +0.5 to clear the flag.
    /// </summary>
    // LILYPOND-REF: scm/define-markup-commands.scm:5609-5614 note-by-number dots
    //   (2 x dotwid apart); :5674-5682 translate to head extent right + dotwid;
    //   :5664-5668 the +0.5 X shift for a short up-stem flag (dir 1 < 1.15).
    public static double DotX(int beatUnit, int k)
        => HeadBox(beatUnit).Right * NoteScale + DotWidth + 2 * k * DotWidth
           + (Log(beatUnit) > 2 ? 0.5 : 0.0);

    /// <summary>The note piece's ink RIGHT edge from its origin: the head's width —
    /// widened by an 8th flag, whose ink hangs off the stem past the head (the concat
    /// advances by the note STENCIL's extent, flag included) — and by the dot run.</summary>
    public static double NoteRight(int beatUnit, int dots)
    {
        double right = HeadBox(beatUnit).Right * NoteScale;
        if (Log(beatUnit) >= 3)
            right = Math.Max(right,
                right - StemThickness / 2.0 + GlyphMetrics.Flag8thUp.Right * NoteScale);
        if (dots > 0)
            right = Math.Max(right, DotX(beatUnit, dots - 1) + DotWidth);
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
    public static double LeadingSpaceAdvance(string rest)
        => TextFontMetrics.Serif(" " + rest, EngravingDefaults.MetronomeMarkFontSize)
           - TextFontMetrics.Serif(rest, EngravingDefaults.MetronomeMarkFontSize);

    /// <summary>Reach of the swing feel-equation drawn right of the count (lead gap +
    /// the drawn pairs). LILYSHARP-OWN: the shuffle equation is Lily#'s own device with
    /// no LilyPond counterpart; this is the reservation estimate its consumers share.</summary>
    public const double SwingEquationReach = 0.8 + 5.0;

    /// <summary>
    /// The whole mark's ink about its (left, baseline) origin: total advance width,
    /// ink top and ink bottom (negative below the baseline). Mirrors the draw's
    /// left-to-right concat: [bold text " ("] note " = N" [")"] [swing].
    /// </summary>
    public static (double Width, double Top, double Bottom) Ink(
        string count, string? tempoText, int beatUnit, int dots, int swingSubdivision)
    {
        double em = EngravingDefaults.MetronomeMarkFontSize;
        double x = 0.0, top = 0.0, bottom = 0.0;
        bool hasMetronome = count.Length > 0;
        if (tempoText != null)
        {
            var tInk = TextFontMetrics.Ink(tempoText, em, sans: false, FontStyle.Bold);
            top = Math.Max(top, tInk.Top);
            bottom = Math.Min(bottom, tInk.Bottom);
            x += TextFontMetrics.SerifBold(tempoText, em);
            if (!hasMetronome)
                return (x, top, bottom);
            var pInk = TextFontMetrics.Ink("(", em);
            top = Math.Max(top, pInk.Top);
            bottom = Math.Min(bottom, pInk.Bottom);
            x += TextFontMetrics.Serif(" (", em);
        }
        // The note: bottom ON the baseline (DOWN-aligned), top at its stem/head.
        top = Math.Max(top, NoteTop(beatUnit));
        x += NoteRight(beatUnit, dots);
        // " = N" — ONE text run whose leading space is the concat's separator, so its
        // advance is one measurement of the whole string, as one stencil's extent is.
        string eq = EquationText(count, tempoText != null);
        var eqInk = TextFontMetrics.Ink(eq, em);
        top = Math.Max(top, eqInk.Top);
        bottom = Math.Min(bottom, eqInk.Bottom);
        x += TextFontMetrics.Serif(" " + eq, em);
        if (swingSubdivision != 0)
            x += SwingEquationReach;
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
