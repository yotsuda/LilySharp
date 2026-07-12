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

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Default settings for musical instruments.
/// </summary>
/// <remarks>
/// Provides clef and octave defaults based on instrument type.
/// Used for automatic configuration when a part specifies an instrument.
/// </remarks>
public static class InstrumentDefaults
{
    /// <summary>
    /// Splits an <c>instrument</c> property's value tokens into the PRESET (the bare
    /// words, e.g. <c>violin</c> / <c>bass-guitar</c> — which drive clef/octave/tuning
    /// and MIDI) and the DISPLAY name (a trailing quoted <c>"…"</c> label if present,
    /// else the preset). A quoted-only value (<c>instrument "1st Violin"</c>) yields
    /// that string as both, so free-text names keep working.
    /// </summary>
    public static (string Preset, string DisplayName) SplitInstrument(
        System.Collections.Generic.IEnumerable<string> valueTokenTexts)
    {
        string? label = null;
        var preset = new System.Text.StringBuilder();
        foreach (var t in valueTokenTexts)
        {
            if (t.Length >= 2 && t[0] == '"' && t[^1] == '"')
                label = t[1..^1];       // trailing quoted display label (last wins)
            else
                preset.Append(t);       // bare word / hyphen segment
        }
        string presetText = preset.Length > 0 ? preset.ToString() : (label ?? "");
        return (presetText, label ?? presetText);
    }

    /// <summary>
    /// Gets the default clef and octave for an instrument.
    /// </summary>
    /// <param name="instrument">The instrument name.</param>
    /// <returns>A tuple of (clef, octave) defaults.</returns>
    public static (ClefType Clef, int Octave) GetDefaults(string instrument)
    {
        return instrument.ToLowerInvariant() switch
        {
            // Strings
            "violin" => (ClefType.Treble, 4),
            "viola" => (ClefType.Alto, 3),
            "cello" => (ClefType.Bass, 3),
            "bass" or "contrabass" or "double-bass" or "bass-guitar" or "electric-bass"
                or "bass5" or "5-string-bass" or "bass6" or "6-string-bass" => (ClefType.Bass, 3),
            
            // Piano
            "piano-right" or "piano-treble" => (ClefType.Treble, 4),
            "piano-left" or "piano-bass" => (ClefType.Bass, 3),
            
            // Guitar (written octave higher than sounds)
            "guitar" or "acoustic-guitar" or "electric-guitar" => (ClefType.Treble8Below, 4),
            
            // Woodwinds
            "flute" or "piccolo" => (ClefType.Treble, 5),
            "oboe" => (ClefType.Treble, 4),
            "clarinet" => (ClefType.Treble, 4),
            "bassoon" => (ClefType.Bass, 3),
            
            // Brass
            "trumpet" => (ClefType.Treble, 4),
            "horn" or "french-horn" => (ClefType.Treble, 4),
            "trombone" => (ClefType.Bass, 3),
            "tuba" => (ClefType.Bass, 2),
            
            // Voice
            "soprano" or "voice-soprano" => (ClefType.Treble, 4),
            "alto" or "voice-alto" => (ClefType.Treble, 4),
            "tenor" or "voice-tenor" => (ClefType.Treble8Below, 4),  // treble_8 clef
            "voice-bass" => (ClefType.Bass, 3),
            
            // Default
            _ => (ClefType.Treble, 4)
        };
    }

    /// <summary>
    /// Gets the default octave for a clef type.
    /// Used when no instrument is specified.
    /// </summary>
    /// <param name="clef">The clef type.</param>
    /// <returns>The default starting octave.</returns>
    public static int GetDefaultOctave(ClefType clef)
    {
        return clef switch
        {
            ClefType.Treble => 4,  // Middle C = c'
            ClefType.Bass => 3,   // One octave below middle C
            ClefType.Alto => 3,   // Middle C on middle line
            ClefType.Tenor => 3,  // Middle C on 4th line
            ClefType.Treble8Below => 4,  // Same written pitch as treble
            _ => 4
        };
    }

    /// <summary>
    /// The default tablature tuning for a fretted/bass instrument, as a tuning name
    /// (the same names a <c>tab</c> render accepts), or null for instruments that are
    /// not played from tab. Lets <c>instrument bass</c> imply <c>tuning bass</c> when a
    /// part is shown as a tab and gives no explicit tuning.
    /// </summary>
    /// <remarks>
    /// Part of the <c>instrument</c>-as-preset role: <c>instrument</c> supplies clef,
    /// octave and (here) tab tuning defaults; <c>name</c> is the display label and
    /// explicit <c>clef</c>/<c>tuning</c> override the preset.
    /// </remarks>
    public static string? GetTuning(string? instrument) => instrument?.ToLowerInvariant() switch
    {
        "bass" or "bass-guitar" or "electric-bass" or "contrabass" or "double-bass" => "bass",
        "bass5" or "5-string-bass" => "bass5",
        "bass6" or "6-string-bass" => "bass6",
        "guitar" or "acoustic-guitar" or "electric-guitar" => "guitar",
        "ukulele" or "uke" => "ukulele",
        _ => null,
    };

    /// <summary>
    /// The preset's default SOUNDING transposition in semitones — the written→sounding
    /// octave a transposing instrument carries, EXCLUDING any octave already shown by
    /// the clef (guitar/tenor use a <c>treble_8</c> clef, so their octave rides the
    /// clef and this is 0). The bass family sounds an octave below its plain bass-clef
    /// notation (−12); the piccolo sounds an octave above (+12). This is the default a
    /// part's explicit <c>transposition</c> property overrides, and the single value the
    /// tab-fret shift and the MIDI playback pitch both read.
    /// </summary>
    /// <remarks>
    /// Mirrors MuseScore's <c>transposeChromatic</c> (instruments.xml): the bass carries
    /// an explicit −12 while the guitar leans on its <c>G8vb</c> clef.
    /// </remarks>
    public static int GetTransposition(string? instrument) => instrument?.ToLowerInvariant() switch
    {
        "bass" or "bass-guitar" or "electric-bass" or "contrabass" or "double-bass"
            or "bass5" or "5-string-bass" or "bass6" or "6-string-bass" => -12,
        "piccolo" => 12,
        _ => 0,
    };

    /// <summary>
    /// Maps a <c>transposition</c> property value (an ottava marker — <c>8va</c>,
    /// <c>8vb</c>, <c>15ma</c>, <c>15mb</c>) to its signed semitone shift, or null if
    /// the text is not a recognized marker. <c>vb</c>/<c>mb</c> = down (sounds lower),
    /// <c>va</c>/<c>ma</c> = up.
    /// </summary>
    public static int? ParseTranspositionSemitones(string value) => value.ToLowerInvariant() switch
    {
        "8va" => 12,
        "8vb" => -12,
        "15ma" => 24,
        "15mb" => -24,
        _ => null,
    };

    /// <summary>
    /// The instrument-name presets a part's <c>instrument</c> accepts, ordered by
    /// family (strings, piano, guitar/fretted, woodwinds, brass, voice). The single
    /// source of truth for <see cref="IsKnownInstrument"/> AND the editor's
    /// after-<c>instrument</c> name completion — keep it in step with BOTH the
    /// <see cref="GetDefaults"/> and <see cref="GetTuning"/> switches (a name either
    /// one recognizes belongs here).
    /// </summary>
    public static readonly IReadOnlyList<string> KnownInstruments = new[]
    {
        // Strings
        "violin", "viola", "cello", "bass", "contrabass", "double-bass",
        // Piano
        "piano-right", "piano-treble", "piano-left", "piano-bass",
        // Guitar / fretted (incl. the tab-tuning presets from GetTuning)
        "guitar", "acoustic-guitar", "electric-guitar",
        "bass-guitar", "electric-bass", "bass5", "5-string-bass", "bass6", "6-string-bass",
        "ukulele", "uke",
        // Woodwinds
        "flute", "piccolo", "oboe", "clarinet", "bassoon",
        // Brass
        "trumpet", "horn", "french-horn", "trombone", "tuba",
        // Voice
        "soprano", "voice-soprano", "alto", "voice-alto",
        "tenor", "voice-tenor", "voice-bass",
    };

    private static readonly HashSet<string> KnownSet =
        new(KnownInstruments, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Checks if the given string is a known instrument name.
    /// </summary>
    public static bool IsKnownInstrument(string name) => KnownSet.Contains(name);
}
