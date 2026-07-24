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

using System.Collections.Generic;
using System.Collections.Immutable;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// One break-aligned grob on a <see cref="BoundaryColumn"/>, with its
/// COLUMN-INTERNAL ink extent and its <c>extra-spacing-width</c>.
/// </summary>
/// <remarks>
/// <paramref name="Left"/>/<paramref name="Right"/> are the ink (the grob's
/// <c>X-extent</c> placed inside the column); the spacing box LilyPond feeds to a
/// skyline is that extent widened by the esw pair.
/// LILYPOND-REF: lily/separation-item.cc:163-186 boxes().
/// </remarks>
internal readonly record struct BoundaryColumnGrob(
    BreakAlignSymbol Symbol,
    double Left,
    double Right,
    double EswLeft,
    double EswRight);

/// <summary>
/// LilyPond's <c>NonMusicalPaperColumn</c> at a measure boundary: the ordered
/// break-align group holding the clef change, the bar line, and any key / time
/// change, each placed at its column-internal X.
/// </summary>
/// <remarks>
/// Lily# has no boundary paper column of its own — a measure boundary is stored
/// split, as the LEFT measure's <see cref="Measure.EndBarline"/> plus the RIGHT
/// measure's leading change items — so the same column has been reconstructed
/// ad hoc wherever it was needed. This type is that column, built once.
///
/// LILYPOND-REF: scm/define-grobs.scm:650-664 BreakAlignment.break-align-orders.
/// The UNBROKEN order (the one a mid-line boundary uses) is
/// <c>… clef, cue-clef, staff-bar, key-cancellation, key-signature,
/// time-signature …</c> — so a clef change sits BEFORE the bar line and a key or
/// time change AFTER it. That single fact is why the clef moves the column's
/// ORIGIN without moving the bar line.
///
/// Placement between adjacent grobs comes from the LEFT grob's <c>space-alist</c>
/// entry for the right symbol, via <see cref="BreakAlignSpacing.GetSpacing"/> —
/// the same table the line-start prefix uses. Measured on LilyPond 2.24.4, those
/// values ARE the edge gaps here: clef→staff-bar 0.700 (Clef.space-alist
/// <c>(staff-bar . (extra-space . 0.7))</c>), staff-bar→key 1.000,
/// staff-bar→time 0.750, key→time 1.150.
///
/// Key CANCELLATION is not a separate grob here: Lily# folds it into
/// <see cref="SpacingRules.GetKeySignatureChangeWidth"/>. That is a documented
/// approximation (LilyPond puts a real inter-grob gap between the cancellation
/// and the new signature), not a modelling claim of this type.
/// </remarks>
internal sealed class BoundaryColumn
{
    /// <summary>
    /// Staff-line Y-extent (positions -4..4 -> -2..2 ss). Every break-aligned grob
    /// here carries an extra-spacing-height that reaches the staff, and the bar line
    /// spans it, so giving every box this same Y makes them all overlap in Y — which
    /// is what makes a horizontal skyline distance reduce to the reach difference.
    /// LILYPOND-REF: scm/define-grobs.scm
    /// pure-from-neighbor-interface::extra-spacing-height-including-staff.
    /// </summary>
    internal const double StaffYBottom = -2.0;
    internal const double StaffYTop = 2.0;

    // separation-item.cc:166-167 — the default extra-spacing-width used by every grob
    // that declares none of its own (Clef and BarLine both).
    private const double EswDefaultLeft = -0.1;
    private const double EswDefaultRight = 0.1;

    /// <summary>The grobs on this column, left to right in break-align order.</summary>
    public ImmutableArray<BoundaryColumnGrob> Grobs { get; }

    private BoundaryColumn(ImmutableArray<BoundaryColumnGrob> grobs) => Grobs = grobs;

    /// <summary>
    /// Builds the column standing between two measures: <paramref name="barline"/> is
    /// the bar line drawn there, <paramref name="leadingItems"/> the RIGHT measure's
    /// items (scanned only as far as its leading zero-duration changes).
    /// </summary>
    public static BoundaryColumn Build(
        BarlineType barline, IEnumerable<MusicItem>? leadingItems)
    {
        ClefChangeItem? clef = null;
        KeySignatureChangeItem? key = null;
        TimeSignatureChangeItem? time = null;
        if (leadingItems != null)
        {
            foreach (var item in leadingItems)
            {
                if (item is ClefChangeItem c) clef = c;
                else if (item is KeySignatureChangeItem k) key = k;
                else if (item is TimeSignatureChangeItem t) time = t;
                else if (item.Duration > Fraction.Zero) break; // reached the music
            }
        }

        // Unbroken break-align order: clef, staff-bar, key-signature, time-signature
        // (LILYPOND-REF define-grobs.scm:650-664), each with its extra-spacing-width. A clef
        // change sits BEFORE the bar line and a key/time change AFTER it — the single fact that
        // moves the column ORIGIN without moving the bar line.
        var candidates = new List<(BreakAlignSymbol Symbol, double Width, double EswLeft, double EswRight)>();
        if (clef != null)
            candidates.Add((BreakAlignSymbol.Clef,
                SpacingRules.GetClefChangeWidth(clef.NewClef), EswDefaultLeft, EswDefaultRight));
        candidates.Add((BreakAlignSymbol.StaffBar,
            EngravingDefaults.BarlineDrawnWidth(barline), EswDefaultLeft, EswDefaultRight));
        if (key != null)
            // scm/define-grobs.scm KeySignature extra-spacing-width (0.0 . 1.0).
            candidates.Add((BreakAlignSymbol.KeySignature,
                SpacingRules.GetKeySignatureChangeWidth(key), 0.0, 1.0));
        if (time != null)
            // scm/define-grobs.scm TimeSignature extra-spacing-width (0.0 . 0.8).
            candidates.Add((BreakAlignSymbol.TimeSignature,
                SpacingRules.GetTimeSignatureChangeWidth(time), 0.0, 0.8));

        // The SAME break-align walk the line-start prefix uses (BreakAlignSpacing.SolveColumns):
        // each present grob is placed at the previous grob's ink right + the space-alist distance,
        // empty grobs skipped. startLeft 0 — a mid-line boundary's first grob IS the column origin.
        var items = new List<(BreakAlignSymbol, double)>();
        foreach (var c in candidates)
            items.Add((c.Symbol, c.Width));
        var placed = BreakAlignSpacing.SolveColumns(items, startLeft: 0.0);

        var grobs = ImmutableArray.CreateBuilder<BoundaryColumnGrob>();
        foreach (var pc in placed)
        {
            // Symbols are unique on a boundary column, so the esw joins back by symbol.
            foreach (var c in candidates)
                if (c.Symbol == pc.Symbol)
                {
                    grobs.Add(new BoundaryColumnGrob(pc.Symbol, pc.Left, pc.Right, c.EswLeft, c.EswRight));
                    break;
                }
        }

        return new BoundaryColumn(grobs.ToImmutable());
    }

    /// <summary>
    /// The bar line's column-internal LEFT edge — how far the column's origin sits to
    /// the LEFT of the bar line. Zero unless a clef change precedes it.
    /// </summary>
    /// <remarks>
    /// This is exactly the quantity LilyPond subtracts to make a note's IDEAL distance
    /// bar-line-relative rather than column-relative:
    /// <c>ideal -= staff_bar_group-&gt;extent (right_col, X_AXIS)[LEFT]</c>
    /// (lily/note-spacing.cc:99-100, reached when the right column is non-musical and
    /// NoteSpacing's <c>space-to-barline</c> is set — scm/define-grobs.scm:2655).
    /// </remarks>
    /// <remarks>
    /// Null when this boundary draws no bar line at all — LilyPond has no staff-bar group
    /// then (<c>find_nonempty_break_align_group</c> returns null for an empty extent,
    /// break-alignment-interface.cc:96-107) and note-spacing.cc:99-107 takes its OTHER
    /// branch, measuring to the column's right side instead. Reached in Lily# only when a
    /// run opens the piece, where the real left bound is the system prefix rather than a
    /// bar line — a case this column does not yet model.
    /// </remarks>
    public double? BarLineLeft
    {
        get
        {
            foreach (var g in Grobs)
                if (g.Symbol == BreakAlignSymbol.StaffBar)
                    return g.Left;
            return null;
        }
    }

    /// <summary>
    /// The column's RIGHT horizontal skyline, measured in a frame whose origin is the
    /// bar line's left edge (i.e. everything from the bar line rightwards).
    /// </summary>
    /// <remarks>
    /// Lily#'s measure boundary is priced bar line to bar line, not column origin to
    /// column origin, so this — not the whole column — is what a bar-line-framed rod
    /// may measure against. A clef change is therefore excluded not by a special case
    /// but by where it sits: BEFORE the bar line, and so outside this frame.
    /// LILYPOND-REF: lily/separation-item.cc:163-186 (box = extent + esw).
    /// </remarks>
    public HorizontalSkyline RightSkylineFromBarLine()
    {
        // No bar line: nothing to frame against, so the column's own origin is the frame.
        double origin = BarLineLeft ?? 0;
        var boxes = new List<(double YBottom, double YTop, double XLeft, double XRight)>();
        foreach (var g in Grobs)
        {
            if (g.Left < origin)
                continue; // left of the bar line — outside this frame
            boxes.Add((StaffYBottom, StaffYTop,
                g.Left - origin + g.EswLeft, g.Right - origin + g.EswRight));
        }
        return HorizontalSkyline.FromBoxes(boxes, HorizontalDirection.Right);
    }
}
