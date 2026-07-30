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
    // Y-up (frame B): staff-spaces above the SYSTEM top, up-positive. (The symbol
    // sits above its staff / in its row band, both system-relative — NOT page-top;
    // the renderer reflects it to device against the measure's system top,
    // sy + old-Y == sy − YUp.)
    double YUp,
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
/// LILYPOND-REF: ly/engraver-init.ly:703-725 - ChordNames context
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
    /// This is the padding FLOOR: the row is then raised further by the per-(system, staff)
    /// note protrusion so it skyline-clears notes/ledger lines poking above the staff (see
    /// the linePeak pass below). On a lower staff, MultiStaffLayouter.ReserveChordRowBand
    /// feeds the same band into the inter-staff gap so the staff above clears the row too.
    /// </remarks>
    private const double StaffPadding = 0.6;

    /// <summary>For an independent chord ROW, the chord text baseline below the
    /// row band's top, so a ~1.5 ss symbol sits inside the reserved band.</summary>
    /// <remarks>
    /// LILYSHARP-OWN: the band is Lily#'s model of an independent row (HANDOFF 3); LilyPond
    /// has no band, only the ChordNames VerticalAxisGroup whose reference point IS this
    /// baseline. ⚠️ THIS IS THE SEAM between the two, and there is exactly one of it:
    /// everything LilyPond-shaped (the row's skyline, the chain's springs, the solved
    /// position) is measured from the BASELINE, and everything band-shaped
    /// (<c>StaffLayout.Y</c>, <c>GetStaffHeight</c>) from the band TOP. Adding it or taking
    /// it off is how one converts, and <c>LayoutEngine.ApplySolvedRowPositions</c> is the
    /// only place that has to.
    /// </remarks>
    internal const double ChordRowTextBaseline = 1.6;

    /// <summary>On a chords-ONLY grid sheet the row is the measure grid itself
    /// ("a staff with the lines removed", full staff-height barlines): centre
    /// the symbol between the bar ends — cap height 1.87 about the band middle
    /// (2.0), baseline ≈ 2.0 + 1.87/2.</summary>
    private const double GridChordBaseline = 2.9;

    /// <summary>
    /// Is this score a chords-ONLY grid sheet — the spelling where the row IS the measure
    /// grid rather than a track above one?
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN, and ONE HOME for it: the rule decides which baseline the symbols are
    /// DRAWN at (<see cref="RowTextBaseline"/>), and since 2026-07-27 it also decides where
    /// the row's REFERENCE POINT is for spacing (<c>MultiStaffLayouter.RefpointBelowTop</c>).
    /// Two spellings of it would put the row's ink 1.300000 away from the space reserved for
    /// it — measured, before this became one function.
    /// <para>
    /// ⚠️ NOTE-BOUND LYRICS DO NOT COUNT: the test is for a lyrics ROW (<c>IsLyricsRow</c>),
    /// so a lead sheet whose words hang off a staff is still "grid".
    /// </para>
    /// </remarks>
    internal static bool IsChordGridSheet(
        ImmutableArray<ChordNameItem> chordNames, ImmutableArray<LyricItem> lyrics)
        => !chordNames.IsDefaultOrEmpty && chordNames.Any(c => c.IsChordRow)
           && (lyrics.IsDefaultOrEmpty || !lyrics.Any(l => l.IsLyricsRow));

    /// <summary>Where an independent chord row's text baseline sits below its band top.</summary>
    internal static double RowTextBaseline(bool chordGridSheet)
        => chordGridSheet ? GridChordBaseline : ChordRowTextBaseline;

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

        // The top staff's chord line is the only one the system up-skyline describes; a
        // lower staff's row reads its OWN up-skyline through lowerStaffUpSkyline. (This
        // comment said "lower-staff chords keep the fixed offset" for longer than that was
        // true, and a 2026-07-30 handoff entry quoted it as evidence that they still do.)
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

            double x = SymbolX(chord, ml, cnMeasures);
            bool topStaff = staffOffset <= (minStaffYAt?.Invoke(chord.MeasureIndex) ?? 0) + 1e-6;
            int sysIdx = measureToSystem.TryGetValue(chord.MeasureIndex, out var si) ? si : -1;

            prepared.Add((chord, x, staffOffset, topStaff, sysIdx, cni));
        }

        // Resolve horizontal overlaps between adjacent TIMING-placed symbols ON THE
        // SAME LINE (same system + staff). Proportional timing X can pack two names —
        // e.g. a chord on a beat that falls inside a longer note — closer than their
        // text boxes; shift the later one right until its box clears the previous
        // one's. Inline @chord symbols (UseTiming false) stay anchored to their note.
        prepared.Sort((a, b) =>
            a.sysIdx != b.sysIdx ? a.sysIdx.CompareTo(b.sysIdx)
            : a.chord.StaffIndex != b.chord.StaffIndex ? a.chord.StaffIndex.CompareTo(b.chord.StaffIndex)
            : a.x.CompareTo(b.x));
        for (int i = 1; i < prepared.Count; i++)
        {
            var prev = prepared[i - 1];
            var cur = prepared[i];
            if (cur.sysIdx != prev.sysIdx || cur.chord.StaffIndex != prev.chord.StaffIndex)
                continue;
            double shifted = ClearOfPrevious(prev.chord, prev.x, cur.chord, cur.x);
            if (shifted != cur.x)
                prepared[i] = (cur.chord, shifted, cur.staffOffset, cur.topStaff, cur.sysIdx, cur.idx);
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
            // The symbol's footprint (see SymbolWidth): the text runs RIGHT from its
            // column. Measured, not guessed — a wide "Gm7♭5" reaches over the NEXT beat's
            // tall chord, which a narrow per-character estimate missed.
            double peak = up.MaxProtrusionInRange(p.x, p.x + SymbolWidth(p.chord));
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
                double rowBaseline = RowTextBaseline(chordGridSheet);
                var (rowText, rowAbove) = DisplayText(p.chord);
                // Store Y-up from the system top (= negation of the system-relative
                // device baseline); no staff offset is baked.
                results.Add(new ChordNameLayout(
                    p.chord.MeasureIndex, p.x, -(p.staffOffset + rowBaseline),
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
            // Store Y-up from the system top (= -y); no staff offset is baked.
            results.Add(new ChordNameLayout(
                p.chord.MeasureIndex, p.x, -y, text, p.chord.SourcePosition, p.idx, AboveLine: above));
        }

        return results.ToImmutable();
    }

    // ===================== ONE SYMBOL: WHERE IT IS AND HOW BIG IT IS =====================
    //
    // Three quantities the engraver used to spell inline and the ROW SKYLINE below now
    // shares: a symbol's X, its width, and the gap that keeps two of them apart. One home
    // each, because the skyline has to describe the symbol that gets DRAWN — a second X
    // model here would be HANDOFF 5.2.1② in the place this island can least afford it.

    /// <summary>
    /// The chord font size — <see cref="EngravingDefaults.ChordNameFontSize"/>, LilyPond's own
    /// <c>ChordName</c> size, shared with the renderer.
    /// </summary>
    /// <remarks>
    /// ⚠️ IT WAS 2.6 AND LILYSHARP-OWN, with LilyPond's rule (<c>font-size = 1.5</c> off the
    /// context's size) quoted beside it — the rule works out to 2.616256, so the constant was
    /// an approximation of the number its own comment named. And it had a SECOND HOME in
    /// <c>SharedRenderer.Marks.DrawChordNames</c> (<c>FontSize * 0.65</c>), the shape
    /// HANDOFF 5.2.1⑤ warns about: this reserved for the face the renderer drew only while
    /// the two happened to agree. One home now, derived rather than approximated.
    /// </remarks>
    private static double ChordFontSize => EngravingDefaults.ChordNameFontSize;

    /// <summary>Minimum ink gap between two adjacent names (staff spaces).</summary>
    /// <remarks>
    /// LILYSHARP-OWN: LilyPond does not shift a ChordName off its column at all — a
    /// ChordName has no X-offset and no self-alignment (scm/define-grobs.scm:837-855), and
    /// two that collide simply collide. This is Lily#'s own overlap resolution for
    /// proportionally-timed symbols; see <see cref="ClearOfPrevious"/>.
    /// </remarks>
    private const double SymbolGap = 0.6;

    /// <summary>
    /// A symbol's X: chordnames entries carry their own rhythm, so they are placed by
    /// musical moment against the shared column grid (the same X the renderer draws a note
    /// at that timing), exactly as bound-voice lyrics are. The note-attached <c>@chord</c>
    /// path keeps the item-index offset instead.
    /// </summary>
    private static double SymbolX(
        ChordNameItem chord, MeasureLayout ml, ImmutableArray<Measure> staffMeasures)
        => chord.UseTiming
            ? ml.X + ml.GetXForTiming(chord.Timing)
            : ml.X + LayoutUtilities.GetItemXOffset(
                staffMeasures, chord.MeasureIndex, chord.ItemIndex, ml);

    /// <summary>
    /// A chord symbol's ink width: its text advance in the SANS face at
    /// <see cref="EngravingDefaults.ChordNameFontSize"/>, REGULAR series
    /// (<see cref="EngravingDefaults.ChordNameFontStyle"/>). The one home — the spacing
    /// rules, the mark collision boxes and this engraver all price the symbol through it.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:837-855 <c>ChordName</c> (the <c>extra-spacing-width</c> block) — font-family sans,
    /// font-size 1.5, NO font-series (regular), and its extent is its stencil's
    /// <c>(0 . w)</c>.
    /// MEASURED (audit/lp-geometry/probes/chord-symbol-width.ly, score CAL): LilyPond's
    /// ChordName exts equal the PLAIN string widths — the Ignatzek markup structure adds
    /// nothing for plain major/minor names — and they are Nimbus Sans REGULAR advances
    /// ("Am" 3.926480 against this face's advance sum 3.924383). Until 2026-07-29 six call
    /// sites measured SansBold at a stale literal 2.6; the
    /// <c>chord.symbol-width.minor-pair-gap</c> point caught the +0.262120 on "Am".
    /// </remarks>
    internal static double SymbolInkWidth(string text) =>
        Rendering.TextFontMetrics.Advance(
            text, EngravingDefaults.ChordNameFontSize,
            sans: true, EngravingDefaults.ChordNameFontStyle);

    /// <summary>
    /// The reserved width of a chord symbol — its ink (<see cref="SymbolInkWidth"/>) under
    /// a floor. The symbol occupies <c>(x . x + width)</c>: LilyPond's ChordName declares
    /// no X-offset and no self-alignment-interface (scm/define-grobs.scm:837-855), so its
    /// reference point is its ink LEFT and it stands ON its column.
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN: the 2.0 floor has no LilyPond source. It is inherited (it was a 1.0
    /// floor on the HALF width before the anchor port doubled the quantity) and it BINDS —
    /// a one-letter symbol like "C" measures 1.888937, so the floor overrides it. LilyPond
    /// has no such floor: a ChordName's extent is its stencil's. It survives here only
    /// because removing it moves output for a reason unrelated to the anchor; it belongs
    /// with the other named inventions in docs/HANDOFF.md section 2H.
    /// </remarks>
    private static double SymbolWidth(ChordNameItem c) =>
        Math.Max(2.0, SymbolInkWidth(DisplayText(c).Text));

    /// <summary>
    /// <paramref name="curX"/> shifted right, if it has to be, so its box clears the
    /// previous symbol's. Both symbols start AT their columns, so only the earlier one's
    /// box lies between them.
    /// </summary>
    private static double ClearOfPrevious(
        ChordNameItem prev, double prevX, ChordNameItem cur, double curX)
    {
        // Inline @chord symbols (UseTiming false) stay anchored to their note.
        if (!cur.UseTiming || !prev.UseTiming)
            return curX;
        double minX = prevX + SymbolWidth(prev) + SymbolGap;
        return curX < minX ? minX : curX;
    }

    /// <summary>
    /// An independent chord ROW's own UP/DOWN skylines for ONE system — the row's real
    /// symbol ink, self-relative to the row's BASELINE.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:948-990 — a ChordNames context is a
    /// non-spaceable line of the alignment, and what the walk above and below it measures is
    /// its VerticalAxisGroup's skyline, i.e. the ChordName stencils themselves.
    /// LILYPOND-REF: lily/axis-group-interface.cc:914-940 <c>skyline_spacing</c> — a
    /// group's skyline is built from its members' stencils.
    /// <para>
    /// ⚠️ THE FRAME IS THE ROW'S TEXT BASELINE, which is where LilyPond's ChordNames
    /// VerticalAxisGroup has its reference point — <see cref="Rendering.TextFontMetrics.Ink"/>
    /// is baseline-relative and nothing is added to it here. Lily#'s <c>StaffLayout.Y</c> for
    /// the same row is the BAND TOP, <see cref="ChordRowTextBaseline"/> above this origin;
    /// that band is Lily#'s own model of where the row sits (HANDOFF 3) and this is the LP
    /// quantity the row is spaced BY. The two are one seam, named here.
    /// </para>
    /// <para>
    /// ⚠️ THE `as both` SECOND LINE IS NOT IN THIS. A Roman degree stacked above the name
    /// (<c>AboveLine</c>) is drawn higher than the ink measured here, so a row that uses it
    /// under-reserves. Named rather than approximated: the stacking distance lives in the
    /// renderer and moving it here without a point to measure it is how a port acquires an
    /// untested branch.
    /// </para>
    /// <para>
    /// ⚠️ BY <c>MeasureIndex</c>, NOT BY POSITION — the caller hands ONE SYSTEM's layouts,
    /// whose positions restart at 0 while a <see cref="ChordNameItem.MeasureIndex"/> is
    /// score-wide. Same trap <c>LyricEngraver.NoteBoundBlockSkylines</c> carries.
    /// </para>
    /// </remarks>
    internal static (VerticalSkyline Up, VerticalSkyline Down) RowSkylines(
        ImmutableArray<ChordNameItem> chordNames,
        ImmutableArray<MeasureLayout> measureLayouts,
        int staffIndex,
        ImmutableArray<Measure> staffMeasures)
    {
        var up = new VerticalSkyline(VerticalDirection.Up);
        var down = new VerticalSkyline(VerticalDirection.Down);
        if (chordNames.IsDefaultOrEmpty || measureLayouts.IsDefaultOrEmpty)
            return (up, down);

        var byMeasure = new Dictionary<int, MeasureLayout>();
        foreach (var ml in measureLayouts)
            byMeasure[ml.MeasureIndex] = ml;

        var placed = new List<(double X, ChordNameItem Chord)>();
        foreach (var chord in chordNames)
        {
            if (!chord.IsChordRow || chord.StaffIndex != staffIndex)
                continue;
            if (!byMeasure.TryGetValue(chord.MeasureIndex, out var ml))
                continue;
            placed.Add((SymbolX(chord, ml, staffMeasures), chord));
        }
        if (placed.Count == 0)
            return (up, down);

        // The same order and the same clearance the drawn row gets — one line, one staff.
        placed.Sort((a, b) => a.X.CompareTo(b.X));
        for (int i = 1; i < placed.Count; i++)
            placed[i] = (ClearOfPrevious(placed[i - 1].Chord, placed[i - 1].X,
                                         placed[i].Chord, placed[i].X), placed[i].Chord);

        foreach (var (x, chord) in placed)
        {
            var (bottom, top) = Rendering.TextFontMetrics.Ink(
                DisplayText(chord).Text, ChordFontSize,
                sans: true, EngravingDefaults.ChordNameFontStyle);
            double right = x + SymbolWidth(chord);
            up.Merge(VerticalSkyline.FromBox(x, right, bottom, top, VerticalDirection.Up));
            down.Merge(VerticalSkyline.FromBox(x, right, bottom, top, VerticalDirection.Down));
        }
        return (up, down);
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
