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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Parameters for grace note spacing.
/// Grace notes use tighter spacing than regular notes.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:1721-1732 GraceSpacing
/// LILYPOND-REF: lily/spacing-basic.cc:163-180 grace note spring creation
/// </remarks>
internal sealed record GraceSpacingParameters
{
    public static GraceSpacingParameters Default { get; } = new();

    /// <summary>
    /// Spacing increment for grace notes (notehead width proxy).
    /// Smaller than regular notes (0.8 vs 1.2) for tighter spacing.
    /// LILYPOND-REF: define-grobs.scm:1725 (spacing-increment . 0.8)
    /// </summary>
    public double SpacingIncrement { get; init; } = 0.8;

    /// <summary>
    /// Duration space factor for the shortest grace note.
    /// LILYPOND-REF: define-grobs.scm:1724 (shortest-duration-space . 1.6)
    /// </summary>
    public double ShortestDurationSpace { get; init; } = 1.6;

    /// <summary>
    /// Base shortest duration for grace note spacing calculation.
    /// Grace notes are typically eighth notes (1/8).
    /// </summary>
    public double BaseShortestDuration { get; init; } = 0.125;
}
