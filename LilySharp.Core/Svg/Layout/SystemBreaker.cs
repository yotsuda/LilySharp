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
/// Breaks measures into systems (lines) using optimal or greedy algorithm.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/constrained-breaking.cc
/// LILYPOND-REF: lily/page-breaking.cc (break decisions)
/// </remarks>
public sealed class SystemBreaker
{
    private readonly LayoutOptions _options;

    public SystemBreaker(LayoutOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Breaks measures into systems.
    /// Uses the first voice as representative for measure widths.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/constrained-breaking.cc
    /// Uses Knuth-Plass optimal algorithm when UseOptimalLineBreaking is true,
    /// otherwise falls back to greedy first-fit algorithm.
    /// </remarks>
    public List<List<Measure>> BreakIntoSystems(Score score,
                                                double? baseShortestDuration = null)
    {
        var measures = score.Voice.Measures;
        double firstPrefixWidth = SpacingRules.CalculatePrefixWidth(score.KeySignature.Sharps, includeTimeSignature: true,
            score.TimeSignature.Beats, score.TimeSignature.BeatType);
        double continuationPrefixWidth = SpacingRules.CalculatePrefixWidth(score.KeySignature.Sharps, includeTimeSignature: false);

        if (_options.UseOptimalLineBreaking)
        {
            // Use Knuth-Plass optimal line breaking
            var breaker = new KnuthPlassBreaker(
                _options.ContentWidth,
                firstPrefixWidth,
                continuationPrefixWidth,
                _options.LineBreakingTolerance,
                raggedRight: _options.RaggedRight);

            return breaker.BreakIntoLines(measures, baseShortestDuration);
        }

        // Fallback to greedy first-fit algorithm
        return BreakIntoSystemsGreedy(measures, firstPrefixWidth, continuationPrefixWidth, baseShortestDuration);
    }

    /// <summary>
    /// Breaks measures into systems for a multi-staff score.
    /// Uses the primary voice of the first staff group for measure widths.
    /// </summary>
    public List<List<Measure>> BreakIntoSystems(MultiStaffScore score,
                                                double? baseShortestDuration = null)
    {
        var measures = score.StaffGroups[0].PrimaryStaff.PrimaryVoice.Measures;
        double firstPrefixWidth = SpacingRules.CalculatePrefixWidth(score.KeySignature.Sharps, includeTimeSignature: true,
            score.TimeSignature.Beats, score.TimeSignature.BeatType);
        double continuationPrefixWidth = SpacingRules.CalculatePrefixWidth(score.KeySignature.Sharps, includeTimeSignature: false);

        if (_options.UseOptimalLineBreaking)
        {
            var breaker = new KnuthPlassBreaker(
                _options.ContentWidth,
                firstPrefixWidth,
                continuationPrefixWidth,
                _options.LineBreakingTolerance,
                raggedRight: _options.RaggedRight);

            return breaker.BreakIntoLines(measures, baseShortestDuration);
        }

        return BreakIntoSystemsGreedy(measures, firstPrefixWidth, continuationPrefixWidth, baseShortestDuration);
    }

    /// <summary>
    /// Breaks measures into systems using a greedy first-fit algorithm.
    /// </summary>
    private List<List<Measure>> BreakIntoSystemsGreedy(
        ImmutableArray<Measure> measures,
        double firstPrefixWidth,
        double continuationPrefixWidth,
        double? baseShortestDuration = null)
    {
        var result = new List<List<Measure>>();
        var currentSystem = new List<Measure>();

        double availableWidth = _options.ContentWidth;
        double currentWidth = firstPrefixWidth;

        foreach (var measure in measures)
        {
            double measureWidth = SpacingRules.CalculateMeasureIdealWidth(measure, baseShortestDuration);

            // Check if measure fits in current system
            if (currentSystem.Count > 0 && currentWidth + measureWidth > availableWidth)
            {
                // Start new system
                result.Add(currentSystem);
                currentSystem = new List<Measure>();
                currentWidth = continuationPrefixWidth;
            }

            currentSystem.Add(measure);
            currentWidth += measureWidth;

            // Force line break if measure has break keyword
            if (measure.HasBreakAfter && currentSystem.Count > 0)
            {
                result.Add(currentSystem);
                currentSystem = new List<Measure>();
                currentWidth = continuationPrefixWidth;
            }
        }

        // Add final system
        if (currentSystem.Count > 0)
            result.Add(currentSystem);

        return result;
    }
}
