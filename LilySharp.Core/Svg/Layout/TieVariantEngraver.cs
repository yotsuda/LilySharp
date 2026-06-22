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
    /// <summary>Start X (the side closer to the host note).</summary>
    double StartX,
    /// <summary>End X (the side away from the note, into empty space).</summary>
    double EndX,
    /// <summary>Y of both endpoints (the tie sits flat at this height).</summary>
    double Y,
    /// <summary>Bezier control point 1.</summary>
    (double X, double Y) Control1,
    /// <summary>Bezier control point 2.</summary>
    (double X, double Y) Control2,
    /// <summary>True = curve up, false = curve down.</summary>
    bool CurveUp,
    int SourcePosition);

/// <summary>
/// Engraver for half-ties (LaissezVibrerTie and RepeatTie).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/laissez-vibrer-engraver.cc / lily/repeat-tie-engraver.cc
/// </remarks>
public static class TieVariantEngraver
{
    /// <summary>
    /// Length of the half-tie in staff spaces (the curve extends this far from the host note).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm LaissezVibrerTie minimum-length default 1.5.
    /// We use a slightly shorter visual length matching engraving convention.
    /// </remarks>
    private const double TieLength = 1.0;

    /// <summary>Arc height (peak above the baseline) for the half-tie.</summary>
    /// <remarks>LILYPOND-REF: lily/bezier-bow.cc — short ties use a small arc.</remarks>
    private const double ArcHeight = 0.35;

    /// <summary>Y offset from notehead center to the tie's flat baseline.</summary>
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
        var itemLayout = measureLayout.Items[itemIndex];
        double noteCenterX = measureLayout.X + itemLayout.X + itemLayout.Width / 2.0;

        // Curve direction: opposite to stem, like a regular tie.
        // LILYPOND-REF: lily/tie.cc — direction defaults opposite to stem.
        bool curveUp = !note.StemUp;

        double staffY = LayoutUtilities.FindStaffYInSystem(system, staffIndex);
        const double StaffHeight = 4.0;
        double staffMiddleY = staffY + StaffHeight / 2.0;
        double noteY = StaffFrame.PositionToDevice(note.StaffPosition, staffMiddleY);
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
            SourcePosition: note.SourcePosition);
    }
}
