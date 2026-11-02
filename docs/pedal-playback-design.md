# Playing the pedal — design, waiting for the owner

**Status: not built.** Written 2026-11-02 (session 1040) after the review of 雨音の午後. Decisions 1 to 3 in §3
are the owner's.

## 1. What happens now

`@sustain`, `@sostenuto` and `@unaCorda` are drawn, and exported to LilyPond and MusicXML. They do not sound.

- `LilySharp.Core/Midi` has no pedal: `MidiExporter` reads no pedal mark, `MidiTrack` has no controller list, and
  `MidiFile.WriteTrack`'s `MidiEventType` has no control change.
- Measured on `Ameoto-no-Gogo.lys`: the left hand's notes have the same lengths with the marks and without them,
  and the `.mid` holds no CC64.
- So the guide's sentence on the pedal (COMPOSE_FOR_LLM, "A piano piece") changes the page and not the sound. A
  quiet piece's broken chords stop where they are written, whatever the page says.

## 2. The roads

**A. Real pedal events (recommended).** The export records pedal changes, and every player receives CC64.

**B. Lengthen the notes at export.** One change in one place, and every player would ring at once. Rejected:

- `DurationTicks` is what the checks read: held clashes, "sung through", tune values, `LengthOf`. They would read
  the pedal as the written music.
- A DAW gets long notes it cannot edit as a pedal.
- A re-struck key under the pedal, and the release of the font, come out wrong.

## 3. The owner's decisions

1. **Road A or B.** Recommended: A.
2. **Which notes a pedal holds.** Recommended: all the staves of a `grandStaff`, as a piano's levels are shared
   (`ShareGrandStaffDynamics`, owner's decision 2026-10-14). Written in the left hand, as it nearly always is, a
   pedal for one MIDI channel would leave the right hand's tune dry. A bracket (`staffGroup`, `choirStaff`) is
   several instruments: a pedal stays in its own part.
3. **The last chord.** LYS4018 requires a release, and `@!sustain` goes on the note it lifts on, so the last chord
   is struck as the pedal lifts. Its notes still last as written. Recommended: keep to the page, the pedal up at
   that note's onset; nothing new in the grammar. The alternative is to hold it through a last note and lift at the
   music's end, which then differs from the drawn bracket.

## 4. Road A, step by step

1. **Core.** `MidiTrack` gets a list of `(Tick, Channel, Down)`. `MidiExporter` records it where a note carries the
   mark, in the played walk, so repeats and D.C. come with it; the tick is the note's onset. A re-press under the
   pedal (`g,4@sustain` with the pedal down) is up then down at one tick, in that order. `@sostenuto` (CC66) and
   `@unaCorda` (CC67) are the same list with another controller, and go in only if the owner wants them now.
2. **The `.mid`.** `MidiFile.WriteTrack` writes `0xB0`. Order at one tick: pedal up, note off, pedal down, note on,
   so a re-press catches the new chord and not the old one. MeltySynth reads the file, so `lysc wav` follows.
3. **The note list.** `lilysharp/playback` gains the pedal events beside `PlaybackNote`, in seconds with the channel.
4. **The preview.** `toEvents` (`playbackCore.ts`) schedules them; `previewSynth.ts` calls
   `synth.controllerChange(c, 64, value, { time })`, already used for all-sound-off.
5. **MIDI out.** `MidiOutEvents` already has `Control = 0xB0`.
6. **Seek and stop.** Play from the middle starts with the pedal state at the offset, and a pedal down over the
   offset is sent first. Stop and the loop's turn send CC64 0: all-sound-off does not lift a pedal, and the next play
   would ring from its first note.

## 5. How it is checked

- C#: the bytes of the `.mid`, the order at one tick, a repeat, a grand staff's two channels, and the note lengths
  unchanged with the marks and without them. The checks' sweep must then be byte for byte the same.
- `npm test`: `toEvents` with a seek inside a pedal, and CC64 0 at stop.
- `lysc wav`: the last seconds of a piece with the marks and without them differ in level (rings against stops).
- By ear, the owner's: the preview, then MIDI out. Machines cannot judge whether it sounds like a piano.

## 6. What follows

- **A check for a pedal held over a change of bass** (雨音の午後, bar 23: E♭ held into B♭, the `@sustain` lost in a
  revision). Its measure is easier once the events exist.
- **The guide's sentence** is true of the sound only after this.
