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
            "clarinet" or "clarinet-a" => (ClefType.Treble, 4),
            "bassoon" => (ClefType.Bass, 3),

            // Saxophones. Every one of them READS treble, whatever it sounds — and each
            // needs its own spelling because "alto" and "tenor" are already the VOICE
            // presets, and a saxophone is not a voice.
            "soprano-sax" or "alto-sax" or "tenor-sax" or "baritone-sax"
                => (ClefType.Treble, 4),

            // Brass
            "trumpet" or "trumpet-c" => (ClefType.Treble, 4),
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
    /// The octave a part's bare letters are anchored to, from the three things that can say
    /// so: an explicit <c>octave N</c>, else an <c>instrument</c> preset's own octave, else
    /// the octave the CLEF implies.
    /// </summary>
    /// <param name="explicitOctave">The part's <c>octave N</c>, or null.</param>
    /// <param name="instrumentPreset">The part's <c>instrument</c> preset, or null.</param>
    /// <param name="clef">The part's clef; treble when it names none.</param>
    /// <remarks>
    /// ⚠️ ONE HOME, because it is one quantity and it had drifted into three: the layout
    /// (MeasureCollector.GetPartDefaults), the LilyPond exporter (which wrote
    /// <c>\relative c'</c> for every part until it was given this chain) and the MIDI
    /// exporter — which had the first two steps and NOT the clef, so a bare
    /// <c>part m { clef bass }</c> printed C3 and played C4. MusicXML has none of the three
    /// and writes C4 for everything; when that is fixed it comes here too.
    /// <para>
    /// ⚠️ The preset beats the clef even when both are written: <c>instrument flute</c>
    /// anchors at 5 while its treble clef would say 4. See <see cref="GetDefaults"/>.
    /// </para>
    /// ⚠️ NOT used by ABSOLUTE mode, which anchors at middle C whatever the clef
    /// (OctaveContext says so in as many words).
    /// </remarks>
    public static int AnchorOctave(int? explicitOctave, string? instrumentPreset, ClefType clef)
    {
        if (explicitOctave is { } o)
            return o;
        if (!string.IsNullOrEmpty(instrumentPreset))
            return GetDefaults(instrumentPreset!).Octave;
        return GetDefaultOctave(clef);
    }

    /// <summary>
    /// The octave a bare letter means in ABSOLUTE mode: an explicit <c>octave N</c>, else
    /// middle C — and nothing else.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE TWO MODES ANCHOR DIFFERENTLY ON PURPOSE, and this one is the short rule.
    /// OctaveContext says it in as many words: "Absolute-mode anchor: bare c =
    /// C(OctaveBase). Defaults to 4 … and is overridden ONLY by an explicit
    /// <c>part X { octave N }</c> … The clef default is deliberately NOT used here."
    /// Neither the clef nor an instrument preset reaches it — <see cref="AnchorOctave"/>
    /// is the relative-mode chain and takes both.
    /// <para>
    /// Reading one where the other belongs is not academic: it made a bass part written
    /// <c>octave absolute</c> play a third lower than it prints.
    /// </para>
    /// </remarks>
    public static int AbsoluteBaseOctave(int? explicitOctave) => explicitOctave ?? 4;

    /// <summary>
    /// The <c>.lys</c> clef WORD for a clef — the spelling a part's <c>clef</c> property
    /// would have to use to name it, and the inverse of the word→<see cref="ClefType"/>
    /// switches the parsers read.
    /// </summary>
    /// <remarks>
    /// One source for "what would a part have to write to get this clef", because two
    /// places need it and they must agree: the collector, resolving an <c>instrument</c>
    /// preset to the clef it implies (MeasureCollector.GetPartDefaults), and the LilyPond
    /// exporter, writing that same clef into the twin.
    /// ⚠️ NOT the two DISPLAY mappings — <c>LayoutReport.StaffLabel</c> (prints <c>tab</c>
    /// and falls back to the enum name) and <c>LayoutEngine.ClefToString</c> (folds every
    /// unlisted clef to treble). Both are lossy on purpose and neither is a clef word a
    /// part could be written with.
    /// </remarks>
    public static string ClefWord(ClefType clef) => clef switch
    {
        ClefType.Treble => "treble",
        ClefType.Bass => "bass",
        ClefType.Alto => "alto",
        ClefType.Tenor => "tenor",
        ClefType.Treble8Below => "treble_8",
        ClefType.Treble8Above => "treble^8",
        ClefType.Soprano => "soprano",
        ClefType.MezzoSoprano => "mezzosoprano",
        ClefType.Baritone => "baritone",
        ClefType.Bass8Below => "bass_8",
        ClefType.Percussion => "percussion",
        _ => "treble",
    };

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

        // The CHROMATIC transposers. Written C sounds the named pitch, so the shift is
        // negative for every instrument that sounds below what it reads.
        // ⚠️ THIS SHIFTS PLAYBACK, NOT THE PAGE. You write what the player reads and the
        // part prints exactly that; the value here is what MIDI (and a tab's frets) apply
        // so it SOUNDS right. Writing at concert pitch and having the part transposed for
        // you is the other convention and is NOT implemented — see the remark below.
        // ⚠️ A bare name takes the common member of its family, because that is the
        // instrument people mean when they write no more: clarinet and trumpet are the
        // B♭ ones, horn is in F. The other members have their own spellings.
        "clarinet" or "trumpet" or "soprano-sax" => -2,   // in B♭
        "clarinet-a" => -3,                               // in A
        "trumpet-c" => 0,                                 // in C — sounds as written
        "horn" or "french-horn" => -7,                    // in F
        "alto-sax" => -9,                                 // in E♭
        "tenor-sax" => -14,                               // in B♭, an octave lower
        "baritone-sax" => -21,                            // in E♭, an octave lower

        _ => 0,
    };

    /// <summary>
    /// ⚠️ WHAT THE CONCERT-PITCH CONVENTION WOULD STILL NEED, recorded here so the next
    /// step does not lose it. <see cref="GetTransposition"/> answers "what does this part
    /// SOUND", which makes playback right for a part written the way its player reads it.
    /// The other direction — write concert pitch, print a transposed part — is a separate
    /// feature, and pitch is only half of it: a transposing part also carries its OWN KEY
    /// SIGNATURE (an E♭ alto saxophone part of a piece in C major prints in A major).
    /// Shifting the printed pitches without the signature would put every such part in the
    /// wrong key, so the two have to land together, along with a way to ask for the score
    /// at concert pitch.
    /// </summary>
    internal const string ConcertPitchIsNotImplemented =
        "written pitch in, written pitch out; the preset shifts only what is heard";

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
    /// The markers <c>transposition</c> takes, beside the switch that reads them so the two
    /// cannot drift — <c>InstrumentDefaultsTests</c> holds the pair together, and the editor's
    /// grammar reads this list rather than keeping a fourth copy.
    /// </summary>
    /// <remarks>
    /// ⚠️ The <c>ToLowerInvariant</c> above cannot fire, and reading it as case-insensitivity
    /// is wrong — measured 2026-08-19, after a first draft of this comment claimed exactly that.
    /// <c>transposition 8VB</c> is REFUSED; only the REFUSER moved later that same day.
    /// It used to be the LEXER, which split <c>8VB</c> into <c>8</c> and <c>VB</c> and then
    /// said three things about the property name and none about the value. The lexer now takes
    /// the suffix whole whatever its case, so a wrong-case marker reaches
    /// <c>SymbolCaseValidator</c> and is refused there by the Ordinal rule every other symbol
    /// in a part header already obeyed — one sentence, naming the value and listing the four
    /// markers.
    /// ⚠️⚠️ Which is why the validator asks <see cref="TranspositionMarkers"/> and NOT this
    /// method. Asking this method would ACCEPT <c>8VB</c>, because of the lowering right
    /// above — measured 2026-08-19, when the validator's first draft did ask it and the
    /// spelling sailed through. The lowering therefore still cannot fire for a book that
    /// compiles, and it is kept only so that deleting it does not read as a claim that case
    /// was never meant to matter here. It matters: markers are lower-case.
    /// </remarks>
    public static readonly IReadOnlyList<string> TranspositionMarkers =
        ["8va", "8vb", "15ma", "15mb"];

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
        "flute", "piccolo", "oboe", "clarinet", "clarinet-a", "bassoon",
        // Saxophones — their own names, because "alto" and "tenor" are voices
        "soprano-sax", "alto-sax", "tenor-sax", "baritone-sax",
        // Brass
        "trumpet", "trumpet-c", "horn", "french-horn", "trombone", "tuba",
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
