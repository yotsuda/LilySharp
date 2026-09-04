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
/// Spacing specification for a pair of adjacent vertical elements.
/// Each property maps to a LilyPond spacing alist sub-property.
/// </summary>
/// <remarks>
/// LILYPOND-REF: ly/paper-defaults-init.ly:64-89
/// LILYPOND-REF: lily/page-layout-problem.cc:1340-1353 alter_spring_from_spacing_spec()
/// </remarks>
internal sealed record VerticalSpacingSpec
{
    /// <summary>
    /// Ideal distance between reference points (staff spaces).
    /// Maps to LilyPond's basic-distance.
    /// </summary>
    public double BasicDistance { get; init; }

    /// <summary>
    /// Absolute minimum distance (staff spaces).
    /// Overrides skyline distance if larger.
    /// Maps to LilyPond's minimum-distance.
    /// </summary>
    public double MinimumDistance { get; init; }

    /// <summary>
    /// Additional safety margin beyond skyline distance (staff spaces).
    /// Maps to LilyPond's padding.
    /// </summary>
    public double Padding { get; init; }

    /// <summary>
    /// Flexibility for stretching (inverse stretch strength), or <c>null</c> when LilyPond's
    /// spec does not declare one.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1350-1357
    /// <c>alter_spring_from_spacing_spec</c> — <c>set_default_strength()</c> runs
    /// UNCONDITIONALLY (:1354) and a declared <c>stretchability</c> overrides it only
    /// afterwards (:1356-1357), where <c>set_default_stretch_strength</c> is
    /// <c>inverse_stretch_strength_ = ideal_distance_</c> (spring.cc:213-216).
    /// <para>
    /// ⚠️ NULLABLE BECAUSE LILYPOND DISTINGUISHES TWO THINGS THIS USED TO COLLAPSE: a spec
    /// that DECLARES <c>(stretchability . 0)</c> is rigid whatever its ideal, while a spec
    /// that declares NOTHING tracks its ideal. Both were written as <c>0</c> and
    /// <c>CreateSpring</c> resolved both to the ideal — HANDOFF 5.2's "the current data
    /// structure cannot express LilyPond's quantity, so it is folded", which the rule names
    /// as forbidden. It was latent rather than live: the only spec declaring 0
    /// (<c>nonstaff-nonstaff-spacing</c>) has an ideal of 0 too, so the two readings agreed.
    /// </para>
    /// </remarks>
    public double? Stretchability { get; init; }
}

/// <summary>
/// Parameters for vertical spacing between systems, markups, and page edges.
/// LilyPond uses 7 named spacing contexts to fine-tune vertical layout.
/// </summary>
/// <remarks>
/// LILYPOND-REF: ly/paper-defaults-init.ly:64-89
/// LILYPOND-REF: lily/page-layout-problem.cc:488-535 spacing spec selection
/// </remarks>
internal sealed record VerticalSpacingParameters
{
    public static VerticalSpacingParameters Default { get; } = new();

    /// <summary>
    /// Spacing between two consecutive music systems.
    /// </summary>
    /// <remarks>LILYPOND-REF: paper-defaults-init.ly:64-67</remarks>
    public VerticalSpacingSpec SystemSystem { get; init; } = new()
    {
        BasicDistance = 12,
        MinimumDistance = 8,
        Padding = 1,
        Stretchability = 60
    };

    /// <summary>
    /// Spacing after a score boundary, before next system (larger gap for new score).
    /// </summary>
    /// <remarks>LILYPOND-REF: paper-defaults-init.ly:68-71</remarks>
    public VerticalSpacingSpec ScoreSystem { get; init; } = new()
    {
        BasicDistance = 14,
        MinimumDistance = 8,
        Padding = 1,
        Stretchability = 120
    };

    /// <summary>
    /// Spacing after a title/markup, before next system.
    /// </summary>
    /// <remarks>LILYPOND-REF: paper-defaults-init.ly:72-74</remarks>
    public VerticalSpacingSpec MarkupSystem { get; init; } = new()
    {
        BasicDistance = 5,
        MinimumDistance = 0,
        Padding = 0.5,
        Stretchability = 30
    };

    /// <summary>
    /// Spacing after a system, before next title/markup.
    /// </summary>
    /// <remarks>LILYPOND-REF: paper-defaults-init.ly:75-77</remarks>
    public VerticalSpacingSpec ScoreMarkup { get; init; } = new()
    {
        BasicDistance = 12,
        MinimumDistance = 0,
        Padding = 0.5,
        Stretchability = 60
    };

    /// <summary>
    /// Spacing between consecutive titles/markups.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/paper-defaults-init.ly:76-77
    /// <c>markup-markup-spacing = #'((basic-distance . 1) (padding . 0.5))</c>.
    /// <para>
    /// ⚠️ NO <c>stretchability</c> MEMBER, and it is SPELLED as the absence — 0, which
    /// <c>LayoutUtilities.CreateSpring</c> reads as LilyPond's absent and answers with the
    /// ideal (<c>alter_spring_from_spacing_spec</c> calls <c>set_default_strength</c>
    /// unconditionally at page-layout-problem.cc:1354 and only then lets a declared
    /// stretchability override it, spring.cc:213-216). ⚠️ This used to say 1 — the number
    /// the rule works out to, since the ideal here IS 1 — which is the failure mode
    /// HANDOFF 5.2's ★★ block names: identical today, and wrong the moment anything
    /// overrides the basic-distance, because LilyPond's spring would follow the new ideal
    /// and a literal would not.
    /// </para>
    /// </remarks>
    public VerticalSpacingSpec MarkupMarkup { get; init; } = new()
    {
        BasicDistance = 1,
        MinimumDistance = 0,
        Padding = 0.5,
        Stretchability = null,
    };

    /// <summary>
    /// Spacing from the page top to a TITLE that opens the page — the spring the page's top
    /// takes instead of <see cref="TopSystem"/> when its first line is a markup.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/paper-defaults-init.ly:81-83 top-markup-spacing
    /// ((basic-distance . 4) (minimum-distance . 0) (padding . 1)) — no stretchability, so
    /// the spring tracks its ideal, as <see cref="TopSystem"/> does.
    /// LILYPOND-REF: lily/page-layout-problem.cc:468-469 Page_layout_problem — the constructor
    /// swaps it in for top-system-spacing when the first element of the page is a Prob;
    /// lily/page-breaking.cc:1789-1790 min_whitespace_at_top_of_page reads it for a title line.
    /// </remarks>
    public VerticalSpacingSpec TopMarkup { get; init; } = new()
    {
        BasicDistance = 4,
        MinimumDistance = 0,
        Padding = 1,
        // ly/paper-defaults-init.ly:81-83 declares no stretchability.
        Stretchability = null,
    };

    /// <summary>
    /// Spacing from page top to first system.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/paper-defaults-init.ly:78-80 top-system-spacing
    /// ((basic-distance . 6) (minimum-distance . 0) (padding . 1)).
    /// (BasicDistance was transcribed as 1 — the last-bottom-spacing value.)
    /// </remarks>
    public VerticalSpacingSpec TopSystem { get; init; } = new()
    {
        BasicDistance = 6,
        MinimumDistance = 0,
        Padding = 1,
        // ly/paper-defaults-init.ly:78-80 declares no stretchability.
        Stretchability = null,
    };

    /// <summary>
    /// Spacing from last element to page bottom.
    /// </summary>
    /// <remarks>LILYPOND-REF: paper-defaults-init.ly:86-89</remarks>
    public VerticalSpacingSpec LastBottom { get; init; } = new()
    {
        BasicDistance = 1,
        MinimumDistance = 0,
        Padding = 1,
        Stretchability = 30
    };

    /// <summary>
    /// Selects the appropriate spacing spec based on the context of two adjacent elements.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:488-535 spacing spec selection logic
    /// </remarks>
    public VerticalSpacingSpec SelectSpec(
        bool isFirstOnPage,
        bool prevIsTitle, bool currentIsTitle,
        bool currentIsNewScore)
    {
        if (isFirstOnPage)
        {
            return TopSystem;
        }

        if (currentIsTitle)
        {
            return prevIsTitle ? MarkupMarkup : ScoreMarkup;
        }

        // Current is a music system
        if (prevIsTitle)
        {
            return MarkupSystem;
        }

        if (currentIsNewScore)
        {
            return ScoreSystem;
        }

        return SystemSystem;
    }
}
