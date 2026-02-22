using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for a piano pedal bracket line.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: define-grobs.scm:2586-2605 PianoPedalBracket grob
/// </remarks>
public readonly record struct PedalBracketLayout(
    double StartX,       // Start X position (at "Ped." text)
    double EndX,         // End X position (at "*" release)
    double Y,            // Y position below staff
    double EdgeHeight,   // Height of the end hook (vertical line at release)
    int SourcePosition   // For click-to-source mapping
);

/// <summary>
/// Detects and calculates piano pedal bracket positions.
/// </summary>
/// <remarks>
/// LILYPOND-REF: piano-pedal-engraver.cc:216-400 Pedal event processing
/// LILYPOND-REF: define-grobs.scm:2586-2605 PianoPedalBracket parameters
/// LILYPOND-REF: define-grobs.scm:3255-3296 SustainPedal/SustainPedalLineSpanner
///
/// In text style, LilyPond renders:
/// - "Ped." at sustain-on position
/// - "*" at sustain-off position
/// - A horizontal line connecting them (bracket)
/// - A vertical hook at the release point
/// </remarks>
public static class PedalEngraver
{
    // LILYPOND-REF: define-grobs.scm:2590 bound-padding = 1.0
    private const double BoundPadding = 1.0;

    // LILYPOND-REF: define-grobs.scm:2594 edge-height = (1.0 . 1.0)
    private const double EdgeHeight = 1.0;

    // LILYPOND-REF: define-grobs.scm:3283 padding = 1.2
    private const double StaffPadding = 1.2;

    // Y position below staff (staff bottom = 4.0, plus padding)
    // LILYPOND-REF: define-grobs.scm:3280 direction = DOWN
    private const double BracketY = 6.5;

    /// <summary>
    /// Detects pedal bracket spans from music marks.
    /// Pairs pedal-on marks with their corresponding pedal-off marks.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: piano-pedal-engraver.cc:293-339 Event pairing logic
    /// </remarks>
    public static ImmutableArray<PedalBracketItem> DetectPedalBrackets(
        ImmutableArray<MusicMarkItem> musicMarks)
    {
        if (musicMarks.IsDefaultOrEmpty)
            return ImmutableArray<PedalBracketItem>.Empty;

        var brackets = ImmutableArray.CreateBuilder<PedalBracketItem>();

        // Process each pedal type independently
        DetectBracketsForType(musicMarks, MusicMarkType.SustainOn, MusicMarkType.SustainOff,
            PedalType.Sustain, brackets);
        DetectBracketsForType(musicMarks, MusicMarkType.SostenutoOn, MusicMarkType.SostenutoOff,
            PedalType.Sostenuto, brackets);
        DetectBracketsForType(musicMarks, MusicMarkType.UnaCordaOn, MusicMarkType.UnaCordaOff,
            PedalType.UnaCorda, brackets);

        return brackets.ToImmutable();
    }

    private static void DetectBracketsForType(
        ImmutableArray<MusicMarkItem> musicMarks,
        MusicMarkType onType, MusicMarkType offType,
        PedalType pedalType,
        ImmutableArray<PedalBracketItem>.Builder brackets)
    {
        // Collect all on/off marks for this pedal type, ordered by position
        var marks = musicMarks
            .Where(m => m.Type == onType || m.Type == offType)
            .OrderBy(m => m.MeasureIndex)
            .ToList();

        MusicMarkItem? activeOn = null;

        foreach (var mark in marks)
        {
            if (mark.Type == onType)
            {
                // If there's already an active pedal and we get another ON,
                // end the current bracket at this measure
                if (activeOn != null)
                {
                    brackets.Add(new PedalBracketItem(
                        pedalType,
                        activeOn.MeasureIndex,
                        mark.MeasureIndex,
                        activeOn.SourcePosition));
                }
                activeOn = mark;
            }
            else if (mark.Type == offType && activeOn != null)
            {
                brackets.Add(new PedalBracketItem(
                    pedalType,
                    activeOn.MeasureIndex,
                    mark.MeasureIndex,
                    activeOn.SourcePosition));
                activeOn = null;
            }
        }
    }

    /// <summary>
    /// Calculates layout positions for pedal brackets.
    /// </summary>
    public static ImmutableArray<PedalBracketLayout> Calculate(
        ImmutableArray<PedalBracketItem> brackets,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (brackets.IsDefaultOrEmpty)
            return ImmutableArray<PedalBracketLayout>.Empty;

        var layouts = ImmutableArray.CreateBuilder<PedalBracketLayout>(brackets.Length);

        // Build measure-to-system mapping for Y coordinates
        var measureToSystemY = new Dictionary<int, double>();
        foreach (var system in systems)
        {
            foreach (var measure in system.Measures)
            {
                measureToSystemY[measure.MeasureIndex] = system.Y;
            }
        }

        foreach (var bracket in brackets)
        {
            if (bracket.StartMeasureIndex >= measureLayouts.Length ||
                bracket.EndMeasureIndex >= measureLayouts.Length)
                continue;

            var startMeasure = measureLayouts[bracket.StartMeasureIndex];
            var endMeasure = measureLayouts[bracket.EndMeasureIndex];

            // Start X: beginning of start measure + small offset for text
            double startX = startMeasure.X + BoundPadding;

            // End X: beginning of end measure + offset
            double endX = endMeasure.X + BoundPadding;

            // Ensure minimum length
            if (endX - startX < 2.0)
                endX = startX + 2.0;

            layouts.Add(new PedalBracketLayout(
                startX,
                endX,
                BracketY,
                EdgeHeight,
                bracket.SourcePosition));
        }

        return layouts.ToImmutable();
    }
}
