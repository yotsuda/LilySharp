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

/// <summary>A pedal going down or up at a tick (`@sustain`, `@!sustain`; docs/pedal-playback-design.md)
/// — the sustain pedal, or since 2026-11-13 (grammar evaluation 4: the other two were printed and
/// never played) the sostenuto (`@sostenuto`) or the soft pedal (`@unaCorda` … `@treCorde`), each
/// on its own MIDI controller.</summary>
/// <param name="Tick">Where.</param>
/// <param name="Down">Pressed (127) or lifted (0).</param>
/// <param name="Controller">The MIDI control change: <see cref="Sustain"/>, <see cref="Sostenuto"/>
/// or <see cref="Soft"/>.</param>
public readonly record struct PedalChange(int Tick, bool Down, int Controller = PedalChange.Sustain)
{
    /// <summary>CC 64, the damper pedal.</summary>
    public const int Sustain = 64;
    /// <summary>CC 66, the sostenuto pedal.</summary>
    public const int Sostenuto = 66;
    /// <summary>CC 67, the soft pedal (una corda).</summary>
    public const int Soft = 67;
}

/// <summary>A place in the text the editor lights while the music there lasts
/// (<see cref="MidiTrack.TextMarks"/>; owner's requests 2026-11-17) — a rest, or a section's name
/// in the form: when it lasts and where it stands in the text — its offset and printed copy, as a
/// note's (<see cref="MidiNote.SourcePos"/>, <see cref="MidiNote.SourceOrdinal"/>) — its part (null
/// for a form's name), and the bar it starts in (<see cref="MidiNote.PrintedBar"/>, -1 until told).</summary>
public readonly record struct TextMark(int StartTick, int DurationTicks, int SourcePos, int SourceOrdinal, string? Part, int PrintedBar = -1);

/// <summary>The pitch moves a singer makes on a note, as written (<see cref="MidiNote.Glide"/>):
/// one into its head and one out of its end, either or both. LILYSHARP-OWN: LilyPond sounds none.</summary>
public readonly record struct SungGlide(SungGlide.Into Head, SungGlide.OutOf Tail)
{
    /// <summary>Into the note's head: from below (<c>@scoop</c>) or from above (<c>@plop</c>).</summary>
    public enum Into { None, FromBelow, FromAbove }

    /// <summary>Out of the note's end: down (<c>@fall</c>), up (<c>@doit</c>), or on to the next sung
    /// note (<c>@glissando</c>, <c>@slide</c>).</summary>
    public enum OutOf { None, Down, Up, ToNext }

    public bool IsNone => Head == Into.None && Tail == OutOf.None;
}

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
    // A hairpin starts on this note, 1 `@cresc`, -1 `@decresc`/`@dim`: a singer swells through
    // its notes (SungVoices) and an instrument strikes each note at the level the hairpin has
    // reached (MidiExporter.SwellInstruments, which writes that into Velocity).
    int Swell = 0,
    // `@!cresc` or `@!decresc` was written on this note. Not written to .mid files.
    bool SwellEnds = false,
    // `@vibrato` was written on this note: how far a singer's pitch swings each way in cents, and
    // how many swings a second (null: the engine's own). A trial, sung by DiffSinger voices
    // (SungVoices; AnnotationValues.Vibrato). Not written to .mid files, and an instrument plays plain.
    double? Vibrato = null,
    double? VibratoRate = null,
    // `@sing(…)` was written on this note: how the voice sings from here on, as
    // AnnotationValues.SingText writes it (SungVoices). Not written to .mid files.
    string? Sing = null,
    // What a singer's pitch does on the note as written (`@scoop` `@plop` at its head, `@fall`
    // `@doit` `@glissando` `@slide` at its end; Cantoria's request 2). A trial, sung by DiffSinger
    // voices (SungVoices.Bend). Not written to .mid files, and an instrument plays plain.
    SungGlide Glide = default,
    // An `afterGrace` run's note: it ends the note before it, where a leading grace begins the
    // note after it. Not written to .mid files.
    bool IsAfterGrace = false,
    // No rest stands between this note and the onset its part played before it
    // (MidiExporter.CloseOnset): where that note was cut short — a staccato — the silence
    // between the two is the articulation's and not a rest, and a singer holding a syllable
    // goes on into this note (SungVoices). Not written to .mid files.
    bool FollowsNote = false,
    // The level written for this note, where a hairpin moved Velocity away from it
    // (MidiExporter.SwellInstruments); 0 where Velocity is that level. A singer reads this one
    // and swells by its own clock (SungVoices.Written). Not written to .mid files.
    int WrittenVelocity = 0,
    // What the note's own marks add to the level it is written in — an accent, a marcato, an
    // `@sfz`, the attack of an `@fp`: Velocity less the level standing after the note's own
    // dynamic. A hairpin reads the level without it (SungVoices.Level), so an accent inside a
    // crescendo neither ends the hairpin nor is its target. Not written to .mid files.
    int Accent = 0,
    // The number of the accent dynamic this note is struck with (`@sfz` 104, `@rf` 103, …), 0
    // with none. Such a dynamic is not so much over the level: it is its own number, or a
    // little over the level where that is louder (MidiExporter.AccentAt) — so where the level
    // moves under the note (a hairpin, a shared dynamic) the strike is read again at the level
    // it has come to (SungVoices.StruckAt). Not written to .mid files.
    int Sforzando = 0,
    // A tie `~` is written after this note (after the last note a tie merged into it) and the
    // onset PRINTED next did not take it: where the music jumps from the note's end, the note
    // PLAYED next may (PlayedTimeline.Unfold). Not written to .mid files.
    bool TiedOut = false,
    // What the note sounds past the time its value takes in the music — a fermata's half again
    // (MidiExporter.ApplyArticulationType) — 0 for most. The note's WRITTEN end is its end less
    // this: a repeat played again starts with no note that only rings into it (PlayedTimeline).
    // Not written to .mid files.
    int Overhang = 0,
    // Written inside `pass 2 { … }`: the passes the note sounds on (empty: every pass). The
    // exporter drops it from the passes it is not of once the music is played
    // (MidiExporter.KeepPassNotes). Not written to .mid files.
    Semantics.PassSet OnlyPasses = default,
    // The source position of that `pass` (its PassStartSyntax), -1 outside one: what tells a
    // region none of whose notes is ever played (MidiExporter.SilentPassRegions).
    int PassRegionAt = -1,
    // The instrument the part changed to in the music before this note (`instrument piccolo`,
    // its preset lower-cased), or null for the part's own: the program it sounds with
    // (MidiTrack.ProgramOf) and the range it is checked against. Rides the note through the
    // repeats as played. Not written to .mid files (the program change is).
    string? Instrument = null,
    // The `%` this note sounds for — a percent repeat plays its bars again for each mark, from the
    // bars' own notes, which the page prints once; the mark is what the page prints there. Null
    // for any other note. Not written to .mid files.
    PercentCopy? Percent = null,
    // The bar the note is PRINTED in, counted from 0 (a pickup is bar 0) as the page counts its
    // measures: the walk goes as printed before the piece is laid out as it plays, and the number
    // rides with the note through that (PlayedTimeline). The preview's A-B repeat picks its bars
    // by it (docs/ab-repeat-design.md). -1 where unknown. Not written to .mid files.
    int PrintedBar = -1,
    // A level (`@p`, `@ff`, the piano of an `@fp`) was written at this note's moment in its part
    // — or in a hand of its grand staff: a hairpin lands on it or stops at it though it writes
    // the level already standing (SungVoices.Swells; MidiExporter.SwellInstruments stamps it).
    // Not written to .mid files.
    bool LevelWritten = false,
    // The section play the note sounds in, counted in the order the form plays them (-1 in a
    // book without sections): a hairpin's reach stays in its section (SungVoices.Swells;
    // MidiExporter.SwellInstruments stamps it). Not written to .mid files.
    int SectionPlay = -1
);

/// <summary>
/// The `%` a note sounds for (<see cref="MidiNote.Percent"/>): the mark's source offset and printed
/// copy, and where the bars it repeats begin and how long they last — ticks from the note's own
/// start back to their start (<paramref name="Lead"/>) and their length — so the bars move with
/// the note when the piece is laid out as it plays (PlayedTimeline). The preview lights the mark
/// for the length of the bars it sounds (owner's request 2026-11-16: the repeated bar's notes lit
/// instead, which read as the music jumping back).
/// </summary>
public readonly record struct PercentCopy(int Pos, int Ordinal, int Lead, int Length);

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
/// with a reading, <c>漢[かん]</c> (<see cref="Music.LyricReading"/>): the voice sings the kana,
/// and so does a written .mid — <c>MidiForFile.Shape</c> writes a word as sung (owner's decision
/// 2026-11-12, lyrics-singer-design.md §16; the kanji until then, 2026-11-08). Null when the
/// printed word is the sung one.</param>
public readonly record struct LyricEvent(
    int Tick,
    string Text,
    string? Part = null,
    string? Sung = null,
    string? Language = null,    // the words' language tag (Music.LyricLanguage), null from a file; not written to .mid
    string? Singer = null,      // who sings them, as written (Music.LyricSinger); not written to .mid
    string? Gender = null,      // the gender of the default voice for them (Music.LyricSinger); not written to .mid
    bool Hyphen = false,        // the next syllable is of the same word; not written to .mid
    bool Voiced = true,         // a voice was asked for them (Music.LyricSinger.SungReaderOf); not written to .mid
    string? Track = null,       // the lyrics track they are written in, as named there; not written to .mid
    string? Sing = null,        // how each voice sings them, its line's `with …` (Music.LyricSinger.SettingsReaderOf); not written to .mid
    int SourcePos = -1,         // where the syllable stands in the text, for the editor to light it while it is sung (owner's request 2026-11-17); -1 from a file; not written to .mid
    string? Phonemes = null     // the voice's symbols it is sung by, space-separated — 병[/pp.y.eo.NG/] (Music.LyricReading.Phonemes); not written to .mid
);