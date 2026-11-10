# Lyrics and singers: the grammar to build

The owner's decisions of 2026-11-10 (session 1217), reached over one long discussion. Nothing here is
built yet. It replaces `sings` / `singer` on lyrics and `singer` on a part; compatibility is not a cost.

## 1. The idea

Three statements, one job each:

- `lyrics` **prints** words. It never changes the sound.
- `singer … sings` says **who sings what**. It is the only thing that makes a voice sound.
- `staff … sounds` says **how a part sounds in this score**.

A track's name only names it. `on` says which part's notes the words follow.

## 2. The grammar

```
lyrics [NAME] [language L] { … }                       // definition: the words only
singer [S] sings [NAME] [on PART]                      // at the top level, or inside a score

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
   **Calamo generates `lyrics … sings` (`src/Omr/Program.cs`, `src/OmrGen/Program.cs`): tell it in
   `docs/repro/REQUESTS.md` before the old spelling becomes an error.**
2. Singing words no row prints (syllables come from a page of the score read and never drawn:
   `LyricSinger.WithSungWords` puts a line's words on its part's staff, on a staff it adds for a part the
   score does not draw; session 1221), and the practice menu of section 7.
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

Rejected: `lyrics words on sop { … }` on the definition, which a file defining its words section by section
would repeat on each block and could contradict, and which section 4 refuses; and binding by name
(`lyrics sop { … }`), which cannot share words between parts and is LYS7008.