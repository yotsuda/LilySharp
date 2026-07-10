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
    string ChordText,        // Display text (e.g., "Cm7", "B♭7", or "IIm7" in Roman mode)
    int SourcePosition,
    int SourceIndex = -1,    // F3/B: index into score.ChordNames (data-pos resolved at render)
    string? AboveLine = null // `as both`: the Roman degree stacked ABOVE ChordText
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
internal static class ChordNameEngraver
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

    /// <summary>On a chords-ONLY grid sheet the row is the measure grid itself
    /// ("a staff with the lines removed", full staff-height barlines): centre
    /// the symbol between the bar ends — cap height 1.87 about the band middle
    /// (2.0), baseline ≈ 2.0 + 1.87/2.</summary>
    private const double GridChordBaseline = 2.9;

    /// <summary>How far left of the first chord symbol the protrusion scan starts —
    /// enough for a centred symbol's left half, but short of the system-start clef.</summary>
    private const double ChordRowLeftMargin = 2.0;



    /// <summary>
    /// Calculates chord name layouts from collected items.
    /// </summary>
    /// <remarks>
    /// <c>systemSkylines</c> carries the per-system up/down skylines (1:1 with
    /// <c>systems</c>). When supplied, the chord-name line of each system is raised so it
    /// clears notes/ledger lines that poke above the staff — LilyPond skyline-spaces the
    /// ChordNames VerticalAxisGroup above the staff's up-skyline rather than from a fixed offset.
    /// LILYPOND-REF: lily/axis-group-interface.cc skyline-based VerticalAxisGroup spacing;
    /// ly/engraver-init.ly:721-722 ChordNames staff-affinity=DOWN, relatedstaff padding=0.5.
    /// </remarks>
    public static ImmutableArray<ChordNameLayout> Calculate(
        ImmutableArray<ChordNameItem> chordNames,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        ImmutableArray<Measure> measures = default,
        Dictionary<int, ImmutableArray<Measure>>? measuresByStaff = null,
        Func<int, int, double>? staffYAt = null,
        Func<int, double>? minStaffYAt = null,
        IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)>? systemSkylines = null,
        bool chordGridSheet = false,
        Func<int, int, VerticalSkyline?>? lowerStaffUpSkyline = null)
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
        // The topmost-staff offset is resolved per chord (minStaffYAt), because under
        // hara-kiri the set of visible staves — and thus the minimum offset — can
        // differ between systems.

        // Pre-resolve each chord's X and per-staff offset.
        var prepared = new List<(ChordNameItem chord, double x, double staffOffset, bool topStaff, int sysIdx, int idx)>(chordNames.Length);
        for (int cni = 0; cni < chordNames.Length; cni++)
        {
            var chord = chordNames[cni];
            if (chord.MeasureIndex >= measureLayouts.Length)
                continue;

            var ml = measureLayouts[chord.MeasureIndex];
            var cnMeasures = LayoutUtilities.ResolveStaffMeasures(measuresByStaff, chord.StaffIndex, measures);
            double staffOffset = staffYAt?.Invoke(chord.MeasureIndex, chord.StaffIndex) ?? 0;

            // chordnames entries carry their own rhythm: place them by musical
            // moment against the shared column grid (the same X the renderer draws
            // a note at that timing), exactly as bound-voice lyrics do. The
            // note-attached @chord path keeps the item-index offset.
            double x = chord.UseTiming
                ? ml.X + ml.GetXForTiming(chord.Timing)
                : ml.X + LayoutUtilities.GetItemXOffset(
                    cnMeasures, chord.MeasureIndex, chord.ItemIndex, ml);
            bool topStaff = staffOffset <= (minStaffYAt?.Invoke(chord.MeasureIndex) ?? 0) + 1e-6;
            int sysIdx = measureToSystem.TryGetValue(chord.MeasureIndex, out var si) ? si : -1;

            prepared.Add((chord, x, staffOffset, topStaff, sysIdx, cni));
        }

        // Half the drawn width of a centred (TextAnchor.Middle) chord symbol, at
        // the renderer's chord font size (FontSize 4.0 × 0.65 = 2.6), measured with
        // SANS bold advances — the face the names render in.
        double HalfWidth(ChordNameItem c) =>
            Math.Max(1.0, Rendering.SansTextMetrics.MeasureBold(DisplayText(c).Text, 2.6) / 2);

        // Resolve horizontal overlaps between adjacent TIMING-placed symbols ON THE
        // SAME LINE (same system + staff). Proportional timing X can pack two names —
        // e.g. a chord on a beat that falls inside a longer note — closer than their
        // text boxes; shift the later one right until its box clears the previous
        // one's. Inline @chord symbols (UseTiming false) stay anchored to their note.
        prepared.Sort((a, b) =>
            a.sysIdx != b.sysIdx ? a.sysIdx.CompareTo(b.sysIdx)
            : a.chord.StaffIndex != b.chord.StaffIndex ? a.chord.StaffIndex.CompareTo(b.chord.StaffIndex)
            : a.x.CompareTo(b.x));
        const double chordGap = 0.6; // minimum ink gap between adjacent names (staff spaces)
        for (int i = 1; i < prepared.Count; i++)
        {
            var prev = prepared[i - 1];
            var cur = prepared[i];
            if (!cur.chord.UseTiming || !prev.chord.UseTiming
                || cur.sysIdx != prev.sysIdx || cur.chord.StaffIndex != prev.chord.StaffIndex)
                continue;
            double minX = prev.x + HalfWidth(prev.chord) + HalfWidth(cur.chord) + chordGap;
            if (cur.x < minX)
                prepared[i] = (cur.chord, minX, cur.staffOffset, cur.topStaff, cur.sysIdx, cur.idx);
        }

        // Per system, the peak protrusion of staff content above the staff top,
        // sampled UNDER EACH SYMBOL (its own X window), then maxed over the
        // system's symbols — the chord line shares one baseline per system.
        // Sampling the whole line instead (first symbol → end) floated every
        // system's chords above its single tallest stem tip: an ordinary
        // up-stem note pokes ~1 ss above the top line, so chord names ended up
        // ~3 ss high even over quiet bars. LilyPond's spacing is the per-X
        // skyline DISTANCE between the ChordNames line (texts at their own Xs)
        // and the staff — content between the symbols does not push the line.
        // LILYPOND-REF: lily/axis-group-interface.cc — VerticalAxisGroup
        // skyline spacing; lily/skyline.cc Skyline::distance (per-X minimum).
        // The topmost staff's chord line skyline-spaces above the SYSTEM up-skyline
        // (script-augmented). A LOWER staff's chord line clears THAT staff's own
        // notes instead — the system skyline carries only the top staff, so without
        // a per-staff skyline a `staff bass with chords` row overprints the bass's
        // high/ledger noteheads. The peak is keyed per (system, staff): each staff's
        // chord row is its own baseline. For the common lead sheet (chords on the top
        // staff only) the key collapses to the top staff and the result is unchanged.
        var linePeak = new Dictionary<(int sys, int staff), double>();
        foreach (var p in prepared)
        {
            if (p.sysIdx < 0 || p.chord.IsChordRow)
                continue;
            VerticalSkyline? up;
            if (p.topStaff)
                up = systemSkylines != null && p.sysIdx < systemSkylines.Count
                    ? systemSkylines[p.sysIdx].up : null;
            else
                up = lowerStaffUpSkyline?.Invoke(p.sysIdx, p.chord.StaffIndex);
            if (up == null || up.IsEmpty)
                continue;
            // The symbol's footprint: centred text at the chord font size
            // (renderer: FontSize 4.0 × 0.65 = 2.6), measured with SANS
            // bold advances — the face chord names render in. Measured,
            // not guessed: a wide "Gm7♭5" reaches over the NEXT beat's
            // tall chord, which a narrow per-character estimate missed.
            double halfWidth = HalfWidth(p.chord);
            double peak = up.MaxProtrusionInRange(p.x - halfWidth, p.x + halfWidth);
            var key = (p.sysIdx, p.chord.StaffIndex);
            if (!linePeak.TryGetValue(key, out var cur) || peak > cur)
                linePeak[key] = peak;
        }

        var results = ImmutableArray.CreateBuilder<ChordNameLayout>(prepared.Count);
        foreach (var p in prepared)
        {
            // Independent chord ROW: the symbol sits WITHIN its own row band (its
            // staff offset is the band top), not floated above an associated staff.
            if (p.chord.IsChordRow)
            {
                double rowBaseline = chordGridSheet ? GridChordBaseline : ChordRowTextBaseline;
                var (rowText, rowAbove) = DisplayText(p.chord);
                results.Add(new ChordNameLayout(
                    p.chord.MeasureIndex, p.x, p.staffOffset + rowBaseline,
                    rowText, p.chord.SourcePosition, p.idx, AboveLine: rowAbove));
                continue;
            }

            // Y position: above the staff (negative = upward), offset to own staff.
            // Raise by this (system, staff) chord line's peak note protrusion so the
            // line clears high notes/ledger lines; the StaffPadding floor reproduces the
            // measured no-protrusion distance (lead sheet without notes above the staff).
            // The skyline carries the real ink of noteheads, stems, ledgers,
            // accidentals (SkylineBuilder) and — for the top staff — above-staff
            // scripts (LayoutEngine.AugmentSkylinesWithScripts), so the sampled peak
            // needs no allowances — it IS the content under the symbol.
            double protrusion = linePeak.TryGetValue((p.sysIdx, p.chord.StaffIndex), out var pk) ? pk : 0;
            double y = -(StaffPadding + protrusion) + p.staffOffset;

            var (text, above) = DisplayText(p.chord);
            results.Add(new ChordNameLayout(
                p.chord.MeasureIndex, p.x, y, text, p.chord.SourcePosition, p.idx, AboveLine: above));
        }

        return results.ToImmutable();
    }

    /// <summary>The symbol's display text for its mode, plus the optional line stacked
    /// above it: Names → the absolute name; Roman → the degree (falling back to the
    /// name when no structure resolved); Both → the name with the degree above it.</summary>
    private static (string Text, string? Above) DisplayText(ChordNameItem c) => c.DisplayMode switch
    {
        ChordDisplayMode.Roman => (c.RomanText ?? c.ChordText, null),
        ChordDisplayMode.Both => (c.ChordText, c.RomanText),
        _ => (c.ChordText, null),
    };
}
