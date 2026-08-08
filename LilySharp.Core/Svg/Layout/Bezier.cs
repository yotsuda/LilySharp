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

using System;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// A cubic bezier with the operations LilyPond's slur machinery consumes:
/// point evaluation, x→y lookup, horizontal-tangent solve, and the rigid
/// transforms <c>fit_factor</c> runs the curve through. This is the Bezier 器
/// the layout was missing (see <see cref="BezierBow.MidpointHeight"/>'s debt
/// note) — bows scored and drawn as four loose tuples now have a type that can
/// be EVALUATED, so scorers read the real curve instead of a parabola.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/bezier.cc:105-148 — curve_coordinate and curve_point.
/// LILYPOND-REF: lily/bezier.cc:73-92 — get_other_coordinate takes the first
/// solution of solve_point.
/// LILYPOND-REF: lily/bezier.cc:229-237 — solve_point, cubic roots filtered to
/// [0,1] by filter_solutions.
/// LILYPOND-REF: lily/bezier.cc:213-224 — solve_derivative, x'·dy − y'·dx = 0.
/// LILYPOND-REF: lily/bezier.cc:319-342 complex_multiply — scale, rotate, translate.
/// </remarks>
internal struct Bezier
{
    public double X0, Y0, X1, Y1, X2, Y2, X3, Y3;

    public Bezier(double x0, double y0, double x1, double y1,
        double x2, double y2, double x3, double y3)
    {
        X0 = x0; Y0 = y0; X1 = x1; Y1 = y1;
        X2 = x2; Y2 = y2; X3 = x3; Y3 = y3;
    }

    /// <summary>Bernstein evaluation of one axis at <paramref name="t"/>.</summary>
    private static double Coordinate(double c0, double c1, double c2, double c3, double t)
    {
        double mt = 1 - t;
        return c0 * mt * mt * mt
             + 3 * c1 * t * mt * mt
             + 3 * c2 * t * t * mt
             + c3 * t * t * t;
    }

    public readonly double CurveX(double t) => Coordinate(X0, X1, X2, X3, t);
    public readonly double CurveY(double t) => Coordinate(Y0, Y1, Y2, Y3, t);

    /// <summary>
    /// Power-basis coefficients (a0 + a1·t + a2·t² + a3·t³) of one axis.
    /// </summary>
    private static (double A0, double A1, double A2, double A3) PowerCoefs(
        double c0, double c1, double c2, double c3) =>
        (c0,
         3 * (c1 - c0),
         3 * (c0 - 2 * c1 + c2),
         -c0 + 3 * c1 - 3 * c2 + c3);

    /// <summary>
    /// All t in [0,1] where the X coordinate equals <paramref name="x"/>.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/bezier.cc:230-237 solve_point.</remarks>
    public readonly int SolvePointX(double x, Span<double> roots)
    {
        var (a0, a1, a2, a3) = PowerCoefs(X0, X1, X2, X3);
        return SolveInUnitInterval(a0 - x, a1, a2, a3, roots);
    }

    /// <summary>
    /// The curve's Y at the given X — the first [0,1] solution, like LilyPond's
    /// <c>get_other_coordinate</c>. Returns 0 when x is outside the curve
    /// (LP raises a programming_error and returns 0 too).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/bezier.cc:74-92 get_other_coordinate.</remarks>
    public readonly double GetOtherCoordinate(double x)
    {
        Span<double> roots = stackalloc double[3];
        int n = SolvePointX(x, roots);
        if (n == 0)
            return 0.0;
        return CurveY(roots[0]);
    }

    /// <summary>
    /// All t in [0,1] where the tangent is horizontal (y'(t) = 0) — LilyPond's
    /// <c>solve_derivative (Offset (1, 0))</c>, whose combine polynomial reduces
    /// to −y'.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/bezier.cc:214-224 solve_derivative.</remarks>
    public readonly int SolveHorizontalTangent(Span<double> roots)
    {
        var (_, a1, a2, a3) = PowerCoefs(Y0, Y1, Y2, Y3);
        // y'(t) = a1 + 2·a2·t + 3·a3·t²
        return SolveInUnitInterval(a1, 2 * a2, 3 * a3, 0.0, roots);
    }

    public void Translate(double dx, double dy)
    {
        X0 += dx; Y0 += dy; X1 += dx; Y1 += dy;
        X2 += dx; Y2 += dy; X3 += dx; Y3 += dy;
    }

    public void Scale(double sx, double sy)
    {
        X0 *= sx; Y0 *= sy; X1 *= sx; Y1 *= sy;
        X2 *= sx; Y2 *= sy; X3 *= sx; Y3 *= sy;
    }

    /// <summary>Rotates about the origin by <paramref name="degrees"/>.</summary>
    public void Rotate(double degrees)
    {
        double rad = degrees * Math.PI / 180.0;
        double c = Math.Cos(rad), s = Math.Sin(rad);
        (X0, Y0) = (c * X0 - s * Y0, s * X0 + c * Y0);
        (X1, Y1) = (c * X1 - s * Y1, s * X1 + c * Y1);
        (X2, Y2) = (c * X2 - s * Y2, s * X2 + c * Y2);
        (X3, Y3) = (c * X3 - s * Y3, s * X3 + c * Y3);
    }

    /// <summary>
    /// Real roots of a0 + a1·t + a2·t² + a3·t³ inside [0,1] (inclusive, with a
    /// small tolerance like LilyPond's solution filter), ascending. Degrades
    /// gracefully through quadratic/linear when leading coefficients vanish.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: flower/polynomial.cc:271-289 solve_cubic — Polynomial::solve
    /// dispatches on the effective degree (solve_quadric / solve_linear too).
    /// LILYPOND-REF: lily/bezier.cc:201-208 — filter_solutions drops roots
    /// outside [0,1].
    /// </remarks>
    internal static int SolveInUnitInterval(
        double a0, double a1, double a2, double a3, Span<double> roots)
    {
        const double degenerateEps = 1e-12;
        Span<double> all = stackalloc double[3];
        int n = 0;

        if (Math.Abs(a3) > degenerateEps)
        {
            // Normalized cubic t³ + p·t² + q·t + r, solved by the trigonometric /
            // Cardano split on the discriminant.
            double p = a2 / a3, q = a1 / a3, r = a0 / a3;
            double sh = p / 3.0;
            double b = q - p * p / 3.0;
            double c = 2 * p * p * p / 27.0 - p * q / 3.0 + r;
            double disc = c * c / 4.0 + b * b * b / 27.0;
            if (disc > degenerateEps)
            {
                double sq = Math.Sqrt(disc);
                double u = Math.Cbrt(-c / 2.0 + sq);
                double v = Math.Cbrt(-c / 2.0 - sq);
                all[n++] = u + v - sh;
            }
            else if (disc < -degenerateEps)
            {
                double m = 2 * Math.Sqrt(-b / 3.0);
                double theta = Math.Acos(Math.Clamp(3 * c / (b * m), -1.0, 1.0)) / 3.0;
                for (int k = 0; k < 3; k++)
                    all[n++] = m * Math.Cos(theta - 2 * Math.PI * k / 3.0) - sh;
            }
            else
            {
                double u = Math.Cbrt(-c / 2.0);
                all[n++] = 2 * u - sh;
                all[n++] = -u - sh;
            }
        }
        else if (Math.Abs(a2) > degenerateEps)
        {
            double disc = a1 * a1 - 4 * a2 * a0;
            if (disc >= 0)
            {
                double sq = Math.Sqrt(disc);
                all[n++] = (-a1 + sq) / (2 * a2);
                all[n++] = (-a1 - sq) / (2 * a2);
            }
        }
        else if (Math.Abs(a1) > degenerateEps)
        {
            all[n++] = -a0 / a1;
        }

        const double edgeEps = 1e-6;
        int count = 0;
        for (int i = 0; i < n; i++)
        {
            double t = all[i];
            if (t >= -edgeEps && t <= 1 + edgeEps)
                roots[count++] = Math.Clamp(t, 0.0, 1.0);
        }
        // Ascending order (n ≤ 3: insertion sort).
        for (int i = 1; i < count; i++)
        {
            double t = roots[i];
            int j = i - 1;
            while (j >= 0 && roots[j] > t)
            {
                roots[j + 1] = roots[j];
                j--;
            }
            roots[j + 1] = t;
        }
        return count;
    }
}
