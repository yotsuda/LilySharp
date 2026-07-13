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
using System.Linq;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for an articulation mark.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: define-grobs.scm:2268-2310 Script grob
/// LILYPOND-REF: script-interface.cc positioning logic
/// </remarks>
internal readonly record struct ArticulationLayout(
    int MeasureIndex,       // Measure containing this articulation
    int ItemIndex,          // Item index within measure (for X alignment)
    double X,               // Absolute X position (staff spaces from score start)
    double Y,               // Y position (staff spaces from staff top, positive = down)
    string Glyph,           // SMuFL glyph to render
    bool IsAbove,           // Whether placed above the note
    int SourcePosition,     // For click-to-source mapping
    double Scale = 1.0,     // Glyph scale (editorial accidentals: magstep(-2))
    GlyphMetrics.BBox Ink = default, // Ink box relative to the anchor (for skyline seeding)
    int SourceIndex = -1,   // F3/B: index into score.Articulations (data-pos resolved at render)
    int StaffIndex = 0      // Which staff this script sits on (per-staff below-staff seeding)
);

/// <summary>
/// Calculates positions for articulation marks.
/// Implements LilyPond's articulation positioning algorithm.
/// </summary>
/// <remarks>
/// LILYPOND-REF: script-engraver.cc:92-125 Script_engraver::acknowledge_note_head
/// LILYPOND-REF: side-position-interface.cc:92-111 axis_aligned_side_helper
///
/// LilyPond places articulations with:
/// - avoid-slur: around
/// - direction: automatically chosen based on stem direction
/// - padding: 0.2 staff spaces
/// - staff-padding: 0.25 staff spaces
/// </remarks>
internal static class ArticulationEngraver
{
    /// <summary>A script's vertical padding to its support — the shared LP table
    /// (fermata 0.40, portato 0.45, else 0.20). See <see cref="ArticulationSpacing"/>.</summary>
    private static double PaddingFor(ArticulationType type) =>
        ArticulationSpacing.VerticalPadding(type);

    // LILYPOND-REF: define-grobs.scm:2295 staff-padding = 0.25
    private const double StaffPadding = 0.25;

    // Bend/fall glyph X placement (staff-spaces; Lily#'s own tuning, no direct
    // LP grob — LP renders bends via a different mechanism).
    /// <summary>X offset of a bend glyph from the note on a TAB staff.</summary>
    private const double BendTabXOffset = 0.5;
    /// <summary>X gap between a bend glyph and the notehead's right edge.</summary>
    private const double BendHeadPadding = 0.15;
    /// <summary>X offset for a bend that arrives FROM THE LEFT (scoop/plop).</summary>
    private const double BendApproachXOffset = 1.55;

    // Notehead half-extent and stem length: the canonical values live in
    // EngravingDefaults (single source of truth, LILYPOND-REF there).
    private const double NoteheadHalfHeight = EngravingDefaults.NoteheadHalfHeight;
    private const double DefaultStemLength = EngravingDefaults.DefaultStemLength;

    // Editorial (suggestion) accidentals print at font-size -2:
    // magstep(-2) = 2^(-2/6) ≈ 0.7937.
    // LILYPOND-REF: scm/define-grobs.scm:101 AccidentalSuggestion (font-size . -2)
    private const double EditorialScale = 0.7937;

    // Staff middle line position (see EngravingDefaults.StaffMiddle).
    private const double StaffMiddle = EngravingDefaults.StaffMiddle;

    // Staff top and bottom
    private const double StaffTop = 0.0;
    private const double StaffBottom = 4.0;

    // Breathing-sign placement: gap to the RIGHT of the note's right edge, and the
    // Y at the top of the staff (the comma straddles the top line). Tuned to
    // LilyPond's \breathe (scripts.rcomma at the staff top).
    // LILYPOND-REF: lily/breathing-sign.cc offset-callback (top of staff).
    private const double BreathGap = 0.55;
    private const double BreathStaffY = -0.5;

    /// <summary>
    /// Calculates layout for all articulations in a score.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: side-position-interface.cc:193-400 aligned_side()
    /// Articulations are positioned relative to the note's staff position:
    /// - For notes above middle line: articulations go below (unless overridden)
    /// - For notes below middle line: articulations go above (unless overridden)
    /// - Fermata and ornaments always go above
    /// </remarks>
    /// <summary>
    /// Iteration order that stacks multiple scripts on one note in LilyPond's
    /// script-priority order (staccato innermost, then tenuto, then default
    /// scripts, then fermata), independent of the written order. Groups by note
    /// (first-occurrence index keeps notes in their original order) and sorts by
    /// priority within each note; OrderBy is stable so equal priorities keep the
    /// written order. Reorders the ITERATION (not the array): SourceIndex must
    /// stay the articulation's original index for click-to-source mapping.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/script.scm script-priority.</remarks>
    private static int[] OrderByScriptPriority(ImmutableArray<ArticulationItem> articulations)
    {
        int[] order = Enumerable.Range(0, articulations.Length).ToArray();
        if (articulations.Length > 1)
        {
            var firstSeen = new Dictionary<(int, int, int), int>();
            for (int k = 0; k < articulations.Length; k++)
            {
                var a = articulations[k];
                var nk = (a.StaffIndex, a.MeasureIndex, a.ItemIndex);
                if (!firstSeen.ContainsKey(nk)) firstSeen[nk] = k;
            }
            order = order
                .OrderBy(i => firstSeen[(articulations[i].StaffIndex,
                    articulations[i].MeasureIndex, articulations[i].ItemIndex)])
                .ThenBy(i => ScriptPriority(articulations[i].Type))
                .ToArray();
        }
        return order;
    }

    public static ImmutableArray<ArticulationLayout> Calculate(
        Score score,
        ImmutableArray<ArticulationItem> articulations,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        Dictionary<int, ImmutableArray<Measure>>? measuresByStaff = null,
        Func<int, int, double>? staffYAt = null,
        Dictionary<int, Staff>? staffByIndex = null,
        ImmutableArray<BeamLayout> beamLayouts = default)
    {
        if (articulations.IsDefaultOrEmpty)
            return ImmutableArray<ArticulationLayout>.Empty;

        int[] order = OrderByScriptPriority(articulations);

        var beamedTips = BuildBeamedStemTips(beamLayouts);
        var beamGroups = BuildBeamGroupMap(beamLayouts);
        var layouts = ImmutableArray.CreateBuilder<ArticulationLayout>(articulations.Length);
        // Per-note, per-side running offset so stacked scripts don't overprint:
        // each successive script on the same (staff, measure, item, side) is pushed
        // outward past the previous glyph + a small padding.
        var stackOffset = new Dictionary<(int, int, int, bool), double>();

        foreach (int arti in order)
        {
            var articulation = articulations[arti];
            // Find the measure layout
            if (articulation.MeasureIndex >= measureLayouts.Length)
                continue;

            var measureLayout = measureLayouts[articulation.MeasureIndex];

            // Bounds guard (single-staff layouts only; multi-staff layouts
            // resolve through timing-aligned columns).
            if (measureLayout.Columns.IsDefaultOrEmpty
                && articulation.ItemIndex >= measureLayout.Items.Length)
                continue;

            // Resolve this articulation's OWN staff (multi-staff): its measures
            // (to read the right note's staff position) and the staff's vertical
            // offset within the system, so it sits under its own staff.
            var artMeasures = LayoutUtilities.ResolveStaffMeasures(measuresByStaff, articulation.StaffIndex, score.Voice.Measures);
            double staffOffset = staffYAt?.Invoke(articulation.MeasureIndex, articulation.StaffIndex) ?? 0;

            if (articulation.MeasureIndex >= artMeasures.Length)
                continue;

            // Get the music item to determine staff position
            // LILYPOND-REF: script-engraver.cc:92-125 acknowledge_note_head
            var measure = artMeasures[articulation.MeasureIndex];
            if (articulation.ItemIndex >= measure.Items.Length)
                continue;
            var item = measure.Items[articulation.ItemIndex];

            // Fall / Doit (bend-after): a short curve trailing off the RIGHT of the
            // note at the note's own height — on a tab staff, off the fret digit's
            // string row. Positioned independently of the Script side machinery.
            if (articulation.Type is ArticulationType.Fall or ArticulationType.Doit
                or ArticulationType.Bend or ArticulationType.Scoop or ArticulationType.Plop)
            {
                double itemX = measureLayout.X + LayoutUtilities.GetItemXOffset(
                    artMeasures, articulation.MeasureIndex, articulation.ItemIndex, measureLayout);
                double fx, fy;
                Staff? tabBendStaff = null;
                if (staffByIndex != null
                    && staffByIndex.TryGetValue(articulation.StaffIndex, out var ts)
                    && ts.IsTab && ts.Tuning.HasValue)
                    tabBendStaff = ts;
                // The fret digit sits a TabHeadCenterOffset right of the note column
                // (see EngravingDefaults) — the gesture hangs off the digit, not the column.
                double noteX = tabBendStaff != null ? itemX + EngravingDefaults.TabHeadCenterOffset : itemX;
                if (tabBendStaff != null)
                {
                    var tt = tabBendStaff.Tuning.Value;
                    int strings = Tunings.GetStringCount(tt);
                    double space = EngravingDefaults.TabStringSpace(strings);
                    int midi = item switch { NoteItem n => n.Midi,
                        ChordItem c when c.Notes.Length > 0 => c.Notes[0].Midi, _ => 0 };
                    int? sn = item is NoteItem ni ? ni.StringNumber : null;
                    var (strNum, _) = Tunings.CalculateFret(
                        midi + Tunings.SoundingShift(tabBendStaff.TabSourceClef, tabBendStaff.Transposition),
                        Tunings.GetTuning(tt), sn ?? 0);
                    fx = noteX + BendTabXOffset;
                    fy = staffOffset + (strNum - 1) * space;
                }
                else
                {
                    fx = noteX + 2.0 * NoteheadHalfWidth(item) + BendHeadPadding;
                    fy = (StaffMiddle - GetStaffPosition(item) * 0.5) + staffOffset;
                }
                bool approach = articulation.Type
                    is ArticulationType.Scoop or ArticulationType.Plop;
                if (approach)
                    fx = noteX - BendApproachXOffset; // the curve arrives FROM THE LEFT
                string bendGlyph = articulation.Type switch
                {
                    ArticulationType.Scoop => "bendScoop",
                    ArticulationType.Plop => "bendPlop",
                    ArticulationType.Fall => "bendFall",
                    ArticulationType.Doit => "bendDoit",
                    // Guitar bend-up: the renderer parses the amount off the
                    // sentinel and draws arrow + label.
                    _ => $"bendUp:{articulation.BendSemitones}",
                };
                layouts.Add(new ArticulationLayout(
                    articulation.MeasureIndex, articulation.ItemIndex, fx, fy,
                    bendGlyph, true, articulation.SourcePosition, 1.0, SourceIndex: arti, StaffIndex: articulation.StaffIndex));
                continue;
            }

            // Breathing signs are not Scripts: place them at the TOP of the staff,
            // just to the right of the note (in the gap before the next note),
            // independent of the note's pitch and stem — so they skip the whole
            // Script side-positioning machinery below.
            // LILYPOND-REF: lily/breathing-sign.cc — BreathingSign Y at staff top;
            // the engraver emits the sign after the note it follows.
            if (articulation.Type is ArticulationType.Breath or ArticulationType.Caesura)
            {
                double bx = measureLayout.X
                    + LayoutUtilities.GetItemXOffset(artMeasures,
                        articulation.MeasureIndex, articulation.ItemIndex, measureLayout)
                    + 2.0 * NoteheadHalfWidth(item)  // full notehead advance → right edge
                    + BreathGap;
                double by = BreathStaffY + staffOffset;
                layouts.Add(new ArticulationLayout(
                    articulation.MeasureIndex,
                    articulation.ItemIndex,
                    bx,
                    by,
                    articulation.GetGlyph(),
                    true,
                    articulation.SourcePosition,
                    1.0,
                    GetSeedBBox(articulation.Type), SourceIndex: arti, StaffIndex: articulation.StaffIndex));
                continue;
            }

            // Get staff position of the note
            int staffPosition = GetStaffPosition(item);
            bool stemUp = GetStemUp(item, staffPosition);

            // In LilyPond the actual Stem grob — carrying the voice's forced
            // direction (\voiceOne up, \voiceTwo down) — is added as a side-position
            // SUPPORT of the script, so the fermata clears the real stem.
            // LILYPOND-REF: lily/script-engraver.cc:181-191 acknowledge_stem —
            //   Side_position_interface::add_support (script, stem).
            // Here the note's pitch-natural StemUp is the WRONG direction for a
            // multi-voice staff (a high voice-1 note is drawn stem-UP), so the up-stem
            // would pierce the glyph. Articulations resolve against the staff's
            // PRIMARY voice (LayoutEngine sets measuresByStaff = PrimaryVoice), so use
            // voice 1's forced direction. Mirrors SkylineBuilder / DynamicEngraver.
            // (Beamed members refine this from the beam just below.)
            if (staffByIndex != null
                && staffByIndex.TryGetValue(articulation.StaffIndex, out var ownStaff)
                && ownStaff.Voices.Length > 1
                && VoiceDefaults.GetDefaultStemUp(1) is { } voiceStemUp)
                stemUp = voiceStemUp;

            // A beamed member's stem ends on the BEAM, not at the unbeamed
            // formula's tip, and the beam also resolves its direction.
            double? beamedStemTipY = null;
            if (beamedTips.TryGetValue(
                (articulation.StaffIndex, articulation.MeasureIndex, articulation.ItemIndex),
                out var beamTip))
            {
                beamedStemTipY = beamTip.TipY;
                stemUp = beamTip.StemUp;
            }

            // On a TAB staff the fret number is centred on the note column (the
            // stem's x), with no notehead. So put the script at that column x — not
            // a notehead-edge offset, which makes a staccato dot look like an
            // augmentation dot beside the number — and just outside the staff on the
            // side away from the stem.
            if (staffByIndex != null
                && staffByIndex.TryGetValue(articulation.StaffIndex, out var tabStaff)
                && tabStaff.IsTab && tabStaff.Tuning.HasValue)
            {
                int strings = Tunings.GetStringCount(tabStaff.Tuning.Value);
                double space = EngravingDefaults.TabStringSpace(strings);
                // Centre on the fret digit, which sits a TabHeadCenterOffset right
                // of the note column (see EngravingDefaults).
                double colX = measureLayout.X
                    + LayoutUtilities.GetItemXOffset(artMeasures,
                        articulation.MeasureIndex, articulation.ItemIndex, measureLayout)
                    + EngravingDefaults.TabHeadCenterOffset;
                const double tabGap = 1.0;
                var geom = new TabStaffGeometry(
                    tabStaff.Tuning.Value, staffOffset, tabStaff.TabSourceClef, tabStaff.Transposition);
                bool isTabBeamed = beamGroups.TryGetValue(
                    (articulation.StaffIndex, articulation.MeasureIndex, articulation.ItemIndex),
                    out var tabBeam);
                bool tabBeamUp = isTabBeamed
                    && geom.GroupStemUp(tabBeam.Group.Members.Select(m => m.Item));
                // A tab stem's direction is string-based (the tab head), not the notated
                // pitch — so a bass note on the bottom strings has an UP stem, and a
                // stem-coupled mark sits on the opposite (DOWN) side. A BEAMED note takes
                // its whole beam's direction, not its own string: an inner note on a high
                // string still points up with the group, so its staccato dot sits below.
                bool tabStemUp = isTabBeamed ? tabBeamUp : geom.StringStemUp(geom.MeanString(item));
                // Fermata, ornaments and bow marks keep direction = UP on a tab
                // staff too — LilyPond's TabVoice keeps the Script_engraver and
                // the script side-positions above the staff symbol; only
                // stem-coupled articulations (staccato, accent, …) sit opposite
                // the stem. Blindly following the stem parked the final chord's
                // fermata INSIDE the staff under the bottom digit.
                // LILYPOND-REF: ly/engraver-init.ly:1170-1188 TabVoice;
                // LILYPOND-REF: scm/define-grobs.scm:1365 fermata direction = UP.
                bool tabForceAbove = IsForcedAbove(articulation);
                bool tabAbove = articulation.DirectionForced
                    ? articulation.IsAbove : (tabForceAbove || !tabStemUp);
                // A fret digit is centred on its string line, so a digit on the
                // OUTER string protrudes half its height past the outer line. Clear
                // that too, or an above-script (accent/staccato/fermata) lands on the
                // number instead of above it.
                double fretHalf = TabConstants.FretDigitHeight / 2.0;
                double topLine = staffOffset;
                double bottomLine = staffOffset + (strings - 1) * space;
                // A stem-coupled mark (staccato/accent/tenuto/…) may sit INSIDE the
                // tab staff, tucked just past the digit in the empty string-gap on the
                // stem's FAR side; a forced-above script (fermata/ornament/bow/stopped/…)
                // clears the whole staff. It must be the far side, though: an explicit
                // .up/.down can force the mark onto the SAME side the stem travels
                // (e.g. `@accent.down` on a top-string, stem-down note), where an inside
                // mark collides with the stem — that case clears the whole staff instead.
                // Tuning-agnostic — the string geometry drives it.
                bool insideEligible = !IsForcedAbove(articulation) && (tabAbove != tabStemUp);
                double tabY;
                if (tabAbove && isTabBeamed && tabBeamUp)
                {
                    // Beamed, stem-up: the beam floats above the digits, so an
                    // above-script must clear the BEAM's outer edge at this note's x —
                    // not just the digit — exactly like the companion notation staff.
                    tabY = TabBeamOuterEdgeY(tabBeam, geom, colX) - tabGap;
                }
                else if (!tabAbove && isTabBeamed && !tabBeamUp)
                {
                    // Beamed, stem-down: the beam hangs below the digits, so a
                    // below-script (e.g. a forced `@accent.down`) must clear the BEAM's
                    // outer (bottom) edge, not just the bottom line — otherwise it
                    // overprints the beam of its own long stem.
                    tabY = TabBeamOuterEdgeY(tabBeam, geom, colX) + tabGap;
                }
                else if (tabAbove)
                {
                    // Above the note's own TOP digit. A stem-coupled mark tucks just
                    // above that digit (inside the staff when it isn't the top string);
                    // a forced-above script clears the whole staff (clamped to the top
                    // line, so a low-string note's mark doesn't park at a phantom top
                    // digit a staff away).
                    double noteTop = geom.StringY(geom.StemHeadString(item, stemUp: true));
                    tabY = insideEligible
                        ? noteTop - fretHalf - tabGap
                        : Math.Min(noteTop - fretHalf, topLine) - tabGap;
                }
                else
                {
                    // Below the note's own BOTTOM digit — inside the staff for a
                    // stem-coupled mark (when it isn't the bottom string), else clamped
                    // to the bottom line so a forced mark clears the whole staff.
                    double noteBottom = geom.StringY(geom.StemHeadString(item, stemUp: false));
                    tabY = insideEligible
                        ? noteBottom + fretHalf + tabGap
                        : Math.Max(noteBottom + fretHalf, bottomLine) + tabGap;
                }
                // The glyph must match the side chosen HERE (the item's own
                // IsAbove was resolved with notation-staff logic).
                string tabGlyph = articulation.Type switch
                {
                    ArticulationType.Fermata =>
                        (tabAbove ? EmmentalerGlyphs.FermataAbove : EmmentalerGlyphs.FermataBelow).ToString(),
                    ArticulationType.FermataShort =>
                        (tabAbove ? EmmentalerGlyphs.FermataShortAbove : EmmentalerGlyphs.FermataShortBelow).ToString(),
                    ArticulationType.FermataLong =>
                        (tabAbove ? EmmentalerGlyphs.FermataLongAbove : EmmentalerGlyphs.FermataLongBelow).ToString(),
                    _ => articulation.GetGlyph(),
                };
                layouts.Add(new ArticulationLayout(
                    articulation.MeasureIndex, articulation.ItemIndex, colX, tabY,
                    tabGlyph, tabAbove, articulation.SourcePosition, 1.0,
                    GetSeedBBox(articulation.Type), SourceIndex: arti, StaffIndex: articulation.StaffIndex));
                continue;
            }

            // Calculate X position (centered on the note).
            // The item X is the notehead's LEFT edge and articulation glyphs are
            // origin-centred (symmetric BBox), so add the notehead's half-width to
            // land the glyph centre on the notehead centre rather than its left edge.
            // LILYPOND-REF: define-grobs.scm:2289 self-alignment-X = CENTER
            double x = measureLayout.X
                + LayoutUtilities.GetItemXOffset(artMeasures,
                    articulation.MeasureIndex, articulation.ItemIndex, measureLayout)
                + NoteheadHalfWidth(item);

            double scale = 1.0;
            if (articulation.IsEditorialAccidental)
            {
                scale = EditorialScale;
                // Accidental glyphs are anchored at the left baseline, not
                // origin-centred like script glyphs — shift so the INK centre
                // lands on the notehead centre.
                // LILYPOND-REF: define-grobs.scm:104-106 AccidentalSuggestion
                //   parent-alignment-X / self-alignment-X = CENTER
                var accBBox = GlyphMetrics.GetAccidentalBBox(
                    ArticulationItem.AccidentalKindFor(articulation.Type));
                x -= scale * (accBBox.Left + accBBox.Width / 2.0);
            }

            // Resolve the side against the BEAM-resolved stem: a stem-coupled script
            // (staccato/accent/tenuto/…) sits opposite the stem, and a beamed note's
            // stem follows the BEAM, not its own pitch — so a high note under a
            // stem-up beam takes its dot BELOW like its neighbours, not above the
            // beam. Fermata/ornament/bow keep their forced-UP side; an explicit
            // .up/.down still wins. Same rule the tab branch above already applies.
            bool forceAbove = IsForcedAbove(articulation);
            bool effectiveAbove = articulation.DirectionForced
                ? articulation.IsAbove : (forceAbove || !stemUp);
            var effArt = effectiveAbove == articulation.IsAbove
                ? articulation : articulation with { IsAbove = effectiveAbove };

            // Calculate Y position based on note position and direction, then bake
            // the staff's within-system offset (multi-staff) so the page-level
            // renderer's system-top + Y lands under THIS staff.
            // LILYPOND-REF: side-position-interface.cc:229-264 skyline calculation
            double y = CalculateYPosition(effArt, staffPosition, stemUp, item, beamedStemTipY) + staffOffset;

            // Stack multiple scripts on the same note & side outward (past the
            // previous glyph + a small padding) instead of overprinting them.
            var stackKey = (effArt.StaffIndex, effArt.MeasureIndex,
                effArt.ItemIndex, effArt.IsAbove);
            double stackDelta = stackOffset.GetValueOrDefault(stackKey, 0.0);
            y += effArt.IsAbove ? -stackDelta : stackDelta;
            var seedBBox = GetSeedBBoxFor(effArt);
            stackOffset[stackKey] = stackDelta + seedBBox.Height + ScriptStackPadding;

            layouts.Add(new ArticulationLayout(
                effArt.MeasureIndex,
                effArt.ItemIndex,
                x,
                y,
                effArt.GetGlyph(),
                effArt.IsAbove,
                effArt.SourcePosition,
                scale,
                seedBBox,
                SourceIndex: arti,
                StaffIndex: effArt.StaffIndex
            ));
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// Staff-LOCAL layouts (staff-top line at Y=0, no inter-staff offset) for a
    /// tab staff's above/below Script articulations, so the per-staff skyline that
    /// drives inter-staff spacing can reserve room for them. Without this, a tab's
    /// forced-above fermata/flageolet sits in the gap and collides with the low
    /// noteheads of the notation staff ABOVE it: the real (offset) layout is built
    /// only AFTER spacing, but the staff-local extent doesn't depend on spacing, so
    /// it can be computed here first. Mirrors the tab branch of <see cref="Calculate"/>
    /// (single source of truth for the placement geometry); only Ink and side matter
    /// for the skyline, so the glyph string is left empty and beam/multi-voice stem
    /// refinements (which don't occur in tab+articulation fixtures) are skipped.
    /// </summary>
    internal static ImmutableArray<ArticulationLayout> CalculateTabStaffLocal(
        Staff staff, int staffIndex,
        ImmutableArray<ArticulationItem> articulations,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (!staff.IsTab || !staff.Tuning.HasValue || articulations.IsDefaultOrEmpty)
            return ImmutableArray<ArticulationLayout>.Empty;

        var measures = staff.PrimaryVoice.Measures;
        int strings = Tunings.GetStringCount(staff.Tuning.Value);
        double space = EngravingDefaults.TabStringSpace(strings);
        double fretHalf = TabConstants.FretDigitHeight / 2.0;
        const double tabGap = 1.0;

        var result = ImmutableArray.CreateBuilder<ArticulationLayout>();
        foreach (var art in articulations)
        {
            if (art.StaffIndex != staffIndex)
                continue;
            // The bend family and breathing signs are placed by the early branches
            // in Calculate (at the note's own height / staff top), not as above/below
            // Scripts — so they don't reserve an inter-staff band.
            if (art.Type is ArticulationType.Fall or ArticulationType.Doit
                or ArticulationType.Bend or ArticulationType.Scoop or ArticulationType.Plop
                or ArticulationType.Breath or ArticulationType.Caesura)
                continue;

            int layoutIdx = -1;
            for (int i = 0; i < measureLayouts.Length; i++)
                if (measureLayouts[i].MeasureIndex == art.MeasureIndex) { layoutIdx = i; break; }
            if (layoutIdx < 0 || art.MeasureIndex >= measures.Length)
                continue;
            var measure = measures[art.MeasureIndex];
            if (art.ItemIndex >= measure.Items.Length)
                continue;
            var item = measure.Items[art.ItemIndex];

            int staffPosition = GetStaffPosition(item);
            bool stemUp = GetStemUp(item, staffPosition);
            bool tabForceAbove = IsForcedAbove(art);
            bool above = art.DirectionForced ? art.IsAbove : (tabForceAbove || !stemUp);

            double colX = measureLayouts[layoutIdx].X + LayoutUtilities.GetItemXOffset(
                measures, art.MeasureIndex, art.ItemIndex, measureLayouts[layoutIdx])
                + EngravingDefaults.TabHeadCenterOffset;
            double y = above
                ? -fretHalf - tabGap
                : (strings - 1) * space + fretHalf + tabGap;

            result.Add(new ArticulationLayout(
                art.MeasureIndex, art.ItemIndex, colX, y, string.Empty, above,
                art.SourcePosition, 1.0, GetSeedBBoxFor(art), StaffIndex: staffIndex));
        }
        return result.ToImmutable();
    }

    // Padding between two stacked scripts (staff-spaces).
    // LILYPOND-REF: scm/script.scm padding ~0.2.
    private const double ScriptStackPadding = 0.2;

    // LilyPond script-priority: lower = closer to the note. Only some scripts set
    // it explicitly; the rest use the Script grob default (0).
    // LILYPOND-REF: scm/script.scm; scm/define-grobs.scm Script.script-priority = 0.
    private static int ScriptPriority(ArticulationType type) => type switch
    {
        ArticulationType.Staccato => -100,
        ArticulationType.Tenuto => -50,
        ArticulationType.Fermata or ArticulationType.FermataShort
            or ArticulationType.FermataLong => 175,
        _ => 0,
    };

    /// <summary>
    /// Gets the staff position of a music item.
    /// </summary>
    private static int GetStaffPosition(MusicItem item) => item switch
    {
        NoteItem note => note.StaffPosition,
        ChordItem chord => chord.Notes.Length > 0
            ? (chord.Notes.Max(n => n.StaffPosition) + chord.Notes.Min(n => n.StaffPosition)) / 2
            : 4,
        _ => 0 // Default to middle line (StaffPosition 0 = B4 in treble clef)
    };

    /// <summary>
    /// Half the notehead's advance width, i.e. the offset from the notehead's left
    /// edge (the item X) to its horizontal centre. Picks the head glyph by note
    /// value (whole / half / black) so the script centres on the actual head.
    /// </summary>
    private static double NoteheadHalfWidth(MusicItem item)
    {
        int noteValue = item switch
        {
            NoteItem n => n.BaseDuration.Numerator == 1 ? n.BaseDuration.Denominator : 1,
            ChordItem c => c.BaseDuration.Numerator == 1 ? c.BaseDuration.Denominator : 1,
            _ => 4
        };
        double advance = noteValue switch
        {
            1 => GlyphMetrics.NoteheadWholeAdvance,
            2 => GlyphMetrics.NoteheadHalfAdvance,
            _ => GlyphMetrics.NoteheadBlackAdvance
        };
        return advance / 2.0;
    }

    /// <summary>
    /// Determines stem direction from the item.
    /// </summary>
    private static bool GetStemUp(MusicItem item, int staffPosition) => item switch
    {
        NoteItem note => note.StemUp,
        ChordItem chord => chord.StemUp,
        _ => staffPosition < 0 // Default: stem up for notes below middle line
    };

    /// <summary>
    /// Gets the glyph bounding box for an articulation type.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: mf/feta-scripts.mf set_char_box() for each script glyph
    /// </remarks>
    private static GlyphMetrics.BBox GetGlyphBBox(ArticulationType type, bool isAbove = true)
    {
        if (IsEditorialType(type))
        {
            // Scaled accidental ink box (anchored at the left baseline).
            var b = GlyphMetrics.GetAccidentalBBox(ArticulationItem.AccidentalKindFor(type));
            return new GlyphMetrics.BBox(
                b.Left * EditorialScale, b.Bottom * EditorialScale,
                b.Right * EditorialScale, b.Top * EditorialScale);
        }

        return type switch
        {
            ArticulationType.Staccato => GlyphMetrics.ArticStaccato,
            ArticulationType.Accent => GlyphMetrics.ArticAccent,
            ArticulationType.Tenuto => GlyphMetrics.ArticTenuto,
            ArticulationType.Marcato => isAbove
                ? GlyphMetrics.ArticMarcatoAbove : GlyphMetrics.ArticMarcatoBelow,
            ArticulationType.Fermata or ArticulationType.FermataShort or ArticulationType.FermataLong => isAbove
                ? GlyphMetrics.FermataAboveGlyph : GlyphMetrics.FermataBelowGlyph,
            ArticulationType.Staccatissimo => isAbove
                ? GlyphMetrics.ArticStaccatissimoAboveGlyph : GlyphMetrics.ArticStaccatissimoBelowGlyph,
            ArticulationType.UpBow => GlyphMetrics.ArticUpBowGlyph,
            ArticulationType.DownBow => GlyphMetrics.ArticDownBowGlyph,
            ArticulationType.Flageolet => GlyphMetrics.ArticFlageoletGlyph,
            // Chord diagram: anchored at the grid bottom, ink rises 2.7.
            ArticulationType.FretFrame => new GlyphMetrics.BBox(-1.7, 0, 2.9, 2.7),
            _ => new GlyphMetrics.BBox(-0.5, -0.5, 0.5, 0.5) // fallback for the ornament family
        };
    }

    /// <summary>Editorial (suggestion) accidental types.</summary>
    private static bool IsEditorialType(ArticulationType type) => type
        is ArticulationType.EditorialSharp or ArticulationType.EditorialFlat
        or ArticulationType.EditorialNatural or ArticulationType.EditorialDoubleSharp
        or ArticulationType.EditorialDoubleFlat;

    /// <summary>
    /// Ink box used to seed the outside-staff occupancy (so movable grobs —
    /// rehearsal/section marks etc. — clear the scripts). Uses the real font
    /// metrics for the ornament glyphs (extracted from Emmentaler via
    /// audit/scripts/Extract-EmmentalerMetrics.py), which are much wider/taller
    /// than the 0.5×0.5 positioning fallback — e.g. prall-prall spans ~2.85sp
    /// wide. The ornaments' own POSITIONING still uses the simplified extents
    /// (GetGlyphBBox / GetArticulationExtent), exactly as the trill does; only
    /// the occupancy a mark must clear changes. Other types fall back.
    /// LILYPOND-REF: mf/feta-scripts.mf set_char_box() for each script glyph.
    /// </summary>
    /// <summary>Real ink box of a chord diagram, anchored at the GRID BOTTOM
    /// centre: 4 fret rows up (2.0), the o/x header above them (0.7), half the
    /// string span each side plus the Nfr side-label allowance.</summary>
    private static GlyphMetrics.BBox FrameBox(string? spec)
    {
        int strings = Math.Max(4, spec?.Length ?? 6);
        double halfW = (strings - 1) * 0.55 / 2 + 0.3;
        return new GlyphMetrics.BBox(-halfW, 0, halfW + 1.2, 2.7);
    }

    /// <summary>Seed box for THIS articulation instance — frame boxes depend
    /// on the spec, everything else on the type alone.</summary>
    private static GlyphMetrics.BBox GetSeedBBoxFor(ArticulationItem articulation) =>
        articulation.Type == ArticulationType.FretFrame
            ? FrameBox(articulation.FrameSpec)
            : GetSeedBBox(articulation.Type, articulation.IsAbove);

    private static GlyphMetrics.BBox GetSeedBBox(ArticulationType type, bool isAbove = true) => type switch
    {
        ArticulationType.Trill => GlyphMetrics.OrnTrillGlyph,
        ArticulationType.Turn => GlyphMetrics.OrnTurnGlyph,
        ArticulationType.InvertedTurn => GlyphMetrics.OrnReverseTurnGlyph,
        ArticulationType.Prall => GlyphMetrics.OrnPrallGlyph,
        ArticulationType.Mordent => GlyphMetrics.OrnMordentGlyph,
        ArticulationType.PrallTriller => GlyphMetrics.OrnPrallPrallGlyph,
        _ => GetGlyphBBox(type, isAbove)
    };

    /// <summary>
    /// Gets the full vertical extent (total height) of an articulation glyph.
    /// Used for non-quantized articulations in the extent+padding positioning model.
    /// </summary>
    private static double GetArticulationExtent(ArticulationType type)
    {
        var bbox = GetGlyphBBox(type);
        return type switch
        {
            // Fermata and ornaments: use larger values since glyph BBox not defined
            ArticulationType.Fermata or ArticulationType.FermataShort or ArticulationType.FermataLong => 1.5,
            ArticulationType.Trill or ArticulationType.Mordent or ArticulationType.Prall
                or ArticulationType.Turn or ArticulationType.InvertedTurn
                or ArticulationType.PrallTriller => 1.0,
            _ => bbox.Height
        };
    }

    /// <summary>
    /// Gets the vertical extent of the glyph in the direction toward the note
    /// (the "near side" extent used in skyline distance calculation).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: side-position-interface.cc:229-264 my_dim skyline is the articulation's
    /// extent in the -dir direction (toward the support/note).
    ///
    /// For symmetric glyphs (staccato, tenuto): near extent = half height.
    /// For asymmetric glyphs (marcato): near extent = 0 (tip points toward note).
    /// </remarks>
    /// <summary>All fermata shapes (normal / short-angled / long-square)
    /// share direction-UP and placement behaviour.</summary>
    private static bool IsFermata(ArticulationType t) =>
        t is ArticulationType.Fermata or ArticulationType.FermataShort or ArticulationType.FermataLong;

    /// <summary>Scripts LilyPond gives direction UP regardless of the stem, so they
    /// sit ABOVE the note (or above the beam) rather than opposite the stem: fermatas,
    /// ornaments, editorial accidentals, bows, flageolet, and the technique marks
    /// stopped (+) / heel / toe / snap-pizzicato. Everything else is stem-coupled and
    /// takes the side opposite the (beam-resolved) stem.
    /// LILYPOND-REF: scm/script.scm — these entries carry (direction . UP).</summary>
    private static bool IsForcedAbove(ArticulationItem a) =>
        IsFermata(a.Type) || a.IsOrnament || a.IsEditorialAccidental
        || a.Type is ArticulationType.UpBow or ArticulationType.DownBow
            or ArticulationType.Flageolet or ArticulationType.Stopped
            or ArticulationType.Heel or ArticulationType.Toe or ArticulationType.SnapPizz;

    private static double GetNearExtent(ArticulationType type, bool isAbove)
    {
        var bbox = GetGlyphBBox(type, isAbove);
        // "Near extent" = how far the glyph extends toward the note from its reference point.
        // For above placement: the glyph's bottom extent (positive = extends downward toward note)
        // For below placement: the glyph's top extent (positive = extends upward toward note)
        return isAbove ? -bbox.Bottom : bbox.Top;
    }

    /// <summary>
    /// Beam-quanted stem tips by (staff, measure, item) in staff-local device Y,
    /// plus the beam-resolved stem direction. A beamed stem ends on the beam
    /// line — the unbeamed length formula under- or over-clears it — so the
    /// script support must read the quanted end.
    /// Sub-voice beams (VoiceIndex &gt; 0) are excluded: an articulation only
    /// carries measure/item indices, which are resolved against the PRIMARY
    /// voice, so a sub-voice key could collide with a primary-voice item.
    /// LILYPOND-REF: lily/stem.cc — a beamed stem's end comes from the beam;
    /// side-position then sees that real extent via the stem support.
    /// </summary>
    /// <summary>
    /// Maps each primary-voice beamed item to its beam group, so the tab branch can
    /// find the group's outer beam edge (the tab beam Y lives only in the renderer's
    /// geometry, recomputed here from the group's members via TabStaffGeometry).
    /// </summary>
    private static Dictionary<(int Staff, int Measure, int Item), BeamLayout>
        BuildBeamGroupMap(ImmutableArray<BeamLayout> beamLayouts)
    {
        var map = new Dictionary<(int, int, int), BeamLayout>();
        if (beamLayouts.IsDefaultOrEmpty)
            return map;
        foreach (var beam in beamLayouts)
        {
            var group = beam.Group;
            if (group.VoiceIndex != 0)
                continue;
            for (int i = 0; i < group.Members.Length; i++)
            {
                var member = group.Members[i];
                int staff = !beam.MemberStaffIndices.IsDefaultOrEmpty && i < beam.MemberStaffIndices.Length
                    ? beam.MemberStaffIndices[i]
                    : Math.Max(0, beam.StaffIndex);
                map[(staff, member.ResolveMeasureIndex(group.MeasureIndex), member.ItemIndex)] = beam;
            }
        }
        return map;
    }

    /// <summary>
    /// Device-Y of a tab beam's OUTER edge at <paramref name="noteX"/> — the top edge
    /// for a stem-up beam (above the digits), the bottom edge for a stem-down beam
    /// (below them) — i.e. the value a script on the beam's side must clear.
    /// Recomputes the SAME quanted beam line the renderer draws
    /// (<see cref="TabBeamQuant"/>), so the two agree.
    /// </summary>
    internal static double TabBeamOuterEdgeY(BeamLayout beam, TabStaffGeometry geom, double noteX)
    {
        // A tab beam's direction is string-based, not the notation pitch direction.
        bool up = geom.GroupStemUp(beam.Group.Members.Select(m => m.Item));
        int n = beam.Group.Members.Length;
        double attach = up ? EngravingDefaults.StemUpAttachX : EngravingDefaults.StemDownAttachX;
        var xs = new double[n];
        for (int i = 0; i < n; i++)
            xs[i] = (i < beam.MemberXPositions.Length ? beam.MemberXPositions[i] : 0) + attach;
        var line = TabBeamQuant.Compute(beam.Group, xs, geom, up);
        double half = EngravingDefaults.BeamThickness / 2;
        return TabBeamMath.At(line, noteX) + (up ? -half : half);
    }

    private static Dictionary<(int Staff, int Measure, int Item), (double TipY, bool StemUp)>
        BuildBeamedStemTips(ImmutableArray<BeamLayout> beamLayouts)
    {
        var tips = new Dictionary<(int, int, int), (double, bool)>();
        if (beamLayouts.IsDefaultOrEmpty)
            return tips;
        foreach (var beam in beamLayouts)
        {
            var group = beam.Group;
            if (group.VoiceIndex != 0)
                continue;
            for (int i = 0; i < group.Members.Length && i < beam.MemberXPositions.Length; i++)
            {
                var member = group.Members[i];
                // Beam Y at this member's X: LeftY/RightY are staff positions
                // (half-spaces, up-positive from the middle line).
                double t = beam.RightX > beam.LeftX
                    ? (beam.MemberXPositions[i] - beam.LeftX) / (beam.RightX - beam.LeftX)
                    : 0;
                double positionAtX = beam.LeftY + t * (beam.RightY - beam.LeftY);
                double tipY = StaffMiddle - positionAtX * 0.5;
                // The beam has thickness; a script on the beam's side must clear its
                // OUTER edge, not the stem end (= beam centre). Push the tip out by
                // half the beam thickness so side-position sees the whole beam.
                // LILYPOND-REF: the Beam grob's stencil joins the script's support skyline.
                tipY += member.MemberStemUp
                    ? -EngravingDefaults.BeamThickness / 2 : EngravingDefaults.BeamThickness / 2;
                int staff = !beam.MemberStaffIndices.IsDefaultOrEmpty
                    ? beam.MemberStaffIndices[i]
                    : Math.Max(0, beam.StaffIndex);
                tips[(staff, member.ResolveMeasureIndex(group.MeasureIndex), member.ItemIndex)] =
                    (tipY, member.MemberStemUp);
            }
        }
        return tips;
    }

    /// <summary>
    /// The ink box a wide, always-outside script (fermata family / ornament)
    /// reserves in the note-SPACING skyline, or null for every other script.
    /// The frame matches <see cref="ItemSkylineFactory"/>: the note column sits
    /// at X = 0, <paramref name="staffY"/> is the middle line and Y increases
    /// DOWNWARD. A fermata is ~1.33 sp wide — ~0.68 past the head — so its glyph
    /// crowds a neighbouring column the note head alone never reaches; LilyPond
    /// reserves that because the Script grob joins the note column's horizontal
    /// skyline. Narrow scripts (staccato/accent/tenuto/marcato), bends and
    /// breaths never protrude far enough to matter, so they are left out (null)
    /// to keep the reservation — and the fixtures it moves — to the real cases.
    /// The Y is the UNBEAMED placement: at spacing time the beam-quanted stem tip
    /// is not visible, and the collision case is a HIGH note whose stem points
    /// AWAY from the script, where note-head support already gives the exact Y.
    /// LILYPOND-REF: lily/separation-item.cc — every grob in a note column
    ///   (Script included) contributes to the column's horizontal skyline.
    /// </summary>
    internal static (double YBottom, double YTop, double XLeft, double XRight)? SpacingInkBox(
        ArticulationItem articulation, MusicItem item, double staffY)
    {
        if (!(IsFermata(articulation.Type) || articulation.IsOrnament))
            return null;

        int staffPosition = GetStaffPosition(item);
        bool stemUp = GetStemUp(item, staffPosition);

        // The side CalculateYPosition will resolve to (fermata/ornament force UP
        // unless an explicit .down overrides), so the glyph box matches the side.
        bool isAbove = articulation.DirectionForced ? articulation.IsAbove : true;

        double anchorEng = CalculateYPosition(articulation, staffPosition, stemUp, item,
            beamedStemTipY: null);
        // Engraver Y is measured from the staff top (middle line at StaffMiddle);
        // shift into the skyline's middle-line-at-staffY frame.
        double anchorSky = anchorEng - StaffMiddle + staffY;

        // Glyph ink box (font frame, Y up); map about the anchor into Y-down.
        var bbox = GetSeedBBox(articulation.Type, isAbove);
        double yBottom = anchorSky - bbox.Top;
        double yTop = anchorSky - bbox.Bottom;
        return (yBottom, yTop, bbox.Left, bbox.Right);
    }

    private static double CalculateYPosition(ArticulationItem articulation, int staffPosition, bool stemUp,
        MusicItem? item = null, double? beamedStemTipY = null)
    {
        // LILYPOND-REF: define-grobs.scm:1365 fermata: direction = UP
        // LILYPOND-REF: define-grobs.scm:2175 TrillSpanner: direction = UP
        // LILYPOND-REF: define-grobs.scm:100 AccidentalSuggestion: direction = UP
        // LILYPOND-REF: scm/script.scm — upbow/downbow/flageolet: direction = UP
        bool forceAbove = IsForcedAbove(articulation);
        // An explicit .up/.down wins over the default UP direction (e.g. \fermata.down).
        bool isAbove = articulation.DirectionForced
            ? articulation.IsAbove : (forceAbove || articulation.IsAbove);

        // Anchor on the chord's extreme head on the SCRIPT's side: the TOP
        // head when above, the BOTTOM head when below. The chord-midpoint
        // anchor parked a fermata straight on a tall chord's top notehead —
        // the staff-padding clamp below only guards against the staff, not
        // against heads on ledger lines above it.
        // LILYPOND-REF: lily/script-engraver.cc:234-250
        //   acknowledge_rhythmic_head — EVERY head of the chord becomes a
        //   side-position support, so the UP side clears the highest head
        //   (the note column at :253-268 only becomes the X-parent);
        //   :181-192 acknowledge_stem — the stem is a support too, which the
        //   supportExtent stem-length term below approximates.
        int anchorPosition = item is ChordItem anchorChord && anchorChord.Notes.Length > 0
            ? (isAbove
                ? anchorChord.Notes.Max(n => n.StaffPosition)
                : anchorChord.Notes.Min(n => n.StaffPosition))
            : staffPosition;

        // Convert staff position to Y coordinate (staff spaces from top).
        // StaffPosition: 0 = middle line, positive = up, negative = down.
        // Canonical formula used by note rendering: Y = StaffMiddle - StaffPosition * 0.5
        // LILYPOND-REF: staff-symbol-referencer.cc:76-89 staff_symbol_referencer::get_position
        double noteY = StaffMiddle - anchorPosition * 0.5;

        // Use quantize-position for staccato, marcato, tenuto
        // LILYPOND-REF: scm/script.scm staccato/marcato/tenuto: (quantize-position . #t)
        if (ShouldQuantize(articulation.Type))
        {
            return QuantizedYPosition(noteY, isAbove, stemUp, articulation.Type, item, anchorPosition,
                beamedStemTipY);
        }

        // Non-quantized path: fermata, ornaments, accent, portato
        // LILYPOND-REF: side-position-interface.cc:360-378 total_off calculation
        // LILYPOND-REF: side-position-interface.cc:426-445 staff-padding clamp
        //
        // include_staff = true (staff-padding exists AND quantize-position = false)
        // The staff is included in the support skyline, then staff-padding is applied.

        double glyphNearExtent = GetNearExtent(articulation.Type, isAbove);
        double supportExtent = isAbove
            ? (stemUp ? StemSupportExtent(item, anchorPosition, noteY, stemUp: true, beamedStemTipY) : NoteheadHalfHeight)
            : (!stemUp ? StemSupportExtent(item, anchorPosition, noteY, stemUp: false, beamedStemTipY) : NoteheadHalfHeight);

        // dist = skyline distance; total_off = dist + padding
        double totalOff = supportExtent + glyphNearExtent + PaddingFor(articulation.Type);
        double targetY = isAbove ? noteY - totalOff : noteY + totalOff;

        if (isAbove)
        {
            // LILYPOND-REF: side-position-interface.cc:426-445 staff-padding clamp
            // Ensure the glyph's bottom edge clears the staff top by staff-padding
            double glyphBottom = targetY + glyphNearExtent; // glyph's edge toward staff
            double staffEdge = StaffTop - StaffPadding;
            if (glyphBottom > staffEdge)
                targetY = staffEdge - glyphNearExtent;
            return targetY;
        }
        else
        {
            // Ensure the glyph's top edge clears the staff bottom by staff-padding
            double glyphTop = targetY - glyphNearExtent; // glyph's edge toward staff
            double staffEdge = StaffBottom + StaffPadding;
            if (glyphTop < staffEdge)
                targetY = staffEdge + glyphNearExtent;
            return targetY;
        }
    }

    /// <summary>
    /// Returns true for articulation types that use LilyPond's quantize-position algorithm.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/script.scm — these scripts have (quantize-position . #t)
    /// </remarks>
    private static bool ShouldQuantize(ArticulationType type) => type switch
    {
        ArticulationType.Staccato => true,
        ArticulationType.Marcato => true,
        ArticulationType.Tenuto => true,
        _ => false
    };

    /// <summary>
    /// Calculates Y position using LilyPond's quantize-position algorithm.
    /// Follows the aligned_side() flow from side-position-interface.cc:
    ///   1. Calculate skyline distance (support extent + glyph extent)
    ///   2. Add padding to get total_off
    ///   3. Convert to LP staff position and apply quantize-position
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: side-position-interface.cc:193-448 aligned_side() full flow
    /// LILYPOND-REF: side-position-interface.cc:360-378 total_off = dir * dist + dir * ss * padding
    /// LILYPOND-REF: side-position-interface.cc:402-425 quantize-position
    /// LILYPOND-REF: misc.cc directed_round() — ceil for UP, floor for DOWN
    ///
    /// LP staff positions for 5-line staff:
    ///   Lines: -4 (bottom), -2, 0 (middle), 2, 4 (top)
    ///   Spaces: -5, -3, -1, 1, 3, 5
    ///
    /// Conversion: lpPos = (StaffMiddle - Y) * 2;  Y = StaffMiddle - lpPos / 2
    /// </remarks>
    /// <summary>
    /// The stem's contribution to the side-position support: the distance from
    /// the anchor head (the stem-tip-side head) to the REAL stem tip, computed
    /// by the same rule the renderer draws stems with (duration-based lengths,
    /// unnatural-direction shortening, middle-line pull). The previous constant
    /// 3.5 over-cleared shortened stems and pretended stemless whole notes had
    /// a stem. Beamed stems still use the unbeamed rule (the beam-quanted end
    /// lives in beam layout, not visible from here) — close, not exact.
    /// LILYPOND-REF: lily/stem.cc:415-523 internal_calc_stem_end_position via
    /// StemCalculator; side-position supports carry the stem's real extent.
    /// </summary>
    private static double StemSupportExtent(MusicItem? item, int anchorPosition, double anchorY, bool stemUp,
        double? beamedStemTipY = null)
    {
        // A beamed stem's end is the beam-quanted line, already resolved by
        // beam layout — trust it over the unbeamed formula.
        if (beamedStemTipY is { } beamTip)
            return Math.Abs(anchorY - beamTip);

        int denominator = item switch
        {
            NoteItem n when n.BaseDuration.Numerator == 1 => n.BaseDuration.Denominator,
            ChordItem c when c.BaseDuration.Numerator == 1 => c.BaseDuration.Denominator,
            _ => 0
        };
        if (item == null)
            return DefaultStemLength;   // legacy callers without an item: old behaviour
        if (denominator < 2)
            return NoteheadHalfHeight;  // whole note / breve: no stem to clear

        int durLog = StemCalculator.GetDurationLog(denominator);
        double tipY = StemCalculator.CalculateStemEndY(
            anchorY, stemUp, StaffTop, durLog, anchorPosition);
        return Math.Abs(anchorY - tipY);
    }

    private static double QuantizedYPosition(double noteY, bool isAbove, bool stemUp, ArticulationType type,
        MusicItem? item = null, int anchorPosition = 0, double? beamedStemTipY = null)
    {
        // ── Stage 4-5 (aligned_side): Calculate total_off ──
        //
        // LILYPOND-REF: side-position-interface.cc:266-328 build support skylines
        // The support skyline for a Script grob is the notehead (+ stem if same direction).
        // Stems pointing AWAY from the articulation are skipped:
        //   LILYPOND-REF: side-position-interface.cc:279-284
        //   if (dir == -get_grob_direction(e)) continue;
        //
        // For staccato (side-relative-direction = DOWN):
        //   stem UP → staccato dir=DOWN → stem dir=UP → dir != -stem_dir → stem SKIPPED
        //   stem DOWN → staccato dir=UP → stem dir=DOWN → dir != -stem_dir → stem SKIPPED
        // In both normal cases, only the notehead is in the support.
        // Stem is only included when direction is forced (e.g., fermata above with stem up).

        double supportExtent; // Support (notehead/stem) extent in the direction of placement
        if (isAbove)
        {
            // For above: support's UP extent (top of notehead, or stem tip if stem goes up)
            // Stem is included only when stem direction matches placement direction
            supportExtent = stemUp
                ? StemSupportExtent(item, anchorPosition, noteY, stemUp: true, beamedStemTipY)
                : NoteheadHalfHeight;
            // ↑ if stemUp AND isAbove: stem IS in support (forced above case), real stem tip
            // ↑ if !stemUp AND isAbove: stem skipped, just notehead top = 0.5
        }
        else
        {
            // For below: support's DOWN extent
            supportExtent = !stemUp
                ? StemSupportExtent(item, anchorPosition, noteY, stemUp: false, beamedStemTipY)
                : NoteheadHalfHeight;
            // ↑ if !stemUp AND !isAbove: stem IS in support (forced below case), real stem tip
            // ↑ if stemUp AND !isAbove: stem skipped, just notehead bottom = 0.5
        }

        // LILYPOND-REF: side-position-interface.cc:229-264 my_dim skyline (-dir direction)
        // The glyph's "near extent" = how far it extends toward the note from its reference point
        double glyphNearExtent = GetNearExtent(type, isAbove);

        // LILYPOND-REF: side-position-interface.cc:360-365
        // dist = dim.distance(my_dim, horizon_padding)
        // For simple bounding boxes: dist = supportExtent + glyphNearExtent
        double dist = supportExtent + glyphNearExtent;

        // LILYPOND-REF: side-position-interface.cc:366-370
        // total_off = dir * dist + dir * ss * padding
        // (ss = staff_space = 1.0 in our coordinate system)
        double totalOff = dist + PaddingFor(type);

        // Convert total_off to target Y position
        double targetY = isAbove ? noteY - totalOff : noteY + totalOff;

        // ── Stage 7 (aligned_side): Apply quantize-position ──
        //
        // LILYPOND-REF: side-position-interface.cc:402-425
        // Note: include_staff = false when quantize-position = true (line 222-226)
        // So staff-padding is NOT applied before quantization.

        // Convert to LP staff position
        // LP: 0 = middle line (our Y=2.0), positive = up, negative = down
        double lpPosition = (StaffMiddle - targetY) * 2.0;

        // Directed round (away from the note)
        // LILYPOND-REF: misc.cc directed_round(): ceil for UP, floor for DOWN
        double rounded = isAbove ? Math.Ceiling(lpPosition) : Math.Floor(lpPosition);

        // Check if quantization applies
        // LILYPOND-REF: side-position-interface.cc:414-424
        // Staff line span for 5-line staff: [-4, 4], widened by 1: [-5, 5]
        const double StaffSpanMin = -5.0;
        const double StaffSpanMax = 5.0;
        bool inStaffSpan = lpPosition >= StaffSpanMin && lpPosition <= StaffSpanMax;
        // LILYPOND-REF: side-position-interface.cc:418
        // has_interface<Note_head>(head) && dir * position < 0
        // Articulation is between note and staff center (ledger line note case)
        bool betweenNoteAndStaff = isAbove ? lpPosition < 0 : lpPosition > 0;

        if (inStaffSpan || betweenNoteAndStaff)
        {
            // LILYPOND-REF: side-position-interface.cc:420
            // total_off += (rounded - position) * 0.5 * ss;
            // Equivalent: snap targetY to the rounded LP position
            targetY = StaffMiddle - rounded / 2.0;

            // LILYPOND-REF: side-position-interface.cc:421-422
            // if (Staff_symbol_referencer::on_line(me, int(rounded)))
            //     total_off += dir * 0.5 * ss;
            // Even LP positions within staff lines [−4, 4] are on lines
            int roundedInt = (int)rounded;
            if (roundedInt >= -4 && roundedInt <= 4 && roundedInt % 2 == 0)
            {
                targetY += isAbove ? -0.5 : 0.5;
            }
        }

        return targetY;
    }
}
