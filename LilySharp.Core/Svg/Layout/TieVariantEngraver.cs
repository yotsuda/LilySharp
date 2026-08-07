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
    /// </summary>
    internal static (double XLeft, double XRight, double BaseYFromMiddleDown, double SignedArc)
        SemiTieGeometry(int noteValue, int staffPosition, bool stemUp, bool? forcedUp,
            TieVariantKind kind)
    {
        // X span, in LilyPond's own numbers: the head-side end stands the tie
        // details' note-head gap (0.2) off the head's INK edge, the free end
        // OpenReach (1.5) out less the same gap.
        // (Verified against 2.26 SVG: a whole-note chord's l.v. spans
        // headRight+0.2 .. headRight+1.3 to the digit — scratch\lpreg\lvchords.)
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

        // Curve direction: forced by ^/_ on the event when given, else opposite
        // to the stem, like a regular tie.
        // LILYPOND-REF: lily/laissez-vibrer-engraver.cc:99-103 acknowledge_note_head —
        //   the event's direction is copied onto the tie and outranks the automatic choice;
        // LILYPOND-REF: lily/tie.cc calc_direction — direction defaults opposite to stem.
        bool curveUp = forcedUp ?? !stemUp;
        double baseY = -staffPosition / 2.0 + (curveUp ? -NoteOffset : NoteOffset);
        double arc = Math.Min(BowHeightLimit, BowRatio * (xr - xl));
        return (xl, xr, baseY, curveUp ? -arc : arc);
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
                switch (measure.Items[ii])
                {
                    case NoteItem note when note.HasLaissezVibrer || note.HasRepeatTie:
                        int noteValue = GlyphMetrics.NoteValueOf(note.BaseDuration);
                        if (note.HasLaissezVibrer)
                            builder.Add(BuildLayout(
                                note.StaffPosition, note.StemUp, note.LaissezVibrerUp,
                                note.SourcePosition, noteValue, mi, ii, measureLayout, system,
                                staffIndex, TieVariantKind.LaissezVibrer));
                        if (note.HasRepeatTie)
                            builder.Add(BuildLayout(
                                note.StaffPosition, note.StemUp, null,
                                note.SourcePosition, noteValue, mi, ii, measureLayout, system,
                                staffIndex, TieVariantKind.Repeat));
                        break;

                    // A chord fans one half-tie per marked member — a chord-level
                    // @laissezVibrer marks all of them, a member-level one just its
                    // own head. This used to silently drop every chord l.v.
                    // LILYPOND-REF: lily/laissez-vibrer-engraver.cc:66-108 acknowledge_note_head
                    //   — one LaissezVibrerTie per head.
                    case ChordItem chordItem:
                        foreach (var member in chordItem.Notes)
                        {
                            if (!member.HasLaissezVibrer)
                                continue;
                            builder.Add(BuildLayout(
                                member.StaffPosition, chordItem.StemUp, member.LaissezVibrerUp,
                                member.SourcePosition >= 0
                                    ? member.SourcePosition : chordItem.SourcePosition,
                                GlyphMetrics.NoteValueOf(chordItem.BaseDuration),
                                mi, ii, measureLayout, system,
                                staffIndex, TieVariantKind.LaissezVibrer));
                        }
                        break;
                }
            }
        }

        return builder.ToImmutable();
    }

    private static TieVariantLayout BuildLayout(
        int staffPosition, bool stemUp, bool? forcedUp, int sourcePosition, int noteValue,
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
            noteValue, staffPosition, stemUp, forcedUp, kind);
        bool curveUp = signedArc < 0;

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
