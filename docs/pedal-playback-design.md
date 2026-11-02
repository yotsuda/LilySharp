# Playing the pedal

**Status: built for `@sustain`** (session 1041, 2026-11-02), the owner's three decisions as §3 recommends. Written
in session 1040 after the review of 雨音の午後. `@sostenuto` and `@unaCorda` still only draw. What remains is the
owner's ear (`editors/vscode/MANUAL-CHECKS.md` C57).

## 1. What happened before

`@sustain`, `@sostenuto` and `@unaCorda` were drawn, and exported to LilyPond and MusicXML. They did not sound.

- Measured on `Ameoto-no-Gogo.lys`: the left hand's notes had the same lengths with the marks and without them,
  and the `.mid` held no CC64.

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

## 4. Road A, as built

1. **Core.** `MidiTrack.Pedals` is a list of `PedalChange(Tick, Down)`. `MidiExporter.ProcessNode` records a mark at
   the onset of the note, chord or rest that carries it, in the played walk, so repeats and D.C. come with it, and
   `AddPedals` gives it to the tracks after the split. A re-press under the pedal (`g,4@sustain` with the pedal
   down) is up then down at one tick. A pedal the form leaves down lifts at the music's end. In a grace note a mark
   is not read. `@sostenuto` (CC66) and `@unaCorda` (CC67) would be the same list with another controller.
2. **The `.mid`.** `MidiFile.WriteTrack` writes `0xB0`. Order at one tick: pedal up, note off, pedal down, note on,
   so a re-press catches the new chord and not the old one. MeltySynth reads the file, so `lysc wav` follows.
3. **The note list.** A pedal change travels IN `lilysharp/playback`'s `Notes`, as a metronome click does: `P` is
   `PlaybackNote.PedalPitch` (-1), `V` is 127 or 0, `S` is -1, `C` and `N` are its track's. So the practice tempo, a
   muted part, a stretch, a loop and a count-in move it with the notes and need no code of their own. Whatever reads
   the list as notes skips `P < 0`: `toEvents`, `endOf`, `MidiOutEvents.From` and `EndOf`, the plain tones.
4. **The preview.** `toEvents` (`playbackCore.ts`) makes `pedal` events; `previewSynth.ts` calls
   `synth.controllerChange(c, 64, value, { time })`.
5. **MIDI out.** `MidiOutEvents.From` sends the same events in the same order.
6. **Seek, stretch and stop.** `fromOffset` carries a pedal held across the offset down at the offset. A stretch
   (`playSpanWindow`) lifts a pedal it leaves down at its end, so a loop's next round does not ring on from the last.
   Stop sends CC64 0 in both players: all-sound-off does not lift a pedal.

## 5. How it was checked

- C# (`MidiPedalTests`): the bytes of the `.mid`, the order at one tick, a repeat, a rest, a phrase, a tuplet, a
  grand staff's two channels, the note lengths unchanged. MeltySynth rings under the pedal and stops at the lift.
- `npm test`: a seek, a stretch, a count-in, the practice tempo, the order at one instant. 25 poisons, all red.
- Over 1364 books: the `.mid` moved in the 33 that write a sustain and in no other; the checks byte for byte the same.
- NOT checked by a machine: the preview's sound. `spessasynth` is not run in the tests, and whether it sounds like a
  piano is the owner's ear (MANUAL-CHECKS C57).

## 6. What follows

- **A check for a pedal held over a change of bass** (雨音の午後, bar 23: E♭ held into B♭, the `@sustain` lost in a
  revision). Its measure is easier once the events exist.
- **`@sostenuto` and `@unaCorda`**, if the owner wants them.
