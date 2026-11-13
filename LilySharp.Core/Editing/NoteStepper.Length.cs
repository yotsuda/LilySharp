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
using System.Globalization;

namespace LilySharp.Core.Editing;

/// <summary>
/// A length with a unit under the step keys — ONE HOME for what a fenced text's <c>xMargin</c>
/// and <c>yMargin</c> (NoteStepper.FenceMargin.cs) and a <c>paper { }</c> entry (NoteStepper.PaperLength.cs) share:
/// the steps of each unit, the walk to the last value that is not refused, and the number's
/// written form (owner's requests 2026-11-13).
/// </summary>
public static partial class NoteStepper
{
    /// <summary>The decimal places a stepped length is written with: the finest step's
    /// (<c>0.05in</c>).</summary>
    private const int LengthPlaces = 2;

    /// <summary>
    /// The steps of a length by its unit, the small one (Ctrl+Shift+Up / Down) and the large
    /// one (PageUp / PageDown). A bare number is staff spaces and steps as a <c>fonts { }</c>
    /// entry's <c>size N</c> does (<see cref="FontSizeEdit"/>: a tenth, and one).
    /// LILYSHARP-OWN: the physical units — a millimetre and five, and about as much in the
    /// other two: a millimetre in <c>cm</c>, 1.27 mm in <c>in</c>.
    /// </summary>
    private static (double Small, double Large) LengthSteps(string? unit) => unit switch
    {
        "mm" => (1, 5),
        "cm" => (0.1, 0.5),
        "in" => (0.05, 0.25),
        _ => (0.1, 1),
    };

    /// <summary>
    /// The value one press takes a length to, or null when it stays where it is: the large step
    /// is as many small steps as it holds, and the press takes the most of them that reach a
    /// value at or over <paramref name="floor"/> that <paramref name="refuses"/> does not refuse
    /// (a large step stops at the edge rather than refusing: CountEdit's rule).
    /// </summary>
    /// <param name="now">The length written.</param>
    /// <param name="steps">Its small and large step (<see cref="LengthSteps"/>).</param>
    /// <param name="dir">Up (&gt; 0) or Down.</param>
    /// <param name="small">The small step (the semitone pair) or the large one.</param>
    /// <param name="floor">The least a Down may reach.</param>
    /// <param name="refuses">Whether a value is one the validator refuses; null refuses none.</param>
    /// <param name="places">The decimal places the result is rounded to.</param>
    private static double? SteppedLength(double now, (double Small, double Large) steps, int dir, bool small,
        double floor, Func<double, bool>? refuses, int places = LengthPlaces)
    {
        var (fine, coarse) = steps;
        int most = small ? 1 : (int)Math.Round(coarse / fine);
        for (int k = most; k >= 1; k--)
        {
            double tried = Math.Round(dir > 0 ? now + k * fine : Math.Max(floor, now - k * fine), places);
            if (tried == now)
                continue;
            if (refuses?.Invoke(tried) != true)
                return tried == 0 ? 0 : tried;      // never a "-0"
        }
        return null;
    }

    /// <summary>A stepped length as it is written: the number as a <c>size</c>'s is (<c>21</c>,
    /// <c>2.6</c>, <c>0.55</c>) and its unit glued on (none for staff spaces).</summary>
    private static string SpelledLength(double value, string? unit, int places = LengthPlaces)
        => value.ToString("0." + new string('#', places), CultureInfo.InvariantCulture) + unit;
}
