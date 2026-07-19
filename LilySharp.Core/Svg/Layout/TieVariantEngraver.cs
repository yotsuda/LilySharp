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
    /// Visual length of the half-tie in staff spaces (how far the curve extends from the
    /// host note). LilyPond has NO fixed length for these grobs — Semi_tie_column places
    /// them from the note-head extent and gap (lily/semi-tie-column.cc); this is a Lily#
    /// fixed approximation of that short span. (The earlier "minimum-length 1.5" reference
    /// was incorrect — the grob has no minimum-length property.)
    /// </summary>
    private const double TieLength = 1.0;

    /// <summary>The half-tie's bow parameters, from its grob details. The arc height is
    /// LilyPond's bezier-bow shape: <c>min(height-limit, ratio × width)</c>.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm LaissezVibrerTie / RepeatTie
    /// (details . ((height-limit . 1.0) (ratio . 0.333))).</remarks>
    private const double BowRatio = 0.333;
    private const double BowHeightLimit = 1.0;

    /// <summary>Arc height (peak above the baseline): ratio × width, capped at the
    /// height-limit — the height LilyPond's bezier bow gives a tie of this width.</summary>
    private static readonly double ArcHeight = Math.Min(BowHeightLimit, BowRatio * TieLength);

    /// <summary>Y offset from notehead center to the tie's flat baseline. Lily# placement
    /// approximation (~notehead half-height, no single LP constant); LP anchors the semi-tie
    /// at the note-head edge via Semi_tie_column.</summary>
    private const double NoteOffset = 0.4;

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
                if (measure.Items[ii] is not NoteItem note)
                    continue;
                if (!note.HasLaissezVibrer && !note.HasRepeatTie)
                    continue;

                if (note.HasLaissezVibrer)
                    builder.Add(BuildLayout(note, mi, ii, measureLayout, system, staffIndex,
                        TieVariantKind.LaissezVibrer));
                if (note.HasRepeatTie)
                    builder.Add(BuildLayout(note, mi, ii, measureLayout, system, staffIndex,
                        TieVariantKind.Repeat));
            }
        }

        return builder.ToImmutable();
    }

    private static TieVariantLayout BuildLayout(
        NoteItem note, int measureIndex, int itemIndex,
        MeasureLayout measureLayout, SystemLayout system, int staffIndex,
        TieVariantKind kind)
    {
        // Reads the raw item slot (X and Width). Safe on every path: MultiStaffLayouter
        // derives Items[i].X/.Width FROM the timing columns (see
        // MeasureLayouter.LayoutItemsFromColumns), so the slot equals the column-grid X the
        // renderer draws the notehead at even when a bar opens with a mid-piece time/clef
        // change; single-staff layouts have no columns and the slot is already the grid.
        var itemLayout = measureLayout.Items[itemIndex];
        double noteCenterX = measureLayout.X + itemLayout.X + itemLayout.Width / 2.0;

        // Curve direction: opposite to stem, like a regular tie.
        // LILYPOND-REF: lily/tie.cc — direction defaults opposite to stem.
        bool curveUp = !note.StemUp;

        const double StaffHeight = 4.0;
        // Within-system Y offset (device, down from the system top) of the staff
        // middle, NOT an absolute page Y — so the tie's Y/control points are
        // independent of where paging places the system. DrawTieVariants resolves
        // the system-top Y-up and subtracts these, keeping the output byte-identical
        // to the former absolute origin while decoupling from SystemLayout.Y for the
        // Stage-4 W2 stacking-origin flip (step 2a MMR / step 2b Ledger). The
        // internal arc geometry stays device-frame (intentional-device island 2).
        double staffMiddleOffset = LayoutUtilities.StaffOffsetInSystem(system, staffIndex)
            + StaffHeight / 2.0;
        double noteY = staffMiddleOffset - note.StaffPosition / 2.0;
        double baseY = curveUp ? noteY - NoteOffset : noteY + NoteOffset;

        // Half-tie geometry: starts at the note edge, extends TieLength away.
        // LaissezVibrer extends to the RIGHT; RepeatTie comes in from the LEFT.
        // LILYPOND-REF: lily/laissez-vibrer-engraver.cc — extends RIGHT
        // LILYPOND-REF: lily/repeat-tie-engraver.cc — extends LEFT
        double anchorX = noteCenterX + (kind == TieVariantKind.LaissezVibrer ? itemLayout.Width / 2.0 : -itemLayout.Width / 2.0);
        double freeX = anchorX + (kind == TieVariantKind.LaissezVibrer ? TieLength : -TieLength);

        double startX = Math.Min(anchorX, freeX);
        double endX = Math.Max(anchorX, freeX);

        double directedHeight = curveUp ? -ArcHeight : ArcHeight;
        // Cubic-bezier control points inset from each end by 0.3 of the tie length — a
        // bow-shape approximation (LP builds the tie bezier from tie-details rather than a
        // single inset fraction; 0.3 reproduces the near-circular arc well enough here).
        double indent = TieLength * 0.3;
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
            SourcePosition: note.SourcePosition,
            StaffIndex: staffIndex);
    }
}
