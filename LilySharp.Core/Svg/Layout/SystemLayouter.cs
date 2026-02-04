using System.Collections.Immutable;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layouts measures within a system and calculates system geometry.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/spacing-spanner.cc:musical_column_spacing()
/// LILYPOND-REF: lily/system.cc
/// </remarks>
public sealed class SystemLayouter
{
    private readonly LayoutOptions _options;
    private readonly MeasureLayouter _measureLayouter;

    public SystemLayouter(LayoutOptions options, MeasureLayouter measureLayouter)
    {
        _options = options;
        _measureLayouter = measureLayouter;
    }

    /// <summary>
    /// Layouts a single system with justification.
    /// </summary>
    /// <remarks>
    /// Delegates to LayoutMeasuresForSystem for actual layout calculation,
    /// then wraps the result in a SystemLayout.
    /// </remarks>
    public SystemLayout LayoutSystem(
        int systemIndex,
        List<Measure> measures,
        double y,
        int keySharps,
        bool isFirstSystem,
        int firstMeasureIndex)
    {
        double prefixWidth = SpacingRules.CalculatePrefixWidth(keySharps, isFirstSystem);
        var measureLayouts = LayoutMeasuresForSystem(measures, keySharps, isFirstSystem, firstMeasureIndex);

        return new SystemLayout(
            systemIndex,
            y,
            prefixWidth,
            measureLayouts);
    }

    /// <summary>
    /// Pre-calculates measure layouts for skyline building (without creating full SystemLayout).
    /// </summary>
    public ImmutableArray<MeasureLayout> LayoutMeasuresForSystem(
        List<Measure> measures,
        int keySharps,
        bool isFirstSystem,
        int firstMeasureIndex)
    {
        double prefixWidth = SpacingRules.CalculatePrefixWidth(keySharps, isFirstSystem);
        double startX = _options.MarginLeft + prefixWidth;
        double rightEdge = _options.PageWidth - _options.MarginRight;
        double availableWidth = rightEdge - startX;

        // Collect springs and barline widths for each measure
        var measureSprings = new List<ImmutableArray<Spring>>();
        var measureBarlineWidths = new List<double>();
        double totalBarlineWidth = 0;

        foreach (var measure in measures)
        {
            var springs = SpacingRules.CreateSpringsForMeasure(measure);
            measureSprings.Add(springs);

            double barlineWidth = SpacingRules.GetBarlineWidth(measure.StartBarline)
                                + SpacingRules.GetBarlineWidth(measure.EndBarline);
            measureBarlineWidths.Add(barlineWidth);
            totalBarlineWidth += barlineWidth;
        }

        // Collect all springs and solve for target width
        var allSprings = measureSprings.SelectMany(s => s).ToImmutableArray();
        double springTargetWidth = availableWidth - totalBarlineWidth;

        double force = 0;
        if (allSprings.Length > 0)
        {
            var solver = new SpringSolver(allSprings);
            var (solvedForce, fits) = solver.Solve(springTargetWidth, _options.RaggedRight);
            force = fits ? solvedForce : 0;
        }

        // Layout measures using the solved force
        var measureLayouts = new List<MeasureLayout>();
        double currentX = startX;

        for (int i = 0; i < measures.Count; i++)
        {
            double measureWidth = measureBarlineWidths[i];
            foreach (var spring in measureSprings[i])
            {
                measureWidth += spring.Length(force);
            }

            var itemLayouts = _measureLayouter.LayoutItems(measures[i], measureWidth, measureSprings[i], force);

            measureLayouts.Add(new MeasureLayout(
                firstMeasureIndex + i,
                currentX,
                measureWidth,
                itemLayouts));

            currentX += measureWidth;
        }

        return measureLayouts.ToImmutableArray();
    }

    /// <summary>
    /// Layouts a single system with justification, considering lyrics.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/separation-item.cc:49-70 set_distance()
    /// Lyrics width is factored into note spacing to prevent syllable overlap.
    /// </remarks>
    public SystemLayout LayoutSystem(
        int systemIndex,
        List<Measure> measures,
        double y,
        int keySharps,
        bool isFirstSystem,
        int firstMeasureIndex,
        IReadOnlyList<LyricItem> lyrics)
    {
        double prefixWidth = SpacingRules.CalculatePrefixWidth(keySharps, isFirstSystem);
        var measureLayouts = LayoutMeasuresForSystem(measures, keySharps, isFirstSystem, firstMeasureIndex, lyrics);

        return new SystemLayout(
            systemIndex,
            y,
            prefixWidth,
            measureLayouts);
    }

    /// <summary>
    /// Pre-calculates measure layouts for skyline building, considering lyrics.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-spacing.cc:80-85 skyline-based min_distance
    /// When lyrics are present, their width affects the minimum distance between notes.
    /// </remarks>
    public ImmutableArray<MeasureLayout> LayoutMeasuresForSystem(
        List<Measure> measures,
        int keySharps,
        bool isFirstSystem,
        int firstMeasureIndex,
        IReadOnlyList<LyricItem> lyrics)
    {
        double prefixWidth = SpacingRules.CalculatePrefixWidth(keySharps, isFirstSystem);
        double startX = _options.MarginLeft + prefixWidth;
        double rightEdge = _options.PageWidth - _options.MarginRight;
        double availableWidth = rightEdge - startX;

        // Collect springs and barline widths for each measure
        var measureSprings = new List<ImmutableArray<Spring>>();
        var measureBarlineWidths = new List<double>();
        double totalBarlineWidth = 0;

        for (int i = 0; i < measures.Count; i++)
        {
            var measure = measures[i];
            int measureIndex = firstMeasureIndex + i;

            // Use lyrics-aware spring creation if lyrics exist
            var springs = lyrics.Count > 0
                ? SpacingRules.CreateSpringsForMeasureWithLyrics(measure, measureIndex, lyrics)
                : SpacingRules.CreateSpringsForMeasure(measure);
            measureSprings.Add(springs);

            double barlineWidth = SpacingRules.GetBarlineWidth(measure.StartBarline)
                                + SpacingRules.GetBarlineWidth(measure.EndBarline);
            measureBarlineWidths.Add(barlineWidth);
            totalBarlineWidth += barlineWidth;
        }

        // Collect all springs and solve for target width
        var allSprings = measureSprings.SelectMany(s => s).ToImmutableArray();
        double springTargetWidth = availableWidth - totalBarlineWidth;

        double force = 0;
        if (allSprings.Length > 0)
        {
            var solver = new SpringSolver(allSprings);
            var (solvedForce, fits) = solver.Solve(springTargetWidth, _options.RaggedRight);
            force = fits ? solvedForce : 0;
        }

        // Layout measures using the solved force
        var measureLayouts = new List<MeasureLayout>();
        double currentX = startX;

        for (int i = 0; i < measures.Count; i++)
        {
            double measureWidth = measureBarlineWidths[i];
            foreach (var spring in measureSprings[i])
            {
                measureWidth += spring.Length(force);
            }

            var itemLayouts = _measureLayouter.LayoutItems(measures[i], measureWidth, measureSprings[i], force);

            measureLayouts.Add(new MeasureLayout(
                firstMeasureIndex + i,
                currentX,
                measureWidth,
                itemLayouts));

            currentX += measureWidth;
        }

        return measureLayouts.ToImmutableArray();
    }
}
