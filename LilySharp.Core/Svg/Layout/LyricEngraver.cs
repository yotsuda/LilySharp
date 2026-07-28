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

    /// <summary>
    /// <c>system-system-spacing</c>'s padding — the term that rides on the minimum of the
    /// spring reaching the NEXT system's first line.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:971-972 — <c>elements_[i].min_distance +
    /// elements_[i].padding</c>, the system spring's own padding, on the loose line that
    /// opens the next system.
    /// </remarks>
    private readonly double _systemPadding;

    /// <summary>
    /// WHERE THE SOLVE PUT EACH LOOSE LINE THAT IS A TEXT ROW, by (system, global staff
    /// index), as a baseline in PAGE Y-up — published for <c>LayoutEngine</c> to apply.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1046-1053 — <c>distribute_loose_lines</c>
    /// ends by <c>translate_axis</c>-ing each loose line to the solved position, i.e. the row
    /// is MOVED after the fact rather than laid out there. Lily# has to do the same and for a
    /// sharper reason: the solve happens inside the annotation pass, while the row's Y is
    /// read both there (<c>ChordNameEngraver</c> through <c>staffYAt</c>) and after it (the
    /// renderer's grid barlines, off <c>systemsArray</c>). Publishing the answer and letting
    /// the engine apply it keeps ONE order of operations instead of two.
    /// </remarks>
    public Dictionary<(int System, int StaffIndex), double> SolvedRowBaselines { get; } = new();

    public LyricEngraver(
        LyricParameters? parameters = null,
        Func<int, Fraction, double>? parentAlignmentCentre = null,
        double? systemPadding = null)
    {
        _params = parameters ?? LyricParameters.Default;
        _parentAlignmentCentre = parentAlignmentCentre
            ?? ((_, _) => EngravingDefaults.PaperColumnXAlignmentExtentWidth / 2);
        _systemPadding = systemPadding ?? VerticalSpacingParameters.Default.SystemSystem.Padding;
    }

    /// <summary>
    /// The lyric serif em size in staff spaces — <see cref="EngravingDefaults.LyricTextFontSize"/>,
    /// which is LilyPond's own <c>LyricText</c> size and is shared with the renderer so the
    /// reserved ink and the drawn ink cannot drift apart.
    /// </summary>
    private static double LyricFontSize => EngravingDefaults.LyricTextFontSize;

    /// <summary>
    /// Staff bottom line to lyric baseline — see
    /// <see cref="LyricParameters.BasicDistanceBelowBottomLine"/>.
    /// </summary>
    private double BasicDistanceBelowBottomLine => _params.BasicDistanceBelowBottomLine;

    /// <summary>
    /// CJK (kana, ideographs, Hangul, fullwidth forms) up-extent as an em fraction.
    /// These glyphs fill the em square — their ink top reaches the ideographic ascent,
    /// ABOVE the Latin cap line — so a syllable like き needs more clearance than any
    /// Latin word.
    /// </summary>
    /// <remarks>
    /// ⚠️ LILYSHARP-OWN, AND IT IS LOAD BEARING NOW THAT NOTHING ELSE IS. LilyPond has no
    /// counterpart because it never needs one: it measures whatever face the context names,
    /// and a CJK score there uses a CJK face. Lily# ships TeX Gyre Schola, which has NO CJK
    /// GLYPHS (measured 2026-07-28: a kana or ideographic syllable's path is empty, so
    /// <c>TextFontMetrics.Ink</c> returns (0, 0)), so this is the FALLBACK for a font that is
    /// missing rather than a spacing rule — <see cref="LyricUpExtent"/> and
    /// <see cref="LyricDownExtent"/> both take it as a floor UNDER the measured outline, so it
    /// can only act where the face has nothing to say.
    /// <para>
    /// ⚠️ It was one of four em fractions until the lyric em was corrected; the other three
    /// are no longer read. It is the only one that cannot be replaced by a measurement, and
    /// it stops being needed the day Lily# carries a CJK face — which is the real fix, and
    /// the reason this is marked rather than tuned.
    /// </para>
    /// </remarks>
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
    /// ★ THE FACE'S OWN OUTLINE, floored by the declared CJK fraction — the SAME shape
    /// <see cref="LyricDownExtent"/> has always had, and it is what the "CJK receptacle" this
    /// was blocked on turned out to be. TeX Gyre Schola has NO CJK GLYPHS (measured: a kana or
    /// ideographic syllable's path is empty, so its ink reads (0, 0)), which is why a bare
    /// substitution was tried and reverted — 14 snapshots moved and
    /// <c>UpperStaffLyrics_DropByFontHeight_TallCjkClearsFurtherThanLatin</c> failed. Taking
    /// the max with <see cref="CjkAscenderEm"/> keeps that case exactly as it was and lets
    /// every Latin syllable be measured.
    /// <para>
    /// ⚠️ IT COULD NOT LAND WITHOUT THE SIZE, and that is why this waited: at the old em of
    /// 3.2 the measured "no" reads 1.539200 against LilyPond's 1.187880, i.e. the outline
    /// made the residual WORSE, not better. With
    /// <see cref="EngravingDefaults.LyricTextFontSize"/> it reads 1.187789. Two halves of one
    /// claim (HANDOFF 5.0), and each alone lands on the wrong side of the answer.
    /// </para>
    /// <para>
    /// ⚠️ THE LETTER-CLASS TABLE IS GONE, deleted 2026-07-29: an <c>AscenderEm</c> of 0.66, an
    /// <c>XHeightEm</c> of 0.456 and a list of ascender letters <c>"bdfhijklt"</c>, which
    /// together guessed which of two heights a syllable had. The face's own outline answers
    /// that per string, including for the words the table got wrong (anything with an accent,
    /// a capital in the middle, or punctuation). Do not reintroduce a letter class here.
    /// <see cref="CjkAscenderEm"/> is the one em fraction that survives, because it stands in
    /// for a face Lily# does not carry; both extents take it as a floor.
    /// </para>
    /// </remarks>
    private static double LyricUpExtent(string text)
    {
        double outline = Rendering.TextFontMetrics.Ink(text, LyricFontSize).Top;
        foreach (char c in text)
            if (IsFullHeightGlyph(c))
                return Math.Max(outline, LyricFontSize * CjkAscenderEm);
        return outline;
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
    /// LILYSHARP-OWN: a row's band is still Lily#'s — its own bar lines run a staff height and
    /// its verses stack inside it — and this is verse 1's baseline within it: the text block
    /// (ascender 2.11 + descender 0.9) centred in the 4.0 ss height, so the words sit where
    /// the staff lines would be, "a staff with the lines removed".
    /// <para>
    /// ⚠️ WHAT THIS IS NO LONGER. Until 2026-07-27 it also decided how FAR the row sat from
    /// its staff: the band was placed as a staff group, 9.600000 down against LilyPond's
    /// 5.500000, a DECIDED divergence (HANDOFF 3). That decision was revisited — the row is
    /// spaced by <c>nonstaff-relatedstaff-spacing</c> off its own ink now, and the distance is
    /// a ledger point (<c>lyrics.row.staff-to-lyric</c>, exact). What survives here is only
    /// the offset of the baseline inside the band, which is also the row's REFERENCE POINT
    /// (<c>MultiStaffLayouter.RefpointBelowTop</c>), so moving it moves where the spec
    /// measures to.
    /// </para>
    /// <para>
    /// ⚠️ STILL NOT the branch a note-bound lyric line takes. <c>staff mel with lyrics words</c>
    /// carries LilyPond's <see cref="LyricParameters.RelatedStaffBasicDistance"/> and does
    /// not read this at all — MEASURED, by perturbation: moving this constant moves the row
    /// spelling with coefficient 1 and the note-bound spelling by ZERO. That the two now land
    /// on the same 5.500000 anyway is LilyPond's own identity reproduced, asserted by
    /// <c>LyricRowIsSpacedLikeTheLyricsContextItIs</c>.
    /// </para>
    /// </remarks>
    internal const double LyricRowBaseline = 2.6;

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
        Func<int, LooseLineSpacer.ChainEnd?>? looseChainEnd = null,
        Func<int, int, (double Room, VerticalSkyline NextStaffUp)?>? betweenStavesEnd = null,
        double lastSpaceableStaffY = 0,
        Func<int, IReadOnlyList<int>>? trailingRowStaves = null)
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
                // ⚠️ THE PRE-CHAIN PLACEMENT ONLY. Where the row stands below a system's last
                // spaceable staff, DistributeLooseLines overwrites every one of these from the
                // solve (:1046-1053) and the band follows through SolvedRowBaselines; this is
                // what a row the chain does not reach keeps — a staffless sheet, a leading row,
                // a row between two staves. The flat VerseSpacing survives in those regimes and
                // nowhere else.
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
                lastSpaceableStaffY, trailingRowStaves);

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
    /// ★ A CHORDS ROW LEADING THE NEXT SYSTEM IS IN THIS CHAIN since 2026-07-27
    /// (page-layout-problem.cc:948-990), which is what
    /// <c>lyrics.chord-row.between-systems.staff-to-lyric</c> measures: the room does not
    /// grow for it, so the extra occupant compresses the solve and the lyric line above is
    /// pulled closer to its staff (4.610861 against 5.500000 without).
    /// <para>
    /// ★ AN INDEPENDENT LYRICS ROW BELOW THE SYSTEM IS IN THIS CHAIN SINCE 2026-07-28, verse
    /// by verse and in the same run as the note-bound block above it — which is what
    /// <c>lyrics.row.two-verse.verse-step</c> measures. LilyPond has one model for a Lyrics
    /// context and does not ask whether it was <c>\lyricsto</c> anything, so book LYRRV now
    /// reads book LYRV digit for digit: that identity, not the step alone, is the port's test.
    /// <para>
    /// What still runs at force 0, i.e. exactly where it was: a system carrying an OSSIA; a
    /// text row standing between two spaceable staves, which belongs to
    /// <c>ComputeBetweenStavesEnd</c>'s span rather than this one; a CHORDS row below a staff,
    /// whose <c>nonstaff-*</c> specs are the ChordNames set and which no corpus point measures
    /// there; and a LEADING lyrics row, which wants one leading line PER VERSE
    /// (<c>LayoutEngine.RowSkylinesOf</c>). All of them decline for the same reason: the room
    /// would be somebody else's.
    /// </para>
    /// </para>
    /// </para>
    /// </remarks>
    private List<LyricLayout> DistributeLooseLines(
        List<LyricLayout> layouts, ImmutableArray<SystemLayout> systems,
        IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)>? systemSkylines,
        double staffBottom,
        IReadOnlyDictionary<int, double>? noteBoundAnchorY,
        Func<int, int, VerticalSkyline?>? noteBoundStaffDownSkyline,
        Func<int, LooseLineSpacer.ChainEnd?>? looseChainEnd,
        Func<int, int, (double Room, VerticalSkyline NextStaffUp)?>? betweenStavesEnd,
        double lastSpaceableStaffY,
        Func<int, IReadOnlyList<int>>? trailingRowStaves)
    {
        var measureToSystem = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);

        // A non-last-group note-bound line is anchored below its OWN group; everything
        // else note-bound shares the legacy placement below the whole system.
        bool IsUpper(LyricItem l) =>
            !l.IsLyricsRow && noteBoundAnchorY != null && noteBoundAnchorY.ContainsKey(l.StaffIndex);

        // Which LINE OF THE ALIGNMENT a syllable stands on. An independent ROW is a line of
        // its own and so is an upper note-bound block; the bottom-staff note-bound lyrics
        // share the legacy -1. THE SAME RULE <see cref="CalculateLayouts"/> GROUPS BY, so the
        // chain's elements are the lines that were drawn rather than a second reading of
        // "what is a line here".
        int LineKeyOf(LyricItem l) => l.IsLyricsRow || IsUpper(l) ? l.StaffIndex : -1;

        // From a family's anchor BASE down to the anchor staff's reference point — the
        // frame the chain is solved in. Derived from the two distances that already
        // exist rather than restating the half-staff, so it cannot drift from them.
        double anchorOffset =
            staffBottom + BasicDistanceBelowBottomLine - _params.RelatedStaffBasicDistance;

        // The independent rows standing below each system's last spaceable staff, in
        // alignment order. Cached because the membership test below is asked once per
        // syllable and a book of this shape has hundreds.
        var trailingCache = new Dictionary<int, IReadOnlyList<int>>();
        IReadOnlyList<int> TrailingRows(int system)
        {
            if (trailingRowStaves == null) return Array.Empty<int>();
            if (!trailingCache.TryGetValue(system, out var rows))
                trailingCache[system] = rows = trailingRowStaves(system);
            return rows;
        }

        bool IsTrailingRow(LyricLayout lay, out int system)
        {
            system = -1;
            return lay.Item.IsLyricsRow
                && measureToSystem.TryGetValue(lay.Item.MeasureIndex, out system)
                && TrailingRows(system).Contains(lay.Item.StaffIndex);
        }

        // ONE PAIR OF DICTIONARIES FOR EVERY LINE, keyed by the alignment line rather than by
        // the verse alone: a row and a note-bound block can stand under the SAME anchor
        // (`staff X with lyrics a` + `lyrics b`), and keying by verse would have their verse 1
        // read each other's ink.
        var up = BuildVerseUpSkylines(layouts, measureToSystem, LineKeyOf);
        var down = BuildVerseDownSkylines(layouts, measureToSystem, LineKeyOf);

        var newY = new Dictionary<(int Family, int System, int Line, int Verse), double>();

        // The blocks the alignment walks. ★ AN INDEPENDENT LYRICS ROW STANDING BELOW THE
        // SYSTEM IS IN THE BLOCK BELOW THE SYSTEM, because that is the run LilyPond collects:
        // every non-spaceable line between the last spaceable staff and the next one, in
        // alignment order, into ONE chain (page-layout-problem.cc:919-925, :948-990). It is
        // NOT a chain of its own — two chains solved into one room would overlap.
        var families = layouts.Where(l => !l.Item.IsLyricsRow)
            .GroupBy(l => IsUpper(l.Item) ? l.Item.StaffIndex : -1)
            .Select(g => (Key: g.Key, Lines: (IReadOnlyList<LyricLayout>)g.ToList()))
            .ToList();
        // ...and a book whose only lyrics ARE a row still has that block: the run is made of
        // rows alone. Without this the loop below never runs and the row keeps its band.
        if (families.All(f => f.Key != -1) && layouts.Any(l => IsTrailingRow(l, out _)))
            families.Add((-1, Array.Empty<LyricLayout>()));

        foreach (var family in families)
        {
            int familyKey = family.Key;
            bool isUpperFamily = familyKey >= 0;
            double anchorBase = isUpperFamily && noteBoundAnchorY != null
                                && noteBoundAnchorY.TryGetValue(familyKey, out var groupBottomY)
                ? groupBottomY : lastSpaceableStaffY;

            var chainSystems = new SortedSet<int>();
            foreach (var lay in family.Lines)
                if (measureToSystem.TryGetValue(lay.Item.MeasureIndex, out int s))
                    chainSystems.Add(s);
            if (!isUpperFamily)
                foreach (var lay in layouts)
                    if (IsTrailingRow(lay, out int s))
                        chainSystems.Add(s);

            foreach (int system in chainSystems)
            {
                // THIS block's lines, in the order the alignment walks them: the note-bound
                // verses that hang under the anchor staff, and then — below the system —
                // every independent row standing under it, verse by verse. A row's verses are
                // separate Lyrics contexts to LilyPond (the LYRV/LYRRV pair is exactly that
                // spelling difference), so they are separate elements here.
                var elements = new List<(int Line, int Verse)>();
                var rowFirstElement = new List<(int RowStaff, int Index)>();
                foreach (int v in family.Lines
                             .Where(l => measureToSystem.TryGetValue(l.Item.MeasureIndex, out int s)
                                         && s == system)
                             .Select(l => l.Item.VerseNumber).Distinct().OrderBy(v => v))
                    elements.Add((familyKey, v));
                if (!isUpperFamily)
                {
                    foreach (int rowStaff in TrailingRows(system))
                    {
                        int before = elements.Count;
                        foreach (int v in layouts
                                     .Where(l => l.Item.IsLyricsRow && l.Item.StaffIndex == rowStaff
                                                 && measureToSystem.TryGetValue(l.Item.MeasureIndex, out int s)
                                                 && s == system)
                                     .Select(l => l.Item.VerseNumber).Distinct().OrderBy(v => v))
                            elements.Add((rowStaff, v));
                        if (elements.Count > before)
                            rowFirstElement.Add((rowStaff, before));
                    }
                }
                if (elements.Count == 0) continue;

                // The staff this block hangs from — ITS OWN down-skyline, whichever block it
                // is. LILYPOND-REF: lily/align-interface.cc:272-273 — the walk measures each
                // element against what has accumulated ABOVE it, and at the first loose line
                // that is the anchor staff.
                // ★ THE LEGACY BRANCH USED TO READ THE WHOLE SYSTEM'S SILHOUETTE, which is the
                // same thing only while the anchor IS the bottom of the system. It stops being
                // so the moment an independent row stands under it — MEASURED on book LYRRV,
                // the staff's own bottom line drops out of the system's bottom profile and the
                // floor came out 1.050000 short. The system skyline remains the FALLBACK for
                // the case with no anchor to name (a staffless sheet).
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
                // ⚠️ THE SYSTEM SILHOUETTE HAS TO CONTAIN THE ANCHOR STAFF, and for one commit
                // in 2026-07-28 it did not: with an independent lyrics row below the staff,
                // SkylineBuilder seeded the bottom line off the row (which draws none) and the
                // staff's own line left the profile, flooring this chain 1.050000 short. It is
                // fixed where it belongs — each edge staff seeds its OWN two lines
                // (SkylineBuilder.SeedSystemStaffSymbol) — and the interim repair that merged
                // the anchor's skyline in HERE is gone with it. One silhouette, one reader.
                double skylineToAnchor = isUpperFamily ? 0 : anchorBase + anchorOffset;

                var gaps = new List<LooseLineSpacer.Gap>(elements.Count + 2);

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
                double Advance((int Line, int Verse) element, double padding, double minimumDistance)
                {
                    up.TryGetValue((system, element.Line, element.Verse), out var lineUp);
                    down.TryGetValue((system, element.Line, element.Verse), out var lineDown);
                    return walk.Advance(lineUp, lineDown, padding, minimumDistance);
                }

                // Staff to the first loose line, in the chain's frame — the anchor staff's
                // REFERENCE POINT, which is what skylineToAnchor converts to.
                // nonstaff-relatedstaff-spacing declares no minimum-distance, which
                // read_spacing_spec leaves as no raise.
                gaps.Add(new LooseLineSpacer.Gap(
                    LooseLineSpacer.NonStaffRelatedStaff,
                    Advance(elements[0], SkylineDrop.RelatedStaffPadding,
                            LooseLineSpacer.NonStaffRelatedStaff.MinimumDistance)
                        - skylineToAnchor));

                // Line to line, whose spec DOES declare one (2.8). ⚠️ THE SAME SPEC WHETHER
                // THE STEP IS VERSE-TO-VERSE OR BLOCK-TO-ROW: get_spacing_spec's loose-loose
                // branch reads the UPPER line's nonstaff-nonstaff-spacing and never asks what
                // kind of Lyrics context it is (page-layout-problem.cc:1315-1332).
                for (int i = 1; i < elements.Count; i++)
                {
                    gaps.Add(new LooseLineSpacer.Gap(
                        LooseLineSpacer.NonStaffNonStaff,
                        Advance(elements[i], SkylineDrop.NonStaffNonStaffPadding,
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
                int firstLeadingPosition = -1;
                if (end is { } chainEnd)
                {
                    room = chainEnd.Room;
                    if (double.IsNaN(chainEnd.NextStaffMinDistance))
                    {
                        // LILYPOND-REF: lily/page-layout-problem.cc:1004-1013 — the last
                        // block on a page runs to the page edge, floored by the last
                        // line's own descent (Lily# has no footer).
                        var last = elements[^1];
                        double descent = 0;
                        foreach (var lay in layouts)
                            if (LineKeyOf(lay.Item) == last.Line && lay.Item.VerseNumber == last.Verse
                                && measureToSystem.TryGetValue(lay.Item.MeasureIndex, out int s) && s == system)
                                descent = Math.Max(descent, LyricDownExtent(lay.Item.Text));
                        gaps.Add(new LooseLineSpacer.Gap(LooseLineSpacer.NullNeighbour, descent));
                    }
                    else if (chainEnd.Lines.IsDefaultOrEmpty)
                    {
                        // LILYPOND-REF: lily/page-layout-problem.cc:928-933 — a NULL line
                        // breaks the affinity to the previous system (minimum 0.0), and
                        // the gap after it reaches the next system's first staff.
                        gaps.Add(new LooseLineSpacer.Gap(LooseLineSpacer.NullNeighbour, 0.0));
                        gaps.Add(new LooseLineSpacer.Gap(
                            LooseLineSpacer.NullNeighbour, chainEnd.NextStaffMinDistance));
                    }
                    else
                    {
                        // ★ THE NEXT SYSTEM OPENS WITH LOOSE LINES, so they are in THIS
                        // chain — LilyPond pushes every non-spaceable line onto the same
                        // `loose_lines` vector and closes the run on the next spaceable
                        // staff (:948-990). The room does not grow for them (MEASURED:
                        // system-to-system is 12.000000 with the row and without it), so an
                        // extra occupant COMPRESSES the solve rather than displacing it, and
                        // the lyric line above is pulled closer to its own staff.
                        // LILYPOND-REF: :973-975 — the null line first, minimum 0.0.
                        gaps.Add(new LooseLineSpacer.Gap(LooseLineSpacer.NullNeighbour, 0.0));
                        // The ELEMENT index of the first leading line, not the spring index:
                        // element j sits at positions[j] and is reached by spring j-1, so the
                        // line after the gap about to be added is one past the count.
                        // ⚠️ THE NULL LINE OCCUPIES AN INDEX OF ITS OWN — LilyPond pushes a
                        // real (null) entry onto loose_lines (:975) and skips it only when
                        // translating (:1047) — so leaving it out here reads the null's
                        // position for the row, which is m2 too high (measured: 6.550800
                        // above the staff's reference point instead of 3.576200).
                        firstLeadingPosition = gaps.Count + 1;

                        for (int k = 0; k < chainEnd.Lines.Length; k++)
                        {
                            var line = chainEnd.Lines[k];
                            // LILYPOND-REF: :971-972 — the FIRST line's minimum is the
                            // system-level `elements_[i].min_distance + elements_[i].padding`,
                            // and :644-645 recomputes that min_distance as
                            // `first_skyline.distance (bottom_skyline_) - bottom_loose_baseline_`
                            // — which is one more step of THIS walk, re-referenced for free.
                            // Every later line carries its own alignment step (LeadingLine).
                            double min = k == 0
                                ? walk.Distance(line.Up, _systemPadding)
                                : line.MinInto;
                            gaps.Add(new LooseLineSpacer.Gap(line.SpecInto, min));
                            walk.Advance(line.Up, line.Down, line.SpecInto.Padding,
                                         line.SpecInto.MinimumDistance);
                        }

                        // ...and the closing spring onto the next system's first spaceable
                        // staff, whose minimum is that system's own alignment step
                        // (:923-925) rather than anything this walk knows.
                        gaps.Add(new LooseLineSpacer.Gap(
                            chainEnd.ClosingSpec ?? LooseLineSpacer.NullNeighbour,
                            chainEnd.ClosingMinDistance));
                    }
                }
                // ...and NOTHING when the room is unknown, which is now the system carrying
                // an ossia, or a text row this chain does not reach — a LYRICS row (no ink to
                // be spaced by) or one standing between two spaceable staves
                // (LayoutEngine.BuildLooseChainEnds and ComputeBetweenStavesEnd decline
                // those; a leading CHORDS row is handled above). LilyPond's chain always
                // ends on something — the next staff, or the page edge — so a terminator with
                // no room behind it would be a spring this port invented: it cannot be given
                // LilyPond's minimum, it changes no position (the verses read
                // positions[1..n], which the gaps above already produce), and it would read
                // to the next person as if the chain were complete. The absent end is the
                // honest spelling of "this chain is not solved yet".

                var positions = LooseLineSpacer.Distribute(gaps, room);
                for (int i = 0; i < elements.Count; i++)
                {
                    // Y-up: a line below the anchor is negative.
                    newY[(familyKey, system, elements[i].Line, elements[i].Verse)] =
                        -(anchorBase + anchorOffset + positions[i + 1]);
                }

                // ...and THIS system's own rows travel with the solve: a row draws its own bar
                // grid off its StaffLayout.Y, so the band has to follow the syllables or the
                // two come apart. What is published is the row's REFERENCE POINT — verse 1's
                // baseline — in page Y-up, which is the frame ApplySolvedRowPositions works in.
                // LILYPOND-REF: lily/page-layout-problem.cc:1046-1053 — distribute_loose_lines
                // ends by translating every loose line to its solved position.
                if (rowFirstElement.Count > 0 && system < systems.Length && !systems.IsDefaultOrEmpty)
                {
                    double rowAnchorPageY = systems[system].Y - (anchorBase + anchorOffset);
                    foreach (var (rowStaff, index) in rowFirstElement)
                        SolvedRowBaselines[(system, rowStaff)] =
                            rowAnchorPageY - positions[index + 1];
                }

                // ...and the NEXT system's leading rows, which were solved in the same chain.
                // LILYPOND-REF: lily/page-layout-problem.cc:1046-1053 — every loose line that
                // is a real grob (the null at index 0 of the run is skipped by
                // `if (loose_lines[i])`) is translated to its solved position.
                if (firstLeadingPosition >= 0 && end is { } solved
                    && system < systems.Length && !systems.IsDefaultOrEmpty)
                {
                    // The anchor staff's reference point in PAGE Y-up — the frame the
                    // published baselines are in, because the row belongs to a DIFFERENT
                    // system from the block that solved it and the two only meet on the page.
                    double anchorPageY = systems[system].Y - (anchorBase + anchorOffset);
                    for (int k = 0; k < solved.Lines.Length; k++)
                        SolvedRowBaselines[(system + 1, solved.Lines[k].StaffIndex)] =
                            anchorPageY - positions[firstLeadingPosition + k];
                }
            }
        }

        if (newY.Count == 0) return layouts;

        var placed = new List<LyricLayout>(layouts.Count);
        foreach (var lay in layouts)
        {
            // A row's family is the one below the system; a note-bound line's is its anchor.
            // A row the chain did not reach — leading, between two staves, or on a staffless
            // sheet — simply has no entry and keeps the band it was laid out in.
            if (measureToSystem.TryGetValue(lay.Item.MeasureIndex, out int s)
                && newY.TryGetValue(
                    (lay.Item.IsLyricsRow ? -1 : IsUpper(lay.Item) ? lay.Item.StaffIndex : -1,
                     s, LineKeyOf(lay.Item), lay.Item.VerseNumber),
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
        var inBlock = new List<LyricItem>();
        foreach (var l in lyrics)
            if (!l.IsLyricsRow
                && l.StaffIndex >= firstStaffIndex && l.StaffIndex < endStaffIndex
                && l.MeasureIndex >= startMeasure && l.MeasureIndex < endMeasure)
                inBlock.Add(l);
        return BlockSkylines(inBlock, measureLayouts);
    }

    /// <summary>
    /// The verses of ONE independent lyrics ROW, in the same self-relative form
    /// <see cref="NoteBoundBlockSkylines"/> returns — its real syllable ink, per verse, for
    /// the measures of one system.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:919-925 and :948-990 — a Lyrics context is
    /// pushed onto <c>loose_lines</c> and spaced by its own skyline wherever it sits, and
    /// <c>\lyricsto</c> is not consulted: association decides which COLUMN a syllable stands
    /// on, not what holds the line. So a row's ink is collected exactly as
    /// <see cref="NoteBoundBlockSkylines"/>'s is, and only the staff index differs. MEASURED,
    /// as whole dumps rather than by eye: books LYRC/LYRR and LYRV/LYRRV print line for line
    /// the same figures (audit/lp-geometry/probes/page-vertical.ly).
    /// <para>
    /// ★ THIS IS THE CHAIN'S INPUT SINCE 2026-07-28. A row standing below the system's last
    /// spaceable staff has its verses walked into that system's own run
    /// (<c>LayoutEngine.LyricReservationBelowSystem</c> for what the page reserves,
    /// <see cref="DistributeLooseLines"/> for where they land), so the same list serves the
    /// reservation, the row's own skyline and the solve — one reading of the ink, three
    /// consumers, which is the shape HANDOFF 5.2.1② asks for.
    /// </para>
    /// <para>
    /// ⚠️ ONE X MODEL. It goes through the same <see cref="CalculateSyllableLayout"/> and
    /// <see cref="ResolveOverlaps"/> as every other reading of syllable ink, for the reason
    /// spelled out on <see cref="NoteBoundBlockSkylines"/>: a second X model here would be
    /// HANDOFF 5.2.1② in the place that has already cost this island a session.
    /// </para>
    /// </remarks>
    internal List<(VerticalSkyline Up, VerticalSkyline Down)> RowBlockSkylines(
        IReadOnlyList<LyricItem> lyrics, IReadOnlyList<MeasureLayout> measureLayouts,
        int startMeasure, int endMeasure, int rowStaffIndex)
    {
        var inBlock = new List<LyricItem>();
        foreach (var l in lyrics)
            if (l.IsLyricsRow && l.StaffIndex == rowStaffIndex
                && l.MeasureIndex >= startMeasure && l.MeasureIndex < endMeasure)
                inBlock.Add(l);
        return BlockSkylines(inBlock, measureLayouts);
    }

    /// <summary>
    /// One independent lyrics ROW's whole ink, as a single up/down pair about the row's own
    /// REFERENCE POINT — verse 1's baseline, which is where <c>MultiStaffLayouter</c> puts a
    /// text row's refpoint.
    /// </summary>
    /// <remarks>
    /// The frame is the one every entry in the per-staff skyline list is in: the element's
    /// own reference point (a staff's middle line, a text row's text baseline).
    /// <para>
    /// ★ THE VERSE STEP IS WALKED, NOT DECLARED (2026-07-28). Verse k sits where
    /// <see cref="AlignmentWalk"/> puts it — <c>nonstaff-nonstaff-spacing</c> (basic-distance
    /// 0, minimum-distance 2.8, padding 0.2 — ly/engraver-init.ly:653-656) through
    /// <c>get_spacing_spec</c>'s loose-loose branch (page-layout-problem.cc:1315-1332), whose
    /// realized step is <c>max(2.8, the two lines' ink + 0.2)</c> and RESPONDS TO THE TEXT.
    /// This used to be a flat <c>k * VerseSpacing</c>, which was HANDOFF 5.2's
    /// "評価結果を書かない" on the wrong side: LilyPond computes the step, so Lily# computes it.
    /// <para>
    /// ⚠️ THE SAME WALK THE CHAIN TAKES, and it has to be: <c>DistributeLooseLines</c> now
    /// makes each verse an element of the loose chain with these very specs, and the verse
    /// spring is rigid in both directions (stretch declared 0, compress derived
    /// <c>max(0, 0 - 2.8)</c>), so the solve cannot move a step off the number this walk
    /// produces. A second model of the step would be free to disagree the day either side
    /// changed — HANDOFF 5.2.1②.
    /// </para>
    /// </para>
    /// </remarks>
    internal (VerticalSkyline Up, VerticalSkyline Down) RowSkylinesAboutBaseline(
        IReadOnlyList<(VerticalSkyline Up, VerticalSkyline Down)> verses)
    {
        var up = new VerticalSkyline(VerticalDirection.Up);
        var down = new VerticalSkyline(VerticalDirection.Down);
        if (verses.Count == 0) return (up, down);

        // Where each verse sits below verse 1's baseline. Walked first and applied second,
        // because applying it raises the very skylines the walk reads.
        var drops = new double[verses.Count];
        var walk = new AlignmentWalk();
        walk.Seed(verses[0].Down);
        for (int v = 1; v < verses.Count; v++)
        {
            walk.Advance(verses[v].Up, verses[v].Down,
                SkylineDrop.NonStaffNonStaffPadding,
                LooseLineSpacer.NonStaffNonStaff.MinimumDistance);
            drops[v] = walk.Where;
        }

        for (int v = 0; v < verses.Count; v++)
        {
            double drop = drops[v];
            // Skyline::raise moves a skyline along its OWN direction, so a DOWN skyline is
            // lowered by +drop and an UP skyline by -drop (lily/skyline.cc:512,
            // `y_intercept_ += sky_ * amount`). AlignmentWalk raises its accumulated DOWN
            // profile the same way.
            var vUp = verses[v].Up;
            var vDown = verses[v].Down;
            if (drop != 0)
            {
                vUp.Raise(-drop);
                vDown.Raise(drop);
            }
            up.Merge(vUp);
            down.Merge(vDown);
        }
        return (up, down);
    }

    /// <summary>
    /// An engraver configured for GEOMETRY ONLY — one X model, no layout — for the callers
    /// that need a syllable's ink before anything has been placed.
    /// </summary>
    /// <remarks>
    /// ⚠️ ONE CONSTRUCTION SITE. Both the page's reservation (<c>LayoutEngine</c>) and the
    /// alignment's own skylines (<c>MultiStaffLayouter</c>) need this, and two of them would
    /// be two X models — the shape HANDOFF 5.2.1② names, in the place that has already cost
    /// this island a session.
    /// </remarks>
    internal static LyricEngraver ForGeometry(MultiStaffScore score)
    {
        var measuresByStaff = new Dictionary<int, ImmutableArray<Measure>>();
        foreach (var (_, st, idx) in score.EnumerateStaves())
            measuresByStaff[idx] = st.PrimaryVoice.Measures;
        return new LyricEngraver(parentAlignmentCentre: ParentAlignmentCentre(measuresByStaff, null));
    }

    /// <summary>
    /// Where a syllable's ink centre lands on its column: the column's ALIGNMENT EXTENT
    /// centre, not the column itself.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/self-alignment-interface.cc:117-176 — the extent is the column's
    /// note heads / rests, which only the MUSIC knows, so it is resolved from the model and
    /// handed to the engraver. Cached per (measure, timing): a bar's syllables all ask the
    /// same measure.
    /// <para>
    /// ⚠️ ONE FACTORY, because a syllable's X is wanted several times over — for the drawn
    /// layouts, for the ink the room between two staves is walked from
    /// (<see cref="NoteBoundBlockSkylines"/>), and for a ROW's own skyline
    /// (<see cref="RowBlockSkylines"/>). Two spellings of an X is the shape HANDOFF 5.2.1②
    /// names. It lives here rather than in <c>LayoutEngine</c>, where it used to be, because
    /// the engraver is the thing that has to agree with itself.
    /// </para>
    /// </remarks>
    internal static Func<int, Fraction, double> ParentAlignmentCentre(
        IReadOnlyDictionary<int, ImmutableArray<Measure>>? measuresByStaff,
        ImmutableArray<Measure>? measures)
    {
        const double placeholderCentre = EngravingDefaults.PaperColumnXAlignmentExtentWidth / 2;
        var alignmentCentreCache = new Dictionary<int, Dictionary<Fraction, double>>();
        return Centre;

        double Centre(int measureIndex, Fraction timing)
        {
            if (!alignmentCentreCache.TryGetValue(measureIndex, out var byTiming))
            {
                byTiming = new Dictionary<Fraction, double>();
                // EVERY staff's bar at this index — a paper column is shared by all of them,
                // and so is the extent a grob on it aligns to.
                var barMeasures = new List<Measure>();
                if (measuresByStaff != null)
                    foreach (var staffMeasures in measuresByStaff.Values)
                    {
                        if (measureIndex < staffMeasures.Length)
                            barMeasures.Add(staffMeasures[measureIndex]);
                    }
                else if (measures is { } scoreMeasures && measureIndex < scoreMeasures.Length)
                    barMeasures.Add(scoreMeasures[measureIndex]);

                var barTimings = new List<Fraction>();
                foreach (var barMeasure in barMeasures)
                {
                    var onset = Fraction.Zero;
                    foreach (var item in barMeasure.Items)
                    {
                        if (!barTimings.Contains(onset))
                            barTimings.Add(onset);
                        onset += item.Duration;
                    }
                }
                barTimings.Sort();

                var centres = SpacingRules.ParentAlignmentCentresPerColumn(barMeasures, barTimings);
                for (int c = 0; c < barTimings.Count; c++)
                    byTiming[barTimings[c]] = centres[c];
                alignmentCentreCache[measureIndex] = byTiming;
            }
            // A moment no staff plays on — a lyric row's own finer grid — has an empty
            // note-column extent, which is exactly when LilyPond takes the placeholder.
            return byTiming.TryGetValue(timing, out var centre) ? centre : placeholderCentre;
        }
    }

    /// <summary>
    /// One self-relative up/down skyline pair per verse, in verse order, for a set of
    /// syllables already selected by the caller.
    /// </summary>
    private List<(VerticalSkyline Up, VerticalSkyline Down)> BlockSkylines(
        List<LyricItem> inBlock, IReadOnlyList<MeasureLayout> measureLayouts)
    {
        var result = new List<(VerticalSkyline, VerticalSkyline)>();
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
    internal static Dictionary<(int System, int Line, int Verse), VerticalSkyline> BuildVerseUpSkylines(
        IEnumerable<LyricLayout> layouts, IReadOnlyDictionary<int, int> measureToSystem,
        Func<LyricItem, int> lineKeyOf)
    {
        var result = new Dictionary<(int System, int Line, int Verse), VerticalSkyline>();
        foreach (var lay in layouts)
        {
            if (!measureToSystem.TryGetValue(lay.Item.MeasureIndex, out int s)) continue;
            var key = (System: s, Line: lineKeyOf(lay.Item), Verse: lay.Item.VerseNumber);
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
    internal static Dictionary<(int System, int Line, int Verse), VerticalSkyline> BuildVerseDownSkylines(
        IEnumerable<LyricLayout> layouts, IReadOnlyDictionary<int, int> measureToSystem,
        Func<LyricItem, int> lineKeyOf)
    {
        var result = new Dictionary<(int System, int Line, int Verse), VerticalSkyline>();
        foreach (var lay in layouts)
        {
            if (!measureToSystem.TryGetValue(lay.Item.MeasureIndex, out int s)) continue;
            var key = (System: s, Line: lineKeyOf(lay.Item), Verse: lay.Item.VerseNumber);
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
