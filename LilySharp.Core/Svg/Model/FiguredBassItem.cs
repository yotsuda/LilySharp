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
    /// <summary>
    /// The figure number (1-9), or 0 for an empty figure placeholder.
    /// </summary>
    int Number,
    /// <summary>
    /// Alteration: 0=none, 1=sharp, -1=flat, 2=natural (cautionary).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/figured-bass-engraver.cc:120 alteration property
    /// </remarks>
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

    public FiguredBassItem(
        ImmutableArray<FiguredBassFigure> figures,
        int measureIndex,
        int itemIndex,
        int sourcePosition)
    {
        Figures = figures;
        MeasureIndex = measureIndex;
        ItemIndex = itemIndex;
        SourcePosition = sourcePosition;
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
                currentAlteration = 0;
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

        return figures.Count > 0 ? figures.ToImmutable() : null;
    }
}
