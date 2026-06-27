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
/// Layout result for a single chord name.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm - ChordName grob properties
/// </remarks>
public readonly record struct ChordNameLayout(
    int MeasureIndex,
    double X,                // X position (staff spaces from page left)
    double Y,                // Y position (staff spaces from page top, above staff)
    string ChordText,        // Display text (e.g., "Cm7", "B♭7")
    int SourcePosition
);

/// <summary>
/// Calculates layout positions for chord name symbols.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/chord-name.cc - ChordName::after_line_breaking
/// LILYPOND-REF: scm/define-grobs.scm - ChordName: font-family=sans, font-size=1.5
/// LILYPOND-REF: ly/engraver-init.ly:571-592 - ChordNames context
///
/// Chord names are positioned above the staff with padding.
/// In LilyPond, ChordNames is a separate context above the staff.
/// </remarks>
public static class ChordNameEngraver
{
    /// <summary>Distance from the associated staff's top line up to the chord-name baseline.</summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly:703-723 - ChordNames context:
    ///   staff-affinity = DOWN, nonstaff-relatedstaff-spacing.padding = 0.5
    /// The ChordNames context has staff-affinity = DOWN, so it is spaced relative to the
    /// staff BELOW it (i.e. it sits just above its associated staff), NOT floated high above.
    /// LilyPond places the chord-name baseline ~0.6 staff-spaces above that staff's top line
    /// (relatedstaff-spacing padding 0.5 plus the glyph's skyline clearance; measured 0.587
    /// against LilyPond 2.24.4 for both solo and top-of-system lead sheets).
    ///
    /// NOTE: an earlier value of 5.5 was the basic-distance of the LYRICS/DYNAMICS contexts
    /// (engraver-init.ly:650/692), mis-attributed to ChordNames. It floated single-staff chords
    /// far too high and, on a lower staff, shoved the chord up into the staff above it.
    ///
    /// Known simplification: like the other annotation engravers, this uses a fixed offset from
    /// the staff's top LINE rather than the staff's full skyline, so notes/ledger lines poking
    /// above the staff are not yet cleared (LilyPond would skyline-space the ChordNames line).
    /// </remarks>
    private const double StaffPadding = 0.6;

    /// <summary>
    /// Calculates chord name layouts from collected items.
    /// </summary>
    /// <param name="systemSkylines">
    /// Per-system up/down skylines (1:1 with <paramref name="systems"/>). When supplied,
    /// the chord-name line of each system is raised so it clears notes/ledger lines that
    /// poke above the staff — LilyPond skyline-spaces the ChordNames VerticalAxisGroup
    /// above the staff's up-skyline rather than from a fixed offset.
    /// LILYPOND-REF: lily/axis-group-interface.cc skyline-based VerticalAxisGroup spacing;
    /// ly/engraver-init.ly:721-722 ChordNames staff-affinity=DOWN, relatedstaff padding=0.5.
    /// </param>
    public static ImmutableArray<ChordNameLayout> Calculate(
        ImmutableArray<ChordNameItem> chordNames,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        ImmutableArray<Measure> measures = default,
        Dictionary<int, ImmutableArray<Measure>>? measuresByStaff = null,
        Dictionary<int, double>? staffYByIndex = null,
        IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)>? systemSkylines = null)
    {
        if (chordNames.IsDefaultOrEmpty || systems.IsDefaultOrEmpty || measureLayouts.IsDefaultOrEmpty)
            return ImmutableArray<ChordNameLayout>.Empty;

        // Map measure index -> system index, so each chord can find its system's
        // up-skyline (the skyline is the system's TOPMOST staff content).
        var measureToSystem = new Dictionary<int, int>();
        for (int s = 0; s < systems.Length; s++)
            foreach (var m in systems[s].Measures)
                measureToSystem[m.MeasureIndex] = s;

        // The top staff's chord line is the only one the system up-skyline describes;
        // lower-staff chords keep the fixed offset (their staff's skyline isn't here).
        double minStaffOffset = staffYByIndex != null && staffYByIndex.Count > 0
            ? staffYByIndex.Values.Min() : 0;

        // Pre-resolve each chord's X and per-staff offset.
        var prepared = new List<(ChordNameItem chord, double x, double staffOffset, bool topStaff, int sysIdx)>(chordNames.Length);
        foreach (var chord in chordNames)
        {
            if (chord.MeasureIndex >= measureLayouts.Length)
                continue;

            var ml = measureLayouts[chord.MeasureIndex];
            var cnMeasures = measuresByStaff != null
                && measuresByStaff.TryGetValue(chord.StaffIndex, out var mm) ? mm : measures;
            double staffOffset = staffYByIndex != null
                && staffYByIndex.TryGetValue(chord.StaffIndex, out var so) ? so : 0;

            // chordnames entries carry their own rhythm: place them by musical
            // moment against the shared column grid (the same X the renderer draws
            // a note at that timing), exactly as bound-voice lyrics do. The
            // note-attached @chord path keeps the item-index offset.
            double x = chord.UseTiming
                ? ml.X + ml.GetXForTiming(chord.Timing)
                : ml.X + LayoutUtilities.GetItemXOffset(
                    cnMeasures, chord.MeasureIndex, chord.ItemIndex, ml);
            bool topStaff = staffOffset <= minStaffOffset + 1e-6;
            int sysIdx = measureToSystem.TryGetValue(chord.MeasureIndex, out var si) ? si : -1;

            prepared.Add((chord, x, staffOffset, topStaff, sysIdx));
        }

        // Per system, the peak protrusion of staff content above the staff top, sampled
        // under the top-staff chords. The whole chord line of a system shares one
        // baseline (a VerticalAxisGroup is placed at a single offset per system), so we
        // take the maximum over the chords in that system.
        var systemPeak = new Dictionary<int, double>();
        if (systemSkylines != null)
        {
            foreach (var p in prepared)
            {
                if (!p.topStaff || p.sysIdx < 0 || p.sysIdx >= systemSkylines.Count)
                    continue;
                var up = systemSkylines[p.sysIdx].up;
                if (up.IsEmpty)
                    continue;
                double h = up.Height(p.x);
                if (double.IsInfinity(h) || double.IsNaN(h))
                    continue;
                double protrusion = Math.Max(0, -h);
                if (!systemPeak.TryGetValue(p.sysIdx, out var cur) || protrusion > cur)
                    systemPeak[p.sysIdx] = protrusion;
            }
        }

        var results = ImmutableArray.CreateBuilder<ChordNameLayout>(prepared.Count);
        foreach (var p in prepared)
        {
            // Y position: above the staff (negative = upward), offset to own staff.
            // Raise by the system's peak note protrusion (top-staff only) so the chord
            // line clears high notes/ledger lines; the StaffPadding floor reproduces the
            // measured no-protrusion distance (lead sheet without notes above the staff).
            double protrusion = p.topStaff && systemPeak.TryGetValue(p.sysIdx, out var pk) ? pk : 0;
            double y = -(StaffPadding + protrusion) + p.staffOffset;

            results.Add(new ChordNameLayout(
                p.chord.MeasureIndex, p.x, y, p.chord.ChordText, p.chord.SourcePosition));
        }

        return results.ToImmutable();
    }
}
