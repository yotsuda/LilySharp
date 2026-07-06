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
/// Layout information for a piano pedal bracket line.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: define-grobs.scm:2586-2605 PianoPedalBracket grob
/// </remarks>
public readonly record struct PedalBracketLayout(
    double StartX,           // Start X position (at "Ped." text)
    double EndX,             // End X position (at "*" release)
    double Y,                // Y position below staff (relative to system top)
    double EdgeHeight,       // Height of the end hook (vertical line at release)
    int StartMeasureIndex,   // For system Y lookup in renderer
    int SourcePosition       // For click-to-source mapping
);

/// <summary>
/// Detects and calculates piano pedal bracket positions.
/// </summary>
/// <remarks>
/// LILYPOND-REF: piano-pedal-engraver.cc:216-400 Pedal event processing
/// LILYPOND-REF: define-grobs.scm:2586-2605 PianoPedalBracket parameters
/// LILYPOND-REF: define-grobs.scm:3255-3296 SustainPedal/SustainPedalLineSpanner
///
/// NOTE: the DEFAULT pedalSustainStyle is 'text — just "Ped." at sustain-on and
/// "*" at sustain-off, with NO connecting line or hook. The horizontal line +
/// release hook belong to the 'bracket / 'mixed styles. Lily# currently emits
/// only the text style (the "Ped." / "*" music marks), so these bracket layouts
/// are not wired into the layout pipeline; this class is retained as the basis
/// for a future bracket style.
/// </remarks>
internal static class PedalEngraver
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
                        activeOn.SourcePosition,
                        activeOn.AnchorItemIndex, mark.AnchorItemIndex,
                        activeOn.AnchorTiming, mark.AnchorTiming));
                }
                activeOn = mark;
            }
            else if (mark.Type == offType && activeOn != null)
            {
                brackets.Add(new PedalBracketItem(
                    pedalType,
                    activeOn.MeasureIndex,
                    mark.MeasureIndex,
                    activeOn.SourcePosition,
                    activeOn.AnchorItemIndex, mark.AnchorItemIndex,
                    activeOn.AnchorTiming, mark.AnchorTiming));
                activeOn = null;
            }
        }
    }

    /// <summary>
    /// Calculates layout positions for pedal brackets.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: piano-pedal-bracket.cc — bracket Y is below the lowest staff
    /// In grand staff context, the pedal bracket is placed below the bass (lower) staff,
    /// not below the treble (upper) staff.
    /// </remarks>
    public static ImmutableArray<PedalBracketLayout> Calculate(
        ImmutableArray<PedalBracketItem> brackets,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (brackets.IsDefaultOrEmpty)
            return ImmutableArray<PedalBracketLayout>.Empty;

        var layouts = ImmutableArray.CreateBuilder<PedalBracketLayout>(brackets.Length);

        // Build measure-to-system mapping
        var measureToSystem = new Dictionary<int, SystemLayout>();
        foreach (var system in systems)
        {
            foreach (var measure in system.Measures)
            {
                measureToSystem[measure.MeasureIndex] = system;
            }
        }

        // The bracket line runs on the SAME baseline as the "Ped." text and
        // the release "*" (classic Ped.____* notation): the below-mark
        // baseline under the system's LAST visible staff.
        double systemBottom = 4.0;
        if (systems.Length > 0 && !systems[0].StaffGroups.IsDefaultOrEmpty)
        {
            foreach (var group in systems[0].StaffGroups)
                foreach (var st in group.Staves)
                    if (!st.IsHidden)
                        systemBottom = Math.Max(systemBottom, st.Y + st.Height);
        }
        double bracketY = MusicMarkEngraver.BelowMarkBaseline(systemBottom);

        foreach (var bracket in brackets)
        {
            if (bracket.StartMeasureIndex >= measureLayouts.Length ||
                bracket.EndMeasureIndex >= measureLayouts.Length)
                continue;

            var startMeasure = measureLayouts[bracket.StartMeasureIndex];
            var endMeasure = measureLayouts[bracket.EndMeasureIndex];

            // X anchors at the engaging / releasing note's column (LP places
            // "Ped." and "*" at the note, not the measure start).
            double startX = AnchorX(startMeasure, bracket.StartItemIndex, bracket.StartTiming);
            double endX = AnchorX(endMeasure, bracket.EndItemIndex, bracket.EndTiming);

            // Ensure minimum length
            if (endX - startX < 2.0)
                endX = startX + 2.0;

            layouts.Add(new PedalBracketLayout(
                startX,
                endX,
                bracketY,
                EdgeHeight,
                bracket.StartMeasureIndex,
                bracket.SourcePosition));
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// X of the note column a pedal mark attaches to. Multi-staff layouts use the
    /// shared, voice-independent timing columns (like MetronomeMark); single-staff
    /// uses the item slot; falls back to a small inset at the measure start.
    /// </summary>
    private static double AnchorX(MeasureLayout ml, int itemIndex, Fraction timing)
    {
        if (!ml.Columns.IsDefaultOrEmpty)
            return ml.X + ml.GetXForTiming(timing);
        if (itemIndex >= 0 && itemIndex < ml.Items.Length)
            return ml.X + ml.Items[itemIndex].X;
        return ml.X + BoundPadding;
    }
}
