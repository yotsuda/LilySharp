# Composing in Copilot's chat — how it works and how a composed piece is reviewed

What the owner and Claude built on 2026-10-29 and 2026-10-30. A composing AI writes a new piece in
Copilot's chat through Lily#'s tools, and Lily# checks it as music. This file holds the flow, the
checks, the rules for sending a piece back, and the procedure each review session follows. The
next step and the open items are `docs/HANDOFF.md` §1.0, which points here.

## 1. The flow

1. **The guide.** The chat reads the guide with the `lilysharp_readGuide` tool (topic `composing`,
   paged). The guide is `docs/GRAMMAR_FOR_LLM.md` plus `docs/COMPOSE_FOR_LLM.md`. Both are bundled
   into the extension as `lilysharp.instructions.md` and `lilysharp-compose.instructions.md`.
2. **The draw ("dice").** Before the guide's first page, `compositionHint()`
   (`editors/vscode/src/aiTransformCore.ts`) draws a starting point:
   - a key tonic (major or minor, as the music asks);
   - a meter (a band's music keeps 4/4 whatever is drawn);
   - a form;
   - an opening.

   It is the AI's alone; the musician does not see it. Anything the request says comes first. The
   draw is made once per piece, however often the guide is read (`drawnHint`, cleared at the
   piece's first check). The key is checked once (`drawnKeyNote`). The form and the opening are
   not checked.
3. **The header.** The chat writes the header, `// Composed in Copilot's chat …`, `// Asked: …` and
   `// The AI: …`, in the request's language (`headerLanguageNote`). `proposeScore` writes the
   model's name into the first line, read from the Copilot CLI session log (`copilotModelOf`,
   `chatModelName`). In a chat that keeps no such log, the line stays as it is.
4. **`lilysharp_proposeScore`** (`aiTransform.ts`, `proposeScore`) decides what happens to each
   proposal, in this order:
   - **A compile error** goes back, every time.
   - **A bar that does not add up** goes back, every time. This covers LYS2001, 2002, 2004 and
     2006; **LYS2007**, a section longer in one part than in another; and **LYS2019**, a later voice
     of a `voice { } { }` span or `drums { }` chart running past the first. That voice lengthens
     the section, and the other parts are drawn silent there (`BAR_LENGTH_CODES`).
   - **A note its instrument cannot play** (LYS1052, `OUT_OF_RANGE_CODE`) also goes back every time.
     Told once, the AI kept a guitar's E♭2 "for its open sound". It goes back even when the score had it before the
     proposal: a revision that leaves it (fewer warnings in all) is not shown either.
   - **The first proposal of a new piece** is checked as music once (`musicCheckedFor`). The
     compiler's warnings, `CompositionChecks.Of`'s findings (through LSP `lilysharp/compositionCheck`),
     the header-language note and the drawn-key note go back together, unseen by the musician.
   - **The revision** is checked again. Findings the first check did not make go back ONCE more
     (`firstFindings`, `recheckedFor`; compared with their digits stripped).
   - **The proposal after that** is written into the file. Whatever the check still finds is listed
     in the reply, and the AI is told to tell the musician each point and why it was left.
5. **Writing.** The piece is converted to relative octaves, as a musician writes
   (`asMusicianWrites` → `OctaveModeConverter`). Unplayed phrases and sections convert too. If the
   conversion fails, the reply says the piece was left in absolute octaves.

## 2. The checks (`LilySharp.Core/Editing/CompositionChecks.cs`)

Each check is one sentence of advice: the composing AI may keep what it meant. The owner's report
that prompted each check is written beside it in the code.

**The score and its parts**
- A title of its own, not a placeholder.
- No instrument on a part.
- An ensemble braced as one grandStaff.
- Staves labelled by the hand that plays them.

**Dynamics**
- A part with no dynamics.
- A drop of two or more levels with nothing leading to it (levels inside phrases count).
- A part whose first level comes late.
- A band's drums with no level while the others move.

**The band**
- Drums, bass and chords present.
- One tempo.
- The bass leaping octaves.
- A part in the bass's register.
- Nobody playing the chords: only single notes, high (a low single-note riff counts as chords).
- One drum groove through every section (a varying kick and snare are said apart from the cymbals).
- The bass or drums at one level from start to end while the lead's level moves.

**The melody**
- One note value.
- One rhythm in most bars. A band's tune is NOT let off as "an even pace is the music's character".
- Leaps up and down its chords (fewer than a quarter of the moves are steps).
- One bar's figure moved along the chords: half the bars or more take the up-and-down shape of the
  bar before. Measured on the owner's 60 pieces: the sequenced rock guitars scored 0.53–0.61, the
  rest 0.42 or less, and Ode to Joy 0.11.
- Compound-meter beats crossed.
- A note held a semitone or a minor ninth from the harmony (a major seventh over the bass is
  colour, not a clash).
- Sections whose tune ends open against the bass (a 2nd, a tritone or a 7th; a 4th does not count).

**The piano**
- A hand far from its register.
- The hands far apart.
- The left hand reaching into the melody.
- Broken chords wider than a twelfth.

**Voicing and spelling**
- Close thirds or seconds under C3.
- Notes of the key spelled by another name (B and F♯ in E♭ minor).
- An octave mark slipped: one note an octave away and straight back.

**Form and length**
- Sections copied note for note.
- Sections played three times in an instrumental.
- A tune in too few bars of an instrumental.
- The length: the bars needed are computed at the piece's own tempo and meter.
- The key the piece ends in.
- The final bass note off the tonic.
- Every part moving in one rhythm.

Pitch names in the messages and in the range lines are spelled as the piece's key spells them
(`PartRanges.SpelledFromPitch(midi, fifths)`).

## 3. Reviewing a composed piece — the procedure

The owner composes in VS Code's Copilot chat (Copilot CLI agent host), saves the piece and sends
its path in `Downloads`. For each piece:

1. **Read the file** (Read tool).
2. **Read the chat's log.** The newest `events.jsonl` under `~\.copilot\session-state\*\` is the
   one to read. Choose it by the file's time: the newest folder may have no log yet.

   ```powershell
   $f = Get-ChildItem ~\.copilot\session-state -Directory | ForEach-Object { Get-Item (Join-Path $_.FullName events.jsonl) -ErrorAction SilentlyContinue } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
   ```

   Then extract, with `python -I -c` over that file, line by line as JSON:
   - the draw: `the key on …` in a `tool.execution_complete` result;
   - every result starting `Not shown` or `Written`;
   - the `assistant.message` contents;
   - the model: `tool.execution_start` and `session.usage_record` carry `model` and
     `modelDisplayName`.
3. **Run the checks on the file as it is now.** Load `LilySharp.Core\bin\Debug\net10.0\LilySharp.Core.dll`
   in a separate `pwsh -NoProfile` with `Add-Type` (never in the shared console — it would lock the
   DLL), and print `[LilySharp.Core.Editing.CompositionChecks]::Of($text)` and
   `[LilySharp.Core.Midi.PartRanges]::Lines($text)`.
4. **Read the pitches** with `lysc octave --absolute <file>`. Remember: inside a relative chord each
   member counts from the one before it, so `<e, gis' b'>` is E2 G♯3 B4, not a close chord. Render
   with `lysc png <file> -d <scratch>` when the question is the page.
5. **Report in Japanese.**
   - What the new checks did.
   - What the music does well.
   - What is still wrong, with bar and pitch.
   - A proposal: a guide sentence, a check, or a change to the flow.

   Implement only after the owner approves ("入れて"). Then run the C# tests and
   `npm test` in `editors/vscode`, commit (never push), and say which rebuild is needed: the server
   for the checks and the language, the extension for `aiTransform*.ts` and the guides.

## 4. Open items

- **Drop-D D2 on a guitar.** The AI writes D2 and the range warning stays. It is not decided whether
  to accept drop D (`tuning guitarDropD`) or to guide the AI away from it.
- **The draw's form and opening are not checked.** Only its key is.
- **A revision's new compiler warnings** (other than the bar-length codes) are not sent back again.
  Only composition findings are.
- **The AI sometimes reports two remaining findings in one line.** Seen once; not acted on.
- **Proposed and not built:**
  - "The chorus does not lift": the melody's height and the level no higher than the verse's.
  - Breathing for a voice or a wind instrument (eight bars or more without a rest; except where
    the request asks for circular breathing).
