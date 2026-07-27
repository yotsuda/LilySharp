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
    /// between this system's LAST spaceable staff's reference point and the next one, and the
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
        Func<int, (double Room, double NextStaffMinDistance)?>? looseChainEnd = null,
        Func<int, int, (double Room, VerticalSkyline NextStaffUp)?>? betweenStavesEnd = null,
        double lastSpaceableStaffY = 0)
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
                // Below the system's LAST SPACEABLE staff, which is the one a
                // staff-affinity-UP line is spaced from (page-layout-problem.cc:943-944,
                // :1284-1294). Zero on a one-staff system, so that case is unchanged.
                verseY = lastSpaceableStaffY + staffBottom + BasicDistanceBelowBottomLine
                         + (verseNumber - 1) * _params.VerseSpacing;

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
                noteBoundAnchorY, noteBoundStaffDownSkyline, looseChainEnd, betweenStavesEnd,
                lastSpaceableStaffY);

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
    /// solved where its anchor really is a staff reference point and the room below it is
    /// known — a note-bound block under the LAST spaceable staff of a system, however many
    /// staves that system has. The one-staff case is the regime
    /// <c>lyrics.two-verse.system-gap</c> measures and the multi-staff one is
    /// <c>lyrics.two-staff.two-verse.staff-to-lyric</c>, where the port took the block from
    /// its force-0 5.500000 to its alignment floor 4.009200 against LilyPond's 3.737890 —
    /// the two lyric faces, and nothing else, left over.
    /// <para>
    /// A block BETWEEN two staves of one system (<c>staff … with lyrics</c> on a non-last
    /// group) is solved too, and closes differently: on the next spaceable staff of the same
    /// system through <c>nonstaff-unrelatedstaff-spacing</c> + LARGE_STRETCH (:1299-1312),
    /// with NO null line, its minimum being the alignment's own last step
    /// <c>min_offsets[k-1] - min_offsets[k]</c> (:923-925). The room is the same
    /// refpoint-to-refpoint span (:936-939 again) — <c>LayoutEngine</c> supplies both ends.
    /// The regimes are <c>lyrics.between-staves.*</c>: with one verse the block's floor stays
    /// under the staff spring's ideal and the chain is compressed but not critically
    /// (4.027851), with two the floor rises past it and every spring sits on its minimum
    /// (3.737890 + 2.800000 into 11.073064).
    /// <para>
    /// What still runs at force 0, i.e. exactly where it was: any system carrying an ossia
    /// or a text ROW, which LilyPond puts INTO the chain as loose lines and Lily# lays out
    /// as bands of their own — see <c>LayoutEngine.BuildLooseChainEnds</c> and
    /// <c>BuildBetweenStavesChainEnds</c>, both of which decline those.
    /// </para>
    /// </para>
    /// </remarks>
    private List<LyricLayout> DistributeLooseLines(
        List<LyricLayout> layouts, ImmutableArray<SystemLayout> systems,
        IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)>? systemSkylines,
        double staffBottom,
        IReadOnlyDictionary<int, double>? noteBoundAnchorY,
        Func<int, int, VerticalSkyline?>? noteBoundStaffDownSkyline,
        Func<int, (double Room, double NextStaffMinDistance)?>? looseChainEnd,
        Func<int, int, (double Room, VerticalSkyline NextStaffUp)?>? betweenStavesEnd,
        double lastSpaceableStaffY)
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
                ? groupBottomY : lastSpaceableStaffY;

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

                // ⚠️ THE TWO SKYLINES ARE IN DIFFERENT FRAMES, and this is the conversion to
                // the anchor's — the chain is solved between REFERENCE POINTS, so whatever
                // frame the anchor's skyline is in has to be stepped to that. A per-staff
                // skyline is ALREADY about its staff's reference point
                // (SkylineBuilder.BuildStaffSkylines: the middle line is its origin), so a
                // block between two staves needs NO step at all; a system skyline is measured
                // from the SYSTEM ORIGIN, so the legacy placement steps past every staff above
                // the anchor and then the half-staff to its reference point.
                // Getting this wrong drops the block by exactly the inter-staff distance —
                // measured, 10.500000 on the two-staff probe.
                double skylineToAnchor = isUpperFamily ? 0 : anchorBase + anchorOffset;

                var gaps = new List<LooseLineSpacer.Gap>(verses.Count + 2);

                // ⚠️ ONE RUNNING DOWN-SKYLINE, NOT A PAIR PER GAP, because that is what the
                // alignment is: it walks the group once, and after fixing each distance it
                // RAISES what it has accumulated by that distance and MERGES the line just
                // placed into it (lily/align-interface.cc:272-273). So the profile a line is
                // measured against is everything above it, not just its neighbour — and at
                // an x where the neighbour has no ink, what shows through is whatever is
                // further up.
                //
                // ⚠️ MEASURED, and it is the term two predictions got wrong (audit/lp-geometry,
                // books LYRB/LYRBV): the SAME gap between a verse and the staff under it reads
                // 4.972149 with one verse and 4.535174 with two, because with one verse the
                // staff ABOVE is only 3.737890 up and still binds over the next staff's clef,
                // while with two it is 6.537890 up and the verse's own outline binds instead.
                // A pairwise distance cannot produce two different numbers there.
                //
                // The walk itself is <see cref="AlignmentWalk"/> — the SAME object the
                // reservation reads, which is the whole point of the type existing.
                var walk = new AlignmentWalk();
                walk.Seed(anchorDown);

                // One step of that walk: the distance from what has accumulated to the next
                // line's up-skyline, raised to the spec's minimum-distance, and then the
                // raise and merge that put the accumulation into the next line's frame.
                // ⚠️ THE MINIMUM-DISTANCE IS PASSED, and it has to be even though CreateSpring
                // applies the same member to the same spring a line later. The walk RAISES BY
                // THE CLAMPED dy (align-interface.cc:271-273), so leaving it out puts a
                // different accumulation in front of every later line — the two are only the
                // same number by coincidence, never the same walk. It used to be left out;
                // MEASURED 2026-07-27, passing it moves nothing, which is what makes the
                // reservation and the chain literally one walk rather than two spellings.
                double Advance(int verse, double padding, double minimumDistance)
                {
                    up.TryGetValue((system, verse), out var lineUp);
                    down.TryGetValue((system, verse), out var lineDown);
                    return walk.Advance(lineUp, lineDown, padding, minimumDistance);
                }

                // Staff to verse 1, in the chain's frame — the anchor staff's REFERENCE
                // POINT, which is what skylineToAnchor converts to. nonstaff-relatedstaff-spacing
                // declares no minimum-distance, which read_spacing_spec leaves as no raise.
                gaps.Add(new LooseLineSpacer.Gap(
                    LooseLineSpacer.NonStaffRelatedStaff,
                    Advance(verses[0], SkylineDrop.RelatedStaffPadding,
                            LooseLineSpacer.NonStaffRelatedStaff.MinimumDistance)
                        - skylineToAnchor));

                // Verse to verse, whose spec DOES declare one (2.8).
                for (int i = 1; i < verses.Count; i++)
                {
                    gaps.Add(new LooseLineSpacer.Gap(
                        LooseLineSpacer.NonStaffNonStaff,
                        Advance(verses[i], SkylineDrop.NonStaffNonStaffPadding,
                                LooseLineSpacer.NonStaffNonStaff.MinimumDistance)));
                }

                // What closes the chain, and how much room it has.
                double room = double.PositiveInfinity;
                if (isUpperFamily)
                {
                    // A block between two staves of ONE system. LilyPond closes it on the
                    // next spaceable staff with no null line at all — the minimum is
                    // `min_offsets[k-1] - min_offsets[k]` (page-layout-problem.cc:923-925),
                    // the alignment's own last step, and the room is the same
                    // reference-point-to-reference-point span every other block is solved
                    // into (:936-939). The spring is the line's own
                    // nonstaff-unrelatedstaff-spacing plus LARGE_STRETCH (:1299-1312).
                    var between = betweenStavesEnd?.Invoke(system, familyKey);
                    if (between is { } b)
                    {
                        room = b.Room;

                        // ⚠️ NO FRAME STEP, and it is worth saying why there is none: both
                        // ends of this distance are per-staff skylines about their own
                        // REFERENCE POINTS, which is the frame the chain is solved in, so the
                        // expression is LilyPond's dy unchanged — `down_skyline.distance (up)
                        // + padding` (align-interface.cc:228), i.e. one more step of the SAME
                        // walk. It carried `+ anchorOffset` when this landed, because staff
                        // skylines were then built about the TOP line; the adapter is GONE
                        // WITH THE FRAME rather than moved elsewhere
                        // (SkylineBuilder.BuildStaffSkylines).
                        double closing = walk.Distance(
                            b.NextStaffUp, SkylineDrop.UnrelatedStaffPadding);
                        gaps.Add(new LooseLineSpacer.Gap(
                            LooseLineSpacer.NonStaffUnrelatedStaff, closing));
                    }
                }
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
                // ...and NOTHING when the room is unknown, which is now only the system
                // carrying an ossia or a text ROW (LayoutEngine.BuildLooseChainEnds and
                // BuildBetweenStavesChainEnds both decline those). LilyPond's chain always
                // ends on something — the next staff, or the page edge — so a terminator with
                // no room behind it would be a spring this port invented: it cannot be given
                // LilyPond's minimum, it changes no position (the verses read
                // positions[1..n], which the gaps above already produce), and it would read
                // to the next person as if the chain were complete. The absent end is the
                // honest spelling of "this chain is not solved yet".

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
    /// The note-bound lyric lines that hang below one staff group, as the alignment sees
    /// them: one self-relative up/down skyline pair per verse, in verse order, for the
    /// measures of ONE system.
    /// </summary>
    /// <remarks>
    /// These are the alignment's ELEMENTS between two spaceable staves — what
    /// <c>MultiStaffLayouter</c> has to walk to know how much room the pair must leave.
    /// LILYPOND-REF: lily/page-layout-problem.cc:919-925 and :948-990 — the run of
    /// non-spaceable lines collected between two spaceable ones, whose minimums are the
    /// successive differences of the alignment's translations.
    /// <para>
    /// ⚠️ CALLED BEFORE THE STAVES ARE PLACED, and that is sound rather than lucky: a
    /// syllable's box is built from its X, its width and its text only
    /// (<see cref="SyllableUpBox"/> anchors at y = 0), so nothing here reads a staff Y.
    /// The room it produces then decides those Ys. Feeding it a real baseline would make
    /// the room a function of itself.
    /// </para>
    /// <para>
    /// ⚠️ IT GOES THROUGH <see cref="CalculateSyllableLayout"/> AND
    /// <see cref="ResolveOverlaps"/> rather than re-deriving X, so the ink the room is
    /// measured from is the ink that gets drawn. A second X model here would be
    /// HANDOFF 5.2.1② one more time, in the place that just cost this island a session.
    /// </para>
    /// </remarks>
    internal List<(VerticalSkyline Up, VerticalSkyline Down)> NoteBoundBlockSkylines(
        IReadOnlyList<LyricItem> lyrics, IReadOnlyList<MeasureLayout> measureLayouts,
        int startMeasure, int endMeasure, int firstStaffIndex, int endStaffIndex)
    {
        var result = new List<(VerticalSkyline, VerticalSkyline)>();
        if (lyrics.Count == 0) return result;

        var inBlock = new List<LyricItem>();
        foreach (var l in lyrics)
            if (!l.IsLyricsRow
                && l.StaffIndex >= firstStaffIndex && l.StaffIndex < endStaffIndex
                && l.MeasureIndex >= startMeasure && l.MeasureIndex < endMeasure)
                inBlock.Add(l);
        if (inBlock.Count == 0) return result;

        int maxVerse = 0;
        foreach (var l in inBlock) maxVerse = Math.Max(maxVerse, l.VerseNumber);

        // ⚠️ BY MeasureIndex, NOT BY POSITION. The caller hands ONE SYSTEM's layouts, whose
        // positions restart at 0 while a LyricItem's MeasureIndex is score-wide.
        var byMeasure = new Dictionary<int, MeasureLayout>();
        foreach (var ml in measureLayouts) byMeasure[ml.MeasureIndex] = ml;

        for (int verse = 1; verse <= maxVerse; verse++)
        {
            var laid = new List<LyricLayout>();
            foreach (var l in inBlock)
            {
                if (l.VerseNumber != verse) continue;
                if (!byMeasure.TryGetValue(l.MeasureIndex, out var ml)) continue;
                // Baseline 0: this walk reads X, width and text, never Y.
                var lay = CalculateSyllableLayout(l, ml, 0);
                if (lay != null) laid.Add(lay);
            }
            if (laid.Count == 0) continue;
            laid = ResolveOverlaps(laid);

            var up = new VerticalSkyline(VerticalDirection.Up);
            var down = new VerticalSkyline(VerticalDirection.Down);
            foreach (var lay in laid)
            {
                up.Merge(SyllableUpBox(lay));
                down.Merge(SyllableDownBox(lay));
            }
            result.Add((up, down));
        }
        return result;
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

        return CalculateSyllableLayout(lyric, measureLayouts[lyric.MeasureIndex], y);
    }

    /// <summary>
    /// The same, with the measure already resolved — for callers holding ONE SYSTEM's
    /// layouts, where a global measure index is not a position in the list.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE POSITIONAL OVERLOAD ABOVE IS ONLY CORRECT ON THE SCORE-WIDE LIST, where index
    /// equals <see cref="MeasureLayout.MeasureIndex"/>. Handed a per-system slice it reads
    /// the wrong bar, or returns null for every bar past the first system's count — which is
    /// exactly what it did when <c>NoteBoundBlockSkylines</c> was first wired up: every
    /// system but the first produced an empty block and silently reserved nothing.
    /// </remarks>
    private LyricLayout? CalculateSyllableLayout(
        LyricItem lyric, MeasureLayout measureLayout, double y)
    {
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
