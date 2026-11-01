# Reviewing a score — the review tool's design

Owner's proposal, 2026-10-30. The checks built for composing in the chat
(`docs/compose-in-chat-design.md`) serve a human composer just as well. The same checks get a
second entrance: a person asks the AI "review this piece (the one open in the editor)". The AI
calls the review tool, reads its report, and explains the piece to the person in plain words —
above all to a beginner, who may not know what a key is.

The checks are machine counts and do not know the music meant. The AI interprets them: it decides
what applies to this piece's style, says why in musical terms, and teaches the convention behind
each point. The report's design serves that interpretation. Without an AI, the same report is free
and fast (§5).

Status: §6 steps 1 and 2 are built (2026-11-02), and not yet tried in a real VS Code (`editors/vscode/MANUAL-CHECKS.md` C55). What was built differs from §2 in the ways §7 lists.

## 1. Entrances

- **The AI tool `lilysharp_reviewScore`** (`#reviewScore`), beside `proposeScore` and `readScore`.
  - Input: `file` (absolute path; default: the score open in the editor), `audience`
    (`beginner` | `intermediate` | `advanced`; the AI infers it from how the person writes, and
    may ask), and `focus` (optional: `key`, `rhythm`, `form`, `parts`, `notation`).
  - It never writes. Fixes go through `proposeScore` once the person agrees.
- **The command "Lily#: Review Score"** (§5): the findings as Information diagnostics (their own
  source, `Lily# review`) in the Problems panel, and a quick fix where one is exact.
- **`lysc review <file> [--json]`**, for many files at once and for CI.

What the composing flow uses stays as it is. The flow reads the same findings' `message`. The
review omits the composing-only checks: the drawn key and the header's language.

## 2. The report

One JSON document. The tool hands it to the AI with the reading instructions of §3 in front.

```jsonc
{
  "audience": "beginner",
  "facts": {
    "title": "…", "key": "g major", "keyHeard": "e minor (0.82) / g major (0.79)",
    "meter": "4/4", "tempo": "♩ = 96", "bars": 32, "seconds": 80,
    "form": ["Intro", "A", "B", "A"],
    "parts": [{ "name": "melody", "instrument": "flute", "range": "D4–A5", "mostly": "G4–E5" }],
    "chords": [{ "bar": 1, "symbol": "G", "roman": "I" }, { "bar": 3, "symbol": "E7", "roman": "V7/vi" }]
  },
  "strengths": [
    "Every section closes on the tonic or the dominant.",
    "The flute stays in its comfortable range."
  ],
  "findings": [
    {
      "id": "spelling.direction",
      "kind": "notation",            // mistake | notation | style | idea
      "confidence": "high",          // high | medium | low — how sure the count is
      "where": { "part": "melody", "bars": [5], "line": 12, "written": "c'4 des' d'" },
      "what": "A rising chromatic step is written with a flat: C–D♭–D.",
      "why": "A note on its way up is written as the lower note raised (C♯), one on its way down as the upper note lowered (D♭): the letter shows the direction, and the bar needs no natural. A note with a harmonic job is spelled by that job instead — the leading tone F♯ to G, the ♭7 B♭ of C7.",
      "fix": { "say": "Write C♯ (cis).", "edit": { "line": 12, "from": "des'", "to": "cis'" } },
      "fine_when": "The D♭ belongs to a chord (D♭ major, B♭ minor) rather than passing.",
      "ask": null
    }
  ]
}
```

- **`kind`** tells the AI how to speak about each finding:
  - `mistake`: almost surely unintended — a note outside the key that resolves nowhere, a key
    signature the music does not fit, an octave slip.
  - `notation`: the music is right but written hard to read — the spelling against the key or
    the direction, a hand's staff label.
  - `style`: a choice to weigh — one groove throughout, one bar's figure repeated.
  - `idea`: a suggestion beyond a fault — a section that could rise, an answer to a phrase.
- **`confidence`** lowers the tone of a weak count. A low-confidence `mistake` is put as a
  question (`ask`).
- **`why`** is the teaching: the convention in one or two sentences, written once per finding id
  in a library (`ReviewNotes`), so every report says it alike. The AI expands it for a beginner and
  shortens it for an advanced reader.
- **`fix`**: `say` is the advice in words. `edit` is present only when the replacement is exact
  (a misspelled note, a missing level), so the editor's quick fix and the AI's `proposeScore`
  apply the same change.
- **`fine_when`** names the cases where the finding does not apply. The AI checks the piece
  against it before raising the point. This is how a sequence in a baroque episode, a chorale's
  voices moving together, or a borrowed chord stays unscolded.
- **`ask`** is a question to put to the person when intent decides, for example "Is the E7 in bar
  12 meant to lead to A minor?".
- **`strengths`** says what the piece does well. A beginner is told what to keep, not only what to
  change.
- **`facts`** gives the AI what it needs to reason about the piece as a whole: the key heard against
  the key written, the chords with their Roman numerals, the parts' ranges, the form.

## 3. The reading instructions (put before the report)

- The person may be a beginner. Speak in plain words and explain each musical term the first time
  it is used.
- Start with what works (`strengths`). Then raise at most five points, mistakes first, grouped by
  theme (the key, the rhythm, the parts, the notation). Offer the rest if the person wants them.
- For each point: where it is (part and bar), what you see, why it matters (`why`, in your own
  words), and what to do (`fix`). Check `fine_when` against the piece and drop a point that does
  not apply. Put `ask` as a question when the intent is not clear.
- These are counts, not judgements. The style of the piece decides: jazz uses notes outside the
  key, a chorale moves its voices together, a minimalist piece repeats its figure.
- Do not change the score. Offer to make the fix, and use `lilysharp_proposeScore` once the person
  agrees.

## 4. The checks the review adds (owner's points, 2026-10-30)

Beyond the composing checks (`compose-in-chat-design.md` §2):

- **`key.signature`** (mistake): the key the notes are heard in, from a pitch-class profile
  (Krumhansl–Schmuckler), against the written `key`. Typical case: "the music is in D major and the
  key signature is C major". This is the beginner's commonest slip — a forgotten key signature,
  every F♯ and C♯ written as an accidental.
- **`key.outside`** (mistake, or a question): notes outside the key that do not resolve by a
  semitone to a note of the key. Leading tones, passing and neighbour notes are left out.
- **`spelling.key`** (notation): a note of the key written under another name. This exists today
  as `SpelledAgainstTheKey`.
- **`spelling.direction`** (notation): a chromatic PASSING note — a semitone on each side, moving
  on in one direction — spelled against its direction (rising C–D♭–D). Notes with a harmonic job
  are left out: chord tones, a leading tone, a ♭7. The convention (Gould, *Behind Bars*): a note
  going up is the lower note raised, a note going down is the upper note lowered, unless its
  harmony says otherwise.
- **`chord.outside`**: chords outside the scale.
  - The `facts` name each one's role, so the AI can explain it: a secondary dominant (V/V, V/vi),
    a chord borrowed from the parallel key (iv, ♭VI, ♭VII, ♭III in major), the Neapolitan (♭II),
    the major V and the vii° of a minor key.
  - A finding is made only for a chord that fits no role, or one whose role is not fulfilled ("E7
    expects A minor and goes to F" — put as `ask`; it may be a deceptive cadence).
  - The first stage reads chord symbols (`chords { }` rows, `@chord(…)`). Chords inferred from the
    notes wait for the harmony analysis (`docs/ROADMAP.md`).

## 5. Without an AI

- The command lists the findings in the Problems panel as `[kind] what`. Hovering shows `why`
  and `fine_when`; a quick fix applies `edit`.
- **Silencing a point** the composer means: `// review: off melody.shape` at the top of the file
  (or in a section). Both the command and the AI honour it. Build it when it is first asked for.

## 6. Order of work

1. **Structured findings in the core.** Built, with the differences in §7.
   - Each check returns `id`, `kind`, `confidence`, part, bars (as played and as written),
     source span and written text, `what`, `fix` (and `edit` where exact).
   - `ReviewNotes` holds `why` and `fine_when` per id.
   - The composing flow keeps reading the one-sentence `message`; its tests stay.
2. **`lilysharp_reviewScore`** with `facts`, `strengths` and the reading instructions. This brings
   the most value. Built, with the differences in §7.
3. **The key checks of §4:** `key.signature`, `key.outside`, `spelling.direction`; then
   `chord.outside` from chord symbols.
4. **The command and `lysc review`.**
5. **Silencing**, when first asked for.

## 7. What was built, against §2

`CompositionChecks.Findings` returns `CompositionFinding` (`id`, `message`, `Parts`, `Bars`).
`CompositionChecks.Of` is its messages, so the composing flow is unchanged. The gate of the change:
`Of` byte-identical over 1,215 books before and after. `ReviewNotes` holds `kind`, `confidence`, `why`
and `fine_when` per id. `ScoreReview.Json` writes the report, and the language server answers it as
`lilysharp/reviewScore`.

Differences from §2, each still to do unless it says otherwise:

- **`what` and `fix.say` are one sentence.** `what` is the check's `message`, finding and advice
  together, and there is no `fix`. Splitting them means rewording every check. The reading
  instructions tell the AI where the advice is.
- **No `fix.edit`, no `ask`, and no `where.line` or `where.written`.** Each needs a source span from
  the check, and most checks read the sounding notes, not the text. `spelling.*` can have them first.
- **`where.bars` is what the check can show, not always all it counted.** It is empty for a check
  that names no bars, as `rhythm.one-value`.
- **`confidence` is per check**, not per finding.
- **`facts` has no `keyHeard` and no `chords`.** They come with step 3.
- **Added, and meant to stay: `compiles`, `errors` and `warnings`.** A score that does not compile
  has to be reported as that, not as a piece with no faults, and the compiler's warnings (a note out
  of range, a bar too long) are the first things a beginner needs to hear of.
