# Lily# roadmap

Features proposed and not yet built, with what was decided about each. A feature leaves this
file when it ships (its CHANGELOG entry says the rest) or when it is decided against (it moves
to *Decided against* with the reason). What is being worked on now, and the next step, is
`docs/HANDOFF.md` §1 — not here.

---

## Proposed

### A figure at the middle of a bass note (proposed 2026-11-13, grammar evaluation 3)

`a2@figuredBass(4)@figuredBass(3)` — a change of figure over a held bass, the second at the
middle of the note, as LilyPond's `<4>4 <3>4` against `a2`: on the page, in MusicXML
(`<figured-bass>` with a `<duration>`), in the LilyPond twin and read back. Today a second group on
one note is LYS1067, whose message says to write two tied notes with a figure each.

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
