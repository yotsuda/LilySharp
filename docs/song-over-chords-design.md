# Write a Song over These Chords…

The second step of the way from a song to a dance (Shosa's `docs/ROADMAP.md` §2 and its `docs/HANDOFF.md`,
decided with the owner 2026-11-26): **song → singing → dance → video**. Step 1 (Shosa's Add a Dance…)
puts a dance on a `.lys`. A `.lys` read from a recording (Orecchiante) has chords and a bass but no voice,
so the dance has no words to follow. This step gives such a score a song.

## The owner's decisions (2026-11-26)

- The song goes into **the same `.lys`**, as a new part. It is not a new file.
- The accompaniment is **the original recording** for now: the recording plays under the song, so the
  score's tempo, bars and key stay as they are. An accompaniment made from the chords is step 4
  (Arrange an Accompaniment…).

## What the musician does

Right-click a `.lys` (Explorer, the editor tab, the editor), or run **Lily#: Write a Song over These Chords…**
from the palette with a `.lys` open. One question: what song to write (its language, what it is about,
the voice). Left empty, the AI chooses. Copilot's chat (agent mode) is opened with the request. The musician
picks the model there, as for Compose with AI….

## What the chat is told (`songOverChordsCore.ts`)

- `songRequest`: the file's absolute path; the part's name (`vocal`, or `vocal2`… when the score uses it)
  and its words' name (`words`, or `songWords`…); read the guide `lilysharp_readGuide`, topic `song`; the
  request. The request says to add to THIS file. It never asks for a new piece.
- The guide, topic `song` (`songGuide`): the steps (`SONG_STEPS`), then the composing guide's item
  *A song with words* copied from `docs/COMPOSE_FOR_LLM.md` when the guide is read. The text is kept in
  one place. It does not include the composing guide's opening (how a NEW piece begins), because that
  opening tells the chat to write a new file.
- The steps: read the score and its chords; plan which sections are sung (sections named A, A2… are told
  apart by their chords and where the form plays them); pick the voice's range in the score's key; write
  the words first, then the tune; write it all in **one** `lilysharp_writePart` call. The panel beside the
  score shows the result, and the musician accepts it or not.

## What `lilysharp_writePart` gained (`PartWriter`)

The three things this step needed did not exist before:

| | Before | Now |
|---|---|---|
| A new part's words in the same call | refused ("a part with no music in A yet") | written after its block, `lyrics WORDS { … }` |
| Where a voice stands | after another part, or last | `first: true`: before every part, in the file, in each section and in each score (a chords row above it stays above) |
| Who sings | could not be written | `singer: "female"` (or `male`, a quoted voice, or empty) → `singer female sings on vocal` after the part; with a singer, each score that gets the staff gets `lyrics WORDS` under it |

The score rows for words are written only when the part has a singer: the harmony writer
(`HarmonyPartWriter`) places a written-out singer's rows itself.

## Not done yet

- The owner's check in a real VS Code (`editors/vscode/MANUAL-CHECKS.md`, "Write a Song over These Chords").
- Step 3 (Lily#): the walkthrough, and showing the next step (dance it with Shosa): done 2026-11-26,
  `docs/song-to-dance-design.md`.
- Step 4 (Lily#): Arrange an Accompaniment…: done 2026-11-26, `docs/accompaniment-design.md`.
