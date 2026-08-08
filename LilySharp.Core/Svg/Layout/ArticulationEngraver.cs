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
    // The script's DECLARED skyline-horizontal-padding (staccato/staccatissimo 0.10,
    // downbow 0.20, everything else 0) — baked from the type exactly as the priority below
    // is. It is not decoration: LilyPond's Script profile IS its stencil skyline PADDED by
    // this number (lily/stencil-integral.cc:881-893), so every consumer of the profile sees
    // a shape up to 2× the glyph's own width. See ArticulationSpacing.SkylineHorizontalPadding.
    double SkylineHorizontalPadding = 0.0,
    double? OutsideStaffPriority = null // The script's outside-staff-priority (the
                            // fermata family's DECLARED 75), or null for the scripts that
                            // declare none — LilyPond's #f, which is not a zero (a grob
                            // declaring 0 would be the first MOVER placed). Baked from the
                            // type by the engraver the way LilyPond resolves a Script's
                            // properties out of scm/script.scm — EXCEPT that a priority-less
                            // script sorted after a mover on the same note & side is bumped
                            // to mover_priority + 0.1 by the script-column walk (Calculate),
                            // which is why this is a double: 75.1 is a real LilyPond value.
                            // A script WITH a priority is a mover in the outside-staff
                            // collision pass, one without seeds the occupancy the movers
                            // clear. See ArticulationSpacing.OutsideStaffPriority.
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

    /// <summary>The Script grob's horizon-padding — the horizontal widening its
    /// engraver-level side-position pays in every skyline distance ("to avoid
    /// interleaving with accidentals"). The outside-staff PASS does not pay it
    /// (that pass's horizon padding is outside-staff-horizontal-padding, which
    /// Script leaves at 0 — see OutsideStaffStacker.PlaceAboveArticulations).</summary>
    // LILYPOND-REF: lily/side-position-interface.cc:354-357 aligned_side — spends the
    //   grob's horizon-padding in dim.distance(my_dim, hpad); the 0.1 is the Script
    //   grob's own declaration (scm/define-grobs.scm:2999, "to avoid interleaving
    //   with accidentals").
    private const double ScriptHorizonPadding = 0.1;

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
        ImmutableArray<BeamLayout> beamLayouts = default,
        ImmutableArray<TieLayout> tieLayouts = default,
        ImmutableArray<SlurLayout> slurLayouts = default)
        => CalculateWithFingerings(score, articulations, measureLayouts, measuresByStaff,
            staffYAt, staffByIndex, beamLayouts, tieLayouts, slurLayouts, default, out _);

    // The Fingering grob's own side-position padding (its vertical float off whatever
    // supports it in the column) — the Script table does not carry it because Fingering
    // is its own grob, not a script.scm entry.
    // LILYPOND-REF: scm/define-grobs.scm:1543-1550 Fingering, add-stem-support block — (padding . 0.5)
    private const double FingeringPadding = 0.5;

    /// <summary>
    /// <see cref="Calculate"/>, with the note-attached FINGERINGS entering each note's
    /// script column: a vertically-oriented fingering is a script at priority
    /// 100 + direction × head position (<see cref="FingeringLayout.ColumnPriority"/>),
    /// so it is stacked INTO the walk — it clears the scripts below its slot and the
    /// scripts above its slot clear it. <paramref name="fingerings"/> come in at their
    /// island answer (staff/head clearance) and leave in <paramref name="adjustedFingerings"/>
    /// at their column answer; entries that do not enter a column (chord fingerings)
    /// pass through unchanged.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/new-fingering-engraver.cc:314-340 position_scripts — an
    ///   up/down-oriented fingering gets Side_position_interface Y-axis and
    ///   script-priority 100 + d·position, which is what makes Script_column sort it
    ///   between a tenuto (−50) and a bow (180);
    ///   lily/script-column.cc:160-186 order_grobs — the shared walk.
    /// ⚠️ The per-staff SKYLINE pass (MultiStaffLayouter.StaffArticulationLayouts) still
    /// calls the fingering-less overload, so a mover's occupancy seed over a
    /// fingering-bearing stack carries the scripts' chain answers without the fingering's
    /// ink — no corpus book or fixture reaches that pairing yet (a fermata directly over
    /// a fingered note); when one does, that call site needs the same fingerings.
    /// </remarks>
    internal static ImmutableArray<ArticulationLayout> CalculateWithFingerings(
        Score score,
        ImmutableArray<ArticulationItem> articulations,
        ImmutableArray<MeasureLayout> measureLayouts,
        Dictionary<int, ImmutableArray<Measure>>? measuresByStaff,
        Func<int, int, double>? staffYAt,
        Dictionary<int, Staff>? staffByIndex,
        ImmutableArray<BeamLayout> beamLayouts,
        ImmutableArray<TieLayout> tieLayouts,
        ImmutableArray<SlurLayout> slurLayouts,
        ImmutableArray<FingeringLayout> fingerings,
        out ImmutableArray<FingeringLayout> adjustedFingerings)
    {
        adjustedFingerings = fingerings;
        if (articulations.IsDefaultOrEmpty)
            return ImmutableArray<ArticulationLayout>.Empty;

        int[] order = OrderByScriptPriority(articulations);

        // The drawn ties whose START or END moment a script sits on: each becomes a
        // side-position SUPPORT of that moment's scripts, which is the whole of
        // LilyPond's "scripts avoid ties" — the accent on a tied note rides up over
        // the bow's shoulder. Keyed by the script's own note; a chord's per-member
        // ties all land on the one item and all support all of its scripts, exactly
        // as every one of them is acknowledged in LilyPond. A tie broken at a system
        // break supports through the piece that KEEPS the relevant bound (the start
        // moment reads the un-broken-left piece, the end moment the un-broken-right
        // one — break substitution hands LilyPond the same piece).
        // LILYPOND-REF: lily/script-engraver.cc:204-222 acknowledge_tie /
        //   acknowledge_end_tie — Side_position_interface::add_support (script, tie)
        //   at the tie's start and end timesteps respectively.
        Dictionary<(int Staff, int Voice, int Measure, int Item), List<TieLayout>>?
            tiesAtBound = null;
        if (!tieLayouts.IsDefaultOrEmpty)
        {
            foreach (var t in tieLayouts)
            {
                int tieStaff = Math.Max(t.StaffIndex, 0);
                if (!t.IsBrokenLeft)
                    AddTieBound((tieStaff, t.Tie.VoiceIndex,
                        t.Tie.StartMeasureIndex, t.Tie.StartItemIndex), t);
                if (!t.IsBrokenRight)
                    AddTieBound((tieStaff, t.Tie.VoiceIndex,
                        t.Tie.EndMeasureIndex, t.Tie.EndItemIndex), t);
            }
            void AddTieBound((int, int, int, int) key, TieLayout t)
            {
                tiesAtBound ??= new Dictionary<(int, int, int, int), List<TieLayout>>();
                if (!tiesAtBound.TryGetValue(key, out var list))
                    tiesAtBound[key] = list = new List<TieLayout>();
                list.Add(t);
            }
        }

        // The drawn slurs by (staff, voice, measure) — every measure a slur's span
        // touches carries its pieces, so a script's lookup is one dictionary hit
        // and a scan of that measure's few slurs, not the voice's whole list. A
        // script on any note a slur COVERS (start note through end note —
        // Slur_engraver acknowledges every Script grob made while its slur runs)
        // gets outside_slur_callback chained onto its side-position answer, keyed
        // by its avoid-slur declaration. Unlike the ties above there is NO
        // chord-member gate: a member's Script grob is acknowledged like any other
        // (ADD_ACKNOWLEDGER (script) matches the grob, not its maker).
        // LILYPOND-REF: lily/slur-engraver.cc:162-167 acknowledge_script;
        //   lily/slur.cc:388-402 auxiliary_acknowledge_extra_object — 'outside /
        //   'around chain outside_slur_callback, 'inside becomes extra encompass
        //   (the SLUR bends, the script stays), 'ignore does nothing.
        Dictionary<(int Staff, int Voice, int Measure), List<SlurLayout>>? slursAtMeasure = null;
        if (!slurLayouts.IsDefaultOrEmpty)
        {
            slursAtMeasure = new Dictionary<(int, int, int), List<SlurLayout>>();
            foreach (var s in slurLayouts)
            {
                int slurStaff = Math.Max(s.StaffIndex, 0);
                var sl = s.Slur;
                for (int m = sl.StartMeasureIndex; m <= sl.EndMeasureIndex; m++)
                {
                    var key = (slurStaff, sl.VoiceIndex, m);
                    if (!slursAtMeasure.TryGetValue(key, out var list))
                        slursAtMeasure[key] = list = new List<SlurLayout>();
                    list.Add(s);
                }
            }
        }

        // WHICH entry of the list a measure index names. The annotation pass hands the whole
        // score's layouts, where the two coincide; the per-staff skyline pass hands ONE
        // system's, where they do not — system 2's first entry is MeasureIndex 4. Indexing
        // positionally read a neighbour's measure there (or fell off the end and dropped the
        // script), which is the same positional/by-index trap MultiStaffLayouter.LyricRowInk
        // names on its own overload.
        var layoutAt = new Dictionary<int, MeasureLayout>(measureLayouts.Length);
        foreach (var m in measureLayouts)
            layoutAt[m.MeasureIndex] = m;

        var beamedTips = BuildBeamedStemTips(beamLayouts);
        var beamGroups = BuildBeamGroupMap(beamLayouts);
        var layouts = ImmutableArray.CreateBuilder<ArticulationLayout>(articulations.Length);
        // Per-note, per-side SUPPORT CHAIN so stacked scripts don't overprint: every
        // priority-less script already placed on the same (staff, measure, item, side)
        // becomes a side-position support of the scripts after it.
        // LILYPOND-REF: lily/script-column.cc:160-186 order_grobs — each script without
        //   an outside-staff-priority is added to the side-support-elements of the
        //   scripts ordered after it (:168-171); the ones WITH a priority are movers
        //   and are left to the outside-staff machinery.
        var supportScripts = new Dictionary<(int, int, int, bool), List<ArticulationLayout>>();
        // The previous script of each (note, side) in the priority-ordered walk: its
        // DECLARED priority and its CURRENT one (they differ once a script is bumped).
        // LILYPOND-REF: lily/script-column.cc:147-156 order_grobs — last /
        //   last_initial_outside_staff carried across the sorted loop.
        var lastOnKey = new Dictionary<(int, int, int, bool),
            (double? InitialOsp, double? CurrentOsp)>();
        // The bump can only ever fire when SOME script declares a priority (a mover is
        // what converts its followers), so a mover-less page — the common one — skips
        // the last-script bookkeeping entirely: one flag test per script instead of
        // two dictionary operations, and the walk is the pre-bump walk.
        bool anyMover = false;
        foreach (var a in articulations)
            if (ArticulationSpacing.OutsideStaffPriority(a.Type) is not null)
            {
                anyMover = true;
                break;
            }

        // Column-participating fingerings by note, each queue sorted by priority; a
        // fingering is FLUSHED into the chain when the walk reaches its slot — before
        // the first same-side script whose priority exceeds its own — or after the
        // loop when nothing outranks it.
        ImmutableArray<FingeringLayout>.Builder? adjFingerings = null;
        Dictionary<(int, int, int), List<int>>? pendingFingerings = null;
        if (!fingerings.IsDefaultOrEmpty)
        {
            for (int fi = 0; fi < fingerings.Length; fi++)
            {
                var fg = fingerings[fi];
                if (fg.ColumnPriority == int.MinValue || !fg.IsAbove)
                    continue;
                adjFingerings ??= fingerings.ToBuilder();
                pendingFingerings ??= new Dictionary<(int, int, int), List<int>>();
                var nk = (Math.Max(fg.StaffIndex, 0), fg.MeasureIndex, fg.ItemIndex);
                if (!pendingFingerings.TryGetValue(nk, out var queue))
                    pendingFingerings[nk] = queue = new List<int>();
                queue.Add(fi);
            }
            if (pendingFingerings != null)
                foreach (var queue in pendingFingerings.Values)
                    queue.Sort((x, y) => fingerings[x].ColumnPriority
                        .CompareTo(fingerings[y].ColumnPriority));
        }

        // Sit the note's queued fingerings below priority <paramref>upTo</paramref>
        // into the chain of (note, UP): each takes max(its island answer, pointwise
        // distance over the placed profiles + its own padding 0.5), then becomes a
        // support itself. The reader-side horizon padding is the FINGERING's — it
        // declares none, so 0 (the Script's 0.1 is not borrowed).
        // ⚠️ upTo is STRICTLY-LESS: a fingering whose priority ties a script's exact
        // number flushes AFTER that script, where LilyPond's stable sort keeps
        // acknowledgment order — no spelling reaches the tie (Fingering is 100 +
        // position, scripts declare −100/−50/0/50/150/175/180).
        // LILYPOND-REF: lily/side-position-interface.cc:354-378 aligned_side — the
        //   distance call spends the placed-grob's horizon-padding and the padding is
        //   the moving grob's own.
        void FlushFingerings((int, int, int, bool) key, int upTo)
        {
            var (kStaff, kMeasure, kItem, _) = key;
            if (pendingFingerings == null
                || !pendingFingerings.TryGetValue(
                    (Math.Max(kStaff, 0), kMeasure, kItem), out var queue))
                return;
            while (queue.Count > 0 && adjFingerings![queue[0]].ColumnPriority < upTo)
            {
                int fi = queue[0];
                queue.RemoveAt(0);
                var fg = adjFingerings[fi];
                var synth = FingeringScriptLayout(fg);
                if (supportScripts.TryGetValue(key, out var sup) && sup.Count > 0)
                {
                    var (_, myDown) = ScriptSkylines(synth, fg.YUp);
                    double closest = double.NegativeInfinity;
                    foreach (var s in sup)
                    {
                        var (sUp, _) = ScriptSkylines(s, s.YUp);
                        closest = Math.Max(closest, myDown.Distance(sUp, 0.0));
                    }
                    double move = Math.Max(0.0, closest + FingeringPadding);
                    if (move > 0)
                    {
                        fg = fg with { YUp = fg.YUp + move };
                        synth = synth with { YUp = fg.YUp };
                        adjFingerings[fi] = fg;
                    }
                }
                if (!supportScripts.TryGetValue(key, out var placedList))
                    supportScripts[key] = placedList = new List<ArticulationLayout>();
                placedList.Add(synth);
                if (anyMover)
                    lastOnKey[key] = (null, null);
            }
        }

        foreach (int arti in order)
        {
            var articulation = articulations[arti];
            // Find the measure layout
            if (!layoutAt.TryGetValue(articulation.MeasureIndex, out var measureLayout))
                continue;

            // Bounds guard (single-staff layouts only; multi-staff layouts
            // resolve through timing-aligned columns).
            if (measureLayout.Columns.IsDefaultOrEmpty
                && articulation.ItemIndex >= measureLayout.Items.Length)
                continue;

            // Resolve this articulation's OWN staff (multi-staff) and, within it, its OWN
            // VOICE: ItemIndex counts the items of the voice the script was written in, so
            // the staff's primary-voice list answers with whatever note shares the index —
            // in a two-voice staff, the upper voice's, at its pitch and (once the rhythms
            // differ) at its column. The \f on the same note has resolved through its voice
            // since the dynamics island; this is the same lookup, one house.
            // LILYPOND-REF: lily/script-engraver.cc:234-250 acknowledge_rhythmic_head — the
            //   heads it takes are its own Voice context's (ly/engraver-init.ly:414-416
            //   Script_engraver).
            // Measured: audit/lp-geometry script.{staccato,marcato}-below.staff-to-ink-top —
            //   LilyPond reads ONE number for both glyphs; the primary-voice anchor spread
            //   them by 0.9 and parked both inside the staff.
            var artVoices = staffByIndex != null
                && staffByIndex.TryGetValue(articulation.StaffIndex, out var voiceStaff)
                && !voiceStaff.Voices.IsDefaultOrEmpty
                    ? voiceStaff.Voices : score.Voices;
            var staffMeasures = LayoutUtilities.ResolveStaffMeasures(measuresByStaff, articulation.StaffIndex, score.Voice.Measures);
            var artMeasures = LayoutUtilities.ResolveVoiceMeasures(
                artVoices, articulation.VoiceIndex, staffMeasures);
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
            // would pierce the glyph. The direction that governs is the one of the script's
            // OWN voice — \voiceOne up, \voiceTwo down — the same voice its anchor note came
            // from just above. (Beamed members refine this from the beam just below.)
            // Only inside the voice { } span, though — outside it voice 1 is the
            // only voice and keeps its pitch-natural direction.
            // LILYPOND-REF: scm/music-functions.scm:1042-1057 voicify-sublist / make-voice-props-set
            if (staffByIndex != null
                && staffByIndex.TryGetValue(articulation.StaffIndex, out var ownStaff)
                && VoiceDefaults.GetDefaultStemUpAt(
                    ownStaff.Voices, articulation.VoiceIndex, articulation.MeasureIndex) is { } voiceStemUp)
                stemUp = voiceStemUp;

            // A beamed member's stem ends on the BEAM, not at the unbeamed
            // formula's tip, and the beam also resolves its direction.
            BeamLayout? memberBeam = null;
            double memberStemX = 0.0;
            if (beamedTips.TryGetValue(
                (articulation.StaffIndex, articulation.VoiceIndex,
                 articulation.MeasureIndex, articulation.ItemIndex),
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
                    (articulation.StaffIndex, articulation.VoiceIndex,
                     articulation.MeasureIndex, articulation.ItemIndex),
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
                    double clear = insideEligible
                        ? noteTop - fretHalf
                        : Math.Min(noteTop - fretHalf, topLine);
                    // ⚠️ AND THE STEM, WHEN IT POINTS THIS WAY. An unbeamed up-stem
                    // protrudes past the digits exactly as a beam does, and the BEAMED
                    // branch above has always cleared the beam's outer edge — this one
                    // had no stem term at all, so a forced-above script was seated on the
                    // staff edge and the stem was drawn straight through it.
                    // The CONDITION is LilyPond's, though the clamp around it is not:
                    // LILYPOND-REF: lily/side-position-interface.cc:279-284 get_grob_direction
                    //   — a support whose direction opposes the script's is skipped, so a
                    //   stem is in the support exactly when it travels the script's way.
                    // ⚠️ The rest of this branch is NOT aligned_side: it clamps to the staff
                    //   and carries no glyph near-extent, which is why the two scripts below
                    //   still land on ONE number. Named in HANDOFF §1 ▶ ⑵, not fixed here.
                    // ⚠️ `!insideEligible` IS FOLDED OUT OF THE GUARD, and the proof is one
                    //   line so the next reader does not have to redo it: insideEligible is
                    //   `!forcedAbove && (tabAbove != tabStemUp)`, so inside THIS branch
                    //   (tabAbove) a true tabStemUp makes the second conjunct false. Writing
                    //   both would be a condition that cannot fire — but if insideEligible's
                    //   definition changes, this fold is the thing that breaks.
                    if (tabStemUp
                        && geom.UnbeamedStemTipY(item, stemUp: true, noteTop) is { } tipUp)
                        clear = Math.Min(clear, tipUp);
                    tabY = clear - tabGap;
                }
                else
                {
                    // Below the note's own BOTTOM digit — inside the staff for a
                    // stem-coupled mark (when it isn't the bottom string), else clamped
                    // to the bottom line so a forced mark clears the whole staff.
                    double noteBottom = geom.StringY(geom.StemHeadString(item, stemUp: false));
                    double clear = insideEligible
                        ? noteBottom + fretHalf
                        : Math.Max(noteBottom + fretHalf, bottomLine);
                    // The same stem term on the other side (see the above branch).
                    if (!tabStemUp
                        && geom.UnbeamedStemTipY(item, stemUp: false, noteBottom) is { } tipDown)
                        clear = Math.Max(clear, tipDown);
                    tabY = clear + tabGap;
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
                    GetSeedBBox(articulation.Type), SourceIndex: arti,
                    StaffIndex: articulation.StaffIndex,
                    SkylineHorizontalPadding:
                        ArticulationSpacing.SkylineHorizontalPadding(articulation.Type)));
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

            var seedBBox = GetSeedBBoxFor(effArt);
            var layout = new ArticulationLayout(
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
                SkylineHorizontalPadding:
                    ArticulationSpacing.SkylineHorizontalPadding(effArt.Type),
                OutsideStaffPriority: ArticulationSpacing.OutsideStaffPriority(effArt.Type)
            );

            // A tie starting or ending on this note supports this script: pointwise
            // distance over the bow's outline, the script's own horizon-padding spent
            // in the distance call and its own padding on top — the same aligned_side
            // reading the chain below makes, folded into the note answer BEFORE the
            // chain so the scripts stacked above ride up with it. The push is a push,
            // not a re-quantize (the chain's rule). Only the bow on the script's own
            // side can bind: the far-side bow hugs the head's other edge and tops out
            // below the head, so its skyline never reaches the script — LilyPond gets
            // the same no-op out of the full stencil skyline's distance.
            // LILYPOND-REF: lily/script-engraver.cc:204-222 acknowledge_tie /
            //   acknowledge_end_tie; lily/side-position-interface.cc:354-378
            //   aligned_side — Tie declares vertical-skylines from its stencil
            //   (scm/define-grobs.scm Tie), so the support profile IS the drawn bow.
            if (tiesAtBound != null && TakesTieSupport(effArt)
                && tiesAtBound.TryGetValue((effArt.StaffIndex, effArt.VoiceIndex,
                    effArt.MeasureIndex, effArt.ItemIndex), out var boundTies))
            {
                var (tieMyUp, tieMyDown) = ScriptSkylines(layout, yUp);
                double closestTie = double.NegativeInfinity;
                foreach (var t in boundTies)
                {
                    if (t.CurveUp != effArt.IsAbove)
                        continue;
                    var bowSky = TieSupportSkyline(t, staffOffset, effArt.IsAbove);
                    closestTie = Math.Max(closestTie, effArt.IsAbove
                        ? tieMyDown.Distance(bowSky, ScriptHorizonPadding)
                        : tieMyUp.Distance(bowSky, ScriptHorizonPadding));
                }
                double tieMove = Math.Max(0.0, closestTie + PaddingFor(effArt.Type));
                if (tieMove > 0)
                {
                    yUp += effArt.IsAbove ? tieMove : -tieMove;
                    layout = layout with { YUp = yUp };
                }
            }

            // Stack multiple scripts on one note & side by the support chain, not by
            // box arithmetic: the next script sits at pointwise distance + its OWN
            // padding over the placed profiles, maxed with its own note answer (one
            // that already clears them stays put). A quantized script keeps its
            // quantized answer unless the chain pushes it (no re-quantize — the box
            // stack didn't either). A mover (fermata family) reads the chain — the
            // engraver floor script-column gives it — and the outside-staff pass
            // finishes it over the same profiles at its own outside-staff-padding.
            // LILYPOND-REF: lily/script-column.cc:168-171 Side_position_interface::add_support
            //   — every script so far supports the current one; side-position then answers
            //   pointwise distance with the Script's own horizon-padding spent in the
            //   distance call (lily/side-position-interface.cc:354-357 aligned_side), plus
            //   this script's `padding` (lily/side-position-interface.cc:360-378 aligned_side).
            var stackKey = (effArt.StaffIndex, effArt.MeasureIndex,
                effArt.ItemIndex, effArt.IsAbove);
            // The note's fingerings whose priority is below this script's enter the
            // column FIRST — they support this script, and this script's own walk
            // slot stays after them (the priority sort is the shared order).
            if (effArt.IsAbove)
                FlushFingerings(stackKey, ScriptPriority(effArt.Type));
            double? declaredOsp = layout.OutsideStaffPriority;
            if (anyMover && lastOnKey.TryGetValue(stackKey, out var lastScript)
                && lastScript.CurrentOsp is { } moverOsp)
            {
                // The previous script of this note & side (in priority order) is a
                // MOVER: a follower that declares no priority — or the same one —
                // becomes a mover itself, one notch above, and gets NO chain supports;
                // the outside-staff pass stacks it right after the one before it.
                // That is how a bow ends up ABOVE a fermata (175 vs 180 in script
                // priority, yet the fermata's 75 makes it a mover first).
                // LILYPOND-REF: lily/script-column.cc:178-185 set_property — the
                //   follower's outside-staff-priority := previous + 0.1 "in order to
                //   preserve ordering"; the unset previous INITIAL priority (:181,
                //   last_initial_outside_staff) compares as 0.
                if (declaredOsp is null
                    || Math.Abs(declaredOsp.Value - (lastScript.InitialOsp ?? 0.0)) < 0.001)
                    layout = layout with
                    {
                        OutsideStaffPriority = moverOsp + 0.1
                    };
            }
            else if (supportScripts.TryGetValue(stackKey, out var supports))
            {
                var (myUp, myDown) = ScriptSkylines(layout, yUp);
                double closest = double.NegativeInfinity;
                foreach (var s in supports)
                {
                    // Padding ONE side of the distance is LP's own equivalence (its
                    // distance(other, hpad) comment: padding other = doubling hpad).
                    var (sUp, sDown) = ScriptSkylines(s, s.YUp);
                    closest = Math.Max(closest, effArt.IsAbove
                        ? myDown.Distance(sUp, ScriptHorizonPadding)
                        : myUp.Distance(sDown, ScriptHorizonPadding));
                }
                double move = Math.Max(0.0, closest + PaddingFor(effArt.Type));
                if (move > 0)
                {
                    yUp += effArt.IsAbove ? move : -move;
                    layout = layout with { YUp = yUp };
                }
            }

            // A slur covering this note pushes an 'around/'outside script off its
            // bow — CHAINED ON TOP of the side-position answer, the way LilyPond
            // chains outside_slur_callback after aligned_side, and BEFORE this
            // script becomes a support: the scripts stacked above ride up with it
            // through the chain, which is why LP's slurred stack moves as one rigid
            // body. An 'inside script (staccato/tenuto/…) stays put — LP makes it
            // extra encompass instead, i.e. the SLUR is the one that moves (that
            // half is the slur scorer's and is NOT ported here; disclosed below).
            // LILYPOND-REF: lily/slur.cc:262-359 outside_slur_callback.
            if (slursAtMeasure != null
                && slursAtMeasure.TryGetValue((effArt.StaffIndex, effArt.VoiceIndex,
                    effArt.MeasureIndex), out var voiceSlurs))
            {
                var policy = SlurAvoidanceOf(effArt.Type);
                if (policy is SlurAvoid.Around or SlurAvoid.Outside
                    && CoveringSlurPiece(voiceSlurs, effArt.MeasureIndex,
                        effArt.ItemIndex) is { } slurPiece)
                {
                    double shift = SlurAvoidanceShift(slurPiece, layout, yUp,
                        staffOffset, around: policy == SlurAvoid.Around);
                    if (shift != 0.0)
                    {
                        yUp += shift;
                        layout = layout with { YUp = yUp };
                    }
                }
            }

            if (anyMover)
                lastOnKey[stackKey] = (declaredOsp, layout.OutsideStaffPriority);
            if (layout.OutsideStaffPriority is null)
            {
                if (!supportScripts.TryGetValue(stackKey, out var placed))
                    supportScripts[stackKey] = placed = new List<ArticulationLayout>();
                placed.Add(layout);
            }

            layouts.Add(layout);
        }

        // Fingerings nothing outranked (no same-side script above their slot): they
        // still take the chain answer over whatever the walk placed below them.
        if (pendingFingerings != null)
        {
            foreach (var nk in pendingFingerings.Keys.ToArray())
                FlushFingerings((nk.Item1, nk.Item2, nk.Item3, true), int.MaxValue);
            adjustedFingerings = adjFingerings!.ToImmutable();
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// The fingering as a script-column PARTICIPANT: its digit run's ink BOX at
    /// font-size −5, origin at the run's left baseline (X centres the run on the head
    /// the way the pen does). The Glyph is left EMPTY on purpose: Fingering declares no
    /// <c>vertical-skylines</c>, so LilyPond gives it the EXTENT-BOX profile, not the
    /// walked outline — the same rule the Dots seed follows. Walking the digit's real
    /// outline let a bow sink 0.21 into the round shoulder of a "0" (measured on
    /// script-stack-order1, LP −4.40 vs walked −4.19).
    /// LILYPOND-REF: lily/grob.cc:81-85 simple_vertical_skylines_from_extents_proc —
    ///   a grob without a vertical-skylines declaration answers with its extent box;
    ///   scm/define-grobs.scm:1540-1571 Fingering declares Y-extent but no
    ///   vertical-skylines.
    /// Never added to the returned layouts: the drawn pass is SharedRenderer.DrawFingerings.
    /// </summary>
    private static ArticulationLayout FingeringScriptLayout(in FingeringLayout fg)
    {
        var (_, ink, width) = FingeringEngraver.DigitRun(fg.Number);
        return new ArticulationLayout(
            fg.MeasureIndex, fg.ItemIndex,
            X: fg.X - width / 2.0,
            YUp: fg.YUp,
            Glyph: string.Empty,
            IsAbove: true,
            SourcePosition: fg.SourcePosition,
            FontSizeStep: -5.0,
            Ink: ink,
            StaffIndex: Math.Max(fg.StaffIndex, 0));
    }

    /// <summary>
    /// Which marks take a tie as side-position support — the ones Script_engraver
    /// makes, because it is the ONLY engraver with a tie acknowledger. A chord
    /// MEMBER's script is New_fingering_engraver's (supports: head, stem/flag, chord
    /// heads — no ties), an editorial accidental is Accidental_engraver's
    /// AccidentalSuggestion, and pluck/fret-frame are markup-family marks with no LP
    /// Script grob at all.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/script-engraver.cc:204-222 acknowledge_tie /
    ///   acknowledge_end_tie; lily/new-fingering-engraver.cc:144-157 add_script.
    /// Measured (scratch/lpreg/sctten.ly + sctten2.ly): a member tenuto on EITHER
    /// head of a tied chord stays at the island answer (−4.83) at both tie bounds —
    /// its own head's tie included — while the chord-level tenuto on the same music
    /// lifts to −5.35. The split is per-engraver, not per-tie-direction.
    /// </remarks>
    private static bool TakesTieSupport(in ArticulationItem a) =>
        !a.IsChordMember
        && !a.IsEditorialAccidental
        && a.Type is not (ArticulationType.Pluck or ArticulationType.FretFrame);

    /// <summary>How a script relates to a slur that covers its note.</summary>
    private enum SlurAvoid { Ignore, Inside, Around, Outside }

    // The Script grob's slur-padding — the widening of the script's box before the
    // slur avoidance is measured (Fingering declares the same 0.2). The property
    // name is a two-section hyphen word, which the citation ratchet rightly
    // refuses as a symbol — so the address stays line-less on purpose.
    // LILYPOND-REF: scm/define-grobs.scm Script, the slur-padding declaration (0.2).
    private const double ScriptSlurPadding = 0.2;

    /// <summary>
    /// The avoid-slur declaration of each mark Lily# spells — one arm per
    /// scm/script.scm entry. 'inside means the SLUR goes around the script (extra
    /// encompass — the slur scorer's half, not handled here); 'around/'outside
    /// mean the script is pushed off the bow; the marks with no LP Script grob
    /// (editorial, pluck, frame, tab letters, bends, breaths) fall to Ignore —
    /// no Slur_engraver ever acknowledges them.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/script.scm avoid-slur declarations.</remarks>
    private static SlurAvoid SlurAvoidanceOf(ArticulationType t) => t switch
    {
        ArticulationType.Staccato or ArticulationType.Staccatissimo
            or ArticulationType.Tenuto or ArticulationType.Marcato
            or ArticulationType.Stopped or ArticulationType.Turn
            or ArticulationType.InvertedTurn => SlurAvoid.Inside,
        ArticulationType.Trill or ArticulationType.SnapPizz => SlurAvoid.Outside,
        ArticulationType.Accent or ArticulationType.Portato
            or ArticulationType.Fermata or ArticulationType.FermataShort
            or ArticulationType.FermataLong or ArticulationType.Flageolet
            or ArticulationType.UpBow or ArticulationType.DownBow
            or ArticulationType.Mordent or ArticulationType.Prall
            or ArticulationType.PrallTriller or ArticulationType.Thumb
            or ArticulationType.Heel or ArticulationType.Toe => SlurAvoid.Around,
        _ => SlurAvoid.Ignore,
    };

    /// <summary>
    /// The slur piece whose span covers the note at (<paramref name="m"/>,
    /// <paramref name="i"/>), or null. The span test is the SLUR's (start note
    /// through end note inclusive — the timesteps Slur_engraver acknowledges
    /// scripts in); among a broken slur's pieces the one on the script's own
    /// system answers, picked as the piece with the greatest start measure not
    /// past the note (a piece that lost its left bound starts at its own system's
    /// first measure = RenderMeasureIndex).
    /// </summary>
    private static SlurLayout? CoveringSlurPiece(List<SlurLayout> voiceSlurs, int m, int i)
    {
        SlurLayout? best = null;
        int bestStart = int.MinValue;
        foreach (var s in voiceSlurs)
        {
            var sl = s.Slur;
            bool onOrAfterStart = sl.StartMeasureIndex < m
                || (sl.StartMeasureIndex == m && sl.StartItemIndex <= i);
            bool onOrBeforeEnd = m < sl.EndMeasureIndex
                || (m == sl.EndMeasureIndex && i <= sl.EndItemIndex);
            if (!onOrAfterStart || !onOrBeforeEnd)
                continue;
            int pieceStart = s.IsBrokenLeft ? s.RenderMeasureIndex : sl.StartMeasureIndex;
            if (pieceStart > m)
                continue;
            if (best == null || pieceStart > bestStart)
            {
                best = s;
                bestStart = pieceStart;
            }
        }
        return best;
    }

    /// <summary>
    /// LilyPond's outside_slur_callback: the rigid Y shift that puts this script
    /// off the slur's bow, or 0. The script's padded ink box is tested against the
    /// curve — 'around shifts only when the curve actually enters the box,
    /// 'outside when the curve pokes past the box's near edge at the script's x
    /// edges — and the shift is the curve's extremum over the box's x-overlap
    /// minus the box's near edge, signed by the script's direction. The curve is
    /// the CENTRELINE control polygon (no sandwich, no pen: LP reads
    /// Slur::get_curve), flattened by sampling where LP solves exactly.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur.cc:262-359 outside_slur_callback — the contains
    ///   test (:298-303), the padded-box widen (:309-311), the 'outside edge test
    ///   (:316-327), the 'around box-crossing test (:328-348) and the
    ///   curve.minmax − yext[-dir] shift (:350-356).
    /// </remarks>
    private static double SlurAvoidanceShift(
        SlurLayout piece, in ArticulationLayout layout, double yUp,
        double staffOffset, bool around)
    {
        var ink = layout.Ink;
        double xL = layout.X + ink.Left, xR = layout.X + ink.Right;
        // LP's guard is on extent length, not emptiness (0-extent tab scripts).
        if (xR - xL <= 0 || ink.Top - ink.Bottom <= 0)
            return 0.0;
        double dy = staffOffset + StaffMiddle;
        double p0x = piece.StartX, p0y = piece.StartYUp + dy;
        double p3x = piece.EndX, p3y = piece.EndYUp + dy;
        double p1x = piece.Control1.X, p1y = piece.Control1.Y + dy;
        double p2x = piece.Control2.X, p2y = piece.Control2.Y + dy;
        // contains: either x edge of the script inside the curve's x span.
        if (!((xL >= p0x && xL <= p3x) || (xR >= p0x && xR <= p3x)))
            return 0.0;
        double dir = layout.IsAbove ? 1.0 : -1.0;
        double yBottom = yUp + ink.Bottom - ScriptSlurPadding;
        double yTop = yUp + ink.Top + ScriptSlurPadding;
        const double Eps = 1e-5;
        double lo = Math.Max(xL, p0x + Eps), hi = Math.Min(xR, p3x - Eps);
        if (lo > hi)
            return 0.0;
        const int Samples = 64;
        bool doShift = false;
        double extreme = dir > 0 ? double.NegativeInfinity : double.PositiveInfinity;
        double prevX = 0.0, prevY = 0.0;
        for (int i = 0; i <= Samples; i++)
        {
            double t = (double)i / Samples;
            double mt = 1 - t;
            double b0 = mt * mt * mt, b1 = 3 * mt * mt * t, b2 = 3 * mt * t * t, b3 = t * t * t;
            double x = b0 * p0x + b1 * p1x + b2 * p2x + b3 * p3x;
            double y = b0 * p0y + b1 * p1y + b2 * p2y + b3 * p3y;
            if (!doShift)
            {
                if (around)
                {
                    doShift = x >= xL && x <= xR && y >= yBottom && y <= yTop;
                }
                else if (i > 0 && x > prevX)
                {
                    // 'outside: the curve's y AT the script's clamped x edges,
                    // past the box's near edge.
                    foreach (double xe in stackalloc[] { lo, hi })
                        if (prevX <= xe && x >= xe)
                        {
                            double ye = prevY + (y - prevY) * (xe - prevX) / (x - prevX);
                            if (dir > 0 ? ye >= yBottom : ye <= yTop)
                            {
                                doShift = true;
                                break;
                            }
                        }
                }
            }
            if (x >= lo && x <= hi)
                extreme = dir > 0 ? Math.Max(extreme, y) : Math.Min(extreme, y);
            prevX = x;
            prevY = y;
        }
        if (!doShift || double.IsInfinity(extreme))
            return 0.0;
        return extreme - (dir > 0 ? yBottom : yTop);
    }

    /// <summary>
    /// One tie's drawn bow as a side-position support profile, in the script frame
    /// (staff-spaces about this staff's middle line, up-positive). The tie scorer hands
    /// its Y system-relative with the staff offset baked in (BowLayout's frame,
    /// <c>yUp = −yDevice</c>), so the door conversion is that offset plus the middle
    /// line — the inverse of the "no staff offset" the script's own YUp carries. The
    /// outline model is <see cref="SkylineBuilder.MergeBowOuterEdge"/>, the SAME core the
    /// staff skyline's bow reservation flattens (SkylineBuilder.SeedBowInk) — the support
    /// a script clears and the band the staff reserves come from one sandwich, not two.
    /// </summary>
    private static VerticalSkyline TieSupportSkyline(
        TieLayout t, double staffOffset, bool above)
    {
        var direction = above ? VerticalDirection.Up : VerticalDirection.Down;
        var sky = new VerticalSkyline(direction);
        double dy = staffOffset + StaffMiddle;
        SkylineBuilder.MergeBowOuterEdge(
            sky, direction,
            t.StartX, t.StartYUp + dy,
            (t.Control1.X, t.Control1.Y + dy),
            (t.Control2.X, t.Control2.Y + dy),
            t.EndX, t.EndYUp + dy,
            0.5 * EngravingDefaults.SlurMidThickness,
            0.5 * EngravingDefaults.BowEndRounding);
        return sky;
    }

    /// <summary>
    /// Whether this mark is engraved as an above/below <c>Script</c> — the ones
    /// <see cref="Calculate"/> side-positions OUT of the staff, and so the ones a staff's
    /// own skyline has to reserve a band for.
    /// </summary>
    /// <remarks>
    /// The bend family and the breathing signs are placed by the early branches of
    /// <see cref="Calculate"/> — at the note's own height, and at the staff's line span — so
    /// none of them is side-positioned OUT of the staff the way a Script is. Stated once, here,
    /// because the per-staff skyline pass (<c>MultiStaffLayouter</c>) and the drawn pass must
    /// agree about WHICH marks are Scripts; the previous spelling of this list lived in a
    /// tab-only copy of the placement.
    /// LILYPOND-REF: lily/breathing-sign.cc:259-277 <c>offset_callback</c> — a BreathingSign's
    /// Y-offset is <c>inter * iv[d]</c> off the staff symbol's own <c>line_span</c>, not a
    /// side-position walk over supports.
    /// <para>
    /// ⚠️ A BREATH'S OWN INK ABOVE THAT LINE IS THEREFORE STILL RESERVED NOWHERE, and LilyPond
    /// does put it in the merged skyline — BreathingSign declares
    /// <c>outside-staff-interface</c> (scm/define-grobs.scm:725-731). A caesura is in the same
    /// branch here, and there the two engines group differently: LilyPond gives it a grob of
    /// its own that stacks the way a Script does (scm/define-grobs.scm:734-738
    /// <c>caesura-script-interface::before-line-breaking</c>). This list is the one the
    /// tab-only helper carried before 2026-08-04 and is kept as it was; whether either mark
    /// protrudes far enough to bind a gap is UNMEASURED, not argued.
    /// </para>
    /// </remarks>
    internal static bool IsSidePositionedScript(ArticulationType type) =>
        type is not (ArticulationType.Fall or ArticulationType.Doit
            or ArticulationType.Bend or ArticulationType.Scoop or ArticulationType.Plop
            or ArticulationType.Breath or ArticulationType.Caesura);

    // LilyPond script-priority: lower = closer to the note. Only some scripts set
    // it explicitly; the rest use the Script grob default (0). These five arms are
    // ALL the declarations in scm/script.scm for types Lily# has a spelling for —
    // staccatissimo, portato, marcato and accent declare none and stay 0.
    // LILYPOND-REF: scm/script.scm; scm/define-grobs.scm Script.script-priority = 0.
    internal static int ScriptPriority(ArticulationType type) => type switch
    {
        ArticulationType.Staccato => -100,
        ArticulationType.Tenuto => -50,
        ArticulationType.Flageolet => 50,
        ArticulationType.Trill => 150,
        ArticulationType.Fermata or ArticulationType.FermataShort
            or ArticulationType.FermataLong => 175,
        ArticulationType.UpBow or ArticulationType.DownBow => 180,
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
            // The font's box, Y-extent (−0.5334 . 0.8000) = LilyPond's own grob extent
            // (after-line-breaking dump, articulation-snappizzicato book). It used to
            // fall to the half-space fallback below while the renderer drew primitives
            // reaching 1.85 ss — three different answers for one glyph.
            ArticulationType.SnapPizz => GlyphMetrics.ScriptSnappizzicato,
            // The trill's REAL font box: its origin is the ink BOTTOM (Bottom 0.000),
            // not the centre the ornament fallback assumes. The near extent this feeds
            // is what seats the trill: with the fallback's −0.5 the ①-clamp parked it
            // at 2.75; the real box lands 2.25 → the ②-refpoint floor lifts it to
            // 2.30, LilyPond's page exactly (probe-script-y: LP origin 2.30 = Δ0.45
            // of the articulations book closed).
            ArticulationType.Trill => GlyphMetrics.OrnTrillGlyph,
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
    /// <param name="a">The script whose profile this is.</param>
    /// <param name="anchorY">The glyph origin's Y in the caller's Y-up frame.</param>
    /// <param name="magnification">The size the STAFF is engraved at (an ossia's
    /// magstep(-3), 1.0 otherwise). LilyPond scales a glyph's whole metric by the context
    /// magnification — LILYPOND-REF: lily/modified-font-metric.cc:62-68 get_indexed_char_dimensions,
    /// which takes the original font's box and does <c>b.scale (magnification_)</c> — so it
    /// rides the font size the OUTLINE is walked at. It does NOT ride the horizon padding:
    /// that is a declared property LilyPond pads with as it stands (see ScriptSkylines).
    /// ⚠️ Only <c>SkylineBuilder</c> passes a magnification today; the below stacker's seed
    /// and the system skyline read a script full-size, exactly as the box spellings they
    /// replaced did. An ossia staff carrying a script UNDER a mover is the regime that would
    /// see the difference, and no fixture and no ledger point reaches it.
    /// ⚠️ The optical DESIGN is still the one this grob's own font-size step selects: an
    /// ossia is <c>fontSize = #-3</c> in LilyPond and would select the 14, which is a
    /// separate island — the box spelling this replaced scaled the 20 the same way.</param>
    internal static (VerticalSkyline Up, VerticalSkyline Down) ScriptSkylines(
        in ArticulationLayout a, double anchorY, double magnification = 1.0)
    {
        if (a.Glyph.Length == 1)
        {
            var (up, down) = TextOutlineSkylines.PlaceMusicGlyph(
                a.Glyph[0], WalkSize(a, magnification), a.X, anchorY,
                EmmentalerDesignSize.ForFontSizeStep(a.FontSizeStep).Rounded,
                a.SkylineHorizontalPadding);
            if (!up.IsEmpty || !down.IsEmpty)
                return (up, down);
        }
        return FallbackBoxSkylines(a, anchorY, magnification);
    }

    /// <summary>
    /// The same profile merged straight into <paramref name="target"/> at this script's
    /// (X, <paramref name="anchorY"/>), with no placed copy of its own — for the skyline
    /// BUILDERS, which merge and discard.
    /// </summary>
    /// <remarks>
    /// ⚠️ MEASURED, and it is why this overload exists rather than the caller taking
    /// <see cref="ScriptSkylines"/>'s pair: on a 512-script page a full layout allocated
    /// 295 MB against the box spelling's 139 MB when every consumer placed, padded and
    /// re-resolved its own copy. With the padded profile cached per (glyph, size, design,
    /// pad) and merged through <see cref="VerticalSkyline.Merge(IReadOnlyList{SkylineBuilding},
    /// double, double)"/>, the same page allocates within a per-cent of the box. The
    /// accidental island paid this exact tax first — see that Merge overload's remark.
    /// </remarks>
    internal static void MergeScriptProfile(
        VerticalSkyline target, in ArticulationLayout a, double anchorY,
        double magnification = 1.0)
    {
        bool up = target.Direction == VerticalDirection.Up;
        if (a.Glyph.Length == 1)
        {
            var profile = TextOutlineSkylines.MusicGlyphProfile(
                a.Glyph[0], WalkSize(a, magnification),
                EmmentalerDesignSize.ForFontSizeStep(a.FontSizeStep).Rounded,
                a.SkylineHorizontalPadding);
            var resolved = up ? profile.Up : profile.Down;
            if (resolved.Count > 0)
            {
                target.Merge(resolved, a.X, anchorY);
                return;
            }
        }
        var (boxUp, boxDown) = FallbackBoxSkylines(a, anchorY, magnification);
        target.Merge(up ? boxUp : boxDown);
    }

    /// <summary>The size the glyph's outline is walked at: what the renderer draws with
    /// (<c>SharedRenderer.FontSize</c> × this grob's magstep), times the staff's own
    /// magnification. The flattening happens at the transformed size, which is why the size
    /// is in the profile cache key.</summary>
    private static double WalkSize(in ArticulationLayout a, double magnification)
        => SharedRenderer.FontSize * EmmentalerDesignSize.Magstep(a.FontSizeStep) * magnification;

    /// <summary>
    /// The designed ink box, padded like any other profile — the fallback for the sentinel
    /// "glyphs" that have no single music character to walk (bends, fret frames, TAB
    /// technique letters, snap pizzicato) and the staff-local tab array.
    /// </summary>
    private static (VerticalSkyline Up, VerticalSkyline Down) FallbackBoxSkylines(
        in ArticulationLayout a, double anchorY, double magnification)
    {
        var (rawUp, rawDown) = RawScriptSkylines(a, anchorY, magnification);
        // THE PROFILE IS THE PADDED ONE. LilyPond does not hand anybody the bare outline: the
        // `vertical-skylines` property every consumer reads is the stencil's skyline PADDED
        // along the horizon by this script's own declaration.
        // LILYPOND-REF: lily/stencil-integral.cc:881-893 Grob::vertical_skylines_from_stencil
        //   — skylines_from_stencil(...) then p.pad (skyline-horizontal-padding).
        // ⚠️ MEASURED, and it is the difference between a right and a wrong obstacle: dumped
        // out of LilyPond for a staccato (audit/lp-geometry probes/dynamic-support.ly, DSK),
        // the stencil skyline is a polygon (±0.2 . 0) reaching 0.2 deep at ONE point, while
        // the property reads 0.2 flat across ±0.1 and runs out to ±0.4 — Skyline::padded's
        // flat-then-45°-sloped extension, corner for corner. Reading the bare outline put a
        // dynamic under a dot 0.12 too close; the marcato, which declares no padding, was
        // right either way.
        // ⚠️ The DECLARED number, not scaled by the staff's magnification: LilyPond hands
        // `p.pad` the property value as it stands, so a magnified staff pads its (smaller)
        // script by the same 0.10 a full-size one does. Scaling it here was this port's own
        // invention for one revision — the source says otherwise and so does the behaviour.
        // ⚠️ Padded AFTER placement where LilyPond pads the grob-local skyline: horizon
        // padding is a horizontal operation on buildings and commutes with the translation,
        // so the two orders agree.
        double pad = a.SkylineHorizontalPadding;
        return pad > 0.0 ? (rawUp.Padded(pad), rawDown.Padded(pad)) : (rawUp, rawDown);
    }

    /// <summary>The unpadded designed box. Nothing outside
    /// <see cref="FallbackBoxSkylines"/> should read this: LilyPond has no consumer of an
    /// unpadded profile.</summary>
    private static (VerticalSkyline Up, VerticalSkyline Down) RawScriptSkylines(
        in ArticulationLayout a, double anchorY, double magnification)
    {
        var box = a.Ink;
        double l = a.X + box.Left * magnification, r = a.X + box.Right * magnification;
        double b = anchorY + box.Bottom * magnification, t = anchorY + box.Top * magnification;
        return (VerticalSkyline.FromBox(l, r, b, t, VerticalDirection.Up),
                VerticalSkyline.FromBox(l, r, b, t, VerticalDirection.Down));
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
    /// ⚠️ Keyed by VOICE as well, and every voice's beams are in it: the key used to drop
    /// sub-voice groups because an articulation carried no voice and a sub-voice key could
    /// collide with a primary-voice item. It carries one now
    /// (<see cref="ArticulationItem.VoiceIndex"/>), so the exclusion would only hide a
    /// lower voice's beam from its own script.
    /// LILYPOND-REF: lily/stem.cc — a beamed stem's end comes from the beam;
    /// side-position then sees that real extent via the stem support.
    /// </summary>
    /// <summary>
    /// Maps each beamed item to its beam group by (staff, voice, measure, item), so the tab
    /// branch can find the group's outer beam edge (the tab beam Y lives only in the
    /// renderer's geometry, recomputed here from the group's members via TabStaffGeometry).
    /// </summary>
    private static Dictionary<(int Staff, int Voice, int Measure, int Item), BeamLayout>
        BuildBeamGroupMap(ImmutableArray<BeamLayout> beamLayouts)
    {
        var map = new Dictionary<(int, int, int, int), BeamLayout>();
        if (beamLayouts.IsDefaultOrEmpty)
            return map;
        foreach (var beam in beamLayouts)
        {
            var group = beam.Group;
            for (int i = 0; i < group.Members.Length; i++)
            {
                var member = group.Members[i];
                int staff = !beam.MemberStaffIndices.IsDefaultOrEmpty && i < beam.MemberStaffIndices.Length
                    ? beam.MemberStaffIndices[i]
                    : Math.Max(0, beam.StaffIndex);
                map[(staff, group.VoiceIndex,
                     member.ResolveMeasureIndex(group.MeasureIndex), member.ItemIndex)] = beam;
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
        var xs = new double[n];
        // Per MEMBER head shape, as the renderer's own DrawBeams does — a tremolo pair beams
        // HALF heads, whose attachment is 0.073200 further out, so one `attach` for the group
        // would quant this line against stems the renderer does not draw.
        for (int i = 0; i < n; i++)
            xs[i] = (i < beam.MemberXPositions.Length ? beam.MemberXPositions[i] : 0)
                  + LayoutUtilities.StemAttachX(
                        up, GlyphMetrics.NoteValueOf(beam.Group.Members[i].Item),
                        beam.Group.Members[i].Item switch
                        {
                            NoteItem note => note.Notehead,
                            ChordItem chord => chord.Notehead,
                            _ => NoteheadStyle.Default,
                        });
        var line = TabBeamQuant.Compute(beam.Group, xs, geom, up);
        double half = EngravingDefaults.BeamThickness / 2;
        return TabBeamMath.At(line, noteX) + (up ? -half : half);
    }

    private static Dictionary<(int Staff, int Voice, int Measure, int Item), (BeamLayout Beam, double MemberX, bool StemUp)>
        BuildBeamedStemTips(ImmutableArray<BeamLayout> beamLayouts)
    {
        var tips = new Dictionary<(int, int, int, int), (BeamLayout, double, bool)>();
        if (beamLayouts.IsDefaultOrEmpty)
            return tips;
        foreach (var beam in beamLayouts)
        {
            var group = beam.Group;
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
                tips[(staff, group.VoiceIndex,
                      member.ResolveMeasureIndex(group.MeasureIndex), member.ItemIndex)] =
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
            ? (stemUp ? StemSupportExtent(item, column) : HeadSupportExtent(column))
            : (!stemUp ? StemSupportExtent(item, column) : HeadSupportExtent(column));

        // dist = skyline distance; total_off = dist + padding. In Y-up an above
        // script sits ABOVE the note (+) and a below script BELOW (−).
        double totalOff = supportExtent + glyphNearExtent + PaddingFor(articulation.Type);
        double targetUp = isAbove ? noteUp + totalOff : noteUp - totalOff;

        // TWO staff clearances stack on a non-quantized script, and they are
        // different quantities (probe-script-y measured both):
        // ① include_staff: with staff-padding set (and no quantize), the STAFF INK
        //    itself joins the support skyline, so the glyph's near edge clears the
        //    outer line's ink by the script's own padding: ink edge ≥ 2.05 + 0.20
        //    = 2.25 — numerically the old StaffHalf + 0.25 clamp, kept as is.
        //    LP's accent over c'' sits exactly there (origin 2.67 = 2.25 + 0.42).
        //    ⚠️ LILYSHARP-OWN: the 2.25 is FLAT, but under the include_staff reading
        //    it should be 2.05 + PaddingFor(type) — identical only for the
        //    padding-0.20 scripts. A fermata (script.scm padding 0.40) would want
        //    2.45; no ledger point or book observes a fermata where this floor
        //    BINDS (the script.* ledger points all pass either way), so the flat
        //    constant stays until a measurement decides it.
        // LILYPOND-REF: lily/side-position-interface.cc:217-223 include_staff —
        //   staff-padding present && !quantize_position puts staff_symbol in common.
        // ② the staff-padding floor proper, on the REFPOINT (total_off): refpoint ≥
        //    staff ink edge + staff-padding = 2.05 + 0.25 = 2.30. It only bites a
        //    glyph whose ink barely dips below its origin (the trill: LP origin
        //    exactly 2.30).
        // LILYPOND-REF: lily/side-position-interface.cc:433-453 staff_padding —
        //   diff = dir * staff_extent[dir] + staff_padding - dir * total_off …;
        //   total_off += dir * max (diff, 0).
        double inkFloor = StaffHalf + StaffPadding; // ① = 2.05 ink + 0.20 padding
        double refpointFloor = StaffHalf + EngravingDefaults.StaffLineThickness / 2
            + StaffPadding;                         // ② = 2.05 ink + 0.25
        if (isAbove)
        {
            double glyphEdgeUp = targetUp - glyphNearExtent;
            if (glyphEdgeUp < inkFloor)
                targetUp = inkFloor + glyphNearExtent;
            return Math.Max(targetUp, refpointFloor);
        }
        else
        {
            double glyphEdgeUp = targetUp + glyphNearExtent;
            if (glyphEdgeUp > -inkFloor)
                targetUp = -inkFloor - glyphNearExtent;
            return Math.Min(targetUp, -refpointFloor);
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
        // A rest (no column) has no stem to clear — see HeadSupportExtent for what
        // LilyPond puts there instead, and why the nominal half still stands.
        return column?.StemSupportDistanceDeviceY() ?? HeadSupportExtent(column);
    }

    /// <summary>
    /// The SUPPORT's own reach on the script's side when the stem is not in it: the head's
    /// own extent, asked per head, rather than a nominal half space.
    /// </summary>
    /// <remarks>
    /// A NoteHead declares no <c>vertical-skylines</c> anywhere in its block, so the skyline
    /// each support contributes — <c>side-position-interface.cc</c> reads that property off
    /// every one of them and merges — falls back to the head's extent, and the head's extent
    /// is its STENCIL's (the block does declare <c>Y-extent</c> from the stencil), which for a
    /// glyph is the designed LILC box. The citation lives in <see cref="GlyphMetrics"/>'s
    /// skyline block, beside <see cref="GlyphMetrics.GetNoteheadBBox(int)"/>, where the same
    /// question was settled by LilyPond's own dump: the notehead reads 0.545 for extent AND
    /// skyline while its outline stops at 0.544, so this is the metric box and not the
    /// traced one. It is the same read <see cref="NoteColumnLayout.OutwardTipDeviceY"/>
    /// already takes. <c>bbox.Top</c> serves
    /// BOTH sides: MEASURED over the whole extraction, all 24 head entries (8 designs x whole,
    /// half, black) have <c>Bottom == -Top</c> exactly.
    /// <para>
    /// ⚠️ ASKING PER HEAD BUYS NOTHING TODAY, AND SAYING SO IS THE POINT. In the same
    /// measurement the three shapes share one extent inside each design (0.545000 at design
    /// 20), so what actually varies is the DESIGN, not the shape — and this read takes design
    /// 20 for a cue or grace head as the sibling read does, which is that house's recorded
    /// debt rather than a new one. The lookup is per head because LilyPond's is; a book where
    /// the shapes diverge would then be right for free instead of silently wrong.
    /// </para>
    /// <para>
    /// ⚠️ A REST KEEPS THE NOMINAL HALF, AND IT IS NOT THE SAME QUANTITY. LilyPond's
    /// <c>Script_engraver</c> takes a Rest as a support too, and a Rest DOES declare
    /// from-stencil skylines, so the extent there is the rest glyph's OUTLINE — a different
    /// box per duration, which Lily# does not read for this purpose. No ledger point observes
    /// a script on a rest today, so the nominal
    /// <see cref="EngravingDefaults.NoteheadHalfHeight"/> stays: replacing it with a head's
    /// ink would be a number with no support behind it, which is worse than a named nominal.
    /// </para>
    /// </remarks>
    private static double HeadSupportExtent(NoteColumnLayout? column)
        => column is { } c ? GlyphMetrics.GetNoteheadBBox(c.NoteValue).Top : NoteheadHalfHeight;

    private static double QuantizedYPosition(double noteUp, bool isAbove, bool stemUp,
        ArticulationType type, MusicItem? item = null, NoteColumnLayout? column = null)
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
                ? StemSupportExtent(item, column)
                : HeadSupportExtent(column);
            // ↑ if stemUp AND isAbove: stem IS in support (forced above case), real stem tip
            // ↑ if !stemUp AND isAbove: stem skipped, just the notehead's own ink top
        }
        else
        {
            // For below: support's DOWN extent
            supportExtent = !stemUp
                ? StemSupportExtent(item, column)
                : HeadSupportExtent(column);
            // ↑ if !stemUp AND !isAbove: stem IS in support (forced below case), real stem tip
            // ↑ if stemUp AND !isAbove: stem skipped, just the notehead's own ink bottom
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

        // ⚠️ A tall quantized script STAYS at its quantized in-staff position — an
        // earlier guard here re-seated any quantized glyph taller than 1.0 ss outside
        // the staff, claiming to "reproduce LP's result directly". MEASURED FALSE
        // (scratch/lpreg/probe-script-y.{ly,svg}): LilyPond's own page puts a forced-up
        // marcato over c'' (and over c', via its stem tip) at staff POSITION 3 — origin
        // 1.5 ss above the middle, INSIDE the staff, the chevron straddling the top
        // line — 0.70 below where the guard seated it. With the guard gone all four
        // probe marcatos (c''/g'/e'/c') land on LilyPond's positions exactly:
        // 3 / 5.4 (past the +5 span gate, unquantized) / 5 (rounded 4 = a line, +1) / 3.
        return targetUp;
    }
}
