// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Parts of this file are ported from LilyPond, the GNU music typesetter.
// The C# is a modified translation of the following, not a copy of it:
//   lily/tie-formatting-problem.cc
//     Copyright (C) 2005--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
// LilyPond is free software under the GNU General Public License version 3 or
// later; its notices are kept here as that licence requires. The full list is in
// LILYPOND-ATTRIBUTION.md. Lily# is an independent project, not affiliated with
// or endorsed by the LilyPond project.
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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// One box of a tie's bound column. X is page X; Y is staff spaces above the staff's middle
/// line, up-positive — the frame <see cref="TieFormattingProblem"/> scores in and the frame
/// LilyPond's tie outline is built in.
/// </summary>
internal readonly record struct TieOutlineBox(double YDown, double YUp, double XLeft, double XRight);

/// <summary>A note head of a tie's bound column: its staff position and its X extent.</summary>
internal readonly record struct TieOutlineHead(int Position, double XLeft, double XRight);

/// <summary>
/// The STEM of a tie's bound column, as the outline reads it.
/// </summary>
/// <param name="IsNormal">
/// <c>Stem::is_normal_stem</c> — false for the invisible stem a whole note carries, which
/// takes the half-plane branch instead of a stem box.
/// </param>
/// <param name="CentreX">
/// The stem's own X ORIGIN, which is the head's edge inset by half the stem thickness —
/// <see cref="LayoutUtilities.StemX(double, bool, int, Model.NoteheadStyle, double)"/>.
/// LilyPond adds this as a POINT and widens it by
/// staff_space/20, so the box is 0.1 wide and NOT the stem's own 0.13.
/// </param>
/// <param name="TipY">The stem's far end (staff spaces from the middle line, up-positive).</param>
/// <param name="NearHeadPosition">
/// <c>Stem::head_positions (stem)[-stemdir]</c> — the head at the stem's FOOT. The box runs
/// from that head's POSITION, not from where the stem's ink actually starts on it.
/// </param>
/// <param name="SupportHeadCentreX">The support head's X centre, for the invisible-stem branch.</param>
internal readonly record struct TieOutlineStem(
    bool IsNormal, double CentreX, double TipY, int NearHeadPosition, double SupportHeadCentreX);

/// <summary>
/// What one bound COLUMN of a tie has — the input to <see cref="TieChordOutline.Build"/>,
/// which is <c>set_column_chord_outline</c>'s walk over the column's grobs.
/// </summary>
/// <remarks>
/// Split the way LilyPond splits it, because the two halves enter the outline differently:
/// the TIED heads also make <c>head_boxes</c> (the recession boxes and <c>head_extents_</c>
/// are built from those alone), the others only make boxes.
/// LILYPOND-REF: lily/tie-formatting-problem.cc:96-287 set_column_chord_outline.
/// </remarks>
internal sealed record TieColumnParts
{
    /// <summary>
    /// The heads this column's ties attach to — LilyPond's <c>bounds</c>. ⚠️ SORTED BY
    /// POSITION ASCENDING: the recession boxes take <c>boundary (head_boxes, updowndir, 0)</c>,
    /// which is the vector's FIRST or LAST element and not its extreme by Y, so the order is
    /// load-bearing (tie-formatting-problem.cc:249).
    /// </summary>
    public required IReadOnlyList<TieOutlineHead> TiedHeads { get; init; }

    /// <summary>
    /// The stem's OTHER note heads — the untied members of the same chord (:210-224). Their
    /// boxes take the head's own ink height, not the one-staff-space box the tied heads get.
    /// </summary>
    public IReadOnlyList<TieOutlineBox> OtherHeads { get; init; } = [];

    /// <summary>The stem, or null when the column has no head to hang one on.</summary>
    public TieOutlineStem? Stem { get; init; }

    /// <summary>Augmentation DOTS — LEFT bound only (:124).</summary>
    public IReadOnlyList<TieOutlineBox> Dots { get; init; } = [];

    /// <summary>The FLAG — LEFT bound only, and only on a normal stem (:181-190).</summary>
    public IReadOnlyList<TieOutlineBox> Flag { get; init; } = [];

    /// <summary>ACCIDENTALS — RIGHT bound only (:231-236).</summary>
    public IReadOnlyList<TieOutlineBox> Accidentals { get; init; } = [];

    /// <summary>Every head position on the stem, tied or not (:238-239).</summary>
    public IReadOnlyList<int> HeadPositions { get; init; } = [];
}

/// <summary>
/// One bound column's CHORD OUTLINE: the skyline a tie reads its attachment X off, plus the
/// head and stem extents the rest of the scorer asks for.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ THE ATTACHMENT IS Y-DEPENDENT AND THIS IS WHY. A tie does not attach to "the head's
/// edge" or "the head's centre" — it attaches wherever the column's outline stands at the
/// tie's own Y, and the outline is built from EVERY box the column has: each head, the dots,
/// the stem, the flag, the untied members of the chord, the accidentals. Above the topmost
/// head and below the bottommost one a RECESSION box takes over that stands at the head's
/// CENTRE (:243-258), which is what makes a tie clearing its heads come out one head wider
/// than one running alongside them.
/// </para>
/// <para>
/// ⚠️ IT REPLACED A TWO-ANCHOR RULE — the caller used to hand the problem an "inner edge" and
/// a "centre" X and a predicate picked between them (|curve y - note y| &gt; 0.5). That rule
/// reproduces the outline for ONE head and cannot see a chord at all: both ties of
/// <c>&lt;c d&gt;2 ~ &lt;c d&gt;2</c> came out the same width, where LilyPond makes them differ
/// by 0.888700 because the upper one runs past the right chord's STEM.
/// audit/lp-geometry tie.width.seconds.upper is that book.
/// </para>
/// LILYPOND-REF: lily/tie-formatting-problem.cc:96-287 set_column_chord_outline,
///   :73-87 get_attachment; lily/skyline.cc:719-725 set_minimum_height, :558-615 padded.
/// </remarks>
internal sealed class TieChordOutline
{
    /// <summary>
    /// LilyPond widens the stem's X POINT by this before boxing it — a twentieth of a staff
    /// space, its own source calling it "ugh." It is NOT half the stem's thickness (0.065),
    /// and reading the drawing rather than the line gives 0.015 too much on each side.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/tie-formatting-problem.cc:150-151 set_column_chord_outline,
    /// <c>x.widen (staff_space / 20)</c>.</remarks>
    private const double StemBoxHalfWidth = 1.0 / 20;

    private readonly HorizontalSkyline _skyline;

    /// <summary>The tied heads' X extent — <c>head_extents_</c>, which the horizontal-distance
    /// term measures against and which carries neither dots nor stem.</summary>
    public (double Left, double Right) HeadX { get; }

    /// <summary>The tied heads' Y extent (staff spaces, up-positive) — the head-edge hug reads
    /// its <c>[dir]</c> end (tie-formatting-problem.cc:497-504).</summary>
    public (double Down, double Up) HeadY { get; }

    /// <summary>The stem's box, or null when there is no normal stem — <c>stem_extents_</c>,
    /// which the attachment's stem avoidance reads (:596-607).</summary>
    public (double Left, double Right, double Down, double Up)? StemBox { get; }

    /// <summary>Every head position on the stem — <c>head_positions_</c> (:238-239), asked
    /// whether it contains a candidate's position (:526-527).</summary>
    public (int Low, int High)? HeadPositions { get; }

    private TieChordOutline(
        HorizontalSkyline skyline,
        (double Left, double Right) headX,
        (double Down, double Up) headY,
        (double Left, double Right, double Down, double Up)? stemBox,
        (int Low, int High)? headPositions)
    {
        _skyline = skyline;
        HeadX = headX;
        HeadY = headY;
        StemBox = stemBox;
        HeadPositions = headPositions;
    }

    /// <summary>
    /// Where this column's outline stands at <paramref name="y"/> — one end of
    /// <c>get_attachment</c>, BEFORE the note-head gap is taken off.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/tie-formatting-problem.cc:72-87 get_attachment —
    /// <c>chord_outlines_[…].height (y)</c>.</remarks>
    public double Attachment(double y) => _skyline.X(y);

    /// <summary>
    /// Builds the outline of one bound column.
    /// </summary>
    /// <param name="parts">The column's grobs, already reduced to boxes.</param>
    /// <param name="isLeftBound">
    /// LilyPond's <c>dir</c>: LEFT for the tie's starting column, RIGHT for its ending one.
    /// The skyline FACES the tie, so it is built with <c>-dir</c>.
    /// </param>
    /// <param name="skylinePadding">Tie detail <c>skyline-padding</c>.</param>
    /// <remarks>LILYPOND-REF: lily/tie-formatting-problem.cc:96-287 set_column_chord_outline.</remarks>
    public static TieChordOutline Build(TieColumnParts parts, bool isLeftBound, double skylinePadding)
    {
        int dir = isLeftBound ? -1 : +1;
        var boxes = new List<(double YBottom, double YTop, double XLeft, double XRight)>();
        var headBoxes = new List<TieOutlineBox>(parts.TiedHeads.Count);

        // The tied heads: a ONE-STAFF-SPACE box on the head's position, not the glyph's ink
        // height. LILYPOND-REF: :116-121.
        foreach (var head in parts.TiedHeads)
        {
            var box = new TieOutlineBox(
                (head.Position - 1) * 0.5, (head.Position + 1) * 0.5, head.XLeft, head.XRight);
            headBoxes.Add(box);
            boxes.Add((box.YDown, box.YUp, box.XLeft, box.XRight));
        }

        // The dots hang off the LEFT bound only — a tie leaves its own head past them and
        // arrives at the next head with nothing in the way. LILYPOND-REF: :123-139.
        if (isLeftBound)
            AddBoxes(boxes, parts.Dots);

        if (parts.Stem is { } stem)
        {
            if (stem.IsNormal)
            {
                // LILYPOND-REF: :146-179. The box runs from the FOOT HEAD'S POSITION to the
                // stem's far end, so it covers the head it stands on as well as the shaft.
                double x1 = stem.CentreX - StemBoxHalfWidth;
                double x2 = stem.CentreX + StemBoxHalfWidth;
                double yA = stem.NearHeadPosition * 0.5;
                double yB = stem.TipY;
                boxes.Add((Math.Min(yA, yB), Math.Max(yA, yB), x1, x2));

                // The flag hangs on the stem, and only the LEFT bound can meet it.
                // LILYPOND-REF: :181-190.
                if (isLeftBound)
                    AddBoxes(boxes, parts.Flag);
            }
            else
            {
                // An INVISIBLE stem (a whole note): instead of a shaft, a half-plane reaching
                // away from the support head's CENTRE across the heads' Y band, so nothing
                // hands the tie an x further in than that centre.
                // LILYPOND-REF: :192-208 "In case of invisible stem, don't pass x-center of heads."
                // ⚠️ IT IS SHADOWED BY THE HEAD BOXES over the heads themselves (their own edge
                // is further out in both directions); it only speaks in the GAPS of a spread chord.
                double down = double.PositiveInfinity, up = double.NegativeInfinity;
                foreach (var b in headBoxes)
                {
                    down = Math.Min(down, b.YDown);
                    up = Math.Max(up, b.YUp);
                }
                if (headBoxes.Count > 0)
                {
                    double xNear = stem.SupportHeadCentreX;
                    boxes.Add(dir < 0
                        ? (down, up, double.NegativeInfinity, xNear)
                        : (down, up, xNear, double.PositiveInfinity));
                }
            }

            // The chord's UNTIED heads (:210-224) and, on the RIGHT bound only, the
            // accidentals the next column carries (:226-236).
            AddBoxes(boxes, parts.OtherHeads);
            if (!isLeftBound)
                AddBoxes(boxes, parts.Accidentals);
        }

        // THE RECESSION BOXES. Above the topmost tied head and below the bottommost one the
        // outline steps back from the head's EDGE to its CENTRE and stays there forever.
        // ⚠️ THE CENTRE IS AN INTEGER-DIVISION ARTEFACT AND NOT A CHOICE: LilyPond writes
        //   x[-dir] = b[X].linear_combination (-dir / 2), and -dir/2 on the ±1 Direction is 0,
        //   so the combination collapses to the interval's midpoint rather than to its
        //   three-quarter point.
        // LILYPOND-REF: flower/include/interval.hh:303-316 linear_combination — its own comment
        // at :303 says the midpoint is "iv.linear_combination (0)"; the caller is
        // lily/tie-formatting-problem.cc:243-258 set_column_chord_outline.
        foreach (int updowndir in new[] { -1, +1 })
        {
            if (headBoxes.Count == 0)
                break;

            // boundary (head_boxes, updowndir, 0): the vector's first element for DOWN and its
            // last for UP — see TieColumnParts.TiedHeads on why the order is load-bearing.
            var b = updowndir < 0 ? headBoxes[0] : headBoxes[^1];
            double centre = (b.XLeft + b.XRight) * 0.5;
            double xLeft = dir < 0 ? b.XLeft : centre;
            double xRight = dir < 0 ? centre : b.XRight;
            double yDown = updowndir > 0 ? b.YUp : double.NegativeInfinity;
            double yUp = updowndir > 0 ? double.PositiveInfinity : b.YDown;
            boxes.Add((yDown, yUp, xLeft, xRight));
        }

        var skyline = HorizontalSkyline
            .FromBoxes(boxes, dir < 0 ? HorizontalDirection.Right : HorizontalDirection.Left)
            .PaddedCopy(skylinePadding);

        // head_extents_ and the floor, both from the TIED heads' union.
        // LILYPOND-REF: :271-286. ⚠️ The break-status branch (:262-270, a piece reattached to a
        //   system edge takes the STAFF's extent instead) has no counterpart here: this engine
        //   gives a broken piece no outline at all and anchors it to the system edge in the
        //   caller, which is named at TieFormattingProblem's _startX/_endX.
        double hxL = double.PositiveInfinity, hxR = double.NegativeInfinity;
        double hyD = double.PositiveInfinity, hyU = double.NegativeInfinity;
        foreach (var b in headBoxes)
        {
            hxL = Math.Min(hxL, b.XLeft);
            hxR = Math.Max(hxR, b.XRight);
            hyD = Math.Min(hyD, b.YDown);
            hyU = Math.Max(hyU, b.YUp);
        }
        if (headBoxes.Count > 0)
            skyline.SetMinimumHeight(dir < 0 ? hxL : hxR);

        (double, double, double, double)? stemBox = null;
        if (parts.Stem is { IsNormal: true } s)
        {
            double yA = s.NearHeadPosition * 0.5;
            stemBox = (s.CentreX - StemBoxHalfWidth, s.CentreX + StemBoxHalfWidth,
                       Math.Min(yA, s.TipY), Math.Max(yA, s.TipY));
        }

        (int, int)? headPositions = null;
        if (parts.HeadPositions.Count > 0)
        {
            int lo = int.MaxValue, hi = int.MinValue;
            foreach (int p in parts.HeadPositions)
            {
                lo = Math.Min(lo, p);
                hi = Math.Max(hi, p);
            }
            headPositions = (lo, hi);
        }

        return new TieChordOutline(skyline, (hxL, hxR), (hyD, hyU), stemBox, headPositions);
    }

    private static void AddBoxes(
        List<(double YBottom, double YTop, double XLeft, double XRight)> boxes,
        IReadOnlyList<TieOutlineBox> more)
    {
        foreach (var b in more)
            boxes.Add((b.YDown, b.YUp, b.XLeft, b.XRight));
    }
}
