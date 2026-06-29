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
    int SourcePosition,
    int SourceIndex = -1     // F3/B: index into score.ChordNames (data-pos resolved at render)
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

    /// <summary>For an independent chord ROW, the chord text baseline below the
    /// row band's top, so a ~1.5 ss symbol sits inside the reserved band.</summary>
    private const double ChordRowTextBaseline = 1.6;

    /// <summary>How far left of the first chord symbol the protrusion scan starts —
    /// enough for a centred symbol's left half, but short of the system-start clef.</summary>
    private const double ChordRowLeftMargin = 2.0;

    /// <summary>Extra clearance (staff spaces) added when notes protrude into the
    /// chord row, covering the accidentals/scripts above the notehead that the
    /// system skyline omits (an Emmentaler flat/sharp rises ~1 sp above its head).</summary>
    private const double ProtrudingAccidentalAllowance = 0.9;

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
        var prepared = new List<(ChordNameItem chord, double x, double staffOffset, bool topStaff, int sysIdx, int idx)>(chordNames.Length);
        for (int cni = 0; cni < chordNames.Length; cni++)
        {
            var chord = chordNames[cni];
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

            prepared.Add((chord, x, staffOffset, topStaff, sysIdx, cni));
        }

        // Per system, the peak protrusion of staff content above the staff top, sampled
        // under the top-staff chords. The whole chord line of a system shares one
        // baseline (a VerticalAxisGroup is placed at a single offset per system), so we
        // take the maximum over the chords in that system.
        // The chord line shares ONE baseline per system, so it must clear the
        // highest note any of its symbols sit over. A symbol has no note of its own
        // at a high beat between chords (e.g. a tall chord on beat 4 under a wide
        // "Gm7♭5" anchored on beat 3), so sampling only at the symbols' anchor
        // points misses it. Instead clear the max staff protrusion from the first
        // symbol rightward — excluding the system-start clef, which sits left of it.
        // LILYPOND-REF: lily/axis-group-interface.cc — the ChordNames
        // VerticalAxisGroup is skyline-spaced above the staff content it overlaps.
        var systemPeak = new Dictionary<int, double>();
        if (systemSkylines != null)
        {
            var systemMinX = new Dictionary<int, double>();
            foreach (var p in prepared)
            {
                if (!p.topStaff || p.sysIdx < 0)
                    continue;
                if (!systemMinX.TryGetValue(p.sysIdx, out var mx) || p.x < mx)
                    systemMinX[p.sysIdx] = p.x;
            }
            foreach (var (sysIdx, minX) in systemMinX)
            {
                if (sysIdx >= systemSkylines.Count)
                    continue;
                var up = systemSkylines[sysIdx].up;
                if (!up.IsEmpty)
                    systemPeak[sysIdx] = up.MaxProtrusionInRange(minX - ChordRowLeftMargin, double.PositiveInfinity);
            }
        }

        var results = ImmutableArray.CreateBuilder<ChordNameLayout>(prepared.Count);
        foreach (var p in prepared)
        {
            // Independent chord ROW: the symbol sits WITHIN its own row band (its
            // staff offset is the band top), not floated above an associated staff.
            if (p.chord.IsChordRow)
            {
                results.Add(new ChordNameLayout(
                    p.chord.MeasureIndex, p.x, p.staffOffset + ChordRowTextBaseline,
                    p.chord.ChordText, p.chord.SourcePosition, p.idx));
                continue;
            }

            // Y position: above the staff (negative = upward), offset to own staff.
            // Raise by the system's peak note protrusion (top-staff only) so the chord
            // line clears high notes/ledger lines; the StaffPadding floor reproduces the
            // measured no-protrusion distance (lead sheet without notes above the staff).
            double protrusion = p.topStaff && systemPeak.TryGetValue(p.sysIdx, out var pk) ? pk : 0;
            // The system skyline is built from noteheads/stems/ledgers and omits the
            // accidentals/scripts that sit above a protruding note, so add a small
            // allowance when notes protrude — otherwise a flat above a high chord
            // grazes the chord text. (No allowance in the common no-protrusion case,
            // which keeps the measured lead-sheet distance exact.)
            double accidentalAllowance = protrusion > 0 ? ProtrudingAccidentalAllowance : 0;
            double y = -(StaffPadding + protrusion + accidentalAllowance) + p.staffOffset;

            results.Add(new ChordNameLayout(
                p.chord.MeasureIndex, p.x, y, p.chord.ChordText, p.chord.SourcePosition, p.idx));
        }

        return results.ToImmutable();
    }
}
