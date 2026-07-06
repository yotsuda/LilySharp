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

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Style of the text spanner line.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:3523 (style . dashed-line)
/// </remarks>
public enum TextSpannerStyle
{
    /// <summary>Dashed line (default for most text spanners).</summary>
    DashedLine,
    /// <summary>Solid continuous line.</summary>
    Line,
    /// <summary>No line (text only).</summary>
    None
}

/// <summary>
/// Represents a text spanner (text label with extending dashed/solid line).
/// Used for rit., accel., and similar expression markings that span a duration.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/text-spanner-engraver.cc TextSpanner engraver
/// LILYPOND-REF: scm/define-grobs.scm:3504-3535 TextSpanner grob
/// LILYPOND-REF: scm/define-grobs.scm:1331-1385 DynamicTextSpanner grob
///
/// Text spanners display text at the start point with a dashed/solid line
/// extending to the end point. Common uses:
/// - rit. --------
/// - accel. --------
/// - cresc. --------  (text alternative to hairpin wedge)
/// </remarks>
public sealed record TextSpannerItem(
    // The display text (e.g., "rit.", "accel.").
    string Text,
    // Measure index of the start point.
    int StartMeasureIndex,
    // Item index within the start measure.
    int StartItemIndex,
    // Measure index of the end point.
    int EndMeasureIndex,
    // Item index within the end measure.
    int EndItemIndex,
    // Line style (dashed, solid, none).
    TextSpannerStyle Style,
    // Source position for click-to-source mapping.
    int SourcePosition,
    // F3/B: index of the originating rit/accel mark in score.MusicMarks,
    // so a reused layout re-derives data-pos from the live score. -1 = unresolved.
    int SourceIndex = -1,
    // The staff this spanner belongs to (0 = first/only staff).
    int StaffIndex = 0
);
