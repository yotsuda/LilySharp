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

using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Calculates durations from syntax nodes.
/// </summary>
public static class DurationCalculator
{
    /// <summary>
    /// Gets the duration of a note, using default if not specified.
    /// </summary>
    public static Fraction GetDuration(NoteSyntax note, Fraction defaultDuration)
        => note.Duration?.ToFraction() ?? defaultDuration;

    /// <summary>
    /// Gets the duration of a rest, using default if not specified.
    /// </summary>
    public static Fraction GetDuration(RestSyntax rest, Fraction defaultDuration)
        => rest.Duration?.ToFraction() ?? defaultDuration;

    /// <summary>
    /// Gets the duration of a chord, using default if not specified.
    /// </summary>
    public static Fraction GetDuration(ChordSyntax chord, Fraction defaultDuration)
        => chord.Duration?.ToFraction() ?? defaultDuration;

    /// <summary>
    /// Parses a time signature like "4/4" or "3/4".
    /// </summary>
    public static Fraction ParseTimeSignature(int beats, int beatUnit)
    {
        // Time signature 4/4 means 4 quarter notes = 4 * 1/4 = 1
        // Time signature 3/4 means 3 quarter notes = 3 * 1/4 = 3/4
        return new Fraction(beats, beatUnit);
    }

    /// <summary>
    /// Calculates the total duration of items in a music block.
    /// </summary>
    public static Fraction CalculateMeasureDuration(MusicBlockSyntax block, Fraction defaultDuration)
    {
        var total = Fraction.Zero;
        var currentDefault = defaultDuration;

        foreach (var item in block.Items)
        {
            switch (item)
            {
                case NoteSyntax note:
                    var noteDuration = GetDuration(note, currentDefault);
                    if (note.Duration != null)
                        currentDefault = noteDuration; // Update default
                    total += noteDuration;
                    break;

                case RestSyntax rest:
                    var restDuration = GetDuration(rest, currentDefault);
                    if (rest.Duration != null)
                        currentDefault = restDuration;
                    total += restDuration;
                    break;

                case ChordSyntax chord:
                    var chordDuration = GetDuration(chord, currentDefault);
                    if (chord.Duration != null)
                        currentDefault = chordDuration;
                    total += chordDuration;
                    break;

                case BarlineSyntax:
                    // Barlines don't affect duration
                    break;

                case TieSyntax:
                case SlurSyntax:
                    // Ties and slurs don't add duration
                    break;
            }
        }

        return total;
    }
}