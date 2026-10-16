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

using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Music;

/// <summary>
/// A bar rest written with no duration — <c>R</c>, <c>R*3</c> — lasts its bar, whatever the
/// meter (owner's decision 2026-10-02, LILYSHARP-OWN): a whole-bar rest is one sign in every
/// meter, and a 5/4 bar has no single note value to write it with (<c>R1</c> is four quarters).
/// LilyPond spells that bar <c>R4*5</c>, and so does the twin (<see cref="LilyPondDuration"/>);
/// written in a Lily# book, <c>R4*5</c> is read the same way (<see cref="WholeBars"/>).
/// </summary>
/// <remarks>
/// A bare <c>R</c> does not move the running duration: the note after it inherits what it
/// would have inherited before it. A bar rest WITH a duration (<c>R1</c>, <c>R2.*4</c>) keeps
/// its written length, as in LilyPond: <c>R&lt;dur&gt;*N</c> is dur × N of silence, and where
/// that is whole bars of the meter it is that many bars, each drawn as the bar's sign
/// (<c>R4*5</c> in 5/4 one bar, <c>R4*40</c> eight, <c>R1*3</c> in 3/4 four — what LilyPond
/// engraves). Until 2026-10-16 it was N bars of dur: <c>R4*5</c> in 5/4 drew five quarter rests
/// under LYS2001 and <c>R4*40</c> left 32 empty bars behind it (the OMR's report of that day).
/// In a pickup the bar is the pickup's length. A bare one in the middle of a bar, or under
/// <c>time none</c> (no bar length), is an error
/// (<see cref="DiagnosticCodes.BareBarRestNeedsABar"/>) and is read as the rest of the bar.
/// </remarks>
public static class BarRest
{
    /// <summary>Whether <paramref name="rest"/> is a bar rest with no written duration.</summary>
    public static bool IsBare(RestSyntax rest)
        => rest.RestText == "R" && !rest.DurationReading.IsPresent;

    /// <summary>
    /// The bars a run <c>R&lt;dur&gt;*N</c> spans — <paramref name="written"/> is its value (dur),
    /// <paramref name="count"/> the N — when dur × N is whole bars of <paramref name="bar"/>:
    /// <c>R4*5</c> in 5/4 is 1, <c>R4*40</c> 8, <c>R1*3</c> in 3/4 4, <c>R1*3</c> in 4/4 3 (the
    /// run as it was always read). Null where it is not whole bars (<c>R4*3</c> in 5/4 — read as
    /// N rests of dur, which the bar check then reports), or no bar is known (<c>time none</c>).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/parser.yy MULTI_MEASURE_REST — one event of dur × N, which
    /// the Multi_measure_rest_engraver lets run over as many bars as Timing closes in it.</remarks>
    public static int? WholeBars(Fraction written, int count, Fraction? bar)
    {
        if (count < 1 || bar is not { Numerator: > 0 } length || written.Numerator <= 0)
            return null;
        var bars = written * new Fraction(count) / length;
        return bars.Denominator == 1 ? bars.Numerator : null;
    }

    /// <summary>
    /// A bar of <paramref name="length"/> as one rest event: a note value and dots when one
    /// spells it (4/4 is <c>1</c>, 3/4 <c>2.</c>, 7/8 <c>2..</c> — the very event
    /// <c>R1</c> / <c>R2.</c> / <c>R2..</c> make, so a bare <c>R</c> draws, plays and exports
    /// exactly as those do), otherwise the length's own denominator times its numerator
    /// (5/4 is a quarter times 5 — LilyPond's <c>R4*5</c>).
    /// </summary>
    public static (int Value, int Dots, int Scale) Shape(Fraction length)
    {
        for (int dots = 0; dots <= 3; dots++)
        {
            // value·(2 − 2^−dots) = length → the undotted base is length·2^dots / (2^(dots+1) − 1).
            var baseLength = length * new Fraction(1 << dots, (1 << (dots + 1)) - 1);
            if (baseLength.Numerator == 1 && IsPowerOfTwo(baseLength.Denominator))
                return (baseLength.Denominator, dots, 1);
            if (baseLength.Denominator == 1 && baseLength.Numerator == 2)
                return (0, dots, 1); // a breve
        }
        return (length.Denominator, 0, length.Numerator);
    }

    /// <summary>The duration LilyPond writes for a bar of <paramref name="length"/>:
    /// <c>1</c>, <c>2.</c>, <c>\breve</c>, or <c>4*5</c>.</summary>
    public static string LilyPondDuration(Fraction length)
    {
        var (value, dots, scale) = Shape(length);
        string text = (value == 0 ? "\\breve" : value.ToString(System.Globalization.CultureInfo.InvariantCulture))
                      + new string('.', dots);
        return scale == 1 ? text : text + "*" + scale;
    }

    private static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;
}
