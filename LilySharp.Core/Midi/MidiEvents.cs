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

namespace LilySharp.Core.Midi;

/// <summary>The sustain pedal going down or up at a tick (`@sustain`, `@!sustain`; docs/pedal-playback-design.md).</summary>
public readonly record struct PedalChange(int Tick, bool Down);

/// <summary>
/// Represents a MIDI note event.
/// </summary>
public readonly record struct MidiNote(
    int Channel,
    int Pitch,
    int Velocity,
    int StartTick,
    int DurationTicks,
    // Source offset of the note's syntax — lets the preview player
    // highlight the notation being played. -1 = no source link. Not written
    // to .mid files.
    int SourcePos = -1,
    // Quarter-tone offset (+1 = +50 cents): pitch bend in the .mid, a
    // fractional pitch in the preview player. 0 for normal notes.
    int QuarterBend = 0,
    // Which PRINTED copy of the source this onset corresponds to
    // (phrase expansions share one position; repeats replay their pass-one
    // ordinals). Not written to .mid files.
    int SourceOrdinal = 0,
    // Timbre family for the preview synth (0 piano, 1 flute,
    // 2 clarinet, 3 strings, 4 guitar, 5 bass, 6 brass, 7 organ, 8 voice),
    // resolved from the part's `instrument` (or its name). Not written to
    // .mid files.
    int Timbre = 0,
    // The part whose music made the note, or null for music no part claims. The exporter
    // gives each part its own track and channel by it (MidiExporter.SplitIntoPartTracks).
    // Not written to .mid files.
    string? Part = null,
    // A grace note: it sounds, but the checks of a voice's line do not read it as one of the
    // line's notes (CompositionChecks, VoiceLeading). Not written to .mid files.
    bool IsGrace = false,
    // A chord member's OWN place — its pitch, degree or drum, where the page puts its head —
    // where SourcePos is the chord's '<' (the onset the preview lights the chord by). -1 for
    // anything not a chord member. The Range panel goes to it. Not written to .mid files.
    int MemberPos = -1,
    // The voice the note is written in: 0 for a part's own line (or the first voice of a
    // `voice { } { }` span), 1 for the second voice, … — so a check reads each voice as a line of
    // its own (CompositionChecks: a chorus tune over its own chords, one guitar part, was told of
    // octave slips between the two; owner's review 2026-11-01). Not written to .mid files.
    int Voice = 0,
    // Which stroke of its chord this is, in a row that plays a strokes part: one symbol sounds at
    // each stroke from one place, and a repeat's pass counts them again, so the preview's loop
    // tells a later stroke from a repeat by it (playSpanWindow). Not written to .mid files.
    int Strike = 0,
    // `@breath` or `@caesura` was written on this note: a singer breathes after it (SungVoices).
    // Not written to .mid files, and an instrument plays on.
    bool BreathAfter = false,
    // `@breath` alone, of those two: with a note right after it the breath is heard. Not written to .mid files.
    bool Inhale = false,
    // A hairpin starts on this note, 1 `@cresc`, -1 `@decresc`/`@dim`: a singer swells (SungVoices).
    // Not written to .mid files: the MIDI plays each note at the last level written.
    int Swell = 0,
    // `@!cresc` or `@!decresc` was written on this note. Not written to .mid files.
    bool SwellEnds = false,
    // An `afterGrace` run's note: it ends the note before it, where a leading grace begins the
    // note after it. Not written to .mid files.
    bool IsAfterGrace = false
);

/// <summary>
/// Represents a tempo change event.
/// </summary>
public readonly record struct TempoChange(
    int Tick,
    int MicrosecondsPerBeat
);

/// <summary>
/// Represents a time signature change.
/// </summary>
public readonly record struct TimeSignatureChange(
    int Tick,
    int Numerator,
    int Denominator
);

/// <summary>
/// Represents a MIDI lyric event (Meta event 0x05).
/// </summary>
/// <param name="Part">The part the syllable is sung by (the page's placement, MidiExporter.SingLyrics),
/// or null for an event read from a file. Not written to .mid files: the singing voice
/// (<see cref="SungVoices"/>) groups the words by it, since the events all land on the first
/// part track. An empty <paramref name="Text"/> is a held note (<c>_</c>, a detached <c>~</c>): the
/// previous syllable's vowel goes on.</param>
/// <param name="Sung">The word a voice sings where it is not <paramref name="Text"/> — a syllable
/// with a reading, <c>漢[かん]</c> (<see cref="Music.LyricReading"/>): the file writes the kanji
/// (a karaoke display shows the words as printed; owner's decision 2026-11-08) and the voice
/// sings the kana. Null when the printed word is the sung one. Not written to .mid files.</param>
public readonly record struct LyricEvent(
    int Tick,
    string Text,
    string? Part = null,
    string? Sung = null,
    string? Language = null,    // the words' language tag (Music.LyricLanguage), null from a file; not written to .mid
    string? Singer = null       // who sings them, as written (Music.LyricSinger); not written to .mid
);