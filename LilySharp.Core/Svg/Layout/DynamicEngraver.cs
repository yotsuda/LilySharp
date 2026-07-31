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
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for a dynamic marking.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: define-grobs.scm:1433-1460 DynamicText grob
/// LILYPOND-REF: define-grobs.scm:1401-1431 DynamicLineSpanner grob
/// </remarks>
public readonly record struct DynamicLayout(
    int MeasureIndex,       // Measure containing this dynamic
    int ItemIndex,          // Item index within measure (for X alignment)
    double X,               // Absolute X position (staff spaces from score start)
    double YUp,             // Y in the LilyPond-native Y-up frame: staff-spaces ABOVE
                            // this dynamic's staff middle line, up-positive (frame B).
                            // The renderer/stacker reflect it to device (middle − Y-up)
                            // against the staff middle they resolve.
    string Text,            // Dynamic text ("p", "ff", etc.)
    int SourcePosition,     // For click-to-source mapping (re-derived at render from SourceIndex)
    int SourceIndex = -1,   // F3/B: index into score.Dynamics — the position-independent
                            // reference the renderer resolves data-pos from the LIVE score, so a
                            // reused (cached) layout emits fresh data-pos. See SharedRenderer.ResolveDataPos.
                            // -1 = "no source" (left unresolved): used by unit tests that build layouts directly.
    bool IsAbove = false,   // Forced above the staff (from @f.up); default below.
    int StaffIndex = 0,     // Which staff this dynamic hangs under (per-staff stacking).
    bool IsExpressiveText = false // @text("…"): plain italic, not a dynamic level.
);

/// <summary>
/// Calculates positions for dynamic markings.
/// Implements LilyPond's dynamic positioning algorithm.
/// </summary>
/// <remarks>
/// LILYPOND-REF: dynamic-align-engraver.cc:36-61 Dynamic_align_engraver class
/// LILYPOND-REF: side-position-interface.cc:92-111 axis_aligned_side_helper
///
/// LilyPond places dynamics below the staff (direction = DOWN) with:
/// - outside-staff-priority: 250
/// - padding: 0.6 staff spaces
/// - staff-padding: 0.1 staff spaces
/// - Y-offset calculated by side-position-interface::y-aligned-side
/// </remarks>
internal static class DynamicEngraver
{
    // One home: EngravingDefaults' outside-staff declaration table (define-grobs.scm
    // values; the LILYPOND-REFs live beside the table entries).
    private const double Padding = EngravingDefaults.DynamicLineSpannerPadding;
    private const double StaffPadding = EngravingDefaults.DynamicLineSpannerStaffPadding;
    private const double MinimumSpace = EngravingDefaults.DynamicLineSpannerMinimumSpace;

    // LILYPOND-REF: define-grobs.scm:1450 DynamicText (Y-offset . (scale-by-font-size
    //   -0.6)) — "center on an 'm'". side-position places the SPANNER, and the text hangs
    //   this far below the spanner's own origin, so the two frames differ by 0.6.
    private const double TextOffsetInSpanner = 0.6;

    // Staff geometry (5 lines = 4 staff spaces)
    private const double StaffMiddle = EngravingDefaults.StaffMiddle;  // staff bottom (4.0) / 2

    // LILYSHARP-OWN: ink above / below the baseline for a label LilyPond does NOT spell in
    // the fetaText dynamic letters — free expressive text (@text), which has no LilyPond
    // grob at all: Lily# rides it on the DynamicText pipeline and draws it in a serif face
    // (SharedRenderer.DrawDynamics). There is no LilyPond formula to port, so these stay
    // nominal. A real dynamic never reaches them: GlyphMetrics.TryGetDynamicInk answers
    // from the font instead, per glyph.
    // ⚠️ DEBT, carried unchanged rather than re-tuned: these are the values the code
    // already had, and 0.64's old comment derived it by MEASURING a LilyPond 2.24.4
    // forced-up dynamic. That derivation is void — the thing it was fitting is the `f`
    // glyph's 0.692002 ink, which now comes from the font — but replacing the number for
    // free text would be fitting a second time. It needs a source, not a better guess.
    // These are also the single fallback for all three paths that used to keep their own
    // (this file's 0.64, the stacker's 0.3, the skyline's 0.3): three numbers for ONE
    // quantity is the duplication that let the real defect hide. 0.64 is the largest, so
    // unifying on it can only reserve more room, never overlap.
    private const double FallbackAscent = 1.2;
    private const double FallbackDescent = 0.64;

    /// <summary>
    /// A label's own ink above (<c>Ascent</c>) and below (<c>Descent</c>) its baseline,
    /// in staff spaces. See <see cref="GlyphMetrics.TryGetDynamicInk"/>: LilyPond's
    /// DynamicText extent is the drawn glyphs' ink, so it differs per dynamic.
    /// </summary>
    internal static (double Ascent, double Descent) InkOf(string? text, bool expressive)
        => !expressive && GlyphMetrics.TryGetDynamicInk(text, out double bottom, out double top)
            ? (top, -bottom)
            : (FallbackAscent, FallbackDescent);

    // Vertical step between two dynamics that fall on the same note column.
    internal const double StackStep = 2.0;

    /// <summary>
    /// Calculates layout for all dynamics in a score.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: side-position-interface.cc:193-400 aligned_side()
    /// LILYPOND-REF: dynamic-align-engraver.cc:120-180 process_acknowledged()
    ///
    /// Dynamics are placed below the staff, avoiding collision with notes
    /// that extend below the staff (low notes, stems down).
    /// </remarks>
    public static ImmutableArray<DynamicLayout> Calculate(
        Score score,
        ImmutableArray<DynamicItem> dynamics,
        ImmutableArray<MeasureLayout> measureLayouts,
        ImmutableArray<Voice> voices = default,
        Dictionary<int, ImmutableArray<Voice>>? voicesByStaff = null,
        Dictionary<int, ImmutableArray<Measure>>? measuresByStaff = null,
        ImmutableArray<BeamLayout> beamLayouts = default)
    {
        if (dynamics.IsDefaultOrEmpty)
            return ImmutableArray<DynamicLayout>.Empty;

        var layouts = ImmutableArray.CreateBuilder<DynamicLayout>(dynamics.Length);
        var beamMembers = BuildBeamMembers(beamLayouts);

        var fallbackVoices = voices.IsDefaultOrEmpty ? ImmutableArray.Create(score.Voice) : voices;

        // Two voices can carry a dynamic on the SAME note column (e.g. an upper
        // voice @f and a lower voice @p in a << \\ >>). They share (measure,
        // item) and would draw on top of each other; stack the 2nd+ downward so
        // both stay legible. Keyed by staff too: same-column dynamics on
        // DIFFERENT staves are independent and must not stack onto each other.
        // Keyed by side too (IsAbove): a column may carry one dynamic above and one
        // below, each stacking AWAY from the staff independently.
        var stackAt = new Dictionary<(int, int, int, bool), int>();

        for (int di = 0; di < dynamics.Length; di++)
        {
            var dynamic = dynamics[di];
            // Find the measure layout
            if (dynamic.MeasureIndex >= measureLayouts.Length)
                continue;

            var measureLayout = measureLayouts[dynamic.MeasureIndex];

            // Bounds guard (single-staff layouts only; multi-staff layouts
            // resolve through timing-aligned columns).
            if (measureLayout.Columns.IsDefaultOrEmpty
                && dynamic.ItemIndex >= measureLayout.Items.Length)
                continue;

            // Resolve this dynamic's OWN staff: its voices (to clear the right
            // stems), its measures (for timing), and the staff's vertical offset
            // within the system (so it sits under its own staff, not the first).
            var dynVoices = voicesByStaff != null
                && voicesByStaff.TryGetValue(dynamic.StaffIndex, out var vv) ? vv : fallbackVoices;
            var dynMeasures = LayoutUtilities.ResolveStaffMeasures(measuresByStaff, dynamic.StaffIndex, score.Voice.Measures);

            // The COLUMN's X (the drawn head starts here), and the label's own anchor:
            // LilyPond X-aligns the DynamicText's extent CENTRE on its X-parent — the
            // dynamic's own voice's note column — so the label centres on that item's
            // head, half an advance right of the column X.
            // LILYPOND-REF: define-grobs.scm:1444 self-alignment-X = CENTER;
            //   lily/self-alignment-interface.cc aligned_on_parent (the parent extent's
            //   centre; measured for the anchor classes in
            //   audit/lp-geometry — see reference self-alignment-parent-extent).
            double xColumn = measureLayout.X + LayoutUtilities.GetItemXOffset(
                dynMeasures, dynamic.MeasureIndex, dynamic.ItemIndex, measureLayout);
            double x = xColumn + AnchorCentreOffset(
                AnchorItem(dynVoices, dynamic.VoiceIndex, dynamic.MeasureIndex, dynamic.ItemIndex));

            // The supports: EVERY voice's note column at this timing (a lower voice's
            // down-stem must not be overlapped by a dynamic positioned from the upper
            // voice's stem-up note), floored by the staff symbol — POINTWISE: heads and
            // real stems as extent boxes at their own X, the distance taken against the
            // label's own outline (my_dim), so the head wins under a narrow \f while the
            // stem binds under a wide \fff. audit/lp-geometry staff.staff.dynamic-*.
            // LILYPOND-REF: dynamic-align-engraver.cc:108-117 acknowledge_rhythmic_head + acknowledge_stem;
            //   side-position-interface.cc:353-358 pointwise Skyline::distance to my_dim.
            int staffIdx = dynamic.StaffIndex;
            int mi = dynamic.MeasureIndex, ii = dynamic.ItemIndex;
            double y = PointwiseBaselineY(dynamic.IsAbove, dynVoices, dynamic.VoiceIndex,
                mi, ii, xColumn, x, dynamic.Text, dynamic.IsExpressiveText,
                vi => beamMembers.TryGetValue((staffIdx, vi, mi, ii), out var b) ? b : null);

            var key = (dynamic.MeasureIndex, dynamic.ItemIndex, dynamic.StaffIndex, dynamic.IsAbove);
            int depth = stackAt.GetValueOrDefault(key, 0);
            stackAt[key] = depth + 1;
            // Stack each successive same-column dynamic AWAY from the staff. In the
            // native Y-up frame that is up (+) for above and down (−) for below.
            y += (dynamic.IsAbove ? depth : -depth) * StackStep;

            // y is already in the LilyPond-native Y-up frame (staff-spaces above the
            // staff middle); no staff offset is baked — the renderer/stacker resolve
            // the staff middle at their own boundary.
            double yUp = y;

            layouts.Add(new DynamicLayout(
                dynamic.MeasureIndex,
                dynamic.ItemIndex,
                x,
                yUp,
                dynamic.Text,
                dynamic.SourcePosition,
                di,
                dynamic.IsAbove,
                dynamic.StaffIndex,
                dynamic.IsExpressiveText
            ));
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// The staff symbol's own extent on either side — the outermost line's INK, half a
    /// line thickness past its centre. Written as the derivation, not as 2.05.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/side-position-interface.cc:323-330 —
    ///   <c>if (include_staff) dim.set_minimum_height (staff_extents[dir]);</c> puts the
    ///   staff symbol's extent under the supports' skyline as a MINIMUM, so a dynamic with
    ///   nothing hanging below it still sides off the staff. Asked of the grob, the staff
    ///   extent is (-2.05 . 2.05) — the same ink 854a0e95 seeded into the skylines.
    ///   <c>include_staff</c> (:217-220) is true exactly because DynamicLineSpanner sets
    ///   staff-padding.
    /// </remarks>
    /// <remarks>Exposed for <see cref="TrillSpannerEngraver"/>, the other aligned_side
    /// consumer that declares staff-padding — one home for the staff symbol's own extent
    /// rather than a second spelling of the derivation.</remarks>
    internal static double StaffExtent
        => StaffMiddle + EngravingDefaults.StaffLineThickness / 2;

    /// <summary>
    /// The DynamicLineSpanner's OWN offset that <c>side-position-interface</c> produces
    /// over the given SUPPORT skylines on the <paramref name="dir"/> side (+1 up, −1
    /// down), in the native Y-up frame — the distance is POINTWISE against the spanner's
    /// own outline (<paramref name="spannerDim"/>, its elements composed about the
    /// spanner origin).
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS RETURNS THE SPANNER, NOT A BASELINE. LilyPond hangs BOTH dynamic grobs off
    /// ONE DynamicLineSpanner (define-grobs.scm:1428-1431 says so in its own description),
    /// and they sit at DIFFERENT offsets inside it: DynamicText at
    /// <see cref="TextOffsetInSpanner"/> below (self-alignment on an 'm'), the Hairpin
    /// centred on it (<c>self-alignment-Y . CENTER</c>, define-grobs.scm Hairpin). So each
    /// caller spends its own child offset — <see cref="PointwiseBaselineY"/> for the text,
    /// <see cref="HairpinEngraver"/> for the wedge, which spends none.
    /// </remarks>
    /// <remarks>
    /// LILYPOND-REF: lily/side-position-interface.cc:188-455 aligned_side, transcribed for
    ///   this grob (side-axis Y, so <c>a == Y_AXIS</c> and <c>ss == 1</c> staff space):
    /// <code>
    ///   :323-330  if (include_staff) dim.set_minimum_height (staff_extents[dir]);
    ///   :354-358  total_off = dir * dim.distance (my_dim, horizon-padding);
    ///   :370      total_off += dir * ss * padding;
    ///   :384-385  if (minimum_space >= 0 &amp;&amp; total_off * dir &lt; minimum_space)
    ///               total_off = minimum_space * dir;
    ///   :433-453  diff = dir * staff_extent[dir] + staff_padding - dir * total_off;
    ///             total_off += dir * max (diff, 0.0);
    /// </code>
    ///   <c>my_dim</c> is the spanner's OWN skyline on the facing side (<c>skyp[-dir]</c>,
    ///   :225,259), so the distance is taken to that edge and not to its origin — which is
    ///   why the text's -0.6 offset inside the spanner enters here and cancels again when
    ///   the baseline is read back out.
    /// ⚠️ staff-padding is NOT a second padding, which is what Lily# used to spend: the
    ///   :433-453 block is a FLOOR on the grob's own refpoint, reached only when the
    ///   supports put the grob nearer than that. Nor does the staff enter at its line
    ///   CENTRES; :323-330 takes its extent. The two errors cancelled, which is how a test
    ///   pinned to LilyPond's forced-up clearance passed for so long — 2.0 + 0.1 + 0.6 and
    ///   a nominal 0.64 descent reach the same total as 2.05 + 0.6 and the `f` glyph's
    ///   own 0.692002.
    /// </remarks>
    internal static double SpannerOffsetY(double dir,
        (VerticalSkyline Up, VerticalSkyline Down) support,
        (VerticalSkyline Up, VerticalSkyline Down) spannerDim)
    {
        // :354-358 — dir * the POINTWISE distance of my facing profile (skyp[-dir],
        // the spanner's own outline about its origin) to the supports' skyline.
        double overlap = dir > 0
            ? spannerDim.Down.Distance(support.Up)
            : spannerDim.Up.Distance(support.Down);
        double totalOff = dir * overlap;
        // :370
        totalOff += dir * Padding;
        // :384-385
        if (totalOff * dir < MinimumSpace)
            totalOff = MinimumSpace * dir;
        // :433-453 — dir * staff_extent[dir] is StaffExtent on either side, and
        // (staff_position - parent_position) drops out: both refpoints are this staff.
        double diff = StaffExtent + StaffPadding - dir * totalOff;
        totalOff += dir * Math.Max(diff, 0.0);

        return totalOff;
    }

    /// <summary>
    /// Baseline Y in the native Y-up frame (staff-spaces above the staff middle,
    /// up-positive; a below-staff dynamic is negative) the dynamic at a given note
    /// column occupies, BEFORE same-column stacking and BEFORE the outside-staff
    /// collision pass — LilyPond's <c>aligned_side</c>, transcribed pointwise.
    /// Shared by <see cref="Calculate"/> (the draw side) and
    /// <c>SkylineBuilder.AddDynamicsToSkyline</c> (the inter-staff seed) so the two
    /// homes run ONE spelling of the quiet position.
    /// </summary>
    /// <param name="xColumn">The note column's X (the drawn head starts here).</param>
    /// <param name="xLabel">The label's centre (column + the anchor's half advance).</param>
    internal static double PointwiseBaselineY(bool above,
        ImmutableArray<Voice> voices, int voiceIndex, int measureIndex, int itemIndex,
        double xColumn, double xLabel, string? text, bool expressive,
        Func<int, (BeamLayout Beam, double MemberX, bool StemUp)?>? beamOf)
    {
        var support = ColumnSupportSkylines(
            voices, voiceIndex, measureIndex, itemIndex, xColumn, beamOf);
        var my = LabelSkylines(text, expressive, xLabel, -TextOffsetInSpanner);
        // total_off positions the SPANNER; the text's baseline sits TextOffsetInSpanner
        // below it (define-grobs.scm:1450 DynamicText Y-offset, "center on an 'm'").
        return SpannerOffsetY(above ? 1.0 : -1.0, support, my) - TextOffsetInSpanner;
    }

    /// <summary>
    /// The side-position SUPPORT skylines of the dynamic's OWN voice's note column: its
    /// head as an extent box at the drawn head's X, its direction-matching REAL stem as a
    /// thin extent box at the stem's own X (a beamed stem ends on the quanted beam face),
    /// the staff symbol's extent as the minimum. Y-up about the staff middle; X absolute.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly:359,410 Dynamic_align_engraver — the
    ///   <c>\name Voice</c> context (:359) consists it (:410), so the supports are the
    ///   dynamic's own voice's grobs; the OTHER voice's ink reaches the dynamic through
    ///   the outside-staff collision pass over the whole staff profile
    ///   (axis-group-interface.cc:648-676 avoid_outside_staff_collisions), never through
    ///   the side-position support. (Until 2026-07-29 Lily# unioned every voice here as
    ///   its own compensation for that missing pass.)
    /// LILYPOND-REF: dynamic-align-engraver.cc:108-117 acknowledge_rhythmic_head / acknowledge_stem —
    ///   heads AND stems into <c>support_</c> (:222-223 <c>add_support</c>).
    /// LILYPOND-REF: grob.cc:81-85 simple_vertical_skylines_from_extents — each support
    ///   enters as its extent BOX (the notehead's LILC ink, the stem's drawn span).
    /// LILYPOND-REF: side-position-interface.cc:273-281 get_grob_direction skip — a stem
    ///   is kept only when its direction matches the spanner's side;
    ///   :323-330 set_minimum_height — the staff extent as minimum.
    /// </remarks>
    internal static (VerticalSkyline Up, VerticalSkyline Down) ColumnSupportSkylines(
        ImmutableArray<Voice> voices, int voiceIndex, int measureIndex, int itemIndex,
        double xColumn,
        Func<int, (BeamLayout Beam, double MemberX, bool StemUp)?>? beamOf)
    {
        var (up, down) = StaffFloorSupport();
        MergeColumnSupport(up, down, voices, voiceIndex, measureIndex, itemIndex, xColumn, beamOf);
        return (up, down);
    }

    /// <summary>
    /// The side-position SUPPORT skylines of EVERY note column a DynamicLineSpanner runs
    /// over — the same head/stem ingredients as <see cref="ColumnSupportSkylines"/>, merged
    /// across the whole span, floored by the staff symbol.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/dynamic-align-engraver.cc:222-223 <c>add_support</c> — the engraver
    ///   calls <c>add_support (line_, support_[i])</c> at EVERY <c>stop_translation_timestep</c>
    ///   while the line spanner is alive, so a hairpin's supports are all the heads and
    ///   stems from its first timestep to its last, not just one column. (A DynamicText
    ///   alone ends its line in the timestep that created it — that is why
    ///   <see cref="ColumnSupportSkylines"/> is the one-column case of this and not a
    ///   different rule.)
    /// </remarks>
    /// <param name="columns">(measure, item, column X) for each timestep in the span.</param>
    internal static (VerticalSkyline Up, VerticalSkyline Down) SpanSupportSkylines(
        ImmutableArray<Voice> voices, int voiceIndex,
        IEnumerable<(int Measure, int Item, double X)> columns,
        Func<int, int, int, (BeamLayout Beam, double MemberX, bool StemUp)?>? beamOf)
    {
        var (up, down) = StaffFloorSupport();
        // A span is many boxes into one skyline, which is the shape Merge's batch mode
        // exists for: append now, resolve once, O(K log K) instead of O(K²). Byte-identical
        // — the resolve keeps the highest at each point, and max is commutative.
        up.BeginBatch();
        down.BeginBatch();
        foreach (var (mi, ii, x) in columns)
            MergeColumnSupport(up, down, voices, voiceIndex, mi, ii, x,
                beamOf is null ? null : vi => beamOf(vi, mi, ii));
        up.EndBatch();
        down.EndBatch();
        return (up, down);
    }

    /// <summary>
    /// :323-330 — the staff extent is the floor under whatever the columns contribute, over
    /// the WHOLE horizon.
    /// </summary>
    /// <remarks>
    /// ⚠️ IT USED TO BE BOUNDED, and that was a workaround rather than a model choice:
    /// <see cref="VerticalSkyline.Merge"/> dropped an unbounded building's tails, so a
    /// note-column box punched the floor out from under the rest of a span (MEASURED
    /// 2026-07-31: a wedge whose distance should have been 2.7666 to a −2.05 floor came
    /// back 1.0849). The merge carries them now — LilyPond's own invariant, kept there by
    /// <c>empty_skyline</c> / <c>single_skyline</c> (skyline.cc:259-282) — so this is back to
    /// what <c>set_minimum_height</c> says: no horizon at all, the whole dim is raised.
    /// </remarks>
    private static (VerticalSkyline Up, VerticalSkyline Down) StaffFloorSupport()
        => (VerticalSkyline.FromBox(
                double.NegativeInfinity, double.PositiveInfinity,
                StaffExtent, StaffExtent, VerticalDirection.Up),
            VerticalSkyline.FromBox(
                double.NegativeInfinity, double.PositiveInfinity,
                -StaffExtent, -StaffExtent, VerticalDirection.Down));

    /// <summary>Merges ONE note column's head and direction-matching real stem into an
    /// accumulating support pair. See <see cref="ColumnSupportSkylines"/> for the refs.</summary>
    private static void MergeColumnSupport(
        VerticalSkyline up, VerticalSkyline down,
        ImmutableArray<Voice> voices, int voiceIndex, int measureIndex, int itemIndex,
        double xColumn,
        Func<int, (BeamLayout Beam, double MemberX, bool StemUp)?>? beamOf)
    {
        var vs = voices.IsDefaultOrEmpty ? ImmutableArray<Voice>.Empty : voices;
        if (vs.Length > 0)
        {
            int vi = Math.Clamp(voiceIndex, 0, vs.Length - 1);
            var voice = vs[vi];
            if (measureIndex < voice.Measures.Length
                && itemIndex < voice.Measures[measureIndex].Items.Length)
            {
                var item = voice.Measures[measureIndex].Items[itemIndex];
                // Direction policy stays the caller side's (NoteColumnLayout's contract):
                // multi-voice forcing, overridden by the beam's resolved direction.
                bool? forcedStemUp = vs.Length > 1 ? VoiceDefaults.GetDefaultStemUp(vi + 1) : null;
                var beamInfo = beamOf?.Invoke(vi);
                if (beamInfo is { } bi)
                    forcedStemUp = bi.StemUp;
                if (NoteColumnLayout.Of(item, forcedStemUp,
                        beamInfo?.Beam, beamInfo?.MemberX ?? 0.0) is { } col)
                {
                    // The head's extent box, at the drawn head's X (the glyph starts at
                    // the column X and runs one advance right).
                    double advance = GlyphMetrics.GetNoteheadAdvance(col.NoteValue);
                    var ink = GlyphMetrics.GetNoteheadBBox(col.NoteValue);
                    double headTop = col.TopHeadPosition * 0.5 + ink.Top;
                    double headBottom = col.BottomHeadPosition * 0.5 + ink.Bottom;
                    up.Merge(VerticalSkyline.FromBox(
                        xColumn, xColumn + advance, headBottom, headTop, VerticalDirection.Up));
                    down.Merge(VerticalSkyline.FromBox(
                        xColumn, xColumn + advance, headBottom, headTop, VerticalDirection.Down));

                    // The REAL stem — drawn length (shortening, middle-line pull,
                    // beam-quanted face) at its own thin X: the renderer's attach (down
                    // at the head's left edge, up at the black head's right edge),
                    // StemThickness wide.
                    if (col.HasStem)
                    {
                        double tipUp = StaffMiddle - col.OutwardTipDeviceY(col.StemUp);
                        double anchorUp = col.HeadPositionToward(col.StemUp) * 0.5;
                        double stemCentre = LayoutUtilities.StemX(xColumn, col.StemUp);
                        double half = EngravingDefaults.StemThickness / 2;
                        if (col.StemUp)
                            up.Merge(VerticalSkyline.FromBox(stemCentre - half, stemCentre + half,
                                anchorUp, tipUp, VerticalDirection.Up));
                        else
                            down.Merge(VerticalSkyline.FromBox(stemCentre - half, stemCentre + half,
                                tipUp, anchorUp, VerticalDirection.Down));
                    }
                }
                // A rest has no head/stem grob to support off — the staff floor stands.
            }
        }
    }

    /// <summary>
    /// The label's OWN skyline pair (<c>my_dim</c>): the fetaText letters' baked
    /// outlines composed at advance+kern pen positions, centred on
    /// <paramref name="xCentre"/> with the baseline at <paramref name="yBaseline"/> —
    /// or the serif box for a label with no feta outline (free expressive text).
    /// </summary>
    internal static (VerticalSkyline Up, VerticalSkyline Down) LabelSkylines(
        string? text, bool expressive, double xCentre, double yBaseline)
    {
        if (!expressive && text is { Length: > 0 }
            && DynamicOutline.AdvanceWidth(text) is { } w
            && DynamicOutline.Place(text, xCentre - w / 2.0, yBaseline) is { } outline)
            return outline;
        var (ascent, descent) = InkOf(text, expressive);
        double half = LabelHalfWidth(text ?? "", expressive);
        return (VerticalSkyline.FromBox(xCentre - half, xCentre + half,
                    yBaseline - descent, yBaseline + ascent, VerticalDirection.Up),
                VerticalSkyline.FromBox(xCentre - half, xCentre + half,
                    yBaseline - descent, yBaseline + ascent, VerticalDirection.Down));
    }

    /// <summary>
    /// The outside-staff collision move (≤ 0, downward) that clears a below-staff
    /// grob's UP profile off a staff's DOWN profile by <paramref name="padding"/>,
    /// pointwise — one spelling shared by the inter-staff seed
    /// (<c>SkylineBuilder.AddDynamicsToSkyline</c>) and equivalent to the stacker's
    /// support-entry placement (<c>OutsideStaffStacker</c>'s tracker: the support entry
    /// forbids <c>(-(d+pad), ∞)</c> and the nearest allowed move at or below 0 is
    /// exactly this).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:648-676 avoid_outside_staff_collisions
    ///   — pointwise, outside-staff-padding.
    /// </remarks>
    internal static double BelowCollisionMove(
        VerticalSkyline profileDown, VerticalSkyline myUp, double padding)
    {
        double d = myUp.Distance(profileDown) + padding;
        return d > 0 ? -d : 0.0;
    }

    /// <summary>
    /// Beam membership per (staff, voice, measure, item) across ALL voices — the
    /// dynamics' beam lookup (a lower-voice beamed pair carries the dynamic in the
    /// probe books, so the voice-0-only articulation map cannot serve here).
    /// </summary>
    internal static Dictionary<(int Staff, int Voice, int Measure, int Item),
        (BeamLayout Beam, double MemberX, bool StemUp)> BuildBeamMembers(
        ImmutableArray<BeamLayout> beamLayouts)
    {
        var map = new Dictionary<(int, int, int, int), (BeamLayout, double, bool)>();
        if (beamLayouts.IsDefaultOrEmpty)
            return map;
        foreach (var beam in beamLayouts)
        {
            var group = beam.Group;
            for (int i = 0; i < group.Members.Length && i < beam.MemberXPositions.Length; i++)
            {
                var member = group.Members[i];
                int staffIdx = !beam.MemberStaffIndices.IsDefaultOrEmpty
                    && i < beam.MemberStaffIndices.Length
                    ? beam.MemberStaffIndices[i]
                    : Math.Max(0, beam.StaffIndex);
                map[(staffIdx, group.VoiceIndex,
                     member.ResolveMeasureIndex(group.MeasureIndex), member.ItemIndex)]
                    = (beam, beam.MemberXPositions[i], member.MemberStemUp);
            }
        }
        return map;
    }

    /// <summary>The dynamic's own voice's item at its timing (the X-parent whose extent
    /// centre the label aligns on), or null when out of range.</summary>
    internal static MusicItem? AnchorItem(
        ImmutableArray<Voice> voices, int voiceIndex, int measureIndex, int itemIndex)
    {
        if (voices.IsDefaultOrEmpty)
            return null;
        var voice = voices[Math.Clamp(voiceIndex, 0, voices.Length - 1)];
        if (measureIndex >= voice.Measures.Length)
            return null;
        var items = voice.Measures[measureIndex].Items;
        return itemIndex < items.Length ? items[itemIndex] : null;
    }

    /// <summary>
    /// The anchor column's extent CENTRE, right of the column X: half the head's
    /// advance for a note/chord, the rest glyph's own ink centre for a rest, 0 otherwise.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/self-alignment-interface.cc:117-175 aligned_on_parent —
    ///   <c>he = him->extent (him, a)</c> (:147, the PARENT's own stencil extent) and the
    ///   two <c>linear_combination</c> terms (:166 self, :171 parent) land the grob's
    ///   extent centre on the parent extent's centre for CENTER/CENTER. The parent's
    ///   extent is its drawn glyph's — so the rest's centre is the rest GLYPH's ink
    ///   centre, per glyph, not a class constant. MEASURED for the anchor classes in the
    ///   self-alignment probes (notehead = half its advance; the whole/half rest's
    ///   0.750 = its ink (0 . 1.5) / 2, which <see cref="GlyphMetrics.GetRestBBox"/>
    ///   reproduces from the font).
    /// </remarks>
    internal static double AnchorCentreOffset(MusicItem? item) => item switch
    {
        NoteItem n => GlyphMetrics.GetNoteheadAdvance(
            LayoutUtilities.GetNoteValueFromFraction(n.BaseDuration)) / 2.0,
        ChordItem c when c.Notes.Length > 0 => GlyphMetrics.GetNoteheadAdvance(
            LayoutUtilities.GetNoteValueFromFraction(c.BaseDuration)) / 2.0,
        RestItem r => RestInkCentre(r),
        _ => 0.0,
    };

    private static double RestInkCentre(RestItem r)
    {
        var box = GlyphMetrics.GetRestBBox(
            LayoutUtilities.GetNoteValueFromFraction(r.BaseDuration));
        return (box.Left + box.Right) / 2.0;
    }

    // The SCALAR support edge (ColumnUpEdge / ColumnSupportEdge / GetHighestExtent /
    // GetLowestExtent, over NoteColumnLayout.SupportEdgeUp) is GONE (2026-07-30,
    // session 39). Its last consumer was the trill spanner, and ledger
    // trill.x.{glyph,wave}-zone measured that the trill's aligned_side is POINTWISE too:
    // the same tall column reads 8.000000 under the glyph and NOTHING under the wave,
    // which no single scalar can answer. Both consumers now build
    // ColumnSupportSkylines (above) and take Skyline::distance against their own facing
    // profile, which is what side-position-interface.cc:265-330,:353-358 does.

    // The dynamic/expressive label is drawn at FontSize*0.5 = 2.0 with
    // TextAnchor.Middle (see DrawDynamics), so it extends this far to each side of
    // the note. TextFontMetrics gives a CJK glyph a full em — the bundled Latin face has
    // no glyph for it and the renderer draws it from a system fallback — so a "これ" label
    // reports its true (wide) extent.
    private const double DynamicFontSize = 2.0;

    private static double LabelHalfWidth(string text, bool expressive)
    {
        double w = expressive
            ? LilySharp.Core.Rendering.TextFontMetrics.Serif(text, DynamicFontSize)
            : LilySharp.Core.Rendering.TextFontMetrics.SerifBold(text, DynamicFontSize);
        return w / 2.0;
    }

    // WidenToNeighbors is GONE (2026-07-29): it was Lily#'s own compensation for the
    // missing below-side outside-staff pass — a wide label now clears its neighbours'
    // ink through the REAL collision pass over the staff's down profile (LilyPond's
    // avoid_outside_staff_collisions, 0.46, pointwise), the device LilyPond actually
    // has. audit/lp-geometry staff.staff.dynamic-beam-avoid.

}
