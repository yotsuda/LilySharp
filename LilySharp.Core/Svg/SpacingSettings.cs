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

namespace LilySharp.Core.Svg;

/// <summary>
/// Spacing specification for vertical layout, following LilyPond conventions.
/// All values are in staff-spaces unless otherwise noted.
/// </summary>
public class SpacingSpec
{
    /// <summary>
    /// The ideal distance between reference points (in staff-spaces).
    /// This is the target spacing when there's no pressure to stretch or compress.
    /// </summary>
    public double BasicDistance { get; init; }

    /// <summary>
    /// The minimum distance allowed (in staff-spaces).
    /// Content will never be closer than this distance.
    /// </summary>
    public double MinimumDistance { get; init; }

    /// <summary>
    /// Extra space to prevent collision (in staff-spaces).
    /// Added to ensure elements don't overlap.
    /// </summary>
    public double Padding { get; init; }

    /// <summary>
    /// How much the spacing can stretch (inverse spring constant).
    /// Higher values mean the spacing stretches more easily.
    /// </summary>
    public double Stretchability { get; init; }

    public SpacingSpec(double basicDistance, double minimumDistance = 0,
                       double padding = 0, double stretchability = 0)
    {
        BasicDistance = basicDistance;
        MinimumDistance = minimumDistance;
        Padding = padding;
        Stretchability = stretchability;
    }
}

/// <summary>
/// Settings for vertical spacing between systems and page elements.
/// All spacing values are in staff-spaces following LilyPond conventions.
/// </summary>
public class SpacingSettings
{
    /// <summary>
    /// Spacing between two systems in the same score.
    /// Default: basic=12, min=8, padding=1, stretch=60
    /// </summary>
    public SpacingSpec SystemSystem { get; set; }
        = new(basicDistance: 12, minimumDistance: 8, padding: 1, stretchability: 60);

    /// <summary>
    /// Spacing between the last system of a score and the first system of the next score.
    /// Default: basic=14, min=8, padding=1, stretch=120
    /// </summary>
    public SpacingSpec ScoreSystem { get; set; }
        = new(basicDistance: 14, minimumDistance: 8, padding: 1, stretchability: 120);

    /// <summary>
    /// Spacing between a markup (title) and the following system.
    /// Default: basic=5, padding=0.5, stretch=30
    /// </summary>
    public SpacingSpec MarkupSystem { get; set; }
        = new(basicDistance: 5, padding: 0.5, stretchability: 30);

    /// <summary>
    /// Spacing between a score's last system and the following markup.
    /// Default: basic=12, padding=0.5, stretch=60
    /// </summary>
    public SpacingSpec ScoreMarkup { get; set; }
        = new(basicDistance: 12, padding: 0.5, stretchability: 60);

    /// <summary>
    /// Spacing between two consecutive markups (titles).
    /// Default: basic=1, padding=0.5
    /// </summary>
    public SpacingSpec MarkupMarkup { get; set; }
        = new(basicDistance: 1, padding: 0.5);

    /// <summary>
    /// Spacing from the top of the printable area to the first system.
    /// Default: basic=6, min=0, padding=1
    /// </summary>
    public SpacingSpec TopSystem { get; set; }
        = new(basicDistance: 6, minimumDistance: 0, padding: 1);

    /// <summary>
    /// Spacing from the top of the printable area to the first markup.
    /// Default: basic=4, min=0, padding=1
    /// </summary>
    public SpacingSpec TopMarkup { get; set; }
        = new(basicDistance: 4, minimumDistance: 0, padding: 1);

    /// <summary>
    /// Spacing from the last system to the bottom of the printable area.
    /// Default: basic=1, min=0, padding=1, stretch=30
    /// </summary>
    public SpacingSpec LastBottom { get; set; }
        = new(basicDistance: 1, minimumDistance: 0, padding: 1, stretchability: 30);

    /// <summary>
    /// Whether to leave the bottom of pages at their natural height (true)
    /// or stretch to fill the page (false).
    /// </summary>
    public bool RaggedBottom { get; set; } = false;

    /// <summary>
    /// Whether to leave the last page at its natural height (true)
    /// or stretch to fill the page (false).
    /// </summary>
    public bool RaggedLastBottom { get; set; } = true;

    /// <summary>
    /// Creates default spacing settings.
    /// </summary>
    public static SpacingSettings Default => new();
}

/// <summary>
/// Represents a spring in the vertical layout system.
/// Springs connect elements and allow them to stretch or compress.
/// Length formula: length = max(ideal + force × inverse_strength, minimum)
/// </summary>
public class Spring
{
    /// <summary>
    /// The ideal distance (unstretched length) in staff-spaces.
    /// </summary>
    public double IdealDistance { get; private set; }

    /// <summary>
    /// The minimum distance (cannot compress below this) in staff-spaces.
    /// </summary>
    public double MinDistance { get; private set; }

    /// <summary>
    /// Inverse stretch strength. Higher = stretches more easily.
    /// </summary>
    public double InverseStretchStrength { get; private set; }

    /// <summary>
    /// Inverse compress strength. Higher = compresses more easily.
    /// </summary>
    public double InverseCompressStrength { get; private set; }

    /// <summary>
    /// The force at which the spring becomes blocked (reaches minimum).
    /// </summary>
    public double BlockingForce { get; private set; }

    public Spring(double idealDistance = 0, double minDistance = 0)
    {
        IdealDistance = idealDistance;
        MinDistance = minDistance;
        SetDefaultStrength();
    }

    /// <summary>
    /// Creates a spring from a spacing specification.
    /// </summary>
    public static Spring FromSpec(SpacingSpec spec)
    {
        var spring = new Spring(spec.BasicDistance, spec.MinimumDistance);
        if (spec.Stretchability > 0)
            spring.InverseStretchStrength = spec.Stretchability;
        return spring;
    }

    /// <summary>
    /// Sets default strength based on ideal and minimum distance.
    /// </summary>
    public void SetDefaultStrength()
    {
        double length = IdealDistance - MinDistance;
        InverseStretchStrength = length > 0 ? length : 1.0;
        InverseCompressStrength = length > 0 ? length : 1.0;
        UpdateBlockingForce();
    }

    /// <summary>
    /// Sets the ideal distance.
    /// </summary>
    public void SetIdealDistance(double distance)
    {
        IdealDistance = distance;
        UpdateBlockingForce();
    }

    /// <summary>
    /// Sets the minimum distance.
    /// </summary>
    public void SetMinDistance(double distance)
    {
        MinDistance = distance;
        UpdateBlockingForce();
    }

    /// <summary>
    /// Ensures the minimum distance is at least the given value.
    /// </summary>
    public void EnsureMinDistance(double distance)
    {
        if (distance > MinDistance)
        {
            MinDistance = distance;
            UpdateBlockingForce();
        }
    }

    /// <summary>
    /// Sets the inverse stretch strength.
    /// </summary>
    public void SetInverseStretchStrength(double strength)
    {
        InverseStretchStrength = strength;
    }

    /// <summary>
    /// Calculates the length of the spring at a given force.
    /// </summary>
    public double Length(double force)
    {
        double inverseK = force >= 0 ? InverseStretchStrength : InverseCompressStrength;
        double length = IdealDistance + force * inverseK;
        return Math.Max(length, MinDistance);
    }

    private void UpdateBlockingForce()
    {
        // Force needed to compress to minimum
        if (InverseCompressStrength > 0)
            BlockingForce = (MinDistance - IdealDistance) / InverseCompressStrength;
        else
            BlockingForce = double.NegativeInfinity;
    }
}

/// <summary>
/// Details about a line (system or markup) for page layout.
/// Following LilyPond's Line_details structure from constrained-breaking.hh
/// </summary>
public class LineDetails
{
    /// <summary>
    /// Total height of this line in staff-spaces.
    /// </summary>
    public double Height { get; set; }

    /// <summary>
    /// Height adjusted for begin/rest of line considerations.
    /// </summary>
    public double Tallness { get; set; }

    /// <summary>
    /// Compulsory padding after this system in staff-spaces.
    /// Used when next line is a music system.
    /// </summary>
    public double Padding { get; set; }

    /// <summary>
    /// Compulsory padding when next line is a title/markup.
    /// </summary>
    public double TitlePadding { get; set; }

    /// <summary>
    /// Minimum distance to next element in staff-spaces.
    /// Used when next line is a music system.
    /// </summary>
    public double MinDistance { get; set; }

    /// <summary>
    /// Minimum distance when next line is a title/markup.
    /// </summary>
    public double TitleMinDistance { get; set; }

    /// <summary>
    /// Spring length (basic distance) to next element.
    /// Used when next line is a music system.
    /// </summary>
    public double Space { get; set; }

    /// <summary>
    /// Spring length when next line is a title/markup.
    /// </summary>
    public double TitleSpace { get; set; }

    /// <summary>
    /// Inverse spring constant (stretchability).
    /// </summary>
    public double InverseHooke { get; set; } = 1.0;

    /// <summary>
    /// Whether this is a title/markup (vs. a music system).
    /// </summary>
    public bool IsTitle { get; set; }

    /// <summary>
    /// Padding at the bottom of this line (for last line on page).
    /// </summary>
    public double BottomPadding { get; set; }

    /// <summary>
    /// Penalty for breaking page after this line.
    /// </summary>
    public double PagePenalty { get; set; }

    /// <summary>
    /// Gets the full height including any additional space.
    /// </summary>
    public double FullHeight() => Height;

    /// <summary>
    /// Gets the appropriate padding for the next line.
    /// </summary>
    public double GetPadding(LineDetails nextLine)
        => nextLine.IsTitle ? TitlePadding : Padding;

    /// <summary>
    /// Gets the appropriate minimum distance for the next line.
    /// </summary>
    public double GetMinDistance(LineDetails nextLine)
        => nextLine.IsTitle ? TitleMinDistance : MinDistance;

    /// <summary>
    /// Gets the appropriate spring length for the next line.
    /// </summary>
    public double GetSpace(LineDetails nextLine)
        => nextLine.IsTitle ? TitleSpace : Space;

    /// <summary>
    /// Calculates the spring length to the next line.
    /// Following LilyPond's Line_details::spring_length implementation.
    /// </summary>
    public double SpringLength(LineDetails nextLine, SpacingSettings settings)
    {
        // Use the appropriate space based on whether next line is title
        double space = nextLine.IsTitle ? TitleSpace : Space;

        // If space values weren't explicitly set, fall back to settings
        if (space == 0)
        {
            SpacingSpec spec;

            if (IsTitle && nextLine.IsTitle)
                spec = settings.MarkupMarkup;
            else if (IsTitle)
                spec = settings.MarkupSystem;
            else if (nextLine.IsTitle)
                spec = settings.ScoreMarkup;
            else
                spec = settings.SystemSystem;

            space = spec.BasicDistance;
        }

        // Spring length calculation: space - refpoint distance
        // For simplicity, we use tallness as refpoint distance approximation
        double refpointDist = Tallness;
        return Math.Max(0.0, space - refpointDist);
    }
}