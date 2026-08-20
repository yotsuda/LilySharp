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
/// Layout information for a piano pedal bracket line.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: define-grobs.scm:2855-2873 PianoPedalBracket grob
/// </remarks>
public readonly record struct PedalBracketLayout(
    double StartX,           // Start X position (at "Ped." text)
    double EndX,             // End X position (at "*" release)
    double Y,                // Y position below staff (relative to system top)
    double EdgeHeight,       // Height of the end hook (vertical line at release)
    int StartMeasureIndex,   // For system Y lookup in renderer
    int SourcePosition,      // For click-to-source mapping
    // Mixed style ("Ped." text then a line): the LEFT hook is omitted and the
    // line starts after the text. LILYPOND-REF: piano-pedal-bracket.cc:80-88.
    bool IsMixed = false,
    // A pedal CHANGE (release + re-engage on the same note) abuts the previous /
    // next bracket. LilyPond draws the shared end not as a vertical hook but as a
    // flared edge; two abutting flares form the "/\" notch at the change, while
    // the outer ends stay vertical. LILYPOND-REF: scm/define-grobs.scm
    // PianoPedalBracket bracket-flare = (0.5 . 0.5).
    bool StartChange = false,
    bool EndChange = false,
    // F3/B: index into the bracket list DetectPedalBrackets rebuilds from the live score,
    // so a reused layout re-derives its data-pos instead of carrying a stale source offset.
    // The same shape MusicMarkLayout uses against BuildAllMarks: the list is not a score
    // side-table, it is reconstructed, and reconstructing it is deterministic.
    // ⚠️ THIS IS WHAT MADE PEDAL SCORES REUSE-ELIGIBLE. IncrementalCompiler.ReuseSafe used
    // to decline whole-layout reuse for any score carrying a pedal bracket, under a comment
    // asserting the array was "always empty today" — showcase/03-piano has had pedals all
    // along, and the benchmark that asserts reuse fires (IncrementalSessionBenchmark) had
    // been failing on exactly that.
    int SourceIndex = -1
);

/// <summary>
/// Detects and calculates piano pedal bracket positions.
/// </summary>
/// <remarks>
/// LILYPOND-REF: piano-pedal-engraver.cc:216-400 Pedal event processing
/// LILYPOND-REF: define-grobs.scm:2855-2873 PianoPedalBracket parameters
/// LILYPOND-REF: define-grobs.scm:3573-3619 SustainPedal/SustainPedalLineSpanner
///
/// Style selection is per-part (Staff.PedalStyle, from the `pedal` part
/// property). LayoutEngine runs this engraver for staves whose style is
/// Bracket (Lily# default) or Mixed and suppresses the corresponding "Ped." /
/// "*" text marks; the Text style keeps the marks and emits no bracket.
/// </remarks>
internal static class PedalEngraver
{
    // LILYPOND-REF: define-grobs.scm:2857 bound-padding = 1.0
    private const double BoundPadding = 1.0;

    // LILYPOND-REF: define-grobs.scm:2860 edge-height = (1.0 . 1.0)
    private const double EdgeHeight = 1.0;

    // LILYPOND-REF: define-grobs.scm:3593-3606 SustainPedalLineSpanner -- the axis group a
    // pedal bracket hangs in: direction DOWN, side-position y-aligned-side, padding 1.2
    // (staff-padding 1.2 is not spelled separately: the staff symbol is in the support
    // profile, so 2.05 + 1.2 emerges from the same walk -- measured indistinguishable on
    // 2.26.0, scratch probe PLF, 2026-08-20).
    private const double SpannerPadding = 1.2;

    // Half the bracket's line thickness (thickness 1.0 x the 0.1 staff line).
    private const double HalfThickness = 0.05;

    // The hook's own width in the bracket's up-profile -- the line thickness. Only its X
    // placement matters (a hook pushes the bracket by its full height exactly where the
    // support ink is under an END, not mid-span).
    private const double HookWidth = 0.1;

    /// <summary>One solved bracket line of one system: which bracket (by its start
    /// measure and pedal type) and where its LINE sits, Y-up about the staff's middle
    /// line. Solved once, at skyline-build time, and read by the draw -- one computation,
    /// two readers (HANDOFF 7.7).</summary>
    internal readonly record struct SolvedPedalLine(
        PedalType Type, int StartMeasureIndex, double LineYUp);

    /// <summary>One solved TEXT-style pedal WORD of one system: which mark (by its
    /// source position) and where its BASELINE sits, Y-up about the staff's middle
    /// line. Solved at skyline-build time, read by the mark draw. Per WORD, not per
    /// family: LilyPond side-positions each pedal item independently — measured on
    /// 2.26.0 (PLT), the release star sits at 4.806 (staff + padding 1.2 + its own ink)
    /// while the engage word is pushed to 5.997 by the note under it.</summary>
    internal readonly record struct SolvedPedalRow(int SourcePosition, double BaselineYUp);

    /// <summary>
    /// A pedal word's own (Up, Down) outline profiles about its baseline, centred on
    /// <paramref name="xCentre"/> — the element stencil LilyPond's spanner measures with.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:3580 SustainPedal always-vertical-skylines-from-stencil / :3204 SostenutoPedal /
    /// :4162 UnaCordaPedal — vertical-skylines from the stencil; the sustain word is
    /// Emmentaler glyphs pasted extent-to-extent (lily/sustain-pedal.cc:47-76 Sustain_pedal::print), the other
    /// two are italic text. Outlines, not boxes, because the pedal.Ped ligature's ink is
    /// a staff space lower over its middle than at the P and LilyPond's y-aligned-side
    /// measures pointwise — with boxes the Ped. row lands 0.248 too low on the PLT book.
    /// </remarks>
    internal static (VerticalSkyline Up, VerticalSkyline Down) WordProfiles(
        Rendering.ScoreTextMetrics fonts, MusicMarkType type, string text, double xCentre)
    {
        if (MusicMarkEngraver.IsGlyphPedal(type))
        {
            var (glyphs, width, _) = MusicMarkEngraver.SustainPedalStencil(text);
            double x0 = xCentre - width / 2;
            var up = new VerticalSkyline(VerticalDirection.Up);
            var down = new VerticalSkyline(VerticalDirection.Down);
            foreach (var g in glyphs)
            {
                var (dQ, uQ) = GlyphMetrics.PedalGlyphVerticalSkylineQuads(g.Glyph);
                up.Merge(VerticalSkyline.FromGlyphOutline(
                    VerticalDirection.Up, uQ, StaffSize.FullSize, x0 + g.X, 0));
                down.Merge(VerticalSkyline.FromGlyphOutline(
                    VerticalDirection.Down, dQ, StaffSize.FullSize, x0 + g.X, 0));
            }
            return (up, down);
        }
        double w = fonts.Advance(text, MusicMarkEngraver.PlainTextFontSize,
            MusicMarkEngraver.TextRoleOf(type), MusicMarkEngraver.TextStyleOf(type));
        return TextOutlineSkylines.Place(
            text, MusicMarkEngraver.PlainTextFontSize,
            fonts.Face(MusicMarkEngraver.TextRoleOf(type), MusicMarkEngraver.TextStyleOf(type)),
            xCentre - w / 2, 0);
    }

    /// <summary>
    /// Solves a TEXT-style staff's pedal rows for ONE system and merges their ink into
    /// the staff's down profile — the same one-computation-two-readers shape the bracket
    /// takes, with the words as the spanner's elements. One row per FAMILY per system,
    /// nearest-first in the order pedal-three.ly measured (una corda, sostenuto,
    /// sustain), each clearing the family before it at outside-staff padding.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: the two-step every below-staff outside grob runs — the spanner's
    /// own y-aligned-side against its support at padding 1.2
    /// (SustainPedalLineSpanner, define-grobs.scm:3599), then
    /// avoid_outside_staff_collisions against everything already placed at 0.46
    /// (axis-group-interface.cc:648-676). MEASURED on 2.26.0 (probes/pedal-three.ly):
    /// the three-family steps 1.961 / 2.443 are exactly each row's own ink + 0.46, so
    /// the steps fall out of the profiles and no step constant exists here.
    /// ⚠️ The words' X is the pedal arm of MusicMarkEngraver.CalculateXPosition spelled
    /// through AnchorX — the same timing-column read, asserted by the ledger points
    /// rather than by a shared function (the draw's arm needs `systems`, which do not
    /// exist yet at skyline-build time).
    /// </remarks>
    internal static ImmutableArray<SolvedPedalRow> SolveAndSeedText(
        MultiStaffScore score, Staff staff, int staffIndex,
        ImmutableArray<MeasureLayout> measureLayouts,
        VerticalSkyline insideDown, VerticalSkyline downProfile)
    {
        if (staff.PedalStyle != PedalStyle.Text || score.MusicMarks.IsDefaultOrEmpty
            || measureLayouts.IsDefaultOrEmpty
            || StaffSize.Of(staff).Span(1.0) != 1.0)
            return ImmutableArray<SolvedPedalRow>.Empty;

        var fonts = score.TextMetrics;
        // Every pedal word on this system, in the order the pass places equal-priority
        // grobs: family rank first (the measured nearest-first order — una corda,
        // sostenuto, sustain — which is what stacks same-X families the way
        // pedal-three.ly reads), then document order within a family.
        var words = new List<(int Rank, int Order, MusicMarkItem Mark, double X)>();
        int order = 0;
        foreach (var mark in score.MusicMarks)
        {
            order++;
            if (mark.StaffIndex != staffIndex || !IsPedalMarkType(mark.Type))
                continue;
            MeasureLayout? ml = null;
            foreach (var m in measureLayouts)
                if (m.MeasureIndex == mark.MeasureIndex) { ml = m; break; }
            if (ml == null)
                continue; // another system's word
            double x = AnchorX(ml, mark.AnchorItemIndex, mark.AnchorTiming);
            words.Add((MusicMarkEngraver.PedalFamilyRank(mark.Type), order, mark, x));
        }
        if (words.Count == 0)
            return ImmutableArray<SolvedPedalRow>.Empty;

        var solved = ImmutableArray.CreateBuilder<SolvedPedalRow>(words.Count);
        foreach (var (rank, _, mark, x) in words
                     .OrderBy(w => w.Rank).ThenBy(w => w.Order))
        {
            var word = WordProfiles(fonts, mark.Type, mark.Text, x);
            // Quiet: the spanner's own side-position against the staff's INSIDE profile
            // (staff, notes, scripts — its support) at padding 1.2...
            double quiet = insideDown.IsEmpty
                ? -(2.05 + SpannerPadding)
                : DynamicEngraver.BelowCollisionMove(insideDown, word.Up, SpannerPadding);
            word.Up.Raise(quiet);
            word.Down.Raise(quiet);
            // ...then the collision pass against everything already placed below —
            // the dynamics, the figures, the words solved before this one. An
            // X-disjoint word keeps its own quiet row (the PLT star), an overlapping
            // one stacks (the pedal-three families).
            double move = DynamicEngraver.BelowCollisionMove(
                downProfile, word.Up, SkylineBuilder.OutsideStaffPaddingValue);
            if (move != 0)
            {
                word.Up.Raise(move);
                word.Down.Raise(move);
            }
            downProfile.Merge(word.Down);
            solved.Add(new SolvedPedalRow(mark.SourcePosition, quiet + move));
        }
        return solved.ToImmutable();
    }

    /// <summary>Every pedal mark type, engage and release, all three families.</summary>
    internal static bool IsPedalMarkType(MusicMarkType t) =>
        t is MusicMarkType.SustainOn or MusicMarkType.SustainOff
          or MusicMarkType.SostenutoOn or MusicMarkType.SostenutoOff
          or MusicMarkType.UnaCordaOn or MusicMarkType.UnaCordaOff;

    /// <summary>
    /// A bracket portion's own UP profile about a trial line at the middle line --
    /// LilyPond's spanner element stencil, pointwise: support ink under a HOOK pushes the
    /// line by the hook's full height, mid-span ink only by the line's half thickness.
    /// </summary>
    /// <remarks>
    /// MEASURED (audit/lp-geometry probes/pedal-lyric-stack.ly PLB, 2.26.0): the bracket
    /// refpoint sits 5.295000 below the staff refpoint = the engaging note's ink bottom
    /// 3.045 + padding 1.2 + hook 1.05, to the digit.
    /// </remarks>
    internal static VerticalSkyline BracketUpProfile(
        double startX, double endX, bool leftHook, bool rightHook)
    {
        // The bracket's own UP profile with its line at the middle line (0), to be pushed
        // down by the same collision move the dynamics run (one spelling, DynamicEngraver).
        var up = VerticalSkyline.FromBox(
            startX, endX, -HalfThickness, HalfThickness, VerticalDirection.Up);
        if (leftHook)
            up.Merge(VerticalSkyline.FromBox(
                startX, startX + HookWidth,
                -HalfThickness, EdgeHeight + HalfThickness, VerticalDirection.Up));
        if (rightHook)
            up.Merge(VerticalSkyline.FromBox(
                endX - HookWidth, endX,
                -HalfThickness, EdgeHeight + HalfThickness, VerticalDirection.Up));
        return up;
    }

    /// <summary>
    /// Solves this staff's bracket lines for ONE system and merges their ink into the
    /// staff's down profile -- the seed the lyric floor and the staff-to-staff springs
    /// read. Bracket style only: mixed keeps its leading "Ped." text on the below-mark
    /// baseline, so moving its line alone would tear the two apart (the text-row port is
    /// the PLT half of the same ledger family and goes with MusicMarkEngraver).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:914-950 skyline_spacing -- the inside
    /// profile first, then each priority in ascending order; SustainPedalLineSpanner is
    /// priority 1000, so it clears the dynamics (250) and the figured bass (25) that were
    /// merged before this call.
    /// An ossia's mixed scale has no measured pedal regime -- same guard as
    /// <c>SkylineBuilder.AddDynamicsToSkyline</c>'s pointwise pass: full size only.
    /// </remarks>
    internal static (ImmutableArray<SolvedPedalLine> Lines, ImmutableArray<SolvedPedalRow> Rows)
        SolveAndSeed(
        MultiStaffScore score, Staff staff, int staffIndex,
        ImmutableArray<MeasureLayout> measureLayouts,
        VerticalSkyline insideDown, VerticalSkyline downProfile)
    {
        bool mixed = staff.PedalStyle == PedalStyle.Mixed;
        if ((staff.PedalStyle != PedalStyle.Bracket && !mixed)
            || score.MusicMarks.IsDefaultOrEmpty
            || measureLayouts.IsDefaultOrEmpty
            || StaffSize.Of(staff).Span(1.0) != 1.0)
            return (ImmutableArray<SolvedPedalLine>.Empty, ImmutableArray<SolvedPedalRow>.Empty);

        var staffMarks = score.MusicMarks
            .Where(m => m.StaffIndex == staffIndex).ToImmutableArray();
        var brackets = DetectPedalBrackets(staffMarks);
        if (brackets.IsDefaultOrEmpty)
            return (ImmutableArray<SolvedPedalLine>.Empty, ImmutableArray<SolvedPedalRow>.Empty);

        int loMeasure = measureLayouts[0].MeasureIndex;
        int hiMeasure = measureLayouts[^1].MeasureIndex;
        MeasureLayout? LayoutOf(int measureIndex)
        {
            foreach (var m in measureLayouts)
                if (m.MeasureIndex == measureIndex) return m;
            return null;
        }

        // ONE LINE PER FAMILY, not per bracket: LilyPond hangs every bracket of a pedal
        // family in ONE SustainPedalLineSpanner, so a system's sustain brackets share one
        // Y -- a pedal CHANGE's abutting pair must, or the "/\" notch tears (measured:
        // per-bracket solving dropped the second bracket below the first's own seeded
        // ink). Families solve in the nearest-first order pedal-three.ly measured
        // (una corda, sostenuto, sustain -- MusicMarkEngraver.PedalFamilyRank), each
        // clearing the ink of the family before it.
        var solved = ImmutableArray.CreateBuilder<SolvedPedalLine>();
        var solvedRows = ImmutableArray.CreateBuilder<SolvedPedalRow>();
        var portions = new List<(PedalType Type, int StartMeasureIndex,
            double StartX, double EndX, bool LeftHook, bool RightHook, int SourcePosition)>();
        foreach (var bracket in brackets)
        {
            if (bracket.EndMeasureIndex < loMeasure || bracket.StartMeasureIndex > hiMeasure)
                continue; // not on this system
            var startLayout = LayoutOf(bracket.StartMeasureIndex);
            var endLayout = LayoutOf(bracket.EndMeasureIndex);
            // The portion on THIS system: a broken end runs to the system edge, hook-less,
            // exactly as the drawn spanner breaks.
            double startX = startLayout != null
                ? AnchorX(startLayout, bracket.StartItemIndex, bracket.StartTiming)
                : measureLayouts[0].X;
            double endX = endLayout != null
                ? AnchorX(endLayout, bracket.EndItemIndex, bracket.EndTiming)
                : measureLayouts[^1].X + measureLayouts[^1].Width;
            if (endX - startX < 2.0)
                endX = startX + 2.0; // the same minimum Calculate applies
            portions.Add((bracket.Type, bracket.StartMeasureIndex, startX, endX,
                // MIXED draws the leading word where a bracket-style LEFT hook would be.
                startLayout != null && !mixed, endLayout != null,
                bracket.SourcePosition));
        }
        static int FamilyRank(PedalType t) => t switch
        {
            PedalType.UnaCorda => 0,
            PedalType.Sostenuto => 1,
            _ => 2,
        };
        foreach (var family in portions.GroupBy(p => p.Type)
                     .OrderBy(g => FamilyRank(g.Key)))
        {
            // The family's whole up-profile about a trial line at the middle line, then
            // the two-step every below-staff outside grob runs: the spanner's own
            // side-position against its SUPPORT (the inside profile: staff, notes,
            // scripts) at padding 1.2, then the collision pass against everything
            // already placed below (dynamics, figures, earlier families) at 0.46.
            // One step until 2026-08-20 — indistinguishable while nothing outside sat
            // under the span (the PLB ledger point covers that shape either way).
            VerticalSkyline? up = null;
            // MIXED: the leading words are elements of the same group as the line — the
            // word's BASELINE is the line's Y (measured: SustainPedal, PianoPedalBracket
            // and their LineSpanner all dump one relY, pedal-mixed.ly) — so their
            // outlines join the group's profile and the group solves ONCE.
            var mixedWords = new List<(MusicMarkItem Mark, double X)>();
            foreach (var p in family)
            {
                var one = BracketUpProfile(p.StartX, p.EndX, p.LeftHook, p.RightHook);
                if (up == null) up = one; else up.Merge(one);
                if (mixed && p.StartMeasureIndex >= loMeasure
                    && p.StartMeasureIndex <= hiMeasure)
                {
                    foreach (var mark in staffMarks)
                        if (mark.SourcePosition == p.SourcePosition)
                        {
                            mixedWords.Add((mark, p.StartX));
                            var w = WordProfiles(score.TextMetrics, mark.Type, mark.Text, p.StartX);
                            up.Merge(w.Up);
                            break;
                        }
                }
            }
            double lineYUp = insideDown.IsEmpty
                ? -(2.05 + SpannerPadding + EdgeHeight + HalfThickness)
                : DynamicEngraver.BelowCollisionMove(insideDown, up!, SpannerPadding);
            up!.Raise(lineYUp);
            double move = DynamicEngraver.BelowCollisionMove(
                downProfile, up, SkylineBuilder.OutsideStaffPaddingValue);
            lineYUp += move;
            foreach (var p in family)
            {
                downProfile.Merge(VerticalSkyline.FromBox(
                    p.StartX, p.EndX, lineYUp - HalfThickness,
                    lineYUp + EdgeHeight + HalfThickness, VerticalDirection.Down));
                solved.Add(new SolvedPedalLine(p.Type, p.StartMeasureIndex, lineYUp));
            }
            foreach (var (mark, x) in mixedWords)
            {
                var w = WordProfiles(score.TextMetrics, mark.Type, mark.Text, x);
                w.Down.Raise(lineYUp);
                downProfile.Merge(w.Down);
                solvedRows.Add(new SolvedPedalRow(mark.SourcePosition, lineYUp));
            }
        }
        return (solved.ToImmutable(), solvedRows.ToImmutable());
    }

    /// <summary>
    /// Detects pedal bracket spans from music marks.
    /// Pairs pedal-on marks with their corresponding pedal-off marks.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: piano-pedal-engraver.cc:293-339 Event pairing logic
    /// </remarks>
    public static ImmutableArray<PedalBracketItem> DetectPedalBrackets(
        ImmutableArray<MusicMarkItem> musicMarks)
    {
        if (musicMarks.IsDefaultOrEmpty)
            return ImmutableArray<PedalBracketItem>.Empty;

        var brackets = ImmutableArray.CreateBuilder<PedalBracketItem>();

        // Process each pedal type independently
        DetectBracketsForType(musicMarks, MusicMarkType.SustainOn, MusicMarkType.SustainOff,
            PedalType.Sustain, brackets);
        DetectBracketsForType(musicMarks, MusicMarkType.SostenutoOn, MusicMarkType.SostenutoOff,
            PedalType.Sostenuto, brackets);
        DetectBracketsForType(musicMarks, MusicMarkType.UnaCordaOn, MusicMarkType.UnaCordaOff,
            PedalType.UnaCorda, brackets);

        return brackets.ToImmutable();
    }

    private static void DetectBracketsForType(
        ImmutableArray<MusicMarkItem> musicMarks,
        MusicMarkType onType, MusicMarkType offType,
        PedalType pedalType,
        ImmutableArray<PedalBracketItem>.Builder brackets)
    {
        // Collect all on/off marks for this pedal type, ordered by position
        var marks = musicMarks
            .Where(m => m.Type == onType || m.Type == offType)
            .OrderBy(m => m.MeasureIndex)
            .ToList();

        MusicMarkItem? activeOn = null;

        foreach (var mark in marks)
        {
            if (mark.Type == onType)
            {
                // If there's already an active pedal and we get another ON,
                // end the current bracket at this measure
                if (activeOn != null)
                {
                    brackets.Add(new PedalBracketItem(
                        pedalType,
                        activeOn.MeasureIndex,
                        mark.MeasureIndex,
                        activeOn.SourcePosition,
                        activeOn.AnchorItemIndex, mark.AnchorItemIndex,
                        activeOn.AnchorTiming, mark.AnchorTiming));
                }
                activeOn = mark;
            }
            else if (mark.Type == offType && activeOn != null)
            {
                brackets.Add(new PedalBracketItem(
                    pedalType,
                    activeOn.MeasureIndex,
                    mark.MeasureIndex,
                    activeOn.SourcePosition,
                    activeOn.AnchorItemIndex, mark.AnchorItemIndex,
                    activeOn.AnchorTiming, mark.AnchorTiming));
                activeOn = null;
            }
        }
    }

    /// <summary>
    /// Calculates layout positions for pedal brackets.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: piano-pedal-bracket.cc — bracket Y is below the lowest staff
    /// In grand staff context, the pedal bracket is placed below the bass (lower) staff,
    /// not below the treble (upper) staff.
    /// </remarks>
    public static ImmutableArray<PedalBracketLayout> Calculate(
        ImmutableArray<PedalBracketItem> brackets,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        bool isMixed = false,
        Func<int, PedalType, double?>? solvedLineUpOf = null,
        double? staffTopDown = null)
    {
        if (brackets.IsDefaultOrEmpty)
            return ImmutableArray<PedalBracketLayout>.Empty;

        var layouts = ImmutableArray.CreateBuilder<PedalBracketLayout>(brackets.Length);

        // Build measure-to-system mapping
        var measureToSystem = new Dictionary<int, SystemLayout>();
        foreach (var system in systems)
        {
            foreach (var measure in system.Measures)
            {
                measureToSystem[measure.MeasureIndex] = system;
            }
        }

        // The bracket line runs on the SAME baseline as the "Ped." text and
        // the release "*" (classic Ped.____* notation): the below-mark
        // baseline under the system's LAST visible staff.
        double systemBottom = 4.0;
        if (systems.Length > 0 && !systems[0].StaffGroups.IsDefaultOrEmpty)
        {
            foreach (var group in systems[0].StaffGroups)
                foreach (var st in group.Staves)
                    if (!st.IsHidden)
                        systemBottom = Math.Max(systemBottom, st.Height - st.Y);
        }
        double bracketY = MusicMarkEngraver.BelowMarkBaseline(systemBottom);

        for (int bi = 0; bi < brackets.Length; bi++)
        {
            var bracket = brackets[bi];
            if (bracket.StartMeasureIndex >= measureLayouts.Length ||
                bracket.EndMeasureIndex >= measureLayouts.Length)
                continue;

            // The SOLVED line, when the room solved one: the same Y the staff's down
            // profile reserved at skyline-build time (SolveAndSeed), converted from
            // Y-up-about-the-middle-line to device-down from the system top. The
            // below-the-whole-system baseline above stays as the fallback -- a staff the
            // seed declined (ossia scale, text/mixed style) keeps the legacy row.
            double y = bracketY;
            if (solvedLineUpOf?.Invoke(bracket.StartMeasureIndex, bracket.Type) is { } lineYUp
                && staffTopDown is { } topDown)
                y = topDown + 2.0 - lineYUp;

            var startMeasure = measureLayouts[bracket.StartMeasureIndex];
            var endMeasure = measureLayouts[bracket.EndMeasureIndex];

            // X anchors at the engaging / releasing note's column (LP places
            // "Ped." and "*" at the note, not the measure start).
            double startX = AnchorX(startMeasure, bracket.StartItemIndex, bracket.StartTiming);
            double endX = AnchorX(endMeasure, bracket.EndItemIndex, bracket.EndTiming);

            // Ensure minimum length
            if (endX - startX < 2.0)
                endX = startX + 2.0;

            layouts.Add(new PedalBracketLayout(
                startX,
                endX,
                y,
                EdgeHeight,
                bracket.StartMeasureIndex,
                bracket.SourcePosition,
                isMixed,
                SourceIndex: bi));
        }

        // Mark abutting ends as pedal CHANGES: where one bracket ends exactly where
        // the next begins (a release + re-engage on the same note), both shared ends
        // render as flared edges (the "/\" notch) instead of vertical hooks.
        for (int a = 0; a < layouts.Count; a++)
            for (int b = 0; b < layouts.Count; b++)
            {
                if (a == b) continue;
                if (Math.Abs(layouts[a].EndX - layouts[b].StartX) < 0.01)
                {
                    layouts[a] = layouts[a] with { EndChange = true };
                    layouts[b] = layouts[b] with { StartChange = true };
                }
            }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// X of the note column a pedal mark attaches to. Multi-staff layouts use the
    /// shared, voice-independent timing columns (like MetronomeMark); single-staff
    /// uses the item slot; falls back to a small inset at the measure start.
    /// </summary>
    private static double AnchorX(MeasureLayout ml, int itemIndex, Fraction timing)
    {
        if (!ml.Columns.IsDefaultOrEmpty)
            return ml.X + ml.GetXForTiming(timing);
        if (itemIndex >= 0 && itemIndex < ml.Items.Length)
            return ml.X + ml.Items[itemIndex].X;
        return ml.X + BoundPadding;
    }
}
