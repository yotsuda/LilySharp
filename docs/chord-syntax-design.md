# Chord syntax — names, diagrams and chords that sound

The design the owner approved on 2026-10-31, worked out from what each kind of user writes. It
replaces a set of rules that grew one feature at a time: `@chord` with or without a shape,
`@diagram`, the shape-chord item and its word. The implementation plan is §6.

## 1. Who writes chords, and what for

| User | Wants |
|---|---|
| ① A lead-sheet writer | Chord names over a melody, diagrams under them for a guitar book. One source prints a guitar book, a ukulele book and a piano score with no diagrams |
| ② A guitarist writing out an accompaniment | Strummed chords as notes, with a tab. A diagram where the chord changes, not over every strum |
| ③ A beginner | Plays a shape found by ear and wants to know its name |
| ④ A teacher writing a method book | A diagram with no name, anywhere, as an illustration |
| ⑤ A pianist or arranger | Chords as notes (`<c e g>`), named from the notes |
| ⑥ The composing AI | Few spellings, none of them ambiguous |

## 2. The two rules

1. **The word says what is seen.** `chord` shows a NAME, and the diagram of a shape written
   with it. `diagram` shows a DIAGRAM only, never a name.
2. **With `@` it marks the note it is on. Without `@` it is an item that sounds.**

What a book shows beyond that is the layout's, per score (§4).

## 3. The spellings

| Written | Name | Diagram | Sounds |
|---|---|---|---|
| `c1@chord(F)` | ✓ | | |
| `c1@chord(F 133211)` | ✓ | ✓ | |
| `c1@chord(x32010)` | ✓ (read off the shape: C) | ✓ | |
| `<f a c>1@chord` | ✓ (read off the notes) | | |
| `c1@diagram(133211)` | | ✓ | |
| `<f a c>1@diagram` | | ✓ (the notes, fretted) | |
| `chord(F)4` | ✓ | | ✓ F A C, close (Q1) |
| `chord(F 133211)4` | ✓ | ✓ | ✓ the shape's strings |
| `diagram(133211)4` | | ✓ | ✓ the shape's strings |
| `diagram(F 133211)4` | | ✓ | ✓ — F is data: the shape is checked against it and Ctrl+Shift+Up/Down walks F's shapes |

Every combination is ONE word. Nothing needs two marks.

- A `chords` row is unchanged: `F` is a name, `F(133211)` a name over its diagram — the rule 1
  of a row.
- `@diagram(133211)@chord` asks in two words what `@chord(NAME 133211)` says in one: it warns
  (LYS1056) and names that spelling.
- What one word cannot say is "this shape, its name, and no diagram". The layout says it
  instead: a diagram stands where the chord changes (Q2), and `chordDiagrams none` draws none.

### Per user

- ① `c1@chord(F)` or a `chords` row; `layout { chordDiagrams guitar all }` gives every name its
  usual diagram, the layout's shape table a chosen one, `@chord(F 133211)` one in one place.
- ② `chord(F 133211)4` strum after strum: the name and the diagram where the chord changes.
- ③ `diagram(x32010)4` plays it; the hover says *this shape is **C***; the quick fix writes
  `chord(C x32010)4` — name, diagram and sound.
- ④ `@diagram(x24442)`.
- ⑤ `<f a c>4@chord`.
- ⑥ "`chord` shows the name, `diagram` the picture, no `@` sounds" — one sentence in
  `GRAMMAR_FOR_LLM.md`.

## 4. What the layout decides (per score, unchanged unless said)

- `chordDiagrams none | guitar | ukulele | …` — the instrument, or no diagrams at all.
- `chordDiagrams … all` — every name draws its usual diagram; a shape table picks others.
- `chordNames shape | sounding | both`, the capo, `chordList` — as now.
- New (Q2): an item's diagram stands where the chord CHANGES — not over a repeat of the same
  shape in the same part — and again at the head of each system.

## 5. Decisions

| | Question | Decided |
|---|---|---|
| Q1 | Does `chord(F)4`, with no shape, sound? | Yes: the chord's notes as the completion writes them (`<f a c>`), close and root up, the root written from G3 to F♯4 — by middle C as the staff draws it (an octave lower on a `treble_8` / `bass_8` staff: a guitar's F is F3 A3 C4) — with no string numbers, on every part. *Revised the same day:* a fretted part first played its instrument's usual shape, six strings under a name that showed no diagram; what is seen and what sounds now agree. A shape written badly, or only for another instrument, does not fall back: LYS1040 says so. This reverses "the shape is required (for now)" of 2026-09-28 for `chord(…)`. `diagram(F)` with no shape, on a fretted part, plays and draws its instrument's usual shape (it shows a diagram, so it plays one); elsewhere it voices the chord close |
| Q2 | Does an item draw its diagram every time? | No: where the chord changes, and at each system's head |
| — | `@chord(F 133211)` | Stays: a name over its diagram (the proposal to forbid it is withdrawn) |
| — | `@chord(x32010)` | Stays: the name read off the shape, over the diagram. LYS1056 no longer warns it |
| — | Bowed strings | No diagrams of their own: a violin, viola or cello part's diagrams are the guitar's, like a piano's |

## 6. Implementation plan

Each stage is committed alone, with its tests, and leaves the build and the site green.
All five shipped on 2026-10-31 (stage 4 with a second part: an item's repeated NAME drops like
its diagram, and a repeat is not priced in its bar — the owner's decision after seeing a strummed
line spread to two bars).

1. **`chord(…)` comes back as an item.** The parser reads `chord(` as the shape-chord item again,
   with its word kept on the node; `chord` is reserved in music again (a phrase cannot be
   named it). The item draws its name — as a bare `@chord` on a `diagram(…)` item does today —
   and its diagram when a shape is written. Twin, MusicXML (`<harmony>` with `<frame>`), hover,
   completion inside `chord(`, the step keys: as the `diagram(…)` item.
2. **A shape may be left out (Q1).** `chord(F)` / `diagram(F)` sound the usual shape of a fretted
   part, the chord's notes elsewhere. LYS1040 then warns only a symbol with no usual shape on
   the instrument.
3. **LYS1056 follows the rules.** `@chord(x32010)` no longer warns; `@diagram(…)@chord` still does,
   naming `@chord(NAME shape)`. The hover's quick fix on `diagram(x32010)` also offers
   `chord(C x32010)`.
4. **A diagram where the chord changes (Q2).** The page drops an item's diagram that repeats the
   previous one in its part, and keeps the first of each system — known only after the line
   breaks, so the drop is made where the systems are laid out. ⚠️ The twin writes diagrams as
   markups, which LilyPond does not drop at a repeat: the twin must make the same choice for the
   breaks it writes, or the two pages differ.
5. **Words.** `SYNTAX_REFERENCE.md`, `GRAMMAR.md`, `GRAMMAR_FOR_LLM.md`, the chords and chord-input
   pages, the examples, the CHANGELOGs.

Risk: stage 4 is the only one that touches layout; stages 1–3 are the tree, the collector and the
editor, where the existing shape-chord tests hold the ground.
