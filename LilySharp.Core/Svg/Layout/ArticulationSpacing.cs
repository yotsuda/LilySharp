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

using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// LilyPond's horizontal-spacing constants for scripts (articulations, fermatas,
/// ornaments), collected in one place so every reservation that clears a script
/// against neighbouring ink uses the SAME LilyPond values instead of ad-hoc gaps.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/separation-item.cc — a note-column grob's separation box
///   grows by <c>extra-spacing-width</c> (default <c>(-0.1 . 0.1)</c>) on each side
///   before columns are spaced, so two facing grobs clear by the sum of their
///   near-side widths.
/// LILYPOND-REF: scm/define-grobs.scm Script — inherits the default
///   extra-spacing-width; <c>horizon-padding . 0.1</c> ("to avoid interleaving with
///   accidentals").
/// LILYPOND-REF: scm/script.scm — per-script vertical <c>padding</c>
///   (fermata 0.40, portato 0.45, most others 0.20); a few set their own
///   <c>skyline-horizontal-padding</c> (e.g. downbow 0.20).
/// </remarks>
internal static class ArticulationSpacing
{
    /// <summary>A grob's separation box grows by this on the side facing a neighbour
    /// (LP extra-spacing-width default 0.1). Two facing grobs therefore clear by
    /// <c>2 ×</c> this — the gap Lily# reserves between a script and adjacent ink.</summary>
    public const double ScriptExtraSpacingWidth = 0.1;

    /// <summary>The clearance between a script and a neighbouring note-column grob:
    /// the script's near-side extra-spacing-width plus the neighbour's. Used wherever
    /// a fermata / ornament must not touch the next note's accidental or a grace flag.</summary>
    public const double ScriptToNeighbourGap = 2 * ScriptExtraSpacingWidth;

    /// <summary>
    /// A script's vertical <c>padding</c> to its support — the distance it floats off
    /// the note/beam it sits over. Most scripts use 0.20; a fermata clears further
    /// (0.40) so it reads clearly above a beam, and portato further still (0.45).
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/script.scm per-articulation <c>padding</c>.</remarks>
    public static double VerticalPadding(ArticulationType type) => type switch
    {
        ArticulationType.Fermata or ArticulationType.FermataShort
            or ArticulationType.FermataLong => 0.40,
        ArticulationType.Portato => 0.45,
        _ => 0.20,
    };

    /// <summary>
    /// A script's declared <c>outside-staff-priority</c>, or <c>null</c> when it declares
    /// none — LilyPond's <c>#f</c>, which is a distinct value and not a zero: a grob whose
    /// priority is unset stays in the support skyline, while a grob that declared 0 would be
    /// the FIRST mover placed. A script that declares one is a MOVER in the outside-staff
    /// collision pass, placed in priority order; one that declares none stays in the
    /// support skyline the movers clear — LilyPond's own split.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/script.scm — the FERMATA family is the only one that declares
    ///   this property, and all seven entries declare 75: fermata, shortfermata,
    ///   longfermata, verylongfermata, veryshortfermata, henzelongfermata,
    ///   henzeshortfermata. Everything else in script.scm (accents, staccato, bows,
    ///   ornaments, pedal marks, …) leaves it unset, so the Script grob's own
    ///   declaration stands — and scm/define-grobs.scm:2992 Script declares none.
    /// LILYPOND-REF: lily/axis-group-interface.cc:914-935 — grobs with no priority go
    ///   into <c>inside_staff_skylines</c>; :952-972 the rest are placed in ascending
    ///   priority order. 75 lands between TrillSpanner 50 and BarNumber 100.
    /// <para>
    /// Lily# has three of the seven shapes (normal / short-angled / long-square). The
    /// Henze and very-long/short glyphs have no Lily# spelling yet; when they arrive they
    /// belong in this arm, not in a new one.
    /// </para>
    /// <para>
    /// ⚠️ MultiMeasureRestScript is a DIFFERENT grob (scm/define-grobs.scm: priority 40,
    /// <c>outside-staff-padding 0</c>), so a fermata over a multi-measure rest is not this
    /// number. No Lily# fixture and no ledger point reaches that regime — a fermata on a
    /// plain rest (<c>r4@fermata</c>, fixture feature-tour) IS an ordinary Script.
    /// </para>
    /// </remarks>
    public static double? OutsideStaffPriority(ArticulationType type) => type switch
    {
        ArticulationType.Fermata or ArticulationType.FermataShort
            or ArticulationType.FermataLong => 75,
        _ => null,
    };

    /// <summary>
    /// How far this script's own skyline is PADDED along the horizon before anything reads
    /// it — 0 for all but three scripts.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/script.scm:86-94 skyline-horizontal-padding (downbow 0.20), and the
    ///   same key at :392 staccatissimo 0.10 / :407 staccato 0.10 — the
    ///   only three entries in the whole file that declare it; every other script leaves it
    ///   unset and scm/define-grobs.scm's Script declares none, so the default 0.0 stands.
    /// LILYPOND-REF: lily/stencil-integral.cc:881-893 Grob::vertical_skylines_from_stencil —
    ///   the property IS the stencil's skyline <c>.pad()</c>ed by that number, so every
    ///   consumer of a Script's profile sees the padded shape, never the raw outline.
    /// <para>
    /// ⚠️ IT IS NOT COSMETIC ON A SMALL GLYPH. Measured out of LilyPond on the staccato dot
    /// (audit/lp-geometry probes/dynamic-support.ly DSK): the raw outline is a polygon
    /// 0.4 wide reaching 0.2 deep at ONE point, and the padded property is 0.8 wide with
    /// that 0.2 held flat across ±0.1 — so a dynamic under a dot clears an obstacle twice
    /// the glyph's width. Reading the unpadded outline there put the label 0.12 too close.
    /// </para>
    /// </remarks>
    public static double SkylineHorizontalPadding(ArticulationType type) => type switch
    {
        ArticulationType.Staccato or ArticulationType.Staccatissimo => 0.10,
        ArticulationType.DownBow => 0.20,
        _ => 0.0,
    };
}
