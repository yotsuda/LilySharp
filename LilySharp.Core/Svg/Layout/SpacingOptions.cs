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
/// The two score-wide quantities every duration spring is built from — LilyPond's
/// <c>Spacing_options</c>: the piece's common shortest duration and the spacing increment.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/spacing-options.cc:30-53 Spacing_options::init_from_grob — both are read
/// off the one SpacingSpanner (<c>spacing-increment</c>, <c>common-shortest-duration</c>) and
/// travel together into get_duration_space (:71-107), note_spacing (lily/spacing-basic.cc:152)
/// and standard_breakable_column_spacing (:53-55).
/// <para>
/// ⚠️ ONE CARRIER, NOT A SECOND PARAMETER. Until session 379 the spring builders took the
/// shortest duration alone and read the increment from the constant
/// <see cref="EngravingDefaults.SpacingIncrement"/>, so <c>paper { spacingIncrement }</c> was
/// parsed into <see cref="LayoutOptions.SpacingIncrement"/> and read by nothing (measured: LilyPond
/// 2.26.0 lengthens a ragged line 74.95 → 98.83 for 1.2 → 1.8, and Lily# did not move —
/// scratch/p380/incr). Carrying both as the one value LilyPond carries is what keeps a later
/// reader from taking one of them from the paper and the other from the constant.
/// </para>
/// <para>
/// ⚠️ The GRACE spacing increment (0.8) is not this one: it is GraceSpacing's own property
/// (<see cref="GraceSpacingParameters.SpacingIncrement"/>), which LilyPond reads through a
/// second <c>Spacing_options</c> built from that grob (lily/spacing-basic.cc:168-174).
/// </para>
/// </remarks>
/// <param name="GlobalShortest">The common shortest duration in whole notes —
/// <c>global_shortest_</c>.</param>
/// <param name="Increment">The spacing increment in staff spaces — <c>increment_</c>.</param>
internal readonly record struct SpacingOptions(double GlobalShortest, double Increment)
{
    /// <summary>LilyPond's defaults: a 3/16 shortest and the 1.2 increment.</summary>
    public static SpacingOptions Default { get; } =
        new(EngravingDefaults.BaseShortestDuration, EngravingDefaults.SpacingIncrement);

    /// <summary>
    /// The options a score lays out with: its common shortest duration (or the default when
    /// none was computed) and its paper's increment.
    /// </summary>
    /// <remarks>
    /// The paper is the one source: every production layout is
    /// <c>new LayoutEngine(score.Paper)</c>, and the static line-break gate
    /// (<see cref="SystemBreaker.ComputeMultiStaffSpringData"/>) has only the score to ask.
    /// </remarks>
    public static SpacingOptions For(Model.MultiStaffScore score, double? globalShortest)
        => new(globalShortest ?? EngravingDefaults.BaseShortestDuration, score.Paper.SpacingIncrement);

    /// <summary>These options with another shortest duration and the same increment.</summary>
    public SpacingOptions WithShortest(double? globalShortest)
        => globalShortest is { } g ? this with { GlobalShortest = g } : this;
}
