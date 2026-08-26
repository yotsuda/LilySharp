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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Tablature;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Handles layout calculations specific to multi-staff scores.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/system.cc
/// LILYPOND-REF: lily/staff-spacing.cc
/// LILYPOND-REF: lily/align-interface.cc — internal_get_minimum_translations()
/// LILYPOND-REF: lily/staff-grouper-interface.cc — staff-staff-spacing, staffgroup-staff-spacing
///
/// Skyline-based staff spacing (align-interface.cc:217-268):
///   when measure layouts are provided, uses skyline distance between staves for collision avoidance;
///   falls back to fixed formula (BasicDistance - staffHeight) when skylines are unavailable
/// IMPLEMENTED — pure height estimation (axis-group-interface.cc:138-173) via CalculatePureSystemHeight
/// IMPLEMENTED — staff-affinity for non-spaceable staves (align-interface.cc:240-252) via StaffAffinity.Select
/// IMPLEMENTED — hara-kiri auto-hide empty staves (hara-kiri-group-spanner.cc)
/// IMPLEMENTED — alignment-distances manual override (StaffSpacingParameters.ApplyOverrides)
/// Outside-staff-priority stacking: implemented in OutsideStaffStacker.cs (axis-group-interface.cc:359-474)
/// IMPLEMENTED — brace collapse-height (system-start-delimiter.cc:127-129)
/// IMPLEMENTED — ChoirStaff/bracket delimiter variants (system-start-delimiter.cc)
/// </remarks>
internal sealed class MultiStaffLayouter
{
    private readonly LayoutOptions _options;
    private readonly MeasureLayouter _measureLayouter;
    // Lays slurs out in the staff's own frame for the per-staff skyline (see
    // StaffSlurLayouts); cheap to construct (options only) and stateless per call.
    private readonly ElementCoordinator _elementCoordinator;

    /// <summary>
    /// Current indent for the system being laid out (in staff spaces).
    /// Set by LayoutEngine before each system's layout calls.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/paper-defaults-init.ly — indent / short-indent
    /// LILYPOND-REF: scm/output-lib.scm — system-start-text::calc-x-offset uses indent
    /// </remarks>
    internal double CurrentIndent { get; set; }

    public MultiStaffLayouter(LayoutOptions options, MeasureLayouter measureLayouter)
    {
        _options = options;
        _measureLayouter = measureLayouter;
        _elementCoordinator = new ElementCoordinator(options);
    }

    /// <summary>
    /// Calculates the total height of a multi-staff system.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc internal_get_minimum_translations()
    /// Uses StaffSpacingParameters for intra-group and inter-group spacing.
    /// </remarks>
    public double CalculateSystemHeight(MultiStaffScore score)
    {
        double height = 0;
        double staffHeight = _options.StaffHeight;
        var sp = _options.StaffSpacing;
        int globalStaffIndex = 0;

        for (int i = 0; i < score.StaffGroups.Length; i++)
        {
            var group = score.StaffGroups[i];

            if (group.IsGrandStaff)
            {
                // Sum each staff's ACTUAL height plus one intra-group gap per pair. A
                // grand staff is usually two normal staves (piano/harp), but may hold
                // three-plus (organ) or an ossia/tab staff; the old `staffHeight * 2`
                // hardcoded exactly two normal staves and under-counted anything else.
                // For the common two-normal-staff case this still equals
                // `staffHeight*2 + (BasicDistance - staffHeight)`, and it mirrors LP's
                // align-interface (sum of real staff extents + inter-staff spacing).
                // LILYPOND-REF: lily/align-interface.cc internal_get_minimum_translations()
                foreach (var staff in group.Staves)
                    height += GetStaffHeight(staff);
                if (group.StaffCount > 1)
                    height += (sp.StaffStaff.BasicDistance - staffHeight) * (group.StaffCount - 1);
            }
            else
            {
                foreach (var staff in group.Staves)
                {
                    height += GetStaffHeight(staff);
                }
                if (group.StaffCount > 1)
                {
                    // Intra-group spacing for each pair
                    double intraSpacing = sp.StaffStaff.BasicDistance - staffHeight;
                    height += Math.Max(0, intraSpacing) * (group.StaffCount - 1);
                }
            }

            if (i < score.StaffGroups.Length - 1)
            {
                // LILYPOND-REF: lily/align-interface.cc:240-252 — direction-aware staff-affinity spec selection.
                // ⚠️ ONE HOME FOR THE SELECTION, INCLUDING THE OSSIA CASE — see
                // InterGroupSpec, which this used to duplicate with a second ossia branch of
                // its own (HANDOFF 5.2.1②: the port lands on one copy and not the other).
                var nextGroup = score.StaffGroups[i + 1];
                var spec = InterGroupSpec(group, nextGroup, sp);
                bool textRowPair = group.Staves[^1].IsTextRow && nextGroup.Staves[0].IsTextRow;
                double interGroupGap = textRowPair
                    ? TextRowPairGap
                    : spec.BasicDistance - GapSpan(score, group.Staves[^1], nextGroup.Staves[0]);
                height += interGroupGap;
            }

            globalStaffIndex += group.StaffCount;
        }

        return height;
    }

    /// <summary>
    /// Picks the right vertical-spacing spec for the gap between two adjacent staff groups,
    /// honouring the boundary staves' <c>staff-affinity</c>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc:240-252 — direction-aware non-staff spacing.
    /// The boundary is between the LAST staff of <paramref name="upper"/> and the FIRST
    /// staff of <paramref name="lower"/>.
    /// <para>
    /// ⚠️ WHEN BOTH ARE SPACEABLE THERE ARE THREE BRANCHES, NOT TWO
    /// (LILYPOND-REF: lily/axis-group-interface.cc:1008-1027). The spec is read off the
    /// UPPER staff — <c>get_spacing_spec</c> asks <c>before</c>
    /// (page-layout-problem.cc:1280-1281) — and describes the gap below it:
    /// a staff with no <c>staff-grouper</c> uses its own
    /// <c>default-staff-staff-spacing</c> (9), a staff that still has a live spaceable
    /// group member below it uses the grouper's <c>staff-staff-spacing</c> (9), and only
    /// the LAST live member of a grouper uses <c>staffgroup-staff-spacing</c> (10.5).
    /// Lily# had no third branch and sent every group boundary to 10.5, so two bare
    /// <c>staff</c> declarations — which carry no grouper at all — sat 1.500000 too far
    /// apart. MEASURED (audit/lp-geometry, books LYRM/LYRMV:
    /// <c>lyrics.two-staff{,.two-verse}.staff-staff-inside</c>).
    /// ⚠️ It is the UPPER group that decides, not the pair: a bare staff above a
    /// PianoStaff takes 9 as well, because the staff above has no grouper to ask.
    /// </para>
    /// </remarks>
    private static VerticalSpacingSpec SelectInterGroupSpec(
        StaffGroup upper, StaffGroup lower, StaffSpacingParameters sp)
    {
        if (upper.Staves.IsDefaultOrEmpty || lower.Staves.IsDefaultOrEmpty)
            return sp.StaffGroupStaff;

        var spaceable = upper.Type == StaffGroupType.Single
            ? sp.DefaultStaffStaff
            : sp.StaffGroupStaff;
        var before = upper.Staves[^1];
        var after = lower.Staves[0];

        // ⚠️ A LYRICS ROW TAKES LILYPOND'S SPEC SINCE 2026-07-27, and the exclusion that used
        // to stand here went with the reason for it: the row had no ink in any skyline, so a
        // spec-driven distance would have been measured against nothing and collapsed onto
        // the neighbour, and the band held it apart instead. The ink is seeded now
        // (BuildAllStaffSkylines), so the row is spaced like the Lyrics context it is —
        // ly/engraver-init.ly:648-658, staff-affinity UP and nonstaff-relatedstaff-spacing.
        // HANDOFF 3's "place it as a staff-like band" decision was revisited to get here.
        return StaffAffinity.GetSpacingSpec(
            before.StaffAffinity, NonStaffSpecsOf(before, sp),
            after.StaffAffinity, NonStaffSpecsOf(after, sp),
            spaceable);
    }

    /// <summary>
    /// Which context's <c>nonstaff-*</c> specs a line carries — the set
    /// <c>get_spacing_spec</c> reads its property out of.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly:648-658 Lyrics, :719-723 ChordNames. A spaceable
    /// staff never has one of these read off it, so it takes the Lyrics set only as a value
    /// the branches cannot reach.
    /// </remarks>
    private static StaffSpacingParameters.NonStaffSpacing NonStaffSpecsOf(
        Staff staff, StaffSpacingParameters sp)
        => staff.IsTextRow && !staff.IsLyricsTextRow ? sp.ChordNames : sp.Lyrics;

    /// <summary>
    /// The spacing spec for the pair straddling two groups, including the ossia rule.
    /// </summary>
    /// <remarks>
    /// One home for the choice, because it is now read twice: by the layout loops that
    /// place the staves and by <see cref="StaffSprings"/>, which builds the page spring
    /// that floors the same distance. Two copies of a spec selection is the shape
    /// HANDOFF 5.2.1 (2) names — the port lands on one of them and not the other.
    /// LILYPOND-REF: lily/align-interface.cc:240-252 staff-affinity-aware selection.
    /// <para>
    /// ⚠️ AN OSSIA PAIR HAS NO SPECIAL SPEC, and it had one until 2026-07-28: the pair was
    /// substituted to <c>sp.StaffStaff</c>, the GROUPER's spec. LilyPond reads the spec off
    /// the upper staff and falls through to that staff's own
    /// <c>default-staff-staff-spacing</c> when it has no <c>staff-grouper</c>
    /// (LILYPOND-REF: lily/axis-group-interface.cc:1007-1027 calc_maybe_pure_staff_staff_spacing),
    /// whose <c>grouper</c> branch returns
    /// <c>staff-staff-spacing</c> / <c>staffgroup-staff-spacing</c> and whose fall-through
    /// returns <c>default-staff-staff-spacing</c> — and an ossia is a bare <c>\new Staff</c>
    /// with no grouper — so it takes the fall-through, which
    /// <see cref="SelectInterGroupSpec"/> already selects for a <c>Single</c> group.
    /// ⚠️ THE TWO SPECS DECLARE THE SAME basic-distance 9 and differ in their MINIMA (7
    /// against 8), so no reading at rest can tell them apart and removing the substitution
    /// moves nothing until a page squeezes — MEASURED, on its own it moved no entry and no
    /// snapshot. It is half of one quantity with the spring above, not an independent change.
    /// </para>
    /// <para>
    /// ⚠️ WITH THE OSSIA BRANCH GONE THIS FORWARDS TO <see cref="SelectInterGroupSpec"/> AND
    /// NOTHING ELSE. It is kept as the name its four call sites use for "the spec for a pair
    /// straddling two groups", and because this is where that decision is recorded; merging
    /// the two is a rename and not a behaviour change, for whoever wants one name.
    /// </para>
    /// </remarks>
    private static VerticalSpacingSpec InterGroupSpec(
        StaffGroup upper, StaffGroup lower, StaffSpacingParameters sp)
        => SelectInterGroupSpec(upper, lower, sp);

    /// <summary>
    /// Estimates pure system height including content-dependent loose line extents.
    /// Used for page breaking optimization before full layout.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:138-173 pure_height
    ///
    /// Pure height = base staff spacing + the extents the caller hands in.
    /// This allows the page breaker to account for variable system heights without
    /// requiring full layout. The base height comes from CalculateSystemHeight; the extents
    /// come from LayoutEngine (the placed annotations' own protrusions, the lyric block's
    /// measured reservation, and — above the staff only — LayoutEngine.EstimateAboveStaffExtents).
    /// </remarks>
    public double CalculatePureSystemHeight(MultiStaffScore score, double looseDownExtent, double looseUpExtent)
    {
        double baseHeight = CalculateSystemHeight(score);
        return baseHeight + looseDownExtent + looseUpExtent;
    }

    /// <summary>
    /// Scale factor for ossia staves: magstep(-3) = 2^(-3/6) ≈ 0.707.
    /// LILYPOND-REF: ly/engraver-init.ly — ossia staves typically use fontSize = #-3
    /// with \override StaffSymbol.staff-space = #(magstep -3).
    /// Shared with the renderer via EngravingDefaults so reserved heights
    /// match the drawn size exactly.
    /// </summary>
    private const double OssiaScaleFactor = EngravingDefaults.OssiaScale;

    /// <summary>
    /// Gets the height of a staff in staff spaces.
    /// Standard staves have 4 staff spaces (5 lines).
    /// Tab staves have (stringCount - 1) staff spaces.
    /// Ossia staves are scaled down.
    /// </summary>
    private double GetStaffHeight(Staff staff)
    {
        if (staff.IsTab && staff.Tuning.HasValue)
        {
            int stringCount = Tunings.GetStringCount(staff.Tuning.Value);
            // Tab lines are spaced wider than a normal staff (TabStringSpace).
            return (stringCount - 1) * EngravingDefaults.TabStringSpace(stringCount); // Bass: 3 → 4.5
        }
        if (staff.IsTextRow)
            // A LYRIC row is "a staff with the lines removed": a full
            // staff-height band (plus one verse-spacing per extra verse), so
            // its barlines, spacing and neighbours behave exactly as around a
            // real staff. A chord row keeps the compact band its symbols
            // hang on.
            return staff.IsLyricsTextRow
                ? _options.StaffHeight + (staff.TextRowVerses - 1) * TextRowVerseSpacing
                : TextRowHeight;
        if (staff.IsOssia)
            return _options.StaffHeight * OssiaScaleFactor;
        return _options.StaffHeight;
    }

    /// <summary>
    /// How far below its own TOP an element's reference point sits — half a staff for a
    /// staff, and the TEXT BASELINE for a text row.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc:201-285 works between VerticalAxisGroup
    /// REFERENCE POINTS, and a group's refpoint is not in general the middle of its extent:
    /// a Lyrics or ChordNames group's is the text baseline
    /// (<c>ChordNameEngraver.ChordRowTextBaseline</c>, <c>LyricEngraver.LyricRowBaseline</c>).
    /// <para>
    /// ⚠️ THIS IS THE ONE SEAM between LilyPond's frame and Lily#'s band model, and every
    /// distance that used to assume "half of a nominal 4.0 staff" goes through it now. It
    /// reads exactly as before for two ordinary staves (2.0 + 2.0 = 4.0); what it fixes is
    /// every pair whose two elements are not the same height — a text row, an ossia, or a
    /// tab staff, the last of which HANDOFF 1 named as wrong-but-unmeasured.
    /// </para>
    /// </remarks>
    private double RefpointBelowTop(Staff staff, bool chordGridSheet)
        => staff.IsTextRow
            ? TextRowRefpointBelowTop(staff, chordGridSheet)
            : GetStaffHeight(staff) / 2.0;

    /// <summary>
    /// How far below a TEXT ROW's band top its reference point — the text baseline — sits.
    /// </summary>
    /// <remarks>
    /// ⚠️ LILYSHARP-OWN, AND IT HAS ONE HOME ON PURPOSE. LilyPond has no band: a Lyrics or
    /// ChordNames VerticalAxisGroup's refpoint IS the syllable/symbol baseline and there is
    /// nothing above it to measure from. The two constants are Lily#'s own band model, so
    /// the CHOICE between them is Lily#'s too — and a choice with two homes is
    /// HANDOFF 5.2.1②. It briefly had two: <c>LayoutEngine.ApplySolvedRowPositions</c> grew
    /// its own copy when a lyrics row started arriving there (2026-07-28), and this is where
    /// that copy went.
    /// </remarks>
    internal static double TextRowRefpointBelowTop(Staff staff, bool chordGridSheet)
        => staff.IsLyricsTextRow
            ? LyricEngraver.LyricRowBaseline
            : ChordNameEngraver.RowTextBaseline(chordGridSheet);

    /// <summary>The rest of an element's height, below its reference point.</summary>
    private double HeightBelowRefpoint(Staff staff, bool chordGridSheet)
        => GetStaffHeight(staff) - RefpointBelowTop(staff, chordGridSheet);

    /// <summary>
    /// What a refpoint-to-refpoint distance has to give up to become the gap between the
    /// UPPER element's bottom and the LOWER one's top — the frame the placement loops stack
    /// in.
    /// </summary>
    /// <remarks>
    /// ★ ASKED FOR EVERY GROUP BOUNDARY SINCE 2026-07-28. It used to be asked only where a
    /// text row was involved, and the nominal staff height stood everywhere else; the two
    /// agree for two ordinary staves (2.0 + 2.0 = 4.0) and disagree for a TAB or OSSIA pair.
    /// See <see cref="GapSpan"/> for the pair that measured it.
    /// <para>
    /// ★ THE STAFF-INTERNAL PATH TAKES IT TOO, since 2026-07-28. <see cref="StackStaves"/>
    /// used to advance by the previous staff's own HEIGHT while the gap it added came from
    /// <c>StaffGap</c> in the refpoint frame, so a pair of DIFFERENT heights inside ONE group
    /// landed half their difference out. Both paths now ask <see cref="GapSpan"/>. It was
    /// unreachable from any score — a tab or ossia staff is always a group of its own — and
    /// is pinned by <c>StaffLayoutFrameTests.UnequalStavesInOneGroup_ArePlacedCentreToCentre</c>,
    /// which builds the arrangement through the model.
    /// </para>
    /// </remarks>
    private double RefpointSpanToGap(Staff upper, Staff lower, bool chordGridSheet)
        => HeightBelowRefpoint(upper, chordGridSheet) + RefpointBelowTop(lower, chordGridSheet);

    /// <summary>
    /// The span a refpoint-to-refpoint distance gives up to become the gap this boundary is
    /// stacked with — LilyPond's frame, for every pair.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc:201-285 — <c>Align_interface</c> accumulates
    /// between VerticalAxisGroup REFERENCE POINTS, and <c>staff-staff-spacing</c>'s
    /// basic-distance is read in the PAGE's staff spaces. So the distance between two staves
    /// does not know how tall either of them is.
    /// <para>
    /// ★ IT USED TO HAND OUT THE NOMINAL 4.000000 EVERYWHERE EXCEPT A TEXT-ROW BOUNDARY
    /// (2026-07-28). That is right for two ordinary staves — <c>2.0 + 2.0</c> — and wrong for
    /// every pair whose elements are not both 4.000000 tall: a six-string TAB staff's lines
    /// span 7.500000, so its refpoint sat <c>(7.5 - 4)/2 = 1.750000</c> too far from the staff
    /// above it. MEASURED, and the pair says it in LilyPond's own numbers:
    /// <c>staff.tab-pair.staff-staff-inside</c> reads 9.000000 in LilyPond and read 10.750000
    /// here, while its control <c>staff.notation-pair.staff-staff-inside</c> is 9.000000 on
    /// both sides.
    /// </para>
    /// <para>
    /// AN OSSIA PAIR was the second half of the same island and is closed too. The span this
    /// method returns already used the ossia's own placed half-height; what remained was the
    /// caller multiplying the finished gap by <c>OssiaScaleFactor</c>, which had no
    /// counterpart — LilyPond places an ossia in the page's absolute staff spaces and scales
    /// the STAFF, not the distance. That multiplication is gone;
    /// <c>staff.ossia-pair.staff-staff-inside</c> now reads 9.000000 on both sides against
    /// its control <c>staff.ossia-control.staff-staff-inside</c>.
    /// </para>
    /// </remarks>
    private double GapSpan(MultiStaffScore score, Staff upper, Staff lower)
        => RefpointSpanToGap(upper, lower,
            ChordNameEngraver.IsChordGridSheet(score.ChordNames, score.Lyrics));

    /// <summary>
    /// The <see cref="RefpointBelowTop(Staff, bool)"/> a PLACED staff carries with it, so that
    /// the frame steps taken downstream of this file do not have to fold a nominal half staff.
    /// </summary>
    /// <remarks>
    /// The score half of the same question <see cref="GapSpan"/> asks, and it goes through the
    /// same one home: whether a book is a chord GRID decides a chord row's text baseline, and a
    /// second copy of that choice is HANDOFF 5.2.1②.
    /// <para>
    /// ★ WHY IT TRAVELS ON THE LAYOUT. <c>OutsideStaffStacker</c> holds
    /// <see cref="StaffLayout"/>s, not <c>Staff</c>es, and the step it applies to a staff's
    /// profile — "the profile is about the element's reference point, the tracker frame is
    /// system-relative Y-up" — is exactly this quantity. It folded the nominal 2.0 instead,
    /// which reads right for an ordinary staff and wrong for every element that is not one.
    /// </para>
    /// </remarks>
    private double PlacedRefpointBelowTop(MultiStaffScore score, Staff staff)
        => RefpointBelowTop(staff,
            ChordNameEngraver.IsChordGridSheet(score.ChordNames, score.Lyrics));

    /// <summary>Reserved vertical band (staff spaces) for an independent text row
    /// (chords / lyrics): a line of text (~1.5 ss tall) plus a little breathing room.</summary>
    private const double TextRowHeight = 2.5;

    /// <summary>Extra band height per additional lyrics verse. MUST match
    /// LyricEngraver's VerseSpacing, or verse 2+ leak out of the reserved
    /// band (they did, at the stale 1.8).</summary>
    /// <remarks>
    /// ⚠️ LILYSHARP-OWN, AND IT IS NOW THE LAST FLAT VERSE STEP LEFT. Where the row is an
    /// element of the loose chain (2026-07-28) the step between its verses is SOLVED at
    /// <c>max(2.8, ink + 0.2)</c> and the band's top follows verse 1, so this only over-states
    /// the band's HEIGHT. ⚠️ DO NOT DELETE IT ON THE STRENGTH OF THAT: reasoning from the
    /// chain that the band is redundant is the misdiagnosis HANDOFF 5.3 records. The port is
    /// to take the band's height from the same walk, which wants the pair first.
    /// <para>
    /// ★ THE REGIME IT IS LIVE IN IS REACHED, AND BY A REAL BOOK (2026-08-26). This used to
    /// say the over-statement "reaches nothing below the system", live "in exactly one
    /// regime, a row placed ABOVE a staff … no fixture and no corpus book has that shape".
    /// The reported book is that shape — <c>staff / chords / lyrics / staff</c>, where the
    /// lyric row IS above a staff — and the height reaches that staff with coefficient 1,
    /// because <c>LayoutStaffGroups</c> advances its running Y by each element's own
    /// height. MEASURED on scratch/ベースタブLy/Untitled-6.lys, refpoint to refpoint:
    /// <c>staff 4.000 + 2.352 + chords 2.500 + 0.600 + lyrics 7.200 + 0.000</c> = 16.652,
    /// against LilyPond's 13.024768 for the same arrangement
    /// (audit/lp-geometry lyrics.chord-lyric-run.staff-to-staff, book CHL1).
    /// </para>
    /// <para>
    /// ⚠️ AND 3.200000 OF THE 3.627 GAP IS ONE VERSE THE SYSTEM DOES NOT DRAW.
    /// <see cref="Model.Staff.TextRowVerses"/> is a SCORE-WIDE maximum
    /// (<c>MeasureCollector</c> takes <c>Math.Max</c> over every <c>VerseNumber</c> in the
    /// score), so a system whose section carries ONE verse still reserves the band for the
    /// score's deepest section: the book above draws one verse on its section-A systems and
    /// reserves 4.0 + 3.2. LilyPond has no such carry — a Lyrics context with nothing on this
    /// system is removed (<c>remove-empty</c>) and reserves nothing.
    /// ⚠️ NOT FIXED HERE, and the reason is the order: shrinking the band shrinks the ROOM
    /// the loose chain is solved into, and a room below the chain's own floors puts the rows
    /// through the staff below. The two are one port, and it wants the LP pair for a row
    /// ABOVE a staff that this remark has always said it wants.
    /// </para>
    /// </remarks>
    private const double TextRowVerseSpacing = 3.2;

    /// <summary>Gap between two adjacent TEXT rows (chord row above a lyric
    /// row): the chord symbols sit just above the lyric band, like chord
    /// names above a staff — not a whole staff-distance away.</summary>
    private const double TextRowPairGap = 0.6;

    /// <summary>How far left of the staff a system-start BRACE sits.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm SystemStartBrace (padding . 0.3)</remarks>
    private const double SystemStartBracePadding = 0.3;

    /// <summary>How far left of the staff a system-start BRACKET sits.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm SystemStartBracket (padding . 0.8)</remarks>
    private const double SystemStartBracketPadding = 0.8;

    /// <summary>
    /// Layouts all staff groups within a system.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc internal_get_minimum_translations()
    /// Uses StaffSpacingParameters for intra-group and inter-group spacing.
    /// </remarks>
    public ImmutableArray<StaffGroupLayout> LayoutStaffGroups(MultiStaffScore score)
    {
        var builder = ImmutableArray.CreateBuilder<StaffGroupLayout>();
        double currentY = 0;
        double staffHeight = _options.StaffHeight;
        var sp = _options.StaffSpacing;
        int globalStaffIndex = 0;

        for (int i = 0; i < score.StaffGroups.Length; i++)
        {
            var group = score.StaffGroups[i];

            if (group.IsGrandStaff)
            {
                var layout = LayoutGrandStaffGroup(score, group, currentY, staffHeight, sp.StaffStaff, globalStaffIndex);
                builder.Add(layout);
                currentY -= layout.Height;
            }
            else if (group.HasDelimiter)
            {
                var layout = LayoutBracketGroup(score, group, currentY, staffHeight, sp.StaffStaff, globalStaffIndex);
                builder.Add(layout);
                currentY -= layout.Height;
            }
            else
            {
                var layout = LayoutSingleStaffGroup(score, group, currentY, staffHeight, sp.StaffStaff, globalStaffIndex);
                builder.Add(layout);
                currentY -= layout.Height;
            }

            if (i < score.StaffGroups.Length - 1)
            {
                // LILYPOND-REF: lily/align-interface.cc:240-252 — staff-affinity-aware spec selection.
                var nextGroup = score.StaffGroups[i + 1];
                var spec = InterGroupSpec(group, nextGroup, sp);
                bool textRowPair = group.Staves[^1].IsTextRow && nextGroup.Staves[0].IsTextRow;
                double interGroupGap = textRowPair
                    ? TextRowPairGap
                    : spec.BasicDistance - GapSpan(score, group.Staves[^1], nextGroup.Staves[0]);
                currentY -= interGroupGap;
            }

            globalStaffIndex += group.StaffCount;
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Layouts all staff groups with hara-kiri support, WITHOUT skylines.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/hara-kiri-group-spanner.cc — consider_suicide()
    /// LILYPOND-REF: lily/align-interface.cc internal_get_minimum_translations()
    /// <para>
    /// ⚠️ THE ESTIMATE, NOT THE PLACEMENT — see <see cref="StackStaves"/>: with no skylines
    /// every gap is the spec's basic-distance, which cannot see ink that needs more room.
    /// The render path calls the overload that takes a <see cref="SkylineBuilder"/>.
    /// </para>
    /// </remarks>
    public ImmutableArray<StaffGroupLayout> LayoutStaffGroups(
        MultiStaffScore score,
        int startMeasure, int endMeasure, bool isFirstSystem)
        => LayoutStaffGroups(score, staffSkylines: null,
            staff => HaraKiri.ShouldHideStaff(staff, startMeasure, endMeasure, isFirstSystem));

    /// <summary>
    /// The staff-stacking loop for one group: places each SURVIVING staff at its real
    /// height (<see cref="GetStaffHeight"/> — a tab/ossia staff differs from the nominal
    /// staffHeight) one spacing below the previous survivor, and gives each staff that
    /// committed hara-kiri zero height at the current Y. Returns the builder plus the
    /// running top-of-last-staff Y (<paramref name="currentY"/>) and whether any staff
    /// survived (<paramref name="anyVisible"/>). The grand/single/bracket helpers share
    /// this and differ only in their delimiter tail.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc:217-268 internal_get_minimum_translations,
    /// which walks the alignment's elements and skips the dead ones (:90 hands an empty
    /// skyline back for a dead group). There is ONE such walk in LilyPond, so there is one
    /// here: <paramref name="isDead"/> is the whole of hara-kiri's effect on placement.
    /// <para>
    /// ⚠️ THE GAP IS MEASURED BETWEEN SURVIVORS, not between neighbours in the model. When
    /// the staff above died, the pair that must clear each other is this staff and the last
    /// one still standing, which is the pair LilyPond's element list leaves adjacent.
    /// </para>
    /// <para>
    /// ⚠️ <paramref name="staffSkylines"/> null falls back to the spec's basic-distance,
    /// which is LilyPond's PURE estimate rather than its placement — <c>align-interface.cc
    /// :234-238</c> is the same fallback, reached when <c>get_pure_minimum_translations</c>
    /// calls with <c>INT_MAX == end &amp;&amp; 0 == start</c>. It cannot see that the ink
    /// between two staves needs more room than the spec asks for, so it is only for callers
    /// that have no measure layouts to build skylines from. The RENDER path never takes it:
    /// as of 2026-07-26 the only callers of the two skyline-less overloads are tests.
    /// </para>
    /// </remarks>
    private ImmutableArray<StaffLayout>.Builder StackStaves(
        MultiStaffScore score,
        StaffGroup group, double y, VerticalSpacingSpec staffSpec, int startIndex,
        List<(VerticalSkyline Up, VerticalSkyline Down)>? staffSkylines,
        Func<Staff, bool> isDead,
        out double currentY, out bool anyVisible,
        // ⚠️ A PAIR INSIDE ONE GROUP CAN HAVE LINES BETWEEN IT, since a note-bound lyric
        // hangs off its OWN staff rather than off its group. While it hung off the group
        // this argument could not exist — nothing was ever between two staves of one group —
        // and StackStaves called StaffGap without it. Leaving it out now places the staves
        // at a gap the block does not fit in, and the syllables are drawn over the staff
        // below (MEASURED on がくふ: four rows placed, none given room).
        LooseLinesBetween? looseLines = null)
    {
        var staffLayouts = ImmutableArray.CreateBuilder<StaffLayout>();
        currentY = y;
        anyVisible = false;
        int lastVisibleIndex = -1;
        double lastVisibleHeight = 0;
        Staff? lastVisibleStaff = null;

        for (int i = 0; i < group.Staves.Length; i++)
        {
            var staff = group.Staves[i];
            int globalIndex = startIndex + i;
            double thisStaffHeight = GetStaffHeight(staff);

            if (isDead(staff))
            {
                staffLayouts.Add(new StaffLayout(
                    StaffIndex: globalIndex,
                    Clef: staff.Clef,
                    Y: currentY,
                    Height: 0,
                    Tuning: staff.Tuning,
                    InstrumentName: staff.InstrumentName,
                    IsOssia: staff.IsOssia,
                    IsHidden: true,
                    StaffAffinity: staff.StaffAffinity,
                    RefpointBelowTop: PlacedRefpointBelowTop(score, staff)));
                continue;
            }

            if (anyVisible)
            {
                // ⚠️ The PREVIOUS staff's height, not this one's. Advancing by the height of
                // the staff about to be placed misplaces every group whose staves are not
                // all the same height (an ossia or tab staff under a normal one); it was
                // invisible while this loop only ever ran on equal-height staves.
                // ★ AND THE SPAN IS THE REFPOINT SPAN, not that height (2026-07-28). What
                // StaffGap subtracts is the distance from the upper staff's REFERENCE POINT
                // down to the lower staff's — the frame every alignment distance is in
                // (LILYPOND-REF: lily/align-interface.cc:217-268 internal_get_minimum_translations,
                // whose dy runs between reference points). Passing the upper staff's whole
                // height instead made this loop treat a centre-to-centre distance as a
                // top-to-top one, so two staves of DIFFERENT heights in one group landed
                // (lower - upper) / 2 out — 1.750000 for a six-string tab staff over an
                // ordinary one. The inter-group path already passed the span (GapSpan), so the
                // same parameter carried two different quantities depending on the caller;
                // that is what forced StaffSprings to RECONSTRUCT the alignment minimum
                // instead of asking for it (HANDOFF 5.2.1②).
                // ⚠️ THE SAME GapSpan THE INTER-GROUP PATH USES, score and all. Writing
                // RefpointSpanToGap(.., chordGridSheet: false) here instead would be folding
                // an evaluated result — "a text row is never in this loop, because RenderSpec
                // gives every chords/lyrics track a Single group of its own" is an invariant
                // of ANOTHER FILE, and asserting one of those in a comment is the shape
                // HANDOFF 5.2 names. Ask for the quantity; do not decide it here.
                double span = GapSpan(score, lastVisibleStaff!, staff);
                double gap = StaffGap(
                    staffSpec, span, staffSkylines, lastVisibleIndex, globalIndex,
                    looseLines?.Invoke(lastVisibleIndex, globalIndex));
                currentY -= lastVisibleHeight + gap;
            }

            staffLayouts.Add(new StaffLayout(
                StaffIndex: globalIndex,
                Clef: staff.Clef,
                Y: currentY,
                Height: thisStaffHeight,
                Tuning: staff.Tuning,
                InstrumentName: staff.InstrumentName,
                IsOssia: staff.IsOssia,
                StaffAffinity: staff.StaffAffinity,
                RefpointBelowTop: PlacedRefpointBelowTop(score, staff)));
            anyVisible = true;
            lastVisibleIndex = globalIndex;
            lastVisibleHeight = thisStaffHeight;
            lastVisibleStaff = staff;
        }

        return staffLayouts;
    }

    /// <summary>
    /// The gap between the bottom of one staff and the top of the next: the skyline-aware
    /// distance when skylines exist, and the spec's basic-distance when they do not. See
    /// <see cref="StackStaves"/> for when the fallback is taken.
    /// </summary>
    /// <param name="refpointSpan">
    /// From the upper staff's REFERENCE POINT down to the lower staff's — what has to come
    /// off a centre-to-centre alignment distance to leave a gap between their edges.
    /// ⚠️ ONE QUANTITY AT BOTH CALL SITES since 2026-07-28. The stacking loop used to pass
    /// the upper staff's whole HEIGHT here while the inter-group path passed the span, so the
    /// same parameter meant two things and only agreed while every pair had equal heights.
    /// </param>
    private static double StaffGap(
        VerticalSpacingSpec spec, double refpointSpan,
        List<(VerticalSkyline Up, VerticalSkyline Down)>? staffSkylines,
        int upperStaffIndex, int lowerStaffIndex,
        IReadOnlyList<PairLooseLine>? looseLines = null)
        => staffSkylines is null
            ? Math.Max(0, spec.BasicDistance - refpointSpan)
            : CalculateStaffGapWithSkylines(
                spec, refpointSpan, staffSkylines, upperStaffIndex, lowerStaffIndex,
                looseLines);

    /// <summary>Nothing ever dies — the filter for a score with no hara-kiri in it.</summary>
    private static readonly Func<Staff, bool> NothingDies = _ => false;

    /// <summary>Height of the last visible staff in a stacked group (0 if none).</summary>
    private static double LastVisibleStaffHeight(ImmutableArray<StaffLayout>.Builder staffLayouts)
    {
        for (int i = staffLayouts.Count - 1; i >= 0; i--)
            if (!staffLayouts[i].IsHidden)
                return staffLayouts[i].Height;
        return 0;
    }

    /// <summary>
    /// Layouts a grand staff group (piano/organ style with brace) from the placed staves.
    /// </summary>
    private StaffGroupLayout LayoutGrandStaffGroupWithSkylines(
        MultiStaffScore score,
        StaffGroup group, double y, VerticalSpacingSpec staffSpec, int startIndex,
        List<(VerticalSkyline Up, VerticalSkyline Down)>? staffSkylines, Func<Staff, bool> isDead,
        LooseLinesBetween? looseLines = null)
    {
        var staffLayouts = StackStaves(
            score, group, y, staffSpec, startIndex, staffSkylines, isDead,
            out double currentY, out bool anyVisible, looseLines);

        if (!anyVisible)
        {
            // Every staff in the group committed hara-kiri — a zero-height group, which is
            // what leaves it out of the system's extent (see SystemHeightOf).
            return StaffGroupLayout.CreateGrandStaff(
                staffLayouts.ToImmutable(), y, 0,
                new GrandStaffLayout(staffLayouts.ToImmutable(), 0, 0, 0));
        }

        double totalHeight = y - currentY + LastVisibleStaffHeight(staffLayouts);
        double braceX = CurrentIndent - SystemStartBracePadding;

        var grandStaffLayout = new GrandStaffLayout(
            Staves: staffLayouts.ToImmutable(),
            BraceX: braceX,
            BraceTop: y,
            BraceBottom: y - totalHeight);

        return StaffGroupLayout.CreateGrandStaff(
            staffLayouts.ToImmutable(), y, totalHeight, grandStaffLayout);
    }

    /// <summary>
    /// Layouts a single staff group from the placed staves.
    /// </summary>
    private StaffGroupLayout LayoutSingleStaffGroupWithSkylines(
        MultiStaffScore score,
        StaffGroup group, double y, VerticalSpacingSpec staffSpec, int startIndex,
        List<(VerticalSkyline Up, VerticalSkyline Down)>? staffSkylines, Func<Staff, bool> isDead,
        LooseLinesBetween? looseLines = null)
    {
        var staffLayouts = StackStaves(
            score, group, y, staffSpec, startIndex, staffSkylines, isDead,
            out double currentY, out bool anyVisible, looseLines);

        if (!anyVisible)
            return StaffGroupLayout.CreateSingle(staffLayouts[0], y, 0);

        double totalHeight = y - currentY + LastVisibleStaffHeight(staffLayouts);

        return StaffGroupLayout.CreateSingle(staffLayouts[0], y, totalHeight);
    }

    /// <summary>
    /// Layouts a bracket group from the placed staves.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/system-start-delimiter.cc — bracket rendering with collapse-height
    /// </remarks>
    private StaffGroupLayout LayoutBracketGroupWithSkylines(
        MultiStaffScore score,
        StaffGroup group, double y, VerticalSpacingSpec staffSpec, int startIndex,
        List<(VerticalSkyline Up, VerticalSkyline Down)>? staffSkylines, Func<Staff, bool> isDead,
        LooseLinesBetween? looseLines = null)
    {
        var staffLayouts = StackStaves(
            score, group, y, staffSpec, startIndex, staffSkylines, isDead,
            out double currentY, out bool anyVisible, looseLines);

        if (!anyVisible)
        {
            return StaffGroupLayout.CreateBracketGroup(
                group.Type,
                staffLayouts.ToImmutable(), y, 0,
                new GrandStaffLayout(staffLayouts.ToImmutable(), 0, 0, 0, SystemStartDelimiterType.Bracket));
        }

        double totalHeight = y - currentY + LastVisibleStaffHeight(staffLayouts);
        double bracketX = CurrentIndent - SystemStartBracketPadding;

        var delimiterLayout = new GrandStaffLayout(
            Staves: staffLayouts.ToImmutable(),
            BraceX: bracketX,
            BraceTop: y,
            BraceBottom: y - totalHeight,
            DelimiterType: SystemStartDelimiterType.Bracket);

        return StaffGroupLayout.CreateBracketGroup(
            group.Type,
            staffLayouts.ToImmutable(), y, totalHeight, delimiterLayout);
    }

    /// <summary>
    /// Layouts a grand staff group (piano/organ style with brace).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:3352-3355 staff-staff-spacing
    /// </remarks>
    private StaffGroupLayout LayoutGrandStaffGroup(
        MultiStaffScore score,
        StaffGroup group, double y, double staffHeight, VerticalSpacingSpec staffSpec, int startIndex)
    {
        var staffLayouts = ImmutableArray.CreateBuilder<StaffLayout>();
        double currentY = y;

        // Intra-group spacing: staff-staff basic distance is center-to-center
        double staffSpacing = staffSpec.BasicDistance - staffHeight;

        for (int i = 0; i < group.Staves.Length; i++)
        {
            var staff = group.Staves[i];
            staffLayouts.Add(new StaffLayout(
                StaffIndex: startIndex + i,
                Clef: staff.Clef,
                Y: currentY,
                Height: staffHeight,
                Tuning: staff.Tuning,
                InstrumentName: staff.InstrumentName,
                StaffAffinity: staff.StaffAffinity,
                RefpointBelowTop: PlacedRefpointBelowTop(score, staff)));

            if (i < group.Staves.Length - 1)
                currentY -= staffHeight + Math.Max(0, staffSpacing);
        }

        double totalHeight = y - currentY + staffHeight;
        double braceX = CurrentIndent - SystemStartBracePadding;

        var grandStaffLayout = new GrandStaffLayout(
            Staves: staffLayouts.ToImmutable(),
            BraceX: braceX,
            BraceTop: y,
            BraceBottom: y - totalHeight);

        return StaffGroupLayout.CreateGrandStaff(
            staffLayouts.ToImmutable(),
            y,
            totalHeight,
            grandStaffLayout);
    }

    /// <summary>
    /// Layouts a single staff or bracket group.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:3352-3355 staff-staff-spacing
    /// </remarks>
    private StaffGroupLayout LayoutSingleStaffGroup(
        MultiStaffScore score,
        StaffGroup group, double y, double staffHeight, VerticalSpacingSpec staffSpec, int startIndex)
    {
        var staffLayouts = ImmutableArray.CreateBuilder<StaffLayout>();
        double currentY = y;

        // Intra-group spacing: staff-staff basic distance is center-to-center
        double staffSpacing = staffSpec.BasicDistance - staffHeight;

        for (int i = 0; i < group.Staves.Length; i++)
        {
            var staff = group.Staves[i];
            double thisStaffHeight = GetStaffHeight(staff);
            staffLayouts.Add(new StaffLayout(
                StaffIndex: startIndex + i,
                Clef: staff.Clef,
                Y: currentY,
                Height: thisStaffHeight,
                Tuning: staff.Tuning,
                InstrumentName: staff.InstrumentName,
                IsOssia: staff.IsOssia,
                StaffAffinity: staff.StaffAffinity,
                RefpointBelowTop: PlacedRefpointBelowTop(score, staff)));

            if (i < group.Staves.Length - 1)
                currentY -= thisStaffHeight + Math.Max(0, staffSpacing);
        }

        double lastStaffHeight = GetStaffHeight(group.Staves[^1]);
        double totalHeight = group.StaffCount == 1
            ? lastStaffHeight
            : y - currentY + lastStaffHeight;

        return StaffGroupLayout.CreateSingle(
            staffLayouts[0],
            y,
            totalHeight);
    }

    /// <summary>
    /// Layouts a bracket group (StaffGroup or ChoirStaff with bracket delimiter).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/system-start-delimiter.cc — bracket rendering
    /// LILYPOND-REF: ly/engraver-init.ly — StaffGroup/ChoirStaff use SystemStartBracket
    /// </remarks>
    private StaffGroupLayout LayoutBracketGroup(
        MultiStaffScore score,
        StaffGroup group, double y, double staffHeight, VerticalSpacingSpec staffSpec, int startIndex)
    {
        var staffLayouts = ImmutableArray.CreateBuilder<StaffLayout>();
        double currentY = y;
        double staffSpacing = staffSpec.BasicDistance - staffHeight;

        for (int i = 0; i < group.Staves.Length; i++)
        {
            var staff = group.Staves[i];
            double thisStaffHeight = GetStaffHeight(staff);
            staffLayouts.Add(new StaffLayout(
                StaffIndex: startIndex + i,
                Clef: staff.Clef,
                Y: currentY,
                Height: thisStaffHeight,
                Tuning: staff.Tuning,
                InstrumentName: staff.InstrumentName,
                IsOssia: staff.IsOssia,
                StaffAffinity: staff.StaffAffinity,
                RefpointBelowTop: PlacedRefpointBelowTop(score, staff)));

            if (i < group.Staves.Length - 1)
                currentY -= thisStaffHeight + Math.Max(0, staffSpacing);
        }

        double lastStaffHeight = GetStaffHeight(group.Staves[^1]);
        double totalHeight = y - currentY + lastStaffHeight;
        double bracketX = CurrentIndent - SystemStartBracketPadding;

        var delimiterLayout = new GrandStaffLayout(
            Staves: staffLayouts.ToImmutable(),
            BraceX: bracketX,
            BraceTop: y,
            BraceBottom: y - totalHeight,
            DelimiterType: SystemStartDelimiterType.Bracket);

        return StaffGroupLayout.CreateBracketGroup(
            group.Type,
            staffLayouts.ToImmutable(),
            y,
            totalHeight,
            delimiterLayout);
    }

    /// <summary>
    /// The line-start spring a system's opening measure gets: its bar-line spring 0
    /// REPLACED by the prefix→first-note spring, floored at LilyPond's 0.3 + min_dist.
    /// The ONE implementation of that spring, shared by the actual layout (LayoutMeasures)
    /// and the line-break gate (SystemBreaker.ComputeMultiStaffSpringData), so section 5.4's
    /// "the two spring systems and the break gate must agree" holds at the line start by
    /// construction rather than by a re-derivation that can drift.
    /// </summary>
    /// <remarks>
    /// Self-contained from the score and the line's first measure so the gate can call it
    /// per measure: it recomputes the prefix reservation (key ink, whether the prefix
    /// carries the meter, the break-align columns) that LayoutMeasures also computes for the
    /// prefix width — the SAME functions on the SAME inputs, so the numbers are identical and
    /// substituting this call for the inline block is output-invariant for the layout.
    /// LILYPOND-REF: lily/spacing-spanner.cc:219-224 — LilyPond generates the spacing for the
    ///   PREBROKEN pieces of a column, so a candidate line start is priced with the spring it
    ///   would really get. The spring itself is staff-spacing.cc:210-220 via
    ///   <see cref="LineStartColumn.SpringWithMinimumDistanceFloor"/>.
    /// </remarks>
    /// <param name="isFirstSystem">systemIndex == 0. The first line carries the opening meter
    /// in its prefix; a continuation hoists only a meter CHANGE that opens it.</param>
    /// <param name="measureSpring0">The opening measure's own spring 0 — the bar-line spring
    /// the substitution replaces, whose minimum still floors the FIXED distance (the leading
    /// grace / lyric widths Lily# prices here that LilyPond puts in separate paper columns;
    /// MEASURED 2026-07-25: dropping it moves 21 snapshots — grace-notes, lyric-break-pricing,
    /// lead-sheet-lyrics, chorale, ornaments — and is inert on a plain line start).</param>
    /// <param name="lyricLeadingFloor">The column-0 leading lyric width of a line that
    /// CONTINUES from the previous measure (<see cref="LyricSpacing.LineStartLyricFloor"/>),
    /// or 0. Since the cross-bar rod port (2026-08-20) that width no longer rides
    /// <paramref name="measureSpring0"/>'s minimum — mid-line the rod owns it — so a line
    /// START must be handed it separately or a broken word's opening syllable loses its
    /// prefix clearance (the lyric-break-pricing snapshot family).</param>
    internal static Spring LineStartSpringForLine(
        MultiStaffScore score, int startMeasureIndex, bool isFirstSystem, Spring measureSpring0,
        double lyricLeadingFloor = 0)
    {
        var prefix = SolveLineStartPrefix(score, startMeasureIndex, isFirstSystem);

        // When an opening CHANGE is hoisted into the prefix, its hang-left width is no
        // longer reserved in the measure, so the bare space-alist fixed distance holds;
        // otherwise the measure's own spring-0 minimum still floors it (min_dist does not
        // cover the leading grace / lyric widths — LilyPond puts those in their own paper
        // columns; an accidental on the first note DOES reach min_dist, probe TKA +1.55).
        // A KEY change hoists exactly like a meter change: it is drawn in the prefix and, as
        // a courtesy, after the previous line's final bar line, so leaving the measure's
        // spring-0 minimum in place charged its column width A SECOND time and pushed the
        // first note right by the whole cancellation+signature (measured: 5.51 ss on
        // scratch/repro.lys bar 9, where the courtesy is 3 naturals + 3 flats = 5.39 ink).
        // The lyric floor joins by max, not by replacement: a non-continuing line's leading
        // half still lives inside spring 0's minimum, so the max is idempotent there. The
        // hoisted-change branch stays null — the pre-port behaviour, where the whole spring-0
        // minimum (lyric bump included) was ignored to avoid double-charging the change.
        double? ownFixedFloor =
            prefix.LeadingTimeChange != null || prefix.LeadingKeyChange != null
                ? null
                : Math.Max(measureSpring0.MinDistance, lyricLeadingFloor);

        // ONE Staff_spacing wish per staff, merged — spacing-spanner.cc:492-517. The staves
        // need NOT agree: a NUMBERS-ONLY tab staff ends its prefix on the TAB clef
        // (minimum-fixed-space 5.0) where its notation neighbour ends on the meter
        // (semi-shrink-space 2.0), and merge_springs averages the two ideals. A full-notation
        // tab engraves the meter too and wishes the same as its neighbour.
        return LineStartColumn.LineStartSpring(
            score, prefix.Columns, SpacingRules.ClefGroupInkLeft(score),
            prefix.HasTime
                ? GlyphMetrics.GetTimeSigWidth(prefix.Numerator, prefix.Denominator)
                : 0.0,
            startMeasureIndex, ownFixedFloor);
    }

    /// <summary>A system's solved line-start break-align table plus the inputs it was
    /// solved from (the hoisted meter change, whether a meter is engraved at all, and
    /// the meter the prefix shows).</summary>
    internal readonly record struct LineStartPrefix(
        BreakAlignSpacing.PrefixColumns Columns,
        TimeSignatureChangeItem? LeadingTimeChange,
        bool HasTime,
        // The meter's two rows AS PRINTED. Strings rather than beat counts because the
        // reservation lays out the same run the pen draws (MeterGlyphRun) — an additive
        // numerator has no integer that spells it, and before session 164 the carrier
        // approximated one (TimeSignature.LayoutBeats, now retired).
        string Numerator,
        string Denominator,
        // The key change that OPENS this system's first measure, or null. Like the meter
        // change above it is engraved in the PREFIX (and as a courtesy at the end of the
        // previous line), never as a column inside bar one — so its width must not be
        // reserved twice. SharedRenderer.GetSystemStartKeyChange finds the same item.
        KeySignatureChangeItem? LeadingKeyChange = null);

    /// <summary>
    /// Solves the line-start break-align column table for the system opening at
    /// <paramref name="startMeasureIndex"/> — ONE derivation shared by the spring model
    /// (<see cref="LineStartSpringForLine"/>), the measure layout
    /// (<see cref="LayoutMeasures"/>) and the metronome mark's break-align X
    /// (<c>MusicMarkEngraver</c>), so the reserved, drawn and annotated prefix cannot
    /// drift apart (three hand-rolled copies is how they would).
    /// </summary>
    internal static LineStartPrefix SolveLineStartPrefix(
        MultiStaffScore score, int startMeasureIndex, bool isFirstSystem)
    {
        var primaryVoice = score.PrimaryContentStaff.PrimaryVoice;
        // The key signature is reprinted at every system head, reflecting any mid-piece
        // change in force before this system. The KeySignature break-align group's extent
        // is the union of the signatures the system's staves ENGRAVE
        // (break-alignment-interface.cc:141-142).
        double activeKeyInk = SpacingRules.WidestActiveKeyInk(score, startMeasureIndex);

        // A meter change that OPENS a continuation system is drawn in the prefix (clef, key,
        // THEN time); the first system carries the initial meter. A score no row of which
        // engraves a meter stencil — chords/lyrics only, or nothing but `tab … as numbers`
        // staves — hoists none.
        TimeSignatureChangeItem? leadingTimeChange = null;
        if (SpacingRules.AnyStaffEngravesTime(score)
            && !isFirstSystem && startMeasureIndex < primaryVoice.Measures.Length)
            foreach (var item in primaryVoice.Measures[startMeasureIndex].Items)
            {
                if (item is TimeSignatureChangeItem tc) { leadingTimeChange = tc; break; }
                if (item.Duration > Fraction.Zero) break;
            }

        // A key change that OPENS a continuation system is likewise engraved break-aligned in
        // the prefix — the NEW signature there, the cancellation as a courtesy at the end of
        // the PREVIOUS line (SharedRenderer.GetSystemStartKeyChange / GetSystemEndKeyChange,
        // and KeyCourtesySuffixWidth for the suffix). It is therefore not a column inside bar
        // one, exactly as the meter change is not.
        KeySignatureChangeItem? leadingKeyChange = null;
        if (!isFirstSystem && startMeasureIndex < primaryVoice.Measures.Length)
            foreach (var item in primaryVoice.Measures[startMeasureIndex].Items)
            {
                if (item is KeySignatureChangeItem kc) { leadingKeyChange = kc; break; }
                if (item.Duration > Fraction.Zero) break;
            }

        // …and no meter is booked when NO row engraves one (a chords / lyrics-only system, or
        // an all-`as numbers` tab score): SpacingRules.AnyStaffEngravesTime.
        bool prefixHasTime = SpacingRules.AnyStaffEngravesTime(score)
                             && (isFirstSystem || leadingTimeChange != null);
        // The break-align GROUP's width places the shared meter column; each staff's own clef
        // ink inside that group is what its own first-note wish is measured from.
        double maxClefWidth = SpacingRules.MaxClefWidth(score);
        string prefixNumerator =
            leadingTimeChange?.NewTime.NumeratorText ?? score.TimeSignature.NumeratorText;
        string prefixDenominator =
            leadingTimeChange?.NewTime.DenominatorText ?? score.TimeSignature.DenominatorText;
        // The break-align table itself, not just its right edge: the min_dist needs every
        // column's X to place the prefatory boxes (staff-spacing.cc:210).
        var prefixColumns = BreakAlignSpacing.SolvePrefixColumns(
            maxClefWidth, activeKeyInk, prefixHasTime, prefixNumerator, prefixDenominator);
        return new LineStartPrefix(
            prefixColumns, leadingTimeChange, prefixHasTime, prefixNumerator, prefixDenominator,
            leadingKeyChange);
    }


    /// <summary>
    /// Layouts measures for multi-staff scores with timing-based column information.
    /// Supports measure ranges for system breaking and proportional justification.
    /// </summary>
    public ImmutableArray<MeasureLayout> LayoutMeasures(
        MultiStaffScore score, int systemIndex,
        int startMeasureIndex = 0, int? measureCount = null,
        bool isLastSystem = false,
        double? baseShortestDuration = null)
    {
        var primaryVoice = score.PrimaryContentStaff.PrimaryVoice;
        int endMeasureIndex = measureCount.HasValue
            ? startMeasureIndex + measureCount.Value
            : primaryVoice.Measures.Length;

        // The line-start break-align table (key union, hoisted meter change, widest clef)
        // — see SolveLineStartPrefix, the ONE derivation this and the spring model share.
        var prefix = SolveLineStartPrefix(score, startMeasureIndex, systemIndex == 0);
        var prefixColumns = prefix.Columns;
        double prefixWidth = prefixColumns.Right;
        // LILYPOND-REF: scm/output-lib.scm — system-start-text::calc-x-offset
        // System-internal coordinates are LINE-RELATIVE (0 = line start); the page
        // places the whole line at MarginLeft once, via the renderer's margin
        // translate. So startX must NOT include MarginLeft — baking it in here AND
        // translating again double-counts the left margin, shoving the music right
        // until the final barline hits the page edge (no right margin).
        double startX = CurrentIndent + prefixWidth;
        double availableWidth = _options.PageWidth - _options.MarginLeft - _options.MarginRight - CurrentIndent - prefixWidth;

        // End-of-line courtesy: the NEXT measure (first of the next system) opening with a
        // key and/or time change reserves room after this line's final barline for the
        // cancellation + new signature, and for the new meter after it.
        // ⚠️ BOTH CAN OPEN THE SAME MEASURE, so this walk must not stop at the first one it
        // recognises — it reads the leading zero-duration items until real music starts.
        // LILYPOND-REF: explicitKeySignatureVisibility default all-visible;
        //   scm/define-grobs.scm:3922-3953 TimeSignature break-visibility all-visible.
        if (endMeasureIndex < primaryVoice.Measures.Length)
        {
            KeySignatureChangeItem? leadKey = null;
            TimeSignatureChangeItem? leadTime = null;
            foreach (var lead in primaryVoice.Measures[endMeasureIndex].Items)
            {
                if (lead is KeySignatureChangeItem kcNext) leadKey ??= kcNext;
                else if (lead is TimeSignatureChangeItem tcNext) leadTime ??= tcNext;
                if (lead.Duration > Fraction.Zero)
                    break;
            }
            if (leadKey is not null)
                availableWidth -= SpacingRules.KeyCourtesySuffixWidth(
                    score, startMeasureIndex, endMeasureIndex, meterFollows: leadTime is not null);
            if (leadTime is { } t)
                availableWidth -= SpacingRules.TimeCourtesySuffixWidth(
                    t, afterCourtesyKey: leadKey is not null);
        }

        // LILYPOND-REF: lily/spacing-spanner.cc — collect springs from ALL columns across
        // the entire system, then solve with a single SpringSolver for uniform force.

        // First pass: collect timing springs and barline widths per measure
        var measureSprings = new List<ImmutableArray<Spring>>();
        var measureTimings = new List<List<Fraction>>();
        var measureAllMeasures = new List<List<Measure>>();
        var measureBarlineWidths = new List<double>();
        double totalBarlineWidth = 0;
        // Per measure, per column: how far that column's own ink reaches PAST the column, on
        // each side — LilyPond's keep_inside_line_ = col->extent (col, X_AXIS), negated on
        // the left. Not symmetric: a note head reaches its full width right and nothing left,
        // while a centred chord symbol reaches half its width both ways.
        var measureColumnOverhangs = new List<(double[] Left, double[] Right)>();
        // Per measure: its lyric lines' edge geometry, for the cross-bar lyric rods below
        // (empty on every measure of an unsung score — the whole machinery is inert there).
        var measureLineEdges = new List<ImmutableArray<LyricSpacing.LyricLineEdge>>();

        // A compressed multi-measure rest is ONE bar between two bar-line columns,
        // so the measures a run swallows contribute neither springs nor bar lines;
        // the run-opening measure carries the whole bar and takes the run rod below.
        var runMap = MmrRunMap.Build(MultiMeasureRestEngraver.FindRuns(score));

        for (int i = startMeasureIndex; i < endMeasureIndex; i++)
        {
            var primaryMeasure = primaryVoice.Measures[i];
            var allTimings = CollectAllTimingsForMeasure(score, i);
            var allMeasures = CollectAllMeasuresAtIndex(score, i);

            if (runMap.IsInterior(i))
            {
                measureSprings.Add(ImmutableArray<Spring>.Empty);
                measureTimings.Add(allTimings);
                measureAllMeasures.Add(allMeasures);
                measureBarlineWidths.Add(0);
                measureColumnOverhangs.Add(
                    (System.Array.Empty<double>(), System.Array.Empty<double>()));
                measureLineEdges.Add(ImmutableArray<LyricSpacing.LyricLineEdge>.Empty);
                continue;
            }

            // The trailing clef column (Measure.IsTrailingClefColumn) shares the
            // PREVIOUS measure's closing bar moment: no springs, no bar, no width.
            // Its clef draws hanging back into that measure's closing gap, which
            // CreateTimingSprings reserved via BoundaryClefAllowance.
            if (primaryMeasure.IsTrailingClefColumn)
            {
                measureSprings.Add(ImmutableArray<Spring>.Empty);
                measureTimings.Add(allTimings);
                measureAllMeasures.Add(allMeasures);
                measureBarlineWidths.Add(0);
                measureColumnOverhangs.Add(
                    (System.Array.Empty<double>(), System.Array.Empty<double>()));
                measureLineEdges.Add(ImmutableArray<LyricSpacing.LyricLineEdge>.Empty);
                continue;
            }

            // The next measure is passed so a clef change opening it can be charged to
            // THIS measure's closing spring — LilyPond draws it before the shared bar
            // line. SystemBreaker mirrors this; the two must agree (SpacingInvariantTests).
            var nextMeasure = i + 1 < primaryVoice.Measures.Length
                ? primaryVoice.Measures[i + 1] : null;
            var springs = _measureLayouter.CreateTimingSprings(
                primaryMeasure, allTimings, baseShortestDuration, allMeasures, nextMeasure,
                CollectStaffIndicesAtIndex(score, i));

            // An empty placeholder measure (`| |`) has no timing springs at all —
            // without a floor it collapses to its barlines and reads as a double
            // barline. Give it one RIGID spring at the empty-bar slot width, so it
            // renders as a visible measure (matching the ideal-width floor the line
            // breaker uses — see SpacingRules.EmptyPlaceholderContentWidth).
            if (springs.Length == 0 && primaryMeasure.IsEmptyPlaceholder)
            {
                double slot = SpacingRules.EmptyPlaceholderContentWidth();
                springs = ImmutableArray.Create(new Spring(slot, slot, 0));
            }

            // Lyric, chord, tab-digit and wide-script widths land on the SHARED
            // columns through the one reservation list the break gate reads too
            // (ApplySharedColumnReservations) — a bar must be priced for breaking
            // exactly as it will be laid out. Applied before the FirstNoteSpring
            // tweak below, which Math.Max-preserves any widened minimum.
            springs = ApplySharedColumnReservations(
                score, i, springs, primaryMeasure, allTimings, allMeasures);

            // LINE-START measure: spring 0 is the prefix→first-note spacing
            // (space-alist of the last prefix item), not the mid-line
            // BarLine semi-shrink. The prefix width itself ends at the ink.
            // LILYPOND-REF: scm/define-grobs.scm Clef/KeySignature/
            //   TimeSignature space-alist (first-note . ...).
            // Each column's own centred ink, for the keep-inside-line rods below (every
            // syllable on its TIMING column — the one map since 2026-08-20).
            // Where a CENTER-aligned grob stands on each column — the note heads'/rests'
            // centre, or the placeholder's when the column has neither. The syllable reaches
            // from there, not from the column.
            // LILYPOND-REF: lily/self-alignment-interface.cc:121-139.
            var alignmentEdges = SpacingRules.ParentAlignmentEdgesPerColumn(
                allMeasures, allTimings);
            // The per-voice edge a lyric actually aligns on; the union above is its
            // fallback (rows, the placeholder). See LyricSpacing.OwnVoiceEdgeProvider —
            // memoized per score, and not asked for at all on an unsung book.
            var ownEdge = score.Lyrics.IsDefaultOrEmpty
                ? null : LyricSpacing.OwnVoiceEdgeProvider(score);
            var (lyricLeft, lyricRight) = LyricSpacing.InkReachPerColumn(
                score.TextMetrics, springs, allTimings, i, score.Lyrics, score.IsLeadSheet,
                alignmentEdges, ownEdge);
            // The measure's lyric line edges, for the cross-bar rods below and the
            // line-start lyric floor — read off the FINAL reserved springs, the same
            // chain ApplyRods will span.
            measureLineEdges.Add(score.Lyrics.IsDefaultOrEmpty
                ? ImmutableArray<LyricSpacing.LyricLineEdge>.Empty
                : LyricSpacing.MeasureLineEdges(
                    score.TextMetrics, springs, allTimings, i, score.Lyrics,
                    score.IsLeadSheet, alignmentEdges, ownEdge));
            var chordWidth = SpacingRules.ChordInkRightReachPerColumn(score.TextMetrics, 
                allTimings, i, score.ChordNames, includeAttached: !score.IsLeadSheet);
            var leftOverhangs = new double[allTimings.Count];
            var rightOverhangs = new double[allTimings.Count];
            for (int c = 0; c < leftOverhangs.Length; c++)
            {
                // A syllable is centred on its column's alignment extent, so it reaches
                // w/2 - he.centre left and w/2 + he.centre right; a chord symbol is
                // anchored at its ink left (scm/define-grobs.scm:837-855), so it reaches
                // its WHOLE width right and nothing at all left.
                double chord = c < chordWidth.Length ? chordWidth[c] : 0.0;
                leftOverhangs[c] = c < lyricLeft.Length ? lyricLeft[c] : 0.0;
                rightOverhangs[c] = Math.Max(
                    c < lyricRight.Length ? lyricRight[c] : 0.0, chord);
            }
            // …and so does the MUSICAL ink on the column, which is the rest of
            // col->extent (col, X_AXIS).
            var (musicalLeft, musicalRight) =
                SpacingRules.MusicalInkOverhangsPerColumn(allMeasures, allTimings);
            for (int c = 0; c < leftOverhangs.Length; c++)
            {
                leftOverhangs[c] = Math.Max(leftOverhangs[c], musicalLeft[c]);
                rightOverhangs[c] = Math.Max(rightOverhangs[c], musicalRight[c]);
            }
            measureColumnOverhangs.Add((leftOverhangs, rightOverhangs));

            if (i == startMeasureIndex && springs.Length > 0)
            {
                // The bar-line spring 0 becomes the prefix→first-note spring, floored at
                // LilyPond's 0.3 + min_dist. ONE implementation (LineStartSpringForLine),
                // shared with the break gate so both price a line start identically
                // (section 5.4). systemIndex == 0 is the first line, which carries the meter.
                springs = springs.SetItem(0, LineStartSpringForLine(
                    score, startMeasureIndex, isFirstSystem: systemIndex == 0, springs[0],
                    LyricSpacing.LineStartLyricFloor(measureLineEdges[^1])));
            }
            measureSprings.Add(springs);
            measureTimings.Add(allTimings);
            measureAllMeasures.Add(allMeasures);

            double barlineWidth = SpacingRules.GetBarlineWidth(primaryMeasure.StartBarline)
                                + SpacingRules.GetBarlineWidth(primaryMeasure.EndBarline);
            measureBarlineWidths.Add(barlineWidth);
            totalBarlineWidth += barlineWidth;
        }

        // Concatenate all springs and solve for a single force across the system
        var allSprings = measureSprings.SelectMany(s => s).ToImmutableArray();

        // The system's rods, all fed through the one Simple_spacer::add_rod port
        // (SpringSolver.ApplyRods, blocking-force propagation included).
        var rods = new List<(int Left, int Right, double Distance)>();

        // KEEP-INSIDE-LINE: no column may push its ink into either margin.
        // LILYPOND-REF: lily/simple-spacer.cc:431-432 — every column but the line starter is
        //   given `keep_inside_line_ = col->extent (col, X_AXIS)` (the property is #t by
        //   default on PaperColumn scm/define-grobs.scm:2742 and NonMusicalPaperColumn :2525,
        //   and means "this column cannot have objects sticking into the margin",
        //   scm/define-grob-properties.scm:637) — and :556-560
        //     spacer.add_rod (i, cols.size (), cols[i].keep_inside_line_[RIGHT]);
        //     spacer.add_rod (0, i, -cols[i].keep_inside_line_[LEFT]);
        //   NO padding and no spring term: each rod is the bare overhang, which is why
        //   LilyPond's answer for a lead sheet is the overhang itself and not overhang + 0.5.
        // MEASURED (audit/lp-geometry/probes/staffless-system.ly, scores CL/CLX/CLL): a
        //   syllable reaching 2.312540 left of its column puts that column on 2.312539
        //   instead of the 0.500000 `min_dist + 0.5` alone gives, moving every column of the
        //   line by the same amount and none of the column-to-column distances; take the
        //   reach away (CLL) and the column drops straight back to 0.500000.
        // The rod is a MINIMUM, so it is inert wherever the springs already clear it — which
        // is everywhere but the first column and the last, since the springs in between
        // accumulate. That is why generalising this from the first column to all of them
        // moves nothing: the constraint was already met.
        // THE INPUT IS THE COLUMN'S WHOLE INK: the centred text on it AND the musical ink
        //   (a note head's width to the right, an accidental's reach to the left), built
        //   above. It was text-only for one commit, which under-measured a column carrying an
        //   accidental by ~1.13 ss (probe TKT: 1.234272 against a plain note's 0.100000,
        //   minus the extra-spacing-width that reading includes).
        // ⚠️ Two LilyPond quantities are unported and would change what this rod measures:
        //   ChordName declares no X-offset and no self-alignment-interface at all
        //   (scm/define-grobs.scm:837-855) so LilyPond's chord ink starts AT its column, and
        //   a LyricText is centred not on the column but on the PaperColumn placeholder
        //   X-alignment-extent = (0 . 1.35), i.e. at -w/2 + 0.675
        //   (self-alignment-interface.cc:117-176, define-grobs.scm:2749-2750). The ledger
        //   point staffless.line-start.chords-vs-staff (-0.438600) is the first of these.
        {
            // LILYSHARP-OWN, the lead-sheet grid's line-start bar (user request
            // 2026-08-20, with the meter decision in HANDOFF 3): the grid opens every
            // line with a bar line, so the line's LEFT EDGE for keep-inside purposes
            // moves right by the LeftEdge gap — ink that used to sit flush on the
            // margin (LP's keep-inside puts it at 0) would overprint the new bar
            // (the user saw 'Up' under it on rows-song-sheet's second line). The gap
            // is the same LeftEdge→prefix 0.8 the first line's bar→meter shows, so
            // every line of the grid opens with one rhythm. Inert on any column the
            // springs already clear — i.e. everywhere but a line's first column.
            double gridLeftEdge = score.IsLeadSheet ? EngravingDefaults.ClefGlyphXOffset : 0.0;
            int columnOffset = 0;
            for (int m = 0; m < measureColumnOverhangs.Count; m++)
            {
                var (left, right) = measureColumnOverhangs[m];
                for (int c = 0; c < left.Length; c++)
                {
                    // Spring j spans column j → column j+1, so measure m's column c is the
                    // right end of spring columnOffset + c.
                    int column = columnOffset + c + 1;
                    // A rod of 0 is satisfied by construction; LilyPond's own add_rod only
                    // records one when the distance is positive (separation-item.cc:57).
                    if (left[c] + gridLeftEdge > 0.0 && column >= 1 && column <= allSprings.Length)
                        rods.Add((0, column, left[c] + gridLeftEdge));
                    // A rod from the LINE's last column to itself is the degenerate one
                    // LilyPond's own `add_rod (i, cols.size (), …)` reduces to; skip it.
                    if (right[c] > 0.0 && column < allSprings.Length)
                        rods.Add((column, allSprings.Length, right[c]));
                }
                columnOffset += measureSprings[m].Length;
            }
        }

        // Multi-measure rest runs: LilyPond's run-level rod across the springs of the
        // run-opening measure (the run's single column pair).
        // LILYPOND-REF: lily/multi-measure-rest.cc:341-391 calculate_spacing_rods →
        // Rod::add_to_cols → lily/simple-spacer.cc:90-128 Simple_spacer::add_rod.
        int springOffset = 0;
        for (int i = 0; i < measureSprings.Count; i++)
        {
            int measureIndex = startMeasureIndex + i;
            int springCount = measureSprings[i].Length;
            if (springCount > 0 && runMap.TryGetRunStartingAt(measureIndex, out var run))
            {
                // LP's Paper_column::minimum_distance (li, ri) between the bounding
                // bar-line columns: a skyline distance over the LEFT column's break-aligned
                // grobs (bar line + any leading key/time change). This used to sum the run
                // measure's spring MinDistances, which has no LilyPond counterpart: it is an
                // accumulation of spacing minima, not a geometric column distance, and it
                // inflated every run (an R1*5 run by ~3.4 ss).
                var runStartMeasure = primaryVoice.Measures[measureIndex];
                double minimumDistance = SpacingRules.MmrRodMinimumDistance(
                    SpacingRules.RunLeftBoundBarline(primaryVoice.Measures, measureIndex),
                    runStartMeasure.Items);

                var measureLength = Fraction.Zero;
                foreach (var item in runStartMeasure.Items)
                    measureLength += item.Duration;

                // Bar lines this measure adds to its width below — subtracted from the
                // rod so the run's CONTENT span, plus these bar lines, equals LilyPond's
                // li->ri column distance. See MmrRodDistance.
                double runBarlineWidth =
                    SpacingRules.GetBarlineWidth(runStartMeasure.StartBarline)
                    + SpacingRules.GetBarlineWidth(runStartMeasure.EndBarline);

                rods.Add((springOffset, springOffset + springCount,
                    SpacingRules.MmrRodDistance(
                        run.Count, measureLength, minimumDistance, runBarlineWidth)));
            }
            springOffset += springCount;
        }

        // CROSS-BAR LYRIC RODS: a lyric line that continues into the adjacent measure is
        // rodded syllable-to-syllable straight across the bar — the LyricSpace/LyricHyphen
        // minimum the halves ReserveLyricLine dropped stood in for.
        // LILYPOND-REF: lily/lyric-hyphen.cc:163-179 set_spacing_rods → Rod::add_to_cols →
        // lily/simple-spacer.cc:90-128 Simple_spacer::add_rod (probe LCW: in-bar and
        // cross-bar word gaps identical to the digit — a bar line takes no part).
        // Strictly ADJACENT measures only: a continuing line implies the neighbour carries
        // its syllable, hence notes, hence springs — an MMR interior or trailing clef
        // column can carry neither, so no continuing pair ever lands on empty springs.
        // At the SYSTEM'S END a continuing line's pair is on the next system, so the
        // dropped trailing half is re-supplied verbatim as a rod to the line's end
        // (LyricSpacing.LineEndLyricReservation) — pre-port geometry, kept until the
        // line-end hyphen regime is measured. The break gate prices both quantities from
        // the same functions (SystemBreaker/LyricSpacing.CrossBarPairMinExcess).
        if (!score.Lyrics.IsDefaultOrEmpty)
        {
            int lyricSpringOffset = 0;
            for (int m = 0; m < measureSprings.Count; m++)
            {
                int springCount = measureSprings[m].Length;
                var edges = measureLineEdges[m];
                foreach (var edge in edges)
                {
                    if (!edge.ContinuesIntoNext)
                        continue;
                    if (m + 1 < measureSprings.Count)
                    {
                        foreach (var next in measureLineEdges[m + 1])
                        {
                            if (!next.Key.Equals(edge.Key))
                                continue;
                            // The drawn distance re-adds the bar-line ink between the two
                            // chains, so the spring-space rod sheds it (MmrRodDistance's
                            // convention).
                            var leftMeasure = primaryVoice.Measures[startMeasureIndex + m];
                            var rightMeasure = primaryVoice.Measures[startMeasureIndex + m + 1];
                            double dist = LyricSpacing.CrossBarLyricRodDistance(
                                edge, next,
                                SpacingRules.GetBarlineWidth(leftMeasure.EndBarline)
                                + SpacingRules.GetBarlineWidth(rightMeasure.StartBarline));
                            if (dist > 0)
                                rods.Add((lyricSpringOffset + edge.LastCol + 1,
                                    lyricSpringOffset + springCount + next.FirstCol + 1,
                                    dist));
                            break;
                        }
                    }
                    else
                    {
                        // Last measure of the system: the pair broke apart, re-supply the
                        // trailing half as a rod over the same suffix springs it bumped.
                        rods.Add((lyricSpringOffset + edge.LastCol + 1,
                            lyricSpringOffset + springCount,
                            LyricSpacing.LineEndLyricReservation(edge)));
                    }
                }
                lyricSpringOffset += springCount;
            }
        }

        if (rods.Count > 0)
        {
            allSprings = SpringSolver.ApplyRods(allSprings, rods);
            // ApplyRods preserves order and count, so slice the adjusted springs back
            // into their measures — per-measure widths below read from these lists.
            int offset = 0;
            for (int i = 0; i < measureSprings.Count; i++)
            {
                int n = measureSprings[i].Length;
                if (n > 0)
                    measureSprings[i] = ImmutableArray.Create(allSprings.AsSpan(offset, n).ToArray());
                offset += n;
            }
        }

        double springTargetWidth = availableWidth - totalBarlineWidth;

        double force = 0;
        if (allSprings.Length > 0)
        {
            var solver = new SpringSolver(allSprings);

            // LILYPOND-REF: lily/simple-spacer.cc:175-205 Simple_spacer::solve()
            // The last system is justified like the others — LilyPond's `ragged-last`
            // is unset by default and inherits `ragged-right` (= #f for a multi-line
            // score), so the final line fills the full width (verified against
            // LilyPond 2.24 output). Only a global ragged-right leaves lines unfilled.
            //
            // SPECIAL CASE (constrained-breaking.cc:142-148): a score whose
            // ONLY line would be STRETCHED uses ragged spacing — a single
            // sparse system keeps its natural width instead of being pulled
            // across the whole page. Compression still applies.
            bool singleSystemRagged = systemIndex == 0 && isLastSystem
                && new SpringSolver(allSprings).IdealTotalLength < springTargetWidth;
            force = SystemForceSolver.ResolveForce(solver, springTargetWidth, _options.RaggedRight || singleSystemRagged);
        }

        // Second pass: layout measures using the solved force
        var layouts = ImmutableArray.CreateBuilder<MeasureLayout>();
        double currentX = startX;

        for (int i = 0; i < measureSprings.Count; i++)
        {
            int measureIndex = startMeasureIndex + i;
            var primaryMeasure = primaryVoice.Measures[measureIndex];

            // Measure width = barline widths + sum of spring lengths at solved force
            double measureWidth = measureBarlineWidths[i];
            foreach (var spring in measureSprings[i])
            {
                measureWidth += spring.Length(force);
            }

            var columnLayouts = _measureLayouter.LayoutColumns(
                primaryMeasure, measureWidth, measureTimings[i],
                baseShortestDuration, measureAllMeasures[i],
                measureSprings[i], force);

            // Derive the item slots FROM the solved columns so Items[i].X == the
            // column-grid X the renderer draws the notehead at — the raw-slot readers
            // (Hairpin / TextSpanner / TrillSpanner / TieVariant) then stay on the
            // notehead instead of drifting when a bar opens with a mid-piece meter/clef
            // change. Fall back to the item-spring layout only for a degenerate measure
            // with no timing columns (all zero-duration items).
            var itemLayouts = MeasureLayouter.LayoutItemsFromColumns(primaryMeasure, columnLayouts, measureWidth);
            if (itemLayouts.IsDefaultOrEmpty && primaryMeasure.Items.Length > 0)
                itemLayouts = _measureLayouter.LayoutItems(primaryMeasure, measureWidth);

            var measureLayout = new MeasureLayout(measureIndex, currentX, measureWidth, itemLayouts, columnLayouts)
            {
                LooseChangeHangs = ComputeLooseChangeHangs(
                    measureAllMeasures[i], measureTimings[i],
                    measureColumnOverhangs[i].Right, columnLayouts),
            };
            layouts.Add(measureLayout);
            currentX += measureLayout.Width;
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// Hang distances for this measure's LOOSE mid-measure change columns, computed from
    /// the SOLVED column positions — the scale factor needs the room the line actually
    /// left, so this cannot run until after the springs are solved. Null when the measure
    /// has none, which is nearly every measure.
    /// </summary>
    /// <remarks>
    /// The loose decision here must match the one CreateInterColumnSpring made when it
    /// pruned the column from the springs — both call the same
    /// <see cref="SpacingRules.IsLooseChangeColumn"/> on the same inputs.
    /// LILYPOND-REF: lily/spacing-loose-columns.cc:33-222 set_loose_columns — loose columns
    ///   are draped around the columns in between-cols after the line is solved.
    /// </remarks>
    private static ImmutableDictionary<Fraction, double>? ComputeLooseChangeHangs(
        List<Measure> allMeasures, List<Fraction> allTimings,
        double[] columnInkRight, ImmutableArray<ColumnLayout> columns)
    {
        if (columns.IsDefaultOrEmpty || columnInkRight.Length < allTimings.Count)
            return null;

        // Nearly every measure has no change item at all — bail before the per-timing
        // walk below, which is the only part of this that costs anything.
        bool anyChange = false;
        foreach (var m in allMeasures)
        {
            foreach (var item in m.Items)
                if (SpacingRules.IsMidMeasureChangeColumn(item))
                {
                    anyChange = true;
                    break;
                }
            if (anyChange)
                break;
        }
        if (!anyChange)
            return null;

        ImmutableDictionary<Fraction, double>.Builder? hangs = null;
        for (int c = 1; c < allTimings.Count; c++)
        {
            var changeTiming = allTimings[c];
            List<MusicItem>? columnItems = null;
            foreach (var m in allMeasures)
            {
                var t = Fraction.Zero;
                foreach (var item in m.Items)
                {
                    if (t == changeTiming)
                        (columnItems ??= new List<MusicItem>()).Add(item);
                    else if (t > changeTiming)
                        break;
                    t += item.Duration;
                }
            }
            if (columnItems == null
                || !columnItems.Any(SpacingRules.IsMidMeasureChangeColumn))
                continue;

            var ownLeft = SpacingRules.LooseChangeLeftNeighborTiming(allMeasures, columnItems);
            if (!SpacingRules.IsLooseChangeColumn(allTimings, ownLeft, changeTiming, columnItems))
                continue;

            int leftIndex = allTimings.IndexOf(ownLeft!.Value);
            if (leftIndex < 0)
                continue;

            // permissible_distance = the right neighbor's origin minus the left
            // neighbor's ink RIGHT edge, both at their solved positions.
            // LILYPOND-REF: lily/spacing-loose-columns.cc:182-184 set_loose_columns.
            double permissible = columns[c].X
                                 - (columns[leftIndex].X + columnInkRight[leftIndex]);
            hangs ??= ImmutableDictionary.CreateBuilder<Fraction, double>();
            hangs[changeTiming] = SpacingRules.LooseChangeColumnHangDistance(
                columnItems, permissible);
        }
        return hangs?.ToImmutable();
    }

    /// <summary>
    /// The reservations the SHARED note columns carry on top of their duration
    /// springs — lyric syllables, chord symbols (lead-sheet grid or staff-attached),
    /// tab fret digits and wide scripts — applied by the ONE list both consumers
    /// read: the system layout (the loop above) and the line-break gate
    /// (SystemBreaker.ComputeMultiStaffSpringData). A bar the gate prices narrower
    /// than the layout sets is a bar the layout cannot fit: the gate carried a
    /// hand-mirrored copy of this list and drifted twice — ApplyTabChordSpacing was
    /// missing (an all-tab book packed onto one system and ran past the page edge,
    /// scratch/ベースタブLy/longtab.lys) and ApplyRowCommandColumnSprings was missing
    /// (a lead-sheet row priced without its command columns' 0.5s). Same failure
    /// shape as the container-skip predicates (docs/HANDOFF.md §1 第98): a
    /// per-caller copy of a whitelist is where drift grows, so there is no copy.
    /// </summary>
    internal static ImmutableArray<Spring> ApplySharedColumnReservations(
        MultiStaffScore score,
        int measureIndex,
        ImmutableArray<Spring> springs,
        Measure primaryMeasure,
        List<Fraction> allTimings,
        List<Measure> allMeasures)
    {
        // Cross-voice column pairs of each staff, with each item's ink at the X the
        // renderer will draw it (note-collision shifts included) — the floors the
        // per-voice rod loop in CreateInterColumnSpring cannot raise, because no
        // single voice occupies both columns of the pair. One separation frame per
        // STAFF, as LilyPond's Separation_item has; never across staves. A tab
        // staff's columns are digit rows, priced by ApplyTabChordSpacing below.
        foreach (var vGroup in score.StaffGroups)
            foreach (var vStaff in vGroup.Staves)
                if (!vStaff.IsTab && vStaff.Voices.Length >= 2)
                    springs = SpacingRules.ApplyCrossVoiceColumnSpacing(
                        springs, allTimings, vStaff, measureIndex);

        // Reserve room for lyric syllables so they don't collide. Only acts
        // on single-voice measures (timing columns == note items); a no-lyric
        // score leaves the chain untouched. Applied before the layout's
        // FirstNoteSpring tweak, which Math.Max-preserves the widened minimum.
        if (!score.Lyrics.IsDefaultOrEmpty)
        {
            // Both reserve on the union TIMING columns the springs were built from —
            // the map the engraver draws with. The fork is the INCLUDE: a lead sheet
            // reserves its lyrics row only (chords and lyrics subdivide the bar
            // differently), a staff-backed score reserves every verse.
            springs = score.IsLeadSheet
                ? LyricSpacing.ApplyLeadSheetLyricSpacing(
                    score.TextMetrics, springs, allTimings, measureIndex, score.Lyrics,
                    SpacingRules.ParentAlignmentEdgesPerColumn(allMeasures, allTimings))
                : LyricSpacing.ApplyLyricSpacing(
                    score.TextMetrics, springs, allTimings, measureIndex, score.Lyrics,
                    SpacingRules.ParentAlignmentEdgesPerColumn(allMeasures, allTimings),
                    // Per-voice edges — the same provider the ink reaches, the line
                    // edges and the drawn X read (a row has no voice, so the lead-sheet
                    // arm keeps the union/placeholder table).
                    LyricSpacing.OwnVoiceEdgeProvider(score));
        }
        if (score.IsLeadSheet)
        {
            // A staff-less row keeps its empty command columns (LilyPond never
            // prunes them without note-column neighbours), so every inter-column
            // gap carries the breakable dt==0 spring's 0.5 on top of its
            // duration space — see ApplyRowCommandColumnSprings.
            springs = SpacingRules.ApplyRowCommandColumnSprings(springs);
            // Chord symbols reserve their widths on the row columns, and a
            // grid bar never collapses below a readable cell (else a long
            // chords-only chart packs onto one line and never wraps).
            springs = SpacingRules.ApplyChordRowSpacing(score.TextMetrics, springs, allTimings, measureIndex, score.ChordNames);
            springs = SpacingRules.EnsureLeadSheetBarWidth(springs);
        }
        else if (!score.ChordNames.IsDefaultOrEmpty)
        {
            // Staff-attached chord symbols price their widths into the
            // columns on EVERY measure, exactly as LilyPond's ChordName
            // item extent (expanded (-0.5 . 0.5)) joins its paper column
            // — most visible over all-rest (R1) bars, which have no other
            // width source and otherwise collapse, overprinting the
            // symbols. LILYPOND-REF: scm/define-grobs.scm ChordName
            // extra-spacing-width.
            springs = SpacingRules.ApplyChordRowSpacing(
                score.TextMetrics, springs, allTimings, measureIndex, score.ChordNames, includeAttached: true);
        }

        // Tab fret digits (a Lily# enlargement of LilyPond's tiny numbers) are
        // wider than note heads and zigzag on chords; reserve their width in the
        // SHARED columns so adjacent digits don't overprint.
        foreach (var tGroup in score.StaffGroups)
            foreach (var tStaff in tGroup.Staves)
                if (tStaff.IsTab && tStaff.Tuning is { } tabTuning
                    && measureIndex < tStaff.PrimaryVoice.Measures.Length)
                    springs = SpacingRules.ApplyTabChordSpacing(
                        springs, allTimings, tStaff.PrimaryVoice.Measures[measureIndex],
                        Tunings.GetTuning(tabTuning),
                        Tunings.SoundingShift(tStaff.TabSourceClef, tStaff.Transposition));

        // Reserve a wide script's (fermata / ornament) sideways reach in the
        // SHARED columns, per staff (a script is keyed by its own staff index),
        // so a fermata over one note doesn't crowd the next note's accidental.
        // Y-gated by the skyline: a fermata high above the staff leaves a low
        // following note untouched. Staff enumeration matches the global index
        // scripts are tagged with (see BuildAllStaffSkylines).
        if (!score.Articulations.IsDefaultOrEmpty)
        {
            int artStaffIndex = 0;
            foreach (var aGroup in score.StaffGroups)
                foreach (var aStaff in aGroup.Staves)
                {
                    if (measureIndex < aStaff.PrimaryVoice.Measures.Length)
                        springs = SpacingRules.ApplyArticulationSpacing(
                            springs, allTimings, aStaff.PrimaryVoice.Measures[measureIndex],
                            score.Articulations, measureIndex, artStaffIndex);
                    artStaffIndex++;
                }
        }
        return springs;
    }

    /// <summary>
    /// Collects all unique timings from all voices for a specific measure.
    /// </summary>
    /// <remarks>
    /// A notation staff's spacer (<c>s</c>) makes NO column of its own while another
    /// voice's note sounds through it: LilyPond's skip engraves no grobs, so its paper
    /// column is empty, gets left/right neighbours from the sustained note's spacing
    /// wishes, and is pruned from the spring chain as a loose column — <c>c'4</c>
    /// against <c>s8[ s]</c> spaces as one plain quarter spring (LP regression
    /// beam-skip.ly). A spacer onset nothing sounds through KEEPS its column: with no
    /// note columns beside it there is nothing to attach the loose column to, and its
    /// duration space survives (<c>c'8 s8 c'8</c> reads two eighth springs in LP, and
    /// the whole lead-sheet spacing model rides on those surviving columns — the
    /// ApplyRowCommandColumnSprings remarks tell the same story from the other side).
    /// Text-row slot spacers and timing-placed chord symbols therefore always stay.
    /// LILYPOND-REF: lily/spacing-determine-loose-columns.cc:82-90 is_loose_column.
    /// </remarks>
    internal static List<Fraction> CollectAllTimingsForMeasure(MultiStaffScore score, int measureIndex)
    {
        var timings = new HashSet<Fraction>();
        // Whether any NOTATION-staff spacer contributed an onset. Almost no measure has
        // one, and the prune below can only ever remove such onsets — so the anchor/span
        // bookkeeping it needs is built in a second pass, only when this trips. This
        // function runs per measure from BOTH the layout and the line breaker, so the
        // common path must stay what it always was: timings.Add and nothing else.
        bool sawNotationSpacer = false;

        foreach (var staffGroup in score.StaffGroups)
        {
            foreach (var staff in staffGroup.Staves)
            {
                foreach (var voice in staff.Voices)
                {
                    if (measureIndex < voice.Measures.Length)
                    {
                        var measure = voice.Measures[measureIndex];
                        var currentTiming = Fraction.Zero;

                        foreach (var item in measure.Items)
                        {
                            timings.Add(currentTiming);
                            if (!staff.IsTextRow && item is RestItem { IsSpacer: true })
                                sawNotationSpacer = true;
                            currentTiming += item.Duration;
                        }
                    }
                }
            }
        }

        // Timing-placed chord symbols carry their own rhythm and may fall BETWEEN
        // note onsets (e.g. a chord on a beat inside a half note). Give each its own
        // column so the spacing reserves the symbol's width there and the measure
        // widens to fit — otherwise the symbol has no column and overhangs the barline.
        if (!score.ChordNames.IsDefaultOrEmpty)
            foreach (var cn in score.ChordNames)
                if (cn.MeasureIndex == measureIndex && cn.UseTiming)
                    timings.Add(cn.Timing);

        var sortedTimings = sawNotationSpacer
            ? PruneSpacerOnlyOnsets(score, measureIndex, timings)
            : timings.ToList();
        sortedTimings.Sort();
        return sortedTimings;
    }

    /// <summary>
    /// The loose-column prune (see <see cref="CollectAllTimingsForMeasure"/>): drops
    /// onsets owed ONLY to notation-staff spacers when a musical item sounds strictly
    /// through them (started before, ends after). Everything the filter could empty is
    /// anchored, so the empty-result guard never fires for real music; it protects a
    /// degenerate all-spacer measure from losing its one column. Runs only when the
    /// measure actually contains a notation-staff spacer.
    /// </summary>
    private static List<Fraction> PruneSpacerOnlyOnsets(
        MultiStaffScore score, int measureIndex, HashSet<Fraction> timings)
    {
        // Onsets owed to something OTHER than a notation-staff spacer: a musical item
        // anywhere, or any text-row item (a lead sheet's slot columns are its spacers).
        var anchoredOnsets = new HashSet<Fraction>();
        // Spans of notation-staff MUSICAL items — the "another voice sounds through it"
        // test for pruning a spacer-only onset.
        var soundingSpans = new List<(Fraction Start, Fraction End)>();

        foreach (var staffGroup in score.StaffGroups)
        {
            foreach (var staff in staffGroup.Staves)
            {
                foreach (var voice in staff.Voices)
                {
                    if (measureIndex < voice.Measures.Length)
                    {
                        var currentTiming = Fraction.Zero;
                        foreach (var item in voice.Measures[measureIndex].Items)
                        {
                            bool musical = SpacingRules.IsMusicalColumn(item);
                            if (musical || staff.IsTextRow)
                                anchoredOnsets.Add(currentTiming);
                            if (musical && !staff.IsTextRow && item.Duration > Fraction.Zero)
                                soundingSpans.Add((currentTiming, currentTiming + item.Duration));
                            currentTiming += item.Duration;
                        }
                    }
                }
            }
        }

        if (!score.ChordNames.IsDefaultOrEmpty)
            foreach (var cn in score.ChordNames)
                if (cn.MeasureIndex == measureIndex && cn.UseTiming)
                    anchoredOnsets.Add(cn.Timing);

        var pruned = timings
            .Where(t => anchoredOnsets.Contains(t)
                        || !soundingSpans.Any(s => s.Start < t && t < s.End))
            .ToList();
        return pruned.Count == 0 ? timings.ToList() : pruned;
    }

    /// <summary>
    /// Collects all measures from all voices at a specific measure index.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/paper-column.cc — paper columns aggregate grobs from all staves.
    /// Column spacing rods must consider skyline collisions from ALL voices.
    /// </remarks>
    internal static List<Measure> CollectAllMeasuresAtIndex(MultiStaffScore score, int measureIndex)
    {
        var measures = new List<Measure>();

        foreach (var staffGroup in score.StaffGroups)
        {
            foreach (var staff in staffGroup.Staves)
            {
                foreach (var voice in staff.Voices)
                {
                    if (measureIndex < voice.Measures.Length)
                    {
                        measures.Add(voice.Measures[measureIndex]);
                    }
                }
            }
        }

        return measures;
    }

    /// <summary>
    /// Which STAFF each entry of <see cref="CollectAllMeasuresAtIndex"/> belongs to, as a
    /// parallel list of global staff ordinals — the two walks are the same loop, so the
    /// indices line up. The spacing-wish scan needs this because LilyPond's neighbor map is
    /// per staff, not per voice: two voices of ONE staff occupying adjacent columns carry a
    /// NoteSpacing wish between them (Note_spacing_engraver keys its last-spacing map by the
    /// voice's parent context), where two different staves do not.
    /// LILYPOND-REF: lily/note-spacing-engraver.cc:109-128 stop_translation_timestep —
    ///   <c>last_spacings_[parent]</c>, the parent being the Staff.
    /// </summary>
    internal static List<int> CollectStaffIndicesAtIndex(MultiStaffScore score, int measureIndex)
    {
        var staffIds = new List<int>();
        int staffId = 0;
        foreach (var staffGroup in score.StaffGroups)
        {
            foreach (var staff in staffGroup.Staves)
            {
                foreach (var voice in staff.Voices)
                {
                    if (measureIndex < voice.Measures.Length)
                        staffIds.Add(staffId);
                }
                staffId++;
            }
        }
        return staffIds;
    }

    // --- Skyline-based staff spacing ---

    /// <summary>
    /// Calculates the total height of a multi-staff system using skyline distances.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc:217-268 internal_get_minimum_translations()
    /// Uses per-staff skylines to determine actual spacing needed, instead of fixed formula.
    /// <para>
    /// This lays the groups out and returns <see cref="SystemHeightOf"/> — it does NOT walk
    /// the specs a second time. LilyPond has one such walk: the alignment translates its
    /// elements and the axis group's Y-extent is whatever came out of it. The second walk
    /// this method used to be drifted from the placement it was supposed to describe (its
    /// grand-staff branch summed each staff's REAL height while the placement stacked the
    /// nominal one), which is the shape HANDOFF 5.2.1 (2) names.
    /// </para>
    /// </remarks>
    public double CalculateSystemHeight(
        MultiStaffScore score, SkylineBuilder skylineBuilder,
        ImmutableArray<MeasureLayout> measureLayouts, int systemIndex)
        => SystemHeightOf(LayoutStaffGroups(score, skylineBuilder, measureLayouts, systemIndex));

    /// <summary>
    /// The height of a system: the Y-extent of the staff groups AS PLACED.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:220-238 generic_group_extent() — a
    /// system's height is the union of the extents of the elements the alignment placed,
    /// never a second sum over the spacing specs.
    /// <para>
    /// ⚠️ THIS IS WHY HARA-KIRI NEEDS NO ARITHMETIC OF ITS OWN. LilyPond does not compute a
    /// hara-kiri'd system's height differently; it removes the dead elements
    /// (<c>page-layout-problem.cc:1366-1370</c> keeps only <c>is_live()</c>,
    /// <c>align-interface.cc:90</c> hands an empty skyline back for a dead group) and runs
    /// the SAME extent over what is left. A hidden group is placed at zero height and is
    /// skipped here, so it leaves the union by itself. The branch this replaced spelled the
    /// gap between surviving groups as the literal
    /// <c>StaffSpacing.StaffGroupStaff.BasicDistance</c> (10.5) and so ignored
    /// <see cref="InterGroupSpec"/> (9 for staves with no grouper), the ossia scaling, the
    /// text-row pair gap and the per-verse lyric constant that used to sit beside them
    /// (deleted once the room became <see cref="AlignmentWalk"/>) — measured at
    /// 1.500000 too deep, audit/lp-geometry book LYRHK
    /// (<c>lyrics.hara-kiri.shown-system.staff-to-lyric</c>).
    /// </para>
    /// </remarks>
    public static double SystemHeightOf(ImmutableArray<StaffGroupLayout> groups)
    {
        if (groups.IsDefaultOrEmpty)
            return 0;

        double top = double.NegativeInfinity;
        double bottom = double.PositiveInfinity;
        foreach (var group in groups)
        {
            // A group whose every staff committed hara-kiri is not in the union.
            if (group.Staves.All(s => s.IsHidden))
                continue;
            top = Math.Max(top, group.Y);
            bottom = Math.Min(bottom, group.Y - group.Height);
        }

        return double.IsInfinity(top) ? 0 : top - bottom;
    }

    /// <summary>
    /// Layouts all staff groups using skyline-based spacing.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc:217-268 internal_get_minimum_translations()
    /// </remarks>
    public ImmutableArray<StaffGroupLayout> LayoutStaffGroups(
        MultiStaffScore score,
        SkylineBuilder skylineBuilder, ImmutableArray<MeasureLayout> measureLayouts,
        int systemIndex)
        => LayoutStaffGroups(
            score,
            BuildAllStaffSkylines(score, skylineBuilder, measureLayouts, systemIndex).Skylines,
            NothingDies);

    /// <summary>
    /// Layouts all staff groups for ONE system, hiding the staves that are empty across
    /// <paramref name="startMeasure"/>..<paramref name="endMeasure"/>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/hara-kiri-group-spanner.cc consider_suicide() — the only thing
    /// hara-kiri contributes is WHICH staves are still there. Everything downstream is the
    /// same walk over whatever survived, which is why this is one line.
    /// </remarks>
    public ImmutableArray<StaffGroupLayout> LayoutStaffGroups(
        MultiStaffScore score,
        SkylineBuilder skylineBuilder, ImmutableArray<MeasureLayout> measureLayouts,
        int startMeasure, int endMeasure, bool isFirstSystem, int systemIndex)
        => LayoutStaffGroups(
            score,
            BuildAllStaffSkylines(score, skylineBuilder, measureLayouts, systemIndex).Skylines,
            startMeasure, endMeasure, isFirstSystem);

    /// <summary>
    /// The same placement on skylines the caller has ALREADY built for this system.
    /// </summary>
    /// <remarks>
    /// One system's staff skylines are the input to both its placement and its page springs
    /// (<see cref="StaffSprings(MultiStaffScore, ImmutableArray{StaffGroupLayout},
    /// List{ValueTuple{VerticalSkyline, VerticalSkyline}}, LooseLinesBetween?)"/>), and building them is the
    /// expensive part of laying a system out — measured 2026-07-27 at roughly 5.6 ms per
    /// build on a fifty-system score, which is most of that system's layout cost. Letting
    /// the caller build once and hand the same list to both is worth an explicit overload:
    /// the convenience overloads above each build their own, so using them for both halves
    /// doubles the work for an identical answer.
    /// </remarks>
    internal ImmutableArray<StaffGroupLayout> LayoutStaffGroups(
        MultiStaffScore score,
        List<(VerticalSkyline Up, VerticalSkyline Down)> staffSkylines,
        int startMeasure, int endMeasure, bool isFirstSystem,
        LooseLinesBetween? looseLines = null)
        => LayoutStaffGroups(
            score, staffSkylines,
            staff => HaraKiri.ShouldHideStaff(staff, startMeasure, endMeasure, isFirstSystem),
            looseLines);

    /// <summary>Builds the per-staff skylines one system is placed and sprung against,
    /// with the inside-staff spanners they were built from — see
    /// <see cref="StaffInsideSpanners"/> for why the second half travels with them.</summary>
    internal StaffSkylineSet BuildStaffSkylines(
        MultiStaffScore score, SkylineBuilder skylineBuilder,
        ImmutableArray<MeasureLayout> measureLayouts, int systemIndex)
        => BuildAllStaffSkylines(score, skylineBuilder, measureLayouts, systemIndex);

    /// <summary>
    /// THE staff-group placement: one walk over the alignment's surviving elements.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc:217-268 internal_get_minimum_translations().
    /// <para>
    /// ⚠️ HARA-KIRI IS THE <paramref name="isDead"/> ARGUMENT AND NOTHING ELSE. Until
    /// 2026-07-26 there were two of these walks — this one, and a hara-kiri copy that spaced
    /// staves at the bare <c>basic-distance</c> and never consulted a skyline — and
    /// LayoutEngine chose between them on whether any staff DECLARED removeEmpty. That is
    /// the shape HANDOFF 5.2.1 (2) names, and it cost exactly what it always costs: on music
    /// whose ink between the staves is tall, declaring removeEmpty collapsed the gap from
    /// 22.090000 to 9.000000 and ran the two staves' ledger lines together. LilyPond has one
    /// walk and a live-filter (page-layout-problem.cc:1366-1370), so this has one walk and a
    /// predicate.
    /// </para>
    /// </remarks>
    private ImmutableArray<StaffGroupLayout> LayoutStaffGroups(
        MultiStaffScore score,
        List<(VerticalSkyline Up, VerticalSkyline Down)>? staffSkylines,
        Func<Staff, bool> isDead,
        LooseLinesBetween? looseLines = null)
    {
        var builder = ImmutableArray.CreateBuilder<StaffGroupLayout>();
        double currentY = 0;
        double staffHeight = _options.StaffHeight;
        var sp = _options.StaffSpacing;
        int globalStaffIndex = 0;

        // The global staff index of each group's first and last SURVIVOR, so the gap between
        // two groups is measured between the staves that actually face each other.
        int FirstLiveIndex(int groupIndex)
        {
            int at = 0;
            for (int g = 0; g < groupIndex; g++)
                at += score.StaffGroups[g].StaffCount;
            var staves = score.StaffGroups[groupIndex].Staves;
            for (int k = 0; k < staves.Length; k++)
                if (!isDead(staves[k]))
                    return at + k;
            return -1;
        }
        int LastLiveIndex(int groupIndex)
        {
            int at = 0;
            for (int g = 0; g < groupIndex; g++)
                at += score.StaffGroups[g].StaffCount;
            var staves = score.StaffGroups[groupIndex].Staves;
            for (int k = staves.Length - 1; k >= 0; k--)
                if (!isDead(staves[k]))
                    return at + k;
            return -1;
        }
        Staff? StaffAt(int groupIndex, int globalIdx)
        {
            int at = 0;
            for (int g = 0; g < groupIndex; g++)
                at += score.StaffGroups[g].StaffCount;
            int k = globalIdx - at;
            var staves = score.StaffGroups[groupIndex].Staves;
            return k >= 0 && k < staves.Length ? staves[k] : null;
        }

        // The last SPACEABLE staff placed so far and the text rows placed since — the state
        // that lets a spaceable pair with rows between it be placed at the alignment's own
        // answer (see the pair branch below) instead of at the rows' stacked bands.
        (Staff Staff, StaffLayout Layout, StaffGroup Group)? lastSpaceable = null;
        var rowsSinceSpaceable = new List<(Staff Staff, StaffLayout Layout)>();

        for (int i = 0; i < score.StaffGroups.Length; i++)
        {
            var group = score.StaffGroups[i];

            var layout = group.IsGrandStaff
                ? LayoutGrandStaffGroupWithSkylines(
                    score, group, currentY, sp.StaffStaff, globalStaffIndex, staffSkylines, isDead,
                    looseLines)
                : group.HasDelimiter
                    ? LayoutBracketGroupWithSkylines(
                        score, group, currentY, sp.StaffStaff, globalStaffIndex, staffSkylines, isDead,
                        looseLines)
                    : LayoutSingleStaffGroupWithSkylines(
                        score, group, currentY, sp.StaffStaff, globalStaffIndex, staffSkylines, isDead,
                        looseLines);
            builder.Add(layout);

            // A group with no survivor takes no room and no gap: it is not in the alignment.
            bool groupIsDead = layout.Staves.All(s => s.IsHidden);
            if (!groupIsDead)
            {
                currentY -= layout.Height;

                // Track the alignment state the pair branch below reads: the last placed
                // SPACEABLE staff, and the non-spaceable rows placed since it.
                for (int k = 0; k < group.Staves.Length && k < layout.Staves.Length; k++)
                {
                    if (layout.Staves[k].IsHidden)
                        continue;
                    if (StaffAffinity.IsSpaceable(group.Staves[k].StaffAffinity))
                    {
                        lastSpaceable = (group.Staves[k], layout.Staves[k], group);
                        rowsSinceSpaceable.Clear();
                    }
                    else if (lastSpaceable is not null)
                    {
                        rowsSinceSpaceable.Add((group.Staves[k], layout.Staves[k]));
                    }
                }

                // The next group that still has a staff — NOT simply the next group, which
                // may have died. LilyPond's element list has already dropped the dead ones,
                // so the spec and the skyline pair both come from the surviving neighbour.
                int next = i + 1;
                while (next < score.StaffGroups.Length && FirstLiveIndex(next) < 0)
                    next++;

                if (next < score.StaffGroups.Length)
                {
                    // LILYPOND-REF: lily/align-interface.cc:240-252 — staff-affinity-aware spec selection.
                    var nextGroup = score.StaffGroups[next];
                    var spec = InterGroupSpec(group, nextGroup, sp);
                    bool textRowPair = group.Staves[^1].IsTextRow && nextGroup.Staves[0].IsTextRow;
                    int upperLive = LastLiveIndex(i);
                    int lowerLive = FirstLiveIndex(next);
                    double span = GapSpan(score, group.Staves[^1], nextGroup.Staves[0]);
                    // The alignment's own elements between the two staves — this group's
                    // `with lyrics` lines. They are IN the walk that fixes the gap; there
                    // is no separate term for them.
                    // LILYPOND-REF: lily/page-layout-problem.cc:948-990 — a non-spaceable
                    // staff is pushed onto `loose_lines` and the NEXT spaceable one closes
                    // the run, so the two spaceable staves are never adjacent in the walk.
                    // NO SCALE FACTOR ANYWHERE IN THE ELEMENT LOOP, which is why
                    // `gap * OssiaScaleFactor` is gone from this line.
                    // LILYPOND-REF: lily/align-interface.cc:201-285
                    // internal_get_minimum_translations. Every term that can enter `dy` is
                    // written out there: :217 (the first element's own skyline), :223-238
                    // (get_spacing_spec between the two elements, then padding /
                    // minimum-distance / basic-distance) and :240-267 (the extra floor
                    // against the last SPACEABLE element). It accumulates at :274 with
                    // `where += stacking_dir * dy`. NOT ONE of those terms reads an element's
                    // magnification or its own staff-space, so a magnified element is spaced
                    // exactly like any other.
                    // ⚠️ Read :240-267 as the sibling remark on StaffSprings reads it —
                    // include_fixed_spacing's second constraint — NOT as this claim's source.
                    // ⚠️ LilyPond DOES scale the STAFF (ly/music-functions-init.ly
                    // magnifyStaff, "Change the staff size by factor mag", driving
                    // StaffSymbol thickness / staff-space and the notation size).
                    // GetStaffHeight keeps that; only the DISTANCE lost it.
                    // ⚠️ THAT THESE OFFSETS ARE REFPOINTS is the CORPUS's claim rather than
                    // this function's — `translates[]` are element offsets here. Measured on
                    // 2.26.0 across three arrangements — probe page-vertical.ly
                    // books OSSU (small staff above), OSSD (small staff below) and TABS (a tab
                    // staff below) all read 9.000000; the falsifier 9 * magstep(-3) = 6.363961
                    // fired on none of them. Ledger: staff.ossia-pair.staff-staff-inside.
                    // ⚠️ AND `StaffGap` ITSELF IS NOT A LITERAL PORT — see
                    // CalculateStaffGapWithSkylines, which takes max(basic-distance,
                    // alignment minimum) where LilyPond reads basic-distance only behind the
                    // pure branch. That deviation is older than this line and is named there.
                    // What changed here is only that an ossia pair now goes through the SAME
                    // reconstruction as every other pair instead of a scaled one; it does not
                    // make the ossia's spacing literal, it makes it not-special.
                    double interGroupGap = textRowPair
                        ? TextRowPairGap
                        : StaffGap(spec, span, staffSkylines, upperLive, lowerLive,
                            looseLines?.Invoke(upperLive, lowerLive));

                    // ★ A SPACEABLE PAIR WITH ROWS BETWEEN IS PLACED AT THE ALIGNMENT'S OWN
                    // ANSWER (2026-08-26). The incremental stack above prices this boundary
                    // as row-to-staff and has already priced staff-to-row and row-to-row —
                    // three Lily#-shaped steps whose sum is the rows' BAND stack, a quantity
                    // LilyPond never computes. LilyPond's pair is one spring whose length at
                    // rest is max(the spec's basic-distance, the alignment minimum walked
                    // over the rows) — the same expression CalculateStaffGapWithSkylines is
                    // for a pair with note-bound lines between — and the rows are then
                    // distributed INTO that span (distribute_loose_lines; Lily#'s
                    // LyricEngraver solves them and ApplySolvedRowPositions moves them), so
                    // the bands the rows were provisionally stacked in claim no room of
                    // their own.
                    // LILYPOND-REF: lily/page-layout-problem.cc:936-939 distribute_loose_lines
                    // — the two positions handed in are consecutive SPACEABLE staves'.
                    // LILYPOND-REF: lily/align-interface.cc:201-285 internal_get_minimum_translations
                    // — the walk the minimum comes from, rows included (:919-925 collects them).
                    // ⚠️ MEASURED before this branch existed (book CHL1, session 258): the
                    // stacked bands left the pair 14.552250371 apart where the walk reads
                    // 13ish and LilyPond 13.024768437, and the chain then spread the +1.5 of
                    // slack over its three springs — no step exact, every step inherited it.
                    // ⚠️ THE SAME SPEC AND THE SAME BLOCKS AS StaffSprings.AddSpring, or the
                    // spring's floor and the drawn distance describe different alignments
                    // and RespaceStaves moves what the single-page path left alone — the two
                    // page paths must read one geometry.
                    if (staffSkylines is not null
                        && rowsSinceSpaceable.Count > 0
                        && lastSpaceable is { } upper)
                    {
                        int lowIdx = FirstLiveIndex(next);
                        var lowStaff = lowIdx >= 0 ? StaffAt(next, lowIdx) : null;
                        if (lowStaff is not null
                            && StaffAffinity.IsSpaceable(lowStaff.StaffAffinity))
                        {
                            var pairSpec = InterGroupSpec(upper.Group, nextGroup, sp);
                            var blocks = PairBlocks(
                                looseLines?.Invoke(upper.Layout.StaffIndex, lowIdx),
                                rowsSinceSpaceable, staffSkylines, sp);
                            double pairMin = AlignmentMinimumWithSkylines(
                                pairSpec, staffSkylines, upper.Layout.StaffIndex, lowIdx,
                                blocks);
                            double pairDist = Math.Max(pairSpec.BasicDistance, pairMin);
                            double targetTop = StaffRefpoint(upper.Layout) - pairDist
                                + PlacedRefpointBelowTop(score, lowStaff);
                            interGroupGap = currentY - targetTop;
                        }
                    }
                    currentY -= interGroupGap;
                }
            }

            globalStaffIndex += group.StaffCount;
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// The springs LilyPond puts BETWEEN the spaceable staves of one system, read off the
    /// layout those staves already have.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:651-720 — <c>append_system</c> walks the
    /// system's elements and pushes one spring per spaceable staff PAIR into the page's
    /// chain, taking the spec from the upper staff's grouper and flooring the spring at the
    /// minimum translation with <c>ensure_min_distance</c>.
    /// <para>
    /// The floor is <see cref="AlignmentMinimumWithSkylines"/> — the same function the
    /// layout floors itself with, on skylines built by the same call — and NOT the distance
    /// the staves were drawn at. The two differ by exactly basic-distance, which is the
    /// SPRING'S IDEAL and must not also be its floor: taken from the drawn distance the
    /// spring cannot compress at all, which is how the first cut of this measured 9.000000
    /// where LilyPond compresses to 8.651797. Only the SPEC selection is shared through
    /// <see cref="InterGroupSpec"/> (HANDOFF 5.2.1 (2) — a second implementation of a
    /// quantity is where a port lands only half the time).
    /// </para>
    /// <para>
    /// The skylines are the CALLER's, built once per system and shared with the placement
    /// (<see cref="BuildStaffSkylines"/>) — the floor and the drawn distance have to be the
    /// same system's answer, since the floor is reconstructed from the distance. There used
    /// to be a skyline-less overload whose floor fell back to the drawn distance, so a
    /// spring built through it could stretch but never compress; that is what held a
    /// hara-kiri'd score's staves at 9.000000 where the same music without the declaration
    /// squeezed to 8.651797. It was fixed in 2026-07-26 and the overload removed once
    /// nothing called it, so this argument is no longer nullable.
    /// </para>
    /// <para>
    /// ⚠️ THREE OF LilyPond's CONSTRAINTS ON THIS SPRING ARE NOT PORTED, listed so the next
    /// reader does not have to rediscover which parts of :651-720 are missing:
    /// <list type="bullet">
    /// <item><c>alignment-distances</c> (:706-717) — a per-system manual override out of
    /// <c>line-break-system-details</c> that pins the spring RIGID (ideal = min = dy,
    /// inverse stretch 0). Lily# has no surface for it; before this port there was no
    /// spring to pin either.</item>
    /// <item>the first spaceable staff's extra floor for loose lines ABOVE it
    /// (:667-670) — same root as the missing <c>distribute_loose_lines</c>.</item>
    /// <item><c>include_fixed_spacing</c>'s second constraint (align-interface.cc:240-267):
    /// a spaceable staff is also floored against the PREVIOUS SPACEABLE staff (not just the
    /// previous element) and by <c>get_fixed_spacing</c>. It only bites when loose lines sit
    /// between two staves, which is the same gap again.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Which pairs get a spring, and why the others do not:
    /// <list type="bullet">
    /// <item>TEXT ROWS (lyric/chord rows) are LilyPond's non-spaceable lines — its own loop
    /// springs only between <c>is_spaceable</c> elements (:660-719) and distributes the rest
    /// afterwards (<c>distribute_loose_lines</c>). ★ SINCE 2026-07-27 Lily# distributes them
    /// too, for the case the corpus measures: a CHORDS row leading a system is an element of
    /// the previous block's chain and is translated to the solve
    /// (<c>LyricEngraver.DistributeLooseLines</c>, <c>LayoutEngine.ApplySolvedRowPositions</c>).
    /// ★ SINCE 2026-07-28 an independent LYRICS row standing below a system's last spaceable
    /// staff is distributed too, verse by verse, in that system's own run. What still keeps
    /// its laid-out offset: a row on a page's FIRST system, whose chain LilyPond runs from the
    /// page top (:963-988), and a LEADING lyrics row (LayoutEngine.RowSkylinesOf). Named, not
    /// hidden.</item>
    /// <item>HIDDEN staves are gone from LilyPond's element list too
    /// (<c>filter_dead_elements</c>, :589).</item>
    /// <item>★ AN OSSIA PAIR IS SPRUNG LIKE ANY OTHER, since 2026-07-28. It used to be
    /// skipped here and left rigid, which cost +0.212184 on a squeezing page
    /// (<c>staff.ossia-pair.compressed.staff-staff-inside</c>, book OSSK) and, through the
    /// force that rigidity refuses, showed up in every other spring on that page.
    /// <para>
    /// ⚠️ THE SKIP WAS NEVER ONE QUANTITY, and deleting the two lines alone made the book
    /// WORSE, not better — measured: the pair fell to 8.350000 and the system spring flew to
    /// 17.350000 against LilyPond's 8.787816 / 11.151264, because the placement still put the
    /// ossia outside the chain while the chain now had a spring for it. THREE THINGS ARE ONE
    /// PORT, and the corpus only lands when all three are in:
    /// <list type="number">
    /// <item>this spring;</item>
    /// <item>the SPEC (<see cref="InterGroupSpec"/>) — an ossia has no staff-grouper, so
    /// LilyPond falls through to <c>default-staff-staff-spacing</c> and compresses at
    /// strength 1, not the grouper's 2;</item>
    /// <item>SPACEABILITY itself (<c>LayoutEngine.ClassifySystem</c> and the two other
    /// spellings of that predicate) — without it the page's anchor skips a LEADING ossia and
    /// the ossia is drawn into the top margin.</item>
    /// </list>
    /// </para>
    /// <para>
    /// ⚠️ WHAT IS STILL NOT PORTED, named rather than hidden: <c>staff-refpoint-extent</c>
    /// spans only spaceable staves in LilyPond too, so probe book LYROS reads 18.000000 where
    /// Lily# reads a two-staff span. Nothing measures it yet.
    /// </para></item>
    /// </list>
    /// </para>
    /// </remarks>
    internal ImmutableArray<StaffSpring> StaffSprings(
        MultiStaffScore score, ImmutableArray<StaffGroupLayout> groups,
        List<(VerticalSkyline Up, VerticalSkyline Down)> staffSkylines,
        LooseLinesBetween? looseLines = null)
    {
        if (groups.IsDefaultOrEmpty)
            return ImmutableArray<StaffSpring>.Empty;

        var sp = _options.StaffSpacing;
        // (model staff, its layout, the group it belongs to) in global staff order — the
        // order EnumerateStaves yields and the order the group layouts were built in.
        var flat = new List<(Staff Staff, StaffLayout Layout, StaffGroup Group, int GroupIndex)>();
        int gi = 0;
        foreach (var group in score.StaffGroups)
        {
            if (gi >= groups.Length)
                break;
            var groupLayout = groups[gi];
            for (int k = 0; k < group.Staves.Length && k < groupLayout.Staves.Length; k++)
                flat.Add((group.Staves[k], groupLayout.Staves[k], group, gi));
            gi++;
        }

        var builder = ImmutableArray.CreateBuilder<StaffSpring>();
        // CONSECUTIVE SPACEABLE, NOT ADJACENT — the walk this loop always claimed to be.
        // LILYPOND-REF: lily/page-layout-problem.cc:660-672 append_system — the loop springs
        // between consecutive `is_spaceable` elements and pushes everything else onto
        // `loose_lines`, so a non-spaceable line standing between two staves does not break
        // the pair; it becomes what the pair's floor is measured OVER (:919-925).
        // ⚠️ IT USED TO PAIR `flat[i]` WITH `flat[i + 1]` and drop the pair when either was
        // non-spaceable, which is the same walk only while nothing ever stands between two
        // staves. A BARE `lyrics` ROW IS ITS OWN ELEMENT OF THE ALIGNMENT — a note-bound
        // block is not, it is ink hanging off its staff — so `staff / lyrics / staff` left
        // the system with NO SPRING AT ALL, and a system with no spring hands
        // `LayoutEngine.CreatePages` its FIRST staff as the end of its chain
        // (`OriginToChainEnd`). The inter-system floor was then written between the two
        // systems' FIRST staves, and the second staff and the row below it fell outside the
        // quantity entirely. MEASURED on the reported book
        // (scratch/ベースタブLy/Untitled-6.lys, user report 2026-08-25): system 0's second
        // staff and system 1's first were drawn 0.470000 apart — through each other — where
        // the pair's own numbers put them 12.000000 apart.
        int upperEntry = -1;
        var between = new List<(Staff Staff, StaffLayout Layout)>();
        for (int i = 0; i < flat.Count; i++)
        {
            var entry = flat[i];
            // Hara-kiri leaves a hidden staff at the current Y with zero height, so it neither
            // draws nor takes room and is not an element of the alignment at all.
            // LILYPOND-REF: lily/page-layout-problem.cc:589 filter_dead_elements, which runs
            // BEFORE append_system's walk. ⚠️ THE SAME SELECTION ClassifySystem MAKES — a
            // hidden staff used to break the pair here while it was skipped there, so the two
            // views of one alignment disagreed about which staves are consecutive.
            if (entry.Layout.IsHidden)
                continue;
            // LILYPOND-REF: lily/page-layout-problem.cc:1173-1177 Page_layout_problem::is_spaceable
            // — the property, not the kind of line that happens to carry it.
            if (!StaffAffinity.IsSpaceable(entry.Staff.StaffAffinity))
            {
                // A line ABOVE the first spaceable staff LEADS the system and belongs to the
                // run the PREVIOUS system's chain closes, not to any pair here
                // (:948-990, LayoutEngine.BuildLooseChainEnds).
                if (upperEntry >= 0)
                    between.Add((entry.Staff, entry.Layout));
                continue;
            }
            if (upperEntry >= 0)
                AddSpring(flat[upperEntry], entry, between);
            upperEntry = i;
            between.Clear();
        }
        return builder.ToImmutable();

        void AddSpring(
            (Staff Staff, StaffLayout Layout, StaffGroup Group, int GroupIndex) up,
            (Staff Staff, StaffLayout Layout, StaffGroup Group, int GroupIndex) low,
            List<(Staff Staff, StaffLayout Layout)> lines)
        {
            // ★ THE PAIR IS SPRUNG WHATEVER STANDS BETWEEN IT (2026-08-26), which is
            // page-layout-problem.cc:660-672 unchanged: the loop springs between consecutive
            // `is_spaceable` elements and pushes everything else onto `loose_lines`, and it
            // never asks what KIND of line it pushed.
            // ⚠️ THIS WAS THE LAST READER OF A DECLINE WHOSE OTHER TWO WENT THE SAME DAY.
            // It used to return here when any line between the pair was not a LYRICS row, on
            // the account that the walk "reads its three steps off a line whose affinity is
            // UP" and that closing it needed "a point that measures the affinity-DOWN steps".
            // Both halves are answered: StaffAffinity.GetSpacingSpec is :1266-1342 entire and
            // the chain calls it per line now, and the points exist
            // (audit/lp-geometry lyrics.chord-lyric-run.*, book CHL1).
            // ⚠️ WHAT THE DECLINE COST WAS NOT THIS SPRING, IT WAS A FRAME. A system with no
            // spring at all tells LayoutEngine.CreatePages that its chain ends at its FIRST
            // staff (OriginToChainEnd), so the inter-system floor was written between the two
            // systems' FIRST refpoints — and on a two-staff system the second staff and
            // everything under it fell outside the quantity. MEASURED on the reported book
            // (scratch/ベースタブLy/Untitled-6.lys, `staff / chords / lyrics / staff`, user
            // report 2026-08-26): system 0's last staff and system 1's first were placed
            // 6.860000 apart where the pair's own numbers put them 12.000000 apart, and the
            // next system's section label and bar number printed through the instrument name.
            // The same book one row shorter reads 12.000000, which is why nothing saw it: a
            // row that FOLDS into its neighbouring staff (RenderSpecParser.FoldAdjacentRows)
            // leaves nothing between the pair, so the decline needs TWO rows to fire and no
            // book in the corpus had two.
            // ⚠️ NO OSSIA SKIP. An ossia pair is sprung like any other spaceable pair, and
            // the two lines that used to skip it here were worth +0.212184 on a page that
            // squeezes (audit/lp-geometry staff.ossia-pair.compressed.staff-staff-inside,
            // book OSSK: a rigid spring prints its ideal 9.000000 whatever force the page
            // solves, where LilyPond compresses to 8.787816). An ossia is a `\new Staff` and
            // declares no `staff-affinity`, so :1173-1177 makes it spaceable.
            var spec = up.GroupIndex == low.GroupIndex
                ? sp.StaffStaff
                : InterGroupSpec(up.Group, low.Group, sp);

            // Refpoint to refpoint, the frame every vertical spring in LilyPond works in, and
            // asked for DIRECTLY: this is the number LilyPond indexes out of
            // Align_interface's vector.
            // LILYPOND-REF: lily/page-layout-problem.cc:699-704 minimum_offsets_with_min_dist
            // — the spring's floor is that vector's [i] - [i+1], i.e. the alignment's own
            // minimum translation for the pair, NOT the distance the staves were drawn at.
            // ★ IT USED TO BE RECONSTRUCTED — `drawn - max(0, basic - minimum)` — because the
            // placement worked in the staff-TOP frame and the two only agreed up to half the
            // difference of the two staff heights. Both call sites of StaffGap now pass the
            // refpoint span, so the drawn distance IS the alignment answer and the
            // reconstruction had nothing left to correct. Verified byte-identical across the
            // suite and the ledger; what it removes is a Lily#-only expression, not a number.
            // ⚠️ THE SAME LOOSE LINES THE PLACEMENT WALKED, or this floor is computed against
            // a different alignment from the one that drew the staves, and the block would be
            // solved into a room it does not fit. THE ROWS BETWEEN ARE PART OF THAT WALK: a
            // note-bound block hangs off the upper staff and so comes first, then the rows
            // standing between the two staves in alignment order, each with the skylines
            // BuildAllStaffSkylines already seeded for it.
            var blocks = PairBlocks(
                looseLines?.Invoke(up.Layout.StaffIndex, low.Layout.StaffIndex),
                lines, staffSkylines, sp);

            double minimum = AlignmentMinimumWithSkylines(
                spec, staffSkylines, up.Layout.StaffIndex, low.Layout.StaffIndex, blocks);
            builder.Add(new StaffSpring(
                up.Layout.StaffIndex, low.Layout.StaffIndex, spec, minimum));
        }
    }

    /// <summary>
    /// A spaceable pair's run, assembled for the alignment walk: the upper staff's own
    /// note-bound block first, then the independent rows standing between the two staves in
    /// alignment order — each with the skylines <see cref="BuildAllStaffSkylines"/> already
    /// seeded for it and the <c>staff-affinity</c> / <c>nonstaff-*</c> set
    /// <c>get_spacing_spec</c> reads off that KIND of line.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:919-925 loose_lines — the run is collected
    /// in one order for one walk; a second assembly of it is how the spring's floor and the
    /// drawn placement come to describe different alignments.
    /// LILYPOND-REF: ly/engraver-init.ly:648-658 Lyrics and :719-723 ChordNames — which
    /// spec set a row carries is a fact about its context, not about the score.
    /// </remarks>
    private static IReadOnlyList<PairLooseLine>? PairBlocks(
        IReadOnlyList<PairLooseLine>? walked,
        IReadOnlyList<(Staff Staff, StaffLayout Layout)> rows,
        List<(VerticalSkyline Up, VerticalSkyline Down)> staffSkylines,
        StaffSpacingParameters sp)
    {
        if (rows.Count == 0)
            return walked;
        var all = new List<PairLooseLine>((walked?.Count ?? 0) + rows.Count);
        if (walked != null)
            all.AddRange(walked);
        foreach (var row in rows)
        {
            var sky = row.Layout.StaffIndex < staffSkylines.Count
                ? staffSkylines[row.Layout.StaffIndex]
                : default;
            all.Add(new PairLooseLine(
                sky.Up, sky.Down,
                row.Staff.StaffAffinity,
                row.Staff.IsLyricsTextRow ? sp.Lyrics : sp.ChordNames));
        }
        return all;
    }

    /// <summary>
    /// A staff's refpoint (its middle line) in the system's Y-up frame.
    /// </summary>
    internal static double StaffRefpoint(StaffLayout staff) => staff.Y - staff.Height / 2.0;

    /// <summary>
    /// The curved and bracketed spanners of ONE staff, in that staff's own frame — the
    /// side tables <see cref="BuildAllStaffSkylines"/> laid out for the alignment.
    /// </summary>
    /// <remarks>
    /// None of Slur, Tie or TupletBracket declares an <c>outside-staff-priority</c>, so all
    /// three are INSIDE-staff ink. ⚠️ THE ADDRESS FOR THAT IS NOT REPEATED HERE: each grob's
    /// citation lives where its ink is seeded — <c>SkylineBuilder.AddSlursToSkyline</c>,
    /// <c>AddTiesToSkyline</c> and <c>AddTupletBracketsToSkyline</c> — and a claim with two
    /// addresses has one that rots (HANDOFF 7.6).
    /// LILYPOND-REF: lily/axis-group-interface.cc:860-935 skyline_spacing seeds
    /// inside_staff_skylines from the group's own elements before any priority is applied,
    /// which is why an ink that declares no priority is in the profile from the start.
    /// <para>
    /// ⚠️ CARRIED OUT OF THE ROOM RATHER THAN RECOMPUTED, and that is the whole reason this
    /// type exists. A consumer that rebuilds its own profile needs these three, and asking
    /// the engravers a second time would be a second spelling of one answer AND a second
    /// walk of the staff — <see cref="StaffTupletBracketLayouts"/> runs
    /// <c>BeamDetector</c> over every voice of the staff, i.e. over the whole score, while
    /// the consumer that wants it runs once per SYSTEM. The room is memoised per system
    /// (<c>LayoutEngine.ComputeStaffSkylines</c>) and the annotation pass is not, so the
    /// recomputation would land on every keystroke. HANDOFF 1 (第89セッション E, 第90セッション ⑧').
    /// </para>
    /// </remarks>
    internal readonly record struct StaffInsideSpanners(
        ImmutableArray<SlurLayout> Slurs,
        ImmutableArray<TieLayout> Ties,
        ImmutableArray<TupletBracketLayout> TupletBrackets);

    /// <summary>One system's per-staff skylines and the inside-staff spanners they were
    /// built from, both indexed by global staff index. <c>PedalLines</c> is the pedal
    /// brackets the DOWN profiles were solved WITH -- the draw reads these instead of
    /// re-deriving (one computation, two readers; see PedalEngraver.SolveAndSeed).</summary>
    internal readonly record struct StaffSkylineSet(
        List<(VerticalSkyline Up, VerticalSkyline Down)> Skylines,
        List<StaffInsideSpanners> Spanners,
        List<(VerticalSkyline Up, VerticalSkyline Down)> Inside,
        List<ImmutableArray<PedalEngraver.SolvedPedalLine>> PedalLines,
        List<ImmutableArray<PedalEngraver.SolvedPedalRow>> PedalRows);

    /// <summary>
    /// Builds UP/DOWN skylines for every staff in the score.
    /// Returns a list indexed by global staff index.
    /// </summary>
    private StaffSkylineSet BuildAllStaffSkylines(
        MultiStaffScore score, SkylineBuilder skylineBuilder,
        ImmutableArray<MeasureLayout> measureLayouts, int systemIndex)
    {
        var result = new List<(VerticalSkyline Up, VerticalSkyline Down)>();
        var spanners = new List<StaffInsideSpanners>();
        var inside = new List<(VerticalSkyline Up, VerticalSkyline Down)>();
        var pedalLines = new List<ImmutableArray<PedalEngraver.SolvedPedalLine>>();
        var pedalRows = new List<ImmutableArray<PedalEngraver.SolvedPedalRow>>();

        // Each staff's own dynamics (tagged by StaffIndex) hang below it and must
        // widen the gap to the staff below; filter so a staff reserves room only
        // for its dynamics, cleared against its own voices.
        int staffIndex = 0;
        foreach (var group in score.StaffGroups)
        {
            foreach (var staff in group.Staves)
            {
                int thisStaff = staffIndex;
                var dynamics = score.Dynamics.IsDefaultOrEmpty
                    ? ImmutableArray<DynamicItem>.Empty
                    : score.Dynamics.Where(d => d.StaffIndex == thisStaff).ToImmutableArray();
                var beams = StaffBeamLayouts(score, staff, thisStaff, measureLayouts, systemIndex);
                // Ties and slurs before scripts: a script on a tie's bound note
                // clears the bow (its tie SUPPORT), and one under a slur rides off
                // the slur's bow (outside_slur_callback) — see
                // StaffArticulationLayouts — so the band the skyline reserves for
                // it has to be the raised one the drawn pass draws.
                var ties = StaffTieLayouts(score, staff, thisStaff, measureLayouts);
                // …and the avoid-slur #'inside marks before the SLURS, which is the other
                // half of the same rule and runs the other way: those the bow does not push
                // aside are the ones the bow is scored AROUND, so their boxes have to exist
                // before it is chosen. Legal because such a mark's placement is slur-free —
                // ArticulationEngraver.InsideSlurScriptLayouts.
                var slurs = StaffSlurLayouts(score, staff, thisStaff, measureLayouts, beams, ties);
                // The staff's own Scripts — a fermata reaching UP into the gap above it, a
                // marcato hanging DOWN into the gap below. See StaffArticulationLayouts for
                // why a Script belongs in this silhouette at all.
                var articulations = StaffArticulationLayouts(
                    score, staff, thisStaff, measureLayouts, beams, ties, slurs);
                // Scripts BEFORE tuplet brackets: the bracket's avoid-scripts term
                // reads their placed boxes, and both answers are in the same
                // staff-local frame (staffYAt null on both) — so the band the
                // skyline reserves is the raised bracket the drawn pass draws.
                var tupletBrackets = StaffTupletBracketLayouts(
                    score, staff, thisStaff, measureLayouts, beams, articulations);
                spanners.Add(new StaffInsideSpanners(slurs, ties, tupletBrackets));
                // ⚠️ CurrentIndent is where this system's clef is, and it is the same value
                // LayoutEngine hands BuildSystemSkylines as its systemLeft. The two
                // silhouettes have to agree about the clef or the page and the alignment
                // are spacing different pictures.
                // A rest another voice pushed out of the staff has to be RESERVED where it
                // was pushed to. The shift is a function of the music alone
                // (ElementCoordinator.CalculateRestNoteCollisions), so it is available this
                // early and the same answer reaches the renderer — one spelling, not two.
                // ⚠️ MEMOISED PER STAFF, and that is not an optimisation detail. This method
                // runs once per SYSTEM while the collision scan reads the WHOLE score, so
                // computing it here would make every single-system rebuild cost O(score) —
                // and a single-system rebuild is exactly what an edit triggers, since the
                // skylines are cached per system (LayoutEngine.ComputeStaffSkylines). The
                // preview's cost per keystroke is the thing that would have paid for it.
                var restShifts = RestCollisionsOf(staff);
                // THE inside-staff silhouette, built ONCE and travelling with the set: every
                // other consumer of this staff's inside profile (the stacker's seed, the
                // figured-bass drop, the chord row, the loose chain's closing staff) reads
                // this object instead of rebuilding its own subset of it.
                // LILYPOND-REF: lily/axis-group-interface.cc:914-935 inside_staff_skylines.
                // ...and this staff's FINGERINGS, which are inside-staff ink in LilyPond
                // (no outside-staff-priority) and were in no Lily# skyline at all until
                // 2026-08-10 — see SkylineBuilder.AddFingeringsToSkyline for the measurement
                // and ledger fingering.chord.dynamic-* for the books.
                var fingerings = StaffFingeringLayouts(staff, thisStaff, measureLayouts, score, beams);
                var insideSky = skylineBuilder.BuildInsideStaffSkylines(
                    staff, measureLayouts, articulations, tupletBrackets, slurs, ties, beams,
                    CurrentIndent, restShifts, fingerings);
                inside.Add(insideSky);
                // ...and the room's own view: the same profile with the priority-250 movers
                // placed on a COPY, so the shared one stays the inside profile.
                var sky = skylineBuilder.PlaceDynamicsOn(
                    insideSky, staff, dynamics, measureLayouts, beams,
                    articulationLayouts: articulations);

                // A staff carrying associated chord names (`staff X with chords ...`)
                // shows a chord-symbol row just above it. The row shares one baseline
                // per system, raised to clear THIS staff's own high notes, so it can
                // rise well above the top line — reserve it in the UP skyline or a low
                // note in the staff ABOVE overprints the chord symbols. (An independent
                // chord GRID row, IsChordRow, is its own staff and reserves its own band.)
                if (!score.ChordNames.IsDefaultOrEmpty
                    && score.ChordNames.Any(c => c.StaffIndex == thisStaff && !c.IsChordRow))
                    ReserveChordRowBand(sky.Up, measureLayouts, _options.StaffHeight / 2.0);

                // An independent chord ROW is a line of the alignment in its own right, and
                // what the lines above and below it are spaced against is its own symbol
                // ink. SkylineBuilder cannot see it — a ChordNameItem is not in the staff's
                // voices — so it is merged here, from the same X model the row is DRAWN with
                // (ChordNameEngraver.RowSkylines).
                // LILYPOND-REF: lily/page-layout-problem.cc:948-990 — a ChordNames context
                //   goes onto `loose_lines` and is distributed between the two spaceable
                //   staves that bracket it, measured by its own skyline.
                // ⚠️ THE FRAME IS THE ROW'S TEXT BASELINE (see RowSkylines), which is where
                // LilyPond's VerticalAxisGroup reference point is. Every OTHER entry in this
                // list is about its staff's MIDDLE LINE. The two agree in kind — both are
                // the element's own reference point — and differ from Lily#'s band model,
                // whose StaffLayout.Y is the band TOP.
                // ★ A LYRICS ROW IS SEEDED THE SAME WAY SINCE 2026-07-27. Its syllables were
                // always as real as the chord symbols; what kept them out was that no ledger
                // point measured them, so seeding would have moved a quantity nothing could
                // check (HANDOFF 1). Book LYRRV measures them now, and with the ink here the
                // row is spaced by LilyPond's own spec instead of Lily#'s band — see
                // SelectInterGroupSpec.
                // ⚠️ THE SAME FRAME AS THE CHORD ROW: the row's TEXT BASELINE, which is what
                // RefpointBelowTop returns for a text row and what LilyPond's VerticalAxisGroup
                // uses. For a multi-verse row that is VERSE 1's baseline, and the verses below
                // it are merged at the step the ALIGNMENT WALKS — the same
                // nonstaff-nonstaff-spacing the loose chain steps them by
                // (RowSkylinesAboutBaseline), not a flat constant.
                if (staff.IsTextRow)
                {
                    var rowInk = staff.IsLyricsTextRow
                        ? LyricRowInk(score, measureLayouts, thisStaff)
                        : ChordNameEngraver.RowSkylines(
                            score.TextMetrics, score.ChordNames, measureLayouts, thisStaff,
                            staff.PrimaryVoice.Measures);
                    sky.Up.Merge(rowInk.Up);
                    sky.Down.Merge(rowInk.Down);
                }

                // A figure row hangs below its own staff exactly as a chord row sits above
                // it, and the staff below has to clear it — LilyPond's
                // BassFigureAlignmentPositioning is an outside-staff grob of THIS staff's
                // axis group, so its stencil is in the skyline Align_interface walks. Merged
                // after the inside-staff profile is complete, because that profile is what
                // the row is placed against (the same order the priority passes run in).
                // ⚠️ Until 2026-07-30 the row was in the SYSTEM silhouette only, so it was
                // reserved between systems and nowhere between staves — measured against
                // LilyPond at 2.624795 short (ledger figbass.upper-staff.staff-gap), which is
                // the row's whole depth plus the nonstaff-unrelatedstaff padding.
                if (!score.FiguredBasses.IsDefaultOrEmpty)
                {
                    var fbInk = FiguredBassEngraver.RowInkBelowStaff(
                        score.FiguredBasses, measureLayouts, thisStaff,
                        staff.PrimaryVoice.Measures, sky.Down);
                    if (!fbInk.IsEmpty)
                        sky.Down.Merge(fbInk);
                }

                // Pedal brackets and text rows LAST among the below-staff occupants:
                // priority 1000 clears the figures (25) and the dynamics (250) already
                // merged above, which is LilyPond's ascending-priority order. Each solve
                // is the two-step every outside grob runs — its own side-position against
                // the INSIDE profile (its support), then the collision pass against the
                // accumulated one. The solved lines and rows travel with the set so the
                // draw uses the exact Y this profile reserved.
                var (bracketLines, mixedRows) = PedalEngraver.SolveAndSeed(
                    score, staff, thisStaff, measureLayouts, insideSky.Down, sky.Down);
                pedalLines.Add(bracketLines);
                var textRows = PedalEngraver.SolveAndSeedText(
                    score, staff, thisStaff, measureLayouts, insideSky.Down, sky.Down);
                // Mutually exclusive by style (mixed emits mixedRows, text emits
                // textRows), so the union is whichever is non-empty.
                pedalRows.Add(mixedRows.IsDefaultOrEmpty || mixedRows.Length == 0
                    ? textRows : mixedRows);

                result.Add(sky);
                staffIndex++;
            }
        }

        return new StaffSkylineSet(result, spanners, inside, pedalLines, pedalRows);
    }

    /// <summary>
    /// An independent lyrics ROW's own ink, about its text baseline — the lyric twin of
    /// <c>ChordNameEngraver.RowSkylines</c>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:948-990 — a Lyrics context goes onto
    /// <c>loose_lines</c> and is spaced by its OWN skyline between the two spaceable staves
    /// that bracket it, exactly as the ChordNames context above does. The two branches of the
    /// caller are one rule in the source.
    /// Goes through <see cref="LyricEngraver.ForGeometry"/> so the X it measures is the X the
    /// row is drawn at; a second X model here is the shape HANDOFF 5.2.1② names.
    /// ⚠️ THE WHOLE SCORE'S MEASURE LAYOUTS, not one system's — <c>RowBlockSkylines</c>
    /// selects by <c>MeasureIndex</c> and this list is score-wide, which is the pairing that
    /// overload wants (the positional one is the trap its remarks describe).
    /// </remarks>
    private static (VerticalSkyline Up, VerticalSkyline Down) LyricRowInk(
        MultiStaffScore score, ImmutableArray<MeasureLayout> measureLayouts, int staffIndex)
    {
        if (score.Lyrics.IsDefaultOrEmpty)
            return (new VerticalSkyline(VerticalDirection.Up),
                    new VerticalSkyline(VerticalDirection.Down));
        var engraver = LyricEngraver.ForGeometry(score);
        var verses = engraver.RowBlockSkylines(
            score.Lyrics, measureLayouts, 0, int.MaxValue, staffIndex);
        return engraver.RowSkylinesAboutBaseline(verses);
    }

    /// <summary>
    /// This staff's own tuplet brackets, positioned in the staff's own frame, so the
    /// skyline can reserve them.
    /// </summary>
    /// <remarks>
    /// The engraver is the SAME one the annotation pass runs; it is not re-implemented
    /// here. Two arguments make its answer staff-local rather than system-relative:
    /// <c>staffYAt</c> is null, so no staff offset is baked in and <c>*YUp</c> comes back
    /// measured from this staff's top line — exactly the skyline's own origin — and the
    /// per-staff dictionaries scope it to this staff's voices and measures.
    /// <para>
    /// The beam groups matter and cannot be skipped: a fully beamed tuplet draws no
    /// bracket (<c>bracket-visibility = if-no-beam</c>), and without them every beamed
    /// tuplet in the corpus would reserve a bracket that is never drawn. They are detected
    /// from the model alone (<see cref="BeamDetector"/> reads voices, time signature and
    /// tuplet spans), so they are available this early.
    /// </para>
    /// <para>
    /// The beam LAYOUTS matter too, since 2026-07-29: a suppressed tuplet's NUMBER is
    /// seeded (SkylineBuilder.AddTupletBracketsToSkyline), and it sits centred on the
    /// invisible bracket at the QUANTED beam edge + padding 1.1. Without them the engraver
    /// falls back to the bracket position built from the raw
    /// <see cref="EngravingDefaults.DefaultStemLength"/> stem tip, and on the probe pair
    /// that fallback read +0.260021 against LilyPond while the drawn beam edge was
    /// six-digit identical (ledger staff.staff.beamed-tuplet-number) — the seed must be
    /// the drawn geometry. The caller passes <see cref="StaffBeamLayouts"/>, the same
    /// per-staff beams the skyline itself reserves, so seed and draw share one beam model.
    /// </para>
    /// <para>
    /// The per-staff beams arrive stamped with the trivial system's <c>StaffIndex</c> 0
    /// (<see cref="StaffBeamLayouts"/> lays them out on a one-staff score), so they are
    /// RE-STAMPED to this staff's global index before the engraver sees them — the
    /// engraver's tab branch keys on <c>beam.StaffIndex == tuplet.StaffIndex</c>, and
    /// without the re-stamp a tab staff's branch selection would flip on where the staff
    /// happens to sit in the score. With it, a tab staff's number seeds from the same
    /// tab-beam edge the renderer draws, exactly as a notation staff's does.
    /// </para>
    /// </remarks>
    private ImmutableArray<TupletBracketLayout> StaffTupletBracketLayouts(
        MultiStaffScore score, Staff staff, int staffIndex,
        ImmutableArray<MeasureLayout> measureLayouts, ImmutableArray<BeamLayout> beamLayouts,
        ImmutableArray<ArticulationLayout> articulations = default)
    {
        if (score.TupletBrackets.IsDefaultOrEmpty)
            return ImmutableArray<TupletBracketLayout>.Empty;
        var staffTuplets = score.TupletBrackets
            .Where(t => t.StaffIndex == staffIndex)
            .ToImmutableArray();
        if (staffTuplets.IsEmpty)
            return ImmutableArray<TupletBracketLayout>.Empty;

        // The staff quantity, read from the per-staff memo. The per-voice fan that lived
        // here was BeamDetector.DetectBeamGroups(Score)'s own multi-voice fan spelt a
        // second time — see StaffBeamGroupsOf for the map and the single-voice
        // equivalence argument.
        var beamGroups = StaffBeamGroupsOf(score, staff, staffIndex);

        var staffBeams = beamLayouts.IsDefaultOrEmpty
            ? beamLayouts
            : beamLayouts.Select(b1 => new BeamLayout(
                b1.Group, b1.LeftY, b1.RightY, b1.LeftX, b1.RightX,
                b1.MemberXPositions, staffIndex, b1.SystemIndex,
                b1.MemberStaffIndices, b1.RestXPositions)).ToImmutableArray();
        return TupletBracketEngraver.Calculate(
            staffTuplets, measureLayouts, staff.PrimaryVoice.Measures,
            beamGroups, beamLayouts: staffBeams,
            forceStemUp: staff.IsMultiVoice,
            measuresByStaff: new Dictionary<int, ImmutableArray<Measure>>
                { [staffIndex] = staff.PrimaryVoice.Measures },
            voicesByStaff: new Dictionary<int, ImmutableArray<Voice>> { [staffIndex] = staff.Voices },
            staffYAt: null,
            staffByIndex: new Dictionary<int, Staff> { [staffIndex] = staff },
            scripts: articulations);
    }

    /// <summary>
    /// This staff's rest/note collision shifts, computed once per staff rather than once per
    /// system.
    /// </summary>
    /// <remarks>
    /// The answer depends on the STAFF alone — which voices sound together and where — so it
    /// is the same for every system, while the scan that produces it reads the whole score.
    /// A weak table rather than a plain dictionary because a <see cref="Staff"/> is replaced
    /// wholesale when the music is edited: the old entry must be collectable, not kept alive
    /// by the cache that made the previous layout fast.
    /// <para>
    /// ⚠️ ONE OWNER, BECAUSE FIVE PLACES RESERVE THIS REST. The alignment's silhouette is
    /// built here; the loose-line chain's closing staff, the figured-bass drop, the
    /// outside-staff stacker's seed and a chord row under a non-top staff each build their
    /// own profile from <c>SkylineBuilder.BuildStaffSkylines</c>, and every one of them has
    /// to place the rest where <c>Rest_collision</c> put it or it reserves an empty box and
    /// prints over the glyph. They read THIS memo (<c>LayoutEngine</c> passes the method
    /// itself down) rather than each calling the coordinator: a second call would be a second
    /// spelling of one answer, and it would also re-run a whole-score scan per system.
    /// </para>
    /// </remarks>
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        Staff, ImmutableDictionary<RestShiftKey, double>> _restCollisions = new();

    internal ImmutableDictionary<RestShiftKey, double> RestCollisionsOf(Staff staff)
        => _restCollisions.GetValue(
            staff, s => _elementCoordinator.CalculateRestNoteCollisions(s));

    /// <summary>
    /// This staff's own Scripts, laid out in the staff's own frame so the skyline can reserve
    /// the band they occupy outside the staff. The mirror of
    /// <see cref="StaffTupletBracketLayouts"/>, and the same engraver the drawn pass runs.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:937-978 <c>skyline_spacing</c> — a staff's
    /// <c>vertical-skylines</c> is <c>inside_staff_skylines</c> merged with every outside-staff
    /// grob the collision pass has placed (<c>add_grobs_of_one_priority</c>, :969-971), and it
    /// is that merged pair <c>Align_interface</c> reads for staff-to-staff spacing
    /// (lily/align-interface.cc:71-87 <c>get_skylines</c>). So a Script is in the silhouette
    /// either way: the fermata family DECLARES an outside-staff-priority and joins by the
    /// second route (scm/script.scm, all seven at 75), and every other script declares none and
    /// is an inside-staff seed from the start.
    /// <para>
    /// ⚠️ UNTIL 2026-08-04 ONLY A TAB STAFF'S MARKS WERE HERE, through a tab-only copy of the
    /// placement. So an ordinary fermata on a lower part was reserved nowhere and the staff
    /// above kept its gap: on the reported book (an SATB chorale, every part ending on a
    /// fermata, every staff carrying the verse) the alto's syllable and the tenor's fermata
    /// were drawn in the same place, three times over. Nothing in the corpus saw it — the one
    /// script-versus-lyric fixture is a SINGLE staff, where a different path clears it
    /// (LayoutEngine.AugmentSkylinesWithScripts over the SYSTEM silhouette).
    /// </para>
    /// <para>
    /// ⚠️ THE SAME ENGRAVER, NOT A SECOND SPELLING: two arguments make its answer staff-local
    /// rather than system-relative, exactly as <see cref="StaffTupletBracketLayouts"/> does it.
    /// <c>staffYAt</c> is null, so no staff offset is baked in and every <c>YUp</c> comes back
    /// about this staff's middle line — the per-staff skyline's own origin — and the
    /// single-entry dictionaries scope the voice and staff lookups to this staff. What that
    /// buys is that a tab staff's marks now come from the engraver's OWN tab branch instead of
    /// a copy of it, so the band reserved is the geometry that will be drawn.
    /// </para>
    /// <para>
    /// The BEAMS matter and cannot be skipped: a beamed member's stem ends on the beam, and a
    /// stem-coupled script sits past that tip — an unbeamed formula would reserve the band in
    /// the wrong place. They are restamped with this staff's index because
    /// <see cref="StaffBeamLayouts"/> builds them on a trivial one-staff system (index 0) and
    /// the engraver keys its stem tips by staff — the same restamp
    /// <see cref="StaffTupletBracketLayouts"/> makes, for the same reason.
    /// </para>
    /// </remarks>
    /// <summary>
    /// One staff's fingerings, in that staff's own frame — the reservation half of the
    /// placement the same engraver does for the page.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE SAME ENGRAVER, NOT A SECOND SPELLING, exactly as
    /// <see cref="StaffArticulationLayouts"/> argues for the scripts: a
    /// <see cref="FingeringLayout"/>'s <c>YUp</c> is already about its own staff's middle
    /// line, so the answer needs no staff offset and no translation — it is the drawn
    /// geometry, asked for again.
    /// <para>
    /// ⚠️ THIS IS THE ISLAND ANSWER, and it is the right one to reserve. The script-column
    /// walk can still raise an ABOVE fingering that a bow outranks
    /// (<c>ArticulationEngraver.CalculateWithFingerings</c>), and that walk runs after this
    /// pass — so a fingering under a script reserves the band it would have had alone. The
    /// same approximation the scripts' own remark records for the movers; the books that
    /// measure a fingering's reservation (ledger fingering.chord.dynamic-*) reach the DOWN
    /// side, where nothing outranks anything.
    /// </para>
    /// </remarks>
    private static ImmutableArray<FingeringLayout> StaffFingeringLayouts(
        Staff staff, int staffIndex, ImmutableArray<MeasureLayout> measureLayouts,
        MultiStaffScore score, ImmutableArray<BeamLayout> beamLayouts = default)
    {
        var staffScore = new Score(
            staff.PrimaryVoice, score.TimeSignature, score.KeySignature,
            LayoutEngine.ClefToString(staff.Clef), score.Tempo, score.Title, score.Composer);
        // The beams arrive stamped with the trivial one-staff system's index 0 and the
        // engraver keys its stem tips by staff, so they are restamped to meet the score's
        // real index — the same restamp StaffArticulationLayouts makes, for the same reason.
        var localBeams = beamLayouts.IsDefaultOrEmpty
            ? beamLayouts
            : beamLayouts.Select(b => new BeamLayout(
                b.Group, b.LeftY, b.RightY, b.LeftX, b.RightX,
                b.MemberXPositions, staffIndex, b.SystemIndex,
                b.MemberStaffIndices, b.RestXPositions)).ToImmutableArray();
        return FingeringEngraver.Calculate(staffScore, measureLayouts, staffIndex, localBeams);
    }

    private ImmutableArray<ArticulationLayout> StaffArticulationLayouts(
        MultiStaffScore score, Staff staff, int staffIndex,
        ImmutableArray<MeasureLayout> measureLayouts, ImmutableArray<BeamLayout> beamLayouts,
        ImmutableArray<TieLayout> tieLayouts, ImmutableArray<SlurLayout> slurLayouts)
    {
        if (score.Articulations.IsDefaultOrEmpty)
            return ImmutableArray<ArticulationLayout>.Empty;
        // ⚠️ THIS SYSTEM'S MEASURES, not the score's. The engraver drops a mark whose measure
        // is not in the layouts it was handed, so the answer is the same either way — but the
        // WORK is not: this method runs once per system, so filtering by staff alone made a
        // rebuild cost O(marks in the whole score) where an edit only invalidates one system.
        var systemMeasures = new HashSet<int>();
        foreach (var ml in measureLayouts)
            systemMeasures.Add(ml.MeasureIndex);
        var staffArticulations = score.Articulations
            .Where(a => a.StaffIndex == staffIndex
                        && systemMeasures.Contains(a.MeasureIndex)
                        && ArticulationEngraver.IsSidePositionedScript(a.Type))
            .ToImmutableArray();
        if (staffArticulations.IsEmpty)
            return ImmutableArray<ArticulationLayout>.Empty;

        var staffBeams = beamLayouts.IsDefaultOrEmpty
            ? beamLayouts
            : beamLayouts.Select(b => new BeamLayout(
                b.Group, b.LeftY, b.RightY, b.LeftX, b.RightX,
                b.MemberXPositions, staffIndex, b.SystemIndex,
                b.MemberStaffIndices, b.RestXPositions)).ToImmutableArray();
        // Restamped with this staff's index for the same reason the beams are:
        // StaffTieLayouts builds them on a trivial one-staff system (index 0), and
        // the engraver keys its tie supports by the script's staff.
        var staffTies = tieLayouts.IsDefaultOrEmpty
            ? tieLayouts
            : tieLayouts.Select(t => t with { StaffIndex = staffIndex }).ToImmutableArray();
        var staffSlurs = slurLayouts.IsDefaultOrEmpty
            ? slurLayouts
            : slurLayouts.Select(s => s with { StaffIndex = staffIndex }).ToImmutableArray();

        var staffScore = new Score(
            staff.PrimaryVoice, score.TimeSignature, score.KeySignature,
            LayoutEngine.ClefToString(staff.Clef), score.Tempo, score.Title, score.Composer);
        return ArticulationEngraver.Calculate(
            staffScore, staffArticulations, measureLayouts,
            measuresByStaff: new Dictionary<int, ImmutableArray<Measure>>
                { [staffIndex] = staff.PrimaryVoice.Measures },
            staffYAt: null,
            staffByIndex: new Dictionary<int, Staff> { [staffIndex] = staff },
            beamLayouts: staffBeams,
            tieLayouts: staffTies,
            slurLayouts: staffSlurs);
    }

    /// <summary>
    /// One staff seen as a score of its own — every voice it has, so the detectors that
    /// walk a <see cref="Score"/> see the same music the drawn pass does.
    /// </summary>
    /// <remarks>
    /// ⚠️ ALL THE VOICES, and that is the whole of this helper. Until 2026-08-04 the slur and
    /// tie paths built this from <c>PrimaryVoice</c> alone while
    /// <see cref="StaffBeamLayouts"/> — the third member of the same family, whose remark
    /// already says the score MUST expose all voices — branched on the voice count. So a
    /// SECOND voice's slur was laid out nowhere and reserved nowhere: MEASURED on one book,
    /// the alignment's minimum for a staff pair read 8.095000 with a drooping second-voice
    /// slur and 8.095000 without it, against 9.512596 once the voice was visible — the same
    /// 1.417596 the identical slur contributes from the primary voice.
    /// <para>
    /// ⚠️ NOT A SECOND SPELLING OF THE VOICE TEST: the three call sites used to disagree
    /// about it, which is how one of them could be wrong while the suite stayed green.
    /// </para>
    /// </remarks>
    /// <summary>
    /// One staff's staff-local <see cref="Score"/> and the slur and tie ITEMS detected from
    /// it — computed once per staff rather than once per staff PER SYSTEM.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE DETECTION IS A WHOLE-SCORE WALK AND THE CALLER RUNS PER SYSTEM, which is the
    /// same shape <see cref="RestCollisionsOf"/> exists for.
    /// <c>SlurDetector.DetectSlurs</c> and <c>TieDetector.DetectTies</c> walk every item of
    /// every voice (<c>VoiceScan.WalkVoiceItems</c>), while
    /// <see cref="BuildAllStaffSkylines"/> asks once per (system, staff) — so a score of S
    /// systems paid that walk S times for an answer that cannot differ. The SCORING below it
    /// is per-system work and stays where it is; only the detection is hoisted.
    /// <para>
    /// ⚠️ SOUND FOR THE SAME REASON, TOO: both detectors read the score's VOICES and nothing
    /// else about it — <c>WalkVoiceItems</c> and, for the slur's default direction,
    /// <c>score.Voices.Length</c>. Every one of those is a function of this <see cref="Staff"/>,
    /// so the answer is the same for every system and for any surrounding score. A weak table
    /// because a <c>Staff</c> is replaced wholesale when the music is edited.
    /// </para>
    /// <para>
    /// ⚠️⚠️ THIS MEMO DOES NOT PAY BACK THE COST OF <see cref="StaffLocalScore"/>, and it was
    /// written believing it would. COUNTED on a four-staff, four-system score whose every
    /// voice carries slurs, ties and tuplets: the detection ran 16 times per render before
    /// (4 staves × 4 systems) and 4 after — and the allocation moved 125310 KB → 125232 KB,
    /// i.e. 0.06%. The detection walk was never the expense. What costs is the per-system
    /// SCORING of the second voice's bows, which is irreducible: <c>LayoutSlurs</c> already
    /// scores only the slurs of the system it was handed, so each bow is scored exactly once
    /// across the score, and a bow that is not laid out cannot be reserved. The 8.1% that
    /// change costs on such a score buys the room its second voice; the only way to not pay
    /// it is to go back to reserving nothing (HANDOFF 1).
    /// ⇒ ★ KEPT ANYWAY, because 12 redundant whole-score walks per render is work nobody
    /// needs whatever it measures — the same judgement <see cref="RestCollisionsOf"/> was
    /// made on, and that one was worth 2× a whole-score scan.
    /// </para>
    /// </remarks>
    private sealed class StaffSpannerItems
    {
        public required Score LocalScore { get; init; }
        public required ImmutableArray<SlurItem> Slurs { get; init; }
        public required ImmutableArray<TieItem> Ties { get; init; }
    }

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        Staff, StaffSpannerItems> _staffSpannerItems = new();

    private StaffSpannerItems StaffSpannerItemsOf(MultiStaffScore score, Staff staff)
        => _staffSpannerItems.GetValue(staff, s =>
        {
            var local = StaffLocalScore(score, s);
            return new StaffSpannerItems
            {
                LocalScore = local,
                Slurs = _elementCoordinator.DetectSlurs(local),
                Ties = _elementCoordinator.DetectTies(local),
            };
        });

    private static Score StaffLocalScore(MultiStaffScore score, Staff staff)
        => staff.Voices.Length > 1
            ? new Score(
                staff.Voices, score.TimeSignature, score.KeySignature,
                LayoutEngine.ClefToString(staff.Clef), score.Tempo, score.Title, score.Composer)
            : new Score(
                staff.PrimaryVoice, score.TimeSignature, score.KeySignature,
                LayoutEngine.ClefToString(staff.Clef), score.Tempo, score.Title, score.Composer);

    /// <summary>
    /// This staff's own slurs, laid out in the staff's own frame so the skyline can
    /// reserve their bows. The mirror of <see cref="StaffTupletBracketLayouts"/>.
    /// </summary>
    /// <remarks>
    /// Slurs are scored against <see cref="SystemLayout"/>s, which do not exist yet when
    /// staves are being spaced — so build a TRIVIAL one-staff system with this staff at
    /// offset 0. The slur scorer's <c>staffMiddleDown</c> then carries no system offset
    /// (<c>StaffOffsetInSystemDown</c> of the sole staff is 0), and every returned
    /// <c>*YUp</c> is measured from the staff's top line, exactly the per-staff skyline's
    /// own origin — the same frame <see cref="StaffTupletBracketLayouts"/> produces. This
    /// reuses <see cref="ElementCoordinator.LayoutSlurs(ImmutableArray{Model.SlurItem},
    /// Model.Score, ImmutableArray{SystemLayout}, int, Model.Staff, ImmutableArray{Model.GraceNoteItem},
    /// ImmutableArray{Model.BeamLayout}, System.Func{ImmutableArray{InsideSlurScript}})"/>
    /// whole rather than a second copy
    /// of its scoring. Slur geometry is independent of inter-staff spacing (it is fixed by
    /// note X and pitch), so computing it before the spacing is decided is sound.
    /// <para>
    /// Beams are not passed (default): they only shift a slur's ENDPOINT attachment to a
    /// beamed stem tip, never the peak that binds the gap. (⚠️ Not for want of layouts —
    /// <see cref="StaffBeamLayouts"/> exists and <see cref="StaffTupletBracketLayouts"/>
    /// consumes it since 2026-07-29; the slur trade stands on the endpoint argument alone.)
    /// </para>
    /// </remarks>
    private ImmutableArray<SlurLayout> StaffSlurLayouts(
        MultiStaffScore score, Staff staff, int staffIndex,
        ImmutableArray<MeasureLayout> measureLayouts,
        ImmutableArray<BeamLayout> beamLayouts = default,
        ImmutableArray<TieLayout> tieLayouts = default)
    {
        var staffLayout = new StaffLayout(
            0, staff.Clef, Y: 0, Height: _options.StaffHeight,
            StaffAffinity: staff.StaffAffinity);
        var group = StaffGroupLayout.CreateSingle(staffLayout, 0, _options.StaffHeight);
        var system = new SystemLayout(
            SystemIndex: 0, Y: 0,
            Width: _options.ContentWidth,
            PrefixWidth: 0,
            Measures: measureLayouts,
            StaffGroups: ImmutableArray.Create(group),
            Indent: 0);
        var items = StaffSpannerItemsOf(score, staff);
        // This system's bars only — the same scoping StaffArticulationLayouts argues for:
        // the engraver drops a mark whose measure is not in the layouts it was handed, so
        // the answer is the same either way but the WORK is per system.
        var systemMeasures = new HashSet<int>();
        foreach (var ml in measureLayouts)
            systemMeasures.Add(ml.MeasureIndex);
        var staffScripts = ArticulationEngraver.SidePositionedScriptsOf(
            score.Articulations, staffIndex, systemMeasures);
        // The scripts arrive stamped with the score's REAL staff index and the engraver
        // keys its stem tips and tie supports by it, so the beams and ties are restamped
        // UP to meet them — the same restamp StaffArticulationLayouts makes, for the same
        // reason (their own builders lay them out on a trivial one-staff system, index 0).
        // The frame is unaffected: staffYAt null means every YUp comes back about the
        // script's own staff middle whatever that index reads, which is the origin the
        // trivial system's slur scorer measures against.
        var localBeams = beamLayouts.IsDefaultOrEmpty
            ? beamLayouts
            : beamLayouts.Select(b => new BeamLayout(
                b.Group, b.LeftY, b.RightY, b.LeftX, b.RightX,
                b.MemberXPositions, staffIndex, b.SystemIndex,
                b.MemberStaffIndices, b.RestXPositions)).ToImmutableArray();
        var localTies = tieLayouts.IsDefaultOrEmpty
            ? tieLayouts
            : tieLayouts.Select(t => t with { StaffIndex = staffIndex }).ToImmutableArray();
        return _elementCoordinator.LayoutSlurs(
            items.Slurs, items.LocalScore, ImmutableArray.Create(system),
            staffIndex: 0, staff, score.GraceNotes,
            insideScripts: staffScripts.IsEmpty ? null : () =>
                ArticulationEngraver.InsideSlurScriptLayouts(
                    items.LocalScore, staffScripts, measureLayouts,
                    measuresByStaff: new Dictionary<int, ImmutableArray<Measure>>
                        { [staffIndex] = staff.PrimaryVoice.Measures },
                    staffYAt: null,
                    staffByIndex: new Dictionary<int, Staff> { [staffIndex] = staff },
                    beamLayouts: localBeams,
                    tieLayouts: localTies));
    }

    /// <summary>
    /// This staff's own ties, laid out in the staff's own frame so the skyline can reserve
    /// their bows — the tie analogue of <see cref="StaffSlurLayouts"/>. Same trivial
    /// one-staff-at-offset-0 system, reusing
    /// <see cref="ElementCoordinator.LayoutTies(ImmutableArray{Model.TieItem}, Model.Score,
    /// ImmutableArray{SystemLayout}, int, Model.Staff)"/>
    /// whole; tie geometry is fixed by note X and pitch, so it is sound to compute before
    /// the inter-staff spacing is decided.
    /// </summary>
    private ImmutableArray<TieLayout> StaffTieLayouts(
        MultiStaffScore score, Staff staff, int staffIndex,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        var staffLayout = new StaffLayout(
            0, staff.Clef, Y: 0, Height: _options.StaffHeight,
            StaffAffinity: staff.StaffAffinity);
        var group = StaffGroupLayout.CreateSingle(staffLayout, 0, _options.StaffHeight);
        var system = new SystemLayout(
            SystemIndex: 0, Y: 0,
            Width: _options.ContentWidth,
            PrefixWidth: 0,
            Measures: measureLayouts,
            StaffGroups: ImmutableArray.Create(group),
            Indent: 0);
        var items = StaffSpannerItemsOf(score, staff);
        return _elementCoordinator.LayoutTies(
            items.Ties, items.LocalScore, ImmutableArray.Create(system),
            staffIndex: 0, staff);
    }

    /// <summary>
    /// This staff's own beams, laid out in the staff's own frame so the skyline can reserve
    /// their outer edges — the beam analogue of <see cref="StaffSlurLayouts"/>. Reuses
    /// <see cref="ElementCoordinator.LayoutBeams"/> whole on the same trivial one-staff system,
    /// exactly as the final pass computes beams (<c>LayoutEngine.LayoutAllSpanners</c>).
    /// </summary>
    /// <remarks>
    /// A beam's Y is measured from the staff MIDDLE (half-space positions) and its X from the
    /// measure layout — both independent of the inter-staff spacing — so computing it before
    /// that spacing is decided is sound, and it matches the drawn beam.
    /// <para>
    /// The score MUST expose all voices, as the final pass does: auto-beaming runs per voice, so
    /// a second-voice beam — the shape audit/lp-geometry BMD/BMU measure — never forms on a
    /// primary-voice-only score. Tuplet spans are passed because auto beams break at tuplet
    /// boundaries (<see cref="BeamDetector"/>), so the beams here are the ones the renderer draws.
    /// </para>
    /// <para>
    /// Internal so <c>LayoutEngine</c> can compute the SAME per-staff beams for the
    /// system silhouette (<c>SkylineBuilder.BuildSystemSkylines</c>) — one beam model
    /// for both skylines, the way <c>BuildStaffSkylines</c> and this pass already share
    /// one. audit/lp-geometry system.beam-{under,over}-notes.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// ⚠️ <paramref name="systemIndex"/> IS THE REAL SYSTEM'S, not the trivial layout's 0. The
    /// trivial system exists to give the beams the staff's own frame; the index the beams are
    /// STAMPED with has to be the one their X positions actually belong to, or the attribution
    /// would lie about a quantity a consumer selects on (BeamLayout.SystemIndex). It is inert
    /// to the layout itself — a one-system measure map has one group whatever it is numbered.
    /// </remarks>
    internal ImmutableArray<BeamLayout> StaffBeamLayouts(
        MultiStaffScore score, Staff staff, int staffIndex,
        ImmutableArray<MeasureLayout> measureLayouts, int systemIndex)
    {
        var staffLayout = new StaffLayout(
            0, staff.Clef, Y: 0, Height: _options.StaffHeight,
            StaffAffinity: staff.StaffAffinity);
        var group = StaffGroupLayout.CreateSingle(staffLayout, 0, _options.StaffHeight);
        var system = new SystemLayout(
            SystemIndex: systemIndex, Y: 0,
            Width: _options.ContentWidth,
            PrefixWidth: 0,
            Measures: measureLayouts,
            StaffGroups: ImmutableArray.Create(group),
            Indent: 0);
        var staffScore = StaffBeamScoreOf(score, staff, staffIndex);
        return _elementCoordinator.LayoutBeams(
            staffScore, ImmutableArray.Create(system), staffIndex: 0,
            StaffBeamGroupsOf(score, staff, staffIndex));
    }

    /// <summary>
    /// The score one staff's beams are detected and laid out on: every voice of the staff
    /// (auto-beaming runs per voice, so a second-voice beam never forms on a
    /// primary-voice-only score) and only THIS staff's tuplets (beam detection breaks at
    /// tuplet boundaries by note index, so a tuplet on another staff would split a beam at
    /// a colliding index).
    /// </summary>
    /// <remarks>
    /// ONE HOME ON PURPOSE: this construction used to be spelt twice — here and in
    /// <c>LayoutEngine.RunPreliminaryAnnotationPass</c> — and the beam memo's soundness
    /// rests on the spellings agreeing (HANDOFF §5.2.1②). The detector reads exactly three
    /// things of this score — Voices, TimeSignature, TupletBrackets — and nothing else
    /// (<see cref="BeamDetector"/> is stateless and its Score entry touches only those), so
    /// the clef/tempo/header fields here serve the LAYOUT call, not the detection.
    /// </remarks>
    internal static Score StaffBeamScoreOf(MultiStaffScore score, Staff staff, int staffIndex)
    {
        var staffTuplets = score.TupletBrackets.IsDefaultOrEmpty
            ? ImmutableArray<TupletBracketItem>.Empty
            : score.TupletBrackets.Where(t => t.StaffIndex == staffIndex).ToImmutableArray();
        return staff.Voices.Length > 1
            ? new Score(
                staff.Voices, score.TimeSignature, score.KeySignature,
                LayoutEngine.ClefToString(staff.Clef), score.Tempo, score.Title, score.Composer,
                tupletBrackets: staffTuplets)
            : new Score(
                staff.PrimaryVoice, score.TimeSignature, score.KeySignature,
                LayoutEngine.ClefToString(staff.Clef), score.Tempo, score.Title, score.Composer,
                tupletBrackets: staffTuplets);
    }

    private sealed record StaffBeamGroupsEntry(
        int StaffIndex, TimeSignature TimeSignature,
        ImmutableArray<TupletBracketItem> ScoreTuplets, ImmutableArray<BeamGroup> Groups);

    private sealed record BeamDetectionEntry(
        ImmutableArray<Voice> Voices, TimeSignature TimeSignature,
        ImmutableArray<TupletBracketItem> Tuplets, ImmutableArray<BeamGroup> Groups);

    /// <summary>Detections keyed on their own INPUT, so two callers asking for the same
    /// three things detect once between them — see <see cref="BeamGroupsOf"/>.</summary>
    /// <remarks>
    /// ⚠️ A LIST PER VOICE, NOT ONE SLOT. The staff quantity and the annotation quantity
    /// share a first voice on every single-voice score, so a one-slot table would have them
    /// evict each other and miss every time — the shape <c>_pagingAugments</c> is on record
    /// for. The list holds one entry per distinct input; in practice that is two, and it is
    /// capped so a score that somehow keeps producing new ones cannot grow it without bound.
    /// </remarks>
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        Voice, List<BeamDetectionEntry>> _beamGroupsByInput = new();

    /// <summary>How many distinct detection inputs are remembered per voice.</summary>
    private const int BeamDetectionEntriesPerVoice = 4;

    /// <summary>
    /// The beam groups of one DETECTION INPUT, detected once however many callers ask.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE KEY IS THE DETECTOR'S WHOLE INPUT AND NOTHING ELSE. <see cref="BeamDetector"/>
    /// holds no instance state and its <c>Score</c> entry reads exactly three things —
    /// <c>Voices</c>, <c>TimeSignature</c>, <c>TupletBrackets</c> — so two Scores agreeing on
    /// those three MUST detect the same groups whatever else differs (clef, tempo, title,
    /// header: all of them serve the LAYOUT, as <see cref="StaffBeamScoreOf"/>'s remarks
    /// already record). That is why this memo can be keyed on the inputs rather than on any
    /// one caller's notion of which score it is holding.
    /// <para>
    /// WHAT IT FOLDS (session 192): the STAFF quantity (<see cref="StaffBeamGroupsOf"/>) and
    /// the ANNOTATION quantity (<c>LayoutEngine.RunPreliminaryAnnotationPass</c>'s
    /// prelimScore) are separate homes BY CONSTRUCTION — the staff quantity takes every voice
    /// of its staff and only its own staff's tuplets, the annotation quantity takes the
    /// primary content staff's PRIMARY voice against the WHOLE score's tuplet list — and on a
    /// multi-voice or multi-staff score they genuinely differ. On a single-voice, single-staff
    /// score they are the same three inputs, and each was detecting them from scratch:
    /// MEASURED on a perf-plain1k keystroke, 2.61 MB apiece of 56.7 MB. This does not merge
    /// the two homes (a merge would have to reason about WHEN they coincide, and get it
    /// wrong silently); it compares the inputs and lets the answer be shared when they are
    /// in fact the same.
    /// </para>
    /// <para>
    /// SOUNDNESS: voices by REFERENCE (a ConditionalWeakTable keys that way too, so an entry
    /// dies with the music that made it — the argument <see cref="StaffBeamGroupsOf"/> makes
    /// for the Staff instance); the meter by value; the tuplet list by CONTENT, because the
    /// staff quantity's list is a fresh filtered array every time and identity would never
    /// match. Getting the key wrong returns the wrong beams silently, which is why the
    /// comparison is spelt out rather than inferred — and why
    /// <c>BeamDetectionInputMemoTests</c> asserts both directions: same inputs share, and a
    /// list differing by one tuplet does not.
    /// </para>
    /// </remarks>
    internal ImmutableArray<BeamGroup> BeamGroupsOf(Score detectionScore)
    {
        var voices = detectionScore.Voices;
        var tuplets = detectionScore.TupletBrackets;
        if (voices.IsDefaultOrEmpty)
            return _elementCoordinator.DetectBeamGroups(detectionScore);

        if (_beamGroupsByInput.TryGetValue(voices[0], out var entries))
        {
            foreach (var e in entries)
                if (e.TimeSignature == detectionScore.TimeSignature
                    && SameVoices(e.Voices, voices)
                    && SameTuplets(e.Tuplets, tuplets))
                    return e.Groups;
        }
        else
        {
            entries = new List<BeamDetectionEntry>(2);
            _beamGroupsByInput.Add(voices[0], entries);
        }

        var groups = _elementCoordinator.DetectBeamGroups(detectionScore);
        if (entries.Count == BeamDetectionEntriesPerVoice)
            entries.RemoveAt(0);
        entries.Add(new BeamDetectionEntry(
            voices, detectionScore.TimeSignature, tuplets, groups));
        return groups;
    }

    /// <summary>Voices compared by REFERENCE: two content-equal voices are different music
    /// as far as this memo is concerned, which only ever costs a miss.</summary>
    private static bool SameVoices(ImmutableArray<Voice> a, ImmutableArray<Voice> b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (!ReferenceEquals(a[i], b[i])) return false;
        return true;
    }

    /// <summary>Tuplet lists compared by CONTENT, in order: the staff quantity builds its
    /// list by filtering the score's on every call, so identity would never match even when
    /// the two lists hold the same brackets.</summary>
    private static bool SameTuplets(
        ImmutableArray<TupletBracketItem> a, ImmutableArray<TupletBracketItem> b)
    {
        if (a.IsDefaultOrEmpty) return b.IsDefaultOrEmpty;
        if (b.IsDefaultOrEmpty || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (!Equals(a[i], b[i])) return false;
        return true;
    }

    /// <summary>One staff's detected beam groups, computed once per staff and read by every
    /// consumer of the staff quantity — see <see cref="StaffBeamGroupsOf"/>.</summary>
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        Staff, StaffBeamGroupsEntry> _staffBeamGroups = new();

    /// <summary>
    /// This staff's beam groups — <see cref="BeamDetector"/> run on
    /// <see cref="StaffBeamScoreOf"/> — detected once and shared by every consumer of that
    /// quantity, where each consumer used to run its own detection per keystroke
    /// (HANDOFF §1 ⒫: ~17–22 ms per detection at 1000 measures, times the caller count).
    /// </summary>
    /// <remarks>
    /// WHO DETECTS, ON WHICH SCORE, WHEN — the map this memo folds (session 139):
    /// <list type="bullet">
    /// <item>THE STAFF QUANTITY (this memo): all of the staff's voices + the score's time
    /// signature + this staff's tuplets. Four consumers, every one of which detected it
    /// itself before this memo: the preliminary per-staff beam pass
    /// (<c>LayoutEngine.LayoutPreliminaryStaffBeams</c>, every keystroke × every staff), the
    /// edge-staff beams seeding the first page Y and each system's silhouette
    /// (<c>LayoutEngine</c>'s <c>EdgeStaffBeams</c> → <see cref="StaffBeamLayouts"/>,
    /// unconditional + per system-skyline miss), the per-staff skylines' beams
    /// (<c>BuildAllStaffSkylines</c> → <see cref="StaffBeamLayouts"/>, per staff-skyline
    /// miss) and the tuplet bracket-visibility groups
    /// (<see cref="StaffTupletBracketLayouts"/>, same misses, tuplet staves only).</item>
    /// <item>THE ANNOTATION QUANTITY IS NOT THIS ONE and stays out: the primary staff's
    /// PRIMARY VOICE against the WHOLE score's tuplet list, detected once per layout and
    /// carried prelim→final (<c>PreliminaryPass.AnnotationBeamGroups</c>). On a multi-voice
    /// or multi-staff score it differs from every staff quantity (no second voice, no
    /// stem-direction forcing, foreign staves' tuplets present).</item>
    /// <item>THE COLLECT-PHASE PROBE (<c>MeasureCollector.ResolveBeamStemDirections</c>)
    /// runs before any <see cref="Staff"/> exists, to bake the stem directions INTO the
    /// items this detection then reads — it cannot share by construction.</item>
    /// </list>
    /// SOUNDNESS: keyed on the <see cref="Staff"/> INSTANCE — replaced wholesale when the
    /// music is edited, the same argument <c>_restCollisions</c> makes — and validated
    /// against the only two detection inputs living OUTSIDE the staff: the score's time
    /// signature (by value) and its tuplet list (by array identity). A score-level change
    /// of either recomputes; a rebuilt-but-content-equal tuplet array merely misses.
    /// <para>
    /// ⚠️ A SINGLE-VOICE STAFF TAKES THE Score ENTRY'S UNFANNED PATH, which
    /// <see cref="StaffTupletBracketLayouts"/>' old per-voice fan spelt differently
    /// (voice-filtered tuplets + an always-supplied <c>forceStemUpAt</c>). Same function
    /// there, twice over: ⑴ both read sites take <c>forceStemUpAt?.Invoke(..) ?? …</c>
    /// (BeamDetector :335/:595) and <c>GetDefaultStemUpAt</c> of a one-voice staff is null
    /// at every measure (<c>IsPolyphonicAt</c> finds no second track), so the lambda IS the
    /// null; ⑵ a one-voice staff's tuplets all carry VoiceIndex 0 — a nonzero index is
    /// assigned only inside a <c>voice { }</c> block (<c>MeasureCollector._currentVoiceIndex</c>),
    /// which builds extra voice tracks, so the voice filter drops nothing. The multi-voice
    /// fan is the Score entry's own fan, line for line.
    /// </para>
    /// </remarks>
    internal ImmutableArray<BeamGroup> StaffBeamGroupsOf(
        MultiStaffScore score, Staff staff, int staffIndex)
    {
        if (_staffBeamGroups.TryGetValue(staff, out var hit)
            && hit.StaffIndex == staffIndex
            && hit.TimeSignature == score.TimeSignature
            && hit.ScoreTuplets == score.TupletBrackets)
            return hit.Groups;
        // Through the input-keyed memo, so the annotation quantity — the same three inputs
        // on a single-voice, single-staff score — shares this detection instead of running
        // its own (see BeamGroupsOf). This table stays because it answers WITHOUT building
        // the per-staff Score and its filtered tuplet list, which the input key needs.
        var groups = BeamGroupsOf(StaffBeamScoreOf(score, staff, staffIndex));
        _staffBeamGroups.AddOrUpdate(staff, new StaffBeamGroupsEntry(
            staffIndex, score.TimeSignature, score.TupletBrackets, groups));
        return groups;
    }

    /// <summary>Half-height (ss) of a bold sans chord symbol's cap above its
    /// baseline — the renderer draws chord names at font 2.6 (FontSize 4.0 × 0.65),
    /// cap height ≈ 0.72 × 2.6.</summary>
    private const double ChordSymbolCapHeight = 1.9;

    /// <summary>Chord-name baseline distance above the staff top line — mirrors
    /// <c>ChordNameEngraver.StaffPadding</c>.</summary>
    private const double ChordRowStaffPadding = 0.6;

    /// <summary>
    /// Extends a staff's UP skyline to cover its associated chord-name row, so the
    /// gap to the staff above reserves room for the symbols. The row's baseline sits
    /// <see cref="ChordRowStaffPadding"/> + note-protrusion above the top line (see
    /// <see cref="ChordNameEngraver"/>), and the symbol reaches ~cap-height higher;
    /// reserve a flat band to that top across the drawn width.
    /// </summary>
    private static void ReserveChordRowBand(
        VerticalSkyline up, ImmutableArray<MeasureLayout> measureLayouts, double halfStaff)
    {
        if (up.IsEmpty || measureLayouts.IsDefaultOrEmpty)
            return;
        double xLeft = double.PositiveInfinity, xRight = double.NegativeInfinity;
        foreach (var ml in measureLayouts)
        {
            xLeft = Math.Min(xLeft, ml.X);
            xRight = Math.Max(xRight, ml.X + ml.Width);
        }
        if (xRight <= xLeft)
            return;

        // Note ink already in the skyline sets where the shared-baseline row floats;
        // the symbol top clears the top line by padding + that protrusion + cap.
        // ⚠️ THE EXPRESSION IS FRAME-FREE AND THAT IS NOT LUCK: `protrusion` is read out of
        // the same skyline the band is merged back into, so both sides move together when the
        // frame does. Only the box's FLOOR names a place — the staff's top line, a half-staff
        // above the reference point this skyline is built about
        // (SkylineBuilder.BuildStaffSkylines). It never reaches the roof of an UP skyline and
        // so cannot move the answer; it is written correctly anyway, because a floor that says
        // "the top line" while meaning the middle one is how the next reader learns the wrong
        // frame.
        double protrusion = up.MaxProtrusionInRange(xLeft, xRight);
        double bandTop = ChordRowStaffPadding + protrusion + ChordSymbolCapHeight;
        up.Merge(VerticalSkyline.FromBox(xLeft, xRight, halfStaff, bandTop, VerticalDirection.Up));
    }

    /// <summary>
    /// Calculates the gap between two adjacent staves using skyline distances.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc:217-268 internal_get_minimum_translations()
    ///
    /// Formula: gap = max(skyline_distance + padding, minimum_distance, basic_distance) - refpoint_span
    ///
    /// The skyline distance gives the minimum center-to-center distance needed to avoid collisions.
    /// We then take the maximum of that (with padding), the minimum-distance, and basic-distance,
    /// then subtract the REFPOINT SPAN — the two staves' own half spans — to get the gap between
    /// bottom of upper and top of lower. Subtracting the upper staff's whole height instead is
    /// the same number only when both staves are the same height; see <see cref="StaffGap"/>.
    /// </remarks>
    private static double CalculateStaffGapWithSkylines(
        VerticalSpacingSpec spec, double refpointSpan,
        List<(VerticalSkyline Up, VerticalSkyline Down)> staffSkylines,
        int upperStaffIndex, int lowerStaffIndex,
        IReadOnlyList<PairLooseLine>? looseLines = null)
    {
        if (upperStaffIndex >= staffSkylines.Count || lowerStaffIndex >= staffSkylines.Count)
            return Math.Max(0, spec.BasicDistance - refpointSpan);

        var upperDown = staffSkylines[upperStaffIndex].Down;
        var lowerUp = staffSkylines[lowerStaffIndex].Up;

        if (upperDown.IsEmpty || lowerUp.IsEmpty)
            return Math.Max(0, spec.BasicDistance - refpointSpan);

        // The distance a staff is DRAWN at is the page spring at rest, and a spring at rest
        // is max(its floor, its ideal) — so basic-distance enters here as the IDEAL and the
        // alignment minimum is the floor. Same number as writing one max over all three,
        // which is how this read until 2026-07-26, and NOT the same model: fed to a spring
        // as its floor, a basic-distance folded into the minimum makes the spring
        // incompressible, and the page could not squeeze the staves the way LilyPond does
        // (ledger page.compressed.staff-staff-inside).
        // ⚠️ THE MAX IS LILY#'s MODEL, NOT LilyPond's ALIGNMENT, and it now reaches a pair
        // LilyPond would answer differently for. align-interface.cc:235-238 takes
        // basic-distance ONLY behind the pure branch, so a NON-SPACEABLE neighbour is placed
        // at the skyline distance plus its padding and nothing else; what puts an ideal under
        // it in LilyPond is the loose-line CHAIN at force 0 (page-layout-problem.cc:1035),
        // which is a different pass. The two agree wherever the ink clears the ideal — on
        // book LYRMC the chords row reads max(1.0, 3.576200) either way — and would part
        // company on a row whose symbols sit closer than its spec's 1.0. No point measures
        // that, so it is named rather than split.
        double centerToCenter = Math.Max(
            spec.BasicDistance,
            AlignmentMinimumWithSkylines(
                spec, staffSkylines, upperStaffIndex, lowerStaffIndex, looseLines));

        return Math.Max(0, centerToCenter - refpointSpan);
    }

    /// <summary>
    /// The refpoint-to-refpoint MINIMUM two staves may sit at — <c>Align_interface</c>'s
    /// minimum translation, which is what floors the page's spring for that pair.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc:228-238 internal_get_minimum_translations —
    /// <c>dy = down_skyline.distance(next) + padding</c>, raised to
    /// <c>minimum-distance</c>, and NOT to basic-distance.
    /// <para>
    /// ⚠️ basic-distance IS in that function, at :234-238, but behind
    /// <c>INT_MAX == end &amp;&amp; 0 == start</c> — the PURE estimate the page breaker uses
    /// for heights (<c>get_pure_minimum_translations</c>). The placement path,
    /// <c>get_minimum_translations</c>, calls with <c>start = end = 0</c> (:128-134), so the
    /// branch is dead there. It reads like a max over three numbers because at force 0 the
    /// spring returns max(floor, basic-distance) anyway — and that is exactly why folding it
    /// in was invisible until a page tried to COMPRESS.
    /// </para>
    /// </remarks>
    private static double AlignmentMinimumWithSkylines(
        VerticalSpacingSpec spec,
        List<(VerticalSkyline Up, VerticalSkyline Down)> staffSkylines,
        int upperStaffIndex, int lowerStaffIndex,
        IReadOnlyList<PairLooseLine>? looseLines = null)
    {
        if (upperStaffIndex >= staffSkylines.Count || lowerStaffIndex >= staffSkylines.Count)
            return spec.MinimumDistance;

        var upperDown = staffSkylines[upperStaffIndex].Down;
        var lowerUp = staffSkylines[lowerStaffIndex].Up;
        if (upperDown.IsEmpty || lowerUp.IsEmpty)
            return spec.MinimumDistance;

        // The alignment's walk from the upper staff down to the lower one. With nothing
        // between them that is ONE step and reads as it always did — Skyline::distance with
        // no horizon padding, plus the pair's own spec padding. With loose lines between,
        // the pair is not adjacent in the alignment and the minimum is the SUM of the walk's
        // steps, each measured against everything above it rather than against its
        // neighbour. One expression for both, because LilyPond has one loop for both.
        // LILYPOND-REF: lily/align-interface.cc:201-285, run over
        // upper staff, line 1 .. line n, lower staff.
        //
        // The spec each step takes is get_spacing_spec's per-LINE selection, asked of the
        // ported selector rather than re-enumerated here — the walk hard-coded the Lyrics
        // constants by step position (first = related, rest = nonstaff, closing =
        // unrelated) until 2026-08-26, which is the right answer exactly while every line
        // in the run is a Lyrics line and silently the wrong one for a ChordNames line
        // (measured: the chords-then-lyrics run's middle step read the Lyrics
        // minimum-distance 2.8 where LilyPond reads the ChordNames padding 0.5 over the
        // ink, 2.320115015 on book CHL1).
        // LILYPOND-REF: lily/page-layout-problem.cc:1266-1342 get_spacing_spec — see
        // StaffAffinity.GetSpacingSpec, the one home of the selection.
        bool hasLooseLines = looseLines is { Count: > 0 };
        var walk = new AlignmentWalk();
        walk.Seed(upperDown);
        int? prevAffinity = null;
        StaffSpacingParameters.NonStaffSpacing prevSpecs = default;
        for (int k = 0; k < (looseLines?.Count ?? 0); k++)
        {
            var line = looseLines![k];
            // ⚠️ THE MINIMUM-DISTANCE BELONGS IN THE WALK: align-interface.cc:231-233
            // raises dy by it BEFORE the raise and merge, so it changes the accumulation
            // every later line is measured against. The chain passes the same two
            // arguments (LyricEngraver.DistributeLooseLines).
            var stepSpec = StaffAffinity.GetSpacingSpec(
                prevAffinity, prevSpecs, line.Affinity, line.Specs, spec);
            walk.Advance(line.Up, line.Down, stepSpec.Padding, stepSpec.MinimumDistance);
            prevAffinity = line.Affinity;
            prevSpecs = line.Specs;
        }
        // ...and the last element to the staff below — the LINE's own spec again
        // (get_spacing_spec :1299-1312: an affinity-UP line closes on its
        // nonstaff-unrelatedstaff-spacing, any other on its nonstaff-relatedstaff-spacing).
        // Same closing step LyricEngraver's chain takes, so the block fits the room. With
        // no loose lines it is the staff pair's own spec.
        double closingPadding = hasLooseLines
            ? StaffAffinity.GetSpacingSpec(prevAffinity, prevSpecs, null, default, spec).Padding
            : spec.Padding;
        double total = walk.Where + walk.Distance(lowerUp, closingPadding);

        // The two STAVES' own minimum-distance applies across the whole span — over the loose
        // lines when there are any.
        // LILYPOND-REF: lily/align-interface.cc:257-263
        //   if (read_spacing_spec (spec, &spaceable_min_distance, "minimum-distance"))
        //     dy = std::max (dy, spaceable_min_distance
        //                          + stacking_dir * (last_spaceable_element_pos - where));
        // i.e. measured from the last SPACEABLE element, not from the neighbour. It sits
        // inside the `include_fixed_spacing` guard (:240), so it is in the RESERVATION
        // vector — which this function is — and not in the chain's.
        return Math.Max(total, spec.MinimumDistance);
    }

    /// <summary>
    /// One non-spaceable line of a pair's run, as the alignment walk needs it: its own
    /// up/down skylines about its reference point, and the two things
    /// <c>get_spacing_spec</c> reads off the grob — its <c>staff-affinity</c> and its own
    /// <c>nonstaff-*</c> spec set.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1266-1342 get_spacing_spec — the selection
    /// is per LINE, off the line's own properties, which is why the walk cannot carry one
    /// score-wide spec set: a run may hold a Lyrics line and a ChordNames line at once and
    /// the two declare different numbers under the same property names
    /// (<see cref="StaffSpacingParameters.Lyrics"/> /
    /// <see cref="StaffSpacingParameters.ChordNames"/>). The walk carried exactly that
    /// score-wide set — the Lyrics constants, hard-coded by step position — until
    /// 2026-08-26; the chain's springs stopped doing so the same day the run port landed
    /// (<c>LooseLineSpacer.RunLine</c>), and this is the walk catching up.
    /// </remarks>
    internal readonly record struct PairLooseLine(
        VerticalSkyline Up, VerticalSkyline Down,
        int? Affinity, StaffSpacingParameters.NonStaffSpacing Specs);

    /// <summary>
    /// The alignment's non-spaceable elements between two spaceable staves of one system —
    /// the <c>with lyrics</c> block, one self-relative skyline pair per verse, in order.
    /// Null or empty means the two staves are adjacent in the alignment.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:919-925 — the run of loose lines collected
    /// between two spaceable staves. Supplied by the caller because only it knows the
    /// system's measure range and can build the syllable geometry
    /// (<c>LyricEngraver.NoteBoundBlockSkylines</c>).
    /// </remarks>
    internal delegate IReadOnlyList<PairLooseLine>? LooseLinesBetween(
        int upperStaffIndex, int lowerStaffIndex);
}
