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
        int firstMeasureIndex,
        bool isLastSystem = false,
        double? baseShortestDuration = null,
        double courtesySuffixWidth = 0,
        double indent = 0)
    {
        double prefixWidth = SpacingRules.CalculatePrefixWidth(keySharps, isFirstSystem);
        var measureLayouts = LayoutMeasuresForSystem(measures, keySharps, isFirstSystem, firstMeasureIndex, isLastSystem, baseShortestDuration, courtesySuffixWidth, indent);

        return new SystemLayout(
            systemIndex,
            y,
            _options.PageWidth - _options.MarginLeft - _options.MarginRight,
            prefixWidth,
            measureLayouts,
            Indent: indent);
    }

    /// <summary>
    /// Pre-calculates measure layouts for skyline building (without creating full SystemLayout).
    /// </summary>
    public ImmutableArray<MeasureLayout> LayoutMeasuresForSystem(
        List<Measure> measures,
        int keySharps,
        bool isFirstSystem,
        int firstMeasureIndex,
        bool isLastSystem = false,
        double? baseShortestDuration = null,
        double courtesySuffixWidth = 0,
        double indent = 0)
    {
        double prefixWidth = SpacingRules.CalculatePrefixWidth(keySharps, isFirstSystem);
        // System-internal coordinates are LINE-RELATIVE (0 = line start); the
        // renderer's margin translate places the whole line at MarginLeft once.
        // So startX/rightEdge must NOT include MarginLeft — baking it in here
        // would double-count it (see MultiStaffLayouter). availableWidth is the
        // same either way, so justification width is unchanged.
        // courtesySuffixWidth: end-of-line courtesy key signature (the next
        // line opens with a key change) — the music stops short of the right
        // edge so the cancellation + new signature fit after the barline.
        // indent: instrument-name space on the FIRST system (single-staff
        // scores used to ignore it entirely, so a part's name never printed).
        double startX = indent + prefixWidth;
        double rightEdge = _options.PageWidth - _options.MarginLeft - _options.MarginRight;
        double availableWidth = rightEdge - startX - courtesySuffixWidth;

        // Collect springs and barline widths for each measure
        var measureSprings = new List<ImmutableArray<Spring>>();
        var measureBarlineWidths = new List<double>();
        double totalBarlineWidth = 0;

        bool firstMeasureOfSystem = true;
        foreach (var measure in measures)
        {
            var springs = SpacingRules.CreateSpringsForMeasure(measure, baseShortestDuration);

            // LINE-START measure: spring 0 carries the prefix→first-note
            // spacing (space-alist of the last prefix item) instead of the
            // mid-line BarLine semi-shrink — see MultiStaffLayouter.
            if (firstMeasureOfSystem && springs.Length > 0)
            {
                var (ideal, min) = SpacingRules.FirstNoteSpring(keySharps, isFirstSystem);
                var s0 = springs[0];
                double newMin = Math.Max(min, s0.MinDistance);
                springs = springs.SetItem(0, new Spring(
                    Math.Max(ideal, newMin), newMin, inverseStretchStrength: 0));
                firstMeasureOfSystem = false;
            }
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
            
            // LILYPOND-REF: lily/page-spacing.cc ragged-right handling
            // In ragged mode, don't stretch lines that are shorter than available width
            // LILYPOND-REF: lily/simple-spacer.cc:175-205 Simple_spacer::solve()
            // LilyPond always applies the solved force, even for overfull lines.
            // An overfull system uses maximum compression force (not natural spacing).
            // LILYPOND-REF: scm/define-paper-variables.scm:472-474 — `ragged-last`
            // default is #f, so the last system is justified like the others.
            // RaggedRight (when set globally) still skips justification for every
            // system.
            if (_options.RaggedRight)
            {
                double naturalLength = solver.IdealTotalLength;
                if (naturalLength < springTargetWidth)
                {
                    // Line is shorter than available - use natural spacing (force = 0)
                    force = 0;
                }
                else
                {
                    // Line is too long - solve normally (may need compression)
                    var (solvedForce, _) = solver.Solve(springTargetWidth, ragged: true);
                    force = solvedForce;
                }
            }
            else
            {
                // Non-ragged: justify to fill available width
                var (solvedForce, _) = solver.Solve(springTargetWidth, ragged: false);
                force = solvedForce;
            }
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
        IReadOnlyList<LyricItem> lyrics,
        bool isLastSystem = false,
        double? baseShortestDuration = null,
        double courtesySuffixWidth = 0,
        double indent = 0)
    {
        double prefixWidth = SpacingRules.CalculatePrefixWidth(keySharps, isFirstSystem);
        var measureLayouts = LayoutMeasuresForSystem(measures, keySharps, isFirstSystem, firstMeasureIndex, lyrics, isLastSystem, baseShortestDuration, courtesySuffixWidth, indent);

        return new SystemLayout(
            systemIndex,
            y,
            _options.PageWidth - _options.MarginLeft - _options.MarginRight,
            prefixWidth,
            measureLayouts,
            Indent: indent);
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
        IReadOnlyList<LyricItem> lyrics,
        bool isLastSystem = false,
        double? baseShortestDuration = null,
        double courtesySuffixWidth = 0,
        double indent = 0)
    {
        double prefixWidth = SpacingRules.CalculatePrefixWidth(keySharps, isFirstSystem);
        // System-internal coordinates are LINE-RELATIVE (0 = line start); the
        // renderer's margin translate places the whole line at MarginLeft once.
        // So startX/rightEdge must NOT include MarginLeft — baking it in here
        // would double-count it (see MultiStaffLayouter). availableWidth is the
        // same either way, so justification width is unchanged. See the
        // non-lyrics overload for courtesySuffixWidth and indent.
        double startX = indent + prefixWidth;
        double rightEdge = _options.PageWidth - _options.MarginLeft - _options.MarginRight;
        double availableWidth = rightEdge - startX - courtesySuffixWidth;

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
                ? SpacingRules.CreateSpringsForMeasureWithLyrics(measure, measureIndex, lyrics, baseShortestDuration)
                : SpacingRules.CreateSpringsForMeasure(measure, baseShortestDuration);
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

            // LILYPOND-REF: lily/simple-spacer.cc:175-205 Simple_spacer::solve()
            // LilyPond always applies the solved force, even for overfull lines.
            // An overfull system uses maximum compression force (not natural spacing).
            // LILYPOND-REF: scm/define-paper-variables.scm:472-474 — `ragged-last`
            // default is #f, so the last system is justified like the others.
            // RaggedRight (when set globally) still skips justification for every
            // system.
            if (_options.RaggedRight)
            {
                double naturalLength = solver.IdealTotalLength;
                if (naturalLength < springTargetWidth)
                {
                    // Line is shorter than available - use natural spacing (force = 0)
                    force = 0;
                }
                else
                {
                    // Line is too long - solve normally (may need compression)
                    var (solvedForce, _) = solver.Solve(springTargetWidth, ragged: true);
                    force = solvedForce;
                }
            }
            else
            {
                // Non-ragged: justify to fill available width
                var (solvedForce, _) = solver.Solve(springTargetWidth, ragged: false);
                force = solvedForce;
            }
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
