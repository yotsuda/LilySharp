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

namespace LilySharp.Core.Semantics;

/// <summary>
/// Which repeat sign a <c>repeat percent</c> body earns, chosen — as LilyPond chooses it —
/// from the body's LENGTH against the measure length, once, for the whole repeat.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/percent-repeat-iterator.cc:75-92 Percent_repeat_iterator::next_element —
///   the test is <c>body_length_.main_part_ == mlen</c> / <c>== mlen * 2</c> against the
///   CONTEXT's measure_length, and everything else falls to its <c>else</c>.
/// </remarks>
internal enum PercentBodyShape
{
    /// <summary>No meter to measure against, or a body of no length: no sign at all.</summary>
    None,

    /// <summary>One measure — the single percent sign, centred in the repeated measure.</summary>
    Single,

    /// <summary>Two measures — the double percent, straddling the bar line between them.</summary>
    Double,

    /// <summary>
    /// Shorter than a measure — a beat slash, LilyPond's third branch, ported 2026-08-29.
    /// </summary>
    BeatSlash,

    /// <summary>
    /// LILYSHARP-OWN: a subdivision of LilyPond's <c>else</c>, which does not name it.
    /// Three or more WHOLE measures, which is where Lily# and LilyPond deliberately part.
    /// LilyPond engraves one bare slash and then leaves the repetition's remaining measures
    /// completely empty (measured on 2.26.0, scratch/p282/wholebody.ly); Lily# marks each
    /// repeated measure with a single percent instead. Neither picture says what the music
    /// is, which is what <c>Diagnostic.PercentBodyTooLong</c> exists to tell the writer.
    /// </summary>
    WholeMeasureRun,

    /// <summary>
    /// LILYSHARP-OWN, and the other half of the same subdivision.
    /// Longer than a measure but not a whole number of them — a malformed body, in practice
    /// one whose measures are already reported short or long. Marked per measure like
    /// <see cref="WholeMeasureRun"/>, and NOT warned about, because the bar diagnostics that
    /// fire on the same body say the useful thing first.
    /// </summary>
    Ragged,
}

/// <summary>
/// The one home for "which sign does this body earn". Two callers ask, and they must not
/// disagree: <c>MeasureCollector</c> asks in order to emit the sign, and
/// <c>MeasureValidator</c> asks in order to warn about the one shape whose sign cannot say
/// what the music does. A warning that fired on a body the collector signs differently would
/// be worse than no warning at all.
/// </summary>
/// <remarks>
/// ⚠️ THE TWO CALLERS MEASURE THE LENGTH SEPARATELY, and that is deliberate: the collector
/// reads it off <c>MeasureBuilder</c>, which is doing the real bar-closing, while the
/// validator walks the body itself because no builder exists at that layer. What is shared
/// is the RULE, which is the part that would drift. The two measurements are checked against
/// each other by sweep rather than by construction — a corpus census counted 30 books with a
/// whole-measure run through the collector, and the validator's warning must land on exactly
/// those 30. It does (2026-08-29: 30 books, 94 warnings, no book missed and none spurious),
/// and getting there took two corrections on the validator's side, both of which the census
/// is what caught.
/// </remarks>
internal static class PercentRepeatShape
{
    /// <summary>Classifies a body by its played length against the measure length.</summary>
    public static PercentBodyShape Classify(Fraction bodyLength, Fraction measureLength)
    {
        if (measureLength <= Fraction.Zero || bodyLength <= Fraction.Zero)
            return PercentBodyShape.None;
        if (bodyLength == measureLength)
            return PercentBodyShape.Single;
        if (bodyLength == measureLength + measureLength)
            return PercentBodyShape.Double;
        if (bodyLength < measureLength)
            return PercentBodyShape.BeatSlash;
        return WholeMeasures(bodyLength, measureLength) is > 0
            ? PercentBodyShape.WholeMeasureRun
            : PercentBodyShape.Ragged;
    }

    /// <summary>
    /// How many whole measures the body is, or 0 when it is not a whole number of them.
    /// Only meaningful for <see cref="PercentBodyShape.WholeMeasureRun"/>, whose message
    /// names the count.
    /// </summary>
    public static int WholeMeasures(Fraction bodyLength, Fraction measureLength)
    {
        if (measureLength <= Fraction.Zero || bodyLength <= Fraction.Zero)
            return 0;
        // Fraction is exact, so this is an exact divisibility question and not a tolerance
        // one: (a/b) / (c/d) = (a·d) / (b·c), whole iff the numerator divides evenly.
        long num = (long)bodyLength.Numerator * measureLength.Denominator;
        long den = (long)bodyLength.Denominator * measureLength.Numerator;
        return den != 0 && num % den == 0 ? (int)(num / den) : 0;
    }
}
