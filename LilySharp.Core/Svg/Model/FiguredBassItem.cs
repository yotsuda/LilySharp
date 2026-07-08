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

using System.Collections.Immutable;

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// A single figure in a figured bass group (e.g., "6", "4♯").
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/figured-bass-engraver.cc - BassFigureEvent
/// LILYPOND-REF: scm/define-grob-interfaces.scm - bass-figure-interface
/// </remarks>
public readonly record struct FiguredBassFigure(
    // The figure number (1-9), or 0 for an empty figure placeholder.
    int Number,
    // Alteration: 0=none, 1=sharp, -1=flat, 2=natural (cautionary).
    // LILYPOND-REF: lily/figured-bass-engraver.cc:120 alteration property
    int Alteration = 0
)
{
    /// <summary>
    /// Gets the display text for this figure (number + optional accidental).
    /// </summary>
    public string DisplayText
    {
        get
        {
            string numStr = Number > 0 ? Number.ToString() : "";
            string altStr = Alteration switch
            {
                1 => "\u266F",   // ♯
                -1 => "\u266D",  // ♭
                2 => "\u266E",   // ♮
                _ => ""
            };
            return numStr + altStr;
        }
    }
}

/// <summary>
/// A group of figured bass figures attached to a bass note.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/figured-bass-engraver.cc - Figure_group struct
/// LILYPOND-REF: scm/define-grobs.scm:362-380 - BassFigure grob defaults
///
/// Figured bass appears as stacked numbers below bass notes, indicating
/// chord inversions and alterations. Common figures:
/// - 6 = first inversion
/// - 6/4 = second inversion (6 and 4 stacked)
/// - 7 = seventh chord
/// - 6/4/3 = third inversion of seventh
///
/// Syntax in LilySharp: @fig.6 (single), @fig.6.4 (two figures),
/// @fig.6.s (with sharp), @fig.4.f (with flat), @fig.7.n (with natural)
/// </remarks>
public sealed record FiguredBassItem
{
    /// <summary>The figures in this group, ordered top to bottom.</summary>
    public ImmutableArray<FiguredBassFigure> Figures { get; }

    /// <summary>Measure index containing this figured bass.</summary>
    public int MeasureIndex { get; }

    /// <summary>Item index of the bass note within the measure.</summary>
    public int ItemIndex { get; }

    /// <summary>Source position for click-to-source mapping.</summary>
    public int SourcePosition { get; }

    /// <summary>Global staff index this figured bass belongs to (multi-staff
    /// routing; see <c>DynamicItem.StaffIndex</c>). 0 for single-staff.</summary>
    public int StaffIndex { get; }

    /// <summary>Creates a figured bass group attached to a bass note.</summary>
    public FiguredBassItem(
        ImmutableArray<FiguredBassFigure> figures,
        int measureIndex,
        int itemIndex,
        int sourcePosition,
        int staffIndex = 0)
    {
        Figures = figures;
        MeasureIndex = measureIndex;
        ItemIndex = itemIndex;
        SourcePosition = sourcePosition;
        StaffIndex = staffIndex;
    }

    /// <summary>
    /// Parses a figured bass annotation string (e.g., "fig.6.4", "fig.7.6.s.4.f").
    /// Returns null if the string is not a valid figured bass annotation.
    /// </summary>
    /// <remarks>
    /// Syntax (dot-separated):
    /// - "fig.N" where N is a digit (1-9)
    /// - "fig.N.M" for two stacked figures
    /// - "fig.N.M.L" for three stacked figures
    /// - Alteration after figure: "fig.6.s" (sharp), "fig.4.f" (flat), "fig.7.n" (natural)
    /// - Mixed: "fig.7.6.s.4.f" = 7, 6♯, 4♭
    /// </remarks>
    public static ImmutableArray<FiguredBassFigure>? ParseFigures(string markName)
    {
        if (!markName.StartsWith("fig.", StringComparison.OrdinalIgnoreCase))
            return null;

        var parts = markName.Substring(4).Split('.');
        if (parts.Length == 0 || parts.All(string.IsNullOrEmpty))
            return null;

        var figures = ImmutableArray.CreateBuilder<FiguredBassFigure>();
        int currentNumber = -1;
        int currentAlteration = 0;
        int pendingAlteration = 0; // a leading '#' seen BEFORE its figure number

        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part))
                return null;

            if (int.TryParse(part, out int number) && number >= 0 && number <= 9)
            {
                // Flush previous figure if any
                if (currentNumber >= 0)
                    figures.Add(new FiguredBassFigure(currentNumber, currentAlteration));

                currentNumber = number;
                currentAlteration = pendingAlteration; // a leading '#6' binds here
                pendingAlteration = 0;
            }
            else if (part == "#")
            {
                // '#' is the jazz / continuo sharp. Written before a number ('#6')
                // it binds to the coming figure; alone ('#') it is a bare sharp.
                // (Suffix sharp keeps the existing 's' spelling: '6.s'.)
                pendingAlteration = 1;
            }
            else if (currentNumber >= 0 && part.Length == 1)
            {
                // Alteration suffix for the current figure
                currentAlteration = part[0] switch
                {
                    's' or 'S' => 1,   // sharp
                    'f' or 'F' => -1,  // flat
                    'n' or 'N' => 2,   // natural
                    _ => 0
                };
                if (currentAlteration == 0)
                    return null; // Invalid alteration
            }
            else
            {
                return null; // Invalid part
            }
        }

        // Flush last figure
        if (currentNumber >= 0)
            figures.Add(new FiguredBassFigure(currentNumber, currentAlteration));

        // A lone '#' with no figure at all is a standalone sharp (raised third).
        if (pendingAlteration != 0 && currentNumber < 0)
            figures.Add(new FiguredBassFigure(0, pendingAlteration));

        return figures.Count > 0 ? figures.ToImmutable() : null;
    }
}
