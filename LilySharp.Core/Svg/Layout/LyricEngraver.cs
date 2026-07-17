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
/// Parameters for lyric layout calculation.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:3020-3060 LyricText grob
/// LILYPOND-REF: lily/lyric-engraver.cc:20-30 default parameters
/// </remarks>
internal sealed record LyricParameters
{
    /// <summary>Distance below the staff in staff spaces.</summary>
    public double StaffPadding { get; init; } = 2.5;

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
/// LILYPOND-REF: lily/lyric-engraver.cc:60-150 process_music, stop_translation_timestep
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

    public LyricEngraver(LyricParameters? parameters = null)
    {
        _params = parameters ?? LyricParameters.Default;
    }

    /// <summary>
    /// The lyric serif font size in staff spaces (SharedRenderer draws lyrics at
    /// FontSize * 0.8 = 4 * 0.8). Kept in sync with EstimateTextWidth, which uses
    /// the same value to turn em-fraction advance widths into staff-space widths.
    /// </summary>
    private const double LyricFontSize = 3.2;

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

    // LilyPond Lyrics relatedstaff-spacing: the line is lowered so its up-skyline
    // clears the staff down-skyline. The distance→drop math and its padding live in
    // SkylineDrop (shared with figured bass).

    /// <summary>Minimum X-width of a syllable's skyline box (narrow glyphs).</summary>
    private const double MinSyllableBoxWidth = 0.8;


    /// <summary>Baseline of an independent lyrics ROW's verse 1 below the row band's
    /// top, so the text sits inside the reserved band (cf. ChordRow text baseline).</summary>
    // Verse 1's baseline inside the row's STAFF-HEIGHT band: the text block
    // (ascender 2.11 + descender 0.9) vertically centred in the 4.0 ss band —
    // the words sit where the staff lines would be, "a staff with the lines
    // removed".
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
    public ImmutableArray<LyricLayout> CalculateLayouts(
        IReadOnlyList<LyricItem> lyrics,
        IReadOnlyList<MeasureLayout> measureLayouts,
        double staffBottom,
        ImmutableArray<SystemLayout> systems = default,
        IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)>? systemSkylines = null,
        IReadOnlyDictionary<int, double>? staffYByIndex = null,
        IReadOnlyDictionary<int, double>? noteBoundAnchorY = null)
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
                // A non-last-group note-bound line sits just BELOW that group's bottom staff.
                verseY = groupBottomY + staffBottom + _params.StaffPadding + (verseNumber - 1) * _params.VerseSpacing;
            else
                verseY = staffBottom + _params.StaffPadding + (verseNumber - 1) * _params.VerseSpacing;

            var verseLayouts = new List<LyricLayout>();
            for (int i = 0; i < verseLyrics.Count; i++)
            {
                var (lyric, srcIndex) = verseLyrics[i];
                var layout = CalculateSyllableLayout(
                    lyric,
                    measureLayouts,
                    verseY,
                    i + 1 < verseLyrics.Count ? verseLyrics[i + 1].Lyric : null);

                if (layout != null)
                    verseLayouts.Add(layout with { SourceIndex = srcIndex });
            }

            // Apply collision avoidance for this verse
            // LILYPOND-REF: lily/lyric-engraver.cc:120-140 collision handling
            verseLayouts = ResolveOverlaps(verseLayouts);
            layouts.AddRange(verseLayouts);
        }

        // Lower each system's lyric line so the TEXT clears notes/ledger lines
        // poking below the staff (LilyPond's max(basic-distance, skyline)).
        if (systemSkylines != null && !systems.IsDefaultOrEmpty)
            layouts = ApplySkylineDrop(layouts, systems, systemSkylines, staffBottom, noteBoundAnchorY);

        return layouts.ToImmutableArray();
    }

    /// <summary>
    /// Places each system's lyric line below the staff at the LilyPond distance
    ///   realized = max(basic-distance, staffDownSkyline.distance(lyricUpSkyline) + padding)
    /// — Align_interface's per-pair spacing (align-interface.cc:222-275,
    /// page-layout-problem.cc:625-629). The lyric line's UP-skyline is built from
    /// the REAL text boxes of its (verse-1) syllables, so the skyline distance is
    /// the true glyph-to-note clearance, not a single-point estimate. basic-distance
    /// (the fixed floor) wins for ordinary music — so common snapshots are
    /// untouched and only notes poking far below drop the line, with the LP padding
    /// gap. The whole line (all verses) shifts together, preserving verse stacking.
    /// </summary>
    private List<LyricLayout> ApplySkylineDrop(
        List<LyricLayout> layouts, ImmutableArray<SystemLayout> systems,
        IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)> systemSkylines,
        double staffBottom,
        IReadOnlyDictionary<int, double>? noteBoundAnchorY = null)
    {
        var measureToSystem = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);

        double basic = staffBottom + _params.StaffPadding;

        // A non-last-group note-bound line is anchored below its own group (not the
        // system bottom); the system-wide drop, which clears the LOWEST staff's notes,
        // must not pull it down there. It sits in its inter-group gap already.
        bool SkipDrop(LyricItem l) =>
            !l.IsLyricsRow && noteBoundAnchorY != null && noteBoundAnchorY.ContainsKey(l.StaffIndex);

        // Build each system's lyric UP-skyline from the verse-1 syllable boxes,
        // self-relative to the line's anchor (anchor at y=0; text top at -topExtent
        // above it, so the UP-skyline height there is +topExtent).
        var lyricUp = new Dictionary<int, VerticalSkyline>();
        foreach (var lay in layouts)
        {
            if (lay.Item.IsLyricsRow || SkipDrop(lay.Item)) continue; // a row / upper line sits in its own band
            if (lay.Item.VerseNumber > 1) continue; // verse 1 is the line's top edge
            if (!measureToSystem.TryGetValue(lay.Item.MeasureIndex, out int s))
                continue;
            double halfW = Math.Max(lay.Width, MinSyllableBoxWidth) / 2.0;
            var box = VerticalSkyline.FromBox(
                lay.X - halfW, lay.X + halfW, 0, -LyricUpExtent(lay.Item.Text), VerticalDirection.Up);
            if (lyricUp.TryGetValue(s, out var sky)) sky.Merge(box);
            else lyricUp[s] = box;
        }

        // Lyrics share ONE basic-distance floor across systems (the line baseline).
        var systemDrop = SkylineDrop.Compute(lyricUp, _ => basic, systemSkylines);

        if (systemDrop.Count == 0)
            return layouts;

        var shifted = new List<LyricLayout>(layouts.Count);
        foreach (var lay in layouts)
        {
            double drop = !lay.Item.IsLyricsRow && !SkipDrop(lay.Item)
                && measureToSystem.TryGetValue(lay.Item.MeasureIndex, out int s)
                && systemDrop.TryGetValue(s, out var d) ? d : 0;
            shifted.Add(drop > 0 ? lay with { Y = lay.Y + drop } : lay);
        }
        return shifted;
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
        double y,
        LyricItem? nextLyric)
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

        // Center the syllable under the note. Hyphen dashes and extender
        // lines are LyricHyphen's job (the LP grobs: lyric-hyphen.cc /
        // lyric-extender.cc); this engraver used to ALSO emit a "-" text and
        // its own extender line, double-drawing every connector.
        double syllableX = noteX;

        return new LyricLayout(
            lyric,
            syllableX,
            y,
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
        => Rendering.SerifTextMetrics.Measure(text, LyricFontSize);
}
