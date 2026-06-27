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
using System.Text;

namespace LilySharp.Core.Music;

/// <summary>
/// The set of named chord qualities Lily# understands in chord-name entry
/// (<c>c:maj7</c>, <c>a:m</c>, …). Each maps to a fixed interval set (semitones
/// above the root) and a display suffix.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/chord-entry.scm — default chord step construction; the
/// named modifiers in ly/chord-modifiers-init.ly (m, dim, aug, maj, sus). The
/// interval sets below are the standard jazz/pop spellings LilyPond produces.
/// </remarks>
public enum ChordQuality
{
    Major,
    Minor,
    Diminished,
    Augmented,
    Dominant7,
    Major7,
    Minor7,
    MinorMajor7,
    Diminished7,
    HalfDiminished7,
    Major6,
    Minor6,
    Dominant9,
    Major9,
    Minor9,
    Sus2,
    Sus4,
    Dominant7Sus4,
}

/// <summary>Interval set (semitones above the root) and display suffix for a quality.</summary>
public readonly record struct ChordQualityInfo(ImmutableArray<int> Intervals, string DisplaySuffix);

/// <summary>
/// Maps chord-entry quality tokens (the text after the <c>:</c>) to a
/// <see cref="ChordQuality"/>, and each quality to its interval set + display
/// suffix. This single table is the foundation the chord NAME display, future
/// staff-note expansion, and future fret diagrams all build on.
/// </summary>
public static class ChordQualityRegistry
{
    // semitone offsets from the root. P1=0, m3=3, M3=4, d5=6, P5=7, #5=8, M6/d7=9,
    // m7=10, M7=11, M9=14.
    private static readonly Dictionary<ChordQuality, ChordQualityInfo> Info = new()
    {
        [ChordQuality.Major] = new(ImmutableArray.Create(0, 4, 7), ""),
        [ChordQuality.Minor] = new(ImmutableArray.Create(0, 3, 7), "m"),
        [ChordQuality.Diminished] = new(ImmutableArray.Create(0, 3, 6), "dim"),
        [ChordQuality.Augmented] = new(ImmutableArray.Create(0, 4, 8), "aug"),
        [ChordQuality.Dominant7] = new(ImmutableArray.Create(0, 4, 7, 10), "7"),
        [ChordQuality.Major7] = new(ImmutableArray.Create(0, 4, 7, 11), "maj7"),
        [ChordQuality.Minor7] = new(ImmutableArray.Create(0, 3, 7, 10), "m7"),
        [ChordQuality.MinorMajor7] = new(ImmutableArray.Create(0, 3, 7, 11), "m maj7"),
        [ChordQuality.Diminished7] = new(ImmutableArray.Create(0, 3, 6, 9), "dim7"),
        [ChordQuality.HalfDiminished7] = new(ImmutableArray.Create(0, 3, 6, 10), "m7♭5"),
        [ChordQuality.Major6] = new(ImmutableArray.Create(0, 4, 7, 9), "6"),
        [ChordQuality.Minor6] = new(ImmutableArray.Create(0, 3, 7, 9), "m6"),
        [ChordQuality.Dominant9] = new(ImmutableArray.Create(0, 4, 7, 10, 14), "9"),
        [ChordQuality.Major9] = new(ImmutableArray.Create(0, 4, 7, 11, 14), "maj9"),
        [ChordQuality.Minor9] = new(ImmutableArray.Create(0, 3, 7, 10, 14), "m9"),
        [ChordQuality.Sus2] = new(ImmutableArray.Create(0, 2, 7), "sus2"),
        [ChordQuality.Sus4] = new(ImmutableArray.Create(0, 5, 7), "sus4"),
        [ChordQuality.Dominant7Sus4] = new(ImmutableArray.Create(0, 5, 7, 10), "7sus4"),
    };

    // Entry tokens (case-sensitive after the ':') that select each quality. Several
    // spellings map to the same quality (m / min, maj7 / maj, m7b5 / m7.5-).
    private static readonly Dictionary<string, ChordQuality> ByToken = new()
    {
        ["m"] = ChordQuality.Minor,
        ["min"] = ChordQuality.Minor,
        ["dim"] = ChordQuality.Diminished,
        ["aug"] = ChordQuality.Augmented,
        ["7"] = ChordQuality.Dominant7,
        ["maj7"] = ChordQuality.Major7,
        ["maj"] = ChordQuality.Major7,
        ["m7"] = ChordQuality.Minor7,
        ["min7"] = ChordQuality.Minor7,
        ["mmaj7"] = ChordQuality.MinorMajor7,
        ["dim7"] = ChordQuality.Diminished7,
        ["m7b5"] = ChordQuality.HalfDiminished7,
        ["m7.5-"] = ChordQuality.HalfDiminished7,
        ["6"] = ChordQuality.Major6,
        ["m6"] = ChordQuality.Minor6,
        ["min6"] = ChordQuality.Minor6,
        ["9"] = ChordQuality.Dominant9,
        ["maj9"] = ChordQuality.Major9,
        ["m9"] = ChordQuality.Minor9,
        ["min9"] = ChordQuality.Minor9,
        ["sus2"] = ChordQuality.Sus2,
        ["sus4"] = ChordQuality.Sus4,
        ["sus"] = ChordQuality.Sus4,
        ["7sus4"] = ChordQuality.Dominant7Sus4,
    };

    /// <summary>The interval set (semitones above the root) and display suffix.</summary>
    public static ChordQualityInfo GetInfo(ChordQuality quality) => Info[quality];

    /// <summary>
    /// Resolves a quality token (text after the <c>:</c>, e.g. "m7"); returns false
    /// for an unknown token. An empty/absent token is a plain major triad.
    /// </summary>
    public static bool TryResolve(string? token, out ChordQuality quality)
    {
        if (string.IsNullOrEmpty(token))
        {
            quality = ChordQuality.Major;
            return true;
        }
        return ByToken.TryGetValue(token, out quality);
    }

    /// <summary>All recognized quality tokens (for tooling / completion).</summary>
    public static IReadOnlyCollection<string> Tokens => ByToken.Keys;
}

/// <summary>
/// A fully resolved chord: a root, a quality (hence an interval set), and an
/// optional slash bass. The display name is derived from the structure; the
/// interval set is the foundation for future staff-note expansion and fret
/// diagrams. Phase 1 renders only the name.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/chord-ignatzek-names.scm — root + quality → printed name.
/// Lily# renders the name as PLAIN TEXT (e.g. "Cmaj7"), not LilyPond's
/// superscript/triangle typography — a deliberate Phase-1 simplification matching
/// the existing @chord text chord names.
/// </remarks>
public sealed record ChordStructure(
    int RootStep,            // 0=C, 1=D, … 6=B (diatonic step)
    int RootAlter,           // accidental: -2..+2 (semitone alteration)
    ChordQuality Quality,
    int? BassStep = null,    // slash bass diatonic step (null = no bass)
    int? BassAlter = null)
{
    /// <summary>Semitone offsets of the chord tones above the root (the pitch set).</summary>
    public ImmutableArray<int> Intervals => ChordQualityRegistry.GetInfo(Quality).Intervals;

    /// <summary>The printed chord symbol, e.g. "C", "Am7", "G7", "F♭maj7", "C/G".</summary>
    public string DisplayName
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append(SpellPitch(RootStep, RootAlter));
            sb.Append(ChordQualityRegistry.GetInfo(Quality).DisplaySuffix);
            if (BassStep is int bs)
            {
                sb.Append('/');
                sb.Append(SpellPitch(bs, BassAlter ?? 0));
            }
            return sb.ToString();
        }
    }

    /// <summary>Spells a diatonic step + alteration as a note name with a Unicode
    /// accidental (e.g. 0/+1 → "C♯", 6/-1 → "B♭"). Shared by the chord-name fallback.</summary>
    public static string SpellPitch(int step, int alter)
    {
        char letter = "CDEFGAB"[((step % 7) + 7) % 7];
        string acc = alter switch
        {
            -2 => "♭♭",
            -1 => "♭",
            0 => "",
            1 => "♯",
            2 => "♯♯",
            _ => "",
        };
        return letter + acc;
    }
}
