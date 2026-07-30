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
/// One bass figure as a run of drawn pieces — the glyphs LilyPond's <c>\number</c> markup
/// puts on the page, at the em <see cref="EngravingDefaults.FiguredBassFontSize"/> sets.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/translation-functions.scm:349-470 <c>format-bass-figure</c> — a figure
/// is <c>\number</c> markup of the digit, with the alteration put adjacent to it, all wrapped
/// in <c>(make-fontsize-markup -5 …)</c>. LILYPOND-REF: scm/define-grobs.scm:352-356 BassFigure (bass-figure-interface at :359) —
/// <c>ly:text-interface::print</c> with
/// <c>grob::always-Y-extent-from-stencil</c>, so the grob's extent IS this run's ink.
/// <para>
/// ⚠️ ONE HOME for a reason (HANDOFF §5.0): the em and the metrics are two halves of one
/// claim, and the reservation (<see cref="FiguredBassEngraver"/>) and the drawing
/// (<c>SharedRenderer.DrawFiguredBass</c>) must read the same halves. Before 2026-07-30 they
/// did not — the drawing used a serif face at 3.0 ss and the reservation a nominal 1.5 cap —
/// and the gap between them was the <c>+0.375204764</c> under every figured-bass ledger point
/// plus digits that printed 0.112 through their own stem.
/// </para>
/// <para>
/// ⚠️ NAMED RESIDUAL, not fitted away: LilyPond selects the Emmentaler DESIGN SIZE nearest
/// the requested one (lily/font-select.cc:41-70 with scm/lily-library.scm:1702-1710's
/// mapping), so a figure at 4 ss × magstep(−5) = 11.2246pt is drawn from
/// <c>emmentaler-11</c>, whose digits are 2.004 design-ss where <c>emmentaler-20</c>'s are
/// 2.000. Lily# bundles only <c>emmentaler-20</c>, which is worth −0.002333 of cap on every
/// figure. That is a font-shipping decision, not an arithmetic one.
/// </para>
/// <para>
/// ⚠️ NOT PORTED HERE: LilyPond puts the alteration on the LEFT of the digit by default
/// (:446-455, <c>alt-dir</c> defaulting to <c>LEFT</c>) with a 0.1 pad, where Lily#'s
/// <c>FiguredBassFigure.DisplayText</c> spells it after the digit and pads nothing. No
/// ledger point watches the X of a figure yet, so the order stays as it was rather than
/// moving output no observer can check.
/// </para>
/// </remarks>
internal static class FiguredBassGlyphRun
{
    /// <summary>A piece of the run: a music glyph, or a character the music font has no
    /// bass-figure glyph for (Lily#'s continuation dash) drawn as text.</summary>
    internal readonly record struct Piece(char Ch, double X, double Advance, bool IsGlyph);

    /// <summary>The em a figure is drawn at, in staff spaces.</summary>
    internal static double Em => EngravingDefaults.FiguredBassFontSize;

    // The generated boxes and advances are per EM at the font's design size, where one em
    // spans the staff's 4 spaces (GlyphMetricsGenerated's header: 1 ss = unitsPerEm / 4).
    private static double Scale => Em / 4.0;

    /// <summary>The run's pieces, left to right, with X relative to the run's left edge.</summary>
    internal static ImmutableArray<Piece> Pieces(string text)
    {
        if (string.IsNullOrEmpty(text)) return ImmutableArray<Piece>.Empty;
        var pieces = ImmutableArray.CreateBuilder<Piece>(text.Length);
        double x = 0;
        foreach (char c in text)
        {
            bool isGlyph = GlyphMetrics.TryGetFiguredBassGlyph(c, out char glyph, out _, out double adv);
            double advance = isGlyph ? adv * Scale : TextFontMetrics.Serif(c.ToString(), Em);
            pieces.Add(new Piece(isGlyph ? glyph : c, x, advance, isGlyph));
            x += advance;
        }
        return pieces.ToImmutable();
    }

    /// <summary>The run's advance width in staff spaces.</summary>
    internal static double Width(string text)
    {
        double w = 0;
        foreach (var p in Pieces(text)) w += p.Advance;
        return w;
    }

    /// <summary>
    /// The run's ink above its baseline — the union of its glyphs' outline tops, which is
    /// what a BassFigure's <c>Y-extent</c> is.
    /// </summary>
    internal static double InkTop(string text)
    {
        double top = 0;
        if (string.IsNullOrEmpty(text)) return top;
        foreach (char c in text)
        {
            double t = GlyphMetrics.TryGetFiguredBassGlyph(c, out _, out var outline, out _)
                ? outline.Top * Scale
                // No feta glyph: the same face and size the drawing falls back to, so this
                // path keeps the two halves together as well.
                : TextFontMetrics.Ink(c.ToString(), Em).Top;
            top = System.Math.Max(top, t);
        }
        return top;
    }
}
