// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Parts of this file are ported from LilyPond, the GNU music typesetter.
// The C# is a modified translation of the following, not a copy of it:
//   lily/skyline.cc
//     Copyright (C) 2006--2026 Joe Neeman <joeneeman@gmail.com>
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
/// One building in a skyline: a horizon interval [<see cref="Start"/>, <see cref="End"/>]
/// carrying a sloped roof, value = <see cref="Slope"/>·coord + <see cref="Intercept"/>.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/skyline.cc:32-46, 98-176 Building struct
///
/// Axis-neutral. A VERTICAL skyline uses X as the horizon axis and (signed) Y as the
/// value; a HORIZONTAL skyline uses Y as the horizon axis and (signed) X as the value.
/// This is the single geometry primitive shared by <see cref="VerticalSkyline"/> and
/// <see cref="HorizontalSkyline"/> — previously duplicated as the axis-transposed
/// <c>Building</c> / <c>HorizontalBuilding</c> structs (line-for-line identical math).
/// </remarks>
internal readonly struct SkylineBuilding
{
    /// <summary>Left/bottom end of the horizon interval.</summary>
    public double Start { get; }
    /// <summary>Right/top end of the horizon interval.</summary>
    public double End { get; }
    public double Slope { get; }
    public double Intercept { get; }

    // Numerical tolerances (same magnitudes as LilyPond's skyline.cc precompute).
    private const double FlatnessEpsilon = 1e-10;     // value/length deltas within this collapse to flat
    private const double MaxSlope = 1e6;              // steeper roofs are treated as flat-at-max (near-vertical)
    private const double SlopeEqualityEpsilon = 1e-4; // near-parallel roofs: no meaningful intersection

    /// <summary>
    /// Creates a sloped building spanning [<paramref name="start"/>, <paramref name="end"/>]
    /// with value <paramref name="startValue"/> at start and <paramref name="endValue"/> at end.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/skyline.cc:98-105, 116-138 precompute()</remarks>
    public SkylineBuilding(double start, double startValue, double endValue, double end)
    {
        Start = start;
        End = end;

        if (double.IsInfinity(start) || double.IsInfinity(end))
        {
            // Infinite buildings must have constant value.
            Slope = 0;
            Intercept = startValue;
        }
        else if (Math.Abs(startValue - endValue) < FlatnessEpsilon)
        {
            // Flat building.
            Slope = 0;
            Intercept = startValue;
        }
        else
        {
            double length = end - start;
            if (Math.Abs(length) < FlatnessEpsilon)
            {
                Slope = 0;
                Intercept = Math.Max(startValue, endValue);
            }
            else
            {
                Slope = (endValue - startValue) / length;

                // Too steep - treat as flat at max value.
                if (Math.Abs(Slope) > MaxSlope)
                {
                    Slope = 0;
                    Intercept = Math.Max(startValue, endValue);
                }
                else
                {
                    Intercept = startValue - Slope * start;
                }
            }
        }
    }

    /// <summary>Creates a flat building (constant <paramref name="value"/>).</summary>
    public SkylineBuilding(double start, double end, double value)
        : this(start, value, value, end)
    {
    }

    /// <summary>Value of the roof at the given horizon coordinate.</summary>
    /// <remarks>LILYPOND-REF: lily/skyline.cc:140-144 Building::height()</remarks>
    public double ValueAt(double coord)
        => double.IsInfinity(coord) ? Intercept : Slope * coord + Intercept;

    /// <summary>Horizon coordinate where this building's roof intersects another's.</summary>
    /// <remarks>LILYPOND-REF: lily/skyline.cc:157-167 Building::intersection_x()</remarks>
    public double Intersection(SkylineBuilding other)
    {
        double slopeDelta = other.Slope - Slope;

        // If slopes are very close, avoid division by a small number.
        if (Math.Abs(slopeDelta) < SlopeEqualityEpsilon)
            return Math.Max(Start, other.Start);

        return (Intercept - other.Intercept) / slopeDelta;
    }

    /// <summary>True if this building's roof is above the other's at the given coordinate.</summary>
    /// <remarks>LILYPOND-REF: lily/skyline.cc:169-176 Building::above()</remarks>
    public bool Above(SkylineBuilding other, double coord)
    {
        if (double.IsInfinity(Intercept) || double.IsInfinity(other.Intercept) || double.IsInfinity(coord))
            return Intercept > other.Intercept;

        return (Slope - other.Slope) * coord + Intercept > other.Intercept;
    }

    /// <summary>A copy restricted to [<paramref name="newStart"/>, <paramref name="newEnd"/>],
    /// with roof values recomputed at the new endpoints.</summary>
    public SkylineBuilding WithRange(double newStart, double newEnd)
        => new SkylineBuilding(newStart, ValueAt(newStart), ValueAt(newEnd), newEnd);

    /// <summary>A copy with the roof raised by <paramref name="d"/> in the value frame
    /// (= adding <paramref name="d"/> to the intercept).</summary>
    /// <remarks>LILYPOND-REF: lily/skyline.cc:512 Skyline::raise — y_intercept_ += sky*r.</remarks>
    public SkylineBuilding RaisedBy(double d)
        => new SkylineBuilding(Start, ValueAt(Start) + d, ValueAt(End) + d, End);

    /// <summary>A copy translated by <paramref name="s"/> along the horizon axis, keeping
    /// the same roof value at each (translated) point.</summary>
    /// <remarks>LILYPOND-REF: lily/skyline.cc:519 Skyline::shift — x_ += s, y_intercept_ -= s*slope_.</remarks>
    public SkylineBuilding ShiftedHorizon(double s)
        => new SkylineBuilding(Start + s, ValueAt(Start), ValueAt(End), End + s);

    /// <summary>A copy uniformly scaled about the origin by <paramref name="k"/> (both the
    /// horizon interval and the value). Used to shrink cue-sized accidental glyphs.</summary>
    public SkylineBuilding ScaledBy(double k)
        => new SkylineBuilding(Start * k, ValueAt(Start) * k, ValueAt(End) * k, End * k);

    public override string ToString()
        => $"SkylineBuilding[{Start:F2}, {End:F2}] v = {Slope:F4}c + {Intercept:F2}";
}

/// <summary>Geometry kernels shared by the axis-typed skyline classes.</summary>
internal static class SkylineMath
{
    /// <summary>
    /// LilyPond <c>internal_distance</c> between two opposite-facing skylines,
    /// given their (sign-convention) building lists. The gap value at a horizon
    /// coordinate is <c>b1.ValueAt(c) + b2.ValueAt(c)</c>; that sum is linear
    /// over any overlap, so its maximum is always at an endpoint — only the
    /// overlap ends are sampled. Representation-independent: a building shadowed
    /// by a taller one can never win the max, so a sparse (concatenated) list
    /// yields the same result as a fully resolved envelope.
    /// </summary>
    /// <returns>
    /// The maximum penetration in the LilyPond internal (sign*coordinate) frame;
    /// larger means closer/overlapping. <see cref="double.NegativeInfinity"/>
    /// when either side is empty (no constraint).
    /// </returns>
    /// <remarks>
    /// LILYPOND-REF: lily/skyline.cc:529-533, 617-649 internal_distance().
    /// ⚠️ ALL-PAIRS ON PURPOSE: <see cref="HorizontalSkyline"/> keeps a LAZY building
    /// list (overlapping, unsorted — its envelope contract), so every pair must be
    /// visited. A RESOLVED list (sorted, disjoint — <see cref="VerticalSkyline"/>'s
    /// invariant) can take the O(n+m) merge walk instead:
    /// <see cref="DistanceResolved"/>, which is what LilyPond's own iterator walk is.
    /// ⚠️ THE PARAMETER TYPE IS THE CONCRETE LIST, and that is not a preference: both
    /// kernels are reached only from the two skyline classes, which each hold a
    /// <c>List&lt;SkylineBuilding&gt;</c>, and a <c>foreach</c> over the INTERFACE boxes
    /// <c>List&lt;T&gt;.Enumerator</c> on the heap. MEASURED (2026-09-20, session 443, the
    /// reader's corpus, 231 books × 8 keystrokes): the inner loop ran once per outer
    /// building, so the padded distance call alone allocated 806,926 B a keystroke — 64.0 B
    /// a box (16 header + 8 list + 4 index + 4 version + 32 current), 12.5% of the whole
    /// keystroke, and the same figure with tiered compilation and dynamic PGO left ON.
    /// </remarks>
    public static double Distance(List<SkylineBuilding> a, List<SkylineBuilding> b)
    {
        if (a.Count == 0 || b.Count == 0)
            return double.NegativeInfinity;

        double max = double.NegativeInfinity;
        foreach (var b1 in a)
            max = Math.Max(max, Against(b1, b));
        return max;
    }

    /// <summary>
    /// The all-pairs body for ONE building against a whole list: the largest gap value over
    /// the pairs it forms, <see cref="double.NegativeInfinity"/> if it overlaps none of them.
    /// </summary>
    /// <remarks>
    /// Split out so <see cref="DistancePadded"/> can hand it a pad building that was never
    /// stored anywhere. Taking the maximum a building at a time is the same number as taking
    /// it over all pairs at once: <see cref="Math.Max(double, double)"/> rounds nothing,
    /// absorbs NaN and ranks -0.0 with +0.0, so the value does not depend on the order the
    /// pairs are visited in.
    /// </remarks>
    private static double Against(in SkylineBuilding b1, List<SkylineBuilding> b)
    {
        double max = double.NegativeInfinity;
        foreach (var b2 in b)
        {
            double lo = Math.Max(b1.Start, b2.Start);
            double hi = Math.Min(b1.End, b2.End);
            // <=, not <: a ZERO-WIDTH overlap — buildings that merely TOUCH at one
            // coordinate — is a real pairing in LilyPond, whose walk evaluates both
            // heights AT every merge boundary, so two grobs whose seed boxes share an
            // edge constrain each other at full height.
            // ⚠️ THE BOOK THAT MEASURES IT RIDES ON THE OTHER ARM. Both arms carried the
            // same sentence, naming stems-clash-between-staves.ly (the upper staff's down
            // stem ends exactly where the lower staff's up stem begins, x 18.425, and the
            // whole 6.5 + 3.333 clearance rides on that point). POISONED (2026-09-20,
            // session 443): turning THIS `<=` into `<` leaves the whole suite green, while
            // the same edit to DistanceResolved reddens exactly one net
            // (StaffClashSpacingTests.ShiftedVoicesDownStem_PushesTheStaffBelowClear). The
            // pairing is kept here because both arms must answer the same question — not
            // because a book is watching this one.
            // LILYPOND-REF: lily/skyline.cc:628-645 internal_distance — start_dist is taken at start == end after the boundary advance, so the zero-length segment still contributes.
            if (lo <= hi)
            {
                double dLo = b1.ValueAt(lo) + b2.ValueAt(lo);
                double dHi = b1.ValueAt(hi) + b2.ValueAt(hi);
                max = Math.Max(max, Math.Max(dLo, dHi));
            }
        }
        return max;
    }

    /// <summary>
    /// <see cref="Distance"/> against the list <paramref name="a"/> THICKENED along the
    /// horizon by <paramref name="horizonPadding"/> — the same number
    /// <c>Distance(a.Padded(hp), b)</c> gives, without the padded list existing.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/skyline.cc:530-554 Skyline::distance(other, horizon_padding), which
    /// pads one side and reuses the other as-is.
    /// ⚠️ A DISTANCE ONLY EVER READS THE PADDED OUTLINE ONCE. Building it first cost a
    /// 5n-element list per call — MEASURED (2026-09-20, session 443): 171.85 calls a
    /// keystroke, 14.47 buildings apiece, 407,587 B a keystroke, and not one of those calls
    /// had an empty side to be let off by. Generating each pad building where it is used
    /// keeps every pair the list version would have formed (<see cref="Pads"/> is the one
    /// spelling of the geometry, shared with <see cref="HorizontalSkyline.PaddedCopy"/>,
    /// which does store its answer) and allocates nothing.
    /// </remarks>
    public static double DistancePadded(List<SkylineBuilding> a, double horizonPadding, List<SkylineBuilding> b)
    {
        if (a.Count == 0 || b.Count == 0)
            return double.NegativeInfinity;

        Span<SkylineBuilding> pads = stackalloc SkylineBuilding[MaxPads];
        double max = double.NegativeInfinity;
        foreach (var b1 in a)
        {
            max = Math.Max(max, Against(b1, b));
            int n = Pads(b1, horizonPadding, pads);
            for (int p = 0; p < n; p++)
                max = Math.Max(max, Against(pads[p], b));
        }
        return max;
    }

    /// <summary>The most pad buildings one building can have — two at each end.</summary>
    public const int MaxPads = 4;

    /// <summary>
    /// Writes the pad buildings that thicken ONE building along the horizon by
    /// <paramref name="horizonPadding"/> into <paramref name="into"/> (at most
    /// <see cref="MaxPads"/>) and returns how many there are: a 45° ramp and a flat shelf at
    /// each end the building actually has, and nothing at an end that runs to infinity or
    /// whose roof is NegativeInfinity.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/skyline.cc:558-615 Skyline::padded. Heights are in the sign frame
    /// (sky*coordinate); subtracting the padding lowers the roof for both directions.
    /// The padding falls off at 45°, so a neighbour that is far away ALONG the horizon
    /// pushes less than a directly-facing one.
    /// </remarks>
    public static int Pads(in SkylineBuilding b, double horizonPadding, Span<SkylineBuilding> into)
    {
        double hp = horizonPadding;
        int n = 0;
        if (!double.IsInfinity(b.Start))
        {
            double h = b.ValueAt(b.Start);
            if (!double.IsNegativeInfinity(h))
            {
                into[n++] = new SkylineBuilding(b.Start - 2 * hp, h - hp, h, b.Start - hp);
                into[n++] = new SkylineBuilding(b.Start - hp, h, h, b.Start);
            }
        }
        if (!double.IsInfinity(b.End))
        {
            double h = b.ValueAt(b.End);
            if (!double.IsNegativeInfinity(h))
            {
                into[n++] = new SkylineBuilding(b.End, h, h, b.End + hp);
                into[n++] = new SkylineBuilding(b.End + hp, h, h - hp, b.End + 2 * hp);
            }
        }
        return n;
    }

    /// <summary>
    /// <see cref="Distance"/> for RESOLVED building lists (sorted by start,
    /// non-overlapping — <see cref="VerticalSkyline"/>'s invariant): the O(n+m) merge
    /// walk. Identical to the all-pairs loop on every strict overlap — a pair's linear
    /// sum takes its extremum at an overlap endpoint either way. At a ZERO-WIDTH touch
    /// where BOTH lists step at the same x, the two are not the same loop: the
    /// all-pairs visit also sums the cross pair (earlier building here, later building
    /// there) that this walk never pairs — and neither does LilyPond's, whose iterator
    /// advance this walk copies (skyline.cc:640-644). THIS walk is the literal port;
    /// the all-pairs loop serves the lazy envelope contract, where summing every
    /// covering pair is the point.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/skyline.cc:617-649 internal_distance() — LilyPond advances
    ///   two iterators in step (:640-644), never the cross product; its skylines are
    ///   always resolved. The all-pairs cost was what the pointwise dynamics port made
    ///   visible: a label's outline (~dozens of buildings) against a full system
    ///   profile (~hundreds) per dynamic priced a dynamics-heavy page at 3×
    ///   (measured 2026-07-30; this walk restored it).
    /// </remarks>
    public static double DistanceResolved(List<SkylineBuilding> a, List<SkylineBuilding> b)
    {
        if (a.Count == 0 || b.Count == 0)
            return double.NegativeInfinity;

        double max = double.NegativeInfinity;
        int i = 0, j = 0;
        while (i < a.Count && j < b.Count)
        {
            var b1 = a[i];
            var b2 = b[j];
            double lo = Math.Max(b1.Start, b2.Start);
            double hi = Math.Min(b1.End, b2.End);
            // <=, not <: a zero-width touch counts, as in the all-pairs loop above —
            // LilyPond's walk reaches the same pairing through its zero-length merge
            // segment (skyline.cc:628-645: after the boundary advance, start == end and
            // start_dist is still taken). stems-clash-between-staves.ly measures it, and
            // THIS is the arm it measures: poisoned to `<` (session 443) it reddens
            // StaffClashSpacingTests.ShiftedVoicesDownStem_PushesTheStaffBelowClear, where
            // the all-pairs arm's identical edit leaves the suite green.
            if (lo <= hi)
            {
                double dLo = b1.ValueAt(lo) + b2.ValueAt(lo);
                double dHi = b1.ValueAt(hi) + b2.ValueAt(hi);
                max = Math.Max(max, Math.Max(dLo, dHi));
            }
            // Advance the list whose building ends first — sorted and disjoint per
            // list, so every overlapping pair is still visited (skyline.cc:640-644).
            // ⚠️ The range said :645-648 until 2026-08-09 (session 121) — that is the
            // loop EXIT (touch_point/return), the neighbour of the advance it named.
            if (b1.End <= b2.End) i++;
            else j++;
        }
        return max;
    }
}
