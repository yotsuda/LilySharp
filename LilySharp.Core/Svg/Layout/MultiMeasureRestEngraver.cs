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
/// Layout for a multi-measure rest spanning <c>MeasureCount</c> consecutive measures.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/multi-measure-rest.cc — Multi_measure_rest grob
/// LILYPOND-REF: scm/define-grobs.scm MultiMeasureRest (expand-limit . 10)
/// When MeasureCount &lt;= ExpandLimit (default 10) the LP renderer combines
/// whole/breve/long rest glyphs (church_rest); above that limit it draws an
/// H-bar (big_rest) with the count printed above. This struct carries the
/// information that a renderer needs to draw either form.
/// </remarks>
public readonly record struct MultiMeasureRestLayout(
    int StartMeasureIndex,
    int MeasureCount,
    /// <summary>X coordinate of the leftmost measure's start.</summary>
    double StartX,
    /// <summary>X coordinate of the rightmost measure's end.</summary>
    double EndX,
    /// <summary>Y coordinate of the rest's vertical center (staff middle).</summary>
    double Y,
    /// <summary>True ⇒ church_rest (1..ExpandLimit), false ⇒ big_rest (H-bar).</summary>
    bool UseChurchRest);

/// <summary>
/// Detects runs of consecutive measures that contain a single full-measure rest
/// and groups them into <see cref="MultiMeasureRestLayout"/> entries.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/multi-measure-rest-engraver.cc — process_music
/// A "full-measure rest" is detected as a measure whose only content is a
/// single <see cref="RestItem"/> with no fingering / dynamics / etc. The
/// renderer collapses such runs into a single visual MMR symbol.
/// </remarks>
public static class MultiMeasureRestEngraver
{
    /// <summary>
    /// Threshold above which the church_rest combination is replaced by an H-bar.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm MultiMeasureRest (expand-limit . 10).</remarks>
    public const int ExpandLimit = 10;

    /// <summary>
    /// Calculates MMR layouts for a single-staff score.
    /// </summary>
    public static ImmutableArray<MultiMeasureRestLayout> Calculate(
        Score score,
        ImmutableArray<SystemLayout> systems,
        double staffHeight,
        int staffIndex = -1,
        IReadOnlyList<ImmutableArray<Measure>>? allStaffMeasures = null)
    {
        if (score.Voices.IsDefaultOrEmpty)
            return ImmutableArray<MultiMeasureRestLayout>.Empty;

        var measureMap = LayoutUtilities.BuildMeasureMap(systems);
        var voice = score.Voice;
        var builder = ImmutableArray.CreateBuilder<MultiMeasureRestLayout>();

        // A measure collapses into a multi-measure rest only when EVERY staff
        // rests it. LilyPond keeps the measures (and their barlines) separate when
        // another staff has content — the resting staff then shows individual
        // whole rests, not a merged MMR symbol. Verified against LilyPond 2.24
        // (single staff R1*4 → individual rests + barlines; only \compressMMRests
        // over all-resting measures merges them). LILYPOND-REF: lily/bar-engraver.cc
        // (barlines from Timing, independent of MMR) + lily/multi-measure-rest.cc.
        bool RestsEverywhere(int m)
        {
            if (m >= voice.Measures.Length || !IsFullMeasureRest(voice.Measures[m]))
                return false;
            if (allStaffMeasures != null)
                foreach (var sm in allStaffMeasures)
                    if (m >= sm.Length || !IsFullMeasureRest(sm[m]))
                        return false;
            return true;
        }

        int mi = 0;
        while (mi < voice.Measures.Length)
        {
            // Skip measures not resting across the whole staff group.
            if (!RestsEverywhere(mi))
            {
                mi++;
                continue;
            }

            // Greedily extend the run while measures stay in the SAME system AND
            // continue to be full-measure rests. Cross-system MMRs are LP-faithful
            // to break at the system boundary (the symbol can't span systems).
            if (!measureMap.TryGetValue(mi, out var startInfo))
            {
                mi++;
                continue;
            }
            var (startSystem, startMeasure) = startInfo;

            int runStart = mi;
            int runEnd = mi;
            while (runEnd + 1 < voice.Measures.Length &&
                   RestsEverywhere(runEnd + 1) &&
                   measureMap.TryGetValue(runEnd + 1, out var nextInfo) &&
                   nextInfo.System.SystemIndex == startSystem.SystemIndex)
            {
                runEnd++;
            }

            int count = runEnd - runStart + 1;
            if (count >= 1)
            {
                if (!measureMap.TryGetValue(runEnd, out var endInfo))
                {
                    mi = runEnd + 1;
                    continue;
                }
                var (_, endMeasure) = endInfo;

                double startX = startMeasure.X;
                double endX = endMeasure.X + endMeasure.Width;
                double y = LayoutUtilities.ResolveStaffMiddleY(startSystem, staffIndex, staffHeight);

                builder.Add(new MultiMeasureRestLayout(
                    StartMeasureIndex: runStart,
                    MeasureCount: count,
                    StartX: startX,
                    EndX: endX,
                    Y: y,
                    UseChurchRest: count <= ExpandLimit));
            }

            mi = runEnd + 1;
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// True iff the measure contains exactly one <see cref="RestItem"/> filling
    /// (or longer than) the time signature — the canonical "rest the whole measure".
    /// </summary>
    internal static bool IsFullMeasureRest(Measure measure)
    {
        if (measure.Items.Length != 1)
            return false;
        if (measure.Items[0] is not RestItem rest)
            return false;
        if (rest.IsSpacer)
            return false; // invisible chord-row filler — not a real rest
        // Whole-note rest covers any time signature up to 4/4. Anything dotted /
        // longer also qualifies (LP's `R1` is a full-measure rest regardless of
        // actual time signature).
        return rest.BaseDuration >= new Fraction(1, 1);
    }
}
