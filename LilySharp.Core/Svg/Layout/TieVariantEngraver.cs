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
    int StaffIndex = -1);

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
    internal readonly record struct SemiTie(int StaffPosition, bool CurveUp, int SourcePosition);

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
    /// ⚠️ LilyPond then SCORES variations of the whole configuration
    ///   (generate_optimal_configuration) which can overturn these seeds and also
    ///   quantizes each tie's Y off staff lines; that scorer is not ported (ticketed) —
    ///   this is the base-configuration letter only.
    /// </remarks>
    internal static ImmutableArray<SemiTie> SemiTiesOf(MusicItem item, TieVariantKind kind)
    {
        bool lv = kind == TieVariantKind.LaissezVibrer;
        switch (item)
        {
            case NoteItem n when lv ? n.HasLaissezVibrer : n.HasRepeatTie:
            {
                bool? forced = lv ? n.LaissezVibrerUp : n.RepeatTieUp;
                // Column of one: sign(position), 0 → neutral (DOWN).
                bool curveUp = forced ?? n.StaffPosition > 0;
                // The `@` that wrote it, NOT n.SourcePosition — the note's own address
                // belongs to the head, and citing it would light the head when the caret
                // sits on the annotation (the side the slur decision rejected).
                return ImmutableArray.Create(new SemiTie(n.StaffPosition, curveUp,
                    lv ? n.LaissezVibrerSourcePosition : n.RepeatTieSourcePosition));
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
                var ties = new (int Pos, bool? Dir, int Src)[count];
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
                        ties[k++] = (m.StaffPosition,
                            lv ? m.LaissezVibrerUp : m.RepeatTieUp,
                            chordSrc >= 0
                                ? chordSrc
                                : lv ? m.LaissezVibrerSourcePosition : m.RepeatTieSourcePosition);
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
                    builder.Add(new SemiTie(t.Pos, t.Dir ?? t.Pos > 0, t.Src));
                return builder.MoveToImmutable();
            }

            default:
                return ImmutableArray<SemiTie>.Empty;
        }
    }

    /// <summary>
    /// Calculates layouts for all half-ties (laissez-vibrer + repeat-tie) in the score.
    /// </summary>
    public static ImmutableArray<TieVariantLayout> Calculate(
        Score score,
        ImmutableArray<SystemLayout> systems,
        int staffIndex = -1)
    {
        if (score.Voices.IsDefaultOrEmpty)
            return ImmutableArray<TieVariantLayout>.Empty;

        var measureMap = LayoutUtilities.BuildMeasureLayoutMap(systems);
        var systemMap = LayoutUtilities.BuildMeasureMap(systems);
        var builder = ImmutableArray.CreateBuilder<TieVariantLayout>();

        var voice = score.Voice;
        for (int mi = 0; mi < voice.Measures.Length; mi++)
        {
            if (!measureMap.TryGetValue(mi, out var measureLayout))
                continue;
            if (!systemMap.TryGetValue(mi, out var info))
                continue;
            var (system, _) = info;

            var measure = voice.Measures[mi];
            for (int ii = 0; ii < measure.Items.Length; ii++)
            {
                // One half-tie per marked head, per kind — the fan and the curve
                // sides are SemiTiesOf's (a chord-level event marks every member,
                // a member-level one just its own head; chord repeat-ties used to
                // silently drop here, the mirror of the chord-l.v. drop before it).
                // LILYPOND-REF: lily/laissez-vibrer-engraver.cc:66-108 acknowledge_note_head
                //   — one tie per head; Repeat_tie_engraver inherits the path
                //   (repeat-tie-engraver.cc:27-33).
                var item = measure.Items[ii];
                foreach (var kind in KindPair)
                {
                    var ties = SemiTiesOf(item, kind);
                    if (ties.IsEmpty)
                        continue;
                    int noteValue = GlyphMetrics.NoteValueOf(item switch
                    {
                        NoteItem n => n.BaseDuration,
                        ChordItem c => c.BaseDuration,
                        _ => default,
                    });
                    foreach (var tie in ties)
                        builder.Add(BuildLayout(
                            tie.StaffPosition, tie.CurveUp, tie.SourcePosition,
                            noteValue, mi, ii, measureLayout, system, staffIndex, kind));
                }
            }
        }

        return builder.ToImmutable();
    }

    internal static readonly TieVariantKind[] KindPair =
        { TieVariantKind.LaissezVibrer, TieVariantKind.Repeat };

    private static TieVariantLayout BuildLayout(
        int staffPosition, bool curveUp, int sourcePosition, int noteValue,
        int measureIndex, int itemIndex,
        MeasureLayout measureLayout, SystemLayout system, int staffIndex,
        TieVariantKind kind)
    {
        // Reads the raw item slot X. Safe on every path: MultiStaffLayouter
        // derives Items[i].X FROM the timing columns (see
        // MeasureLayouter.LayoutItemsFromColumns), so the slot equals the column-grid X the
        // renderer draws the notehead at even when a bar opens with a mid-piece time/clef
        // change; single-staff layouts have no columns and the slot is already the grid.
        var itemLayout = measureLayout.Items[itemIndex];
        double headLeftX = measureLayout.X + itemLayout.X;

        // The half-tie's own geometry (X span, baseline, signed arc) — the one
        // spelling shared with the spacing skylines' box (SemiTieGeometry).
        var (xLeft, xRight, baseYFromMiddle, signedArc) = SemiTieGeometry(
            noteValue, staffPosition, curveUp, kind);

        const double StaffHeight = 4.0;
        // Within-system Y offset (device, down from the system top) of the staff
        // middle, NOT an absolute page Y — so the tie's Y/control points are
        // independent of where paging places the system. DrawTieVariants resolves
        // the system-top Y-up and subtracts these, keeping the output byte-identical
        // to the former absolute origin while decoupling from SystemLayout.Y for the
        // Stage-4 W2 stacking-origin flip (step 2a MMR / step 2b Ledger). The
        // internal arc geometry stays device-frame (intentional-device island 2).
        double staffMiddleOffset = LayoutUtilities.StaffOffsetInSystemDown(system, staffIndex)
            + StaffHeight / 2.0;
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
