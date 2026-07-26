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
/// Parameters for lyric layout calculation.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:2213-2239 LyricText grob
/// LILYPOND-REF: lily/lyric-engraver.cc:20-30 default parameters
/// </remarks>
internal sealed record LyricParameters
{
    /// <summary>
    /// Staff REFPOINT to lyric baseline for a lyric line attached to the staff above it —
    /// LilyPond's <c>basic-distance</c> for a <c>staff-affinity = UP</c> Lyrics line.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly:648-652 — the Lyrics context sets
    /// <c>VerticalAxisGroup.staff-affinity = #UP</c> and
    /// <c>nonstaff-relatedstaff-spacing = ((basic-distance . 5.5) (padding . 0.5)
    /// (stretchability . 1))</c>. This value is the <c>basic-distance</c> member, taken
    /// from that line of LilyPond's source and not from any measurement of its output.
    /// LILYPOND-REF: lily/page-layout-problem.cc:1284-1294 <c>get_spacing_spec</c> — that is
    /// the spec of the spring between a spaceable staff and the loose line under it, and
    /// <c>distribute_loose_lines</c> (:1025-1054) realizes
    /// <c>max(ideal, ensure_min_distance(alignment minimum))</c>.
    /// <para>
    /// WHERE THE OTHER TWO MEMBERS OF THE SPEC LIVE, so the port is not silently partial:
    /// <c>padding</c> is <see cref="SkylineDrop.RelatedStaffPadding"/>, which is the second
    /// term of that max; <c>stretchability</c> is not modelled at all, and is INERT rather
    /// than skipped — <c>get_spacing_spec</c> hands the springs from a loose line to its
    /// non-own side LARGE_STRETCH 10e5 / HUGE_STRETCH 10e7 (:1257-1338, with LilyPond's own
    /// comment that this is deliberate so an affinity-UP line "will still be placed close to
    /// its staff"), so this spring takes about a part in 10e7 of a page's slack and the
    /// solved length is its rest length either way.
    /// </para>
    /// <para>
    /// That last claim is the one the corpus checks rather than assumes
    /// (audit/lp-geometry, <c>lyrics.{natural,stretched}.staff-to-lyric</c>): LilyPond puts
    /// the row 5.500000 below the staff on a page at rest AND on one stretched to staff gaps
    /// of 43.841185. ⚠️ Those readings are the CHECK on this constant, not its source.
    /// </para>
    /// <para>
    /// ⚠️ Refpoint to refpoint (the middle line), not from the bottom line: the spacing runs
    /// between two VerticalAxisGroup reference points, and a Lyrics group's reference point
    /// is the syllable baseline.
    /// </para>
    /// </remarks>
    public double RelatedStaffBasicDistance { get; init; } = 5.5;

    /// <summary>The outer staff line, staff spaces from the middle line — the step between
    /// the frame <see cref="RelatedStaffBasicDistance"/> is defined in (the staff's own
    /// reference point) and the one the layout code works in (the bottom line).</summary>
    private const double StaffHalf = 2.0;

    /// <summary>
    /// Staff BOTTOM LINE to lyric baseline — <see cref="RelatedStaffBasicDistance"/> in the
    /// frame the callers measure in. Lives here, on the parameters, so the page breaker's
    /// band estimate and the placement cannot drift apart (HANDOFF 5.2.1②).
    /// </summary>
    public double BasicDistanceBelowBottomLine => RelatedStaffBasicDistance - StaffHalf;

    /// <summary>Minimum distance between syllables in staff spaces.</summary>
    public double MinSyllableSpacing { get; init; } = 0.5;

    /// <summary>Font size relative to staff space.</summary>
    public double FontSize { get; init; } = 1.2;

    /// <summary>Hyphen character width estimate (in staff spaces).</summary>
    public double HyphenWidth { get; init; } = 0.4;

    /// <summary>Minimum hyphen length before it's drawn (in staff spaces).</summary>
    public double MinHyphenLength { get; init; } = 0.3;

    /// <summary>Padding between syllable and hyphen (in staff spaces).</summary>
    public double HyphenPadding { get; init; } = 0.2;

    /// <summary>Extender line thickness (in staff spaces).</summary>
    public double ExtenderThickness { get; init; } = 0.04;

    /// <summary>Additional distance between lyric lines for multiple verses.</summary>
    /// <remarks>Baseline-to-baseline step between stacked verses. Must clear a
    /// full line of the 3.2 ss lyric font (ascender 2.11 + descender ~0.9): the
    /// old 1.8 printed verse 2's ascenders through verse 1's descenders.
    /// LILYPOND-REF: lyric lines space by their text extents (VerticalAxisGroup
    /// per LyricLine context).</remarks>
    public double VerseSpacing { get; init; } = 3.2;

    public static LyricParameters Default { get; } = new();
}

/// <summary>
/// Calculates lyric layout positions.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/lyric-engraver.cc:64-175 process_music, stop_translation_timestep
/// LILYPOND-REF: lily/lyric-combine-music-iterator.cc:100-200 note-lyric association
///
/// Lyrics are positioned:
/// - Horizontally: Centered under the associated note
/// - Vertically: Below the staff, with multiple verses stacked
///
/// Hyphens connect syllables of the same word.
/// Extenders indicate melisma (one syllable over multiple notes).
/// </remarks>
internal sealed class LyricEngraver
{
    private readonly LyricParameters _params;

    /// <summary>
    /// The point on a column that a CENTER-aligned grob aligns to, by (measure index, timing)
    /// — LilyPond's <c>he.linear_combination (CENTER)</c>. Supplied by the caller, which is
    /// what holds the music; the default is the placeholder every staff-less column takes.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/self-alignment-interface.cc:121-139 — <c>he</c> is the paper column's
    /// note-column extent, falling back to <c>X-alignment-extent</c> when that is empty.
    /// See <see cref="Svg.Layout.SpacingRules.ParentAlignmentCentresPerColumn"/> for which
    /// grobs are in that extent (MEASURED — heads and rests yes, accidentals and dots no).
    /// </remarks>
    private readonly Func<int, Fraction, double> _parentAlignmentCentre;

    public LyricEngraver(
        LyricParameters? parameters = null,
        Func<int, Fraction, double>? parentAlignmentCentre = null)
    {
        _params = parameters ?? LyricParameters.Default;
        _parentAlignmentCentre = parentAlignmentCentre
            ?? ((_, _) => EngravingDefaults.PaperColumnXAlignmentExtentWidth / 2);
    }

    /// <summary>
    /// The lyric serif font size in staff spaces (SharedRenderer draws lyrics at
    /// FontSize * 0.8 = 4 * 0.8). Kept in sync with EstimateTextWidth, which uses
    /// the same value to turn em-fraction advance widths into staff-space widths.
    /// </summary>
    private const double LyricFontSize = 3.2;

    /// <summary>
    /// Staff bottom line to lyric baseline — see
    /// <see cref="LyricParameters.BasicDistanceBelowBottomLine"/>.
    /// </summary>
    private double BasicDistanceBelowBottomLine => _params.BasicDistanceBelowBottomLine;

    /// <summary>
    /// Serif cap/ascender height as an em fraction — the top of letters like
    /// l, d, k, t, every capital, and digits. Measured from the rendered face
    /// (an 'l' tops 2.11 sp above the baseline at <see cref="LyricFontSize"/>).
    /// </summary>
    private const double AscenderEm = 0.66;

    /// <summary>
    /// Serif x-height as an em fraction — the top of an all-short word like
    /// "up"/"now". Measured at 1.46 sp above the baseline.
    /// </summary>
    private const double XHeightEm = 0.456;

    /// <summary>Lowercase letters whose ink reaches the cap/ascender line.</summary>
    private const string AscenderLetters = "bdfhijklt";

    /// <summary>
    /// CJK (kana, ideographs, Hangul, fullwidth forms) up-extent as an em fraction.
    /// These glyphs fill the em square — their ink top reaches the ideographic ascent,
    /// ABOVE the Latin cap line — so a syllable like き needs more clearance than any
    /// Latin word. Without this it would fall to <see cref="XHeightEm"/> (no upper/
    /// ascender char matches) and overlap notes that dip below the staff.
    /// </summary>
    private const double CjkAscenderEm = 0.88;

    /// <summary>True for a full-em-height script glyph (Hiragana, Katakana, CJK
    /// ideographs incl. Ext. A, Hangul syllables, and fullwidth/halfwidth forms).</summary>
    private static bool IsFullHeightGlyph(char c) =>
        c is (>= '぀' and <= 'ヿ')   // Hiragana + Katakana
          or (>= '㐀' and <= '鿿')   // CJK ideographs (Ext. A + BMP)
          or (>= '가' and <= '힣')   // Hangul syllables
          or (>= '＀' and <= '￯');  // Fullwidth / Halfwidth forms

    /// <summary>
    /// Real up-extent (baseline → top of ink) of a lyric syllable in staff spaces,
    /// from the serif font's metrics at the lyric font size. LilyPond builds the
    /// LyricText skyline from the grob's true stencil bounding box; here the
    /// ascender / x-height em-fractions (measured from the rendered face) stand in
    /// for FreeType glyph metrics — mirroring how EstimateTextWidth derives advance
    /// widths. The extent is the MAX over the syllable's glyphs (a single ascender,
    /// capital, or CJK glyph lifts the whole box), so the up-skyline reflects the
    /// tallest ink the staff's down-skyline must clear.
    /// </summary>
    /// <remarks>
    /// ⚠️ NOT <see cref="Rendering.TextFontMetrics"/>, which reads the bundled face's own
    /// outline and is what this engraver already uses for syllable WIDTHS. Reading heights
    /// from it too was tried and reverted, for a reason worth keeping: TeX Gyre Schola has
    /// NO CJK GLYPHS, so the outline of a kana syllable is empty and its up-extent would
    /// come out ZERO — the case <see cref="CjkAscenderEm"/> exists for. Measured while
    /// trying it: 14 snapshots move and
    /// <c>UpperStaffLyrics_DropByFontHeight_TallCjkClearsFurtherThanLatin</c> fails. So the
    /// two sources are a real duplication (HANDOFF 5.2.1②) and closing it needs a CJK
    /// fallback first, not a one-line substitution.
    /// </remarks>
    private static double LyricUpExtent(string text)
    {
        double em = XHeightEm;
        foreach (char c in text)
        {
            if (IsFullHeightGlyph(c))
                return LyricFontSize * CjkAscenderEm; // the tallest — no glyph exceeds it
            if (char.IsUpper(c) || char.IsDigit(c) || AscenderLetters.IndexOf(c) >= 0)
                em = AscenderEm;
        }
        return LyricFontSize * em;
    }

    /// <summary>
    /// Down-extent (baseline → bottom of ink, POSITIVE for a descender) of a lyric syllable.
    /// </summary>
    /// <remarks>
    /// The second operand of LilyPond's verse-to-verse spacing: the step between two lyric
    /// lines is <c>max(minimum-distance 2.8, this line's descenders to the next line's
    /// ascenders + padding 0.2)</c>, so a line that descends pushes the next one down.
    /// <para>
    /// LATIN COMES FROM THE FACE, not from a table: <see cref="Rendering.TextFontMetrics"/>
    /// reads the bundled outline, which is LilyPond's own way of measuring text
    /// (lily/modified-font-metric.cc:125-143). There is no descender constant here to get
    /// wrong — a syllable with no descender measures 0 and one with a `g` measures the g.
    /// </para>
    /// <para>
    /// LILYSHARP-OWN: the CJK term. The bundled face has no CJK glyphs, so the outline is
    /// empty for them and cannot answer; those glyphs fill the em square, so what is left
    /// below the baseline is the rest of that square — written as
    /// <c>1 - CjkAscenderEm</c> rather than as a number of its own, so it cannot drift away
    /// from the up-extent it is the complement of.
    /// </para>
    /// </remarks>
    private static double LyricDownExtent(string text)
    {
        double outline = -Rendering.TextFontMetrics.Ink(text, LyricFontSize).Bottom;
        foreach (char c in text)
            if (IsFullHeightGlyph(c))
                return Math.Max(outline, LyricFontSize * (1.0 - CjkAscenderEm));
        return outline;
    }

    // LilyPond Lyrics relatedstaff-spacing: the line is lowered so its up-skyline
    // clears the staff down-skyline. The distance→drop math and its padding live in
    // SkylineDrop (shared with figured bass).

    /// <summary>Minimum X-width of a syllable's skyline box (narrow glyphs).</summary>
    private const double MinSyllableBoxWidth = 0.8;


    /// <summary>Baseline of an independent lyrics ROW's verse 1 below the row band's
    /// top, so the text sits inside the reserved band (cf. ChordRow text baseline).</summary>
    /// <remarks>
    /// LILYSHARP-OWN: an independent row is a lead sheet's word TRACK rather than a staff's
    /// lyrics, and it is laid out as one — a staff-like band with its own bar lines, spaced
    /// from its neighbour as a staff group. This is verse 1's baseline inside that band: the
    /// text block (ascender 2.11 + descender 0.9) centred in the 4.0 ss height, so the words
    /// sit where the staff lines would be, "a staff with the lines removed".
    /// <para>
    /// ⚠️ NOT the branch a note-bound lyric line takes. <c>staff mel with lyrics words</c>
    /// carries LilyPond's <see cref="LyricParameters.RelatedStaffBasicDistance"/> and does
    /// not read this at all — MEASURED, by perturbation: moving this constant moves the row
    /// spelling with coefficient 1 and the note-bound spelling by ZERO. The two are 5.600000
    /// apart on the same music, which is a DECIDED divergence from LilyPond (HANDOFF 3) and
    /// is asserted by <c>LyricRowIsSpacedAsAStaffLikeBand</c>, not by a ledger point.
    /// </para>
    /// </remarks>
    private const double LyricRowBaseline = 2.6;

    /// <summary>
    /// Calculate layouts for all lyrics in a score.
    /// </summary>
    /// <param name="lyrics">Collection of lyric items.</param>
    /// <param name="measureLayouts">Measure layout information for note positions.</param>
    /// <param name="staffBottom">Y position of the bottom staff line (in staff spaces).</param>
    /// <param name="systems">Systems, to map a lyric to its system's down-skyline.</param>
    /// <param name="systemSkylines">
    /// Per-system up/down skylines (1:1 with <paramref name="systems"/>). When given,
    /// each system's lyric line is lowered so the TEXT clears notes/ledger lines that
    /// poke below the staff — LilyPond places the Lyrics line at
    /// max(basic-distance, down-skyline + lyric-extent), the staff-affinity-UP
    /// VerticalAxisGroup spacing (engraver-init.ly:648-652).
    /// </param>
    /// <param name="staffYByIndex">Optional per-staff-index Y positions (staff spaces) for
    /// placing lyric rows in a multi-staff score; null falls back to <paramref name="staffBottom"/>.</param>
    /// <param name="looseChainEnd">
    /// Per system, what closes its lyric block's spring chain: the ROOM the page left
    /// between this system's staff reference point and the next spaceable one, and the
    /// minimum distance from the null line that breaks the affinity to that next staff
    /// (NaN when the chain runs to the page edge instead, LilyPond's
    /// <c>-page_height_</c> call at page-layout-problem.cc:1012-1013). Null — or a null
    /// result for a system — leaves that chain at force 0, i.e. every spring at
    /// <c>max(min, ideal)</c>, which is where a chain with room to spare lands anyway.
    /// See <see cref="DistributeLooseLines"/> for which chains are supplied today.
    /// </param>
    public ImmutableArray<LyricLayout> CalculateLayouts(
        IReadOnlyList<LyricItem> lyrics,
        IReadOnlyList<MeasureLayout> measureLayouts,
        double staffBottom,
        ImmutableArray<SystemLayout> systems = default,
        IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)>? systemSkylines = null,
        IReadOnlyDictionary<int, double>? staffYByIndex = null,
        IReadOnlyDictionary<int, double>? noteBoundAnchorY = null,
        Func<int, int, VerticalSkyline?>? noteBoundStaffDownSkyline = null,
        Func<int, (double Room, double NextStaffMinDistance)?>? looseChainEnd = null)
    {
        if (lyrics.Count == 0)
            return ImmutableArray<LyricLayout>.Empty;

        var layouts = new List<LyricLayout>();

        // Note-bound lyrics attached to a staff in a NON-LAST staff group sit directly
        // below that GROUP (the anchor Y is the group's bottom staff), in the inter-group
        // gap — so `staff melody with lyrics …` / `staff back` puts the words between the
        // two staves. `noteBoundAnchorY` holds those staves (present ⇒ this placement).
        // A staff in the LAST group is absent, so its lyrics keep the legacy "below the
        // whole system" placement AND its skyline drop — this deliberately includes a
        // grand staff's top staff (the last group), so an SATB chorale's lyrics still sit
        // below the whole grand staff. LILYPOND-REF: a Lyrics context lives at a fixed
        // position under its associated Staff in the vertical hierarchy.
        bool IsUpperNoteBound(LyricItem l) =>
            !l.IsLyricsRow && noteBoundAnchorY != null && noteBoundAnchorY.ContainsKey(l.StaffIndex);

        // Group by (anchor staff, kind, verse): a lyrics ROW and an upper note-bound
        // line each anchor to their own staff index; the bottom-staff/single-staff
        // note-bound lyrics lump at -1 (legacy placement). Verses stack within each.
        // F3/B: carry each syllable's ORIGINAL index in `lyrics` (== score.Lyrics order)
        // through the grouping so the emitted layout can re-derive its data-pos later.
        var verseGroups = lyrics
            .Select((l, i) => (Lyric: l, Index: i))
            .GroupBy(x => (
                Row: x.Lyric.IsLyricsRow || IsUpperNoteBound(x.Lyric) ? x.Lyric.StaffIndex : -1,
                IsRow: x.Lyric.IsLyricsRow,
                Verse: x.Lyric.VerseNumber))
            .OrderBy(g => g.Key.Row).ThenBy(g => g.Key.Verse);

        foreach (var verseGroup in verseGroups)
        {
            int verseNumber = verseGroup.Key.Verse;
            int rowKey = verseGroup.Key.Row;
            bool isRow = verseGroup.Key.IsRow;
            var verseLyrics = verseGroup.ToList();

            // Calculate Y position for this verse
            // LILYPOND-REF: lily/lyric-engraver.cc:85-95 vertical positioning
            double verseY;
            if (isRow && rowKey >= 0 && staffYByIndex != null && staffYByIndex.TryGetValue(rowKey, out var rowAnchor))
                // An independent row sits IN its own band at that staff's Y.
                verseY = rowAnchor + LyricRowBaseline + (verseNumber - 1) * _params.VerseSpacing;
            else if (!isRow && rowKey >= 0 && noteBoundAnchorY != null
                     && noteBoundAnchorY.TryGetValue(rowKey, out var groupBottomY))
                // A non-last-group note-bound line sits just BELOW that group's bottom
                // staff. This is the BASIC floor; ApplySkylineDrop then lowers it — using
                // the attached staff's own down-skyline — so it clears that staff's notes
                // and the line's real (font-metric) glyph height.
                verseY = groupBottomY + staffBottom + BasicDistanceBelowBottomLine + (verseNumber - 1) * _params.VerseSpacing;
            else
                verseY = staffBottom + BasicDistanceBelowBottomLine + (verseNumber - 1) * _params.VerseSpacing;

            var verseLayouts = new List<LyricLayout>();
            for (int i = 0; i < verseLyrics.Count; i++)
            {
                var (lyric, srcIndex) = verseLyrics[i];
                var layout = CalculateSyllableLayout(
                    lyric,
                    measureLayouts,
                    verseY);

                if (layout != null)
                    verseLayouts.Add(layout with { SourceIndex = srcIndex });
            }

            // Apply collision avoidance for this verse
            // LILYPOND-REF: lily/lyric-engraver.cc:120-140 collision handling
            verseLayouts = ResolveOverlaps(verseLayouts);
            layouts.AddRange(verseLayouts);
        }

        // ...and then hand the whole block to the loose-line spacer, which places every
        // line of it at once: the drop that clears the staff's notes and the step from one
        // verse to the next are two springs of ONE chain, solved into the room the page
        // left between this staff and the next spaceable one.
        if (!systems.IsDefaultOrEmpty)
            layouts = DistributeLooseLines(layouts, systems, systemSkylines, staffBottom,
                noteBoundAnchorY, noteBoundStaffDownSkyline, looseChainEnd);

        return layouts.ToImmutableArray();
    }

    /// <summary>
    /// Places every line of a note-bound lyric block at once, as LilyPond's second
    /// spacing pass does: one spring chain from the staff down through the verses to
    /// whatever closes it, solved into the room the page left.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:919-942 collects the loose lines between
    /// two spaceable ones and :1025-1054 <c>distribute_loose_lines</c> solves them. The
    /// springs come from <see cref="LooseLineSpacer"/>; the minimums are this engraver's,
    /// because they are made of the syllables' own ink.
    /// <para>
    /// THIS REPLACED TWO INDEPENDENT PASSES (a skyline "drop" of the whole line, then a
    /// re-stack of verses 2..n) and the replacement is exact at force 0, which is what
    /// makes the change reviewable: <c>Spring::length (0)</c> is
    /// <c>max (min_distance, ideal)</c>, so spring 0 gives back
    /// <c>max (basic-distance, staff-down.distance (verse-1-up) + padding)</c> — the old
    /// drop — and each verse spring gives back <c>max (2.8, ink + 0.2)</c> — the old step.
    /// EVERYTHING the chain changes comes from the force being something other than 0.
    /// </para>
    /// <para>
    /// ⚠️ WHICH CHAINS ARE SOLVED, named rather than left to be discovered. A chain is
    /// solved only where its anchor really is a staff reference point and the room below
    /// it is known — today that is a note-bound block under a SINGLE-staff system, which
    /// is the regime <c>lyrics.two-verse.system-gap</c> measures. The other two run at
    /// force 0, i.e. exactly where they were:
    /// <list type="bullet">
    /// <item>A block under a MULTI-staff system is anchored at
    /// <paramref name="staffBottom"/> below the SYSTEM ORIGIN, not at the last spaceable
    /// staff LilyPond anchors it to (<c>last_spaceable_line</c>, :943-944) — so its
    /// basic-distance is measured from the wrong staff and only the skyline minimum ever
    /// binds. Solving a chain whose anchor is wrong would move the lines by that error.
    /// That anchor is a separate defect and wants its own ledger point.</item>
    /// <item>A block between two staves of one system (<c>staff … with lyrics</c> on a
    /// non-last group) closes on the next staff through
    /// <c>nonstaff-unrelatedstaff-spacing</c> + LARGE_STRETCH (:1301-1312), whose minimum
    /// is that staff's up-skyline against the last verse's descenders — an input this
    /// engraver is not given.</item>
    /// </list>
    /// </para>
    /// </remarks>
    private List<LyricLayout> DistributeLooseLines(
        List<LyricLayout> layouts, ImmutableArray<SystemLayout> systems,
        IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)>? systemSkylines,
        double staffBottom,
        IReadOnlyDictionary<int, double>? noteBoundAnchorY,
        Func<int, int, VerticalSkyline?>? noteBoundStaffDownSkyline,
        Func<int, (double Room, double NextStaffMinDistance)?>? looseChainEnd)
    {
        var measureToSystem = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);

        // A non-last-group note-bound line is anchored below its OWN group; everything
        // else note-bound shares the legacy placement below the whole system.
        bool IsUpper(LyricItem l) =>
            !l.IsLyricsRow && noteBoundAnchorY != null && noteBoundAnchorY.ContainsKey(l.StaffIndex);

        // From a family's anchor BASE down to the anchor staff's reference point — the
        // frame the chain is solved in. Derived from the two distances that already
        // exist rather than restating the half-staff, so it cannot drift from them.
        double anchorOffset =
            staffBottom + BasicDistanceBelowBottomLine - _params.RelatedStaffBasicDistance;

        var newY = new Dictionary<(int Family, int System, int Verse), double>();

        foreach (var family in layouts.Where(l => !l.Item.IsLyricsRow)
                                      .GroupBy(l => IsUpper(l.Item) ? l.Item.StaffIndex : -1))
        {
            int familyKey = family.Key;
            bool isUpperFamily = familyKey >= 0;
            double anchorBase = isUpperFamily && noteBoundAnchorY != null
                                && noteBoundAnchorY.TryGetValue(familyKey, out var groupBottomY)
                ? groupBottomY : 0;

            var up = BuildVerseUpSkylines(family, measureToSystem);
            var down = BuildVerseDownSkylines(family, measureToSystem);

            foreach (int system in family.Select(l => l.Item.MeasureIndex)
                                         .Where(measureToSystem.ContainsKey)
                                         .Select(m => measureToSystem[m])
                                         .Distinct())
            {
                var verses = family
                    .Where(l => measureToSystem.TryGetValue(l.Item.MeasureIndex, out int s) && s == system)
                    .Select(l => l.Item.VerseNumber).Distinct().OrderBy(v => v).ToList();
                if (verses.Count == 0) continue;

                // The staff this block hangs from: the whole system's silhouette for the
                // legacy placement, that staff's own for a block between two staves.
                var anchorDown = isUpperFamily
                    ? noteBoundStaffDownSkyline?.Invoke(system, familyKey)
                    : systemSkylines != null && system < systemSkylines.Count
                        ? systemSkylines[system].down : null;

                var gaps = new List<LooseLineSpacer.Gap>(verses.Count + 2);

                // Staff to verse 1. LILYPOND-REF: lily/align-interface.cc:227-238 — the
                // alignment minimum is the two skylines' distance plus the spec's padding.
                double staffToFirst = 0;
                if (anchorDown is { IsEmpty: false } ad
                    && up.TryGetValue((system, verses[0]), out var firstUp) && !firstUp.IsEmpty)
                {
                    double dist = ad.Distance(firstUp, SkylineDrop.HorizonPadding);
                    if (!double.IsInfinity(dist) && !double.IsNaN(dist))
                        staffToFirst = dist + SkylineDrop.RelatedStaffPadding - anchorOffset;
                }
                gaps.Add(new LooseLineSpacer.Gap(LooseLineSpacer.NonStaffRelatedStaff, staffToFirst));

                // Verse to verse — the upper line's descenders against the lower one's
                // ascenders. CreateSpring raises this to the spec's 2.8 when it is shorter.
                for (int i = 1; i < verses.Count; i++)
                {
                    double step = 0;
                    if (down.TryGetValue((system, verses[i - 1]), out var d)
                        && up.TryGetValue((system, verses[i]), out var u)
                        && !d.IsEmpty && !u.IsEmpty)
                    {
                        double dist = d.Distance(u, SkylineDrop.HorizonPadding);
                        if (!double.IsInfinity(dist) && !double.IsNaN(dist))
                            step = dist + SkylineDrop.NonStaffNonStaffPadding;
                    }
                    gaps.Add(new LooseLineSpacer.Gap(LooseLineSpacer.NonStaffNonStaff, step));
                }

                // What closes the chain, and how much room it has.
                double room = double.PositiveInfinity;
                var end = isUpperFamily ? null : looseChainEnd?.Invoke(system);
                if (end is { } chainEnd)
                {
                    room = chainEnd.Room;
                    if (double.IsNaN(chainEnd.NextStaffMinDistance))
                    {
                        // LILYPOND-REF: lily/page-layout-problem.cc:1004-1013 — the last
                        // block on a page runs to the page edge, floored by the last
                        // line's own descent (Lily# has no footer).
                        double descent = 0;
                        foreach (var lay in family)
                            if (lay.Item.VerseNumber == verses[^1]
                                && measureToSystem.TryGetValue(lay.Item.MeasureIndex, out int s) && s == system)
                                descent = Math.Max(descent, LyricDownExtent(lay.Item.Text));
                        gaps.Add(new LooseLineSpacer.Gap(LooseLineSpacer.NullNeighbour, descent));
                    }
                    else
                    {
                        // LILYPOND-REF: lily/page-layout-problem.cc:928-933 — a NULL line
                        // breaks the affinity to the previous system (minimum 0.0), and
                        // the gap after it reaches the next system's first staff.
                        gaps.Add(new LooseLineSpacer.Gap(LooseLineSpacer.NullNeighbour, 0.0));
                        gaps.Add(new LooseLineSpacer.Gap(
                            LooseLineSpacer.NullNeighbour, chainEnd.NextStaffMinDistance));
                    }
                }
                else
                {
                    gaps.Add(new LooseLineSpacer.Gap(LooseLineSpacer.NullNeighbour, 0.0));
                }

                var positions = LooseLineSpacer.Distribute(gaps, room);
                for (int i = 0; i < verses.Count; i++)
                {
                    // Y-up: a line below the anchor is negative.
                    newY[(familyKey, system, verses[i])] =
                        -(anchorBase + anchorOffset + positions[i + 1]);
                }
            }
        }

        if (newY.Count == 0) return layouts;

        var placed = new List<LyricLayout>(layouts.Count);
        foreach (var lay in layouts)
        {
            if (!lay.Item.IsLyricsRow
                && measureToSystem.TryGetValue(lay.Item.MeasureIndex, out int s)
                && newY.TryGetValue(
                    (IsUpper(lay.Item) ? lay.Item.StaffIndex : -1, s, lay.Item.VerseNumber),
                    out double y))
            {
                placed.Add(lay with { YUp = y });
            }
            else placed.Add(lay);
        }
        return placed;
    }

    /// <summary>
    /// The UP-skyline box of one syllable, self-relative to its own baseline: anchor at
    /// y = 0, ink top at <see cref="LyricUpExtent"/> above it in the Y-up frame.
    /// </summary>
    /// <remarks>
    /// A font-metric height, so the clearance it produces reflects a tall CJK glyph as well
    /// as a low note. LilyPond builds the LyricText skyline from the grob's true stencil
    /// bounding box; the em-fraction extents stand in for that here.
    /// </remarks>
    internal static VerticalSkyline SyllableUpBox(LyricLayout lay)
    {
        double halfW = Math.Max(lay.Width, MinSyllableBoxWidth) / 2.0;
        return VerticalSkyline.FromBox(
            lay.X - halfW, lay.X + halfW, 0, LyricUpExtent(lay.Item.Text), VerticalDirection.Up);
    }

    /// <summary>
    /// One merged UP-skyline per (system, VERSE) — each verse's own ink, in its own frame.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1315-1332 <c>get_spacing_spec</c> — a
    /// second lyric line under the first is spaced from it by the UPPER line's
    /// <c>nonstaff-nonstaff-spacing</c> (ly/engraver-init.ly:653-656:
    /// <c>((basic-distance . 0) (minimum-distance . 2.8) (padding . 0.2))</c>), and a zero
    /// ideal under a minimum means the realized step is
    /// <c>max(2.8, the two lines' own ink + 0.2)</c> — LilyPond's verse spacing RESPONDS TO
    /// THE TEXT. Measuring that needs each verse's ink separately, which is what this
    /// returns; the flat <see cref="LyricParameters.VerseSpacing"/> cannot express it.
    /// <para>
    /// Every verse is read now: <see cref="DistributeLooseLines"/> takes verse k's up-skyline
    /// against verse k-1's <see cref="BuildVerseDownSkylines"/> for that spring's minimum,
    /// and verse 1's against the staff for the spring above it.
    /// </para>
    /// </remarks>
    internal static Dictionary<(int System, int Verse), VerticalSkyline> BuildVerseUpSkylines(
        IEnumerable<LyricLayout> layouts, IReadOnlyDictionary<int, int> measureToSystem)
    {
        var result = new Dictionary<(int System, int Verse), VerticalSkyline>();
        foreach (var lay in layouts)
        {
            if (!measureToSystem.TryGetValue(lay.Item.MeasureIndex, out int s)) continue;
            var key = (System: s, Verse: lay.Item.VerseNumber);
            var box = SyllableUpBox(lay);
            if (result.TryGetValue(key, out var sky)) sky.Merge(box);
            else result[key] = box;
        }
        return result;
    }

    /// <summary>
    /// The DOWN-skyline box of one syllable, self-relative to its own baseline: anchor at
    /// y = 0, ink bottom at <see cref="LyricDownExtent"/> below it.
    /// </summary>
    internal static VerticalSkyline SyllableDownBox(LyricLayout lay)
    {
        double halfW = Math.Max(lay.Width, MinSyllableBoxWidth) / 2.0;
        return VerticalSkyline.FromBox(
            lay.X - halfW, lay.X + halfW, -LyricDownExtent(lay.Item.Text), 0,
            VerticalDirection.Down);
    }

    /// <summary>
    /// One merged DOWN-skyline per (system, VERSE) — the descenders the verse below has to
    /// clear. The mirror of <see cref="BuildVerseUpSkylines"/>.
    /// </summary>
    internal static Dictionary<(int System, int Verse), VerticalSkyline> BuildVerseDownSkylines(
        IEnumerable<LyricLayout> layouts, IReadOnlyDictionary<int, int> measureToSystem)
    {
        var result = new Dictionary<(int System, int Verse), VerticalSkyline>();
        foreach (var lay in layouts)
        {
            if (!measureToSystem.TryGetValue(lay.Item.MeasureIndex, out int s)) continue;
            var key = (System: s, Verse: lay.Item.VerseNumber);
            var box = SyllableDownBox(lay);
            if (result.TryGetValue(key, out var sky)) sky.Merge(box);
            else result[key] = box;
        }
        return result;
    }

    /// <summary>
    /// An upper BOUND on the verse-to-verse step for a set of syllables — the estimate the
    /// page breaker prices a band against, where <see cref="DistributeLooseLines"/>'s
    /// per-system skylines are not available yet.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1315-1332 + ly/engraver-init.ly:653-656 —
    /// the <c>max(minimum-distance 2.8, ink + padding 0.2)</c> the placement solves.
    /// <para>
    /// LILYSHARP-OWN: the BOUNDING, not the rule. Taking the deepest descender of the verses
    /// above against the tallest ascender of the verses below — as though every syllable
    /// stood over every other one — is not a line of LilyPond. LilyPond's estimate path asks
    /// the same specs through <c>get_maybe_pure_property</c> and
    /// <c>Align_interface</c>'s pure branch; this is a cruder over-estimate that happens to
    /// bound it. It can only OVER-reserve, never under, which is the direction an estimate
    /// has to err in, and it exists so the breaker cannot drift from the placement again: a
    /// flat constant here read 1.8 against the placement's 3.2 for as long as both were
    /// constants.
    /// </para>
    /// </remarks>
    internal static double VerseStepBound(
        IEnumerable<string> upperTexts, IEnumerable<string> lowerTexts)
    {
        double down = 0, up = 0;
        foreach (var t in upperTexts) down = Math.Max(down, LyricDownExtent(t));
        foreach (var t in lowerTexts) up = Math.Max(up, LyricUpExtent(t));
        return Math.Max(SkylineDrop.NonStaffNonStaffMinimum,
                        down + up + SkylineDrop.NonStaffNonStaffPadding);
    }

    /// <summary>
    /// The band a lyric block occupies below the staff's BOTTOM LINE when its lines are at
    /// their ALIGNMENT MINIMUM — LilyPond's <c>minimum_offsets_with_min_dist</c>, which is
    /// what the system skyline reserves, as opposed to the distance the lines are drawn at.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:593-599 — <c>build_system_skyline</c> is
    /// handed <c>Align_interface::get_minimum_translations</c>, so a loose line IS in the
    /// system's skyline, but at its minimum.
    /// LILYPOND-REF: lily/align-interface.cc:227-238 — that minimum is
    /// <c>down_skyline.distance(up) + padding</c>, raised by the spec's
    /// <c>minimum-distance</c>. ⚠️ The spec's BASIC-DISTANCE is added only behind
    /// <c>INT_MAX == end &amp;&amp; 0 == start</c>, the PURE estimate branch, and the call
    /// that feeds the skyline passes <c>start = end = 0</c> — so the 5.5 this engraver draws
    /// at is NOT in the reservation. The drawn distance arrives afterwards, out of
    /// <c>distribute_loose_lines</c>.
    /// <para>
    /// This is what <c>LayoutEngine.EstimateLooseLineExtents</c> reserves, and it landed in
    /// the same commit as <see cref="DistributeLooseLines"/> because neither half works
    /// alone: reserving the minimum without placing the lines inside the resulting gap
    /// drops them onto the staff below, and placing them without shrinking the reservation
    /// leaves the chain room it should not have, so it never compresses.
    /// </para>
    /// <para>
    /// The staff's own down-ink is taken as the bottom line, which is what an estimate can
    /// know before the skylines exist; the real pass uses the staff's down-skyline and can
    /// only come out larger, never smaller.
    /// </para>
    /// </remarks>
    internal static double AlignmentMinimumBand(IReadOnlyList<(string Text, int Verse)> block)
    {
        if (block.Count == 0) return 0;
        int maxVerse = block.Max(b => b.Verse);

        // Verse 1's own minimum, measured from the bottom line: padding + its ink.
        double up = 0;
        foreach (var (text, verse) in block)
            if (verse == 1) up = Math.Max(up, LyricUpExtent(text));
        double band = SkylineDrop.RelatedStaffPadding + up;

        // ...then each further verse's step, at ITS minimum, and the last one's descender.
        for (int v = 2; v <= maxVerse; v++)
        {
            band += VerseStepBound(
                block.Where(b => b.Verse == v - 1).Select(b => b.Text),
                block.Where(b => b.Verse == v).Select(b => b.Text));
        }
        double down = 0;
        foreach (var (text, verse) in block)
            if (verse == maxVerse) down = Math.Max(down, LyricDownExtent(text));
        return band + down;
    }

    /// <summary>One verse's skylines, keyed by system — the shape SkylineDrop consumes.</summary>
    private static Dictionary<int, VerticalSkyline> VerseSkylines(
        Dictionary<(int System, int Verse), VerticalSkyline> byVerse, int verse)
    {
        var result = new Dictionary<int, VerticalSkyline>();
        foreach (var ((system, v), sky) in byVerse)
            if (v == verse)
                result[system] = sky;
        return result;
    }

    /// <summary>
    /// Resolves overlapping syllables by shifting them apart.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/lyric-engraver.cc:150-180 horizontal spacing
    ///
    /// Strategy: Limit shifts to prevent lyrics from drifting too far from their notes.
    /// If a large shift would be needed, reduce the effective width estimate instead.
    /// </remarks>
    private List<LyricLayout> ResolveOverlaps(List<LyricLayout> layouts)
    {
        if (layouts.Count < 2)
            return layouts;

        // Maximum shift allowed (prevents lyrics from drifting too far from notes)
        const double maxShift = 2.0;

        var result = new List<LyricLayout>(layouts.Count);

        for (int i = 0; i < layouts.Count; i++)
        {
            var current = layouts[i];

            if (i == 0)
            {
                result.Add(current);
                continue;
            }

            var previous = result[i - 1];

            // A new SYSTEM starts here (X rewinds to the left margin): the
            // previous syllable belongs to the prior line and cannot collide —
            // treating the rewind as an overlap used to shove the new line's
            // first syllable right by maxShift, into its neighbour.
            if (current.X < previous.X)
            {
                result.Add(current);
                continue;
            }

            // Use reduced width for collision detection (allows some overlap)
            // This keeps lyrics closer to their notes while still readable
            double effectiveWidth = 0.6; // Use a smaller effective width for collision

            double prevRight = previous.X + effectiveWidth;
            double currLeft = current.X - effectiveWidth;
            double gap = currLeft - prevRight;

            // If there's not enough gap, shift current syllable to the right
            if (gap < _params.MinSyllableSpacing)
            {
                double neededShift = _params.MinSyllableSpacing - gap;
                double shift = Math.Min(neededShift, maxShift);
                current = current with { X = current.X + shift };
            }

            result.Add(current);
        }

        return result;
    }

    /// <summary>
    /// Calculate layout for a single syllable.
    /// </summary>
    private LyricLayout? CalculateSyllableLayout(
        LyricItem lyric,
        IReadOnlyList<MeasureLayout> measureLayouts,
        double y)
    {
        // Find the note position for this syllable
        if (lyric.MeasureIndex < 0 || lyric.MeasureIndex >= measureLayouts.Count)
            return null;

        var measureLayout = measureLayouts[lyric.MeasureIndex];

        // Get X position from the associated note's musical MOMENT against the shared
        // column grid — the same X the renderer draws that timing at.
        // LILYPOND-REF: lily/lyric-engraver.cc:100-110 horizontal alignment
        //
        // Resolving by timing (not by the item's slot X) is essential when a measure
        // opens with a non-note item — a mid-piece `time`/`clef` change: its ItemLayout.X
        // does not track the note column grid, so a slot-index lookup compressed every
        // syllable of that bar into a cluster. In a plain measure the two agree exactly.
        double noteX = measureLayout.X + measureLayout.GetXForTiming(lyric.Timing);

        double textWidth = EstimateTextWidth(lyric.Text);

        // Centre the syllable on the column's ALIGNMENT EXTENT, which is not the column.
        // Hyphen dashes and extender lines are LyricHyphen's job (the LP grobs:
        // lyric-hyphen.cc / lyric-extender.cc); this engraver used to ALSO emit a "-" text
        // and its own extender line, double-drawing every connector.
        //
        // LILYPOND-REF: lily/self-alignment-interface.cc:117-176 aligned_on_parent —
        //   x = -ext.linear_combination (self_align) + he.linear_combination (par_align)
        // with self_align = CENTER (LyricText's `left-align-at-split-notes` returns CENTER
        // unless a Completion_heads_engraver split the head, scm/output-lib.scm:1642-1673)
        // and par_align = () copying self (:156-157). So the syllable's INK CENTRE lands on
        // `column + he.centre`: the -w/2 that centres it and the +w/2 back to the centre
        // cancel, which is why this needs no text width at all.
        // `he` is the column's note-column extent, or the (0 . 1.35) placeholder when the
        // column carries no rhythmic grob — SpacingRules.ParentAlignmentCentresPerColumn.
        // MEASURED (audit/lp-geometry/probes/staffless-system.ly): 0.675000 with no note
        // head (CLI/CLA), 0.688700 over a 1.377400 head (LSH), 0.750000 over a half rest
        // (LSR); ledger lyric.syllable-centre.{placeholder-column,note-column}.
        // ⚠️ Lily# used to draw the syllable centred on the column itself, i.e. with this
        // term missing entirely — both regimes, not one branch of them.
        double syllableX = noteX + _parentAlignmentCentre(lyric.MeasureIndex, lyric.Timing);

        // y is the verse baseline in system-relative device (down+ from the system
        // top); store it Y-up from the system top (= its negation), offset-free.
        return new LyricLayout(
            lyric,
            syllableX,
            -y,
            textWidth);
    }

    /// <summary>
    /// Estimate text width in staff spaces using serif font character width ratios.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/font-metric.cc:100-120 text extent calculation
    ///
    /// The SVG renderer uses font-size = 4 * 0.8 = 3.2 staff spaces.
    /// Character widths are approximated using Times New Roman proportions (em fractions).
    /// Width classes are grouped by similar advance widths in standard serif fonts.
    /// </remarks>
    private double EstimateTextWidth(string text)
        => Rendering.TextFontMetrics.Serif(text, LyricFontSize);
}
