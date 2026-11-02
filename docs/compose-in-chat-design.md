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
   - **A syllable with no note** (LYS4001, `LOST_SYLLABLE_CODE`) goes back every time (2026-11-01:
     a chorus's last word was left off the page, the warning told once and the piece written).
   - **A tune held against its chords 8 times or more** (the music check's clash finding, read by
     `heldClashesOf`, `CLASHES_TURNED_DOWN`) goes back every time once the piece is past its first
     check (2026-11-01: 12 held semitone clashes in a dawn ballad were written in "as colour"). Fewer
     keep the blue-note escape.
   - **Chords no hand can hold, 8 or more** (LYS1055 "cannot be fingered", `unfingerableOf`,
     `UNFINGERABLE_TURNED_DOWN`) go back every time once a new piece is past its first check (2026-11-01:
     a solo guitar's 14, written in as "hard to hold"). A change the musician asks for in the chat is
     not held to it, and a bowed string's "cannot be bowed" is not counted.
   - **The first proposal of a new piece** is checked as music once (`musicCheckedFor`). The
     compiler's warnings, `CompositionChecks.Of`'s findings (through LSP `lilysharp/compositionCheck`),
     the header-language note and the drawn-key note go back together, unseen by the musician.
   - **The revision** is checked again. Findings the first check did not make go back ONCE more
     (`firstFindings`, `recheckedFor`; compared with their digits stripped).
   - **The proposal after that** goes back once, and only, for a finding the review calls a `mistake`
     (`ReviewNotes`, `Mistakes` in the response) that no earlier check made (`mistakeRecheckedFor`; owner's
     decision 2026-11-02: a carol's theme kept to its 12/8 beats, was told "70% eighths", and came back
     across them in 17 of 35 bars, too late). The AI is told to mend that and leave the rest.
   - **Otherwise, and the proposal after that,** is written into the file. Whatever the check still finds is listed
     in the reply, and the AI is told to tell the musician each point and why it was left.
   - **A proposal while the composing goes on** — the chat revising its own piece, unasked, after it
     was written — is written into the file again in its place, never put on the panel
     (`newPieces`, `composingGoesOn`; owner's request 2026-11-01). The composing goes on while the
     document's text is still what `proposeScore` wrote and the musician has said nothing in the
     chat since: the newest `user.message` in the Copilot CLI session log (`lastUserMessageAt`) is
     older than the write. A chat that keeps no log goes by the text alone. A change the musician
     asks for in the chat (a new `user.message`), or a piece they edited, is a proposal on the panel.
   What the check still finds at the write is also put in the extension's log, and a notification
   gives its count (2026-11-01: a chat left the points out of its last message).
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
- A chord instrument's staff restating the chords row: block chords of the row's chord in four
  fifths of its bars, eight or more (2026-11-01: a song to sing and play yourself, strums under
  the row). The guide's rule for such a song: alto for a woman's or an unnamed voice, baritone
  (G2–G4, a preset of its own since that day) for a man's, a lead sheet, the diagrams always,
  and the strum as a `strokes` part the row plays (`chords harmony plays rhythm` — how the row
  sounds; `strokes rhythm` in the score prints it; the grammar of that day. A strokes part is no
  part to these checks — it needs no instrument).

**Dynamics**
- A part with no dynamics.
- A drop of two or more levels with nothing leading to it (levels inside phrases count).
- A part whose first level comes late.
- A band's drums with no level while the others move.
- Levels set, not shaped: no hairpin anywhere and three sections or more opening `.subito`
  (2026-11-01: the drop finding's "or write .subito" was written into every section).

**The band**
- Drums, bass and chords present.
- One tempo.
- The bass leaping octaves.
- A part in the bass's register.
- Nobody playing the chords: only single notes, high (a low single-note riff counts as chords).
- A tune in too few bars: a bar is the tune's with three onsets or more, most single notes, or a
  chord struck where the tune rests (chords no more than half the single notes); a tune in fewer
  than a third of the bars is said (2026-11-01: half until then — the riff under the verses and
  the tune in the choruses, the shape the finding asks for, has about half).
- A fretted part striking its chords (two strikes or more in the bar) in one voice under its own
  tune in another, in half its tune's bars: one player cannot (2026-11-01, three trios in a row).
- One drum groove through every section (a varying kick and snare are said apart from the cymbals);
  and the kick and snare on one beat in every section but one, of six or more (2026-11-01).
- The bass or drums at one level from start to end while the lead's level moves.

**The singers** (2026-11-01: 風のひかり, a school chorus; the voice presets alone)
- Two singers in octaves eight times or more, 10% of the moves they make together (`SingersLines`).
  From a fifth of those moves up to four fifths (a piece in unison may be meant) `proposeScore`
  turns the piece down every time (`singersInOneLineOf`; 2026-11-01: 夢の先へ kept 30 in 131).
- A lower singer over the part above it, eight times or more and 10% of the moments they sing
  together (the stack is the score's order); and any on the last chord.
- An inner singer with fewer than a quarter of its moves steps, 16 moves or more.
- A piano under them holding three quarters of its bars, eight or more: chords alone in the right
  hand, a bass note or two in the left (`PianoHolds`).
- A sung piece's voices singing two bars or more in sections no lyrics track writes words for
  (`BarsWithoutWords`; read from the sections, the MIDI's lyric events carrying no `verse N { }`).

**The accompaniments** (2026-11-01; any piece, not a band's alone)
- A part that is not the melody playing one figure in every section (or in all but one of six or
  more): each section's commonest bar — the onsets' places and each note's interval from the bar's
  first, so the figure moved along the chords is one figure — the same, where that bar owns half
  the section. The guide's own rule: a contrasting section takes a figure of its own.

**The harmony** (2026-11-01: a rock instrumental ran Cm A♭ E♭ B♭ from its intro to its outro)
- One chord progression under every section (or all but one of six or more): the lowest note
  sounding at each bar's start, the loop the longest section's shortest period of two chords or
  more, a section on it when its bars walk the loop from any of its chords. Escape: a blues, a vamp.

**The melody** — read from the part's line voice (of a `voice { } { }` part, the voice with the most
single notes; `MidiNote.Voice`), and in a band's instrumental only in the bars that voice plays a
line (2026-11-01: a chorus hook over the guitar's own power chords read as "no tune"; its riff bars
as the melody's rhythm).
- One note value.
- One rhythm in most bars. A band's tune is NOT let off as "an even pace is the music's character".
  And, where the piece's count passes, one rhythm in three quarters of a SECTION's bars (four bars
  or more), the sections named (2026-11-01: a verse of eight bars in one rhythm under a varied solo).
- One rhythm in three quarters of its PHRASES (two bars, else four, counted from each section's
  first bar; six phrases or more) — the bar-by-bar readings miss a two-bar formula, which splits
  into two bar rhythms of half the bars each (2026-11-01: 夜明けのうた, `8 8 4 2 | 4 2 r4` in 10 of 12).
- Where the phrases are not one formula: three quarters of the bars of its four-bar phrases (six
  phrases or more) in the commonest rhythm for their place in the phrase (`PhrasePositions`;
  2026-11-01: 風の道, 21 of 24; 7 of the owner's 144 books, mostly composed by AI).
- Where the phrases are not one formula: three quarters of the two-bar phrases ending the same way
  — the last two notes at the same places from the phrase's end, the last the same length
  (`PhraseEndings`; 2026-11-01: every phrase `… 4 r8`).
- **A song's tune** (a part a lyrics track sings) only: the chorus — a section named
  chorus/sabi/refrain/hook, else the one the form plays most, of ties the last — opening with another section's first
  phrase note for note; the tune's highest note spent in its first phrase, or the chorus staying
  below the sections before it, level with them being fine (`ChorusOf`, `ChorusOpensLikeAnother`, `PeakSpentEarly`);
  and more than half of the sections (four or more) closing on one figure — the first four single
  notes of each section's last bar, as intervals (`OneClosingFigure`; 2026-11-01: `cis8 b'4 ais8
  gis8` closing a verse, a chorus and a second verse); and the chorus sung three times or more,
  the last as the first (`LastChorusUnchanged`); its first bar the same line as another section's
  first bar, an octave apart or none (in `ChorusOpensLikeAnother`); seven eighths of its four-bar
  phrases' bars with the commonest number of notes for their place, the numbers not all one
  (`PhraseCounts`; 2026-11-01: あしたの窓, 3, 3, 5, 2 in 31 of 32 bars; 4 of 145 books).
- Leaps up and down its chords (fewer than a quarter of the moves are steps).
- One bar's figure moved along the chords: half the bars or more take the up-and-down shape of the
  bar before. Measured on the owner's 60 pieces: the sequenced rock guitars scored 0.53–0.61, the
  rest 0.42 or less, and Ode to Joy 0.11.
- Compound-meter beats crossed.
- A part in voices whose tune (its highest voice of single notes) has another voice's chord reaching
  over it in half its bars, eight or more (`TuneUnderItsChords`; 2026-11-01: a solo guitar's bossa nova).
  A voice below another voice's chords is their bass, never the tune, here and for every reading of a
  part's line (`VoiceIsUnderChords`; 2026-11-01: a guitar's thumb, told "98% half notes").
- A singer with no rest in 16 bars or more in a row (a voice preset, or a part a lyrics track sings;
  2026-11-01: a song sang its 48 bars from the first note to the last without one). A bar is sung
  through when the part's notes cover it with no gap.
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
- Four notes or more in half the chords (eight or more) of a part whose `midiInstrument` distorts
  (2026-11-01: an overdriven guitar in `<e, e b e' b'>`, the voicing tool's first E5 shape, from start
  to end). The tool `lilysharp_chordVoicings` takes the part's sound as `sound` and, when it
  distorts, answers the power chords — the root and its fifth, the octave above — before the full
  shapes; the chat's instructions tell it to pass the sound.
- Notes of the key spelled by another name (B and F♯ in E♭ minor).
- An octave mark slipped: one note an octave away and straight back — each voice of a part read
  as its own line (`MidiNote.Voice`; 2026-11-01: a tune over its own part's chords was told of slips).

**Form and length**
- Sections copied note for note.
- Sections played three times in an instrumental. Beyond a band: two parts or more, no words, and over half
  the bars played being repeats (`PlayedRound`; 2026-11-02: a carol went round its 24 bars four times). A rondo
  stays under the half, and a part alone is left out (a song's bass written down goes round as the song does).
- A tune in too few bars of an instrumental.
- The length: the bars needed are computed at the piece's own tempo and meter. A one-part piece is
  let off the minutes as a head played round — unless it is sung: a song's words run out
  (2026-11-01: a song to sing and play yourself of 24 bars, 69 seconds). The finding says a
  section that returns is named again in the form — that counts — and a copy under a new name with
  a few bars altered is not length (2026-11-01: told it was short, a piece wrote its verse,
  pre-chorus and chorus again as Verse2, PreChorus2, Chorus2). ⚠️ A near-copy check was tried and
  dropped the same day: three quarters of the bars alike named a second verse whose tune was new over
  the same bass and drums, the ordinary shape of a song; the 2026-10-30 rule stands — a tune changed
  in three bars of eight is music of its own.
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
