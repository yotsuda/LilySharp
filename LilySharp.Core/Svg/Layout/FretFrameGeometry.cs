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
using System.Collections.Immutable;
using System.Globalization;
using LilySharp.Core.Rendering;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// The dimensions of a chord diagram (<c>@diagram(…)</c>, a chord's shape) — ONE home for the
/// drawing (<c>SharedRenderer.DrawFretFrame</c>) and the box the layout reserves for it
/// (<c>ArticulationEngraver.FrameBox</c>, <c>ChordNameEngraver.DiagramBox</c>,
/// <c>HeaderBand.WithChordList</c>), so the two cannot drift.
/// </summary>
/// <remarks>
/// <para>
/// LilyPond's fret diagram (scm/fret-diagrams.scm make-fret-diagram) at its default
/// <c>size 1.0</c>, transcribed since 2026-09-29 (session 668; until then Lily# drew its own
/// arithmetic "at LilyPond's proportions"): one staff space between strings and between frets,
/// four fret rows, the strings running one more fret past the last row (<c>string-overhang</c>),
/// the nut three line thicknesses thick, a dot 0.6 of the way down its fret space, a curved barre
/// over the dots it spans, an <c>X</c> / <c>O</c> set in the sans face over each muted / open
/// string, the base fret's "Nfr" to the right, and — where the diagram is a <c>FretBoard</c>, a
/// chords row's — the finger numbers under the strings. Every length multiplies by <c>size</c>,
/// which is the score's <c>fonts { diagram step ±n }</c> (or <c>size n</c>, the label's em): the
/// same ratio the label's em takes (<see cref="Scale"/>), so the label and the grid move as one.
/// Owner's decision, session 646 (the size); the transcription is HANDOFF §2 K5 ①'s tail.
/// </para>
/// <para>
/// LILYPOND-REF: scm/fret-diagrams.scm:261-323 make-fret-diagram — size 1.0, fret-distance 1.0,
///   string-distance 1.0, fret-count 4, finger-code none, dot-radius 0.25, dot-position 0.6,
///   th = line-thickness × thickness 0.5, xo-padding 0.2, barre-type curved, string-overhang 1.0.
/// LILYPOND-REF: scm/define-grobs.scm:1678 FretBoard — fret-diagram-details finger-code below-string,
///   which is why a chords row's diagram carries its finger numbers and a markup diagram does not.
/// </para>
/// <para>
/// THE SPEC. A diagram is handed to the page as one string: one character per string, LOW string
/// first — <c>x</c> muted, <c>o</c> / <c>0</c> open, a digit the fret, <c>a</c>–<c>f</c> frets
/// 10–15 (<see cref="FretAt"/>) — and, for a shape LilyPond's predefined table fingers, a detail
/// suffix: <c>|</c> then one finger digit per string (<c>0</c> none), then <c>|</c> and the
/// barres as <c>FROM-TO@FRET</c> in LilyPond's string numbers (1 = the highest), comma separated
/// (<c>133211|134211|6-1@1</c>; <see cref="Detailed"/>, <see cref="FingerAt"/>,
/// <see cref="Barres"/>). A written shape has no suffix (nobody writes fingers), so it draws
/// dots alone — as LilyPond's terse string of it does.
/// </para>
/// </remarks>
internal static class FretFrameGeometry
{
    /// <summary>The text font's em before a magnification: 11 pt at staff size 20
    /// (<see cref="MusicMarkEngraver.TextFontEm"/>).</summary>
    internal const double TextEm = MusicMarkEngraver.TextFontEm;

    // ---- LilyPond's fret-diagram-details defaults ----------------------------------------
    // LILYPOND-REF: scm/fret-diagrams.scm:767 draw-xo — xo-font-magnification 0.4
    internal const double XoMagnification = 0.4;
    // LILYPOND-REF: scm/fret-diagrams.scm:310 make-fret-diagram — xo-padding 0.2
    internal const double XoPadding = 0.2;
    // LILYPOND-REF: scm/fret-diagrams.scm:816-852 label-fret — fret-label-font-mag 0.5, label-space
    //   0.5 × size, fret-label-horizontal-offset 0.2, fret-label-vertical-offset −0.5, format "~dfr"
    internal const double LabelMagnification = 0.5;
    internal const double LabelSpace = 0.5;
    internal const double LabelHorizontalOffset = 0.2;
    internal const double LabelVerticalOffset = -0.5;
    // LILYPOND-REF: scm/fret-diagrams.scm:540-551 draw-dots — finger-label-padding 0.3,
    //   string-label-font-mag 0.6 (normal orientation)
    internal const double FingerMagnification = 0.6;
    internal const double FingerLabelPadding = 0.3;
    // LILYPOND-REF: scm/fret-diagrams.scm:289-296 make-fret-diagram — default-dot-radius 0.25 and
    //   default-dot-position 0.6 when the finger code is none or below-string (0.425 and 0.525 only for in-dot)
    internal const double DotRadius = 0.25;
    internal const double DotPosition = 0.6;
    // LILYPOND-REF: scm/fret-diagrams.scm:302-306 make-fret-diagram — th = line-thickness × thickness (props 0.5), sth = size × th
    internal const double Thickness = 0.5;
    // LILYPOND-REF: scm/fret-diagrams.scm:319-320 make-fret-diagram — string-overhang 1.0
    internal const double StringOverhang = 1.0;
    // LILYPOND-REF: scm/fret-diagrams.scm:744-745 draw-thick-zero-fret — top-fret-thickness 3.0
    internal const double TopFretThickness = 3.0;
    // LILYPOND-REF: scm/fret-diagrams.scm:516-533 make-curved-barre-stencil — bezier-height 0.5,
    //   bezier-thick 0.1, both × size
    internal const double BarreHeight = 0.5;
    internal const double BarreThick = 0.1;

    /// <summary>Fret rows drawn (LilyPond's default <c>fret-count</c>).</summary>
    internal const int FretRows = 4;

    /// <summary>The "Nfr" label's ENGRAVING em at <c>size 1</c>: the text em at the label's
    /// magnification, 2.2 × 0.5.</summary>
    internal const double LabelEm = TextEm * LabelMagnification;

    /// <summary>LilyPond's <c>size</c> for this score: 1 with no directive, 2 at
    /// <c>diagram step +6</c>.</summary>
    internal static double Scale(ScoreTextMetrics fonts)
        => fonts.Size(TextRole.FretFrame, LabelEm) / LabelEm;

    /// <summary>The line thickness <c>sth</c> at size <paramref name="s"/>: the strings, the
    /// frets, and the ring around a dot.</summary>
    internal static double LineThickness(double s) => EngravingDefaults.LineThickness * Thickness * s;

    // ---------------------------------------------------------------- the spec

    /// <summary>The number of strings a spec draws: its characters before the detail suffix.</summary>
    internal static int Strings(string spec)
    {
        int bar = spec.IndexOf('|');
        return bar < 0 ? spec.Length : bar;
    }

    /// <summary>
    /// The fret on string <paramref name="i"/> of a spec (LOW string first): −1 muted
    /// (<c>x</c>), 0 open (<c>o</c> or <c>0</c>), else the fret.
    /// </summary>
    /// <remarks>
    /// ⚠️ TWO ALPHABETS, ONE READER. A shape at frets 10–15 — Lily#'s order's
    /// (<c>Music.ChordVoicings</c>), or one WRITTEN dash-separated (<c>@diagram(x-x-10-12-13-11)</c>,
    /// <c>Cm(8-10-10-8-8-8)</c>, owner's decision 2026-09-28; read by
    /// <c>Music.ChordShapes.TryRead</c>) — cannot be spelled one digit per string, so the spec
    /// handed to the page carries those frets as <c>a</c>–<c>f</c> (<c>8aa988</c> is
    /// 8-10-10-9-8-8; <c>Music.ChordVoicings.ToFrameSpec</c> writes it). A writer cannot type
    /// those letters — the written gate (<c>Semantics.AnnotationValues.Frame</c>) refuses them —
    /// so the internal alphabet never meets a user's spelling. The drawing, the reservation, the twin and MusicXML all read a fret
    /// through here, so none of them can read the two alphabets differently.
    /// </remarks>
    internal static int FretAt(string spec, int i) => spec[i] switch
    {
        'x' => -1,
        'o' => 0,
        >= '0' and <= '9' and var d => d - '0',
        >= 'a' and <= 'f' and var h => h - 'a' + 10,
        _ => -1,
    };

    /// <summary>The finger on string <paramref name="i"/> (LOW string first), 0 for none or
    /// for a spec with no detail suffix.</summary>
    internal static int FingerAt(string spec, int i)
    {
        int n = Strings(spec);
        int at = n + 1 + i;
        if (at >= spec.Length || spec[at] == '|')
            return 0;
        char ch = spec[at];
        return ch is >= '1' and <= '4' ? ch - '0' : 0;
    }

    /// <summary>Whether any string of the spec carries a finger.</summary>
    internal static bool HasFingers(string spec)
    {
        int n = Strings(spec);
        for (int i = 0; i < n; i++)
            if (FingerAt(spec, i) > 0)
                return true;
        return false;
    }

    /// <summary>One barre of a spec, as string INDICES (LOW string first, from ≤ to) at a fret
    /// as written (not shifted by the base fret).</summary>
    internal readonly record struct BarreSpan(int From, int To, int Fret);

    /// <summary>The barres of a spec (its second detail section), in string indices.</summary>
    internal static IReadOnlyList<BarreSpan> Barres(string spec)
    {
        int n = Strings(spec);
        if (n >= spec.Length)
            return [];
        int second = spec.IndexOf('|', n + 1);
        if (second < 0 || second + 1 >= spec.Length)
            return [];
        var list = new List<BarreSpan>();
        foreach (var item in spec[(second + 1)..].Split(','))
        {
            int dash = item.IndexOf('-'), at = item.IndexOf('@');
            if (dash <= 0 || at <= dash
                || !int.TryParse(item[..dash], NumberStyles.None, CultureInfo.InvariantCulture, out int s1)
                || !int.TryParse(item[(dash + 1)..at], NumberStyles.None, CultureInfo.InvariantCulture, out int s2)
                || !int.TryParse(item[(at + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int fret))
                continue;
            // LilyPond numbers strings from the highest (1); the spec indexes from the lowest.
            int a = n - s1, b = n - s2;
            if (a < 0 || b < 0 || a >= n || b >= n)
                continue;
            list.Add(new BarreSpan(System.Math.Min(a, b), System.Math.Max(a, b), fret));
        }
        return list;
    }

    /// <summary>
    /// A spec with the detail suffix: <paramref name="frets"/> (the bare spec) plus the
    /// <paramref name="fingers"/> (one per string, 0 none) and <paramref name="barres"/>
    /// (LilyPond's string numbers) of a predefined shape — the bare spec when both are empty.
    /// </summary>
    internal static string Detailed(string frets, ImmutableArray<int> fingers,
        ImmutableArray<Music.PredefinedFretboards.Barre> barres)
    {
        bool anyFinger = false;
        foreach (int f in fingers)
            anyFinger |= f > 0;
        if (!anyFinger && barres.IsDefaultOrEmpty)
            return frets;
        var sb = new System.Text.StringBuilder(frets).Append('|');
        for (int i = 0; i < frets.Length; i++)
            sb.Append(i < fingers.Length && fingers[i] is > 0 and <= 4 ? (char)('0' + fingers[i]) : '0');
        if (!barres.IsDefaultOrEmpty)
        {
            sb.Append('|');
            for (int k = 0; k < barres.Length; k++)
            {
                if (k > 0)
                    sb.Append(',');
                var b = barres[k];
                sb.Append(b.FirstString.ToString(CultureInfo.InvariantCulture)).Append('-')
                  .Append(b.LastString.ToString(CultureInfo.InvariantCulture)).Append('@')
                  .Append(b.Fret.ToString(CultureInfo.InvariantCulture));
            }
        }
        return sb.ToString();
    }

    /// <summary>The fret the grid starts at: 1, or the lowest fretted note when a fretted
    /// note lies beyond the 4th fret (the shape is shifted and labelled "Nfr").</summary>
    /// <remarks>
    /// LILYPOND-REF: scm/fret-diagrams.scm:233-254 fret-parse-marking-list — "calculate
    ///   fret-range": when <c>maxfret &gt; my-fret-count</c> the range becomes
    ///   <c>(minfret . max(minfret + fret-count − 1, maxfret))</c>, else <c>(1 . fret-count)</c>;
    ///   the dots are then counted from <c>(1- (car fret-range))</c>. Open strings are not dots.
    /// ⚠️ Until 2026-09-28 Lily# shifted only when EVERY fretted note was above the 4th fret
    /// (<c>minFret &gt; 4</c>), so a shape straddling it — <c>x35553</c>, <c>x35343</c> — kept
    /// the grid at fret 1 and its 5th-fret dots fell off the bottom row undrawn. No book wrote
    /// such a diagram (measured: no <c>@diagram(</c> in any <c>.lys</c> of the repo or of the
    /// Lab corpora), and the shapes Lily#'s order yields are often exactly that, so
    /// LilyPond's rule came in with them.
    /// </remarks>
    internal static int BaseFret(string spec)
    {
        var (min, max) = FrettedRange(spec);
        return max > FretRows ? min : 1;
    }

    /// <summary>The fret rows the grid draws: <see cref="FretRows"/>, or more when a shifted
    /// shape spans further (LilyPond's <c>fret-range</c> above — a span of five frets draws
    /// five rows rather than dropping a dot).</summary>
    internal static int RowCount(string spec)
    {
        var (min, max) = FrettedRange(spec);
        return max > FretRows ? System.Math.Max(FretRows, max - min + 1) : FretRows;
    }

    private static (int Min, int Max) FrettedRange(string spec)
    {
        int min = int.MaxValue, max = 0;
        int n = Strings(spec);
        for (int i = 0; i < n; i++)
        {
            int f = FretAt(spec, i);
            if (f <= 0)
                continue;
            min = System.Math.Min(min, f);
            max = System.Math.Max(max, f);
        }
        return (min == int.MaxValue ? 1 : min, max);
    }

    // ---------------------------------------------------------------- the measured diagram

    /// <summary>A piece of text the diagram sets — an <c>X</c>, an <c>O</c>, a finger, the
    /// "Nfr" — with its ink about its pen origin, so the drawing can centre it as LilyPond's
    /// <c>centered-stencil</c> does.</summary>
    internal readonly record struct Label(string Text, double Em, double InkLeft, double InkRight,
        double InkBottom, double InkTop)
    {
        internal double Width => InkRight - InkLeft;
        internal double Height => InkTop - InkBottom;
        /// <summary>The pen X that puts the ink's centre at <paramref name="cx"/>.</summary>
        internal double PenX(double cx) => cx - (InkLeft + InkRight) / 2;
        /// <summary>The baseline that puts the ink's centre at <paramref name="cy"/> (Y-up).</summary>
        internal double Baseline(double cy) => cy - (InkBottom + InkTop) / 2;
    }

    /// <summary>
    /// Everything the drawing and the reservation share, measured once from a spec at the
    /// score's size, in staff spaces about the GRID BOTTOM centre (Y-up). Fret <c>k</c>'s line
    /// is at <c>Rows − k</c> spacings above the anchor; string <c>i</c> at
    /// <c>(i − (Strings − 1) / 2)</c> spacings right of it.
    /// </summary>
    /// <param name="Size">LilyPond's <c>size</c> (<see cref="Scale"/>).</param>
    /// <param name="Strings">The string count.</param>
    /// <param name="Rows">The fret rows (<see cref="RowCount"/>).</param>
    /// <param name="BaseFret">The fret the grid starts at (<see cref="FretFrameGeometry.BaseFret(string)"/>).</param>
    /// <param name="Th">The line thickness <c>sth</c>.</param>
    /// <param name="AboveTop">How far the ink reaches above the top fret's line: the nut's box at
    /// fret 1 (three thicknesses, of which half a thickness hangs below the line), else half a line.</param>
    /// <param name="XoCentre">The <c>X</c> / <c>O</c> row: the ink centre's height above the grid
    /// bottom, or null when every string is fretted (LilyPond draws no row then).</param>
    /// <param name="XoHeight">The height of the tallest <c>X</c> / <c>O</c>, whose half is the
    /// row's reach each side of <paramref name="XoCentre"/>.</param>
    /// <param name="XoHalfWidth">The widest <c>X</c> / <c>O</c>'s half width — the ink each side
    /// of an end string.</param>
    /// <param name="X">The <c>X</c> glyph, when a string is muted.</param>
    /// <param name="O">The <c>O</c> glyph, when a string is open.</param>
    /// <param name="FretLabel">The "Nfr" label, or null at the first position.</param>
    /// <param name="LabelCentreX">The label's ink centre, right of the anchor.</param>
    /// <param name="LabelCentreY">The label's ink centre, above the anchor.</param>
    /// <param name="Fingers">The finger labels under the strings (a FretBoard's), one per string
    /// or null.</param>
    /// <param name="FingerTop">The line the finger labels' ink TOPS stand on, below the grid
    /// bottom (Y-up, negative).</param>
    /// <param name="Box">The ink box about the anchor.</param>
    internal sealed record Measured(
        double Size, int Strings, int Rows, int BaseFret, double Th, double AboveTop,
        double? XoCentre, double XoHeight, double XoHalfWidth, Label? X, Label? O,
        Label? FretLabel, double LabelCentreX, double LabelCentreY,
        ImmutableArray<Label?> Fingers, double FingerTop,
        GlyphMetrics.BBox Box);

    /// <summary>The spec measured at the score's size — <paramref name="fingers"/> for a
    /// FretBoard (a chords row's diagram), which sets its finger numbers under the strings; a
    /// markup diagram (an <c>@chord</c>'s, the chord list's) does not.</summary>
    internal static Measured Measure(string? spec, ScoreTextMetrics fonts, bool fingers = false)
    {
        spec ??= "xxxxxx";
        double s = Scale(fonts);
        int n = System.Math.Max(1, Strings(spec));
        int rows = RowCount(spec);
        int baseFret = BaseFret(spec);
        double th = LineThickness(s);
        double half = th / 2;
        double gridTop = rows * s;
        var style = fonts.Style(TextRole.FretFrame, FontStyle.Regular);

        Label Set(string text, double magnification)
        {
            double em = TextEm * magnification * s;
            var (l, r) = fonts.InkSpan(text, em, TextRole.FretFrame, style);
            var (b, t) = fonts.Ink(text, em, TextRole.FretFrame, style);
            return new Label(text, em, l, r, b, t);
        }

        // The nut (draw-thick-zero-fret): a box from half a thickness under fret 0's line to
        // top-fret-thickness above that — at the first position only; elsewhere fret 0 is an
        // ordinary line, half a thickness each side.
        // LILYPOND-REF: scm/fret-diagrams.scm:737-762 draw-thick-zero-fret — half-thick, top-fret-thick
        double aboveTop = baseFret == 1 ? TopFretThickness * th - half : half;

        // X / O over the muted / open strings (draw-xo): each glyph centred on its string, the
        // row's centre line xo-padding over the diagram's top plus the row's own half height —
        // the union of the centred glyphs, so the tallest sets it.
        // LILYPOND-REF: scm/fret-diagrams.scm:764-801 draw-xo — centered-stencil, xo-fret-offset, xo-padding
        bool anyX = false, anyO = false;
        for (int i = 0; i < n; i++)
        {
            int f = FretAt(spec, i);
            anyX |= f < 0;
            anyO |= f == 0;
        }
        Label? x = anyX ? Set("X", XoMagnification) : null;
        Label? o = anyO ? Set("O", XoMagnification) : null;
        double xoHeight = System.Math.Max(x?.Height ?? 0, o?.Height ?? 0);
        double xoHalfWidth = System.Math.Max(x?.Width ?? 0, o?.Width ?? 0) / 2;
        double? xoCentre = anyX || anyO ? gridTop + aboveTop + XoPadding * s + xoHeight / 2 : null;

        // "Nfr" (label-fret): centred half a fret under the top line, its ink starting
        // label-space + horizontal-offset right of the last string.
        Label? fretLabel = baseFret > 1 ? Set(baseFret.ToString(CultureInfo.InvariantCulture) + "fr", LabelMagnification) : null;
        double labelCentreX = fretLabel is { } fl
            ? (n - 1) * s + LabelSpace * s + LabelHorizontalOffset * s + fl.Width / 2
            : 0;
        double labelCentreY = gridTop - (1 + LabelVerticalOffset) * s;

        // The finger numbers (draw-dots, finger-code below-string): each centred on its string,
        // its top finger-label-padding under the strings' overhang end.
        // LILYPOND-REF: scm/fret-diagrams.scm:695-728 draw-dots below-string — finger-label-fret-coordinate
        var fingerLabels = ImmutableArray.CreateBuilder<Label?>(n);
        double fingerTop = -(StringOverhang * s + FingerLabelPadding * s);
        double fingerDepth = 0;
        for (int i = 0; i < n; i++)
        {
            int finger = fingers && FretAt(spec, i) > 0 ? FingerAt(spec, i) : 0;
            Label? label = finger > 0 ? Set(finger.ToString(CultureInfo.InvariantCulture), FingerMagnification) : null;
            fingerLabels.Add(label);
            fingerDepth = System.Math.Max(fingerDepth, label?.Height ?? 0);
        }

        // The box: the strings' own reach (half a thickness past the end strings, the overhang
        // and half a thickness under the last fret), the X / O row, the label, the fingers.
        double reach = System.Math.Max(half, xoHalfWidth);
        double left = -reach;
        double right = (n - 1) * s + reach;
        if (fretLabel is { } fl2)
            right = System.Math.Max(right, labelCentreX + fl2.Width / 2);
        double top = xoCentre is { } xc ? xc + xoHeight / 2 : gridTop + aboveTop;
        double bottom = -(StringOverhang * s + half);
        if (fingerDepth > 0)
            bottom = System.Math.Min(bottom, fingerTop - fingerDepth);
        // About the grid centre, as the drawing anchors it.
        double shift = (n - 1) * s / 2;
        var box = new GlyphMetrics.BBox(left - shift, bottom, right - shift, top);

        return new Measured(s, n, rows, baseFret, th, aboveTop, xoCentre, xoHeight, xoHalfWidth,
            x, o, fretLabel, labelCentreX - shift, labelCentreY,
            fingerLabels.MoveToImmutable(), fingerTop, box);
    }

    /// <summary>
    /// The diagram's ink box, anchored at the GRID BOTTOM centre: the fret rows up, the X / O
    /// row above them, half the string span each side, the "Nfr" label right, the strings'
    /// overhang and — for a FretBoard, <paramref name="fingers"/> — the finger numbers below.
    /// </summary>
    internal static GlyphMetrics.BBox Box(string? spec, ScoreTextMetrics fonts, bool fingers = false)
        => Measure(spec, fonts, fingers).Box;

    /// <summary>
    /// How far right of the note column's origin (the head's LEFT edge) a markup diagram's grid
    /// centre stands, given its <see cref="Box"/> (about that centre): the point 30% across the
    /// diagram's whole extent sits on the origin.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/fret-diagrams.scm:905 make-fret-diagram — the stencil is
    ///   <c>ly:stencil-aligned-to … X alignment</c>, alignment the align-dir property, −0.4 by
    ///   default, and Interval::linear_combination(−0.4) is 0.7 left + 0.3 right.
    /// LILYPOND-REF: scm/define-grobs.scm TextScript self-alignment-X / parent-alignment-X #f —
    ///   lily/self-alignment-interface.cc:150-175 aligned_on_parent adds nothing for either, so
    ///   the stencil's origin is the note column's.
    /// MEASURED (2.26.0, Lab sessions/p834/tw/ll c5, x32010 over e'4): LilyPond's grid left edge
    /// stands 1.40 left of the head's; centring the grid on the head put it 1.85 left.
    /// </remarks>
    internal static double GridCentreFromColumnOrigin(GlyphMetrics.BBox box)
        => -(0.7 * box.Left + 0.3 * box.Right);
}
