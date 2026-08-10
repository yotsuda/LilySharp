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
/// Layout information for a tuplet bracket together with its number.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/tuplet-bracket.cc:288-443 print method
/// LILYPOND-REF: lily/tuplet-number.cc — TupletNumber grob (LP keeps it as its
/// own grob, but the rendered stencil is always centered on the bracket).
/// LilySharp combines bracket + number into one Layout record; the number's
/// position derives from the bracket midpoint and ShowBracket=false suppresses
/// the bracket lines (number-only display, matching LP's standard appearance
/// for fully beamed tuplets).
/// </remarks>
public readonly record struct TupletBracketLayout(
    int MeasureIndex,           // Measure containing this tuplet
    double StartX,              // X position of bracket start
    double EndX,                // X position of bracket end
    double StartYUp,            // Y-up (frame B): staff-spaces ABOVE the system top at
                                // the bracket start (supports slope). Reflected to device
                                // against the system top (sy + old-Y == sy − YUp).
    double EndYUp,              // Y-up at the bracket end (supports slope).
    string NumberText,          // Text to display (e.g., "3")
    bool IsStemUp,              // Whether bracket goes above (true) or below (false)
    bool ShowBracket,           // False = all notes beamed, show number only
    int SourcePosition,         // For click-to-source mapping
    int SourceIndex = -1,       // F3/B: index into score.TupletBrackets (data-pos resolved at render)
    int StaffIndex = -1         // owning staff (ossia shrink); -1 = unknown/test construction
)
{
    /// <summary>
    /// X coordinate of the tuplet number's visual center (LP TupletNumber X).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tuplet-number.cc — TupletNumber sits at bracket midpoint.
    /// ⚠️ Off the LOGICAL bounds, not the drawn ones: <c>shorten-pair</c> moves the two
    /// ends by the same amount in opposite directions, so the midpoint is the same either
    /// way — but LilyPond's own number reads X-positions (:294-299 calc_x_offset), which
    /// the shorten never touches. Keep it that way.
    /// </remarks>
    public double NumberX => (StartX + EndX) / 2.0;

    /// <summary>
    /// The bracket's DRAWN left / right end — the logical bound moved out by
    /// <c>shorten-pair</c>. One house for the two readers (renderer and skyline).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm TupletBracket <c>(shorten-pair . (-0.2 . -0.2))</c>,
    ///   spent in lily/bracket.cc:54-55 make_bracket:
    ///   <c>straight_corners[d] += -d * shorten[d] / length * dz</c>. A NEGATIVE shorten
    ///   therefore LENGTHENS: the horizontal run and the edge hook at each end both move
    ///   outward along the bracket by 0.2 staff spaces.
    /// <para>
    /// ⚠️ The stencil is what a TupletBracket's skyline is built from
    /// (scm/define-grobs.scm: <c>grob::unpure-vertical-skylines-from-stencil</c>), so the
    /// skyline reads THESE, not the logical bounds.
    /// </para>
    /// <para>
    /// MEASURED on audit/lp-regression's autobeam-tuplet-recheck: LilyPond draws the two
    /// brackets over 15.876..20.110 and 22.085..26.319 where the logical stem faces are
    /// 16.076..19.910 and 22.285..26.119 — 0.2 outward at each of the four ends.
    /// </para>
    /// </remarks>
    public double DrawnStartX => StartX - BracketShortenPair;

    /// <inheritdoc cref="DrawnStartX"/>
    public double DrawnEndX => EndX + BracketShortenPair;

    /// <summary>The outward reach <c>shorten-pair</c> buys at each end, in staff spaces.</summary>
    internal const double BracketShortenPair = 0.2;

    /// <summary>
    /// Y-up of the tuplet number's visual center (LP TupletNumber Y, frame B).
    /// </summary>
    public double NumberYUp => (StartYUp + EndYUp) / 2.0;
}

/// <summary>
/// Calculates positions for tuplet brackets.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/tuplet-bracket.cc:1-400 Tuplet_bracket_interface
/// LILYPOND-REF: lily/tuplet-bracket.cc:779-817 get_default_dir
/// LILYPOND-REF: lily/tuplet-engraver.cc:1-200 Tuplet_engraver
///
/// LilyPond tuplet brackets:
/// - Horizontal bracket above or below the note group
/// - Number (e.g., "3") centered on the bracket
/// - Small hooks at bracket ends
/// - Position depends on majority stem direction of notes
/// </remarks>
internal static class TupletBracketEngraver
{
    // LILYPOND-REF: scm/define-grobs.scm TupletBracket defaults
    // LILYPOND-REF: scm/define-grobs.scm TupletBracket (padding . 1.1) —
    // distance from the encompass points (stem tips / staff edge) to the
    // bracket LINE. The 0.7 edge hooks eat into this and still clear.
    private const double BracketPadding = 1.1;
    // LILYPOND-REF: scm/define-grobs.scm TupletBracket (staff-padding . 0.25)
    // — the staff extent, widened by this, joins the encompass points, so
    // the bracket never enters the staff even over low notes.
    private const double StaffPaddingLP = 0.25;
    private const double EdgeHeight = 0.7;

    /// <summary>
    /// The tuplet number's font size, in staff spaces, from LilyPond's own scale.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: <c>scm/define-grobs.scm</c> TupletNumber <c>(font-size . -2)</c> —
    /// magnification steps of 2^(1/6) — applied to <c>scm/paper.scm:78</c>'s
    /// <c>text-font-size</c> of 11 pt, with one staff space = 5 pt at the default 20 pt
    /// staff. Written as the derivation rather than as 1.746141 so a different staff size
    /// still gets LilyPond's own answer. Lily# drew this digit at <c>FontSize * 0.6</c> =
    /// 2.4, an unsourced 37% larger.
    /// <para>
    /// A PROPERTY, not a <c>static readonly</c>: static initialisation order between
    /// partial classes is undefined in C#, and reading a not-yet-initialised default is
    /// how <c>ec7a2254</c> silently zeroed every change-clef width.
    /// </para>
    /// </remarks>
    internal static double NumberFontSize => 11.0 * Math.Pow(2.0, -2.0 / 6.0) / 5.0;

    /// <summary>The number's face: italic, and NOT bold.</summary>
    /// <remarks>LILYPOND-REF: <c>scm/define-grobs.scm</c> TupletNumber
    /// <c>(font-shape . italic)</c>, with no weight override.</remarks>
    internal const Rendering.FontStyle NumberFontStyle = Rendering.FontStyle.Italic;
    private const double StaffMiddleDown = 2.0;    // staff-top frame: middle line = StaffHeight/2
    private const double YOffsetAbove = -2.5;  // Above staff
    private const double YOffsetBelow = 5.5;   // Below staff

    // LILYPOND-REF: scm/define-grobs.scm TupletBracket (max-slope-factor . 0.5). The
    // endpoint height difference is limited to max-slope-factor × bracket width, NOT an
    // absolute value (lily/tuplet-bracket.cc:570 max_dy = max_slope_factor * last_x).
    private const double MaxSlopeFactor = 0.5;


    /// <summary>
    /// Y offset per nesting depth level for stacked nested tuplet brackets.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tuplet-bracket.cc:400-500 nested bracket stacking
    /// LILYPOND-REF: scm/define-grobs.scm TupletBracket.outside-staff-priority
    /// </remarks>
    private const double NestingDepthOffset = 2.0;

    /// <summary>
    /// Calculates layout for all tuplet brackets.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tuplet-bracket.cc:779-817 get_default_dir
    /// LILYPOND-REF: lily/tuplet-bracket.cc:444 calc_position_and_height (slope)
    /// LILYPOND-REF: scm/define-grobs.scm TupletBracket.bracket-visibility = if-no-beam
    ///
    /// Direction is determined by counting stem directions:
    /// - If stems UP > stems DOWN, bracket goes above (UP)
    /// - If stems DOWN > stems UP, bracket goes below (DOWN)
    /// - If equal, default to above (UP)
    ///
    /// bracket-visibility: if all notes in the tuplet are beamed, the bracket
    /// is hidden but the number is still shown.
    ///
    /// Slope: bracket follows the contour of the first and last note's staff position.
    /// </remarks>
    public static ImmutableArray<TupletBracketLayout> Calculate(
        ImmutableArray<TupletBracketItem> tuplets,
        ImmutableArray<MeasureLayout> measureLayouts,
        ImmutableArray<Measure> measures,
        ImmutableArray<BeamGroup> beamGroups = default,
        ImmutableArray<BeamLayout> beamLayouts = default,
        bool forceStemUp = false,
        Dictionary<int, ImmutableArray<Measure>>? measuresByStaff = null,
        Dictionary<int, ImmutableArray<Voice>>? voicesByStaff = null,
        Func<int, int, double>? staffYAt = null,
        Dictionary<int, Staff>? staffByIndex = null,
        ImmutableArray<ArticulationLayout> scripts = default)
    {
        if (tuplets.IsDefaultOrEmpty)
            return ImmutableArray<TupletBracketLayout>.Empty;

        var layouts = ImmutableArray.CreateBuilder<TupletBracketLayout>(tuplets.Length);

        for (int ti = 0; ti < tuplets.Length; ti++)
        {
            var tuplet = tuplets[ti];
            // Find measure layout
            if (tuplet.MeasureIndex >= measureLayouts.Length)
                continue;

            // A numbers-only tab (`tab … as numbers`) draws no tuplet bracket or
            // number — fret digits only, matching its suppressed stems and beams.
            if (staffByIndex != null
                && staffByIndex.TryGetValue(tuplet.StaffIndex, out var tupStaff)
                && tupStaff is { IsTab: true, TabNumbersOnly: true })
                continue;

            var measureLayout = measureLayouts[tuplet.MeasureIndex];

            // Find start and end X positions. Multi-staff layouts use
            // timing-aligned columns, not Items — go through the shared
            // resolver (a direct Items index silently shifts the bracket).
            if (measureLayout.Columns.IsDefaultOrEmpty
                && (tuplet.StartNoteIndex >= measureLayout.Items.Length ||
                    tuplet.EndNoteIndex >= measureLayout.Items.Length))
                continue;

            // Resolve this tuplet's OWN voice on its OWN staff (multi-staff /
            // polyphony): its measures drive the note staff positions used for
            // slope / X, and whether the staff is polyphonic drives the forced
            // stem side. A voice-2 tuplet must anchor to voice 2's notes and its
            // OWN stem direction — not the staff's primary voice.
            ImmutableArray<Voice> tupVoices = default;
            voicesByStaff?.TryGetValue(tuplet.StaffIndex, out tupVoices);
            // Polyphonic HERE, not somewhere else in the part: \voiceOne/\voiceTwo
            // live and die with the voice { } span.
            // LILYPOND-REF: scm/music-functions.scm:1042-1057 voicify-sublist / make-voice-props-set
            bool staffMultiVoice = !tupVoices.IsDefaultOrEmpty
                ? VoiceDefaults.IsPolyphonicAt(tupVoices, tuplet.MeasureIndex)
                : forceStemUp;
            ImmutableArray<Measure> tupMeasures =
                !tupVoices.IsDefaultOrEmpty && tuplet.VoiceIndex < tupVoices.Length
                    ? tupVoices[tuplet.VoiceIndex].Measures
                    : LayoutUtilities.ResolveStaffMeasures(measuresByStaff, tuplet.StaffIndex, measures);
            double staffOffset = staffYAt?.Invoke(tuplet.MeasureIndex, tuplet.StaffIndex) ?? 0;

            double startOffset = LayoutUtilities.GetItemXOffset(
                tupMeasures, tuplet.MeasureIndex, tuplet.StartNoteIndex, measureLayout);
            double endOffset = LayoutUtilities.GetItemXOffset(
                tupMeasures, tuplet.MeasureIndex, tuplet.EndNoteIndex, measureLayout);

            // LILYPOND-REF: lily/tuplet-bracket.cc:779-817 get_default_dir
            // In a polyphonic staff each voice's stems are FORCED by voice (voice 1
            // up / voice 2 down, VoiceDefaults), and the bracket sits on that
            // voice's stem side. Drive the direction from the tuplet's OWN voice —
            // the old staff-wide "multi-voice => up" put a lower voice's bracket on
            // the wrong (upper) side. Single-voice staves keep the pitch/stem
            // majority (CalculateDirection).
            bool isStemUp = staffMultiVoice
                ? (VoiceDefaults.GetDefaultStemUp(tuplet.VoiceIndex + 1)
                    ?? CalculateDirection(tuplet, tupMeasures))
                : CalculateDirection(tuplet, tupMeasures);

            // The bracket's bound items are the OUTER STEMS when the stems
            // point in the bracket's direction (always true here: the
            // bracket sits on the stem side), so the end hooks align with
            // the stem X — not the notehead edges.
            // LILYPOND-REF: lily/tuplet-bracket.cc:71-85 get_x_bound_item —
            //   bound = the column's stem when Note_column::dir == bracket
            //   dir; :180-189 x_span = bound extent LEFT/RIGHT edges.
            // Each bound reads its OWN head shape: :71-85 hands back that column's stem, and
            // a stem's x is its head's attachment (LayoutUtilities.StemAttachX). A tuplet
            // whose ends are a half note and a quarter has two different offsets.
            var startItem = TupletItemAt(tuplet, tupMeasures, tuplet.StartNoteIndex);
            var endItem = TupletItemAt(tuplet, tupMeasures, tuplet.EndNoteIndex);
            double startAttach = LayoutUtilities.StemAttachX(isStemUp,
                GlyphMetrics.NoteValueOf(startItem),
                LayoutUtilities.NoteheadStyleOf(startItem));
            double endAttach = LayoutUtilities.StemAttachX(isStemUp,
                GlyphMetrics.NoteValueOf(endItem),
                LayoutUtilities.NoteheadStyleOf(endItem));
            double halfStem = EngravingDefaults.StemThickness / 2;
            double startX = measureLayout.X + startOffset + startAttach - halfStem;
            double endX = measureLayout.X + endOffset + endAttach + halfStem;

            // LILYPOND-REF: lily/tuplet-bracket.cc:100-115 bracket_basic_visibility —
            //   the bracket is hidden ONLY when the tuplet's own beam is equally long.
            bool showBracket = !HasEquallyLongBeam(tuplet, beamGroups, tupMeasures);

            // Tab staves keep the raw-reach fallback: their staff positions are
            // string slots, not pitches, and no ledger point measures the tab
            // bracket regime (same gate session 30 left on the seed).
            bool isTabStaff = staffByIndex != null
                && staffByIndex.TryGetValue(tuplet.StaffIndex, out var encStaff)
                && encStaff.IsTab;

            // LILYPOND-REF: lily/tuplet-bracket.cc:566-629 slope calculation
            // Calculate slope based on first/last note staff positions
            var (startY, endY) = CalculateSlope(tuplet, tupMeasures, isStemUp, endX - startX,
                isTabStaff ? default : beamLayouts, measureLayout, useRealExtents: !isTabStaff,
                bracketStartX: startX, scripts: scripts);

            // When the bracket is suppressed (fully beamed), the NUMBER
            // attaches to the BEAM: centered between the outer stems, sitting
            // just off the beam line on its stem side — not at the bracket's
            // notehead-based position (which reads as shifted up-left).
            // LILYPOND-REF: lily/tuplet-number.cc — number follows the beam
            // when there is no bracket.
            bool tabBeamPlaced = false;
            if (!showBracket && !beamLayouts.IsDefaultOrEmpty)
            {
                var beam = FindCoveringBeam(beamLayouts, tuplet, tupMeasures);
                if (beam != null)
                {
                    isStemUp = beam.Group.StemUp;
                    // BeamLayout X values are notehead anchors; the stems (and
                    // thus the beam bar) sit at the attach offset — right of
                    // the head for up-stems (same correction DrawBeams makes),
                    // per OUTER MEMBER's head shape, as DrawBeams also does.
                    startX = beam.LeftX + LayoutUtilities.StemAttachX(
                        isStemUp, GlyphMetrics.NoteValueOf(beam.Group.Members[0].Item),
                        LayoutUtilities.NoteheadStyleOf(beam.Group.Members[0].Item));
                    endX = beam.RightX + LayoutUtilities.StemAttachX(
                        isStemUp, GlyphMetrics.NoteValueOf(beam.Group.Members[^1].Item),
                        LayoutUtilities.NoteheadStyleOf(beam.Group.Members[^1].Item));
                    // TAB BRANCH ONLY: the tab renderer's text offset assumes a
                    // baseline-anchored digit, so its clearance arithmetic still
                    // carries the digit height. The notation branch below no
                    // longer uses either — its number is centred on the
                    // invisible bracket. No ledger point measures the tab
                    // regime; when one does, this constant goes with it.
                    const double digitHeight = 1.7; // cap height of the old 2.4-unit digit

                    // On a TAB staff the beam floats a fixed distance off the fret
                    // digits — NOT at the notation beam's staff-relative height — so
                    // reconstructing its Y from beam.LeftY (a notation-frame value)
                    // and shifting by the staff offset lands the number on the tab
                    // beam. Instead clear the ACTUAL tab beam edge, recomputed from
                    // the same quanter the renderer draws; that value already bakes
                    // the staff offset, so skip the +staffOffset below.
                    // LILYPOND-REF: ly/engraver-init.ly TabVoice — the tuplet number
                    // sits outside the tab beam, mirroring ArticulationEngraver's
                    // tab branch (TabBeamOuterEdgeY).
                    if (staffByIndex != null
                        && staffByIndex.TryGetValue(tuplet.StaffIndex, out var tstaff)
                        && tstaff.IsTab && tstaff.Tuning.HasValue
                        // The tab-beam edge math below (TabBeamOuterEdgeY / TabBeamQuant)
                        // reads the beam MEMBERS' assigned strings, which is only valid
                        // when the covering beam is the tuplet's OWN tab beam. FindCovering-
                        // Beam can fall back to the companion NOTATION beam (a different
                        // staff), whose members carry no string and would quant a beam line
                        // from missing fret data. Require the tab staff's own beam; the
                        // notation fallback drops to the plain placement below.
                        && beam.StaffIndex == tuplet.StaffIndex)
                    {
                        var geom = new TabStaffGeometry(
                            tstaff.Tuning.Value, staffOffset, tstaff.TabSourceClef, tstaff.Transposition);
                        // A tab beam's direction is string-based, not the notation
                        // Group.StemUp — so the number sits on the tab beam's OWN side.
                        // Read it from the tuplet's tab notes (which carry the strings).
                        isStemUp = geom.GroupStemUp(TupletNoteItems(tuplet, tupMeasures));
                        startX = beam.LeftX + LayoutUtilities.StemAttachX(
                            isStemUp, GlyphMetrics.NoteValueOf(beam.Group.Members[0].Item),
                            LayoutUtilities.NoteheadStyleOf(beam.Group.Members[0].Item));
                        endX = beam.RightX + LayoutUtilities.StemAttachX(
                            isStemUp, GlyphMetrics.NoteValueOf(beam.Group.Members[^1].Item),
                            LayoutUtilities.NoteheadStyleOf(beam.Group.Members[^1].Item));
                        const double tabClearance = 0.5; // baseline above beam edge
                        double sEdge = ArticulationEngraver.TabBeamOuterEdgeY(beam, geom, startX);
                        double eEdge = ArticulationEngraver.TabBeamOuterEdgeY(beam, geom, endX);
                        // Compensate the renderer's own -0.3 (up) / +0.8 (down) text
                        // offset so the digit clears the beam by tabClearance.
                        double tabOff = isStemUp
                            ? -tabClearance + 0.3
                            : tabClearance + digitHeight - 0.8;
                        startY = sEdge + tabOff;
                        endY = eEdge + tabOff;
                        tabBeamPlaced = true;
                    }
                    else
                    {
                        // The INVISIBLE bracket spans the TUPLET'S OWN outer stems,
                        // not the covering beam's ends: one auto-beam can cover
                        // several tuplets (two 16th triplets inside one beat —
                        // tuplet-number-alignment.ly), and reading the beam's span
                        // stacked every number onto the same beam midpoint.
                        // LILYPOND-REF: lily/tuplet-bracket.cc:495-519
                        //   calc_position_and_height follow-beam — points from
                        //   columns[0] / columns.back(), the tuplet's own stems;
                        // LILYPOND-REF: lily/tuplet-number.cc:294-299 calc_x_offset —
                        //   the number centres on the bracket's own X-positions.
                        // MemberXPositions are HEAD anchors (SharedRenderer.Beams
                        // applies LayoutUtilities.StemX on top of them), so the
                        // stem centre needs the same attach correction here —
                        // an up-stem tuplet's number sat half a head left without
                        // it (LP centres on the stems: tupnumss twin, stem
                        // midpoint 26.73 = LP number centre 26.69).
                        if (!beam.MemberXPositions.IsDefaultOrEmpty)
                        {
                            for (int mi = 0; mi < beam.Group.Members.Length
                                 && mi < beam.MemberXPositions.Length; mi++)
                            {
                                var m = beam.Group.Members[mi];
                                if (m.ResolveMeasureIndex(beam.Group.MeasureIndex)
                                    != tuplet.MeasureIndex)
                                    continue;
                                double attach = LayoutUtilities.StemAttachX(isStemUp,
                                    GlyphMetrics.NoteValueOf(m.Item),
                                    LayoutUtilities.NoteheadStyleOf(m.Item));
                                if (m.ItemIndex == tuplet.StartNoteIndex)
                                    startX = beam.MemberXPositions[mi] + attach;
                                if (m.ItemIndex == tuplet.EndNoteIndex)
                                    endX = beam.MemberXPositions[mi] + attach;
                            }
                        }
                        // ⚠️ THE Y IS NOT RECOMPUTED HERE ANY MORE — only the X is.
                        // LilyPond's TupletNumber reads the BRACKET's position whether or
                        // not the bracket is printed (lily/tuplet-number.cc:342
                        // calc_y_offset — the bracket midpoint, for every tuplet that is
                        // not a knee), and CalculateSlope now computes that position from
                        // the beam itself in exactly this case (follow_beam, ported from
                        // lily/tuplet-bracket.cc:491-519 + :633-637). Keeping a second
                        // formula here made the two disagree by the bracket thickness.
                        // MEASURED on the LP twins scratch/lpreg/tupnum{a,b}-lp.svg, which
                        // differ only in 8ths vs 16ths: LilyPond puts the number at the
                        // SAME y=15.3153 in both — bracket hidden in (a), drawn in (b) —
                        // and its beam ink edge is 13.4756 in both, so the offset is
                        // +1.26 either way. The old spelling here used +1.100 (padding
                        // alone) and left the hidden-bracket book 0.16 short, which is
                        // precisely what TupletNumberAlignmentTests
                        // .EighthAndSixteenthBeams_PutTheNumberAtTheSameHeight asserts
                        // against.
                    }
                }
            }

            // Bake the staff's within-system offset (multi-staff) so the bracket
            // sits over its OWN staff, not the first. The tab-beam path already
            // baked it (its Y comes from TabStaffGeometry), so skip it there.
            if (!tabBeamPlaced)
            {
                startY += staffOffset;
                endY += staffOffset;
            }
            // Store Y-up from the system top; the placement above stays in the
            // device staff-top frame (system.Y is added at draw), so negate here.
            layouts.Add(new TupletBracketLayout(
                tuplet.MeasureIndex,
                startX,
                endX,
                -startY,
                -endY,
                tuplet.DisplayText,
                isStemUp,
                showBracket,
                tuplet.SourcePosition,
                ti,
                StaffIndex: tuplet.StaffIndex
            ));
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// Overload with measures but no beam info.
    /// </summary>
    public static ImmutableArray<TupletBracketLayout> Calculate(
        ImmutableArray<TupletBracketItem> tuplets,
        ImmutableArray<MeasureLayout> measureLayouts,
        ImmutableArray<Measure> measures)
    {
        return Calculate(tuplets, measureLayouts, measures, default);
    }

    /// <summary>
    /// Calculates the bracket direction based on stem directions of notes.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tuplet-bracket.cc:779-817 get_default_dir implementation
    /// Counts stem directions and returns the majority direction. Equal counts
    /// tiebreak on the extremal head positions (no stems at all → UP); see the
    /// port in the body.
    /// </remarks>
    private static bool CalculateDirection(TupletBracketItem tuplet, ImmutableArray<Measure> measures)
    {
        if (measures.IsDefaultOrEmpty || tuplet.MeasureIndex >= measures.Length)
            return true; // Default: stems up (bracket above)

        var measure = measures[tuplet.MeasureIndex];
        int stemsUp = 0;
        int stemsDown = 0;

        // Count stem directions for notes in the tuplet
        for (int i = tuplet.StartNoteIndex; i <= tuplet.EndNoteIndex && i < measure.Items.Length; i++)
        {
            var item = measure.Items[i];
            
            // LILYPOND-REF: lily/tuplet-bracket.cc:786
            // Skip rests when counting directions
            if (item is NoteItem note)
            {
                if (note.StemUp)
                    stemsUp++;
                else
                    stemsDown++;
            }
            else if (item is ChordItem chord)
            {
                if (chord.StemUp)
                    stemsUp++;
                else
                    stemsDown++;
            }
        }

        // Equal counts: no stems at all → UP; otherwise the tie goes to the side
        // whose extreme head protrudes deeper past the staff edge in its own
        // direction — a down-stem C6 outweighs an up-stem F4 (the regression book
        // tuplet-bracket-direction.ly pinned this: LP puts that bracket DOWN).
        // The staff-edge constants cancel in the comparison (it reduces to
        // extUp + extDown <= 0), but the letter keeps them: staff extent ±2.0
        // staff SPACES against head POSITIONS in half-spaces is LP's own unit mix.
        // LILYPOND-REF: lily/tuplet-bracket.cc:793-813 get_default_dir
        //   (the extremal-positions tiebreak; :795-796 the no-stem UP).
        if (stemsUp == stemsDown)
        {
            if (stemsUp == 0)
                return true;
            double extUp = double.NegativeInfinity;
            double extDown = double.PositiveInfinity;
            for (int i = tuplet.StartNoteIndex; i <= tuplet.EndNoteIndex && i < measure.Items.Length; i++)
            {
                // A rest column's head interval is EMPTY in LP (:803-809 walks
                // it), so skipping rests here reads as the same answer. That is
                // an EQUIVALENCE argument, not LP's letter — but every pinned
                // tie case carries a rest column (tuplet-rest t1/t7/t8,
                // tuplet-bracket-direction t4/t5) and all match LP.
                switch (measure.Items[i])
                {
                    case NoteItem n:
                        if (n.StemUp) extUp = Math.Max(extUp, n.StaffPosition);
                        else extDown = Math.Min(extDown, n.StaffPosition);
                        break;
                    case ChordItem c when c.Notes.Length > 0:
                        if (c.StemUp) extUp = Math.Max(extUp, c.Notes.Max(x => x.StaffPosition));
                        else extDown = Math.Min(extDown, c.Notes.Min(x => x.StaffPosition));
                        break;
                }
            }
            double protrudeUp = extUp - 2.0;      // -UP · (staff[UP] − ext[UP])
            double protrudeDown = -2.0 - extDown; // -DOWN · (staff[DOWN] − ext[DOWN])
            return protrudeUp <= protrudeDown;    // :813 — UP keeps the final tie
        }

        // LILYPOND-REF: lily/tuplet-bracket.cc:816 get_default_dir majority
        return stemsUp > stemsDown;
    }

    /// <summary>
    /// True when the tuplet's own beam has the SAME BOUNDS as the tuplet — the one case
    /// in which the bracket is not drawn.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tuplet-bracket.cc:100-115 bracket_basic_visibility —
    ///   <c>bracket_visibility = !(par_beam &amp;&amp; equally_long)</c>, where
    ///   <c>equally_long</c> is lily/tuplet-bracket.cc:88-98 equal_bounds: the beam and the
    ///   bracket must share the same LEFT column and the same RIGHT column. A beam that
    ///   merely COVERS the tuplet is not enough.
    /// <para>
    /// ⚠️ TupletBracket has NO <c>bracket-visibility</c> default — read
    /// scm/define-grobs.scm:4097-4125, the whole grob definition: the property is absent, so
    /// <c>scm_is_bool</c> and the <c>if-no-beam</c> branch are both skipped and the
    /// equal-bounds rule above is what runs. The old code here cited
    /// "define-grobs.scm bracket-visibility = if-no-beam" for a default that is not there,
    /// and implemented the STRONGER rule that citation implies (any covering beam hides the
    /// bracket).
    /// </para>
    /// <para>
    /// MEASURED with a positive control (scratch/beamskip/lp-tuplet.ly, three scores, one
    /// paper): beam exactly over the tuplet -&gt; <b>0</b> bracket lines; no beam at all
    /// -&gt; 4; beam LONGER than the tuplet (autobeam-tuplet-recheck's shape) -&gt; 4 per
    /// tuplet. Lily# drew none in the third case — the number floated with no bracket.
    /// </para>
    /// </remarks>
    private static bool HasEquallyLongBeam(TupletBracketItem tuplet,
        ImmutableArray<BeamGroup> beamGroups, ImmutableArray<Measure> tupMeasures)
    {
        if (beamGroups.IsDefaultOrEmpty)
            return false;

        // Covers() answers "is this the tuplet's OWN beam" (par_beam); the bounds test
        // answers "equally_long". LilyPond needs both.
        foreach (var beam in beamGroups)
        {
            if (!Covers(beam, tuplet, tupMeasures))
                continue;
            if (HasSameBounds(beam, tuplet))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The port of <c>equal_bounds</c>: the beam's outer stems stand on the tuplet's outer
    /// columns.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tuplet-bracket.cc:88-98 equal_bounds — LilyPond compares the
    ///   spanners' bound COLUMNS. A beam member's item index is that column here.
    /// </remarks>
    private static bool HasSameBounds(BeamGroup beam, TupletBracketItem tuplet)
    {
        if (beam.Members.Length == 0)
            return false;
        var first = beam.Members[0];
        var last = beam.Members[^1];
        return first.ResolveMeasureIndex(beam.MeasureIndex) == tuplet.MeasureIndex
               && last.ResolveMeasureIndex(beam.MeasureIndex) == tuplet.MeasureIndex
               && first.ItemIndex == tuplet.StartNoteIndex
               && last.ItemIndex == tuplet.EndNoteIndex;
    }

    /// <summary>
    /// True when <paramref name="beam"/> is the tuplet's own beam: same measure,
    /// same VOICE, and its member set contains every NOTE slot of the tuplet's
    /// range (rests are transparent — a manual beam legitimately spans them).
    /// The old span check (StartIndex + Members.Length - 1) assumed contiguous
    /// members, and no voice check meant ANOTHER voice's beam at the same item
    /// range could hide this voice's bracket.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tuplet-bracket.cc:107-112 — the if-no-beam visibility
    /// check consults the bracket's OWN beam (the beam of the stems it
    /// encompasses), never a different voice's.
    /// </remarks>
    private static bool Covers(BeamGroup beam, TupletBracketItem tuplet,
        ImmutableArray<Measure> tupMeasures)
    {
        if (beam.MeasureIndex != tuplet.MeasureIndex || beam.VoiceIndex != tuplet.VoiceIndex)
            return false;

        var members = new HashSet<int>();
        foreach (var m in beam.Members)
            if (m.ResolveMeasureIndex(beam.MeasureIndex) == tuplet.MeasureIndex)
                members.Add(m.ItemIndex);

        var items = !tupMeasures.IsDefaultOrEmpty && tuplet.MeasureIndex < tupMeasures.Length
            ? tupMeasures[tuplet.MeasureIndex].Items
            : default;

        bool sawNote = false;
        for (int i = tuplet.StartNoteIndex; i <= tuplet.EndNoteIndex; i++)
        {
            // Without item info every slot is treated as a note (conservative:
            // more slots must be members before the bracket may hide).
            bool isNote = items.IsDefault || i >= items.Length
                || items[i] is NoteItem or ChordItem;
            if (!isNote)
                continue;
            sawNote = true;
            if (!members.Contains(i))
                return false;
        }
        return sawNote;
    }

    /// <summary>
    /// Calculates the Y positions (with slope) for the tuplet bracket
    /// based on the staff positions of the first and last notes.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tuplet-bracket.cc:444 calc_position_and_height
    /// The bracket follows the contour of the notes. The slope is limited
    /// to avoid excessively tilted brackets.
    /// </remarks>
    private static BeamLayout? FindCoveringBeam(
        ImmutableArray<BeamLayout> beamLayouts, TupletBracketItem tuplet,
        ImmutableArray<Measure> tupMeasures)
    {
        // Prefer the beam on the tuplet's OWN staff: in a staff+tab score the same
        // notes carry both a notation beam (no string numbers) and a tab beam. A tab
        // tuplet must read its TAB beam so the number's side and the beam edge come
        // from the strings, not the pitch. Covers() matches only measure+voice.
        BeamLayout? fallback = null;
        foreach (var beam in beamLayouts)
        {
            if (!Covers(beam.Group, tuplet, tupMeasures))
                continue;
            if (beam.StaffIndex == tuplet.StaffIndex)
                return beam;
            fallback ??= beam;
        }
        if (fallback != null)
            return fallback;
        return null;
    }

    // The column's REAL extent on the bracket's side (quanted beam face / drawn stem
    // end / head ink) lives in NoteColumnLayout.OutwardTipDeviceY — the single house of
    // a column's reach (HANDOFF §5.2.1②, session 34's port of
    // Note_column::cross_staff_extent). The ledger pair
    // staff.staff.tuplet-bracket-{partial-beam,shortened-stem} pins that read.

    /// <summary>
    /// The pre-port raw reach, kept for tab staves only: string-slot staff positions,
    /// and no ledger point measures the tab bracket regime (LILYSHARP-OWN gate, the
    /// same one session 30 left on the seed). Tab staves never reach the real-extent
    /// read (<c>useRealExtents</c> false keeps this).
    /// </summary>
    private static double RawOutwardTip(int staffPosition, Semantics.Fraction? baseDuration, bool isStemUp)
    {
        double noteY = StaffMiddleDown - (staffPosition * 0.5);
        int noteValue = baseDuration is { } d ? LayoutUtilities.GetNoteValueFromFraction(d) : int.MaxValue;
        double reach = noteValue >= 2
            ? EngravingDefaults.DefaultStemLength
            : GlyphMetrics.GetNoteheadBBox(noteValue).Top;
        return isStemUp ? noteY - reach : noteY + reach;
    }

    // LILYPOND-REF: lily/tuplet-bracket.cc:530-549 calc_position_and_height's general
    //   branch — graphical_dy = rv[dir] - lv[dir] off cross_staff_extent, zeroed when its
    //   sign disagrees with the musical head contour (head_positions_interval).
    // ⚠️ DERIVED, NOT TRANSCRIBED, and REF'd for exactly that reason (HANDOFF 5.2 / 7.6 ⒝):
    //   the quantity below is LilyPond's, the form is not, so the address has to stay
    //   readable or the next hand reads this as an invention and rebuilds it. It is NOT
    //   LILYSHARP-OWN — that label is for a quantity LilyPond does not have, and LilyPond
    //   has this one.
    // Three differences, all in the direction of SIMPLER, none of them yet measured:
    //   ⑴ Lily# slopes from the outer MUSICAL positions (firstPos/lastPos, staff positions),
    //      LilyPond from the GRAPHICAL extents with the sign guard above;
    //   ⑵ LilyPond damps against the covering beam's own slope (:566-630, max_slope read
    //      off quantized-positions), Lily# applies the max-slope-factor cap only;
    //   ⑶ LilyPond QUANTIZES a near-flat bracket onto staff positions when it lies inside
    //      the widened staff (:726-746); Lily# does not quantize at all.
    // ⚠️ WHY IT IS SIMPLER: NOT a trade-off anyone made, and NOT performance. The body
    //   predates the porting discipline (it arrives whole in the bulk commit 26f91d85 of
    //   2026-02-24, before the ledger existed); the words "simpler than LilyPond's" were
    //   written on 2026-07-29 while the encompass beside it was being ported, i.e. they
    //   DESCRIBE an unported device rather than record a decision. Read them that way.
    // ★ AND THE INPUTS ARE ALREADY HERE — checked 2026-08-01, after a first version of this
    //   comment guessed otherwise and was wrong. The same loop below already builds the
    //   columns' real outward reach (NoteColumnLayout.OutwardTipDeviceY, under
    //   useRealExtents) and already resolves the covering beam through MemberBeam(i), whose
    //   BeamLayout carries the quanted positions. So ⑴ and ⑵ want a different READ of data
    //   this function holds, not new data threaded in, and ⑶ is free after ⑵.
    // ⚠️ WHAT ACTUALLY BLOCKS IT IS THE MISSING PAIR, not the plumbing. The ledger pair
    //   staff.staff.tuplet-bracket-* pins the ENCOMPASS only (flat, outside the staff —
    //   none of the three fire there), so a sloped / staff-adjacent pair has to be opened
    //   first (HANDOFF 5.0: points before ports).
    private static (double startY, double endY) CalculateSlope(
        TupletBracketItem tuplet, ImmutableArray<Measure> measures, bool isStemUp, double bracketWidth,
        ImmutableArray<BeamLayout> beamLayouts = default, MeasureLayout? measureLayout = null,
        bool useRealExtents = false, double bracketStartX = double.NaN,
        ImmutableArray<ArticulationLayout> scripts = default)
    {
        double nestingOffset = tuplet.NestingDepth * NestingDepthOffset;
        // Fallback only — when no note positions are found the bracket
        // parks outside the staff. The real position is NOTE-DRIVEN below.
        double baseY = isStemUp
            ? YOffsetAbove - nestingOffset
            : YOffsetBelow + nestingOffset;

        if (measures.IsDefaultOrEmpty || tuplet.MeasureIndex >= measures.Length)
            return (baseY, baseY);

        var measure = measures[tuplet.MeasureIndex];

        // The beam an item's stem belongs to (same measure + voice, own staff
        // preferred) and the member's index in it — its quanted face at the beam
        // model's OWN member X is that stem's real end (the same canonical read
        // ArticulationEngraver's beam-side scripts make).
        // LILYPOND-REF: lily/tuplet-bracket.cc:504-509, lily/stem.cc Stem::get_beam.
        (BeamLayout beam, int memberIndex)? MemberBeam(int itemIndex)
        {
            if (beamLayouts.IsDefaultOrEmpty)
                return null;
            (BeamLayout, int)? fallback = null;
            foreach (var b in beamLayouts)
            {
                if (b.Group.MeasureIndex != tuplet.MeasureIndex
                    || b.Group.VoiceIndex != tuplet.VoiceIndex)
                    continue;
                int member = -1;
                for (int mi = 0; mi < b.Group.Members.Length; mi++)
                {
                    var m = b.Group.Members[mi];
                    if (m.ResolveMeasureIndex(b.Group.MeasureIndex) == tuplet.MeasureIndex
                        && m.ItemIndex == itemIndex)
                    {
                        member = mi;
                        break;
                    }
                }
                if (member < 0)
                    continue;
                if (b.StaffIndex == tuplet.StaffIndex)
                    return (b, member);
                fallback ??= (b, member);
            }
            return fallback;
        }

        // Get staff positions of first and last notes, and — separately — the most
        // OUTWARD point any of them reaches on the bracket's side.
        int? firstPos = null, lastPos = null;
        // LP port inputs: per-column encompass points (x = column LEFT edge, the
        // column REFPOINT — measured six-digit on the TBSD/TBSA pair: the offset
        // pass's x is refpoint − x0 and goes NEGATIVE at the left bound — and
        // y = the column's outward reach in Y-up staff-middle spaces), plus the
        // bound columns' head-position INTERVALS for the musical sign gates.
        // LILYPOND-REF: lily/tuplet-bracket.cc:554-562 calc_position_and_height
        //   points loop; lily/tuplet-bracket.cc:537-542 head_positions_interval
        //   into musical_dy.
        var lpPoints = new List<(double X, double YUp)>();
        int firstLo = 0, firstHi = 0, lastLo = 0, lastHi = 0;
        double firstTipUp = 0, lastTipUp = 0, lastColX = double.NaN;
        (BeamLayout beam, int memberIndex)? lpAnyBeam = null;
        // The extreme ENCOMPASS POINT in the staff-top device frame (down-positive),
        // not the extreme staff POSITION. Those are different aggregates the moment the
        // tuplet's members differ in duration: a stemless whole note reaches only its own
        // notehead ink while a half note reaches a full stem, so the note that is highest
        // on the staff need not be the one the bracket has to clear.
        // LILYPOND-REF: lily/tuplet-bracket.cc calc_position_and_height — the points are
        //   the note columns' own extents (Note_column::cross_staff_extent[dir]).
        double? extremeTip = null;

        for (int i = tuplet.StartNoteIndex; i <= tuplet.EndNoteIndex && i < measure.Items.Length; i++)
        {
            var item = measure.Items[i];
            int? pos = item switch
            {
                NoteItem note => note.StaffPosition,
                ChordItem chord when chord.Notes.Length > 0 =>
                    isStemUp ? chord.Notes.Max(n => n.StaffPosition)
                             : chord.Notes.Min(n => n.StaffPosition),
                _ => null
            };

            if (pos == null) continue;

            // Head-position interval of the column (chord: min..max) — only the
            // SIGNS of last−first feed the LP gates.
            (int lo, int hi) posIv = item switch
            {
                NoteItem n2 => (n2.StaffPosition, n2.StaffPosition),
                ChordItem c2 => (c2.Notes.Min(n => n.StaffPosition),
                                 c2.Notes.Max(n => n.StaffPosition)),
                _ => (pos.Value, pos.Value),
            };
            if (firstPos == null)
                (firstLo, firstHi) = posIv;
            (lastLo, lastHi) = posIv;

            firstPos ??= pos;
            lastPos = pos;

            var duration = item switch
            {
                NoteItem note => note.BaseDuration,
                ChordItem chord => chord.BaseDuration,
                _ => (Semantics.Fraction?)null
            };
            double tip;
            if (useRealExtents && measureLayout is { } ml)
            {
                bool itemUp = item switch
                {
                    NoteItem n => n.StemUp,
                    ChordItem c => c.StemUp,
                    _ => isStemUp
                };
                // ONE MemberBeam probe per column: it scans beamLayouts×members
                // (the bars² family perf round 16 already had to cache for slurs)
                // — the stem read and the damping's beam pick must share it.
                var probedBeam = MemberBeam(i);
                var member = itemUp == isStemUp ? probedBeam : null;
                // LP scans the columns from the END and keeps the first beam it
                // meets — i.e. the LAST beamed column's beam. Forward loop here,
                // so overwrite instead of keep-first.
                // LILYPOND-REF: lily/tuplet-bracket.cc:584 calc_position_and_height
                //   `for (vsize i = columns.size (); i--;)` ... break.
                lpAnyBeam = probedBeam ?? lpAnyBeam;
                double colX = ml.X
                    + LayoutUtilities.GetItemXOffset(measures, tuplet.MeasureIndex, i, ml);
                // ⚠️ MemberXPositions are HEAD anchors, not stem centres (the
                // renderer applies LayoutUtilities.StemX on top — established
                // by tuplet-number-slur-script.ly, 2026-08-09), so the beamed
                // branch here reads the stem's X half a head LEFT on up-stems.
                // Left standing: it only feeds a DRAWN bracket over a PARTIAL
                // beam, where the pinned point (staff.staff.tuplet-bracket-
                // partial-beam) is a FLAT beam — the face Y it reads is the
                // same at either X. A SLOPED partial beam would surface the
                // seam (face shifts by slope × attach); no corpus book measures
                // that regime.
                double stemX = member is { } mb && !mb.beam.MemberXPositions.IsDefaultOrEmpty
                    ? mb.beam.MemberXPositions[mb.memberIndex]
                    : colX
                      + LayoutUtilities.StemAttachX(itemUp, GlyphMetrics.NoteValueOf(item),
                          LayoutUtilities.NoteheadStyleOf(item));
                // The single house of a column's reach (HANDOFF §5.2.1②). Of() cannot
                // return null here — the `pos` gate above keeps only notes/chords.
                tip = NoteColumnLayout.Of(item, null, member?.beam, stemX) is { } col
                    ? col.OutwardTipDeviceY(isStemUp)
                    : RawOutwardTip(pos.Value, duration, isStemUp);
                // Y-up staff-middle spaces (device staff-top middle = 2.0).
                double tipUp = 2.0 - tip;
                if (lpPoints.Count == 0)
                    firstTipUp = tipUp;
                lastTipUp = tipUp;
                lastColX = colX;
                lpPoints.Add((colX, tipUp));
            }
            else
            {
                tip = RawOutwardTip(pos.Value, duration, isStemUp);
            }
            extremeTip = extremeTip == null
                ? tip
                : (isStemUp ? Math.Min(extremeTip.Value, tip) : Math.Max(extremeTip.Value, tip));
        }

        if (firstPos == null || lastPos == null || extremeTip == null)
        {
            // An ALL-REST tuplet still runs LP's offset pass: with no note
            // points the staff edge at both bounds is the only encompass, so
            // the flat bracket sits at staff ink 2.3 + padding 1.1 = 3.4 above
            // the middle line (tuplet-rest.ly t4, LP 3.400 measured; the old
            // fallback parked it at the fixed 4.5).
            // LILYPOND-REF: lily/tuplet-bracket.cc:633-637 calc_position_and_height
            //   (the staff points join regardless of columns); :708-726 the
            //   offset pass + padding.
            // ⚠️ Disclosed: LP also pushes the REST columns' own ink as points
            //   (:554-562 walks every column raw) and takes a rest column as a
            //   slope BOUND (:525-535) — a default mid-staff rest never beats
            //   the staff edge, so neither is wired; tuplet-rest.ly t5/t6
            //   measure the bound seam at ≤0.055 (rest-bound slope regime).
            if (useRealExtents && !double.IsNaN(bracketStartX) && bracketWidth > 0.001)
            {
                int rdir = isStemUp ? 1 : -1;
                double off = (2.3 + BracketPadding) * rdir + nestingOffset * rdir;
                double posQ = off / 0.5;
                if (posQ >= -5.0 && posQ <= 5.0)
                {
                    posQ = Math.Round(posQ, MidpointRounding.ToEven);
                    if ((int)posQ % 2 == 0 && Math.Abs((int)posQ) <= 4)
                        posQ += rdir;
                    off = posQ * 0.5;
                }
                return (2.0 - off, 2.0 - off);
            }
            return (baseY, baseY);
        }

        // ---- LP port: calc_position_and_height, the no-beam (drawn-bracket)
        // branch — graphical dy from the bound columns UNITED WITH THE STAFF,
        // sign gates, damping, then the per-point offset pass. Pinned six-digit
        // by the ledger pair staff.staff.tuplet-bracket-sloped-{desc,asc}
        // (positions (3.6 . 3.4) / (3.446261350737798 . 3.646261350737798)).
        // The tab/fallback path below keeps the old derived formula (tab staff
        // positions are string slots; no ledger point measures that regime).
        // LILYPOND-REF: lily/tuplet-bracket.cc:520-631 calc_position_and_height
        //   (graphical dy, sign gates, damping); lily/tuplet-bracket.cc:633-637
        //   calc_position_and_height staff points; lily/tuplet-bracket.cc:708-746
        //   calc_position_and_height offset pass + flat quantize.
        // ⚠️ Unported clauses, disclosed (no pinned point reaches any of them):
        //   ⑴ ★ PORTED (was: "vacuously false here"). The old reasoning was
        //     "a beam covering the WHOLE tuplet hides the bracket, so a DRAWN bracket
        //     never carries LP's par_beam" — true only under the WRONG visibility rule
        //     this engraver used to have. LilyPond hides the bracket only when the beam
        //     is EQUALLY LONG (see HasEquallyLongBeam), so a covering-but-longer beam
        //     draws a bracket AND carries par_beam. Measured on autobeam-tuplet-recheck:
        //     with the staff floor still applied the bracket came out 1.90 ss above
        //     LilyPond's (Lily# 8.29 vs LP 10.1906 device, beams identical).
        //   ⑵ the scripts term (:682-706 avoid-scripts) — PORTED into the offset
        //     pass below (tuplet-bracket-avoid-scripts.ly pinned it), with its
        //     own narrowings disclosed at the port site;
        //   ⑶ nested-tuplet points (:646-680) — the Lily#-own NestingDepthOffset
        //     step below stands in;
        //   ⑷ staff-padding's cross-staff gate (:466-477) — every bracket this
        //     engraver sees lives on one staff, vacuously true;
        //   ⑸ x0/x1 come from the caller's stem-attach faces for BOTH bounds —
        //     LP's get_x_bound_item falls back to the COLUMN when a bound stem
        //     points AGAINST the bracket (mixed-direction tuplets).
        if (lpPoints.Count > 0 && !double.IsNaN(bracketStartX) && bracketWidth > 0.001)
        {
            int dir = isStemUp ? 1 : -1;             // Y-up
            double span = bracketWidth;              // x0..x1 = bound stem faces
            double x0 = bracketStartX;
            // LILYPOND-REF: lily/tuplet-bracket.cc:491-492 calc_position_and_height —
            //   follow_beam = par_beam && the beam points the bracket's way && not knee.
            //   It selects LP's FIRST branch (:495-519), where the encompass points are
            //   the two outer STEM TIPS and the staff never enters: neither the slope
            //   (:530-533 rv/lv.unite(staff) live in the ELSE branch) nor the offset pass
            //   (:633-637 pushes the staff edge only `if (!follow_beam)`).
            // ⚠️ default(ImmutableArray<T>) throws on foreach — the unit tests call this
            //    with no beams at all. Same guard the caller uses before FindCoveringBeam.
            var parBeam = beamLayouts.IsDefaultOrEmpty
                ? null : FindCoveringBeam(beamLayouts, tuplet, measures);
            bool followBeam = parBeam != null
                              && parBeam.Group.StemUp == isStemUp
                              && !parBeam.Group.IsKnee;
            // The staff, ink 2.05 widened by staff-padding 0.25, united into the
            // bound columns' extents — THIS is what flattens a within-staff
            // tuplet (:530-535 rv.unite (staff)).
            double staffEdge = 2.3 * dir;
            double lvDir = followBeam ? firstTipUp
                : dir > 0 ? Math.Max(firstTipUp, staffEdge) : Math.Min(firstTipUp, staffEdge);
            double rvDir = followBeam ? lastTipUp
                : dir > 0 ? Math.Max(lastTipUp, staffEdge) : Math.Min(lastTipUp, staffEdge);
            double graphicalDy = rvDir - lvDir;

            // Musical sign gates (:537-549): zero the dy when the chord's top and
            // bottom move opposite ways, or when the graphical dy contradicts them.
            int musUp = Math.Sign(lastHi - firstHi);
            int musDown = Math.Sign(lastLo - firstLo);
            double dy;
            if (musUp != musDown)
                dy = 0.0;
            else if (Math.Sign(graphicalDy) != musDown)
                dy = 0.0;
            else
                dy = graphicalDy;

            // Damping (:566-630): max_dy = max-slope-factor × the LAST column's x;
            // a covering beam lends its own slope as the cap.
            if (dy != 0.0)
            {
                double lpSlope = Math.Abs(dy / span);
                double lastX = lastColX - x0;
                double lpMaxDy = MaxSlopeFactor * lastX * Math.Sign(dy);
                double beamDy = 0.0, subSpan = 0.0;
                if (lpAnyBeam is { } ab)
                {
                    // The beam's quanted outer-edge Y-up at its two ends — the
                    // spelled stand-in for LP's quantized-positions read (:576-604).
                    beamDy = ab.beam.OuterEdgeStaffSpaceAtX(ab.beam.RightX, isStemUp)
                        - ab.beam.OuterEdgeStaffSpaceAtX(ab.beam.LeftX, isStemUp);
                    subSpan = ab.beam.RightX - ab.beam.LeftX;
                }
                if (beamDy != 0.0)
                {
                    double beamSlope = Math.Abs(beamDy / (subSpan > 0.001 ? subSpan : span));
                    double maxSlope = beamSlope != 0.0
                        ? Math.Max(beamSlope, MaxSlopeFactor) : MaxSlopeFactor;
                    lpSlope = Math.Min(lpSlope, maxSlope);
                    if (Math.Abs(dy) > Math.Abs(lpMaxDy))
                        dy = Math.Abs(dy * lpSlope) <= Math.Abs(lpMaxDy) ? dy * lpSlope : lpMaxDy;
                }
                else if (Math.Abs(dy) > Math.Abs(lpMaxDy))
                {
                    dy = lpMaxDy;
                }
            }

            // The offset pass (:708-719): every column point + the staff edge at
            // x0 and x1, cleared against the sloped chord, then padding 1.1.
            double factor = lpPoints.Count > 1 ? 1.0 / span : 1.0;
            double offsetUp = dir > 0 ? double.NegativeInfinity : double.PositiveInfinity;
            void Clear(double px, double py)
            {
                double tuplety = dy * px * factor;
                if (dir * py > dir * (offsetUp + tuplety))
                    offsetUp = py - tuplety;
            }
            foreach (var p in lpPoints)
                Clear(p.X - x0, p.YUp);
            // LILYPOND-REF: lily/tuplet-bracket.cc:633-637 calc_position_and_height —
            //   `if (!follow_beam) { points.push_back(staff[dir]) ×2 }`. A bracket that
            //   rides its own beam is NOT lifted clear of the staff; it sits one padding
            //   off the beam wherever the beam is, INSIDE the staff when the beam is.
            if (!followBeam)
            {
                Clear(0.0, staffEdge);
                Clear(span, staffEdge);
            }
            // The avoid-scripts term: every script of this tuplet's notes that
            // declares NO outside-staff-priority adds the point (its X centre
            // − x0, its ink edge on the bracket's side), and the same max pass
            // clears the bracket over it. TupletBracket declares avoid-scripts
            // #t and no outside-staff-priority of its own by default, and no
            // Lily# grammar can override either — the gate is always open. A
            // script WITH a priority (the fermata family's 75) is skipped: it
            // is an outside-staff MOVER and clears the bracket, not the other
            // way around.
            // LILYPOND-REF: lily/tuplet-bracket.cc:682-706 calc_position_and_height
            //   (the avoid-scripts block); lily/tuplet-engraver.cc:199-233
            //   acknowledge_script → add_script_to_all_tuplets (dynamics excluded).
            // ⚠️ Disclosed narrowings (no pinned point reaches them):
            //   ⑴ LP feeds Fingering and StringNumber grobs into the same set
            //     (acknowledge_finger/_string_number) — not wired here;
            //   ⑵ LP skips a script that rides a slur (:696-697) — Lily# scripts
            //     never link to slurs (avoid-slur unported), so the skip has
            //     nothing to act on;
            //   ⑶ pairing is (staff, measure, item range) — LP pairs by Voice
            //     context, so a polyphonic staff whose OTHER voice scripts the
            //     same item indices would over-collect.
            // The CALLERS owe this loop Script-family layouts only: breath /
            // caesura / bend marks share the ArticulationLayout stream but are
            // not Scripts in LP (no tuplet acknowledger) — both wires sieve
            // through IsSidePositionedScript before passing `scripts`.
            if (!scripts.IsDefaultOrEmpty)
            {
                foreach (var a in scripts)
                {
                    if (a.OutsideStaffPriority != null)     // LP :690-692
                        continue;
                    if (a.StaffIndex != tuplet.StaffIndex
                        || a.MeasureIndex != tuplet.MeasureIndex
                        || a.ItemIndex < tuplet.StartNoteIndex
                        || a.ItemIndex > tuplet.EndNoteIndex)
                        continue;
                    Clear(a.X + a.Ink.CenterX - x0,
                        a.YUp + (dir > 0 ? a.Ink.Top : a.Ink.Bottom));
                }
            }
            offsetUp += BracketPadding * dir;
            // Nested brackets keep the Lily#-own stacking step (LP stacks via the
            // inner tuplets' boxes, :646-680 — not ported; no pinned point).
            offsetUp += nestingOffset * dir;

            // A flat bracket quantizes onto a line/space and steps OFF a line
            // when it lands inside the widened staff (:726-746).
            if (Math.Abs(dy) < 0.01)
            {
                double posQ = offsetUp / 0.5;
                if (posQ >= -5.0 && posQ <= 5.0)
                {
                    posQ = Math.Round(posQ, MidpointRounding.ToEven);
                    if ((int)posQ % 2 == 0 && Math.Abs((int)posQ) <= 4)
                        posQ += dir;
                    offsetUp = posQ * 0.5;
                }
            }

            // Back to the device staff-top frame (middle line = 2.0).
            return (2.0 - offsetUp, 2.0 - (offsetUp + dy));
        }

        // LILYPOND-REF: lily/tuplet-bracket.cc:566-629 slope calculation
        // Convert staff position difference to slope (half staff spaces)
        double positionDiff = (lastPos.Value - firstPos.Value) * 0.5;

        // Limit the endpoint height difference to max-slope-factor × bracket width
        // (width-proportional, not an absolute cap).
        // LILYPOND-REF: lily/tuplet-bracket.cc:570,620 — max_dy = max_slope_factor * last_x.
        double maxDy = MaxSlopeFactor * bracketWidth;
        double slope = positionDiff;
        if (Math.Abs(slope) > maxDy)
            slope = Math.Sign(slope) * maxDy;

        // The bracket follows the pitch contour on EITHER side: ascending
        // notes raise the right end. In the down-positive staff frame that
        // is the same sign for above and below brackets (the old
        // direction-dependent sign came from the fixed-base formulation and
        // inverted above brackets).
        double slopeDir = slope;

        double startY, endY;
        // NOTE-DRIVEN base: the bracket hugs the stems — its edge sits one
        // padding beyond the extreme stem tip in the bracket's direction,
        // wherever the notes are (a low triplet brings the bracket DOWN to
        // the staff; the old fixed outside-staff base left it floating).
        // LILYPOND-REF: lily/tuplet-bracket.cc calc_position_and_height —
        //   positions derive from the extremal stem/head edges + padding.
        if (isStemUp)
        {
            // Encompass points: every column's stem-side extent PLUS the
            // widened staff edge — then one padding to the bracket line.
            // LILYPOND-REF: lily/tuplet-bracket.cc:444-719
            // calc_position_and_height — points from
            // Note_column::cross_staff_extent[dir] and staff.widen(pad);
            // *offset += padding * dir.
            double tipY = extremeTip!.Value;
            double edge = Math.Min(tipY, -StaffPaddingLP)
                - BracketPadding - nestingOffset;
            double mid = edge;
            startY = mid + slopeDir * 0.5;
            endY = mid - slopeDir * 0.5;
            // The slope must not dip the bracket below the extreme stem tip.
            if (startY > edge) { endY -= startY - edge; startY = edge; }
            if (endY > edge) { startY -= endY - edge; endY = edge; }
        }
        else
        {
            double tipY = extremeTip!.Value;
            double edge = Math.Max(tipY, 4.0 + StaffPaddingLP)
                + BracketPadding + nestingOffset;
            double mid = edge;
            startY = mid + slopeDir * 0.5;
            endY = mid - slopeDir * 0.5;
            if (startY < edge) { endY += edge - startY; startY = edge; }
            if (endY < edge) { startY += edge - endY; endY = edge; }
        }

        return (startY, endY);
    }

    /// <summary>
    /// The NOTE/CHORD items a tuplet spans, read from its OWN staff's measures — so a
    /// tab tuplet gets the assigned string numbers (the covering beam may be the
    /// companion notation beam, whose members carry no string).
    /// </summary>
    /// <summary>
    /// The item at ONE index of the tuplet's own measure, or null when the index is out of
    /// range — the bound whose STEM a bracket hook stands on, so each end can read its own
    /// head shape rather than sharing one offset with the other end.
    /// </summary>
    private static MusicItem? TupletItemAt(
        TupletBracketItem tuplet, ImmutableArray<Measure> measures, int itemIndex)
    {
        if (measures.IsDefaultOrEmpty || tuplet.MeasureIndex >= measures.Length)
            return null;
        var items = measures[tuplet.MeasureIndex].Items;
        return itemIndex >= 0 && itemIndex < items.Length ? items[itemIndex] : null;
    }

    private static System.Collections.Generic.IEnumerable<MusicItem> TupletNoteItems(
        TupletBracketItem tuplet, ImmutableArray<Measure> measures)
    {
        if (measures.IsDefaultOrEmpty || tuplet.MeasureIndex >= measures.Length)
            yield break;
        var items = measures[tuplet.MeasureIndex].Items;
        for (int i = tuplet.StartNoteIndex; i <= tuplet.EndNoteIndex && i < items.Length; i++)
            if (items[i] is NoteItem or ChordItem)
                yield return items[i];
    }

    /// <summary>
    /// Gets the edge height for tuplet bracket hooks.
    /// </summary>
    public static double GetEdgeHeight() => EdgeHeight;
}
