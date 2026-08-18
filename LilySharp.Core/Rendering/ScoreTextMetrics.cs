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

using System.Collections.Concurrent;

namespace LilySharp.Core.Rendering;

/// <summary>
/// ONE score's text measurements — the layout asking the same question the drawing asks.
/// </summary>
/// <remarks>
/// <c>IDrawingContext.DrawText</c> takes <c>(text, …, role, style)</c>: the ROLE decides the
/// face through the score's <see cref="TextFontPlan"/>, and the STYLE is what the engraving
/// decided and no <c>font</c> directive may touch. The layout used to ask something else
/// entirely — <c>TextFontMetrics.SerifBold(text, size)</c>, a FAMILY and nothing about what
/// the string was — so the reservation could not follow a binding even in principle. The
/// two sides now ask the same question in the same words, and this type is where the
/// layout's half of it lives.
/// <para>
/// ⚠️ THE GAP THIS CLOSES WAS MEASURED, 2026-08-18: with a face named but not measured, a
/// 16-character tempo mark at 2.2 staff spaces ran from 2.05 short (Times) to 3.61 long
/// (Courier New) against the box reserved for it, and <c>rit.</c> in Courier New was 68%
/// wider than its box. Not a rounding difference — a different face.
/// </para>
/// <para>
/// ⚠️ A NAMED FACE IS MEASURED WHEN THIS MACHINE HAS IT, and the layout is therefore
/// machine-dependent for a score that names one. That is LilyPond's exposure too and for
/// the same reason (LILYPOND-REF: lily/font-select.cc:193-217 select_font hands a
/// <c>font-name</c> string to <c>find_pango_font</c>, i.e. to Fontconfig), so this is a
/// port rather than a new hazard. It is NOT silent: a face nobody has is reported —
/// <c>DiagnosticCodes.FontNotFound</c> — and the reservation falls back to the bundled
/// family of the role, which is what a score with no directive gets.
/// </para>
/// <para>
/// ⚠️ WHY THE FALLBACK IS HERE AND NOT IN <see cref="TextFontMetrics"/>: that class throws
/// for a face it cannot read, deliberately, so that nothing substitutes without a decision.
/// This is the one place that makes the decision, once, per (role, style).
/// </para>
/// </remarks>
public sealed class ScoreTextMetrics
{
    /// <summary>What a score with no <c>font</c> directive measures with.</summary>
    public static readonly ScoreTextMetrics Bundled = new(TextFontPlan.Default);

    private readonly TextFontPlan _plan;
    private readonly ConcurrentDictionary<(TextRole Role, FontStyle Style), TextFace> _faces = new();

    /// <summary>Builds the metrics a score's resolved <c>font</c> directive implies.</summary>
    public ScoreTextMetrics(TextFontPlan plan) => _plan = plan;

    /// <summary>The plan these metrics resolve through.</summary>
    public TextFontPlan Plan => _plan;

    /// <summary>
    /// WHICH font program <paramref name="role"/> is measured from at
    /// <paramref name="style"/>.
    /// </summary>
    /// <remarks>
    /// The chain is walked in order and the first face this machine can read wins — that is
    /// what a fallback chain IS, and it is why a score may write a Latin face and a CJK one
    /// beside it. When none of them can be read the bundled face of the role's family is
    /// the answer, which is the same face the score would have got by naming nothing.
    /// <para>
    /// Cached per (role, style) because the walk asks the font manager, and a page asks for
    /// a handful of roles once per drawn string.
    /// </para>
    /// </remarks>
    public TextFace Face(TextRole role, FontStyle style = FontStyle.Regular)
        => _faces.GetOrAdd((role, style), key =>
        {
            var resolved = _plan.Resolve(key.Role);
            bool sans = resolved.Family == TextFontFamily.Sans;
            if (!resolved.IsBundled)
            {
                foreach (var name in resolved.Names)
                {
                    var face = TextFace.Named(name, sans, key.Style);
                    if (TextFontMetrics.CanMeasure(face))
                        return face;
                }
            }
            return TextFace.Bundled(sans, key.Style);
        });

    /// <summary>Advance width of <paramref name="text"/> in staff spaces.</summary>
    public double Advance(string text, double fontSize, TextRole role,
        FontStyle style = FontStyle.Regular)
        => TextFontMetrics.Advance(text, fontSize, Face(role, style));

    /// <summary>
    /// INK extent of <paramref name="text"/> relative to its baseline, up-positive.
    /// </summary>
    public (double Bottom, double Top) Ink(string text, double fontSize, TextRole role,
        FontStyle style = FontStyle.Regular)
        => TextFontMetrics.Ink(text, fontSize, Face(role, style));

    /// <summary>Ink height (<c>Top - Bottom</c>) of <paramref name="text"/>.</summary>
    public double InkHeight(string text, double fontSize, TextRole role,
        FontStyle style = FontStyle.Regular)
        => TextFontMetrics.InkHeight(text, fontSize, Face(role, style));
}
