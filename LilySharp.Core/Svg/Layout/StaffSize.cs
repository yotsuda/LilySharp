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

using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// One staff's own SIZE — the magnification its glyphs and its staff-spaces are engraved at.
/// A staff decorated by an ossia is engraved smaller than the staff it decorates, and every
/// length that belongs to it shrinks with it while the X it sits at does not.
/// </summary>
/// <remarks>
/// LILYPOND-REF: ly/music-functions-init.ly <c>magnifyStaff</c> sets the two properties this
/// carries together — the context's <c>fontSize</c> and <c>StaffSymbol.staff-space</c> — so a
/// magnified staff's GLYPHS and its own STAFF-SPACES scale by the same factor.
/// <para>
/// ⚠️ WHY THIS IS A TYPE AND NOT A MULTIPLICATION AT EACH SEED. In LilyPond there is nothing
/// to multiply: a grob reads its dimensions through the font its context gave it, and that
/// font has already scaled them —
/// LILYPOND-REF: lily/modified-font-metric.cc:62-68 Modified_font_metric::get_indexed_char_dimensions
/// is <c>Box b = orig_-&gt;get_indexed_char_dimensions (i); b.scale (magnification_);</c>, three
/// lines, applied once where the metric is READ. Porting that shape means the seeds never
/// multiply and so can never forget to: they ask this for the number and get it at the right
/// size. <see cref="Ink"/> is that <c>b.scale()</c>.
/// </para>
/// <para>
/// ★ AND IT IS WHAT MAKES A MISSED SITE VISIBLE. The two units that used to share one
/// <c>double</c> now have names — a glyph's box goes through <see cref="Ink"/>, a staff-space
/// length through <see cref="Span"/> — so the review rule for a seed is one sentence:
/// <b>the only bare number left in a seed is an X POSITION.</b> Positions are the score-wide
/// paper columns and must NOT scale (LILYPOND-REF: lily/spacing-spanner.cc,
/// lily/paper-column.cc — one column per moment spans every staff), which is why they are the
/// one thing that stays bare. Anything else bare is an unconverted site.
/// </para>
/// </remarks>
internal readonly record struct StaffSize(double Magnification)
{
    /// <summary>A staff engraved at the score's own size — the identity.</summary>
    public static readonly StaffSize FullSize = new(1.0);

    /// <summary>The size <paramref name="staff"/> is engraved at.</summary>
    /// <remarks>
    /// An ossia is LilyPond's <c>\with { fontSize = #-3 }</c> staff, so its factor is
    /// <c>magstep(-3)</c> — <see cref="EngravingDefaults.OssiaScale"/>, the same constant the
    /// renderer's scale group and <c>MultiStaffLayouter.GetStaffHeight</c> use, so the drawn
    /// ink, the staff's height and the room reserved for it cannot drift apart.
    /// <para>
    /// ⚠️ LILYSHARP-OWN: THIS LINE IS A TYPE ENUMERATION, and it is the one part of this file
    /// that is NOT the literal shape. LilyPond reads the context's <c>fontSize</c> and never
    /// asks whether a staff is an ossia —
    /// LILYPOND-REF: ly/music-functions-init.ly magnifyStaff gives ANY staff a magnification —
    /// so the faithful spelling is a property lookup, exactly as
    /// <c>is_spaceable</c>'s was once <c>Staff.StaffAffinity</c> existed to look up. The
    /// obstacle is the model: <c>Staff</c> carries <c>IsOssia</c> and no font size at all.
    /// It goes when <c>Staff</c> carries the magnification, and then this method is one line
    /// that reads it. Named in HANDOFF's inventory of proxies as (5).
    /// </para>
    /// </remarks>
    public static StaffSize Of(Staff? staff) =>
        staff is { IsOssia: true } ? new StaffSize(EngravingDefaults.OssiaScale) : FullSize;

    /// <summary>Whether this is the score's own size, so nothing needs scaling.</summary>
    public bool IsFullSize => Magnification == 1.0;

    /// <summary>
    /// A glyph's box AT THIS STAFF'S SIZE — both axes, exactly as LilyPond's font metric
    /// hands it to a grob.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/modified-font-metric.cc:62-68 Modified_font_metric::get_indexed_char_dimensions
    /// — the whole of it is <c>b.scale (magnification_)</c>.
    /// ⚠️ BOTH AXES. A smaller staff's clef is narrower as well as shorter; only the X it is
    /// anchored at is shared with the staff it decorates.
    /// </remarks>
    public GlyphMetrics.BBox Ink(GlyphMetrics.BBox box) =>
        IsFullSize
            ? box
            : new GlyphMetrics.BBox(
                box.Left * Magnification, box.Bottom * Magnification,
                box.Right * Magnification, box.Top * Magnification);

    /// <summary>
    /// A length expressed in THIS staff's staff-spaces, in the system's — a stem's length, a
    /// staff position's distance from the middle line, a ledger's overhang.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/music-functions-init.ly <c>magnifyStaff</c> —
    /// <c>StaffSymbol.staff-space</c> scales with the font, so a note two spaces above the
    /// middle line of a small staff is two of ITS spaces up, not two of the score's.
    /// </remarks>
    public double Span(double staffSpaces) =>
        IsFullSize ? staffSpaces : staffSpaces * Magnification;
}
