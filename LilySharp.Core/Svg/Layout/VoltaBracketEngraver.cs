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
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for a volta bracket.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/volta-bracket.cc:60-120 print method
/// </remarks>
public readonly record struct VoltaBracketLayout(
    int StartMeasureIndex,      // First measure of this volta
    int EndMeasureIndex,        // Last measure of this volta
    double StartX,              // X position of bracket start
    double EndX,                // X position of bracket end
    double Y,                   // Y position (above staff)
    string VoltaText,           // Text to display (e.g., "1.")
    bool IsClosed,              // Has right hook
    int SourcePosition          // For click-to-source mapping
);

/// <summary>
/// Calculates positions for volta brackets.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/volta-bracket.cc:1-170 Volta_bracket_interface
/// LILYPOND-REF: lily/volta-engraver.cc:1-150 Volta_engraver
///
/// LilyPond volta brackets:
/// - Start with a vertical hook (downward)
/// - Have horizontal line at consistent Y above staff
/// - Display number text at start
/// - End with vertical hook if closed, or open if continuing
/// </remarks>
public static class VoltaBracketEngraver
{
    // LILYPOND-REF: scm/define-grobs.scm:4296 edge-height = (2.0 . 2.0) (VoltaBracket grob)
    private const double EdgeHeight = 2.0;

    // VoltaBracket Y placement is governed by side-position-interface +
    // outside-staff-priority in LP (no single literal — see VoltaBracket grob
    // properties at scm/define-grobs.scm:4292-4317). LilySharp uses a fixed
    // hand-tuned offset above the staff that matches typical LP output.
    private const double YOffset = -3.0;
    
    // Padding from barline
    private const double StartPadding = 0.3;
    private const double EndPadding = 0.3;

    /// <summary>
    /// Calculates layout for all volta brackets.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/volta-bracket.cc — brackets split at system breaks
    /// When a volta bracket spans multiple systems, it is split into segments.
    /// The first segment shows the volta text and has no right hook.
    /// Continuation segments have no left hook and no text.
    /// The last segment has a right hook if the bracket is closed.
    /// </remarks>
    public static ImmutableArray<VoltaBracketLayout> Calculate(
        ImmutableArray<VoltaBracketItem> voltaBrackets,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (voltaBrackets.IsDefaultOrEmpty)
            return ImmutableArray<VoltaBracketLayout>.Empty;

        // Build measure-to-system-index mapping
        var measureToSystemIdx = new Dictionary<int, int>();
        for (int si = 0; si < systems.Length; si++)
        {
            foreach (var m in systems[si].Measures)
                measureToSystemIdx[m.MeasureIndex] = si;
        }

        var layouts = ImmutableArray.CreateBuilder<VoltaBracketLayout>();

        foreach (var bracket in voltaBrackets)
        {
            if (bracket.StartMeasureIndex >= measureLayouts.Length ||
                bracket.EndMeasureIndex >= measureLayouts.Length)
                continue;

            int startSystemIdx = measureToSystemIdx.GetValueOrDefault(bracket.StartMeasureIndex, 0);
            int endSystemIdx = measureToSystemIdx.GetValueOrDefault(bracket.EndMeasureIndex, startSystemIdx);

            if (startSystemIdx == endSystemIdx)
            {
                // Same system — single bracket
                var startMeasure = measureLayouts[bracket.StartMeasureIndex];
                var endMeasure = measureLayouts[bracket.EndMeasureIndex];
                layouts.Add(new VoltaBracketLayout(
                    bracket.StartMeasureIndex,
                    bracket.EndMeasureIndex,
                    startMeasure.X + StartPadding,
                    endMeasure.X + endMeasure.Width - EndPadding,
                    YOffset,
                    bracket.VoltaText,
                    bracket.IsClosed,
                    bracket.SourcePosition
                ));
            }
            else
            {
                // Cross-system: split into one bracket per system
                for (int si = startSystemIdx; si <= endSystemIdx; si++)
                {
                    var system = systems[si];
                    if (system.Measures.IsDefaultOrEmpty)
                        continue;

                    int sysFirstMeasure = system.Measures[0].MeasureIndex;
                    int sysLastMeasure = system.Measures[^1].MeasureIndex;

                    int segStart = si == startSystemIdx ? bracket.StartMeasureIndex : sysFirstMeasure;
                    int segEnd = si == endSystemIdx ? bracket.EndMeasureIndex : sysLastMeasure;

                    if (segStart >= measureLayouts.Length || segEnd >= measureLayouts.Length)
                        continue;

                    var segStartMeasure = measureLayouts[segStart];
                    var segEndMeasure = measureLayouts[segEnd];

                    bool isFirst = (si == startSystemIdx);
                    bool isLast = (si == endSystemIdx);

                    // First segment shows volta text; continuations are empty
                    string segText = isFirst ? bracket.VoltaText : "";
                    // Only last segment has right hook if bracket is closed
                    bool segClosed = isLast && bracket.IsClosed;

                    layouts.Add(new VoltaBracketLayout(
                        segStart,
                        segEnd,
                        segStartMeasure.X + StartPadding,
                        segEndMeasure.X + segEndMeasure.Width - EndPadding,
                        YOffset,
                        segText,
                        segClosed,
                        bracket.SourcePosition
                    ));
                }
            }
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// Gets the edge height for volta bracket hooks.
    /// </summary>
    public static double GetEdgeHeight() => EdgeHeight;
}
