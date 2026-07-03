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
/// A percent repeat sign marking a measure as a repetition of the previous measure.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/percent-repeat-engraver.cc - PercentRepeat grob
/// LILYPOND-REF: lily/percent-repeat-interface.cc - visual rendering (slash + dots)
/// LILYPOND-REF: scm/define-grobs.scm:2520-2539 - PercentRepeat properties
///
/// A single percent sign (%) repeats the previous measure.
/// A double percent sign (%%) repeats the previous 2 measures.
///
/// In LilySharp, percent repeats are generated from:
/// - `repeat percent N { body }` syntax (automatic)
/// - Body is unfolded N times, iterations 2+ marked as percent repeats
/// </remarks>
public sealed record PercentRepeatItem(
    /// <summary>Measure index where the percent sign appears.</summary>
    int MeasureIndex,
    /// <summary>Source position for click-to-source mapping.</summary>
    int SourcePosition,
    /// <summary>The staff the repeat was written on — the sign prints there,
    /// like LilyPond's Percent_repeat_engraver living in that Voice.</summary>
    int StaffIndex = 0
);
