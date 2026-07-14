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
internal sealed class SystemBreaker
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
        // Fold each system's indent into the prefix width so the break decision
        // matches the rendered fit (the layout subtracts the same indent from the
        // available width). Default indent 0 → no-op.
        // LILYPOND-REF: scm/output-lib.scm system-start-text indent.
        double firstPrefixWidth = SpacingRules.CalculatePrefixWidth(score.KeySignature.Sharps, includeTimeSignature: true,
            score.TimeSignature.Beats, score.TimeSignature.BeatType) + _options.Indent;
        double continuationPrefixWidth = SpacingRules.CalculatePrefixWidth(score.KeySignature.Sharps, includeTimeSignature: false)
            + _options.ShortIndent;

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
                                                double? baseShortestDuration = null,
                                                IReadOnlyList<int>? precomputedLineSizes = null)
    {
        var measures = score.PrimaryContentStaff.PrimaryVoice.Measures;

        // F3 incremental cutoff (the F3 incremental design notes §4): when the caller
        // has verified the line-break gate (per-measure spring vector + prefix
        // widths) is unchanged, the break solution cannot change, so regroup the
        // new measures by the cached line sizes and skip the spring computation
        // and the DP entirely. The default path (precomputedLineSizes == null) is
        // byte-identical to before.
        if (precomputedLineSizes != null)
            return RegroupBySizes(measures, precomputedLineSizes);

        // Fold each system's indent into the prefix width so the break decision
        // matches the rendered fit (the layout subtracts the same indent from the
        // available width). Default indent 0 → no-op.
        // LILYPOND-REF: scm/output-lib.scm system-start-text indent.
        // An all-tab score reserves neither key nor time-signature width (tab prints
        // neither), so the break budget matches the reclaimed prefix the layout uses;
        // otherwise the score key and the opening meter, as before.
        double firstPrefixWidth = SpacingRules.CalculatePrefixWidth(score.LeadingKeySharps, includeTimeSignature: !score.AllStavesTab,
            score.TimeSignature.Beats, score.TimeSignature.BeatType) + _options.Indent;
        double continuationPrefixWidth = SpacingRules.CalculatePrefixWidth(score.LeadingKeySharps, includeTimeSignature: false)
            + _options.ShortIndent;

        if (_options.UseOptimalLineBreaking)
        {
            var breaker = new KnuthPlassBreaker(
                _options.ContentWidth,
                firstPrefixWidth,
                continuationPrefixWidth,
                _options.LineBreakingTolerance,
                raggedRight: _options.RaggedRight);

            return breaker.BreakIntoLines(measures, ComputeMultiStaffSpringData(score, baseShortestDuration));
        }

        return BreakIntoSystemsGreedy(measures, firstPrefixWidth, continuationPrefixWidth, baseShortestDuration);
    }

    /// <summary>
    /// The multi-staff line-break gate: each measure priced by the COMBINED
    /// springs across all staves (the same springs the actual system layout
    /// solves with). This vector — with the paper width and prefix widths — is
    /// the sole determinant of the break solution, so the incremental driver
    /// compares it to decide whether line-breaking can be skipped.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/paper-column.cc — columns aggregate staves, so pricing
    /// by the primary staff alone would pack lines wherever that staff rests
    /// while another staff is dense.
    /// </remarks>
    internal static MeasureSpringData[] ComputeMultiStaffSpringData(MultiStaffScore score,
                                                                    double? baseShortestDuration)
    {
        var measures = score.PrimaryContentStaff.PrimaryVoice.Measures;
        var layouter = new MeasureLayouter();
        var springData = new MeasureSpringData[measures.Length];
        for (int i = 0; i < measures.Length; i++)
        {
            var primaryMeasure = measures[i];
            var allTimings = MultiStaffLayouter.CollectAllTimingsForMeasure(score, i);
            var allMeasures = MultiStaffLayouter.CollectAllMeasuresAtIndex(score, i);
            var springs = layouter.CreateTimingSprings(
                primaryMeasure, allTimings, baseShortestDuration, allMeasures);

            // Price lyric syllables into the break gate exactly as the system
            // layout does (MultiStaffLayouter), so a syllable that widens a measure
            // can push it to the next line instead of overflowing. Without this the
            // breaker under-counts lyric-heavy bars and packs lines too tightly.
            // LILYPOND-REF: lily/lyric-extender.cc / spacing — syllable widths join
            // the column springs the spacing-spanner solves.
            if (!score.Lyrics.IsDefaultOrEmpty)
            {
                var lyricMeasure = score.IsLeadSheet
                    ? MultiStaffLayouter.DensestMeasure(allMeasures)
                    : primaryMeasure;
                springs = LyricSpacing.ApplyLyricSpacing(springs, lyricMeasure, i, score.Lyrics);
            }
            if (score.IsLeadSheet)
            {
                // Mirror the system layout (MultiStaffLayouter): the break gate
                // must price chord widths + the grid-cell floor identically.
                springs = SpacingRules.ApplyChordRowSpacing(springs, allTimings, i, score.ChordNames);
                springs = SpacingRules.EnsureLeadSheetBarWidth(springs);
            }
            else if (!score.ChordNames.IsDefaultOrEmpty)
            {
                // Mirror the system layout: attached chord symbols price
                // their widths on every measure (LP ChordName extent).
                springs = SpacingRules.ApplyChordRowSpacing(
                    springs, allTimings, i, score.ChordNames, includeAttached: true);
            }

            // Mirror the system layout: a wide script (fermata / ornament) reserves
            // its sideways reach per staff, so a bar it widens is priced the same in
            // the break gate and cannot overflow when actually laid out.
            if (!score.Articulations.IsDefaultOrEmpty)
            {
                int artStaffIndex = 0;
                foreach (var aGroup in score.StaffGroups)
                    foreach (var aStaff in aGroup.Staves)
                    {
                        if (i < aStaff.PrimaryVoice.Measures.Length)
                            springs = SpacingRules.ApplyArticulationSpacing(
                                springs, allTimings, aStaff.PrimaryVoice.Measures[i],
                                score.Articulations, i, artStaffIndex);
                        artStaffIndex++;
                    }
            }

            double ideal = 0, min = 0, invStretch = 0;
            foreach (var s in springs)
            {
                ideal += s.IdealDistance;
                min += s.MinDistance;
                invStretch += s.InverseStretchStrength;
            }
            double barlines = SpacingRules.GetBarlineWidth(primaryMeasure.StartBarline)
                            + SpacingRules.GetBarlineWidth(primaryMeasure.EndBarline);
            springData[i] = new MeasureSpringData(ideal + barlines, min + barlines, invStretch,
                primaryMeasure.BreakPenalty, primaryMeasure.LineBreakPermission);
        }
        return springData;
    }

    /// <summary>
    /// Regroups measures into systems by cached per-line measure counts — the
    /// incremental reuse of a prior break solution when the gate is unchanged.
    /// </summary>
    private static List<List<Measure>> RegroupBySizes(ImmutableArray<Measure> measures, IReadOnlyList<int> sizes)
    {
        var groups = new List<List<Measure>>(sizes.Count);
        int idx = 0;
        foreach (int size in sizes)
        {
            var group = new List<Measure>(size);
            for (int k = 0; k < size && idx < measures.Length; k++)
                group.Add(measures[idx++]);
            groups.Add(group);
        }
        // Defensive: a correct gate guarantees the sizes cover every measure, but
        // never drop measures if a caller misuses the hook — append the remainder.
        if (idx < measures.Length)
        {
            var tail = new List<Measure>(measures.Length - idx);
            while (idx < measures.Length)
                tail.Add(measures[idx++]);
            groups.Add(tail);
        }
        return groups;
    }

    /// <summary>
    /// Breaks measures into systems using a greedy first-fit algorithm.
    /// Break permissions are honored the same way the optimal path does:
    /// <c>break</c> (Force) always ends the system, and a width-driven break
    /// never lands on a Forbid (noBreak) boundary — the Forbid-joined tail
    /// moves to the next system instead, unless the whole system is one chain
    /// (then it stays and goes overfull; permissions are absolute).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/constrained-breaking.cc — break_permission_ is a hard
    /// constraint, not a preference weight.
    /// </remarks>
    internal List<List<Measure>> BreakIntoSystemsGreedy(
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
                // The break may not land after a noBreak measure: carry the
                // whole Forbid-joined tail over to the new system. keep == 0
                // means the entire system is one chain — no legal break exists,
                // so keep filling (overfull).
                int keep = currentSystem.Count;
                while (keep > 0 && currentSystem[keep - 1].LineBreakPermission == BreakPermission.Forbid)
                    keep--;
                if (keep > 0)
                {
                    var carried = currentSystem.GetRange(keep, currentSystem.Count - keep);
                    currentSystem.RemoveRange(keep, currentSystem.Count - keep);
                    result.Add(currentSystem);
                    currentSystem = new List<Measure>(carried);
                    currentWidth = continuationPrefixWidth;
                    foreach (var carriedMeasure in carried)
                        currentWidth += SpacingRules.CalculateMeasureIdealWidth(carriedMeasure, baseShortestDuration);
                }
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
