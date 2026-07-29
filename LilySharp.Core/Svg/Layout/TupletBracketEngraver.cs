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
    /// </remarks>
    public double NumberX => (StartX + EndX) / 2.0;

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
        Dictionary<int, Staff>? staffByIndex = null)
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
            bool staffMultiVoice = !tupVoices.IsDefaultOrEmpty ? tupVoices.Length > 1 : forceStemUp;
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
            double stemAttach = isStemUp
                ? EngravingDefaults.StemUpAttachX
                : EngravingDefaults.StemDownAttachX;
            double halfStem = EngravingDefaults.StemThickness / 2;
            double startX = measureLayout.X + startOffset + stemAttach - halfStem;
            double endX = measureLayout.X + endOffset + stemAttach + halfStem;

            // LILYPOND-REF: scm/define-grobs.scm bracket-visibility = if-no-beam
            bool showBracket = !AreAllNotesBeamed(tuplet, beamGroups, tupMeasures);

            // Tab staves keep the raw-reach fallback: their staff positions are
            // string slots, not pitches, and no ledger point measures the tab
            // bracket regime (same gate session 30 left on the seed).
            bool isTabStaff = staffByIndex != null
                && staffByIndex.TryGetValue(tuplet.StaffIndex, out var encStaff)
                && encStaff.IsTab;

            // LILYPOND-REF: lily/tuplet-bracket.cc:566-629 slope calculation
            // Calculate slope based on first/last note staff positions
            var (startY, endY) = CalculateSlope(tuplet, tupMeasures, isStemUp, endX - startX,
                isTabStaff ? default : beamLayouts, measureLayout, useRealExtents: !isTabStaff);

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
                    // the head for up-stems (same correction DrawBeams makes).
                    double stemOffset = isStemUp
                        ? EngravingDefaults.StemUpAttachX
                        : EngravingDefaults.StemDownAttachX;
                    startX = beam.LeftX + stemOffset;
                    endX = beam.RightX + stemOffset;
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
                        double tabStemOffset = isStemUp
                            ? EngravingDefaults.StemUpAttachX
                            : EngravingDefaults.StemDownAttachX;
                        startX = beam.LeftX + tabStemOffset;
                        endX = beam.RightX + tabStemOffset;
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
                        // The INVISIBLE bracket's position: the beam's OUTER edge (where
                        // the stems end — a multi-line beam pushes it out by its full
                        // thickness) plus the bracket's own padding, and the number's
                        // CENTRE rides its midpoint exactly as it rides a drawn bracket.
                        // Measured six-digit in two musics on 2.26.0 (audit/lp-geometry
                        // staff.staff.beamed-tuplet-number): centre = beam edge + 1.100 —
                        // NOT stem tip + TupletNumber padding 0.5, and NOT the old
                        // 0.5 + digitHeight − 0.8 here, which compensated a renderer text
                        // offset DrawTupletBrackets no longer applies (it draws
                        // VerticalAnchor.Middle at NumberYUp since 99ecd3aa).
                        // LILYPOND-REF: lily/tuplet-number.cc:342 calc_y_offset — the
                        //   bracket midpoint, for every tuplet that is not a knee.
                        // LILYPOND-REF: scm/define-grobs.scm TupletBracket (padding . 1.1).
                        double offset = isStemUp ? -BracketPadding : BracketPadding;
                        // OuterEdgeStaffSpaceAtX is Y-up staff-space from the middle
                        // line (frame B); reflect it to the bracket's device top frame
                        // (device = middle 2.0 − Y-up).
                        startY = (2.0 - beam.OuterEdgeStaffSpaceAtX(beam.LeftX, isStemUp)) + offset;
                        endY = (2.0 - beam.OuterEdgeStaffSpaceAtX(beam.RightX, isStemUp)) + offset;
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
    /// Counts stem directions and returns majority direction.
    /// If equal, returns UP (bracket above).
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

        // LILYPOND-REF: lily/tuplet-bracket.cc:793-816
        // Return majority direction, or UP if equal
        return stemsUp >= stemsDown;
    }

    /// <summary>
    /// Checks whether all notes in the tuplet are covered by a beam group.
    /// If so, the bracket can be hidden (bracket-visibility = if-no-beam).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm TupletBracket.bracket-visibility = if-no-beam
    /// LILYPOND-REF: lily/tuplet-bracket.cc:98-146 bracket visibility check
    /// </remarks>
    private static bool AreAllNotesBeamed(TupletBracketItem tuplet,
        ImmutableArray<BeamGroup> beamGroups, ImmutableArray<Measure> tupMeasures)
    {
        if (beamGroups.IsDefaultOrEmpty)
            return false;

        // Find the tuplet's OWN beam: one group covering every note of the range.
        foreach (var beam in beamGroups)
        {
            if (Covers(beam, tuplet, tupMeasures))
                return true;
        }

        return false;
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

    // LILYSHARP-OWN: the SLOPE machinery below is simpler than LilyPond's. LilyPond's
    // general branch slopes from the outer columns' GRAPHICAL extents (rv[dir]-lv[dir],
    // zeroed when its sign disagrees with the musical head contour,
    // lily/tuplet-bracket.cc:530-549), damps against the covering beam's own slope
    // (:566-630 max_slope from quantized-positions), and QUANTIZES a near-flat bracket
    // onto staff positions when it lies within the widened staff (:726-746). Lily#
    // slopes from the outer MUSICAL positions with the max-slope-factor cap only. The
    // ledger pair staff.staff.tuplet-bracket-* pins the ENCOMPASS (flat, outside the
    // staff — none of the three differences fire there); a sloped or staff-adjacent
    // bracket regime has no point yet, so the slope port waits for its own pair.
    private static (double startY, double endY) CalculateSlope(
        TupletBracketItem tuplet, ImmutableArray<Measure> measures, bool isStemUp, double bracketWidth,
        ImmutableArray<BeamLayout> beamLayouts = default, MeasureLayout? measureLayout = null,
        bool useRealExtents = false)
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
                var member = itemUp == isStemUp ? MemberBeam(i) : null;
                double stemX = member is { } mb && !mb.beam.MemberXPositions.IsDefaultOrEmpty
                    ? mb.beam.MemberXPositions[mb.memberIndex]
                    : ml.X
                      + LayoutUtilities.GetItemXOffset(measures, tuplet.MeasureIndex, i, ml)
                      + (itemUp ? EngravingDefaults.StemUpAttachX : EngravingDefaults.StemDownAttachX);
                // The single house of a column's reach (HANDOFF §5.2.1②). Of() cannot
                // return null here — the `pos` gate above keeps only notes/chords.
                tip = NoteColumnLayout.Of(item, null, member?.beam, stemX) is { } col
                    ? col.OutwardTipDeviceY(isStemUp)
                    : RawOutwardTip(pos.Value, duration, isStemUp);
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
            return (baseY, baseY);

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
