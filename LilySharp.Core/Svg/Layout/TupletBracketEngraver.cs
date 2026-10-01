// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Parts of this file are ported from LilyPond, the GNU music typesetter.
// The C# is a modified translation of the following, not a copy of it:
//   lily/tuplet-bracket.cc
//     Copyright (C) 1997--2026 Jan Nieuwenhuizen <janneke@gnu.org>;
//     Han-Wen Nienhuys <hanwen@xs4all.nl>
//   scm/define-grobs.scm
//     Copyright (C) 1998--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>;
//     Jan Nieuwenhuizen <janneke@gnu.org>
//   lily/tuplet-number.cc
//     Copyright (C) 2005--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
// LilyPond is free software under the GNU General Public License version 3 or
// later; its notices are kept here as that licence requires. The full list is in
// LILYPOND-ATTRIBUTION.md. Lily# is an independent project, not affiliated with
// or endorsed by the LilyPond project.
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
    int StaffIndex = -1,        // owning staff (ossia shrink); -1 = unknown/test construction
    // The staff's line spacing (MultiStaffLayouter.LineSpacingOf) — LilyPond's `ss`, by which
    // print scales shorten-pair and edge-height: 1.5 on a tab, so its hooks reach 0.3 out
    // and stand 1.05 tall. LILYPOND-REF: lily/tuplet-bracket.cc:343-347 Tuplet_bracket::print
    // scale_drul (&shorten, ss); :360-366 scale_drul (&height, -ss * dir).
    double LineSpacing = 1.0
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
    public double DrawnStartX => StartX - AlongBracket(EndX - StartX);

    /// <inheritdoc cref="DrawnStartX"/>
    public double DrawnEndX => EndX + AlongBracket(EndX - StartX);

    /// <summary>The Y-up of <see cref="DrawnStartX"/>'s end: the same move along the
    /// bracket's slope, so a sloped bracket's drawn end stays ON its line.</summary>
    /// <inheritdoc cref="DrawnStartX"/>
    public double DrawnStartYUp => StartYUp - AlongBracket(EndYUp - StartYUp);

    /// <inheritdoc cref="DrawnStartYUp"/>
    public double DrawnEndYUp => EndYUp + AlongBracket(EndYUp - StartYUp);

    /// <summary>One component of the reach past a bound, taken ALONG the bracket:
    /// <c>shorten / length * dz</c>, the component of <c>dz</c> being <paramref name="d"/>.
    /// Until session 694 the whole reach went into X and none into Y, so a sloped bracket's
    /// ends ran out level instead of along its line.</summary>
    private double AlongBracket(double d)
    {
        double length = Math.Sqrt((EndX - StartX) * (EndX - StartX)
                                  + (EndYUp - StartYUp) * (EndYUp - StartYUp));
        return length > 0 ? BracketOutwardReach * LineSpacing / length * d : 0.0;
    }

    /// <summary>
    /// How far each end reaches PAST its logical bound, in staff spaces.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS THE NEGATION OF LilyPond's property, and is named for what it does rather
    /// than for the property, on purpose. <c>shorten-pair</c> is <c>(-0.2 . -0.2)</c>; a
    /// constant called BracketShortenPair holding +0.2 would read as the property value
    /// with the sign silently flipped, and a label that lies preserves the value it lies
    /// about. The sign is spent in lily/bracket.cc:54-55 (see <see cref="DrawnStartX"/>).
    /// </remarks>
    internal const double BracketOutwardReach = 0.2;

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
    /// how <c>22673bbb</c> silently zeroed every change-clef width.
    /// </para>
    /// </remarks>
    internal static double NumberFontSize => 11.0 * Math.Pow(2.0, -2.0 / 6.0) / 5.0;

    /// <summary>The number's face: italic, and NOT bold.</summary>
    /// <remarks>LILYPOND-REF: <c>scm/define-grobs.scm</c> TupletNumber
    /// <c>(font-shape . italic)</c>, with no weight override.</remarks>
    internal const Rendering.FontStyle NumberFontStyle = Rendering.FontStyle.Italic;

    /// <summary>The number's em for THIS score: <see cref="NumberFontSize"/> unless the
    /// score's <c>fonts { }</c> wrote a <c>step</c> or <c>size</c> for <c>tuplet</c>. The draw,
    /// the staff skyline, the outside-staff pass and the slur scorer all read it.</summary>
    internal static double NumberEm(Rendering.ScoreTextMetrics fonts)
        => fonts.Size(Rendering.TextRole.Tuplet, NumberFontSize);

    /// <summary>The number's weight and slant: <see cref="NumberFontStyle"/> unless the score
    /// wrote a style for <c>tuplet</c>.</summary>
    internal static Rendering.FontStyle NumberStyle(Rendering.ScoreTextMetrics fonts)
        => fonts.Style(Rendering.TextRole.Tuplet, NumberFontStyle);
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
    /// <param name="fonts">The score's text metrics — a tab bracket clears the tab beam,
    /// whose geometry stands on fret digits of the plan's em
    /// (<see cref="TabStaffGeometry"/>).</param>
    public static ImmutableArray<TupletBracketLayout> Calculate(
        Rendering.ScoreTextMetrics fonts,
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
        ImmutableArray<ArticulationLayout> scripts = default,
        Func<int, IReadOnlyDictionary<RestShiftKey, double>?>? restShiftsOf = null)
    {
        if (tuplets.IsDefaultOrEmpty)
            return ImmutableArray<TupletBracketLayout>.Empty;

        var layouts = ImmutableArray.CreateBuilder<TupletBracketLayout>(tuplets.Length);

        // INNER TUPLETS FIRST: a tab bracket clears the brackets it encloses (LilyPond's
        // `tuplets' points, TabBracketPositions), so a nested one is computed before the one
        // around it — deepest NestingDepth first — and the results are handed out in the
        // input order all the same. A book with no nesting walks 0..n-1 as before.
        var results = new TupletBracketLayout?[tuplets.Length];
        bool anyNested = false;
        foreach (var t in tuplets)
            anyNested |= t.NestingDepth > 0;
        int[] order = new int[tuplets.Length];
        for (int i = 0; i < order.Length; i++)
            order[i] = i;
        if (anyNested)
            order = order.OrderByDescending(i => tuplets[i].NestingDepth).ToArray();   // stable

        foreach (int ti in order)
        {
            var tuplet = tuplets[ti];
            // Find measure layout — BY MEASURE INDEX: the array is keyed by it (its callers
            // hand over the score's measures in index order), and a caller scoped to one
            // system leaves the other systems' slots empty
            // (MultiStaffLayouter.StaffTupletBracketLayouts). A tuplet whose measure is not
            // here is not on this system's page and is dropped, as the slur engraver drops
            // a mark whose measure is not in the layouts it was handed.
            if (tuplet.MeasureIndex >= measureLayouts.Length
                || measureLayouts[tuplet.MeasureIndex] is null)
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
                ? VoiceDefaults.CoversItem(tupVoices, tuplet.VoiceIndex, tuplet.MeasureIndex,
                    tuplet.StartNoteIndex)
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

            // The bracket's bound items are the OUTER STEMS when the bound column has a
            // visible stem pointing the bracket's way, and the COLUMNS themselves otherwise
            // (a rest, a stemless whole, a stem pointing against the bracket) — so the end
            // hooks align with the stem edge on a note and with the ink edge on a rest.
            // Each bound reads its OWN item: a tuplet whose ends are a half note and a
            // quarter has two different offsets; one whose end is a rest has that rest's
            // own ink edge (BoundEdgeOffset carries the citations).
            // A TAB staff's bounds are its own (user report 2026-09-29, bohemian-rhapsody.lys
            // score "tab2" bar 25): a tab stem stands at the fret digit's CENTRE — a
            // TabHeadCenterOffset right of the column, where a notation head's LEFT edge
            // stands — and points the way its STRING says (TabStaffGeometry.TabStemUp), not
            // the notated pitch. Read with the notation offsets, the hook stood 0.5–0.7 off
            // the digit at either end, and the bracket could take the wrong side.
            // MEASURED (2.26.0, Lab sessions/p690/probes/tabtuplet*.ly, \tabFullNotation): the
            // stem rect is centred on the digit (26.2243, the whiteout box's centre), the
            // bracket's X-positions start at the stem's edge (26.1593 = centre − 0.065), and
            // the hook is drawn 0.3 further out — print scales shorten-pair by the TabStaff's
            // staff-space 1.5 (TupletBracketLayout.LineSpacing), as it scales edge-height.
            // LILYPOND-REF: lily/tuplet-bracket.cc:72-85 get_x_bound_item — the stem when the
            //   column's direction is the bracket's and the stem has a stencil
            //   (\tabFullNotation); :180-189 the bound's own extent edge; :343-347 and
            //   :360-366 print — shorten-pair and edge-height scaled by ss.
            // LILYPOND-REF: lily/tab-note-heads-engraver.cc:99-122 Tab_note_heads_engraver::process_music
            //   — a TabNoteHead's staff position is its string's, which is what its stem's
            //   direction reads.
            Staff? tabStaff = staffByIndex != null
                && staffByIndex.TryGetValue(tuplet.StaffIndex, out var ts)
                && ts.IsTab && ts.Tuning.HasValue
                ? ts : null;
            TabStaffGeometry tabGeom = default;
            if (tabStaff != null)
            {
                tabGeom = new TabStaffGeometry(fonts, tabStaff.Tuning!.Value, staffOffset,
                    tabStaff.TabSourceClef, tabStaff.Transposition);
                if (!staffMultiVoice)
                    isStemUp = TabDirection(tuplet, tupMeasures, tabGeom, beamLayouts);
            }

            var startItem = TupletItemAt(tuplet, tupMeasures, tuplet.StartNoteIndex);
            var endItem = TupletItemAt(tuplet, tupMeasures, tuplet.EndNoteIndex);
            double startX = measureLayout.X + startOffset + (tabStaff != null
                ? TabBoundEdgeOffset(startItem, isStemUp, left: true, tabStaff, tabGeom, fonts,
                    startItem is null || TabColumnStemUp(tuplet, startItem, tuplet.StartNoteIndex, tabGeom, beamLayouts))
                : BoundEdgeOffset(startItem, isStemUp, left: true));
            double endX = measureLayout.X + endOffset + (tabStaff != null
                ? TabBoundEdgeOffset(endItem, isStemUp, left: false, tabStaff, tabGeom, fonts,
                    endItem is null || TabColumnStemUp(tuplet, endItem, tuplet.EndNoteIndex, tabGeom, beamLayouts))
                : BoundEdgeOffset(endItem, isStemUp, left: false));

            // LILYPOND-REF: lily/tuplet-bracket.cc:100-115 bracket_basic_visibility —
            //   the bracket is hidden ONLY when the tuplet's own beam is equally long.
            // ⚠️ THE TUPLET'S OWN STAFF'S BEAMS, read from the laid-out beams when the caller
            // has them (FindCoveringBeam's filter, below): the annotation pass hands
            // `beamGroups` as the PRIMARY staff's detection, so until session 733 every tuplet
            // on a lower staff looked for its beam among the top staff's — found none, and drew
            // the bracket LilyPond hides (an SATB + piano book's right-hand 16th triplet, Lab
            // sessions/p733/tup b4: one soprano staff above the piano was enough).
            bool showBracket = !(beamLayouts.IsDefaultOrEmpty
                ? HasEquallyLongBeam(tuplet, beamGroups, tupMeasures)
                : HasEquallyLongStaffBeam(tuplet, beamLayouts, tupMeasures));

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
                bracketStartX: startX, scripts: scripts,
                restShifts: restShiftsOf?.Invoke(tuplet.StaffIndex),
                staffLines: staffByIndex != null
                    && staffByIndex.TryGetValue(tuplet.StaffIndex, out var linesStaff)
                    ? linesStaff.Lines : 5);

            // When the bracket is suppressed (fully beamed), the NUMBER
            // attaches to the BEAM: centered between the outer stems, sitting
            // just off the beam line on its stem side — not at the bracket's
            // notehead-based position (which reads as shifted up-left).
            // A DRAWN bracket on a TAB staff clears the TAB's own columns — its stems end
            // where TabStaffGeometry says (2.25 below the bottom string for a down-stem
            // eighth), not where the notation frame CalculateSlope reads puts them, so the
            // bracket ran through the stems (user report 2026-09-29, bohemian-rhapsody.lys
            // score "tab", bar 42). LilyPond's offset pass, over the tab columns' reach.
            // ⚠️ STAFF-RELATIVE, LIKE CalculateSlope: the geometry here stands at Y 0 (the
            // staff's top line) and the staff offset is added below with the notation
            // path's, not baked as the beam-attached number's branch bakes it — MEASURED on
            // the user's book: the offset that branch reads is not the one the renderer
            // places the staff at in a later system (bar 42 came out 1.04 high while the
            // engine's own layout of the same score had it right).
            // ★ PRINTED OR NOT: a tab tuplet whose own beam hides its bracket still has the
            // bracket's positions — the follow_beam arm, one padding off the beam — and its
            // number centres on them (lily/tuplet-number.cc:342 calc_y_offset), as the notation
            // path already reads it. The tab-only beam-number formula below (a 1.7 digit height,
            // a 0.5 clearance) put the number ON the beam, and a bracket enclosing it cleared
            // that low number: MEASURED (2.26.0, Lab sessions/p690/probes/tt-nest.ly) the
            // outer bracket stood 0.31 lower than LilyPond's.
            if (tabStaff != null)
            {
                var staffLocal = new TabStaffGeometry(fonts, tabStaff.Tuning!.Value, 0.0,
                    tabStaff.TabSourceClef, tabStaff.Transposition);
                // The brackets this one encloses, already laid out (inner first, above).
                List<TupletBracketLayout>? children = null;
                for (int j = 0; j < tuplets.Length; j++)
                {
                    var inner = tuplets[j];
                    if (results[j] is { } innerLayout
                        && inner.NestingDepth == tuplet.NestingDepth + 1
                        && inner.StaffIndex == tuplet.StaffIndex
                        && inner.VoiceIndex == tuplet.VoiceIndex
                        && inner.MeasureIndex == tuplet.MeasureIndex
                        && inner.StartNoteIndex >= tuplet.StartNoteIndex
                        && inner.EndNoteIndex <= tuplet.EndNoteIndex)
                        (children ??= new()).Add(innerLayout);
                }
                (startY, endY) = TabBracketPositions(tuplet, tupMeasures, measureLayout, isStemUp,
                    startX, endX, staffLocal, beamLayouts, fonts, scripts, children, staffOffset);
            }

            // LILYPOND-REF: lily/tuplet-number.cc — number follows the beam
            // when there is no bracket.
            if (!showBracket && !beamLayouts.IsDefaultOrEmpty && tabStaff == null)
            {
                var beam = FindCoveringBeam(beamLayouts, tuplet, tupMeasures);
                if (beam != null)
                {
                    isStemUp = beam.Group.StemUp;
                    // The invisible bracket's X span stays the tuplet's OWN bounds computed
                    // above (BoundEdgeOffset = LilyPond's X-positions: a stem's edge, a
                    // bounding rest's ink edge), which is what the number centres on.
                    // LILYPOND-REF: lily/tuplet-number.cc:294-299 calc_x_offset — the number
                    //   centres on the bracket's own X-positions, printed or not.
                    // Until 2026-09-07 this read the BEAM's outer member stems and then
                    // re-read the tuplet's bound MEMBERS' stems — right for a note-bound
                    // tuplet, and off by half the rest's reach for one bounded by a
                    // bracketed rest (`tuplet 3/2 { r8[ c c] }'), whose bound is no member.
                    // (A TAB tuplet never reaches here: its positions, printed or not, come
                    // from TabBracketPositions above — the tab-only beam-number formula that
                    // stood here until 2026-09-29 put the number on the beam.)
                    {
                        // The INVISIBLE bracket spans the TUPLET'S OWN bounds (startX/endX
                        // above), not the covering beam's ends: one auto-beam can cover
                        // several tuplets (two 16th triplets inside one beat —
                        // tuplet-number-alignment.ly), and reading the beam's span
                        // stacked every number onto the same beam midpoint. The bound
                        // stems' EDGES (∓ halfStem) centre where their centres did, so the
                        // note-bound number (tupnumss twin, stem midpoint 26.73 = LP number
                        // centre 26.69) is unchanged by reading the bounds instead of the
                        // members.
                        // LILYPOND-REF: lily/tuplet-bracket.cc:495-519
                        //   calc_position_and_height follow-beam — points from
                        //   columns[0] / columns.back(), the tuplet's own stems.
                        // ⚠️ THE Y IS NOT RECOMPUTED HERE — nor, since 2026-09-07, the X.
                        // LILYPOND-REF: lily/tuplet-number.cc:342 calc_y_offset — the
                        //   TupletNumber reads the BRACKET's midpoint whether or not the
                        //   bracket is printed, for every tuplet that is not a knee.
                        // LILYPOND-REF: lily/tuplet-bracket.cc:491-519 calc_position_and_height
                        //   follow_beam — the branch CalculateSlope now runs in exactly this
                        //   case, so the bracket's own position IS the beam-relative one.
                        // Keeping a second formula here made the two disagree by the
                        // bracket thickness.
                        // MEASURED on the LP twins audit/lpreg/tupnum{a,b}-lp.svg, which
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
            // sits over its OWN staff, not the first — the tab path's too (it is
            // computed staff-local).
            startY += staffOffset;
            endY += staffOffset;
            // Store Y-up from the system top; the placement above stays in the
            // device staff-top frame (system.Y is added at draw), so negate here.
            results[ti] = new TupletBracketLayout(
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
                StaffIndex: tuplet.StaffIndex,
                LineSpacing: tabStaff != null ? MultiStaffLayouter.LineSpacingOf(tabStaff) : 1.0
            );
        }

        foreach (var r in results)
            if (r is { } layout)
                layouts.Add(layout);
        return layouts.ToImmutable();
    }

    /// <summary>
    /// Overload with measures but no beam info.
    /// </summary>
    public static ImmutableArray<TupletBracketLayout> Calculate(
        Rendering.ScoreTextMetrics fonts,
        ImmutableArray<TupletBracketItem> tuplets,
        ImmutableArray<MeasureLayout> measureLayouts,
        ImmutableArray<Measure> measures)
    {
        return Calculate(fonts, tuplets, measureLayouts, measures, default);
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
    /// <see cref="HasEquallyLongBeam"/> over the laid-out beams of the tuplet's OWN staff —
    /// LilyPond's par_beam is the beam on the tuplet's own columns' stems, so it cannot be
    /// another staff's (the filter <see cref="FindCoveringBeam"/> applies).
    /// </summary>
    private static bool HasEquallyLongStaffBeam(TupletBracketItem tuplet,
        ImmutableArray<BeamLayout> beamLayouts, ImmutableArray<Measure> tupMeasures)
    {
        foreach (var beam in beamLayouts)
        {
            if (beam.StaffIndex != tuplet.StaffIndex || !Covers(beam.Group, tuplet, tupMeasures))
                continue;
            if (HasSameBounds(beam.Group, tuplet))
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
        // The beam's bound COLUMNS: a rest the writer bracketed at either end is the beam's
        // bound there, not the first or last note member — LilyPond's beam spanner is bound
        // to that rest's column, so `tuplet 3/2 { r8[ c c] }' has equal bounds and no bracket
        // (MEASURED 2026-09-07, scratch/p346/hid-probe.ly: TupletBracket extent empty, the
        // number centred on X-positions (0 . 6.0084) from the rest's column). Until then the
        // bounds were read from Members alone and the bracket was drawn over the beam.
        var first = beam.Members[0];
        var last = beam.Members[^1];
        int leftMeasure = first.ResolveMeasureIndex(beam.MeasureIndex), leftItem = first.ItemIndex;
        int rightMeasure = last.ResolveMeasureIndex(beam.MeasureIndex), rightItem = last.ItemIndex;
        foreach (var r in beam.RestStems)
        {
            if (!r.BracketBound) continue;
            int rm = r.MeasureIndex < 0 ? beam.MeasureIndex : r.MeasureIndex;
            if (r.BeforeMember == 0)
                (leftMeasure, leftItem) = (rm, r.ItemIndex);
            else if (r.BeforeMember == beam.Members.Length)
                (rightMeasure, rightItem) = (rm, r.ItemIndex);
        }
        return leftMeasure == tuplet.MeasureIndex
               && rightMeasure == tuplet.MeasureIndex
               && leftItem == tuplet.StartNoteIndex
               && rightItem == tuplet.EndNoteIndex;
    }

    /// <summary>
    /// The invisible stem of a REST at the tuplet's first (or last) column, when this beam
    /// carries it: its x — the rest's own ink centre, which is where LilyPond stands the stem
    /// it gives a beamed rest — and the beam face that stem ends on. Null when that column is
    /// a note (the caller already has its tip) or when the beam does not carry it.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tuplet-bracket.cc:498-510 calc_position_and_height — the follow-beam
    ///   arm reads <c>Note_column::get_stem(columns[0])</c> and <c>columns.back()</c>, and
    ///   takes each stem's own extent on its own side. A rest column's stem is invisible but
    ///   present, and its extent is the single point where the beam is.
    /// LILYPOND-REF: lily/stem.cc:1093-1105 Stem::offset_callback — the "rests" branch puts
    ///   that stem on the rest's extent centre.
    /// </remarks>
    private static (double X, double TipUp)? OuterColumnRestStem(
        BeamLayout beam, TupletBracketItem tuplet, Measure measure, bool isStemUp, bool first)
    {
        int step = first ? 1 : -1;
        int from = first ? tuplet.StartNoteIndex : tuplet.EndNoteIndex;
        int to = first ? tuplet.EndNoteIndex : tuplet.StartNoteIndex;
        for (int i = from; first ? i <= to : i >= to; i += step)
        {
            if (i < 0 || i >= measure.Items.Length) continue;
            var item = measure.Items[i];
            // The first thing that forms a COLUMN, in the direction asked. A note ends the
            // walk with no answer: the caller already holds that column's tip.
            if (item is NoteItem or ChordItem) return null;
            if (item is not RestItem { IsSpacer: false }) continue;
            var rests = beam.Group.RestStems;
            if (beam.RestXPositions.Length != rests.Length) return null;
            for (int r = 0; r < rests.Length; r++)
            {
                if (rests[r].ItemIndex != i) continue;
                int rm = rests[r].MeasureIndex < 0
                    ? beam.Group.MeasureIndex : rests[r].MeasureIndex;
                if (rm != tuplet.MeasureIndex) continue;
                double x = beam.RestXPositions[r];
                return (x, beam.OuterEdgeStaffSpaceAtX(x, isStemUp));
            }
            return null;
        }
        return null;
    }

    /// <summary>
    /// True when <paramref name="beam"/> is the tuplet's <c>par_beam</c>: its OUTER TWO note
    /// columns are both carried by this one beam. Same measure and same VOICE first — another
    /// voice's beam at the same item range is never this bracket's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LILYPOND-REF: scm/output-lib.scm:3945-3968 <c>ly:tuplet-bracket::calc-potential-beam</c>
    ///   — the object callback behind <c>TupletBracket.beam</c>
    ///   (scm/define-grobs.scm:4118-4120), which reads
    ///   <c>(ly:grob-object first-col 'stem)</c> and <c>(ly:grob-object last-col 'stem)</c>,
    ///   takes each stem's <c>'beam</c>, and answers that beam only when both exist and are
    ///   <c>eq?</c>; scm/output-lib.scm:3970-3977 <c>ly:tuplet-bracket::calc-beam</c> adds the
    ///   not-broken gate. lily/tuplet-bracket.cc:481 reads it as <c>par_beam</c>.
    /// </para>
    /// <para>
    /// ⚠️ ONLY THE OUTER COLUMNS ARE CONSULTED. This predicate used to ask instead whether the
    /// beam covered every NOTE slot of the range, treating rests as transparent — which is
    /// right for the MIDDLE of a tuplet and wrong at its ENDS. MEASURED 2026-09-07 on the
    /// reported book (scratch/ベースタブLy/rest-tuplet.lys bar 4,
    /// <c>\tuplet 3/4 { r16 c a, }</c>, LP dump in scratch/p345): LilyPond answers
    /// <c>beam=#f</c> there, so <c>follow_beam</c> is false, the staff edge joins the encompass
    /// points (lily/tuplet-bracket.cc:633-637) and the bracket comes out FLAT at
    /// <c>positions=(-3.4 . -3.4)</c>. Lily# found the beam over the two notes, followed it,
    /// and drew a SLOPED bracket at -1.383 .. -2.149 — through the rest, whose ink reaches
    /// -2.05. That collision is what the reader reported.
    /// </para>
    /// <para>
    /// ⚠️⚠️ AND THE REASON IS NOT "A REST COLUMN HAS NO STEM" — it was written that way here
    /// for one commit and it is false. MEASURED (scratch/p345/beamrest.ly, four books):
    /// EVERY LilyPond NoteColumn carries a stem, a rest's included, and a rest inside a beam
    /// IS one of that beam's <c>stems</c> (it prints as a stem with no note-heads); a manual
    /// beam may even BEGIN on a rest. What answers <c>#f</c> on the reported book is that the
    /// r16's stem carries NO beam — the beam there starts at the note after it. Where a beam
    /// does run over the bounding rest, LilyPond follows it:
    /// <c>c,16[ tuplet 3/4 { r16 c a, ] }</c> answers <c>beam=&lt;Beam&gt;</c> and
    /// <c>positions=(-4.315073 . -3.271576)</c>, sloped (scratch/p345/e1-probe.ly).
    /// So the question to ask a column is not "is it a note" but "is it carried by THIS beam",
    /// and Lily# spells that carriage in two lists rather than one: a stem is a
    /// <see cref="BeamGroup.Members"/> entry, a rest ridden over is a
    /// <see cref="BeamGroup.RestStems"/> entry ("an INVISIBLE stem: no member",
    /// Svg/Collector/BeamDetector.cs). Both are consulted below.
    /// ⚠️ The two lists are not an accident to be merged away: LilyPond keeps rests out of the
    /// stem SCORING with <c>Stem::is_normal_stem</c> (lily/beam-quanting.cc:299), which is the
    /// gate <see cref="BeamGroup.RestStems"/>' own remark cites. Moving rests into
    /// <c>Members</c> would hand them to the quanter, the drawing and the direction vote and
    /// need that gate written back at each of those; the split makes it structural.
    /// </para>
    /// <para>
    /// ⚠️ Disclosed, not ported: LilyPond also requires the last column's stem to share a
    /// paper column with the bracket's right bound — the guard its own comment calls
    /// "don't use a parallel beam if tupletFullNote = ##t" (scm/output-lib.scm:3960-3962).
    /// Lily# has no <c>tupletFullNote</c> / <c>span-all-note-heads</c> grammar, so the
    /// bracket's right bound is always its last column and the test is vacuously true.
    /// </para>
    /// </remarks>
    private static bool Covers(BeamGroup beam, TupletBracketItem tuplet,
        ImmutableArray<Measure> tupMeasures)
    {
        if (beam.MeasureIndex != tuplet.MeasureIndex || beam.VoiceIndex != tuplet.VoiceIndex)
            return false;

        var items = !tupMeasures.IsDefaultOrEmpty && tuplet.MeasureIndex < tupMeasures.Length
            ? tupMeasures[tuplet.MeasureIndex].Items
            : default;

        // The tuplet's first and last NOTE COLUMNS. A spacer occupies no column, so it is
        // not a bound; a rest occupies one and is, which is the whole point.
        int firstCol = -1, lastCol = -1;
        for (int i = tuplet.StartNoteIndex; i <= tuplet.EndNoteIndex; i++)
        {
            // Without item info every slot is treated as a column that carries a stem,
            // which keeps the pre-2026-09-07 answer for callers that pass no measures.
            bool isColumn = items.IsDefault || i >= items.Length
                || items[i] is NoteItem or ChordItem or RestItem { IsSpacer: false };
            if (!isColumn)
                continue;
            if (firstCol < 0) firstCol = i;
            lastCol = i;
        }
        if (firstCol < 0)
            return false;

        // Every column this beam carries, however it carries it: a visible stem is a member,
        // a rest ridden over is one of the invisible stems. Together they are LilyPond's
        // `the column has a stem, and that stem's beam is this one'.
        // ⚠️ ASKED OF TWO COLUMNS ONLY, so two flags and no set: until session 462 every
        // carried column went into a HashSet<int> that was then asked about the first and the
        // last — MEASURED (session 457's census, Release, the reader's corpus, eight forward
        // keystrokes a book) 10.3 sets a keystroke at 2.69 columns each, none alive past its
        // render, 1,797 B a keystroke. "Some carried column is firstCol" is exactly what the
        // set's Contains(firstCol) answered.
        bool firstCarried = false, lastCarried = false;
        foreach (var m in beam.Members)
            if (m.ResolveMeasureIndex(beam.MeasureIndex) == tuplet.MeasureIndex)
            {
                firstCarried |= m.ItemIndex == firstCol;
                lastCarried |= m.ItemIndex == lastCol;
            }
        foreach (var r in beam.RestStems)
            if ((r.MeasureIndex < 0 ? beam.MeasureIndex : r.MeasureIndex) == tuplet.MeasureIndex)
            {
                firstCarried |= r.ItemIndex == firstCol;
                lastCarried |= r.ItemIndex == lastCol;
            }

        // Both outer columns on THIS beam — the whole of
        // `(and left-stem right-stem left-beam right-beam (eq? left-beam right-beam))',
        // since a column can be on at most one beam.
        return firstCarried && lastCarried;
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
        // ⚠️ AND ONLY THAT STAFF'S. LilyPond's par_beam is the beam on the tuplet's OWN outer
        // columns' stems (scm/output-lib.scm:3955-3967), so it cannot be another staff's; the
        // old cross-staff fallback here made a GrandStaff's right-hand tuplet follow the LEFT
        // hand's beam. MEASURED 2026-09-07 on LilySharp.Tests/Fixtures/test/
        // multistaff-tuplet-beams.lys, whose tuplet is three unbeamed QUARTERS: LilyPond
        // answers no parallel beam and flattens the bracket on the staff, while this engraver
        // was following a beam belonging to the other staff of the same measure and voice.
        foreach (var beam in beamLayouts)
        {
            if (beam.StaffIndex != tuplet.StaffIndex)
                continue;
            if (Covers(beam.Group, tuplet, tupMeasures))
                return beam;
        }
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
    //   readable or the next hand reads this as an invention and rebuilds it.
    //   It is not LILYSHARP-OWN — that label is for a quantity LilyPond does not have,
    //   and LilyPond has this one.
    // Three differences, all in the direction of SIMPLER, none of them yet measured:
    //   ⑴ Lily# slopes from the outer MUSICAL positions (firstPos/lastPos, staff positions),
    //      LilyPond from the GRAPHICAL extents with the sign guard above;
    //   ⑵ LilyPond damps against the covering beam's own slope (:566-630, max_slope read
    //      off quantized-positions), Lily# applies the max-slope-factor cap only;
    //   ⑶ LilyPond QUANTIZES a near-flat bracket onto staff positions when it lies inside
    //      the widened staff (:726-746); Lily# does not quantize at all.
    // ⚠️ WHY IT IS SIMPLER: NOT a trade-off anyone made, and NOT performance. The body
    //   predates the porting discipline (it arrives whole in the bulk commit dc363123 of
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
        ImmutableArray<ArticulationLayout> scripts = default,
        IReadOnlyDictionary<RestShiftKey, double>? restShifts = null,
        int staffLines = 5)
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
            foreach (var b in beamLayouts)
            {
                // ⚠️ THIS STAFF'S BEAMS ONLY, as FindCoveringBeam does and for the same
                // reason: a stem's beam is a fact about its own staff (lily/stem.cc
                // Stem::get_beam), and the cross-staff fallback that used to stand here let a
                // GrandStaff's upper tuplet read its column tips off the LOWER staff's beam.
                if (b.StaffIndex != tuplet.StaffIndex)
                    continue;
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
                return (b, member);
            }
            return null;
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
        // The outer NOTE columns' drawn stem x — LilyPond's follow-beam arm asks the STEMS,
        // not the columns.
        double firstStemX = double.NaN, lastStemX = double.NaN;
        (BeamLayout beam, int memberIndex)? lpAnyBeam = null;
        // The extreme ENCOMPASS POINT in the staff-top device frame (down-positive),
        // not the extreme staff POSITION. Those are different aggregates the moment the
        // tuplet's members differ in duration: a stemless whole note reaches only its own
        // notehead ink while a half note reaches a full stem, so the note that is highest
        // on the staff need not be the one the bracket has to clear.
        // LILYPOND-REF: lily/tuplet-bracket.cc calc_position_and_height — the points are
        //   the note columns' own extents (Note_column::cross_staff_extent[dir]).
        double? extremeTip = null;

        // A rest column's reach on the bracket's side, Y-up in staff-middle spaces: the
        // glyph the renderer draws (GlyphMetrics.GetRestBBox — LilyPond's Rest extent is
        // its stencil's, the LILC box) at the origin it draws it at (the staff's neutral
        // letter, ElementCoordinator.NeutralRestPosition — the middle line, a semibreve
        // hanging from the line above — plus the shift the rest-collision pass gave it,
        // the SAME memo the renderer's GetRestShift and the skyline seed read), united
        // with the beam face where a beam carries the rest as an invisible stem.
        // ⚠️ With no memo handed in (the slur pass rebuilding tuplet numbers), the PURE
        // position stands: the written pitch of `a4@rest', the voiced base, or the neutral
        // letter — LilyPond's own pure-chain reading, the collision push left out.
        // LILYPOND-REF: lily/rest.cc:33-45 y_offset_callback, :47-145 staff_position_internal;
        //   lily/rest.cc:229-257 brew_internal_stencil — the extent is the glyph's;
        //   lily/note-column.cc:251-258 cross_staff_extent — `iv.unite (stem extent)'.
        double RestReachUp(RestItem rest, int itemIndex)
        {
            int restValue = GlyphMetrics.NoteValueOf(rest.BaseDuration);
            double neutral = ElementCoordinator.NeutralRestPosition(staffLines, restValue);
            double shift;   // staff positions from the glyph's default origin, up-positive
            if (restShifts is not null)
                shift = restShifts.TryGetValue(
                    new RestShiftKey(tuplet.MeasureIndex, tuplet.VoiceIndex, itemIndex), out var rs)
                    ? rs : 0.0;
            else
                // The pitched and the voiced arm both come through RestStaffPosition, as
                // the collision pass spells the memo (a written pitch carries the
                // semibreve's +2 whatever the staff; on five lines it cancels the letter's).
                shift = rest.StaffPosition is not null || rest.VoiceDirection != 0
                    ? ElementCoordinator.RestStaffPosition(rest, rest.VoiceDirection, restValue, staffLines)
                        - neutral
                    : 0.0;
            double originUp = neutral / 2.0 + shift * 0.5;
            var box = GlyphMetrics.GetRestBBox(restValue);
            double lo = originUp + box.Bottom, hi = originUp + box.Top;
            // The invisible stem of a rest a beam runs over ends on the beam's face at the
            // rest's own x (MEASURED, scratch/p345/beamslope.ly: its extent is that single
            // point) — one of the beam's own stems, on THIS staff and voice.
            // LILYPOND-REF: lily/beam-engraver.cc:211-220 acknowledge_rest; lily/stem.cc
            //   Stem::get_beam — the rest's stem carries the beam.
            if (!beamLayouts.IsDefaultOrEmpty)
            {
                foreach (var b in beamLayouts)
                {
                    if (b.StaffIndex != tuplet.StaffIndex
                        || b.Group.MeasureIndex != tuplet.MeasureIndex
                        || b.Group.VoiceIndex != tuplet.VoiceIndex)
                        continue;
                    var rests = b.Group.RestStems;
                    if (b.RestXPositions.Length != rests.Length)
                        continue;
                    for (int r = 0; r < rests.Length; r++)
                    {
                        if (rests[r].ItemIndex != itemIndex)
                            continue;
                        int rm = rests[r].MeasureIndex < 0 ? b.Group.MeasureIndex : rests[r].MeasureIndex;
                        if (rm != tuplet.MeasureIndex)
                            continue;
                        double face = b.OuterEdgeStaffSpaceAtX(b.RestXPositions[r], b.Group.StemUp);
                        lo = Math.Min(lo, face);
                        hi = Math.Max(hi, face);
                    }
                }
            }
            return isStemUp ? hi : lo;
        }

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

            if (pos == null)
            {
                // A REST is a column too, and LilyPond's general arm walks every column
                // raw: the rest's own ink on the bracket's side is an encompass point,
                // united with its (invisible) stem when a beam carries it. Only the SLOPE
                // bounds skip rests (get_bounds, :423-438) — so a rest joins lpPoints and
                // last_x here and stays out of firstPos/lastPos.
                // MEASURED (audit/lp-geometry/probes/tuplet-bracket-rest-point.ly): a
                // quarter rest written at c' under \tuplet 3/2 { c''4 c'4\rest c'' } drops
                // LilyPond's bracket from (-4.1 . -4.1) to (-5.35 . -5.35) = its ink bottom
                // -4.25 + padding, and does so at a bound (TQB) exactly as in the middle
                // (TQD); a rest raised to the far side (TQU) moves nothing. Before this
                // arm the ledger pair read -1.250000 on both deep books.
                // LILYPOND-REF: lily/tuplet-bracket.cc:554-562 calc_position_and_height —
                //   `for (i < columns.size ()) points.push_back (x, note_ext[dir])';
                //   lily/note-column.cc:251-258 Note_column::cross_staff_extent — the
                //   column's extent united with its stem's.
                if (item is RestItem { IsSpacer: false, IsMultiMeasure: false } rest
                    && useRealExtents && measureLayout is { } rml)
                {
                    double restColX = rml.X
                        + LayoutUtilities.GetItemXOffset(measures, tuplet.MeasureIndex, i, rml);
                    lpPoints.Add((restColX, RestReachUp(rest, i)));
                    lastColX = restColX;
                }
                continue;
            }

            // Head-position interval of the column (chord: min..max) — only the
            // SIGNS of last−first feed the LP gates.
            (int lo, int hi) posIv = item switch
            {
                NoteItem n2 => (n2.StaffPosition, n2.StaffPosition),
                ChordItem c2 => (c2.Notes.Min(n => n.StaffPosition),
                                 c2.Notes.Max(n => n.StaffPosition)),
                _ => (pos.Value, pos.Value),
            };
            // "First NON-REST column" — the slope bound — is asked of firstPos, not of
            // lpPoints being empty: a leading rest already pushed its point there.
            bool firstNote = firstPos == null;
            if (firstNote)
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
                // The DRAWN stem's x — the beam model's member stem for a beamed column
                // (BeamLayout.MemberStemX: the anchor plus this head's attach, the frame
                // the beam face is read in), the same attach on the column for an unbeamed
                // one. LilyPond's follow-beam arm takes its two points at the STEMS
                // (:514-518 `stems[side]->relative_coordinate'), where its general arm takes
                // them at the COLUMNS (:559 `columns[i]->relative_coordinate').
                // ⚠️ Until 2026-09-07 the beamed branch handed the column ANCHOR to the face
                // read, which was then in the anchor frame, so the tip came back right by
                // accident and a slope × attach "correction" was added to it below for the
                // follow arm — the seam ledger staff.staff.tuplet-bracket-follow-beam{,-rest}
                // measured at slope × half a stem. One frame now; no correction.
                double stemX = member is { } mb && !mb.beam.MemberXPositions.IsDefaultOrEmpty
                    ? mb.beam.MemberStemX(mb.memberIndex)
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
                if (firstNote)
                {
                    firstTipUp = tipUp;
                    firstStemX = stemX;
                }
                lastTipUp = tipUp;
                lastStemX = stemX;
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

        // An ALL-REST tuplet runs the SAME general arm below: get_bounds finds no bounds so
        // dy is 0 (:551-552), and the offset pass clears the rest columns' own ink (the loop
        // above pushed them) plus the staff edge at both bounds — for middle-line rests the
        // staff edge wins and the flat bracket sits at 2.3 + 1.1 = 3.4 (tuplet-rest.ly t4,
        // LP 3.400 measured). Until 2026-09-07 a SECOND spelling of that offset pass stood
        // here, staff edge only, under "a default mid-staff rest never beats the staff
        // edge" — true of `r4', false of `c4@rest' (the ledger books
        // staff.staff.tuplet-bracket-rest-point-* are where the ink was measured).
        // LILYPOND-REF: lily/tuplet-bracket.cc:551-552 calc_position_and_height —
        //   `else *dy = 0'; :633-637 the staff points join regardless of columns.
        bool allRest = firstPos == null || lastPos == null || extremeTip == null;
        if (allRest && !(useRealExtents && !double.IsNaN(bracketStartX) && bracketWidth > 0.001))
            return (baseY, baseY);

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
        if ((lpPoints.Count > 0 || allRest) && !double.IsNaN(bracketStartX) && bracketWidth > 0.001)
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
            // ⚠️ `firstPos != null': a beam needs a NOTE member in this model (BeamDetector
            // builds no group from rests alone), so a par_beam over an all-rest tuplet cannot
            // occur; the gate only keeps the follow arm from reading an unset stem x if that
            // ever changes. LilyPond would follow such a beam (every rest has a stem).
            bool followBeam = parBeam != null
                              && firstPos != null
                              && parBeam.Group.StemUp == isStemUp
                              && !parBeam.Group.IsKnee;
            // ⚠️ LILYPOND'S TWO BRANCHES ARE NOT ONE BRANCH WITH A FLAG. Everything below the
            // `else' here — uniting with the staff, the musical sign gates, the damping, and
            // the per-column points — lives in lily/tuplet-bracket.cc's ELSE arm (:520-631).
            // The follow-beam arm (:495-519) does one thing: it takes the OUTER TWO COLUMNS'
            // STEM TIPS, sets dy to their difference, and pushes exactly those two points.
            // Running the sign gates over it as well flattened brackets LilyPond slopes:
            // MEASURED on `c,16[ tuplet 3/4 { r16 c a, ] }' (scratch/p345/e1.lys), the beam
            // rises (+0.561446) while the heads descend, so `sign(graphicalDy) != musDown'
            // zeroed the dy and the bracket came out flat at -3.730 against LilyPond's
            // sloped positions=(-4.315073 . -3.271576).
            double staffEdge = 2.3 * dir;
            var followPoints = new List<(double X, double YUp)>();
            double dy;
            if (followBeam)
            {
                // LILYPOND-REF: lily/tuplet-bracket.cc:495-519 calc_position_and_height —
                //   the follow_beam arm. poss[side] is
                //   `stems[side]->extent(...)[stem dir] + parent_relative', i.e. the outer
                //   COLUMN's stem tip. A bracketed rest at a bound has one too: an INVISIBLE
                //   stem standing on the rest's ink centre and ending on the beam's face
                //   (MEASURED, scratch/p345/beamslope.ly: `r8[ c e g]' reports that stem's
                //   extent as the single point 0.253716, which is the beam's upper edge at
                //   the rest's x). The follow arm only ever runs when both outer stems carry
                //   this one beam, so a bounding rest here is always a beamed one.
                // The two points stand at the outer note columns' STEMS, and their tips were
                // read there (the beam face at the drawn stem x, one frame). A bounding rest
                // replaces its side below with the invisible stem's point.
                // MEASURED (session 344): with the face read in the stems' frame the ledger
                // pair staff.staff.tuplet-bracket-follow-beam{,-rest} closes from
                // +0.007811 / +0.006569 (slope × half a stem, both ends, dy exact) — the
                // residual the slope × attach "correction" that stood here left behind.
                double lvX = firstStemX, lvY = firstTipUp;
                double rvX = lastStemX, rvY = lastTipUp;
                if (OuterColumnRestStem(parBeam!, tuplet, measure, isStemUp, first: true) is { } lr)
                    (lvX, lvY) = lr;
                if (OuterColumnRestStem(parBeam!, tuplet, measure, isStemUp, first: false) is { } rr)
                    (rvX, rvY) = rr;
                dy = rvY - lvY;
                followPoints.Add((lvX, lvY));
                followPoints.Add((rvX, rvY));
            }
            else if (allRest)
            {
                // No non-rest column on either side: no slope bounds, no slope.
                // LILYPOND-REF: lily/tuplet-bracket.cc:551-552 calc_position_and_height.
                dy = 0.0;
            }
            else
            {
            // The staff, ink 2.05 widened by staff-padding 0.25, united into the
            // bound columns' extents — THIS is what flattens a within-staff
            // tuplet (:530-535 rv.unite (staff)).
            double lvDir = dir > 0 ? Math.Max(firstTipUp, staffEdge) : Math.Min(firstTipUp, staffEdge);
            double rvDir = dir > 0 ? Math.Max(lastTipUp, staffEdge) : Math.Min(lastTipUp, staffEdge);
            double graphicalDy = rvDir - lvDir;

            // Musical sign gates (:537-549): zero the dy when the chord's top and
            // bottom move opposite ways, or when the graphical dy contradicts them.
            int musUp = Math.Sign(lastHi - firstHi);
            int musDown = Math.Sign(lastLo - firstLo);
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
                    // The beam's quanted outer-edge Y-up at its two outer STEMS — the
                    // spelled stand-in for LP's quantized-positions read (:576-604), over
                    // the span between those stems (the frame the two Y are given in).
                    beamDy = ab.beam.OuterEdgeStaffSpaceAtX(ab.beam.RightStemX, isStemUp)
                        - ab.beam.OuterEdgeStaffSpaceAtX(ab.beam.LeftStemX, isStemUp);
                    subSpan = ab.beam.RightStemX - ab.beam.LeftStemX;
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
            }

            // The offset pass (:708-719): the branch's own points + the staff edge at
            // x0 and x1, cleared against the sloped chord, then padding 1.1.
            // ⚠️ WHICH POINTS depends on the branch, as it does in LilyPond: the follow arm
            // pushed exactly two (:514-518) and the general arm one per column (:554-562).
            var passPoints = followBeam ? followPoints : lpPoints;
            double factor = passPoints.Count > 1 ? 1.0 / span : 1.0;
            double offsetUp = dir > 0 ? double.NegativeInfinity : double.PositiveInfinity;
            void Clear(double px, double py)
            {
                double tuplety = dy * px * factor;
                if (dir * py > dir * (offsetUp + tuplety))
                    offsetUp = py - tuplety;
            }
            foreach (var p in passPoints)
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
        // (`!': an all-rest tuplet never reaches this fallback — it either returned above
        // or took the general arm, whose gate `allRest' implies.)
        double positionDiff = (lastPos!.Value - firstPos!.Value) * 0.5;

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
    /// <summary>
    /// The x of the bracket's bound on <paramref name="left"/>'s side, relative to the bound
    /// item's column x: the STEM's edge when the item has a visible stem pointing the
    /// bracket's way, the COLUMN's ink edge otherwise — a rest's glyph box, a stemless
    /// whole's head, or a head united with a stem that points AGAINST the bracket.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tuplet-bracket.cc:71-85 get_x_bound_item — the bound is
    ///   <c>Note_column::get_stem</c> only when the column's direction is the bracket's AND
    ///   the stem is not <c>Stem::is_invisible</c> AND it has a stencil; otherwise the bound
    ///   stays the column. A rest column's stem is invisible (it prints nothing on its own),
    ///   so a rest bound is its column.
    /// LILYPOND-REF: lily/tuplet-bracket.cc:180-189 calc_x_positions —
    ///   <c>x_span[d] = generic_bound_extent(bounds[d])[d]</c>: the bound's own X extent
    ///   edge, i.e. the stem's edge (its x ± half its thickness) or the column's ink edge.
    /// <para>
    /// ⚠️ UNTIL 2026-09-07 EVERY BOUND WAS TREATED AS A STEM IN THE BRACKET'S DIRECTION,
    /// a rest's included: <c>StemAttachX(bracketDir, 4, Default) ∓ halfStem</c>, which put a
    /// rest-bound bracket's x0 one up-stem attach (1.1742) to the right of the rest's ink
    /// left. That x0 is the frame the encompass points' x are read in, so the offset pass
    /// landed the bracket dy × Δx / span low. MEASURED (scratch/p346/upr-probe.ly,
    /// <c>e8[ \tuplet 3/2 { r8 d c ] } c8</c>): LilyPond's <c>X-positions</c> start at the
    /// rest's ink left (relX 11.791155 = the Rest's own extent left, the NoteColumn's), and
    /// Lily#'s bracket sat 0.040574 low at both ends with the dy exact.
    /// The stem-against-the-bracket and stemless cases are the same clause, ported with it:
    /// for a default head the column's edge on the bracket's side IS the attachment edge the
    /// old spelling read (down attach 0.065 − halfStem = the head's left edge; up attach
    /// + halfStem = its right), so those bounds do not move on default heads.
    /// </para>
    /// </remarks>
    private static double BoundEdgeOffset(MusicItem? item, bool bracketUp, bool left)
    {
        double halfStem = EngravingDefaults.StemThickness / 2;
        switch (item)
        {
            case RestItem { IsSpacer: false } rest:
            {
                var box = GlyphMetrics.GetRestBBox(GlyphMetrics.NoteValueOf(rest.BaseDuration));
                return left ? box.Left : box.Right;
            }
            case NoteItem or ChordItem:
            {
                int value = GlyphMetrics.NoteValueOf(item);
                var style = LayoutUtilities.NoteheadStyleOf(item);
                bool itemUp = item is NoteItem n ? n.StemUp : ((ChordItem)item).StemUp;
                // LILYPOND-REF: lily/stem.cc Stem::is_normal_stem — a whole or a breve has no
                //   stem to be the bound (the same gate NoteColumnLayout.HasStem takes).
                bool hasStem = value >= 2;
                if (hasStem && itemUp == bracketUp)
                    return LayoutUtilities.StemAttachX(bracketUp, value, style)
                        + (left ? -halfStem : halfStem);
                // The column: its head's ink united with its stem's, on the bound's side.
                var head = GlyphMetrics.GetNoteheadBBox(value);
                double edge = left ? head.Left : head.Right;
                if (hasStem)
                {
                    double stemX = LayoutUtilities.StemAttachX(itemUp, value, style);
                    edge = left ? Math.Min(edge, stemX - halfStem) : Math.Max(edge, stemX + halfStem);
                }
                return edge;
            }
            default:
                // A spacer or nothing at the bound: no ink to bound on. The pre-2026-09-07
                // reading, a stem in the bracket's direction, stands in.
                return LayoutUtilities.StemAttachX(bracketUp, 4, NoteheadStyle.Default)
                    + (left ? -halfStem : halfStem);
        }
    }

    /// <summary>
    /// <see cref="BoundEdgeOffset"/> on a TAB staff: the tab stem stands at the digit's
    /// centre (<see cref="EngravingDefaults.TabHeadCenterOffset"/> from the column) and
    /// points its string's way, so the bound is that stem's edge when it points the
    /// bracket's way; otherwise the column's ink — the fret digit's advance united with its
    /// stem — or a rest's glyph box, which the tab draws at the same axis.
    /// </summary>
    /// <remarks>See the call site's citations (get_x_bound_item, calc_x_positions). A chord
    /// reads its first note's fret for the width, as its bend does (ArticulationEngraver).
    /// <paramref name="stemUp"/> is the column's own stem direction — a beam's when beamed
    /// (<see cref="TabColumnStemUp"/>).
    /// MEASURED (2.26.0, Lab sessions/p690/probes/tt-against{L,R}.ly): a bound whose stem
    /// points the bracket's way sits at the stem's edge, one pointing away at the fret
    /// digit's ink edge (LilyPond's whiteout box, 8.709 / 12.842) — both ends, both sides.</remarks>
    private static double TabBoundEdgeOffset(MusicItem? item, bool bracketUp, bool left,
        Staff tab, TabStaffGeometry geom, Rendering.ScoreTextMetrics fonts, bool stemUp)
    {
        double halfStem = EngravingDefaults.StemThickness / 2;
        double centre = EngravingDefaults.TabHeadCenterOffset;
        switch (item)
        {
            case RestItem { IsSpacer: false } rest:
            {
                var box = GlyphMetrics.GetRestBBox(GlyphMetrics.NoteValueOf(rest.BaseDuration));
                return centre + (left ? box.Left : box.Right);
            }
            case NoteItem or ChordItem:
            {
                int value = GlyphMetrics.NoteValueOf(item);
                bool hasStem = value >= 2;
                if (hasStem && stemUp == bracketUp)
                    return centre + (left ? -halfStem : halfStem);
                var (_, fret) = ArticulationEngraver.TabFretOf(tab, Tablature.Tunings.GetTuning(tab.Tuning!.Value), item);
                double half = Math.Max(ArticulationEngraver.TabFretHalfWidth(fonts, fret), hasStem ? halfStem : 0.0);
                return centre + (left ? -half : half);
            }
            default:
                return centre + (left ? -halfStem : halfStem);
        }
    }

    /// <summary>
    /// The DRAWN bracket's positions on a TAB staff — LilyPond's
    /// <c>calc_position_and_height</c> run over the tab's own columns: each column reaches
    /// as far as its fret digits' boxes and its tab stem (the tip
    /// <see cref="TabStaffGeometry.UnbeamedStemTipY"/> gives, or the tab beam's edge), a rest
    /// as far as its glyph where the tab draws it; the bound columns united with the strings
    /// (widened by staff-padding) give the slope, gated by the strings' sign and damped; the
    /// staff's edge joins the points; the line is pushed <c>padding</c> past the farthest point
    /// and, when flat, quantised off the strings. Device Y in the frame the geometry's
    /// <see cref="TabStaffGeometry.StaffY"/> sets — the caller hands a staff-local one
    /// (top line at 0) and adds the staff offset as the notation path does.
    /// </summary>
    /// <remarks>
    /// MEASURED (2.26.0, Lab sessions/p690/probes/tabtuplet5.ly — two eighths on the D
    /// string, stems down): the stems end 2.25 below the bottom string and the bracket line
    /// stands 1.13 below the stem tips (18.25 against 17.12), i.e. padding 1.1 off the
    /// columns' reach; the notation-frame reading put it 1.4 ABOVE them, through the stems.
    /// LILYPOND-REF: lily/tuplet-bracket.cc:463-477 calc_position_and_height — the staff
    ///   widened by staff-padding; :491-519 the follow_beam arm (a covering beam on the
    ///   bracket's side: the outer stems' tips are the two points, their difference the dy,
    ///   no staff points); :520-562 the else arm: bound columns' cross_staff_extent united
    ///   with the staff, the musical sign gates, one point per column; :566-630 the damping
    ///   (max-slope-factor × the last column's x, capped by the covering beam's slope, or the
    ///   slope of the beam the last beamed column's stem belongs to over that beam's own
    ///   extent); :633-637 the staff points; :646-680 the enclosed tuplets' points (each one's
    ///   box at its two ends, taken at `linear_combination (d * sign (other_dy))`, and its
    ///   number's outer edge at the number's centre); :682-706 avoid-scripts (every script of
    ///   the tuplet's notes with no outside-staff-priority, its centre and its edge on the
    ///   bracket's side); :708-746 the offset pass, padding, and the flat quantise
    ///   (`*offset /= 0.5 * ss`, staff_span widened by ss, rint, off a line by dir).
    /// LILYPOND-REF: lily/note-column.cc:251-258 cross_staff_extent — the heads' extent
    ///   united with the stem's.
    /// ⚠️ Narrowings, disclosed: the follow arm needs NOTE columns at both ends (a beamed
    ///   rest at an edge falls to the else arm — LilyPond gives the rest an invisible stem);
    ///   an enclosed tuplet whose own bracket is hidden adds its number only (LilyPond reads
    ///   an empty stencil box there); scripts pair by (staff, bar, item range), not by voice —
    ///   the notation port's same narrowing.
    /// ⚠️ THE BEAM-SLOPE CAP HAS NO OBSERVER YET: it is reached only when a covering beam
    ///   does NOT decide the bracket's side (else the follow arm runs) or when the last beamed
    ///   column's beam leaves the tuplet, AND the slope passes the sign gates AND exceeds
    ///   max-slope-factor × the last column's x. Six tab probes against 2.26.0
    ///   (sessions/p690/probes/tt-cap*.ly) never reached it — poisoning it changed none.
    /// MEASURED residuals, not of this method: a beamed tab stem ends 0.56 higher in Lily#
    ///   than in LilyPond (the tab beam's own placement), and a tab turn 0.17 higher — the
    ///   bracket clears both by LilyPond's padding, so it inherits the two differences.
    /// </remarks>
    private static (double startY, double endY) TabBracketPositions(
        TupletBracketItem tuplet, ImmutableArray<Measure> measures, MeasureLayout measureLayout,
        bool bracketUp, double x0, double x1, TabStaffGeometry geom,
        ImmutableArray<BeamLayout> beamLayouts, Rendering.ScoreTextMetrics fonts,
        ImmutableArray<ArticulationLayout> scripts, IReadOnlyList<TupletBracketLayout>? children,
        double staffOffset)
    {
        int dir = bracketUp ? 1 : -1;               // Y-up, from the tab's middle
        double ss = geom.StringSpace;               // the TabStaff's staff-space (1.5)
        double middle = geom.MiddleY;               // device
        double YUp(double device) => middle - device;
        double halfDigit = TabConstants.FretDigitHeight(fonts) / 2.0;
        int strings = geom.StringCount;
        // The staff symbol's extent, widened by staff-padding (:471-476), Y-up.
        double staffReach = (strings - 1) / 2.0 * ss + StaffPaddingLP;
        double staffEdge = dir * staffReach;

        // The tab beam a member stem belongs to, if any (this staff, this voice, this bar).
        BeamLayout? MemberBeam(int itemIndex)
        {
            if (beamLayouts.IsDefaultOrEmpty)
                return null;
            foreach (var b in beamLayouts)
            {
                if (b.StaffIndex != tuplet.StaffIndex
                    || b.Group.MeasureIndex != tuplet.MeasureIndex
                    || b.Group.VoiceIndex != tuplet.VoiceIndex)
                    continue;
                foreach (var m in b.Group.Members)
                    if (m.ResolveMeasureIndex(b.Group.MeasureIndex) == tuplet.MeasureIndex && m.ItemIndex == itemIndex)
                        return b;
            }
            return null;
        }

        // The x a beam's line is read at for a member: its column plus the stem attachment on
        // the beam's side — the frame TabBeamOuterEdgeY quants the line in (the same read the
        // tab slur's stem edge makes, ElementCoordinator's TabStemOf).
        static double BeamFrameX(double columnX, MusicItem item, bool up)
            => columnX + LayoutUtilities.StemAttachX(up, GlyphMetrics.NoteValueOf(item),
                LayoutUtilities.NoteheadStyleOf(item));

        // A beam's two ends in its own frame, and its outer edge there (Y-up): LilyPond's
        // quantized-positions and the beam's X extent, as the damping reads them.
        (double XL, double XR, double YL, double YR) BeamEnds(BeamLayout beam)
        {
            bool up = geom.GroupStemUp(beam.Group.MemberItems());
            int n = beam.Group.Members.Length;
            double xl = BeamFrameX(beam.MemberXPositions.Length > 0 ? beam.MemberXPositions[0] : 0,
                beam.Group.ItemOf(0), up);
            double xr = BeamFrameX(beam.MemberXPositions.Length >= n ? beam.MemberXPositions[n - 1] : 0,
                beam.Group.ItemOf(n - 1), up);
            return (xl, xr, YUp(ArticulationEngraver.TabBeamOuterEdgeY(beam, geom, xl)),
                YUp(ArticulationEngraver.TabBeamOuterEdgeY(beam, geom, xr)));
        }

        // A note column's tab stem tip (Y-up): on its beam's OUTER edge when beamed, else where
        // an unbeamed tab stem ends; null with no stem. ⚠️ The outer edge although LilyPond
        // DRAWS a beamed tab stem only to the beam's middle: the stem's Y extent reaches the
        // beam's far side. MEASURED (2.26.0, Lab sessions/p690/probes): tt-nest.ly's hidden
        // bracket stands 1.10 off the beam's outer edge (1.34 off its middle, where the stem
        // rect ends), and tt-cap2.ly comes out flat only if the first column reaches the outer
        // edge (3.01 over the last digit's 2.85 — the sign gate zeroes the slope; the middle,
        // 2.83, would have tilted it).
        double? StemTipUp(MusicItem item, int itemIndex, double columnX)
        {
            if (NoteColumnLayout.Of(item) is not { HasStem: true })
                return null;
            var beam = MemberBeam(itemIndex);
            if (beam is not null)
            {
                bool beamUp = geom.GroupStemUp(beam.Group.MemberItems());
                return YUp(ArticulationEngraver.TabBeamOuterEdgeY(beam, geom, BeamFrameX(columnX, item, beamUp)));
            }
            bool up = geom.TabStemUp(item);
            return geom.UnbeamedStemTipY(item, up, geom.StemHeadString(item, up)) is { } t ? YUp(t) : null;
        }

        // A column's reach on the bracket's side (Y-up) and its strings' positions.
        (double Reach, int Lo, int Hi)? ColumnReach(MusicItem item, int itemIndex, double columnX)
        {
            switch (item)
            {
                case NoteItem or ChordItem:
                {
                    var (lo, hi) = geom.HeadPositionRange(item);
                    // The digits' boxes: every string the item sounds, ± half a digit.
                    double reach = dir > 0
                        ? hi * ss / 2.0 + halfDigit
                        : lo * ss / 2.0 - halfDigit;
                    // …united with the stem, whichever way it points (cross_staff_extent).
                    if (StemTipUp(item, itemIndex, columnX) is { } tip)
                        reach = dir > 0 ? Math.Max(reach, tip) : Math.Min(reach, tip);
                    return (reach, lo, hi);
                }
                case RestItem { IsSpacer: false } rest:
                {
                    // Where the tab draws the rest (SharedRenderer.Tab): a whole hangs from
                    // the upper central string, a half sits on the lower, the rest centre on
                    // the tab's middle — the glyph's box from that origin.
                    int value = GlyphMetrics.NoteValueOf(rest.BaseDuration);
                    var box = GlyphMetrics.GetRestBBox(value);
                    double originUp = value switch
                    {
                        1 => YUp(geom.StringY(strings / 2)),
                        2 => YUp(geom.StringY(strings / 2 + 1)),
                        _ => -(box.Top + box.Bottom) / 2.0,
                    };
                    return (dir > 0 ? originUp + box.Top : originUp + box.Bottom, 0, 0);
                }
                default:
                    return null;
            }
        }

        var items = measures[tuplet.MeasureIndex].Items;
        var points = new List<(double X, double Y)>();
        (double Reach, int Lo, int Hi)? first = null, last = null;
        double lastX = 0;
        int columnCount = 0;
        // The outer COLUMNS (note or rest), and the last column whose stem carries a beam —
        // the follow arm and the damping's beam read them.
        (MusicItem Item, int Index, double X)? firstColumn = null, lastColumn = null;
        BeamLayout? lastColumnBeam = null;
        for (int i = tuplet.StartNoteIndex; i <= tuplet.EndNoteIndex && i < items.Length; i++)
        {
            if (items[i].GraceTime)
                continue;
            double columnX = measureLayout.X
                + LayoutUtilities.GetItemXOffset(measures, tuplet.MeasureIndex, i, measureLayout);
            if (ColumnReach(items[i], i, columnX) is not { } col)
                continue;
            columnCount++;
            firstColumn ??= (items[i], i, columnX);
            lastColumn = (items[i], i, columnX);
            if (items[i] is NoteItem or ChordItem && NoteColumnLayout.Of(items[i]) is { HasStem: true }
                && MemberBeam(i) is { } b)
                lastColumnBeam = b;
            double x = columnX - x0;
            points.Add((x, col.Reach));
            lastX = x;
            if (items[i] is NoteItem or ChordItem)
            {
                first ??= col;
                last = col;
            }
        }

        // par_beam: the tab beam that carries the tuplet's own stems (:481), and whether the
        // bracket FOLLOWS it — the beam on the bracket's side, both outer columns stemmed
        // notes (:491-496; a tab beam is never a knee).
        var parBeam = beamLayouts.IsDefaultOrEmpty ? null : FindCoveringBeam(beamLayouts, tuplet, measures);
        bool followBeam = parBeam is not null
            && geom.GroupStemUp(parBeam.Group.MemberItems()) == bracketUp
            && firstColumn is { Item: NoteItem or ChordItem } fc && NoteColumnLayout.Of(fc.Item) is { HasStem: true }
            && lastColumn is { Item: NoteItem or ChordItem } lc && NoteColumnLayout.Of(lc.Item) is { HasStem: true };

        double dy = 0.0;
        if (followBeam)
        {
            // The follow arm (:497-518): the two outer stems' tips, at the drawn stems' x.
            var (fItem, fIndex, fX) = firstColumn!.Value;
            var (lItem, lIndex, lX) = lastColumn!.Value;
            double tipL = StemTipUp(fItem, fIndex, fX) ?? staffEdge;
            double tipR = StemTipUp(lItem, lIndex, lX) ?? staffEdge;
            dy = tipR - tipL;
            points.Clear();
            points.Add((fX + EngravingDefaults.TabHeadCenterOffset - x0, tipL));
            points.Add((lX + EngravingDefaults.TabHeadCenterOffset - x0, tipR));
        }
        else if (first is { } f && last is { } l)
        {
            // The slope: the bound columns' reach united with the staff, the sign gates.
            double lv = dir > 0 ? Math.Max(f.Reach, staffEdge) : Math.Min(f.Reach, staffEdge);
            double rv = dir > 0 ? Math.Max(l.Reach, staffEdge) : Math.Min(l.Reach, staffEdge);
            double graphicalDy = rv - lv;
            int musUp = Math.Sign(l.Hi - f.Hi), musDown = Math.Sign(l.Lo - f.Lo);
            dy = musUp != musDown || Math.Sign(graphicalDy) != musDown ? 0.0 : graphicalDy;
        }

        // The damping (:566-630): max_dy = max-slope-factor × the last column's x; a beam —
        // par_beam over the bracket's own span, else the last beamed column's beam over its own
        // extent — lends its slope as the cap.
        if (dy != 0.0)
        {
            double span = x1 - x0;
            double slope = Math.Abs(dy / span);
            double maxDy = MaxSlopeFactor * lastX * Math.Sign(dy);
            double beamDy = 0.0, subSpan = 0.0;
            if (parBeam is not null)
            {
                var e = BeamEnds(parBeam);
                beamDy = e.YR - e.YL;
            }
            else if (lastColumnBeam is not null)
            {
                var e = BeamEnds(lastColumnBeam);
                beamDy = e.YR - e.YL;
                subSpan = e.XR - e.XL;
            }
            if (beamDy != 0.0)
            {
                double beamSlope = Math.Abs(beamDy / (subSpan != 0.0 ? subSpan : span));
                double maxSlope = beamSlope != 0.0 ? Math.Max(beamSlope, MaxSlopeFactor) : MaxSlopeFactor;
                slope = Math.Min(slope, maxSlope);
                if (Math.Abs(dy) > Math.Abs(maxDy))
                    dy = Math.Abs(dy * slope) <= Math.Abs(maxDy) ? dy * slope : maxDy;
            }
            else if (Math.Abs(dy) > Math.Abs(maxDy))
            {
                dy = maxDy;
            }
        }

        // The staff's own edge joins the points (:633-637) — unless the bracket follows its beam.
        if (!followBeam)
        {
            points.Add((0.0, staffEdge));
            points.Add((x1 - x0, staffEdge));
        }

        // The enclosed tuplets (:646-680): each one's box at its two ends, and its number.
        // Their layouts carry the staff offset (Y-up from the system top); this frame is
        // the staff-local one, so it comes off again.
        if (children is not null)
        {
            var numberStyle = NumberStyle(fonts);
            double numberEm = NumberEm(fonts);
            double halfThick = EngravingDefaults.TupletBracketThickness / 2.0;
            foreach (var c in children)
            {
                double y0 = YUp(-c.StartYUp - staffOffset), y1 = YUp(-c.EndYUp - staffOffset);
                if (c.ShowBracket)
                {
                    int cdir = c.IsStemUp ? 1 : -1;
                    double edge = GetEdgeHeight() * c.LineSpacing;
                    double lo = Math.Min(Math.Min(y0, y1), Math.Min(y0 - cdir * edge, y1 - cdir * edge)) - halfThick;
                    double hi = Math.Max(Math.Max(y0, y1), Math.Max(y0 - cdir * edge, y1 - cdir * edge)) + halfThick;
                    int otherSign = Math.Sign(y1 - y0);
                    foreach (int d in new[] { -1, 1 })
                    {
                        double l = d * otherSign;
                        double y = ((1 - l) * lo + (1 + l) * hi) / 2.0;
                        double x = (d < 0 ? c.DrawnStartX - halfThick : c.DrawnEndX + halfThick) - x0;
                        points.Add((x, y));
                    }
                }
                if (!string.IsNullOrEmpty(c.NumberText))
                {
                    double halfH = fonts.InkHeight(c.NumberText, numberEm, Rendering.TextRole.Tuplet, numberStyle) / 2.0;
                    points.Add((c.NumberX - x0, (y0 + y1) / 2.0 + dir * halfH));
                }
            }
        }

        // avoid-scripts (:682-706): every script of the tuplet's notes with no
        // outside-staff-priority, at its ink centre and its edge on the bracket's side. A tab
        // script's YUp is staff-local about the nominal middle (EngravingDefaults.StaffMiddle
        // below the top line — ArticulationEngraver's tabYUp), so it re-bases onto the tab's.
        if (!scripts.IsDefaultOrEmpty)
        {
            double rebase = middle - EngravingDefaults.StaffMiddle;
            foreach (var a in scripts)
            {
                if (a.OutsideStaffPriority != null
                    || a.StaffIndex != tuplet.StaffIndex
                    || a.MeasureIndex != tuplet.MeasureIndex
                    || a.ItemIndex < tuplet.StartNoteIndex
                    || a.ItemIndex > tuplet.EndNoteIndex)
                    continue;
                points.Add((a.X + a.Ink.CenterX - x0,
                    rebase + a.YUp + (dir > 0 ? a.Ink.Top : a.Ink.Bottom)));
            }
        }

        // The offset pass (:708-719): the line pushed just past the farthest point, then padding.
        double offset = -dir * double.PositiveInfinity;
        double factor = columnCount > 1 ? 1.0 / (x1 - x0) : 1.0;
        foreach (var (x, y) in points)
        {
            double tuplety = dy * x * factor;
            if (y * dir > (offset + tuplety) * dir)
                offset = y - tuplety;
        }
        offset += BracketPadding * dir;

        // A flat bracket keeps off the strings (:726-746): in the tab's positions, rounded,
        // and stepped past a string it would sit on.
        if (Math.Abs(dy) < 0.01)
        {
            offset /= 0.5 * ss;
            double spanLo = -(strings - 1) - ss, spanHi = (strings - 1) + ss;
            if (offset >= spanLo && offset <= spanHi)
            {
                offset = Math.Round(offset, MidpointRounding.ToEven);
                if (EngravingDefaults.OnStaffLine((int)offset, strings))
                    offset += dir;
            }
            offset *= 0.5 * ss;
        }

        return (middle - offset, middle - (offset + dy));
    }

    /// <summary>
    /// <see cref="CalculateDirection"/> on a TAB staff: each note column's stem points its
    /// STRINGS' way (<see cref="TabStaffGeometry.TabStemUp"/>) — or its tab beam's
    /// (<see cref="TabStaffGeometry.GroupStemUp"/>) when it is beamed — and the count reads
    /// those; a tie with no stem at all goes UP; a tie between stems goes to the side whose
    /// extreme head stands deeper past the staff's edge on its own side — LilyPond's
    /// extremal-positions rule, with its unit mix (the staff extent in the tab's staff
    /// spaces against head positions in half-spaces) kept.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/tuplet-bracket.cc:779-817 Tuplet_bracket::get_default_dir —
    /// the columns' Note_column::dir (the stem's, a beam's when beamed), then :797-813 the
    /// extremal positions against the staff symbol's extent; on a TabVoice a head's position
    /// is its string's (lily/tab-note-heads-engraver.cc:99-122).
    /// MEASURED (2.26.0, Lab sessions/p690/probes/tt-beam.ly — `f,4` on the D string, stem
    /// down, and a beamed `bes,,8` on the A string, stem up): the tie goes UP; the tab beam's
    /// own rule, which stood here, answered DOWN.</remarks>
    private static bool TabDirection(TupletBracketItem tuplet, ImmutableArray<Measure> measures,
        TabStaffGeometry geom, ImmutableArray<BeamLayout> beamLayouts)
    {
        bool StemUp(MusicItem item, int itemIndex) => TabColumnStemUp(tuplet, item, itemIndex, geom, beamLayouts);

        if (measures.IsDefaultOrEmpty || tuplet.MeasureIndex >= measures.Length)
            return true;
        var items = measures[tuplet.MeasureIndex].Items;
        int up = 0, down = 0;
        double extremeUp = double.NegativeInfinity, extremeDown = double.PositiveInfinity;
        for (int i = tuplet.StartNoteIndex; i <= tuplet.EndNoteIndex && i < items.Length; i++)
        {
            if (items[i] is not (NoteItem or ChordItem) || items[i].GraceTime)
                continue;
            var (lo, hi) = geom.HeadPositionRange(items[i]);
            if (StemUp(items[i], i))
            {
                up++;
                extremeUp = Math.Max(extremeUp, hi);
            }
            else
            {
                down++;
                extremeDown = Math.Min(extremeDown, lo);
            }
        }
        if (up != down)
            return up > down;
        if (up == 0)
            return true;
        // The staff symbol's extent (the outer strings ± half a line) in the tab's spaces.
        double staffHalf = (geom.StringCount - 1) / 2.0 * geom.StringSpace + EngravingDefaults.StaffLineThickness / 2.0;
        double upDepth = -(staffHalf - extremeUp);          // :811, d = UP
        double downDepth = (-staffHalf - extremeDown);      // :811, d = DOWN
        return upDepth <= downDepth;
    }

    /// <summary>A tab note column's stem direction: its tab beam's
    /// (<see cref="TabStaffGeometry.GroupStemUp"/>) when the stem is beamed, else its
    /// strings' (<see cref="TabStaffGeometry.TabStemUp"/>) — LilyPond's Note_column::dir,
    /// which every tab reader of the bracket asks: the side it takes and which bound is a
    /// stem. MEASURED (2.26.0, Lab sessions/p690/probes/tt-cap.ly): an A-string eighth
    /// beamed DOWN with the D-string one is a stem-down bound — the bracket starts at its
    /// stem's edge, where the strings' rule (up) put it at the digit's edge, 0.75 left.</summary>
    private static bool TabColumnStemUp(TupletBracketItem tuplet, MusicItem item, int itemIndex,
        TabStaffGeometry geom, ImmutableArray<BeamLayout> beamLayouts)
    {
        if (!beamLayouts.IsDefaultOrEmpty)
            foreach (var b in beamLayouts)
            {
                if (b.StaffIndex != tuplet.StaffIndex || b.Group.VoiceIndex != tuplet.VoiceIndex)
                    continue;
                foreach (var m in b.Group.Members)
                    if (m.ResolveMeasureIndex(b.Group.MeasureIndex) == tuplet.MeasureIndex && m.ItemIndex == itemIndex)
                        return geom.GroupStemUp(b.Group.MemberItems());
            }
        return geom.TabStemUp(item);
    }

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
