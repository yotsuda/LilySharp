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

using System.Text.RegularExpressions;

namespace LilySharp.Core.Midi;

/// <summary>One end of a part's range: the sounding pitch, its name, and where it is written.</summary>
/// <param name="Pitch">The MIDI pitch, fractional for a quarter tone.</param>
/// <param name="Name">As a musician names it, spelled as written where the source says (<c>B♭4</c>, C4 = middle C).</param>
/// <param name="Positions">The source offsets of the notes at this pitch, in the order they sound, each once.</param>
public sealed record RangeEnd(double Pitch, string Name, IReadOnlyList<int> Positions);

/// <summary>A part's lowest and highest note.</summary>
public sealed record PartRange(string Name, RangeEnd Low, RangeEnd High);

/// <summary>
/// The lowest and highest note of each part — to check a part's range, and for a singer to find
/// the highest note at once (owner's request 2026-10-25). Read off the notes the exporter plays,
/// so it is what SOUNDS: transpositions applied, every repeat and phrase expansion counted. A
/// part's chord row is accompaniment, not the part, and the drums have no pitch: both are left out.
/// </summary>
public static class PartRanges
{
    private const int DrumChannel = 9;
    private const string ChordRowSuffix = " (chords)";

    /// <summary>The ranges in <paramref name="midi"/>, parts in track order; <paramref name="source"/>
    /// spells each end as it is written (null: spelled from the pitch).</summary>
    public static IReadOnlyList<PartRange> Of(MidiFile midi, string? source)
    {
        var parts = new List<PartRange>();
        foreach (var track in midi.Tracks)
        {
            var notes = track.Notes
                .Where(n => n.Channel != DrumChannel && !(n.Part ?? track.Name).EndsWith(ChordRowSuffix, StringComparison.Ordinal))
                .OrderBy(n => n.StartTick)
                .ToList();
            if (notes.Count == 0)
                continue;
            static double Sounding(MidiNote n) => n.Pitch + 0.5 * n.QuarterBend;
            double low = notes.Min(Sounding), high = notes.Max(Sounding);
            parts.Add(new PartRange(track.Name, End(notes, low, source), End(notes, high, source)));
        }
        return parts;
    }

    private static RangeEnd End(List<MidiNote> notes, double pitch, string? source)
    {
        var at = notes.Where(n => n.Pitch + 0.5 * n.QuarterBend == pitch).ToList();
        var positions = at.Select(n => n.SourcePos).Where(p => p >= 0).Distinct().ToList();
        string? written = null;
        int midi = (int)Math.Round(pitch);
        foreach (int p in positions)
            if ((written = WrittenName(source, p, midi)) != null)
                break;
        return new RangeEnd(pitch, written ?? SpelledFromPitch(midi), positions);
    }

    private static readonly int[] StepSemitones = [0, 2, 4, 5, 7, 9, 11];
    private static readonly Regex PitchWord = new(@"\G\s*([a-g])(isis|eses|ses|is|es|s)?(?![a-z])", RegexOptions.Compiled);
    private static readonly Regex ChordPitch = new(@"(?<![a-z])([a-g])(isis|eses|ses|is|es|s)?(?![a-z])", RegexOptions.Compiled);

    /// <summary>
    /// The name of the pitch written at <paramref name="pos"/> that sounds <paramref name="midi"/>:
    /// the note there, or the chord member there whose pitch class matches. Null when none does
    /// (a transposed part, a note-name language the reading does not know).
    /// </summary>
    internal static string? WrittenName(string? source, int pos, int midi)
    {
        if (source == null || pos < 0 || pos >= source.Length)
            return null;
        int at = pos;
        while (at < source.Length && char.IsWhiteSpace(source[at])) at++;
        IEnumerable<Match> candidates;
        if (at < source.Length && source[at] == '<')
        {
            int close = source.IndexOf('>', at);
            if (close < 0) return null;
            candidates = ChordPitch.Matches(source[(at + 1)..close]);
        }
        else
        {
            var m = PitchWord.Match(source, at);
            candidates = m.Success ? [m] : [];
        }
        foreach (var m in candidates)
        {
            int step = m.Groups[1].Value[0] - 'c';
            if (step < 0) step += 7;   // a, b
            int alter = Alteration(m.Groups[2].Value);
            int natural = midi - alter;
            if (((natural - StepSemitones[step]) % 12 + 12) % 12 != 0)
                continue;
            int octave = (natural - StepSemitones[step]) / 12 - 1;
            return char.ToUpperInvariant(m.Groups[1].Value[0]) + Sign(alter) + octave;
        }
        return null;
    }

    // `as` and `es` are the flats of a and e; `is`/`isis` raise, `es`/`eses` (and s/ses) lower.
    private static int Alteration(string suffix) => suffix switch
    {
        "is" => 1,
        "isis" => 2,
        "es" or "s" => -1,
        "eses" or "ses" => -2,
        _ => 0,
    };

    private static string Sign(int alter) => alter switch
    {
        1 => "♯",
        2 => "𝄪",
        -1 => "♭",
        -2 => "𝄫",
        _ => "",
    };

    private static readonly string[] PitchClassNames = ["C", "C♯", "D", "E♭", "E", "F", "F♯", "G", "A♭", "A", "B♭", "B"];

    /// <summary>A pitch named without its written spelling: the common one of each pitch class.</summary>
    internal static string SpelledFromPitch(int midi) => PitchClassNames[((midi % 12) + 12) % 12] + (midi / 12 - 1);
}
