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
/// Represents a trill spanner (tr symbol with wavy line extension).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/trill-spanner-engraver.cc Trill_spanner_engraver class
/// LILYPOND-REF: scm/define-grobs.scm:2175-2230 TrillSpanner grob definition
///
/// Trill spanners display "tr" at the start point with a wavy line
/// extending to the end point. Used for sustained trills:
///   tr~~~~~~~~~~~~
/// </remarks>
public sealed record TrillSpannerItem(
    /// <summary>Measure index of the start point.</summary>
    int StartMeasureIndex,
    /// <summary>Item index within the start measure.</summary>
    int StartItemIndex,
    /// <summary>Measure index of the end point.</summary>
    int EndMeasureIndex,
    /// <summary>Item index within the end measure.</summary>
    int EndItemIndex,
    /// <summary>Source position for click-to-source mapping.</summary>
    int SourcePosition
);
