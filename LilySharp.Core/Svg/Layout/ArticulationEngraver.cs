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
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for an articulation mark.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: define-grobs.scm:2992-3024 Script grob
/// LILYPOND-REF: script-interface.cc positioning logic
/// </remarks>
internal readonly record struct ArticulationLayout(
    int MeasureIndex,       // Measure containing this articulation
    int ItemIndex,          // Item index within measure (for X alignment)
    double X,               // Absolute X position (staff spaces from score start)
    double YUp,             // Y in the LilyPond-native Y-up frame: staff-spaces ABOVE
                            // this script's staff middle line, up-positive (frame B).
                            // The renderer/skyline reflect it to device (middle − Y-up)
                            // against the staff middle they resolve.
    string Glyph,           // SMuFL glyph to render
    bool IsAbove,           // Whether placed above the note
    int SourcePosition,     // For click-to-source mapping
    // The font-size this grob STATES, in LilyPond's sixths of an octave (0 = the score's
    // own, an editorial accidental −2). It decides BOTH the magnification and WHICH
    // Emmentaler design is read and drawn — see EmmentalerDesignSize / GlyphMetrics.
    // AtFontSize. It replaced a bare Scale on 2026-08-02: a scale cannot say which design.
    double FontSizeStep = 0.0,
    GlyphMetrics.BBox Ink = default, // Ink box relative to the anchor (for skyline seeding)
    int SourceIndex = -1,   // F3/B: index into score.Articulations (data-pos resolved at render)
    int StaffIndex = 0,     // Which staff this script sits on (per-staff below-staff seeding)
    int? OutsideStaffPriority = null // The script's DECLARED outside-staff-priority (the
                            // fermata family's 75), or null for the scripts that declare
                            // none — LilyPond's #f, which is not a zero (a grob declaring 0
                            // would be the first MOVER placed). Baked from the type by the
                            // engraver, the way LilyPond resolves a Script's properties out
                            // of scm/script.scm: a script WITH a priority is a mover in the
                            // outside-staff collision pass, one without seeds the occupancy
                            // the movers clear. See ArticulationSpacing.OutsideStaffPriority.
);

/// <summary>
/// Calculates positions for articulation marks.
/// Implements LilyPond's articulation positioning algorithm.
/// </summary>
/// <remarks>
/// LILYPOND-REF: script-engraver.cc:235-250 Script_engraver::acknowledge_rhythmic_head
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

    // LILYPOND-REF: define-grobs.scm:3004 staff-padding = 0.25
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

    // Editorial (suggestion) accidentals print at font-size -2.
    // LILYPOND-REF: scm/define-grobs.scm:101 AccidentalSuggestion (font-size . -2)
    private const double EditorialFontSizeStep = -2.0;

    /// <summary>What that font-size multiplies a length by — <c>magstep(-2)</c>.</summary>
    /// <remarks>
    /// It was written down as 0.7937 until 2026-08-02, four digits of magstep(-2) =
    /// 0.79370053. A rounded scale inside a placement law is the same defect the grace's
    /// 0.65 was (see EmmentalerDesignSize.Magstep).
    /// </remarks>
    private static readonly double EditorialScale =
        EmmentalerDesignSize.Magstep(EditorialFontSizeStep);

    /// <summary>
    /// The FONT an editorial accidental reads: the design font-size −2 selects (the 16),
    /// already magnified. Nothing read out of it is multiplied again.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/font-select.cc:115-186 select_font — Emmentaler is optically
    ///   sized, so this is NOT the 20's box scaled: the 16 design's own sharp is drawn
    ///   wider in its own staff spaces. See GlyphMetrics.AtFontSize.
    /// </remarks>
    private static GlyphMetrics.DesignMetrics EditorialFont
        => GlyphMetrics.AtFontSize(EditorialFontSizeStep);

    // Staff middle line position (see EngravingDefaults.StaffMiddle).
    private const double StaffMiddle = EngravingDefaults.StaffMiddle;

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
            // LILYPOND-REF: script-engraver.cc:235-250 acknowledge_rhythmic_head
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
                double fx, fyUp;
                Staff? tabBendStaff = null;
                if (staffByIndex != null
                    && staffByIndex.TryGetValue(articulation.StaffIndex, out var ts)
                    && ts.IsTab && ts.Tuning.HasValue)
                    tabBendStaff = ts;
                // The fret digit sits a TabHeadCenterOffset right of the note column
                // (see EngravingDefaults) — the gesture hangs off the digit, not the column.
                double noteX = tabBendStaff != null ? itemX + EngravingDefaults.TabHeadCenterOffset : itemX;
                if (tabBendStaff is { Tuning: { } tt })
                {
                    int strings = Tunings.GetStringCount(tt);
                    double space = EngravingDefaults.TabStringSpace(strings);
                    int midi = item switch { NoteItem n => n.Midi,
                        ChordItem c when c.Notes.Length > 0 => c.Notes[0].Midi, _ => 0 };
                    int? sn = item is NoteItem ni ? ni.StringNumber : null;
                    var (strNum, _) = Tunings.CalculateFret(
                        midi + Tunings.SoundingShift(tabBendStaff.TabSourceClef, tabBendStaff.Transposition),
                        Tunings.GetTuning(tt), sn ?? 0);
                    fx = noteX + BendTabXOffset;
                    // Y-up: the string row sits (strNum−1)·space below the (notation)
                    // staff middle. No staff offset — resolved at draw time.
                    fyUp = StaffMiddle - (strNum - 1) * space;
                }
                else
                {
                    fx = noteX + 2.0 * NoteheadHalfWidth(item) + BendHeadPadding;
                    // Y-up: the gesture hangs at the note's own staff position (pos/2).
                    fyUp = GetStaffPosition(item) * 0.5;
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
                    articulation.MeasureIndex, articulation.ItemIndex, fx, fyUp,
                    bendGlyph, true, articulation.SourcePosition, FontSizeStep: 0.0,
                    SourceIndex: arti, StaffIndex: articulation.StaffIndex));
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
                    + 2.0 * NoteheadHalfWidth(item)  // twice the half-extent → the head's right edge
                    + BreathGap;
                // Y-up: BreathStaffY is a device staff-top offset; reflect to Y-up
                // about the staff middle. No staff offset — resolved at draw time.
                double byUp = StaffMiddle - BreathStaffY;
                layouts.Add(new ArticulationLayout(
                    articulation.MeasureIndex,
                    articulation.ItemIndex,
                    bx,
                    byUp,
                    articulation.GetGlyph(),
                    true,
                    articulation.SourcePosition,
                    FontSizeStep: 0.0,
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
            // Only inside the voice { } span, though — outside it voice 1 is the
            // only voice and keeps its pitch-natural direction.
            // LILYPOND-REF: scm/music-functions.scm:1042-1057 voicify-sublist / make-voice-props-set
            if (staffByIndex != null
                && staffByIndex.TryGetValue(articulation.StaffIndex, out var ownStaff)
                && VoiceDefaults.GetDefaultStemUpAt(
                    ownStaff.Voices, 0, articulation.MeasureIndex) is { } voiceStemUp)
                stemUp = voiceStemUp;

            // A beamed member's stem ends on the BEAM, not at the unbeamed
            // formula's tip, and the beam also resolves its direction.
            BeamLayout? memberBeam = null;
            double memberStemX = 0.0;
            if (beamedTips.TryGetValue(
                (articulation.StaffIndex, articulation.MeasureIndex, articulation.ItemIndex),
                out var beamTip))
            {
                memberBeam = beamTip.Beam;
                memberStemX = beamTip.MemberX;
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
                bool tabBeamUp = tabBeam is not null
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
                // `tabBeam is not null` is equivalent to isTabBeamed here (the
                // TryGetValue out), but stated this way it narrows tabBeam to
                // non-null for the TabBeamOuterEdgeY call inside.
                if (tabAbove && tabBeam is not null && tabBeamUp)
                {
                    // Beamed, stem-up: the beam floats above the digits, so an
                    // above-script must clear the BEAM's outer edge at this note's x —
                    // not just the digit — exactly like the companion notation staff.
                    tabY = TabBeamOuterEdgeY(tabBeam, geom, colX) - tabGap;
                }
                else if (!tabAbove && tabBeam is not null && !tabBeamUp)
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
                // tabY is device with the staff offset baked in (topLine/bottomLine/
                // geom all carry it); reflect to Y-up about that staff's middle so the
                // stored value is offset-free (StaffMiddle + staffOffset as the mirror).
                double tabYUp = (StaffMiddle + staffOffset) - tabY;
                layouts.Add(new ArticulationLayout(
                    articulation.MeasureIndex, articulation.ItemIndex, colX, tabYUp,
                    tabGlyph, tabAbove, articulation.SourcePosition, FontSizeStep: 0.0,
                    GetSeedBBox(articulation.Type), SourceIndex: arti, StaffIndex: articulation.StaffIndex));
                continue;
            }

            // Calculate X position (centered on the note).
            // The item X is the notehead's LEFT edge and articulation glyphs are
            // origin-centred (symmetric BBox), so add the notehead's half-width to
            // land the glyph centre on the notehead centre rather than its left edge.
            // LILYPOND-REF: define-grobs.scm:3001 self-alignment-X = CENTER
            double x = measureLayout.X
                + LayoutUtilities.GetItemXOffset(artMeasures,
                    articulation.MeasureIndex, articulation.ItemIndex, measureLayout)
                + NoteheadHalfWidth(item);

            double fontSizeStep = 0.0;
            if (articulation.IsEditorialAccidental)
            {
                fontSizeStep = EditorialFontSizeStep;
                // Accidental glyphs are anchored at the left baseline, not
                // origin-centred like script glyphs — shift so the INK centre
                // lands on the notehead centre.
                // LILYPOND-REF: define-grobs.scm:104-106 AccidentalSuggestion
                //   parent-alignment-X / self-alignment-X = CENTER
                // The box comes from the font this grob's font-size selected, already at
                // that size — so there is no scale factor on this line any more.
                var accBBox = GlyphMetrics.GetAccidentalBBox(
                    EditorialFont, ArticulationItem.AccidentalKindFor(articulation.Type));
                x -= accBBox.Left + accBBox.Width / 2.0;
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

            // Y-up placement (staff-spaces above the staff middle). No staff offset:
            // the renderer/skyline resolve the staff middle at their own boundary.
            // LILYPOND-REF: side-position-interface.cc:229-264 skyline calculation
            double yUp = CalculateYPosition(effArt, staffPosition, stemUp, item,
                NoteColumnLayout.Of(item, stemUp, memberBeam, memberStemX));

            // Stack multiple scripts on the same note & side OUTWARD (past the
            // previous glyph + a small padding) instead of overprinting them —
            // outward is up (+) for above, down (−) for below in the Y-up frame.
            var stackKey = (effArt.StaffIndex, effArt.MeasureIndex,
                effArt.ItemIndex, effArt.IsAbove);
            double stackDelta = stackOffset.GetValueOrDefault(stackKey, 0.0);
            yUp += effArt.IsAbove ? stackDelta : -stackDelta;
            var seedBBox = GetSeedBBoxFor(effArt);
            stackOffset[stackKey] = stackDelta + seedBBox.Height + ScriptStackPadding;

            layouts.Add(new ArticulationLayout(
                effArt.MeasureIndex,
                effArt.ItemIndex,
                x,
                yUp,
                effArt.GetGlyph(),
                effArt.IsAbove,
                effArt.SourcePosition,
                fontSizeStep,
                seedBBox,
                SourceIndex: arti,
                StaffIndex: effArt.StaffIndex,
                OutsideStaffPriority: ArticulationSpacing.OutsideStaffPriority(effArt.Type)
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
            // Staff-local device (staff top = 0, Y down) → Y-up about the staff middle.
            double yUp = StaffMiddle - (
                above
                    ? -fretHalf - tabGap
                    : (strings - 1) * space + fretHalf + tabGap);

            result.Add(new ArticulationLayout(
                art.MeasureIndex, art.ItemIndex, colX, yUp, string.Empty, above,
                art.SourcePosition, FontSizeStep: 0.0, GetSeedBBoxFor(art), StaffIndex: staffIndex,
                // Carried so the record never lies about the grob, though this array
                // only ever feeds the per-staff skyline that reserves the band — the
                // outside-staff pass runs on Calculate's layouts.
                OutsideStaffPriority: ArticulationSpacing.OutsideStaffPriority(art.Type)));
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
    /// The offset from the notehead's left edge (the item X) to its horizontal CENTRE —
    /// half its EXTENT. Picks the head glyph by note value (whole / half / black) so the
    /// script centres on the actual head.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/output-lib.scm:1906-1907 script-interface::calc-x-offset takes its
    ///   note-head-location from ly:self-alignment-interface::aligned-on-x-parent — the same
    ///   callback scm/define-grobs.scm:110 gives an AccidentalSuggestion outright.
    /// LILYPOND-REF: lily/self-alignment-interface.cc:116-160 aligned_on_parent — it reads the
    ///   X parent's EXTENT (the note column's, i.e. the head's box) and takes a
    ///   linear_combination of it, so what a script centres on is the head's extent centre
    ///   and never its advance.
    /// ⚠️ IT WAS THE ADVANCE (1.304000/2) UNTIL 2026-08-02, which put every script 0.000100
    /// left of LilyPond's — MEASURED by all three editorial.accidental.* ledger points at
    /// once, the same residual on three different glyphs, which is what said it was not the
    /// accidental's own box but this one.
    /// </remarks>
    private static double NoteheadHalfWidth(MusicItem item)
    {
        int noteValue = item switch
        {
            NoteItem n => n.BaseDuration.Numerator == 1 ? n.BaseDuration.Denominator : 1,
            ChordItem c => c.BaseDuration.Numerator == 1 ? c.BaseDuration.Denominator : 1,
            _ => 4
        };
        return GlyphMetrics.GetNoteheadBBox(noteValue).CenterX;
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
            // The accidental's ink box (anchored at the left baseline) AT ITS OWN SIZE:
            // read from the font font-size −2 selects, so nothing is scaled here.
            return GlyphMetrics.GetAccidentalBBox(
                EditorialFont, ArticulationItem.AccidentalKindFor(type));
        }

        return type switch
        {
            ArticulationType.Staccato => GlyphMetrics.ArticStaccato,
            ArticulationType.Accent => GlyphMetrics.ArticAccent,
            ArticulationType.Tenuto => GlyphMetrics.ArticTenuto,
            // Portato (tenuto line + staccato dot). Its near edge toward the note is
            // only the line's half-thickness (~0.07 ss), NOT the 0.5 ss the generic
            // fallback box assumed — which parked the mark ~0.43 ss too far below the
            // note. LILYPOND-REF: mf/feta-scripts.mf draw_portato —
            //   set_char_box(.6 ss, .6 ss, thick/2, .5 ss + .5 dot_size), thick =
            //   1.4·line-thickness (≈0.14 ss), dot_size ≈ 0.32 ss ⇒ far extent ≈0.66 ss;
            //   dportato is the y-mirror, so the near (line) edge stays ~0.07 ss.
            // Box ported straight from feta's draw_portato constants (LILYPOND-REF:
            // mf/feta-scripts.mf), with line-thickness = 0.1 ss (LilyPond's default):
            //   dot_size   = 2.4·0.1 + 0.08          = 0.32 ss   (drawdot diameter)
            //   dot centre = 0.5 + 0.5·dot_size      = 0.66 ss   (drawdot (0, h))
            //   NEAR edge  = dot centre + dot_size/2 = 0.82 ss   (the dot's outer rim,
            //                                                      toward the note)
            //   FAR edge   = thick/2 = 1.4·0.1/2     = 0.07 ss   (the tenuto line)
            //   half-width = 0.6 ss                              (set_char_box .6, .6)
            // The rim (0.82), NOT the centre, is what the staff-padding clamp measures —
            // using the centre seated an in-staff note's dot only ~0.1 ss past a staff
            // line (nearly touching); the rim clears it by the full staff-padding.
            ArticulationType.Portato => isAbove
                ? new GlyphMetrics.BBox(-0.6000, -0.8200, 0.6000, 0.0700)
                : new GlyphMetrics.BBox(-0.6000, -0.0700, 0.6000, 0.8200),
            ArticulationType.Marcato => isAbove
                ? GlyphMetrics.ArticMarcatoAbove : GlyphMetrics.ArticMarcatoBelow,
            ArticulationType.Fermata or ArticulationType.FermataShort or ArticulationType.FermataLong => isAbove
                ? GlyphMetrics.FermataAboveGlyph : GlyphMetrics.FermataBelowGlyph,
            ArticulationType.Staccatissimo => isAbove
                ? GlyphMetrics.ArticStaccatissimoAboveGlyph : GlyphMetrics.ArticStaccatissimoBelowGlyph,
            ArticulationType.UpBow => isAbove
                ? GlyphMetrics.ArticUpBowAboveGlyph : GlyphMetrics.ArticUpBowBelowGlyph,
            ArticulationType.DownBow => isAbove
                ? GlyphMetrics.ArticDownBowAboveGlyph : GlyphMetrics.ArticDownBowBelowGlyph,
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
    /// THE Script grob's vertical-skyline pair: the drawn glyph's real OUTLINE, anchored at
    /// the layout's own (X, <paramref name="anchorY"/>) in the caller's Y-up frame. ONE home
    /// for every consumer — the occupancy a script seeds, and the profile a script that
    /// declares a priority is placed with — because LilyPond hands the SAME
    /// <c>vertical-skylines</c> to <c>avoid_outside_staff_collisions</c> and to
    /// <c>all_v_skylines</c> (HANDOFF §5.2.1②; the trill paid for two spellings of one
    /// grob's profile in session 39).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:3006 Script
    ///   <c>vertical-skylines = grob::always-vertical-skylines-from-stencil</c> — so the
    ///   profile is the glyph's outline, not its designed box. It MATTERS pointwise: the
    ///   ufermata's underside is -0.076 at the centre where the dot hangs but rises past
    ///   +1.0 under the arch, so a thin stem tucks INTO the arch where a flat box would be
    ///   pushed clear of it (ledger script.stem-support.staff-to-ink-bottom = the drawn tip
    ///   + the script's own 0.40, with no collision move at all).
    /// LILYPOND-REF: lily/stencil-integral.cc:535-563 add_named_glyph_segments — the walk
    ///   TextOutlineSkylines.PlaceMusicGlyph reproduces.
    /// <para>
    /// Falls back to the designed ink box when there is no single music glyph to walk: the
    /// sentinel "glyphs" (bends, fret frames, TAB technique letters, snap pizzicato) and the
    /// staff-local tab array, which carries no glyph string at all.
    /// </para>
    /// </remarks>
    internal static (VerticalSkyline Up, VerticalSkyline Down) ScriptSkylines(
        in ArticulationLayout a, double anchorY)
    {
        // The size the renderer draws at (SharedRenderer: FontSize × the grob's magstep);
        // the flattening happens at the transformed size, which is why it is in the key.
        // ⚠️ …and from the DESIGN the renderer draws with: an editorial accidental is the
        // 16's outline, not the 20's shrunk, so walking the 20 here would profile a glyph
        // the page does not carry (Emmentaler is optically sized).
        if (a.Glyph.Length == 1)
        {
            var (up, down) = TextOutlineSkylines.PlaceMusicGlyph(
                a.Glyph[0],
                SharedRenderer.FontSize * EmmentalerDesignSize.Magstep(a.FontSizeStep),
                a.X, anchorY,
                EmmentalerDesignSize.ForFontSizeStep(a.FontSizeStep).Rounded);
            if (!up.IsEmpty || !down.IsEmpty)
                return (up, down);
        }
        // No walkable glyph: the designed box, as before.
        var box = a.Ink;
        return (VerticalSkyline.FromBox(a.X + box.Left, a.X + box.Right,
                    anchorY + box.Bottom, anchorY + box.Top, VerticalDirection.Up),
                VerticalSkyline.FromBox(a.X + box.Left, a.X + box.Right,
                    anchorY + box.Bottom, anchorY + box.Top, VerticalDirection.Down));
    }

    /// <summary>
    /// Ink box used to seed the outside-staff occupancy (so movable grobs —
    /// rehearsal/section marks etc. — clear the scripts). Uses the real font
    /// metrics for the ornament glyphs (extracted from Emmentaler via
    /// audit/scripts/Extract-EmmentalerMetrics.py), which are much wider/taller
    /// than the 0.5×0.5 positioning fallback — e.g. prall-prall spans ~2.85sp
    /// wide. The ornaments' own POSITIONING still uses the simplified extents
    /// (GetGlyphBBox), exactly as the trill does; only
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
        double attach = LayoutUtilities.StemAttachX(up);
        var xs = new double[n];
        for (int i = 0; i < n; i++)
            xs[i] = (i < beam.MemberXPositions.Length ? beam.MemberXPositions[i] : 0) + attach;
        var line = TabBeamQuant.Compute(beam.Group, xs, geom, up);
        double half = EngravingDefaults.BeamThickness / 2;
        return TabBeamMath.At(line, noteX) + (up ? -half : half);
    }

    private static Dictionary<(int Staff, int Measure, int Item), (BeamLayout Beam, double MemberX, bool StemUp)>
        BuildBeamedStemTips(ImmutableArray<BeamLayout> beamLayouts)
    {
        var tips = new Dictionary<(int, int, int), (BeamLayout, double, bool)>();
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
                // A script on the beam's side must clear the beam stack's OUTER edge (the
                // outermost beam's far face, not the single-beam centre) — the same canonical
                // line the slur/tuplet use. The face itself is read by the single house of a
                // column's reach (NoteColumnLayout, at the beam model's member X); this map
                // only resolves WHICH beam a (staff, measure, item) belongs to.
                int staff = !beam.MemberStaffIndices.IsDefaultOrEmpty
                    ? beam.MemberStaffIndices[i]
                    : Math.Max(0, beam.StaffIndex);
                tips[(staff, member.ResolveMeasureIndex(group.MeasureIndex), member.ItemIndex)] =
                    (beam, beam.MemberXPositions[i], member.MemberStemUp);
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

        double anchorUp = CalculateYPosition(articulation, staffPosition, stemUp, item,
            NoteColumnLayout.Of(item, stemUp));
        // CalculateYPosition now returns Y-up (staff-spaces above the middle line).
        // This spacing skyline has its middle line at staffY with Y increasing DOWN,
        // so the device value there is staffY − up. (ArticulationLayout is not built
        // here — this box stays device for its GraceNote/SpacingRules consumers.)
        double anchorSky = staffY - anchorUp;

        // Glyph ink box (font frame, Y up); map about the anchor into Y-down.
        var bbox = GetSeedBBox(articulation.Type, isAbove);
        double yBottom = anchorSky - bbox.Top;
        double yTop = anchorSky - bbox.Bottom;
        return (yBottom, yTop, bbox.Left, bbox.Right);
    }

    private static double CalculateYPosition(ArticulationItem articulation, int staffPosition, bool stemUp,
        MusicItem? item = null, NoteColumnLayout? column = null)
    {
        // LILYPOND-REF: define-grobs.scm:1365 fermata: direction = UP
        // LILYPOND-REF: define-grobs.scm:4075 TrillSpanner: direction = UP
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

        // Convert staff position to the LilyPond-native Y-up frame: staff-spaces
        // ABOVE the staff middle line, up-positive. StaffPosition 0 = middle,
        // positive = up, so noteUp = pos/2. (The stem-length support term is a
        // frame-invariant DISTANCE computed by NoteColumnLayout in the device frame.)
        // LILYPOND-REF: staff-symbol-referencer.cc:76-89 staff_symbol_referencer::get_position
        double noteUp = anchorPosition * 0.5;

        // Use quantize-position for staccato, marcato, tenuto
        // LILYPOND-REF: scm/script.scm staccato/marcato/tenuto: (quantize-position . #t)
        if (ShouldQuantize(articulation.Type))
        {
            return QuantizedYPosition(noteUp, isAbove, stemUp, articulation.Type, item,
                column);
        }

        // Non-quantized path: fermata, ornaments, accent, portato
        // LILYPOND-REF: side-position-interface.cc:360-378 total_off calculation
        // LILYPOND-REF: side-position-interface.cc:426-445 staff-padding clamp
        //
        // include_staff = true (staff-padding exists AND quantize-position = false)
        // The staff is included in the support skyline, then staff-padding is applied.

        // StaffHalf = the outer staff line, staff-spaces above/below the middle (Y-up).
        const double StaffHalf = 2.0;
        double glyphNearExtent = GetNearExtent(articulation.Type, isAbove);
        double supportExtent = isAbove
            ? (stemUp ? StemSupportExtent(item, column) : NoteheadHalfHeight)
            : (!stemUp ? StemSupportExtent(item, column) : NoteheadHalfHeight);

        // dist = skyline distance; total_off = dist + padding. In Y-up an above
        // script sits ABOVE the note (+) and a below script BELOW (−).
        double totalOff = supportExtent + glyphNearExtent + PaddingFor(articulation.Type);
        double targetUp = isAbove ? noteUp + totalOff : noteUp - totalOff;

        if (isAbove)
        {
            // LILYPOND-REF: side-position-interface.cc:426-445 staff-padding clamp
            // Ensure the glyph's staff-facing (bottom) edge clears the top staff line
            // by staff-padding — in Y-up the bottom edge is the smaller value.
            double glyphEdgeUp = targetUp - glyphNearExtent;
            double staffEdgeUp = StaffHalf + StaffPadding;
            if (glyphEdgeUp < staffEdgeUp)
                targetUp = staffEdgeUp + glyphNearExtent;
            return targetUp;
        }
        else
        {
            // Ensure the glyph's staff-facing (top) edge clears the bottom staff line.
            double glyphEdgeUp = targetUp + glyphNearExtent;
            double staffEdgeUp = -StaffHalf - StaffPadding;
            if (glyphEdgeUp > staffEdgeUp)
                targetUp = staffEdgeUp - glyphNearExtent;
            return targetUp;
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
    /// the anchor head (the stem-tip-side head) to the REAL stem tip — the
    /// beam-quanted face for a beamed column, the drawn stem end for an unbeamed
    /// one. The body lives in <see cref="NoteColumnLayout.StemSupportDistanceDeviceY"/>,
    /// the single house of a column's reach (HANDOFF §5.2.1②).
    /// </summary>
    private static double StemSupportExtent(MusicItem? item, NoteColumnLayout? column)
    {
        if (item == null)
            return DefaultStemLength;   // legacy callers without an item: old behaviour
        // A rest (no column) has no stem to clear — the nominal half head, as before.
        return column?.StemSupportDistanceDeviceY() ?? NoteheadHalfHeight;
    }

    private static double QuantizedYPosition(double noteUp, bool isAbove, bool stemUp,
        ArticulationType type, MusicItem? item = null, NoteColumnLayout? column = null)
    {
        // StaffHalf = the outer staff line, staff-spaces above/below the middle (Y-up).
        const double StaffHalf = 2.0;
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
                ? StemSupportExtent(item, column)
                : NoteheadHalfHeight;
            // ↑ if stemUp AND isAbove: stem IS in support (forced above case), real stem tip
            // ↑ if !stemUp AND isAbove: stem skipped, just notehead top = 0.5
        }
        else
        {
            // For below: support's DOWN extent
            supportExtent = !stemUp
                ? StemSupportExtent(item, column)
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

        // Convert total_off to target Y in the Y-up frame (above = +, below = −).
        double targetUp = isAbove ? noteUp + totalOff : noteUp - totalOff;

        // ── Stage 7 (aligned_side): Apply quantize-position ──
        //
        // LILYPOND-REF: side-position-interface.cc:402-425
        // Note: include_staff = false when quantize-position = true (line 222-226)
        // So staff-padding is NOT applied before quantization.

        // Convert to LP staff position (half-spaces): Y-up staff-spaces × 2.
        // LP: 0 = middle line, positive = up, negative = down.
        double lpPosition = targetUp * 2.0;

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
            // Equivalent: snap targetUp to the rounded LP position (half-spaces × 0.5).
            targetUp = rounded * 0.5;

            // LILYPOND-REF: side-position-interface.cc:421-422
            // if (Staff_symbol_referencer::on_line(me, int(rounded)))
            //     total_off += dir * 0.5 * ss;
            // Even LP positions within staff lines [−4, 4] are on lines; push a
            // half-space further OUT (up for above, down for below) — Y-up signs.
            int roundedInt = (int)rounded;
            if (roundedInt >= -4 && roundedInt <= 4 && roundedInt % 2 == 0)
            {
                targetUp += isAbove ? 0.5 : -0.5;
            }
        }

        // LilyPond positions a script against the glyph's real ink SKYLINE, not a box:
        // side-position-interface.cc:259 (my_dim = the glyph's vertical-skyline) and :354
        // (dist = dim.distance(my_dim)). A marcato is a chevron whose ink sits HIGH over
        // the notehead centre, so LP's skyline distance seats the ~1.1-ss-tall glyph clear
        // of the staff — its near edge ends up a script-padding outside the outer line.
        // Lily# approximates a script by its BOX (near extent 0 for marcato), which
        // under-clears the tall glyph and leaves its body straddling a staff line. A glyph
        // taller than a 1.0-ss space cannot sit between two lines anyway, so reproduce LP's
        // result directly: seat an in-staff script taller than a space with its near edge a
        // padding outside the outer staff line. Thinner quantized scripts (staccato dot
        // 0.4 ss, tenuto dash 0.16 ss) fit within a space, so the >1.0 guard — and every
        // fixture relying on their placement — is untouched.
        if (GetGlyphBBox(type, isAbove).Height > 1.0)
        {
            double gap = PaddingFor(type);      // the script's own padding, as in LP
            if (isAbove && targetUp < StaffHalf && targetUp >= -StaffHalf)
                targetUp = StaffHalf + gap;      // glyph bottom clears the top line by a gap
            else if (!isAbove && targetUp <= StaffHalf && targetUp > -StaffHalf)
                targetUp = -StaffHalf - gap;     // glyph top clears the bottom line by a gap
        }

        return targetUp;
    }
}
