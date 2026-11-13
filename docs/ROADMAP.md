# Lily# roadmap

Features proposed and not yet built, with what was decided about each. A feature leaves this
file when it ships (its CHANGELOG entry says the rest) or when it is decided against (it moves
to *Decided against* with the reason). What is being worked on now, and the next step, is
`docs/HANDOFF.md` §1 — not here.

---

## Proposed

### Score review for people (proposed 2026-10-30, the owner's design)

The composing checks, given a second entrance for a person's own piece: the AI tool
`lilysharp_reviewScore` returns a structured report (facts, strengths, and findings, each with its
kind, why, fix, fine-when and ask), which the AI explains to the person — above all to a beginner.
A command and `lysc review` do the same without an AI. The review adds key checks: the key heard
against the key written, notes outside the key, spelling by direction, and chords outside the
scale. The design and the order of work are in `docs/review-tool-design.md`.

### Singing the lyrics (proposed 2026-11-08, the owner's request)

The preview sings a part's words instead of playing its preset: the server resolves the sung
part to a score in seconds (`lilysharp/sung`), a separate engine — Cantoria, the owner's
Apache-2.0 repository, driving the user's own NEUTRINO today — renders a voice, and the webview
plays it beside the sound font; exports mix it in. Nothing is linked and no voice is shipped:
`lilysharp.playback.singer` names the engine as `soundFont` names a font. The design, the
stages and the owner's open questions (kana for kanji lyrics, a choir, replace or double the
instrument) are in `docs/singing-design.md`. Stages 1–4 shipped the same day (sessions
1124–1131: NEUTRINO through the Cantoria extension, Japanese, Windows); what remains, and the
long term — a second engine (DiffSinger in-process), languages, expression, a choir, more
engines, a voice on every platform — is `docs/singing-roadmap.md` (2026-11-08, the owner's aim:
Lily# as the notation editor that integrates several singing engines).

### Page-turn check (proposed 2026-10-25)

Whether each page ends where the player can turn — on a rest, or a held note — and a mark where
it does not. It would be turned on as the voice-leading check is, by the check pragma:
`check pageTurns` (docs/voice-leading-design.md §7).

### A capo of one part's chord shapes (listed 2026-11-13, grammar evaluation 3)

A TAB has a capo of its own since 2026-11-13 (`tab g capo 2`: its frets are counted from there,
over the file's `capo N`), so two guitars, one capoed, are two tab items. What is still the
file's alone is the capo the chord SHAPES read — the diagrams, the pressed names, what a
`chord(D x32010)` item sounds: one for every part, and a tab whose own capo differs under a part
that writes shapes is warned (LYS4069). A part's own would be a part property (it changes a
sound, so it cannot be a score item's). Not designed: what "Capo N" at the head says then, and
which capo a chords row with no part reads.

### Notation the grammar evaluation asked for, not built (listed 2026-11-13, grammar evaluation 3)

The owner decided not to build these now. Every spelling here is a PROPOSAL of the writers who
asked — none is decided.

- **A section in another key, from the form**: `form { A  B up 2 }` — play a section again a
  step or a key away without writing it out (a musical-theatre number's last chorus).
- **Cue notes quoted from another part**: `cue from vn1 { … }` — the small notes taken from
  the part named, at the moment they sound, where today a cue's notes are typed again.
- **A vamp**: `repeat vamp { … }` — a bar or two repeated until a cue, with its "vamp" text and
  repeat signs, played a set number of times.
- **A spoken cue line**: a line of dialogue over the staff that tells the player where the
  music resumes, with no notes of its own.
- **A translation row of lyrics**: a second row of words under the sung one, in italics, that
  no voice sings and no syllable count binds.
- **Metric modulation marks**: ♩ = ♩. — the note value that keeps its speed across a change of
  meter, printed over the bar line and played.
- **A tuplet across a bar line**: a triplet whose notes straddle the bar, which today must
  close before it.
- **MusicXML import**: senza misura (`time none`), `fine` / `dc al fine`, and cue-size notes
  are written by the exporter and not read back.
- **MusicXML import of the new guitar marks**: palm mute, let ring and barre brackets, the
  wavy vibrato line, a bend's release, harmonics at a fret.
- **A LilyPond twin for `@bend` / `@release`**: the twin writes the note plain and says so;
  LilyPond's bend spanner runs between two written notes.
- **`@harmonic(N)` on a chord**: natural harmonics on several strings at once, today written
  in voices (LYS4072).
- **Another part's marks in a part sheet's exports**: a part sheet's page prints the rehearsal
  mark, tempo and rit. another part writes (2026-11-13); its LilyPond twin and MusicXML do
  not yet carry the mark or the rit.

### MIDI keyboard input (deferred by the owner 2026-10-25)

Step input from a MIDI keyboard. Deferred until there is a keyboard to try it with.

---

## Decided against

- **Editing the score with the mouse in the preview** (2026-11-03): built in one long day and all taken
  out: dragging notes (by staff position, by octaves with Ctrl, by semitones with Alt), tempos,
  dynamics, articulations, chord shapes, clefs and key signatures, some of them redrawn as the mouse
  moved, a right-click menu on a note, and a padlock in the toolbar against a slip of the hand. The
  owner's reasons: typing was faster; it was inconsistent with the way other things are edited; the
  caret on a note and `Ctrl+Shift+Up` / `Down` is handier, and is where people should be led; a key
  signature's or a dynamic's change reflows the systems under the hand; and an operation this
  unimportant should not need a lock button. The preview is for reading and playing: a click goes
  to the source. The drags are at `59e4aff37` to `db0f2d9c9` should anyone ask again.- **A range CodeLens on each part's line** (2026-10-25): the section CodeLens already stands
  between the lines, and the range is not needed while writing; the preview's Range panel
  answers it when asked.
- **An overflow (⋯) menu for the preview's header** (2026-10-25): the header's groups wrap as
  one onto the next row, and the practice controls live in their own drop-down.
