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

using System;
using System.Collections.Generic;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Shared "lower a below-staff line so its text clears the notes" spacing. ⚠️ <see
/// cref="Compute"/> itself now has ONE caller, the figured-bass engraver: the lyric line went
/// onto <c>LooseLineSpacer</c>'s chain and reads only the constants below. Mirrors
/// LilyPond's Align_interface per-pair spacing: each line is placed at
///   realized = max(basic-distance, staffDownSkyline.distance(lineUpSkyline) + padding)
/// so the fixed basic-distance wins for ordinary music and only notes poking far
/// below push the line down, keeping the LilyPond padding gap.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/align-interface.cc:222-275 — Align_interface minimum translations.
/// LILYPOND-REF: lily/page-layout-problem.cc:625-629 — read-spacing-spec padding.
/// </remarks>
internal static class SkylineDrop
{
    /// <summary>relatedstaff-spacing padding (ly/engraver-init.ly:651): the gap left
    /// between the line's up-skyline and the staff down-skyline when the skyline
    /// distance beats the basic-distance.</summary>
    public const double RelatedStaffPadding = 0.5;

    /// <summary>
    /// The horizontal padding the line/staff skyline distance is taken with.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:4243 skyline-horizontal-padding 0.1 of
    /// VerticalAxisGroup — the LOOSE-LINE device's number, read where a line's axis group is
    /// spaced against a staff's.
    /// ⚠️ IT IS NOT THE SIDE-POSITION DEVICE'S NUMBER, and the figure row is side-positioned:
    /// lily/side-position-interface.cc:357 aligned_side reads <c>horizon-padding</c> with a
    /// default of <b>0.0</b> and <c>BassFigureAlignmentPositioning</c> declares none, so
    /// LilyPond takes that distance with NO horizontal padding at all.
    /// ⚠️ MEASURED (2026-07-30), which is why this is still 0.1 for both callers: switching the
    /// figure row to 0.0 MOVES <c>test/figbass-below-script</c> — the row there clears a
    /// staccatissimo DAGGER, an obstacle narrow enough that 0.1 either side changes which x
    /// the distance is taken at — and NO ledger point moves with it. An output change no point
    /// observes is not made (HANDOFF §5.0; the clef flat-box deletion was held back for the
    /// same reason). ⇒ THE POINT TO OPEN FIRST is a figure row under a NARROW obstacle, which
    /// is what that fixture is; the port is then one argument.
    /// </remarks>
    public const double HorizonPadding = 0.1;

    /// <summary>
    /// <c>nonstaff-nonstaff-spacing</c>'s minimum-distance — the floor under the step from
    /// one lyric line to the next.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly:653-656 — the Lyrics context sets
    /// <c>nonstaff-nonstaff-spacing = ((basic-distance . 0) (minimum-distance . 2.8)
    /// (padding . 0.2) (stretchability . 0))</c>, and page-layout-problem.cc:1315-1332 is
    /// the branch of <c>get_spacing_spec</c> that hands it to a spring between two loose
    /// lines.
    /// ⚠️ A ZERO basic-distance with a minimum is the opposite shape from
    /// <see cref="RelatedStaffPadding"/>'s spec: there is no ideal to fall back on, so the
    /// realized step IS <c>max(minimum, ink + padding)</c> and nothing else. The spring is
    /// rigid in stretch because the spec DECLARES <c>(stretchability . 0)</c> at :657.
    /// ⚠️ CORRECTED 2026-07-26: this said <c>set_default_strength</c> derived the strength
    /// from the ideal. It does run (page-layout-problem.cc:1354, unconditionally), but a
    /// declared stretchability overrides it immediately after (:1356-1357), so the 0 here is
    /// LilyPond's own number and not a derived one. The two agree only because the ideal is
    /// also 0 — the same coincidence that hid a literal in <c>MarkupMarkup</c>. MEASURED (audit/lp-geometry, <c>lyrics.verse-step</c>): 2.800000 on a
    /// page whose loose chain is compressed hard enough to pull the first line off its own
    /// basic-distance.
    /// </remarks>
    public const double NonStaffNonStaffMinimum = 2.8;

    /// <summary>
    /// <c>nonstaff-nonstaff-spacing</c>'s padding — the gap left between one lyric line's
    /// descenders and the next line's ascenders when the ink beats the minimum.
    /// </summary>
    /// <remarks>LILYPOND-REF: ly/engraver-init.ly:656.</remarks>
    public const double NonStaffNonStaffPadding = 0.2;

    /// <summary>
    /// <c>nonstaff-unrelatedstaff-spacing</c>'s padding — the gap left between the LAST
    /// line of a lyric block and the up-skyline of the staff on the far side of it, the
    /// side its <c>staff-affinity</c> does NOT point at.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly:658 — the Lyrics context overrides exactly this
    /// one member, <c>nonstaff-unrelatedstaff-spacing.padding = #1.5</c>, over the
    /// VerticalAxisGroup default's 0.5 (scm/define-grobs.scm:4240).
    /// LILYPOND-REF: lily/page-layout-problem.cc:1299-1312 — the branch of
    /// <c>get_spacing_spec</c> that reaches it: <c>before</c> is the loose line, its
    /// affinity is UP, and <c>after</c> is spaceable.
    /// <para>
    /// ⚠️ THE PADDING IS ALL THE SPEC DECLARES. No basic-distance, no minimum-distance and
    /// no stretchability, so every other member of the resulting spring comes from the
    /// caller's own <c>Spring spring (1.0, 0.0)</c> (:1035) and from the LARGE_STRETCH the
    /// branch adds — see <see cref="LooseLineSpacer.NonStaffUnrelatedStaff"/>, which is
    /// where that spring is spelled.
    /// </para>
    /// </remarks>
    public const double UnrelatedStaffPadding = 1.5;

    /// <summary>
    /// The downward Y-shift that makes each line's UP-skyline clear the DOWN-skyline it
    /// hangs from: <c>drop = max(0, max(basicY, distance + RelatedStaffPadding) - basicY)</c>.
    /// Only lines that need a shift appear in the result. <paramref name="basicY"/> gives the
    /// line's basic-distance floor and <paramref name="downSkyline"/> the profile it has to
    /// clear; both are asked per KEY, which is whatever identifies one line — a system for
    /// lyrics, a (system, staff) pair for figured bass.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE KEY IS A PARAMETER BECAUSE IT WAS THE DEFECT. This took a system index and read
    /// <c>systemSkylines[s].down</c> — the whole system's silhouette — so a figure row on a
    /// non-bottom staff was measured against the LOWEST ink in the system and thrown below all
    /// of it. LilyPond's is per-line by construction, from either end: the loose-line spacer
    /// hangs a FiguredBass context off the staff <c>page-layout-problem.cc</c> records as
    /// <c>last_spaceable_line</c>, and <c>BassFigureAlignmentPositioning</c> side-positions
    /// against the note columns its own engraver acknowledged
    /// (lily/figured-bass-position-engraver.cc:90-100 <c>acknowledge_note_column</c>,
    /// <c>acknowledge_stem</c>). MEASURED, three arrangements of one score: LilyPond reads
    /// 8.124795235605315 whichever staff the figures belong to (ledger <c>figbass.*</c>).
    /// <para>
    /// Every argument is in ONE frame — Y-up about the system's origin — and it is the caller
    /// that puts them there, so this has no staff geometry in it at all.
    /// </para>
    /// </remarks>
    /// <param name="padding">The gap the CALLER's device declares between the line's
    /// up-skyline and the profile it hangs from. It is a parameter because the two callers
    /// read two different LilyPond declarations that happen to hold the same number: a lyric
    /// line pays <see cref="RelatedStaffPadding"/> (ly/engraver-init.ly:651, the Lyrics
    /// context's nonstaff-relatedstaff-spacing) and a figure row pays
    /// <c>EngravingDefaults.BassFigurePadding</c> (scm/define-grobs.scm:393,
    /// BassFigureAlignmentPositioning's side-position padding, spent at aligned_side :370).
    /// Passing one home's constant for the other device's spacing is the shape §5.2.1② warns
    /// about — it stays right only for as long as the two numbers agree.</param>
    /// <param name="horizonPadding">The horizontal padding the distance is taken with. A
    /// parameter so the caller has to name its device — see <see cref="HorizonPadding"/>,
    /// whose remark carries the one open case: a side-positioned grob's own default is 0.0
    /// and the figure row passes 0.1 anyway, held there until a point observes the
    /// difference.</param>
    public static Dictionary<TKey, double> Compute<TKey>(
        IReadOnlyDictionary<TKey, VerticalSkyline> upByLine,
        Func<TKey, double> basicY,
        Func<TKey, VerticalSkyline?> downSkyline,
        double padding,
        double horizonPadding)
        where TKey : notnull
    {
        var drops = new Dictionary<TKey, double>();
        foreach (var (key, up) in upByLine)
        {
            var down = downSkyline(key);
            if (down is null || down.IsEmpty || up.IsEmpty) continue;
            double dist = down.Distance(up, horizonPadding);
            if (double.IsInfinity(dist) || double.IsNaN(dist)) continue;
            double basic = basicY(key);
            double drop = Math.Max(basic, dist + padding) - basic;
            if (drop > 1e-6) drops[key] = drop;
        }
        return drops;
    }
}
