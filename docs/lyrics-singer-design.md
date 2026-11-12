# Lyrics and singers: the grammar

The owner's decisions of 2026-11-10 (session 1217), reached over one long discussion, and built in sessions
1217 to 1222. It replaced `sings` / `singer` on lyrics and `singer` on a part; compatibility was not a cost.
The reference is `GRAMMAR.md`; this page says why. The notes in `singing-design.md` and
`singing-roadmap.md` that write `lyrics NAME sings PART` are from before it.

## 1. The idea

Three statements, one job each:

- `lyrics` **prints** words. It never changes the sound.
- `singer … sings` says **who sings what**. It is the only thing that makes a voice sound.
- `staff … sounds` says **how a part sounds in this score**.

A track's name only names it. `on` says which part's notes the words follow.

## 2. The grammar

```
lyrics [NAME] [language L] { … }                       // definition: the words only
part PART { lyrics NAME }                              // the words this part sings (section 10)
singer [S] sings [NAME] [on PART] [with NAME VALUE …]  // at the top level, or inside a score

score {
  staff PART [hidden] [sounds voice|instrument|both]   // tab takes the same clauses
  lyrics [NAME] [on PART]                              // prints the row here
}
```

The order of the clauses is fixed. `S` is `female`, `male`, one `"engine/name"`, or a gender before a name
(`male "neutrino/SOMA"`). Each line is one voice: several lines with the same words on one part sing it
together. With no `S`, the gender comes from the part's
instrument, else its clef. Role words (`"soprano"`) go.

```
part sop { instrument soprano }
lyrics words { あ い う え | }
singer female sings words
score { staff sop  lyrics words }
```

## 3. What is refused

- `lyrics NAME` where NAME is a part or a named voice (LYS7008, built).
- `on`, `hum` and `solfege` as a track's name.
- `lyrics` inside a `part { }` block or inside a part's `{ }` in a section. At the top level or directly
  in a section it may be written. (`part { }` takes a `lyrics NAME` with no body, section 10.)
- Two `singer` lines giving one part different words in one scope (LYS4044): the top level, or one score. To
  have another score sing other words on a part, its line goes inside that score. The same voice on the same
  words of a part twice is warned about (LYS4053).
- `on NAME` where NAME is a voice name two parts share.
- `sounds voice` or `both` on a staff whose part no `singer` line sings.
- `sounds` on a condensed or combined staff.
- The old spellings (`lyrics x sings p`, `singer` on a track, a row or a part) are plain syntax errors, or unknown properties. No error is written for them (owner's decision 2026-11-10: nothing is built for backward compatibility).

## 4. When `on` is left out

| Statement | Resolved by, in order |
|---|---|
| `lyrics NAME` in a score | the nearest staff above it, passing over lyrics and chords rows, `hidden` staves counted; with no staff above, the even-spread row; directly under a condensed or combined staff, an error |
| `singer … sings NAME` | the file's only part; the one part every score prints these words under; otherwise an error |

An unnamed `lyrics { }` is sung by `singer S sings` with no name. A definition takes no `on`.

## 5. What sounds

Only a part with a `staff` or `tab` line in the score sounds; `.mid` follows. `staff PART hidden` draws
nothing and still sounds: it is how a voice sings a melody the page does not show, and the row under it
takes that melody's rhythm.

| Staff | Part has a `singer` line | No `singer` line |
|---|---|---|
| no `sounds` | voice only | instrument |
| `sounds voice` | voice only | error |
| `sounds instrument` | instrument | instrument |
| `sounds both` | voice and instrument | error |

Two staves of one part add up, each sound once. This maps onto today's `Sung` and `Played`
(`SungStaves.Sounds`). While no voice can be had (no engine, not ready, words absent from the form) the
instrument stands in. The stand-in is General MIDI's choir only for a voice-only part naming no sound;
`sounds instrument` and `both` play the part's instrument, else the piano.

Printing changes nothing here: a row of words on a part with no `singer` line leaves it an instrument.

## 6. Words the file does not write: `hum` and `solfege`

Reserved track names, generated from the notes, needing no definition:

```
singer female sings hum on sop        // hums, on ん
singer female sings solfege on alt    // sings the note names
score { staff alt  lyrics solfege }   // prints them under the notes
```

`solfege` is movable do: the key's tonic is do, a minor key's is la, and a `key` change moves it. An
altered note takes the name of its letter and sounds as written. The file's `language` picks the words:
ドレミファソラシ in Japanese, do re mi fa sol la ti in English, 哆来咪发唆拉西 in Chinese (simplified
characters; owner's decision 2026-11-10), 도레미파솔라시 in Korean. One name a note: a tied note keeps its
syllable, a chord takes its top note, a rest takes none, grace notes take none. `lyrics hum` is an error
(there is nothing to print). `.ly` and MusicXML write the names as lyrics. How ん sounds held long has to be
heard on each engine before it is settled; ら is the fallback.

## 7. The editor

- The practice menu lists one line per `singer` line of a part in this score, named by the voice that
  sings (`MERROW`; `MERROW (sop)` when one voice sings several parts), each with its own switch, fader and
  microphone. A part's instrument stands in only when none of its voices sounds. The hover says what
  it sings and how far along it is. Words with no `singer` line have no line.
- The microphone stays on each line (session 1217).
- Completing the name after `singer`: the words are not written yet, so the file's `language` narrows the
  voices, and every voice is offered where tracks differ. Once `sings NAME` is there, a voice that cannot
  sing its language is flagged.
- "These words are printed, not sung" is said once, only where the file has no `singer` line at all.
- Rename, colours, hover and go-to-definition learn `on` and `singer … sings`.

## 8. Limits kept

A `singer` line at the top level is every score's default; a line inside a score replaces the defaults of its
part in that score (owner's decision 2026-11-10). No change of singer within a part; two voices of one part
cannot have two singers; a sung part's instrument stays off in bars with no words.

## 9. Order of work

1. The grammar and the migration: `on`, top-level `singer`, `staff hidden` / `sounds`, sections 3 to 5, the
   the reserved words. Repository books, tests, docs and the composing guide.
   The owner's books are rewritten by machine, adding `singer sings …` where a voice instrument sang and
   `staff PART hidden` where words sang a part no staff showed. `LysWriter` (MusicXML import) writes `on`.
   Calamo generated `lyrics … sings` (`src/Omr/Program.cs`, `src/OmrGen/Program.cs`): told in its
   `docs/repro/REQUESTS.md` (2026-11-10, sessions 1217 and 1222).
2. Singing words no row prints (syllables come from a page of the score read and never drawn:
   `LyricSinger.WithSungWords` puts a line's words on its part's staff, on a staff it adds for a part with
   none of its own, and moves a sung track's rows there too; sessions 1221, 1222), and the practice menu of
   section 7.
3. Done (session 1218). Inside a group a row with `on` may stand anywhere, on its own band; a track on one
   part placed twice in a group is warned about (LYS6015). Measured against LilyPond 2.26.0 under `choirStaff`,
   `staffGroup` and `grandStaff`: staff distance, row height and syllable positions agree.
4. Done (session 1219), but for the ear: `hum` and `solfege` are built as section 6 says, in
   `Music/GeneratedLyrics`. Under written words the names stand on the next row. `ん` held long has not
   been heard on an engine; ら is the fallback. A hum is Japanese whatever the file's language.
5. Done (session 1222): a part's own words, section 10.

## 10. A part's own words (owner's decision 2026-11-10, session 1221; built in session 1222)

Words usually belong to a part, and the file had nowhere to say so. A score's row says where words are
printed and a `singer` line says who sings them, so a part printing one track and singing another went
unnoticed (a tenor printing `mw` with `singer … sings words on ten`). The relation gets a statement
of its own, on the part:

```
part sop { instrument soprano  lyrics words }
part alt { instrument alto     lyrics words }
part ten { instrument baritone lyrics mw }

singer female sings on sop                     // sings the words the part names
score { staff sop  lyrics words  staff alt  lyrics words  staff ten  lyrics mw }
```

- `lyrics NAME` in a `part { }` has no braces of its own. It names a track and defines nothing, so
  section 3's refusal stays for a `lyrics` with a body. A part may name several tracks, one statement each.
- A `singer … sings on PART` with no name sings the track the part names. Where the part names several,
  leaving the name out is an error (LYS4054). Where it names none, it is the unnamed track, as today.
- Naming a track a part does not list, on a `singer` line or on a row for that part, is a warning
  (LYS4055), and only for a part that lists any. `hum`, `solfege` and an unnamed row count as tracks
  here. Another score singing other words is written by listing both tracks.
- A `lyrics NAME` row with no `on` and no staff above it follows the part's rhythm when exactly one part
  names the track; otherwise it stays the even-spread row. A row under a staff keeps section 4's rule.
- `lyrics` rows stay: a score prints only what it says (section 1). A file with no such statement behaves
  as it does now.

**The standard** (owner's decision 2026-11-12): the part's statement is the way a file is
taught to say whose words they are — in both writings, by section and by part (a part that
holds its own sections names its words with its header's properties, before the first
section). The relation can still be said by a score's row under a staff, or by a singer
line's `on`, and a file that writes only those is read as before; nothing nags it. The editor
offers "Name these words on the part" on a singer line and on a lyrics block's head
(`Editing.PartWordsNamer`), and the composing guide writes the part's statement first. Why
the question came up: looking at a part, the owner could not tell which words were its — the
score had stacked the row under its staff, and that was the only place that said so.

Rejected: `lyrics words on sop { … }` on the definition, which a file defining its words section by section
would repeat on each block and could contradict, and which section 4 refuses; and binding by name
(`lyrics sop { … }`), which cannot share words between parts and is LYS7008.

## 11. How a voice sings (owner's decisions 2026-11-12; `with` built the same day)

Three layers, so that nothing a voice can do is lost and nothing has to be written:

1. **Nothing written**: the voice sings as its engine makes it. The default, and usually enough.
2. **Music's own words** on notes (`@vibrato`, a trial so far): one meaning whatever the engine;
   Cantoria makes each engine do it, and an engine that cannot is told to the writer.
3. **The engine's own settings**, by name and value, passed through:
   - for the whole voice, on its singer line — `singer "…" sings words on lead with breathiness 3 gender -0.2`
     (built: `SingerDeclarationSyntax.Settings`, `LyricSinger.SettingsReaderOf`, `SungVoice.Sing`,
     Cantoria's score `sing` and `parameters(voice)`);
   - inside the music, on notes — `c4@sing(breathiness 6)` holds until the next one, as a dynamic
     does; `@sing(breathiness default)` gives it back to the singer line's value; `g2@sing(breathiness to 9)`
     arrives there gliding from the value before (built: `AnnotationValues.Sing`, a note's `Sing`
     text, the sung request's `Sg` / `St`, Cantoria's note `sing` / `singTo` and `SingCurves`).

Decided: the gate on notes is `@sing(…)`; a setting is `NAME VALUE`, settings follow one another
with no comma (as a part's own settings do — `@vibrato(40, 6)`'s nameless arguments keep theirs);
a name may be dotted (`mix.Sweet 0.3`, `diffsinger.velocity 1.2` to name an engine's own); a value
is a number, a word (`support KIRITAN`, `default`) or a string. The settings end with the line.

Names and units are the ENGINE's where a setting is that engine's alone. Where two engines mean
one thing by different names or scales, Cantoria owns one name and one scale and translates
(none yet: only DiffSinger has such settings today). Each setting has a scope the voice reports —
`curve` (changes at any note, can glide), `phrase` (changes where a phrase ends: NEUTRINO's
transpose `-f`), `voice` (once a score: NEUTRINO's support model `-S`, whose value is a NAME) —
and the editor's list shows it. Measured 2026-11-12: NEUTRINO v3.2.6 has those two and its
intermediate files (timing, f0, melspec) and nothing like breathiness; VOICEVOX has frame pitch
and volume only. A DiffSinger voice's breathiness, tension, energy and voicing are OFFSETS on
what its variance model predicts — the voice's own expression stays under the setting.

Built after that, the same day: NEUTRINO's two (`transpose`, scope phrase; `support`, scope voice,
a NAME — `-S` wants the model's folder, measured), listed with the names a `support` may be;
the step keys on a setting's number, on the singer line and on a note (`NoteStepper`'s
`SingValue`: one of the number's last written place, ten on the octave pair). Open: which way a
DiffSinger voice's `gender` goes (measured brighter at +0.6, undecided at −0.6: by ear); a glide
for a phrase-scope setting; one name and one scale across engines, when a second engine has a
setting the first one has.

And then: a phrase-scope setting may be written on a note — it steps, each phrase sung with the
value at its first note (Cantoria's `SingCurves.PhraseValues`), and cannot glide. What a voice
does not take is marked where it is written: the server lists what the document writes with its
spans (`lilysharp/singSettings`), the extension asks the engine what each voice takes and joins
the two (`singProblems`, the `Lily# singer` diagnostics) — the server does not know the engines,
and the engine does not know the document. A note is checked against every voice of the file:
the editor does not follow a note to its singer.

NEUTRINO's `transpose` MOVES THE NOTES (measured 2026-11-12 on v3.2.6, an A4 held two seconds:
440.4 Hz with none, 527.5 with 3, 369.2 with -3; its own help says `-f i : transpose (key)`).
The first description — "the colour of the voice moves, the notes stay" — was written without
measuring and was wrong. So the word means on a singer line what it means in Lily#, for that
voice alone. A setting may be named by a keyword (the parser, the same day): `with transpose 3`
ended the line at `with` and transposed the FILE until then.

A singer line's settings are HEARD (owner's request 2026-11-12): the phrase a voice is heard by
is sung with what the line writes after `with` — when the caret comes to the line (its name or
its settings) and at each step of a value. The extension reads the line itself (`singerLineOf`),
keeps such a sample by the voice and the settings (`voiceSampleKey`'s `!name=value;…`, in a
`tried` folder that holds the newest 40), and a value asked while another of the same voice
waits takes its place, so a run of steps sings its last. A note's `@sing` is not heard this way:
the phrase has no place for it, and the note's own bar would have to be sung.

## 12. What of its part a voice sings (owner's decisions 2026-11-12; built the same day)

```
singer [S] sings [NAME] [on PART] [up N | down N] [octave N] [in SECTION …] [with NAME VALUE …]
```

A band says: this section A sings, this one B, and here C comes in a third above. So does a
singer line. `in Verse Bridge` names the sections the voice sings in, every time they are
played (a number of the time is left for when it is asked). `up 3` / `down 6` is an interval
ALONG THE KEY; `octave -1` an octave. Several lines on one part sing together (section 2), each
its own sections and its own interval:

```
singer "diffsinger/Nishiren/2P" sings words on lead in Verse
singer "neutrino/MERROW" sings words on lead in Chorus
singer "diffsinger/Nishiren/Soft" sings words on lead up 3 in Chorus
```

How it came about: NEUTRINO's `-f` had been offered as `with transpose N`. Measured, it moves
every note by N semitones whatever the key (an A4: 440.4 Hz, 527.5 with 3) — a harmony nobody
writes, the owner said, where a third along the scale is asked for all the time, and for a
section, not a piece. Moving notes is Lily#'s work, not an engine's: Cantoria offers `-f` no
more, and every engine sings a line's interval alike.

- `Midi.SungVoices.Placed` makes each line's notes from its part's: those whose tick is in a
  named section (`MidiFile.SectionStarts`), stepped by `Step` along the major scale whose tonic
  sounds at that tick (`MidiFile.KeyStarts`, the file's `shift` and the score's `transpose` in;
  a minor key is its relative major's scale). A voice that leaves a section ends its phrase
  there; a note that only holds the syllable of the section before is not its to sing.
- The interval and the sections ride with the line's settings under names no engine has
  (`LyricSinger.PlaceInterval` …), so two lines that differ only in them are two voices, and are
  taken out before the settings go to a voice.
- A note that is not of the scale: its pitch alone is known here, not how it was spelled. It is
  read as the degree it most often alters — a raised first, fourth or fifth (a minor key's
  leading note), a lowered third or seventh — and the voice lands on the scale's own note the
  interval away: over g-sharp in a minor a third above is b. LILYSHARP-OWN, a reading.
- Refused (LYS4059): an interval under a second or over two octaves, an octave of 0 or past 3,
  a section the file has not. The same voice on the same words in other sections, or at
  another interval, is not a repeat (LYS4053).
- The editor: after the part the server lists `up`, `down`, `octave`, `in` in their order,
  each once, their numbers, and the sections not yet named; the step keys move the numbers.

Not built: the harmony is HEARD, not printed (a part made from another, to print, is the
other half); `in Chorus 2` for one playing of a section; a part of its own transposition.
