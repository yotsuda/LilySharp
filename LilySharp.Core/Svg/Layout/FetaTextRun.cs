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
/// How LilyPond lays a run of fetaText glyphs out: each glyph fed by its OWN advance plus
/// its GPOS kern to the next, that sum hinted to one device pixel, and the run's ink the
/// union of the glyphs' outlines.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/pango-font.cc:344-362 Pango_font::pango_item_string_stencil — Pango
///   lays the glyphs out by their hinted advances and reports the run's box as X = the
///   LOGICAL rect (so the left edge is the pen origin and the right edge is the advance run)
///   and Y = the INK rect. The snap is PER GLYPH, not on the total.
/// LILYPOND-REF: lily/modified-font-metric.cc:125-143 Modified_font_metric::text_stencil — the
///   text path measures the OUTLINE and never reads LILC, which is why the boxes asked for
///   here are the <c>…Outline</c> ones.
/// <para>
/// ⚠️ THIS FILE IS THE THIRD COPY NOT BEING WRITTEN. <see cref="FingeringGlyphRun"/> and
/// <see cref="FiguredBassGlyphRun"/> spelled this loop out separately, and the meter's digits
/// needed it a third time (session 164). HANDOFF §5.1 says an extraction rides the commit that
/// would have written the next copy rather than a refactor session of its own, and this is
/// that commit. The three differ in exactly three things — WHICH glyph a character maps to,
/// what EM the fallback is drawn at, and whether the pair carries a KERN — so those are the
/// parameters and nothing else is.
/// </para>
/// <para>
/// ⚠️ THE KERN GOES INSIDE THE SNAP. GPOS adjusts the FIRST glyph's own advance and Pango
/// hints the result, so a kerned pair carries ONE rounding, not a rounding plus a raw kern.
/// Session 93 measured the difference on the dynamic labels (0.015426772 per pair, and the
/// <c>\fff</c> ledger point crossed zero under the wrong spelling — see
/// <see cref="DynamicOutline"/>); session 164 measured it again on the meter, where
/// LilyPond's "10" row is 34 + 43 = 77 device pixels and the outside-the-snap spelling gives
/// 80. A run whose glyphs do not kern passes <c>null</c> and is unaffected.
/// </para>
/// </remarks>
internal static class FetaTextRun
{
    /// <summary>A drawn piece of a run: a music glyph, or a character the music font has no
    /// glyph for in this cut, drawn as text.</summary>
    internal readonly record struct Piece(char Ch, double X, double Advance, bool IsGlyph);

    /// <summary>The glyph, its outline box and its UNHINTED advance for one character, in the
    /// PAGE's staff spaces out of the run's own design. False when this cut has no glyph for
    /// the character and the caller's fallback applies.</summary>
    internal delegate bool GlyphLookup(
        char c, out char glyph, out GlyphMetrics.BBox outline, out double advance);

    /// <summary>The GPOS pair kern between two adjacent glyphs of the run, in the PAGE's staff
    /// spaces — 0 for a pair the font does not kern.</summary>
    internal delegate double KernLookup(char first, char second);

    /// <summary>
    /// The run's pieces, left to right, with X relative to the run's left edge — each fed by
    /// its OWN hinted advance.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE KERN IS TAKEN TO THE NEXT CHARACTER OF THE RUN, including when that character
    /// falls back to text: the pen moves by whatever the shaper says, and the fallback is a
    /// Lily#-own branch that no LilyPond reading covers either way.
    /// </remarks>
    /// <remarks>
    /// ⚠️ THE TEXT BRANCH BELOW TAKES THE BUNDLED FACE, not the score's. It is the
    /// FALLBACK inside a feta run — the character the music font has no glyph for — and the
    /// run's three consumers (fingering, figured bass, meter) each reach it through their
    /// own <c>static</c> wrapper, so binding it means threading a score through all three.
    /// Left named and counted rather than half-done, 2026-08-18. The same note stands on
    /// <c>InkTop</c> and <c>InkBottom</c>.
    /// </remarks>
    internal static ImmutableArray<Piece> Pieces(
        string text, GlyphLookup lookup, double em, KernLookup? kern = null)
    {
        if (string.IsNullOrEmpty(text)) return ImmutableArray<Piece>.Empty;
        var pieces = ImmutableArray.CreateBuilder<Piece>(text.Length);
        double x = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            bool isGlyph = lookup(c, out char glyph, out _, out double adv);
            double advance;
            if (isGlyph)
            {
                if (kern is not null && i + 1 < text.Length)
                    adv += kern(c, text[i + 1]);
                advance = TextFontMetrics.QuantiseToPangoPixel(adv);
            }
            else
            {
                advance = TextFontMetrics.Serif(c.ToString(), em);
            }
            pieces.Add(new Piece(isGlyph ? glyph : c, x, advance, isGlyph));
            x += advance;
        }
        return pieces.ToImmutable();
    }

    /// <summary>The run's advance width in staff spaces — the whole X extent, whose left edge
    /// is the pen origin.</summary>
    internal static double Width(
        string text, GlyphLookup lookup, double em, KernLookup? kern = null)
    {
        double w = 0;
        foreach (var p in Pieces(text, lookup, em, kern)) w += p.Advance;
        return w;
    }

    /// <summary>The run's ink above its baseline — the union of its glyphs' outline tops.</summary>
    /// <remarks>Measured on the outline and NOT hinted to a pixel the way the width is: Pango
    /// reports the ink rect in its own finer units, and the residual that leaves is the ~7e-5
    /// the fetaText family already carries (HANDOFF §1 ⒧ — named, not fitted).</remarks>
    internal static double InkTop(string text, GlyphLookup lookup, double em)
    {
        double top = 0;
        if (string.IsNullOrEmpty(text)) return top;
        foreach (char c in text)
        {
            double t = lookup(c, out _, out var outline, out _)
                ? outline.Top
                : TextFontMetrics.Ink(c.ToString(), em).Top;
            top = System.Math.Max(top, t);
        }
        return top;
    }

    /// <summary>The run's ink BELOW its baseline (≤ 0) — the other end of the same box.</summary>
    /// <remarks>⚠️ IT IS NOT ALWAYS ZERO, which is why it is a call and not a 0: most feta
    /// digits sit ON the baseline and the seven hangs below it, and the figured-bass
    /// accidentals hang further.</remarks>
    internal static double InkBottom(string text, GlyphLookup lookup, double em)
    {
        double bottom = 0;
        if (string.IsNullOrEmpty(text)) return bottom;
        foreach (char c in text)
        {
            double b = lookup(c, out _, out var outline, out _)
                ? outline.Bottom
                : TextFontMetrics.Ink(c.ToString(), em).Bottom;
            bottom = System.Math.Min(bottom, b);
        }
        return bottom;
    }
}
