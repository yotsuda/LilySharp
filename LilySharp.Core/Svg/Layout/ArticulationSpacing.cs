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
}
