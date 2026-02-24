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
public sealed record VerticalSpacingSpec
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
    /// Flexibility for stretching (inverse stretch strength).
    /// Higher values = more elastic. 0 = rigid.
    /// Maps to LilyPond's stretchability.
    /// </summary>
    public double Stretchability { get; init; }
}

/// <summary>
/// Parameters for vertical spacing between systems, markups, and page edges.
/// LilyPond uses 7 named spacing contexts to fine-tune vertical layout.
/// </summary>
/// <remarks>
/// LILYPOND-REF: ly/paper-defaults-init.ly:64-89
/// LILYPOND-REF: lily/page-layout-problem.cc:488-535 spacing spec selection
/// </remarks>
public sealed record VerticalSpacingParameters
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
    /// <remarks>LILYPOND-REF: paper-defaults-init.ly:78-79</remarks>
    public VerticalSpacingSpec MarkupMarkup { get; init; } = new()
    {
        BasicDistance = 1,
        MinimumDistance = 0,
        Padding = 0.5,
        Stretchability = 1
    };

    /// <summary>
    /// Spacing from page top to first system.
    /// </summary>
    /// <remarks>LILYPOND-REF: paper-defaults-init.ly:80-82</remarks>
    public VerticalSpacingSpec TopSystem { get; init; } = new()
    {
        BasicDistance = 1,
        MinimumDistance = 0,
        Padding = 1,
        Stretchability = 0
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
    /// Extra padding between note content of adjacent systems.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:483 in-note-system-padding
    /// LILYPOND-REF: ly/paper-defaults-init.ly — default 1.5 staff spaces
    ///
    /// This ensures a minimum gap between the note extents of adjacent systems,
    /// beyond what the general spacing padding provides. Applied as an additional
    /// floor on the minimum distance when positioning systems on a page.
    /// </remarks>
    public double InNoteSystemPadding { get; init; } = 1.5;

    /// <summary>
    /// Selects the appropriate spacing spec based on the context of two adjacent elements.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:488-535 spacing spec selection logic
    /// </remarks>
    public VerticalSpacingSpec SelectSpec(
        bool isFirstOnPage, bool isLastOnPage,
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
