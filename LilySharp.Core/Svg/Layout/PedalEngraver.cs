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
/// LILYPOND-REF: define-grobs.scm:2855-2873 PianoPedalBracket grob
/// </remarks>
public readonly record struct PedalBracketLayout(
    double StartX,           // Start X position (at "Ped." text)
    double EndX,             // End X position (at "*" release)
    double Y,                // Y position below staff (relative to system top)
    double EdgeHeight,       // Height of the end hook (vertical line at release)
    int StartMeasureIndex,   // For system Y lookup in renderer
    int SourcePosition,      // For click-to-source mapping
    // Mixed style ("Ped." text then a line): the LEFT hook is omitted and the
    // line starts after the text. LILYPOND-REF: piano-pedal-bracket.cc:80-88.
    bool IsMixed = false,
    // A pedal CHANGE (release + re-engage on the same note) abuts the previous /
    // next bracket. LilyPond draws the shared end not as a vertical hook but as a
    // flared edge; two abutting flares form the "/\" notch at the change, while
    // the outer ends stay vertical. LILYPOND-REF: scm/define-grobs.scm
    // PianoPedalBracket bracket-flare = (0.5 . 0.5).
    bool StartChange = false,
    bool EndChange = false,
    // F3/B: index into the bracket list DetectPedalBrackets rebuilds from the live score,
    // so a reused layout re-derives its data-pos instead of carrying a stale source offset.
    // The same shape MusicMarkLayout uses against BuildAllMarks: the list is not a score
    // side-table, it is reconstructed, and reconstructing it is deterministic.
    // ⚠️ THIS IS WHAT MADE PEDAL SCORES REUSE-ELIGIBLE. IncrementalCompiler.ReuseSafe used
    // to decline whole-layout reuse for any score carrying a pedal bracket, under a comment
    // asserting the array was "always empty today" — showcase/03-piano has had pedals all
    // along, and the benchmark that asserts reuse fires (IncrementalSessionBenchmark) had
    // been failing on exactly that.
    int SourceIndex = -1
);

/// <summary>
/// Detects and calculates piano pedal bracket positions.
/// </summary>
/// <remarks>
/// LILYPOND-REF: piano-pedal-engraver.cc:216-400 Pedal event processing
/// LILYPOND-REF: define-grobs.scm:2855-2873 PianoPedalBracket parameters
/// LILYPOND-REF: define-grobs.scm:3573-3619 SustainPedal/SustainPedalLineSpanner
///
/// Style selection is per-part (Staff.PedalStyle, from the `pedal` part
/// property). LayoutEngine runs this engraver for staves whose style is
/// Bracket (Lily# default) or Mixed and suppresses the corresponding "Ped." /
/// "*" text marks; the Text style keeps the marks and emits no bracket.
/// </remarks>
internal static class PedalEngraver
{
    // LILYPOND-REF: define-grobs.scm:2857 bound-padding = 1.0
    private const double BoundPadding = 1.0;

    // LILYPOND-REF: define-grobs.scm:2860 edge-height = (1.0 . 1.0)
    private const double EdgeHeight = 1.0;

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
        ImmutableArray<MeasureLayout> measureLayouts,
        bool isMixed = false)
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
                        systemBottom = Math.Max(systemBottom, st.Height - st.Y);
        }
        double bracketY = MusicMarkEngraver.BelowMarkBaseline(systemBottom);

        for (int bi = 0; bi < brackets.Length; bi++)
        {
            var bracket = brackets[bi];
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
                bracket.SourcePosition,
                isMixed,
                SourceIndex: bi));
        }

        // Mark abutting ends as pedal CHANGES: where one bracket ends exactly where
        // the next begins (a release + re-engage on the same note), both shared ends
        // render as flared edges (the "/\" notch) instead of vertical hooks.
        for (int a = 0; a < layouts.Count; a++)
            for (int b = 0; b < layouts.Count; b++)
            {
                if (a == b) continue;
                if (Math.Abs(layouts[a].EndX - layouts[b].StartX) < 0.01)
                {
                    layouts[a] = layouts[a] with { EndChange = true };
                    layouts[b] = layouts[b] with { StartChange = true };
                }
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
