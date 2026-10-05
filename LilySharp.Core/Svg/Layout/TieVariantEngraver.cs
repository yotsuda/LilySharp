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

/// <summary>Kind of half-tie attached to a single note.</summary>
public enum TieVariantKind
{
    /// <summary>Laissez vibrer: tie pointing right from the note (let-ring).</summary>
    LaissezVibrer,
    /// <summary>Repeat tie: tie pointing left into the note (continuation from a repeat).</summary>
    Repeat,
}

/// <summary>
/// Layout for a half-tie (laissez-vibrer or repeat-tie) attached to a single note.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/laissez-vibrer-engraver.cc — LaissezVibrerTie grob
/// LILYPOND-REF: lily/repeat-tie-engraver.cc — RepeatTie grob
/// LILYPOND-REF: scm/define-grobs.scm — LaissezVibrerTie / RepeatTie
///
/// Unlike a full Tie that connects two notes, a half-tie attaches to a single
/// note and curves outward into empty space. The curve length is short
/// (~1.0 staff space) and tapers like a normal tie.
/// </remarks>
public readonly record struct TieVariantLayout(
    TieVariantKind Kind,
    int MeasureIndex,
    int ItemIndex,
    // Start X (the side closer to the host note).
    double StartX,
    // End X (the side away from the note, into empty space).
    double EndX,
    // Y of both endpoints (the tie sits flat at this height).
    double Y,
    // Bezier control point 1.
    (double X, double Y) Control1,
    // Bezier control point 2.
    (double X, double Y) Control2,
    // True = curve up, false = curve down.
    bool CurveUp,
    // Offset of the '@' that wrote this tie's @laissezVibrer / @repeatTie — the address
    // DrawTieVariants names on the bow. MusicItem.NoSourcePosition = nothing wrote it and
    // the bow gets no Source scope. See TieVariantEngraver.SemiTie.
    int SourcePosition,
    // Owning staff (ossia shrink); -1 = unknown/test construction.
    int StaffIndex = -1,
    // The voice of that staff the host item is in (0-based) — with StaffIndex, the table
    // SharedRenderer.ResolveSemiTies re-reads the live annotation from.
    int VoiceIndex = 0);

/// <summary>
/// Engraver for half-ties (LaissezVibrerTie and RepeatTie).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/laissez-vibrer-engraver.cc / lily/repeat-tie-engraver.cc
/// </remarks>
internal static class TieVariantEngraver
{
    /// <summary>
    /// How far the half-tie's OPEN end reaches past the head's ink edge, before the
    /// attachment gaps: <c>from_semi_ties</c> builds the open-side chord outline at
    /// <c>extremal − head_dir · 1.5</c>, the head-side outline being the heads
    /// themselves.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/tie-formatting-problem.cc:436-441 from_semi_ties.
    /// Internal: SpacingRules charges the same span as the column's rightward ink.</remarks>
    internal const double OpenReach = 1.5;

    /// <summary>The half-tie's bow parameters, from its grob details. The arc height is
    /// LilyPond's bezier-bow shape: <c>min(height-limit, ratio × width)</c>.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm LaissezVibrerTie / RepeatTie
    /// (details . ((height-limit . 1.0) (ratio . 0.333))).</remarks>
    private const double BowRatio = 0.333;
    private const double BowHeightLimit = 1.0;

    /// <summary>
    /// The half-tie's LINE thickness: its <c>line-thickness</c> (0.8) in staff-line units.
    /// The stencil is the curve widened by half of this on every edge, and that stencil is
    /// the grob's extent — so the spacing box reaches 0.04 past the curve at both ends.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:2039 LaissezVibrerTie line-thickness 0.8, and
    ///   :2934 RepeatTie line-thickness 0.8;
    /// LILYPOND-REF: lily/tie.cc:225-253 Tie::print — line_thick = staff_thick × line-thickness,
    ///   handed to Lookup::slur;
    /// LILYPOND-REF: lily/lookup.cc:483-516 Lookup::bezier_sandwich — b.widen (0.5 * thickness)
    ///   on both axes, the box the stencil carries.
    /// MEASURED (2.26.0, audit/lp-geometry/probes/semi-tie-spacing.ly book LVA): the l.v.
    /// tie's X-extent is head right + 0.16 .. + 1.34 for a curve spanning + 0.2 .. + 1.3.
    /// </remarks>
    internal static double LineThickness => 0.8 * EngravingDefaults.LineThickness;

    /// <summary>The half-tie's <c>extra-spacing-height</c>: its spacing box reaches half a
    /// staff space above and below its stencil.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:2037 LaissezVibrerTie extra-spacing-height
    /// (-0.5 . 0.5), and :2932 RepeatTie the same.</remarks>
    internal const double ExtraSpacingHeight = 0.5;

    /// <summary>Y offset from notehead center to the tie's flat baseline. Lily# placement
    /// approximation (~notehead half-height, no single LP constant); LP anchors the semi-tie
    /// at the note-head edge via Semi_tie_column.</summary>
    private const double NoteOffset = 0.4;

    /// <summary>
    /// The ONE spelling of a half-tie's geometry, relative to the host column: X span
    /// from the column origin (= head ink left), the flat baseline in device-down
    /// staff spaces from the staff MIDDLE, and the signed arc (negative = bulges up).
    /// <see cref="BuildLayout"/> draws from it and <see cref="ItemSkylineFactory"/>
    /// boxes it for the spacing skylines — the pair HANDOFF 5.2.1② warns about.
    /// The curve side comes resolved from <see cref="SemiTiesOf"/> (the one place
    /// that knows the item's whole column).
    /// </summary>
    internal static (double XLeft, double XRight, double BaseYFromMiddleDown, double SignedArc)
        SemiTieGeometry(int noteValue, int staffPosition, bool curveUp, TieVariantKind kind)
    {
        // X span, in LilyPond's own numbers: the head-side end stands the tie
        // details' note-head gap (0.2) off the head's INK edge, the free end
        // OpenReach (1.5) out less the same gap.
        // (Verified against 2.26 SVG: a whole-note chord's l.v. spans
        // headRight+0.2 .. headRight+1.3 to the digit — audit\lpreg\lvchords;
        // the repeat-tie mirror spans headLeft−1.3 .. headLeft−0.2 — rtchords.)
        // LILYPOND-REF: lily/laissez-vibrer-engraver.cc acknowledge_note_head — head-direction LEFT (tie RIGHT of head)
        // LILYPOND-REF: lily/repeat-tie-engraver.cc make_my_tie — head-direction RIGHT (tie LEFT of head)
        // LILYPOND-REF: lily/tie-formatting-problem.cc:436-441 from_semi_ties — open outline at extremal − dir·1.5
        double xGap = TieDetails.Default.XGap;
        double xl, xr;
        if (kind == TieVariantKind.LaissezVibrer)
        {
            double edge = GlyphMetrics.GetNoteheadBBox(noteValue).Right;
            xl = edge + xGap;
            xr = edge + OpenReach - xGap;
        }
        else
        {
            // The head's ink LEFT is the column origin (every head's ink Left is 0).
            xl = -OpenReach + xGap;
            xr = -xGap;
        }

        double baseY = -staffPosition / 2.0 + (curveUp ? -NoteOffset : NoteOffset);
        double arc = Math.Min(BowHeightLimit, BowRatio * (xr - xl));
        return (xl, xr, baseY, curveUp ? -arc : arc);
    }

    /// <summary>One half-tie of an item's column, its curve side resolved.</summary>
    /// <param name="StaffPosition">The host head's staff position.</param>
    /// <param name="CurveUp">Resolved curve side.</param>
    /// <param name="SourcePosition">The offset of the <c>@</c> that wrote this tie's
    /// <c>@laissezVibrer</c> / <c>@repeatTie</c> — the address the drawn bow names, so a
    /// caret on the annotation lights the tie. <c>MusicItem.NoSourcePosition</c> when the
    /// item was built without one (a tab's rebuilt column, a hand-made test item), and the
    /// drawer then opens no <c>Source</c> scope at all.</param>
    internal readonly record struct SemiTie(int StaffPosition, bool CurveUp, int SourcePosition, bool? Forced = null);

    /// <summary>
    /// The half-ties of one <paramref name="kind"/> on one item — the COLUMN LilyPond
    /// builds per kind (LaissezVibrerTieColumn / RepeatTieColumn): a note contributes
    /// its own head when flagged, a chord one tie per flagged member. Directions are
    /// forced by ^/_ where written and otherwise assigned by the standard-directions
    /// rule below. Both the drawing fan (<see cref="Calculate"/>) and the spacing
    /// skylines (<see cref="ItemSkylineFactory"/>) consume THIS list — one spelling.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/laissez-vibrer-engraver.cc:66-108 acknowledge_note_head —
    ///   "use the heard event_ for all note heads, or an individual event for just
    ///   a single note head"; :99-103 the event's direction is copied onto the tie.
    /// LILYPOND-REF: lily/semi-tie-column.cc:51-86 calc_positioning_done — ties are
    ///   sorted by head position (Semi_tie::less) and the unforced directions come
    ///   from the formatting problem's base configuration.
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:1026-1066
    ///   set_ties_config_standard_directions — a single unforced tie takes
    ///   sign(position) (0 → neutral-direction, DOWN when unset — tie-details.cc:43-46);
    ///   in a column of several, the bottom tie takes DOWN, the top UP, adjacent ties
    ///   within a second split DOWN/UP, and the rest take sign(position) (0 → DOWN).
    /// LILYPOND-REF: scm/music-functions.scm:617-634 direction-polyphonic-grobs —
    ///   LaissezVibrerTie and RepeatTie are in the list, so inside a polyphonic span
    ///   make-voice-props-set (:666-674) sets their direction like a Tie's: voice one UP,
    ///   voice two DOWN, for every tie of the column. Read here off the item's
    ///   <c>VoiceStemUp</c> (the same voice-props answer MeasureCollector bakes for the
    ///   stem); a written ^/_ still wins (the event's direction is copied onto the tie).
    ///   Until 2026-09-28 a lower voice's half-tie took the pitch rule.
    /// ⚠️ LilyPond then SCORES variations of the whole configuration
    ///   (generate_optimal_configuration) which can overturn these seeds and also
    ///   quantizes each tie's Y off staff lines. The drawn notation half-ties go through that
    ///   scorer (SolveSemiTieColumn, since session 573); THIS is the base-configuration letter
    ///   only, and what still reads it — the fallback / tab path and the spacing boxes — does
    ///   not get the scored answer.
    /// </remarks>
    internal static ImmutableArray<SemiTie> SemiTiesOf(MusicItem item, TieVariantKind kind)
    {
        bool lv = kind == TieVariantKind.LaissezVibrer;
        switch (item)
        {
            case NoteItem n when lv ? n.HasLaissezVibrer : n.HasRepeatTie:
            {
                // Written ^/_ first, then the voice props' direction (see the remarks).
                bool? forced = (lv ? n.LaissezVibrerUp : n.RepeatTieUp) ?? n.VoiceStemUp;
                // Column of one: sign(position), 0 → neutral (DOWN).
                bool curveUp = forced ?? n.StaffPosition > 0;
                // The `@` that wrote it, NOT n.SourcePosition — the note's own address
                // belongs to the head, and citing it would light the head when the caret
                // sits on the annotation (the side the slur decision rejected).
                return ImmutableArray.Create(new SemiTie(n.StaffPosition, curveUp,
                    lv ? n.LaissezVibrerSourcePosition : n.RepeatTieSourcePosition, forced));
            }

            case ChordItem c:
            {
                int count = 0;
                foreach (var m in c.Notes)
                    if (lv ? m.HasLaissezVibrer : m.HasRepeatTie)
                        count++;
                if (count == 0)
                    return ImmutableArray<SemiTie>.Empty;

                // Sorted by head position, bottom first (Semi_tie::less).
                var ties = new (int Pos, bool? Dir, int Src, bool? Forced)[count];
                int k = 0;
                // The CHORD's annotation is read first, not the member's: a chord-level
                // @laissezVibrer half-ties every head, so its one `@` is the character
                // that wrote all of them (the same precedence the direction above uses).
                // A member-level annotation is the fallback, and cites its own `@`.
                // ⚠️ Neither is the member's SourcePosition — that is its PITCH token,
                // which belongs to the head.
                int chordSrc = lv ? c.LaissezVibrerSourcePosition : c.RepeatTieSourcePosition;
                foreach (var m in c.Notes)
                    if (lv ? m.HasLaissezVibrer : m.HasRepeatTie)
                    {
                        // Written ^/_ first, then the voice props' direction, which sets
                        // every tie of the column alike (see the remarks).
                        bool? dir = (lv ? m.LaissezVibrerUp : m.RepeatTieUp) ?? c.VoiceStemUp;
                        ties[k++] = (m.StaffPosition,
                            dir,
                            chordSrc >= 0
                                ? chordSrc
                                : lv ? m.LaissezVibrerSourcePosition : m.RepeatTieSourcePosition,
                            dir);
                    }
                Array.Sort(ties, static (a, b) => a.Pos.CompareTo(b.Pos));

                // set_ties_config_standard_directions, on the sorted column.
                if (ties[0].Dir == null)
                {
                    if (count == 1 && ties[0].Pos != 0)
                        ties[0].Dir = ties[0].Pos > 0;
                    // Several ties → bottom DOWN; a lone tie on the middle line →
                    // neutral-direction, DOWN when unset (tie-details.cc:43-46).
                    ties[0].Dir ??= false;
                }
                if (ties[^1].Dir == null)
                    ties[^1].Dir = true;
                // Seconds: adjacent ties within one position split DOWN/UP. (The
                // column-span arm is dead here — every head of one chord shares the
                // column, so span_diff is always 0.)
                for (int i = 1; i < count; i++)
                    if (Math.Abs(ties[i].Pos - ties[i - 1].Pos) <= 1)
                    {
                        ties[i - 1].Dir ??= false;
                        ties[i].Dir ??= true;
                    }
                var builder = ImmutableArray.CreateBuilder<SemiTie>(count);
                foreach (var t in ties)
                    builder.Add(new SemiTie(t.Pos, t.Dir ?? t.Pos > 0, t.Src, t.Forced));
                return builder.MoveToImmutable();
            }

            default:
                return ImmutableArray<SemiTie>.Empty;
        }
    }

    /// <summary>
    /// Calculates layouts for all half-ties (laissez-vibrer + repeat-tie) in the score: every
    /// voice of every notation staff each system carries.
    /// </summary>
    /// <param name="measureMap">The caller's measure → (system, layout) map, when it has one
    /// (<c>LayoutEngine.CalculateAnnotationLayouts</c> builds it once for the tail's three
    /// engravers). Null ⇒ build it here, which is what every non-keystroke caller does.
    /// Read only on the single-staff path (<paramref name="staffByIndex"/> null).</param>
    /// <param name="staffByIndex">Every staff by index (the annotation pass's table). Given,
    /// the walk is the inside-staff skyline's (<c>SkylineBuilder.AddStaffToSkylines</c>): per
    /// system, per placed staff, per voice — so the bows drawn are the bows reserved. Null ⇒
    /// the single-staff path: every voice of <paramref name="score"/> at
    /// <paramref name="staffIndex"/>.</param>
    /// <remarks>
    /// ⚠️ UNTIL 2026-09-28 THIS WALKED <c>score.Voice</c> ALONE — the primary staff's first
    /// voice — while the skyline (in place since that morning) reserved room on every staff and voice: a
    /// <c>@laissezVibrer</c> / <c>@repeatTie</c> in a second part, a piano's lower staff or a
    /// lower voice, and the automatic repeat tie SectionTieCarry adds there, reserved room
    /// and drew nothing.
    /// A TAB staff gets none: LilyPond's TabStaff engraves no half-tie
    /// (audit/lpreg/tabtie-probe2 — a repeat tie parenthesises the fret instead), and the
    /// tab skyline (AddTabStaffToSkylines) reserves none. Until the same date a PRIMARY tab
    /// staff drew a Lily#-own approximation with nothing reserved for it.
    /// </remarks>
    public static ImmutableArray<TieVariantLayout> Calculate(
        Score score,
        ImmutableArray<SystemLayout> systems,
        int staffIndex = -1,
        IReadOnlyDictionary<int, (SystemLayout System, MeasureLayout Measure)>? measureMap = null,
        IReadOnlyDictionary<int, Staff>? staffByIndex = null)
    {
        if (score.Voices.IsDefaultOrEmpty)
            return ImmutableArray<TieVariantLayout>.Empty;

        // ⚠️ IT WAITS FOR ITS FIRST ELEMENT. `ImmutableArray.CreateBuilder<T>()` lays out its
        // first block — 88 B for a reference element — before a single Add, and over the
        // reader's corpus this one is built 2.17 times a keystroke and stays EMPTY in every
        // one of them: a book with no l.v. or repeat tie has nothing to lay out (session 448).
        ImmutableArray<TieVariantLayout>.Builder? builder = null;

        if (staffByIndex is not null)
        {
            // The skyline's walk: each system's measure layouts, each staff it places.
            foreach (var system in systems)
            {
                if (system.StaffGroups.IsDefaultOrEmpty)
                    continue;
                foreach (var group in system.StaffGroups)
                {
                    if (group.Staves.IsDefaultOrEmpty)
                        continue;
                    foreach (var placed in group.Staves)
                    {
                        if (placed.IsHidden
                            || !staffByIndex.TryGetValue(placed.StaffIndex, out var staff)
                            || staff.IsTab || staff.IsTextRow)
                            continue;
                        foreach (var measureLayout in system.Measures)
                            for (int vi = 0; vi < staff.Voices.Length; vi++)
                                AppendMeasure(ref builder, system, measureLayout,
                                    staff.Voices[vi], vi, placed.StaffIndex);
                    }
                }
            }
            return builder?.ToImmutable() ?? [];
        }

        // ⚠️ ONE MAP, NOT TWO. This used to build BuildMeasureLayoutMap AND BuildMeasureMap —
        // the second is the first plus the system, over the identical key set by construction
        // (both walk every system's Measures and key on MeasureIndex), so the layout half was
        // a whole second dictionary of the score's measures for a value already in hand.
        var map = measureMap ?? LayoutUtilities.BuildMeasureMap(systems);
        for (int vi = 0; vi < score.Voices.Length; vi++)
        {
            var voice = score.Voices[vi];
            for (int mi = 0; mi < voice.Measures.Length; mi++)
                if (map.TryGetValue(mi, out var info))
                    AppendMeasure(ref builder, info.System, info.Measure, voice, vi, staffIndex);
        }
        return builder?.ToImmutable() ?? [];
    }

    /// <summary>One measure of one voice: its items' half-ties, at the X and staff the inside-
    /// staff skyline reserves them at (<c>SkylineBuilder.AddSemiTiesToSkylines</c>).</summary>
    private static void AppendMeasure(
        ref ImmutableArray<TieVariantLayout>.Builder? builder, SystemLayout system,
        MeasureLayout measureLayout, Voice voice, int voiceIndex, int staffIndex)
    {
        int mi = measureLayout.MeasureIndex;
        if (mi >= voice.Measures.Length)
            return;
        var measure = voice.Measures[mi];
        {
            for (int ii = 0; ii < measure.Items.Length; ii++)
            {
                // The skyline's guard: a slot the layout did not place is not drawn.
                if (measureLayout.Columns.IsDefaultOrEmpty && ii >= measureLayout.Items.Length)
                    continue;

                // One half-tie per marked head, per kind — the fan and the curve
                // sides are SemiTiesOf's (a chord-level event marks every member,
                // a member-level one just its own head; chord repeat-ties used to
                // silently drop here, the mirror of the chord-l.v. drop before it).
                // LILYPOND-REF: lily/laissez-vibrer-engraver.cc:66-108 acknowledge_note_head
                //   — one tie per head; Repeat_tie_engraver inherits the path
                //   (repeat-tie-engraver.cc:27-33).
                var item = measure.Items[ii];
                if (!HasSemiTie(item))
                    continue;
                // Reads the raw item slot X for the approximation. Safe on every path:
                // MultiStaffLayouter derives Items[i].X FROM the timing columns (see
                // MeasureLayouter.LayoutItemsFromColumns), so the slot equals the column-grid
                // X the renderer draws the notehead at even when a bar opens with a mid-piece
                // time/clef change; single-staff layouts have no columns and the slot is
                // already the grid.
                double columnX = measureLayout.X
                    + LayoutUtilities.GetItemXOffset(voice.Measures, mi, ii, measureLayout);
                double slotX = ii < measureLayout.Items.Length
                    ? measureLayout.X + measureLayout.Items[ii].X
                    : columnX;
                // Within-system Y offset (device, down from the system top) of the staff
                // middle, NOT an absolute page Y — so the tie's Y/control points are
                // independent of where paging places the system. DrawTieVariants resolves
                // the system-top Y-up and subtracts these, keeping the output byte-identical
                // to the former absolute origin while decoupling from SystemLayout.Y for the
                // Stage-4 W2 stacking-origin flip (step 2a MMR / step 2b Ledger). The
                // internal arc geometry stays device-frame (intentional-device island 2).
                const double StaffHeight = 4.0;
                double staffMiddleDown = LayoutUtilities.StaffOffsetInSystemDown(system, staffIndex)
                    + StaffHeight / 2.0;
                builder ??= ImmutableArray.CreateBuilder<TieVariantLayout>();
                AppendItemSemiTies(builder, voice, mi, ii, item, columnX, slotX,
                    staffMiddleDown, staffIndex, voiceIndex);
            }
        }
    }

    /// <summary>The cheap flag scan: does <paramref name="item"/> carry a half-tie of either
    /// kind? Almost no item does, and the per-kind fan must not be paid for the rest.</summary>
    internal static bool HasSemiTie(MusicItem item)
    {
        switch (item)
        {
            case NoteItem n:
                return n.HasLaissezVibrer || n.HasRepeatTie;
            case ChordItem c:
                foreach (var m in c.Notes)
                    if (m.HasLaissezVibrer || m.HasRepeatTie)
                        return true;
                return false;
            default:
                return false;
        }
    }

    /// <summary>
    /// One item's half-ties, both kinds, exactly as they are DRAWN — the one home both the
    /// drawing (<see cref="Calculate"/>) and the staff's inside-staff skyline
    /// (<c>SkylineBuilder.AddStaffToSkylines</c>) read, so the bow a section label or a
    /// volta bracket clears is the bow on the page.
    /// </summary>
    /// <param name="into">Receives one layout per half-tie.</param>
    /// <param name="voice">The voice the item is in (the column outline is built from it).</param>
    /// <param name="measureIndex">The item's measure.</param>
    /// <param name="itemIndex">The item's index in its measure.</param>
    /// <param name="item">The host note or chord.</param>
    /// <param name="columnX">The host column's X (head ink left), in the caller's X frame.</param>
    /// <param name="slotX">The item slot's X, which the approximation hangs off.</param>
    /// <param name="staffMiddleDown">The staff middle's device-down offset in the caller's
    /// Y frame: the within-system offset for the drawing, 0 for a staff-local skyline (the
    /// layouts then come back device-down about the staff's middle line).</param>
    /// <param name="staffIndex">Stamped on the layouts (ossia shrink).</param>
    /// <param name="voiceIndex">Stamped on the layouts (the data-pos re-read).</param>
    internal static void AppendItemSemiTies(
        ICollection<TieVariantLayout> into, Voice voice, int measureIndex, int itemIndex,
        MusicItem item, double columnX, double slotX, double staffMiddleDown, int staffIndex,
        int voiceIndex)
    {
        // One half-tie per marked head, per kind — the fan and the curve
        // sides are SemiTiesOf's (a chord-level event marks every member,
        // a member-level one just its own head; chord repeat-ties used to
        // silently drop here, the mirror of the chord-l.v. drop before it).
        // LILYPOND-REF: lily/laissez-vibrer-engraver.cc:66-108 acknowledge_note_head
        //   — one tie per head; Repeat_tie_engraver inherits the path
        //   (repeat-tie-engraver.cc:27-33).
        foreach (var kind in KindPair)
        {
            var ties = SemiTiesOf(item, kind);
            if (ties.IsEmpty)
                continue;
            if (!SolveSemiTieColumn(into, voice, measureIndex, itemIndex, item, ties,
                    kind, columnX, staffMiddleDown, staffIndex, voiceIndex))
            {
                // Not a note column the tie outline can be built from: the drawn
                // approximation (SemiTieGeometry) is all there is. (A TAB staff never
                // gets here — LilyPond's TabStaff engraves no half-tie; see Calculate.)
                int noteValue = GlyphMetrics.NoteValueOf(item switch
                {
                    NoteItem n => n.BaseDuration,
                    ChordItem c => c.BaseDuration,
                    _ => default,
                });
                foreach (var tie in ties)
                    into.Add(BuildLayout(
                        tie.StaffPosition, tie.CurveUp, tie.SourcePosition,
                        noteValue, measureIndex, itemIndex, slotX, staffMiddleDown,
                        staffIndex, kind) with { VoiceIndex = voiceIndex });
            }
        }
    }

    /// <summary>
    /// Lays out one item's half-ties of one kind as LilyPond does: a whole
    /// <see cref="TieFormattingProblem"/> over the column, the head side read off the host's own
    /// chord outline and the open side a fixed end 1.5 past the outline's extreme.
    /// </summary>
    /// <returns>False when the item is not a note column the outline can be built from.</returns>
    /// <remarks>
    /// LILYPOND-REF: lily/semi-tie-column.cc:51-86 calc_positioning_done — the column's ties
    ///   sorted, <c>problem.from_semi_ties</c>, <c>generate_optimal_configuration</c>, and each
    ///   tie's control points and direction from the winner;
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:386-442 from_semi_ties — the host heads are
    ///   the bound on the tie's <c>head-direction</c> side (LEFT for an l.v., RIGHT for a repeat
    ///   tie), and the open side is <c>extremal − head_dir · 1.5</c> with <c>extremal</c> the host
    ///   outline's <c>max_height</c>.
    /// ✔ PORTED IN SESSION 573 (HANDOFF R9(f)). Until then the bow sat a fixed 0.4 ss off the
    /// head centre with a Lily#-own bow shape (0.3 indent), and the X span never read the head:
    /// MEASURED (2.26.0, Lab sessions/p573/lv) LilyPond's l.v. on a half note one step over the
    /// middle line stands at position 2 + 0.20 and leaves from the head's CENTRE (it clears the
    /// head, so the outline recedes there), and every l.v. clear of the staff at (pos + 1) / 2.
    /// ⚠️ The SPACING box (<see cref="SemiTieGeometry"/>, read by ItemSkylineFactory) keeps the
    /// old fixed span and baseline: LilyPond boxes the tie at its stencil after positioning,
    /// which a column's spacing box cannot ask for before layout.
    /// </remarks>
    private static bool SolveSemiTieColumn(
        ICollection<TieVariantLayout> builder, Voice voice, int measureIndex,
        int itemIndex, MusicItem item, ImmutableArray<SemiTie> ties, TieVariantKind kind,
        double columnX, double staffMiddleDown, int staffIndex, int voiceIndex)
    {
        bool lv = kind == TieVariantKind.LaissezVibrer;
        var positions = new List<int>(ties.Length);
        foreach (var tie in ties)
            if (!positions.Contains(tie.StaffPosition))
                positions.Add(tie.StaffPosition);
        var parts = ElementCoordinator.BuildTieColumn(
            voice, measureIndex, itemIndex, columnX, positions, isLeftBound: lv);
        if (parts is null)
            return false;

        // LILYPOND-REF: lily/tie-formatting-problem.cc:436-441 — the open outline, set_minimum_height (extremal − head_dir · 1.5).
        double openX = lv ? OutlineExtreme(parts, right: true) + OpenReach
                          : OutlineExtreme(parts, right: false) - OpenReach;
        bool? stemUp = ElementCoordinator.BoundStemUp(voice, measureIndex, itemIndex);
        int dots = SpacingRules.GetDots(item);
        var baseDuration = item switch
        {
            NoteItem n => n.BaseDuration,
            ChordItem c => c.BaseDuration,
            _ => default,
        };

        var specs = new List<TieSpecification>(ties.Length);
        foreach (var tie in ties)
        {
            var head = item as NoteItem
                       ?? new NoteItem(tie.StaffPosition, baseDuration, dots, null, false, tie.SourcePosition);
            specs.Add(new TieSpecification
            {
                Tie = new TieItem(head, head, tie.StaffPosition, tie.Forced,
                    measureIndex, measureIndex, itemIndex, itemIndex),
                StartX = lv ? columnX : openX,
                EndX = lv ? openX : columnX,
                Y = staffMiddleDown - tie.StaffPosition / 2.0,
                StartDots = lv ? dots : 0,
                StartColumn = lv ? parts : null,
                EndColumn = lv ? null : parts,
                StartStemUp = lv ? stemUp : null,
                EndStemUp = lv ? null : stemUp,
                IsSemiTie = true,
            });
        }

        var layouts = TieFormattingProblem.SolveColumn(specs, TieDetails.SemiTie);
        for (int i = 0; i < ties.Length; i++)
        {
            var l = layouts[i];
            // The problem answers page Y-up (the negated device frame the specification's Y
            // is in); the half-tie layout keeps the device frame DrawTieVariants reads.
            builder.Add(new TieVariantLayout(
                Kind: kind,
                MeasureIndex: measureIndex,
                ItemIndex: itemIndex,
                StartX: l.StartX,
                EndX: l.EndX,
                Y: -l.StartYUp,
                Control1: (l.Control1.X, -l.Control1.Y),
                Control2: (l.Control2.X, -l.Control2.Y),
                CurveUp: l.CurveUp,
                SourcePosition: ties[i].SourcePosition,
                StaffIndex: staffIndex,
                VoiceIndex: voiceIndex));
        }
        return true;
    }

    /// <summary>
    /// The host outline's reach on the open side — <c>chord_outlines_[head_key].max_height ()</c>:
    /// the furthest right (l.v.) or left (repeat tie) edge of every box
    /// <see cref="TieChordOutline.Build"/> walks for that bound.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/tie-formatting-problem.cc:438 extremal; the boxes are :96-287
    /// set_column_chord_outline's (dots and flag on the LEFT bound, accidentals on the RIGHT).</remarks>
    private static double OutlineExtreme(TieColumnParts parts, bool right)
    {
        double v = right ? double.NegativeInfinity : double.PositiveInfinity;
        void Take(double l, double r) => v = right ? Math.Max(v, r) : Math.Min(v, l);
        foreach (var h in parts.TiedHeads)
            Take(h.XLeft, h.XRight);
        if (right)
            foreach (var d in parts.Dots)
                Take(d.XLeft, d.XRight);
        if (parts.Stem is { } stem)
        {
            if (stem.IsNormal)
            {
                Take(stem.CentreX - 1.0 / 20, stem.CentreX + 1.0 / 20);
                if (right)
                    foreach (var f in parts.Flag)
                        Take(f.XLeft, f.XRight);
            }
            foreach (var o in parts.OtherHeads)
                Take(o.XLeft, o.XRight);
            if (!right)
                foreach (var a in parts.Accidentals)
                    Take(a.XLeft, a.XRight);
        }
        return v;
    }

    internal static readonly TieVariantKind[] KindPair =
        { TieVariantKind.LaissezVibrer, TieVariantKind.Repeat };

    private static TieVariantLayout BuildLayout(
        int staffPosition, bool curveUp, int sourcePosition, int noteValue,
        int measureIndex, int itemIndex,
        double headLeftX, double staffMiddleOffset, int staffIndex,
        TieVariantKind kind)
    {
        // The half-tie's own geometry (X span, baseline, signed arc) — the one
        // spelling shared with the spacing skylines' box (SemiTieGeometry).
        var (xLeft, xRight, baseYFromMiddle, signedArc) = SemiTieGeometry(
            noteValue, staffPosition, curveUp, kind);

        double baseY = staffMiddleOffset + baseYFromMiddle;

        // It used to hang off the item SLOT's right edge — a whole note's slot
        // spans the measure, which pushed the tie mid-bar (~4 ss past LilyPond's).
        double startX = headLeftX + xLeft;
        double endX = headLeftX + xRight;
        double directedHeight = signedArc;
        // Cubic-bezier control points inset from each end by 0.3 of the tie length — a
        // bow-shape approximation (LP builds the tie bezier from tie-details rather than a
        // single inset fraction; 0.3 reproduces the near-circular arc well enough here).
        double indent = (endX - startX) * 0.3;
        var control1 = (X: startX + indent, Y: baseY + directedHeight);
        var control2 = (X: endX - indent, Y: baseY + directedHeight);

        return new TieVariantLayout(
            Kind: kind,
            MeasureIndex: measureIndex,
            ItemIndex: itemIndex,
            StartX: startX,
            EndX: endX,
            Y: baseY,
            Control1: control1,
            Control2: control2,
            CurveUp: curveUp,
            SourcePosition: sourcePosition,
            StaffIndex: staffIndex);
    }
}
