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
using System.Collections.Generic;

namespace LilySharp.Core.Midi;

/// <summary>
/// The General MIDI sound set, spelled as LilyPond spells it: the 128 names a part's
/// <c>midiInstrument "…"</c> takes, and the program number each one selects.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/midi.scm:21-180 instrument-names-alist — the names in program order,
/// each mapped to <c>(- n 1)</c>, i.e. the 0-based program a MIDI program change carries.
/// The drum kits that follow them in that table (channel 10, +32768) are not part of this
/// vocabulary: a drum part already plays on channel 10.
/// </remarks>
public static class GeneralMidi
{
    /// <summary>The 128 instrument names; the index is the 0-based program number.</summary>
    public static readonly IReadOnlyList<string> InstrumentNames =
    [
        // 1-8 piano
        "acoustic grand", "bright acoustic", "electric grand", "honky-tonk",
        "electric piano 1", "electric piano 2", "harpsichord", "clav",
        // 9-16 chromatic percussion
        "celesta", "glockenspiel", "music box", "vibraphone",
        "marimba", "xylophone", "tubular bells", "dulcimer",
        // 17-24 organ
        "drawbar organ", "percussive organ", "rock organ", "church organ",
        "reed organ", "accordion", "harmonica", "concertina",
        // 25-32 guitar
        "acoustic guitar (nylon)", "acoustic guitar (steel)", "electric guitar (jazz)", "electric guitar (clean)",
        "electric guitar (muted)", "overdriven guitar", "distorted guitar", "guitar harmonics",
        // 33-40 bass
        "acoustic bass", "electric bass (finger)", "electric bass (pick)", "fretless bass",
        "slap bass 1", "slap bass 2", "synth bass 1", "synth bass 2",
        // 41-48 strings
        "violin", "viola", "cello", "contrabass",
        "tremolo strings", "pizzicato strings", "orchestral harp", "timpani",
        // 49-56 ensemble
        "string ensemble 1", "string ensemble 2", "synthstrings 1", "synthstrings 2",
        "choir aahs", "voice oohs", "synth voice", "orchestra hit",
        // 57-64 brass
        "trumpet", "trombone", "tuba", "muted trumpet",
        "french horn", "brass section", "synthbrass 1", "synthbrass 2",
        // 65-72 reed
        "soprano sax", "alto sax", "tenor sax", "baritone sax",
        "oboe", "english horn", "bassoon", "clarinet",
        // 73-80 pipe
        "piccolo", "flute", "recorder", "pan flute",
        "blown bottle", "shakuhachi", "whistle", "ocarina",
        // 81-88 synth lead
        "lead 1 (square)", "lead 2 (sawtooth)", "lead 3 (calliope)", "lead 4 (chiff)",
        "lead 5 (charang)", "lead 6 (voice)", "lead 7 (fifths)", "lead 8 (bass+lead)",
        // 89-96 synth pad
        "pad 1 (new age)", "pad 2 (warm)", "pad 3 (polysynth)", "pad 4 (choir)",
        "pad 5 (bowed)", "pad 6 (metallic)", "pad 7 (halo)", "pad 8 (sweep)",
        // 97-104 synth effects
        "fx 1 (rain)", "fx 2 (soundtrack)", "fx 3 (crystal)", "fx 4 (atmosphere)",
        "fx 5 (brightness)", "fx 6 (goblins)", "fx 7 (echoes)", "fx 8 (sci-fi)",
        // 105-112 ethnic
        "sitar", "banjo", "shamisen", "koto",
        "kalimba", "bagpipe", "fiddle", "shanai",
        // 113-120 percussive
        "tinkle bell", "agogo", "steel drums", "woodblock",
        "taiko drum", "melodic tom", "synth drum", "reverse cymbal",
        // 121-128 sound effects
        "guitar fret noise", "breath noise", "seashore", "bird tweet",
        "telephone ring", "helicopter", "applause", "gunshot",
    ];

    private static readonly Dictionary<string, int> ProgramByName = BuildIndex();

    private static Dictionary<string, int> BuildIndex()
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < InstrumentNames.Count; i++)
            index[InstrumentNames[i]] = i;
        return index;
    }

    /// <summary>The 0-based program a LilyPond instrument name selects, or null when the name
    /// is not one of the 128. Exact and case-sensitive, as LilyPond's <c>assoc-get</c> is.</summary>
    public static int? ProgramOf(string name)
        => ProgramByName.TryGetValue(name, out int program) ? program : null;

    /// <summary>
    /// The editor preview synth's timbre family for a program (0 piano, 1 flute, 2 reed,
    /// 3 strings, 4 guitar, 5 bass, 6 brass, 7 organ, 8 voice; 9 is the drum patch).
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN: the preview synth is Lily#'s own nine-waveform approximation, so this is a
    /// grouping of GM's sixteen families onto those nine, not a port. It replaces a substring
    /// match on the preset name that read <c>double-bass</c> and <c>piano-bass</c> as a bass
    /// guitar.
    /// </remarks>
    public static int PreviewTimbreFamily(int program) => program switch
    {
        >= 16 and <= 23 => 7,            // organ, accordion, harmonica
        >= 24 and <= 31 => 4,            // guitar
        >= 32 and <= 39 => 5,            // bass
        >= 40 and <= 46 => 3,            // strings, harp
        >= 48 and <= 51 => 3,            // string ensembles
        >= 52 and <= 54 => 8,            // choir, voice
        >= 56 and <= 63 => 6,            // brass
        >= 64 and <= 71 => 2,            // reed
        >= 72 and <= 79 => 1,            // pipe
        104 or 105 or 106 or 107 => 4,   // sitar, banjo, shamisen, koto
        109 or 111 => 2,                 // bagpipe, shanai
        110 => 3,                        // fiddle
        _ => 0,
    };
}
