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
/// The dimensions of a chord diagram (<c>@diagram(…)</c>) — ONE home for the drawing
/// (<c>SharedRenderer.DrawFretFrame</c>) and the box the layout reserves for it
/// (<c>ArticulationEngraver.FrameBox</c>), so the two cannot drift.
/// </summary>
/// <remarks>
/// LilyPond's fret diagram at its default <c>size 1.0</c>: one staff space between strings and
/// between frets, four fret rows, strings at half a staff line's thickness and the nut three
/// times that, every length multiplied by <c>size</c>.
/// LILYPOND-REF: scm/fret-diagrams.scm make-fret-diagram — size 1.0, fret-distance 1.0,
///   string-distance 1.0; :306 sth = size × th (the thickness multiplies too).
/// Until 2026-09-26 Lily# drew it at 0.55 × 0.5 staff spaces — about half LilyPond's —
/// and a reader found it too small to read. The <c>size</c> is the score's
/// <c>fonts { diagram step ±n }</c> (or <c>size n</c>, the "Nfr" label's em): the same ratio
/// the label's em takes (<see cref="Scale"/>), so the label and the grid move as one.
/// Owner's decision, session 646.
/// ⚠️ LILYSHARP-OWN in the detail: the o / x header, the dots and the label are drawn by
/// Lily#'s own arithmetic at LilyPond's proportions, not transcribed from fret-diagrams.scm.
/// </remarks>
internal static class FretFrameGeometry
{
    /// <summary>The "Nfr" label's ENGRAVING em at <c>size 1</c>.</summary>
    internal const double LabelEm = 1.1;

    /// <summary>Fret rows drawn (LilyPond's default <c>fret-count</c>).</summary>
    internal const int FretRows = 4;

    /// <summary>LilyPond's <c>size</c> for this score: 1 with no directive, 2 at
    /// <c>diagram step +6</c>.</summary>
    internal static double Scale(ScoreTextMetrics fonts)
        => fonts.Size(TextRole.FretFrame, LabelEm) / LabelEm;

    internal static double StringSpacing(double s) => 1.0 * s;
    internal static double FretSpacing(double s) => 1.0 * s;
    internal static double StringThickness(double s) => 0.05 * s;
    internal static double NutThickness(double s) => 0.15 * s;
    /// <summary>The o / x row's centre above the grid top.</summary>
    internal static double HeaderRise(double s) => 0.68 * s;
    /// <summary>Half the x mark's reach, and the open circle's radius.</summary>
    internal static double MarkHalf(double s) => 0.32 * s;
    internal static double DotRadius(double s) => 0.34 * s;
    /// <summary>Gap from the grid's right edge to the "Nfr" label.</summary>
    internal static double LabelGap(double s) => 0.7 * s;

    /// <summary>
    /// The diagram's ink box, anchored at the GRID BOTTOM centre: the fret rows up, the o / x
    /// row above them, half the string span each side, and room for the "Nfr" label right.
    /// </summary>
    internal static GlyphMetrics.BBox Box(string? spec, double s)
    {
        int strings = System.Math.Max(4, spec?.Length ?? 6);
        double halfW = (strings - 1) * StringSpacing(s) / 2 + MarkHalf(s);
        int rows = spec is null ? FretRows : RowCount(spec);
        double top = rows * FretSpacing(s) + HeaderRise(s) + MarkHalf(s);
        // Room for the label only where one is drawn — a first-position shape has none, and
        // reserving it anyway pushed the NEXT diagram off its neighbour's empty air.
        double labelRoom = spec is null || BaseFret(spec) > 1 ? LabelGap(s) + 2.0 * s : 0.0;
        return new GlyphMetrics.BBox(-halfW, 0, halfW + labelRoom, top);
    }

    /// <summary>
    /// The fret on string <paramref name="i"/> of a spec (LOW string first): −1 muted
    /// (<c>x</c>), 0 open (<c>o</c> or <c>0</c>), else the fret.
    /// </summary>
    /// <remarks>
    /// ⚠️ TWO ALPHABETS, ONE READER. A WRITTEN spec (<c>@diagram(x32010)</c>, the position
    /// string of <c>@chord(x32010)</c>) is x / o / 0–9 only — the gate is
    /// <c>Semantics.AnnotationValues.Frame</c>. A shape Lily#'s order picks (<c>Music.ChordVoicings</c>) can
    /// stand at frets 10–15, which one character per string cannot spell in digits, so the
    /// spec it hands the page carries them as <c>a</c>–<c>f</c> (<c>8aa988</c> is
    /// 8-10-10-9-8-8; <c>Music.ChordVoicings.ToFrameSpec</c> writes it). A writer cannot type
    /// those letters — the written gate refuses them — so the internal alphabet never meets a
    /// user's spelling. The drawing, the reservation, the twin and MusicXML all read a fret
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
        for (int i = 0; i < spec.Length; i++)
        {
            int f = FretAt(spec, i);
            if (f <= 0)
                continue;
            min = System.Math.Min(min, f);
            max = System.Math.Max(max, f);
        }
        return (min == int.MaxValue ? 1 : min, max);
    }
}
