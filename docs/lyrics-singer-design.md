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
singer [S] on PART [sings NAME] [with NAME VALUE …]    // at the top level, or inside a score (section 15;
                                                       // until 2026-11-12 `sings [NAME] [on PART]`, still read)

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
- Two `singer` lines giving one part different words IN ONE SECTION, in one scope (LYS4044): the top level,
  or one score. Other words in other sections stand together (owner's decision 2026-11-12: a verse in
  Japanese and a chorus in English is one song — `in Verse` on one line, `in Chorus` on the other; a line
  with no `in` sings every section). `SungVoices.Read` makes the voices of a part track by track: the
  words, their language and who sings them are the track's. To
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
  it sings the ONE in the singer's language — the line's own (`singer alice female japanese sings on
  lead`: a bare word of the head, said as the gender is, in either order with it — it was `language
  japanese` for an hour, and the owner asked for it as short as the gender; it narrows the voices the
  editor offers too, as the gender does), else the file's (owner's decision 2026-11-12:
  "alice sings lead, and lead names its English words; the line need not say en" — and not the first
  listed: the language that fits). None or several in that language is an error (LYS4054), which says
  the language it looked for. Where the part names none, it is the unnamed track, as today.
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

Where nobody sings (owner's decision 2026-11-12, the same day): a part with a singer line is
a voice and its instrument is off — but in a section as it is played where NONE of its voices
has a note, its instrument plays. `SungVoices.Unsung` finds those stretches by the sections'
ticks; the playback list marks each such note (`PlaybackNote.U`) and the three places of the
editor that keep a sung part's instrument out let them through (the preview's Play, the WAV /
MP3 / MP4 export, a proposal's Play), as does `lysc wav --singer`. A section with notes and NO
word at all is such a section even right after a sung one (owner's report the same day: a
two-bar Coda after the last chorus was sung "u— u—", the last syllable's vowel held on by the
rule that the notes after a phrase's last word are its melisma): `SungVoices.Read` carries a
vowel only as far as its section, or into one that has words of its own. By SECTION, not by note: a
bar of a sung section with notes and no words is still the voice's silence (section 8).

Printed since the same day: section 13. Not built: `in Chorus 2` for one playing of a
section; a part of its own transposition.

## 13. A singer has a name, and a score prints what the singer sings (owner's decisions 2026-11-12; built the same day)

```
singer alice female sings on lead in Verse
singer bob   male   sings on lead octave -1 in Chorus
singer carol female sings on lead up 3 in Chorus

score { staff lead  lyrics words  staff carol  lyrics carol }   // the lead, and carol's harmony under it
score carol { staff carol  lyrics carol }                       // carol's own sheet
```

The owner's two points: the notes of a harmony are the SINGER LINE's to know — nothing
should write them a second time — and a sheet for that singer is the use that matters. So
the singer gets a name, and the score names the singer.

- `singer NAME [male|female] ["engine/voice"] sings …`: a word before `sings` that is no
  gender is the singer's own name; of two words the first is the name and the second the
  gender. Lines of one name are ONE singer: the voice is said on one of them and is the
  others' too (`singer alice sings on lead up 3 in Chorus` on a second line). A line with no
  name sings as before and is not printed.
- `staff NAME` in a score (and in a staff group), where NAME is a singer and no part, is the
  staff of what that singer sings: the notes of the singer's lines, moved as they say, in the
  sections they say; whole bars of rest where the singer does not sing. It is a staff item
  like any other (a name in quotes, `as …`, `hidden`, `sounds …`). The staff is named as the
  singer, capitalised.
- `lyrics NAME`, where NAME is a singer and no lyrics track, is that singer's WORDS: the
  words of the sections she sings. Under her staff it is that staff's verse; anywhere else it
  is a row of its own, at her rhythm (her words under the lead's staff).
- A singer whose lines sing words made from the notes (`sings solfege`) has those for her
  words: `lyrics carol` is then the note names under her staff (`LyricSinger.WordsOfSingerAt`).
- A score prints what it says, of a singer as of a part: `staff carol` alone is her notes
  with no words (a harmony under a lead that carries the same words), `staff carol  lyrics
  carol` both, `lyrics carol` her words alone.
- A PART called as a singer is, is the part: `staff carol` then engraves it and nothing is
  made. That is what her notes written out are (section 14), and the one case in which a
  singer may have a part's name: the line sings that part as it stands. Any other singer
  called as a part is, is an error on her line (LYS4061) — her harmony could not be printed,
  and nothing would say why.
- A singer who sings the part an octave down in every line is written in the treble clef an
  octave down (a tenor's), unless the part writes a clef: the notes stand where the part's do.
- A score that prints a singer SINGS WHAT IT SHOWS: the singer's lines are read there on the
  made part, as they stand, so the page and the voice cannot differ. A score that does not
  print the singer sings the line as section 12 says. `score { staff carol  lyrics carol }` therefore
  plays carol alone.

**No word of its own** (owner's decisions the same day). The item was `singer NAME` for
some hours — one word then meant the line that says who sings and the item that prints her,
and a score holding both read badly — and `lyricsStaff NAME` for one more, a staff that
always came with its words. The owner asked for the staff without the words, and the words
without the staff; the answer needed no keyword: a singer's name stands where a part's does
after `staff`, and where a track's does after `lyrics`.

**A score reads its own singer lines** (owner's report the same day: a Japanese score with
its own lines, `staff alice  lyrics alice` added, printed alice with the English words). A singer's
staff is made of the lines THE SCORE reads — its own, and the top level's for the parts it
writes no line for, the rule a score sings by (`LyricSinger.LinesOfSingerIn`). Where those
are not the top level's, the part made is the score's own (`alice__2`, the second score;
`alice__0` is the one made of the top level's lines: `LyricSinger.PartOfSinger`, read off
the syntax alone since the resolved lines read the scores), so one file holds alice's English
staff and her Japanese one. ⚠️ A made part is never called by the singer's bare name: a part
of that name is her notes written out, and the text that makes the part would turn the item
that asked for it into a staff of a part.

**A score's named line replaces that singer alone** (owner's decision 2026-11-12, after the
evaluation scores): a score's own singer lines took the place of EVERY top-level line for
their part, so changing one singer in a score meant writing the others again. A named line
replaces the file's lines of that name; a line with no name replaces the file's lines for its
part, as before (`LyricSinger.Replaces`).

**How it is made** (`Editing.SingerParts`): the part is WRITTEN, as Lily# text, after the
file's own — the way `using` adds a file, the document staying the prefix so that every
position in it holds — and every reader of a score (the page, the MIDI, the MusicXML, the
LilyPond twin) reads it as any other part. The command line's `LoadAndParse` and the server's
`ExpandUsings` are the two houses that call it; a file whose scores print no singer pays one
look at its scores. What is written:

```
// (written by Lily#, not the file's own text: …)
part carol__0 "Carol" { instrument alto lyrics __words_carol__0
  section Verse { R*4 }
  section Chorus { b'4 b c b | g4 f e2 | }
}
lyrics __words_carol__0 {
  section Chorus { Sing it o- ver, | o- ver- now, | }
}
```

The header is the sung part's (its instrument, its clef, its octave), the part's own words
left out; the music of a section is the COPY of the part's there, its pitch names moved
along the key each note is written in (`MeasureCollector.WrittenKeyObserver` says which,
the part's transposition taken out) and its octave marks set so that each lands the interval
from the note it copies (`OctaveModeConverter.Pin`, the forcing `Repin` does; the parts sung
are played by a score added for the asking, since a singer's own sheet plays none of them).
The words are a track of the singer's own, the sung sections' alone: the part's track under a
staff that rests for a section would have words with no notes. Diagnostics that stand in the
written text are dropped (`SingerParts.OwnOf`; the server already drops what lies past the
document): a note below the alto's range there is the singer's octave, not a slip.

BY THE LETTER, where section 12's sound goes by the pitch: a note the key does not hold is
read as the letter it is written on and lands on the key's own note the interval away — over
g-sharp in a minor a third above is b, over c-sharp in G it is e. The two readings agree but
for a note spelled against its usual alteration (d-flat in C: the page lands on f, the
unprinted voice on e). LILYSHARP-OWN, a reading.

Not made, and said at the score's item (LYS4060): a singer the score's own singer lines leave
out (they take the place of the file's for their part); a line that sings one voice of a part;
a moved line whose music calls a phrase (the phrase's notes are shared, not the copy's to
move). A name no singer and no part has is an unknown part (LYS1007), as ever.

Limits kept: a click on a made note leads nowhere in the document (its position is past the
text); words written for the whole piece with no sections (`lyrics words { … }` at the top
with no `section`) are not cut by section and are not printed under a singer's staff; lines
of one name that say different voices are not refused; the harmony drawn as small notes on
the lead's own staff is later work.


## 14. A harmony written out as a part (owner's decisions 2026-11-12; built the same day)

A third along the key is not always the note, and the place to mend a note is a part. So
what a line says can be WRITTEN OUT — and, the owner's point, for an instrument as for a
voice: a second violin a third under the first is the same request.

- On a named singer's line the light bulb offers **"Write NAME's notes out as a part"**
  (`HarmonyPartWriter.WriteSinger`): a part called as the singer is, after the part sung — a
  block beside that part's in each section, or sections of its own in a file written by part,
  bars of rest where the singer does not sing; the words of the sung sections as a track of
  the singer's own (`carolWords`), named in the new part's header; the singer's lines made
  ONE line that sings the written part as it stands (`singer carol female "x/y" sings on carol
  with …`, the first line's voice and settings kept). A score that printed the singer keeps its
  `staff carol` where it stood — the name is the written part's now, and nothing is made — and
  its `lyrics carol` becomes `lyrics carolWords`; a score that showed only the sung part takes
  `staff carol  lyrics carolWords` after it.
- **"Lily#: Write a Harmony Part…"** (the command palette; `HarmonyPartWriter.WritePart`)
  asks which part (the caret's first), how far (a third, a fourth, a fifth, a sixth, a tenth,
  above or below along the key, or an octave), which sections, and the new part's name, and
  writes that part the same way, with a staff after the part it follows in every score that
  shows it. No words, no singer line: it is an instrument's.
- The notes are section 13's, note for note: the writer asks `SingerParts` for the part of
  that singer (for an instrument, of a line it writes for the asking, in a score it adds for
  the asking) and takes the made part's music section by section; `PartWriter` places it as
  it places any new part. An instrument an octave down keeps its clef (the treble clef an
  octave down is a voice's).
- The server: `lilysharp/harmonyPartChoices` (the parts, the caret's, the sections, the names
  taken) and `lilysharp/harmonyPart` (the rewritten text, or why nothing was written); the
  code action carries the command `lilysharp.writeSingerPart`, worked out when picked.

Not written: a singer who sings one voice of a part, or whose moved music calls a phrase
(section 13's refusals, said in the answer); lines of one singer with different settings keep
the first's.


## 15. The part before the words (owner's decision 2026-11-12; built the same day)

```
singer alice female japanese "diffsinger/Nishiren" on lead in Verse
singer carol female on lead sings en up 3 in Chorus
```

Twice in one day the owner wrote a singer line with no `sings` — `singer alice female on lead
in Verse` — and twice it was an error. The line was right and the grammar was not: since the
part names its words (section 10) and a line picks among several by its language (section 10),
`sings` usually had nothing to say. And the owner's reason for the order: once the part is
said, the words IT names can be listed.

- `singer [S] on PART [sings NAME] [up N | down N] [octave N] [in SECTION …] [with …]`: whose
  notes, then — only where they have to be named — which words. A part that names its words
  needs no `sings`.
- The older order, `sings [NAME] [on PART]`, is read as it was: every file written before
  this holds. The two are one line to everything that reads it
  (`SingerDeclarationSyntax.ClauseToken`: the head ends at the first `on` or `sings`). A line
  with neither is LYS4041.
- The editor: after the voice the list holds `on` first; after `on` the parts; after the part
  `sings` beside `up` / `down` / `octave` / `in`; after `on PART sings` the words THAT PART
  names (and, where it names none, the file's), with their language, then `hum` and `solfege`.
  A voice taken from the list goes on to ` on `. The grammar colours the part-first line; the
  extension's readers know a singer line by its `on` as by its `sings`.
- "Write NAME's notes out as a part" leaves `singer carol female on carol`.
- The docs and the site write the part first; the tests keep both orders.

